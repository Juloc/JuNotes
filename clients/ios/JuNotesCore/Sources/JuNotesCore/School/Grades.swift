import Foundation

public enum GradeScale: String, Sendable, Codable, Equatable, CaseIterable {
    /// 1 (best) to 6 (worst), entered in tenths or finer; `GradingSettings.decimals` says how fine.
    case german
    /// 1 to 6 with a tendency, as on a report: 2+, 2, 2−. A tendency counts as 0,3.
    case tendency
    /// 0 to 15 points, as in the upper school; more is better.
    case points

    public var range: ClosedRange<Double> { self == .points ? 0...15 : 1...6 }
    public var higherIsBetter: Bool { self == .points }

    /// How a value reads on this scale: "2,3", "2−" or "12".
    public func display(_ value: Double?, decimals: Int = 1) -> String {
        guard let value else { return "–" }
        switch self {
        case .german: return GradeCalculator.format(value, decimals: decimals)
        case .points: return GradeCalculator.format(value, decimals: 0)
        case .tendency:
            let whole = value.rounded()
            let offset = ((value - whole) * 10).rounded() / 10
            let digit = String(Int(max(1, min(6, whole))))
            if offset <= -0.2 { return digit + "+" }
            if offset >= 0.2 { return digit + "−" }
            return digit
        }
    }

    /// The values to choose from when she types a grade: tendencies and points are fixed steps, German grades follow `decimals`.
    public func choices(decimals: Int = 1) -> [Double] {
        switch self {
        case .points: return (0...15).map(Double.init)
        case .tendency: return [1.0, 1.3, 1.7, 2.0, 2.3, 2.7, 3.0, 3.3, 3.7, 4.0, 4.3, 4.7, 5.0, 5.3, 5.7, 6.0]
        case .german:
            let step = decimals >= 2 ? 0.01 : 0.1
            let count = Int(((6 - 1) / step).rounded())
            return (0...count).map { ((1 + Double($0) * step) * 100).rounded() / 100 }
        }
    }

    /// Snaps a typed value to the scale and its range; nil when it cannot be a grade.
    public func normalized(_ value: Double, decimals: Int = 1) -> Double? {
        guard value.isFinite, range.contains(value) else { return nil }
        switch self {
        case .points: return value.rounded()
        case .tendency: return choices().min { abs($0 - value) < abs($1 - value) }
        case .german:
            let factor = decimals >= 2 ? 100.0 : 10.0
            return (value * factor).rounded() / factor
        }
    }
}

public enum GradeCategory: String, Sendable, Codable, Equatable, CaseIterable {
    /// Klassenarbeit, Klausur.
    case written
    /// Mündliche Note, Mitarbeit.
    case oral
    /// Kurztest, Vokabeltest.
    case test
    /// Referat, Präsentation, Praxisnote.
    case other

    /// Written work and tests make up the written part of a subject's grade; oral grades and presentations the oral part.
    public var group: GradeGroup {
        switch self {
        case .written, .test: .written
        case .oral, .other: .oral
        }
    }

    /// How much one grade of this kind counts inside its part unless she says otherwise: a Klassenarbeit twice, the rest once.
    public var defaultWeight: Double { self == .written ? 2 : 1 }
}

public enum GradeGroup: String, Sendable, Codable, Equatable {
    case written, oral
}

/// How one subject is graded. Schools differ, so everything here is hers to set.
public struct GradingSettings: Sendable, Codable, Equatable {
    public var scale: GradeScale
    /// 1 for tenths, 2 for hundredths, when the scale is German.
    public var decimals: Int
    /// The share of the oral part in percent; the written part takes the rest, so 30 means oral 30, written 70.
    public var oralPercent: Int
    /// A main subject ("maßgebendes Fach" or Hauptfach); main subjects get an average of their own.
    public var isMajor: Bool
    /// How much the subject counts in the overall average.
    public var factor: Double
    /// Places after the comma for averages.
    public var averageDecimals: Int

    public var writtenPercent: Int { 100 - oralPercent }

    public init(scale: GradeScale = .german, decimals: Int = 1, oralPercent: Int = 50, isMajor: Bool = false, factor: Double = 1, averageDecimals: Int = 2) {
        self.scale = scale
        self.decimals = min(2, max(1, decimals))
        self.oralPercent = min(100, max(0, oralPercent))
        self.isMajor = isMajor
        self.factor = max(0, factor)
        self.averageDecimals = min(3, max(0, averageDecimals))
    }

    enum CodingKeys: String, CodingKey { case scale, decimals, oralPercent, isMajor, factor, averageDecimals }

