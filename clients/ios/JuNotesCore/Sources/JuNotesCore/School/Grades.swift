import Foundation

public enum GradeScale: String, Sendable, Codable, Equatable {
    /// 1 (best) to 6 (worst), in tenths.
    case german
    /// 0 to 15 points, as in the upper school; more is better.
    case points

    public var range: ClosedRange<Double> { self == .german ? 1...6 : 0...15 }
    public var higherIsBetter: Bool { self == .points }
}

public enum GradeCategory: String, Sendable, Codable, Equatable, CaseIterable {
    /// Klassenarbeit, Klausur.
    case written
    /// Mündliche Note, Mitarbeit.
    case oral
    /// Kurztest, Vokabeltest.
    case test
    case other
}

/// How much each kind of grade counts. Schools differ, so this is hers to set; the usual start is written twice as much.
public struct GradeWeights: Sendable, Codable, Equatable {
    public var written: Double
    public var oral: Double
    public var test: Double
    public var other: Double

    public static let standard = GradeWeights(written: 2, oral: 1, test: 1, other: 1)

    public init(written: Double, oral: Double, test: Double, other: Double) {
        self.written = written
        self.oral = oral
        self.test = test
        self.other = other
    }

    public func weight(for category: GradeCategory) -> Double {
        switch category {
        case .written: written
        case .oral: oral
        case .test: test
        case .other: other
        }
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
    public static func weight(of grade: Grade, _ weights: GradeWeights) -> Double { grade.weight ?? weights.weight(for: grade.category) }

    /// The weighted average, or nil when there is no grade with a weight above zero.
    public static func average(_ grades: [Grade], weights: GradeWeights = .standard) -> Double? {
        let total = grades.reduce(0.0) { $0 + weight(of: $1, weights) }
        guard total > 0 else { return nil }
        return grades.reduce(0.0) { $0 + $1.value * weight(of: $1, weights) } / total
    }

    /// The average of the subjects' averages, each subject counting once.
    public static func overall(_ gradesBySubject: [[Grade]], weights: GradeWeights = .standard) -> Double? {
        let averages = gradesBySubject.compactMap { average($0, weights: weights) }
        return averages.isEmpty ? nil : averages.reduce(0, +) / Double(averages.count)
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

    /// What she needs in the next piece of work, counting `nextWeight`, to end at `target`.
    public static func needed(forTarget target: Double, grades: [Grade], nextWeight: Double, scale: GradeScale, weights: GradeWeights = .standard) -> Need {
        let total = grades.reduce(0.0) { $0 + weight(of: $1, weights) }
        guard !grades.isEmpty, nextWeight > 0 else { return .noData }
        let sum = grades.reduce(0.0) { $0 + $1.value * weight(of: $1, weights) }
        let needed = (target * (total + nextWeight) - sum) / nextWeight

        let best = scale.higherIsBetter ? scale.range.upperBound : scale.range.lowerBound
        let worst = scale.higherIsBetter ? scale.range.lowerBound : scale.range.upperBound
        let beyondBest = scale.higherIsBetter ? needed > best : needed < best
        let beyondWorst = scale.higherIsBetter ? needed <= worst : needed >= worst
        if beyondBest { return .impossible }
        if beyondWorst { return .alreadySafe }
        // German grades come in tenths and she needs to be at least that good: round toward better. Points are whole numbers: round up.
        return .achievable(scale.higherIsBetter ? needed.rounded(.up) : (needed * 10).rounded(.down) / 10)
    }
}