    public init(from decoder: any Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            scale: (try? values.decodeIfPresent(GradeScale.self, forKey: .scale)) ?? .german,
            decimals: try values.decodeIfPresent(Int.self, forKey: .decimals) ?? 1,
            oralPercent: try values.decodeIfPresent(Int.self, forKey: .oralPercent) ?? 50,
            isMajor: try values.decodeIfPresent(Bool.self, forKey: .isMajor) ?? false,
            factor: try values.decodeIfPresent(Double.self, forKey: .factor) ?? 1,
            averageDecimals: try values.decodeIfPresent(Int.self, forKey: .averageDecimals) ?? 2
        )
    }
}

public struct Grade: SyncEntity {
    public static let kind = EntityKind.grade

    public var id: UUID
    public var subjectID: UUID
    public var value: Double
    public var category: GradeCategory
    /// Overrides the weight of the category for this one grade, e.g. a Klassenarbeit that counts three times.
    public var weight: Double?
    public var date: Date
    public var title: String
    public var parentID: UUID? { subjectID }

    public init(id: UUID = UUID(), subjectID: UUID, value: Double, category: GradeCategory = .written, weight: Double? = nil, date: Date = Date(), title: String = "") {
        self.id = id
        self.subjectID = subjectID
        self.value = value
        self.category = category
        self.weight = weight
        self.date = date
        self.title = title
    }

    enum CodingKeys: String, CodingKey { case value, category, weight, date, title }

    public init(from decoder: any Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        id = UUID()
        subjectID = UUID()
        value = try values.decode(Double.self, forKey: .value)
        category = try values.decodeIfPresent(GradeCategory.self, forKey: .category) ?? .other
        weight = try values.decodeIfPresent(Double.self, forKey: .weight)
        date = try values.decode(Date.self, forKey: .date)
        title = try values.decodeIfPresent(String.self, forKey: .title) ?? ""
    }

    public static func decode(_ record: SyncRecord) throws -> Grade {
        guard record.kind == kind, !record.deleted, let payload = record.payload, let parent = record.parentID else { throw EntityCoding.Failure.notDecodable(record.kind) }
        var grade: Grade = try EntityCoding.decode(payload)
        grade.id = record.id
        grade.subjectID = parent
        return grade
    }
}

public enum GradeCalculator {
    public static func weight(of grade: Grade) -> Double { grade.weight ?? grade.category.defaultWeight }

    /// The mean of one part (written or oral), each grade counting by its weight; nil when the part has no grade that counts.
    private static func mean(_ grades: [Grade]) -> Double? {
        let total = grades.reduce(0.0) { $0 + max(0, weight(of: $1)) }
        guard total > 0 else { return nil }
        return grades.reduce(0.0) { $0 + $1.value * max(0, weight(of: $1)) } / total
    }

    /// The grade of a subject: the written part and the oral part each averaged, then mixed by the shares she set.
    /// A part without grades is left out, so the other part stands alone until there is something to mix.
    public static func average(_ grades: [Grade], settings: GradingSettings = GradingSettings()) -> Double? {
        let written = mean(grades.filter { $0.category.group == .written })
        let oral = mean(grades.filter { $0.category.group == .oral })
        switch (written, oral) {
        case (nil, nil): return nil
        case (let value?, nil), (nil, let value?): return value
        case (let w?, let o?):
            let writtenShare = Double(settings.writtenPercent)
            let oralShare = Double(settings.oralPercent)
            let share = writtenShare + oralShare
            guard share > 0 else { return (w + o) / 2 }
            return (w * writtenShare + o * oralShare) / share
        }
    }

    public typealias SubjectResult = (grades: [Grade], settings: GradingSettings)

    /// The average over subjects, each counting by its factor. Subjects graded in points are left out, since they cannot be mixed with 1 to 6.
    /// `majorOnly` restricts it to main subjects (true) or to the others (false).
    public static func overall(_ subjects: [SubjectResult], majorOnly: Bool? = nil) -> Double? {
        var total = 0.0
        var sum = 0.0
        for subject in subjects where subject.settings.scale != .points {
            if let majorOnly, subject.settings.isMajor != majorOnly { continue }
            guard let value = average(subject.grades, settings: subject.settings), subject.settings.factor > 0 else { continue }
            total += subject.settings.factor
            sum += value * subject.settings.factor
        }
        return total > 0 ? sum / total : nil
    }

    /// "2,3" with a decimal comma, "–" for no value. Rounded to `decimals` places the usual way.
    public static func format(_ value: Double?, decimals: Int = 1) -> String {
        guard let value else { return "–" }
        let factor = pow(10.0, Double(decimals))
        let rounded = (value * factor).rounded(.toNearestOrAwayFromZero) / factor
        return String(format: "%.\(decimals)f", rounded).replacingOccurrences(of: ".", with: ",")
    }

    public enum Need: Sendable, Equatable {
        /// This grade is needed in the next piece of work to reach the target.
        case achievable(Double)
        /// The target is reached whatever the next grade is.
        case alreadySafe
        /// Even the best possible grade does not reach it.
        case impossible
        case noData
    }

    /// What she needs in the next piece of work (of `category`, counting `weight` or its usual weight) to end at `target`.
    public static func needed(forTarget target: Double, grades: [Grade], next category: GradeCategory, weight nextWeight: Double? = nil, settings: GradingSettings = GradingSettings()) -> Need {
        guard !grades.isEmpty, (nextWeight ?? category.defaultWeight) > 0 else { return .noData }
        // With the next grade as x the average is a straight line in x, so two points give the answer.
        func outcome(_ x: Double) -> Double? {
            average(grades + [Grade(subjectID: UUID(), value: x, category: category, weight: nextWeight)], settings: settings)
        }
        guard let atOne = outcome(1), let atTwo = outcome(2) else { return .noData }
        let slope = atTwo - atOne
        let scale = settings.scale
        let better: (Double, Double) -> Bool = { scale.higherIsBetter ? $0 >= $1 : $0 <= $1 }
        guard abs(slope) > 1e-9 else { return better(atOne, target) ? .alreadySafe : .impossible }
        let needed = 1 + (target - atOne) / slope

        let best = scale.higherIsBetter ? scale.range.upperBound : scale.range.lowerBound
        let worst = scale.higherIsBetter ? scale.range.lowerBound : scale.range.upperBound
        let beyondBest = scale.higherIsBetter ? needed > best : needed < best
        let beyondWorst = scale.higherIsBetter ? needed <= worst : needed >= worst
        if beyondBest { return .impossible }
        if beyondWorst { return .alreadySafe }
        switch scale {
        // A hair of tolerance, so 11,0000000001 does not become 12.
        case .points: return .achievable((needed - 1e-9).rounded(.up))
        case .tendency: return .achievable(scale.choices().last { $0 <= needed + 1e-6 } ?? worst)
        case .german:
            let factor = settings.decimals >= 2 ? 100.0 : 10.0
            return .achievable((needed * factor + 1e-9).rounded(.down) / factor)
        }
    }
}

/// The school year in two halves: from August to January and from February to July.
public enum SchoolTerm {
    public static func interval(containing date: Date, calendar: Calendar = .current) -> DateInterval {
        let components = calendar.dateComponents([.year, .month], from: date)
        let year = components.year ?? 2000
        let month = components.month ?? 1
        let start: (year: Int, month: Int)
        let end: (year: Int, month: Int)
        switch month {
        case 8...12: (start, end) = ((year, 8), (year + 1, 2))
        case 1: (start, end) = ((year - 1, 8), (year, 2))
        default: (start, end) = ((year, 2), (year, 8))
        }
        let from = calendar.date(from: DateComponents(year: start.year, month: start.month, day: 1)) ?? date
        let to = calendar.date(from: DateComponents(year: end.year, month: end.month, day: 1)) ?? date
        return DateInterval(start: from, end: to)
    }

    public static func previous(of interval: DateInterval, calendar: Calendar = .current) -> DateInterval {
        Self.interval(containing: interval.start.addingTimeInterval(-86400), calendar: calendar)
    }

    /// 1 for the first half (from August), 2 for the second.
    public static func number(of interval: DateInterval, calendar: Calendar = .current) -> Int {
        calendar.component(.month, from: interval.start) == 8 ? 1 : 2
    }
}

/// A school year runs from August to July and is named by the year it begins in: 2026 is "2026/27".
public enum SchoolYear {
    public static func startYear(containing date: Date, calendar: Calendar = .current) -> Int {
        let components = calendar.dateComponents([.year, .month], from: date)
        let year = components.year ?? 2000
        return (components.month ?? 1) >= 8 ? year : year - 1
    }

    public static func label(_ startYear: Int) -> String {
        String(format: "%d/%02d", startYear, (startYear + 1) % 100)
    }

    public static func interval(startYear: Int, calendar: Calendar = .current) -> DateInterval {
        let start = calendar.date(from: DateComponents(year: startYear, month: 8, day: 1)) ?? Date()
        let end = calendar.date(from: DateComponents(year: startYear + 1, month: 8, day: 1)) ?? start
        return DateInterval(start: start, end: end)
    }

    /// The two halves: August to January, and February to July.
    public static func halves(startYear: Int, calendar: Calendar = .current) -> (first: DateInterval, second: DateInterval) {
        let year = interval(startYear: startYear, calendar: calendar)
        let middle = calendar.date(from: DateComponents(year: startYear + 1, month: 2, day: 1)) ?? year.start
        return (DateInterval(start: year.start, end: middle), DateInterval(start: middle, end: year.end))
    }
}
