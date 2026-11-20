import Foundation

/// One notification to be shown at a time, planned from the timetable and from what she is learning.
public struct Reminder: Sendable, Equatable {
    public var id: String
    public var fire: Date
    public var title: String
    public var body: String

    public init(id: String, fire: Date, title: String, body: String) {
        self.id = id
        self.fire = fire
        self.title = title
        self.body = body
    }
}

/// Which kinds of notification she wants. All are on until she turns them off.
public struct ReminderSettings: Sendable, Codable, Equatable {
    public var cancellations: Bool
    public var exams: Bool
    public var studyPlan: Bool
    public var cards: Bool
    public var news: Bool

    public init(cancellations: Bool = true, exams: Bool = true, studyPlan: Bool = true, cards: Bool = true, news: Bool = true) {
        self.cancellations = cancellations
        self.exams = exams
        self.studyPlan = studyPlan
        self.cards = cards
        self.news = news
    }

    public var any: Bool { cancellations || exams || studyPlan || cards || news }
}

public enum ReminderPlanner {
    public static let lookAheadDays = 7
    public static let examLookAheadDays = 21

    /// What to remind her of, from now on. The app plans again whenever it learns something new (a sync, a new day), so this only has to be right for now.
    /// - openStudyTasks: for each day (start of day) the number of study tasks of her plans that are not ticked off.
    public static func plan(
        now: Date,
        timetable: Timetable,
        settings: ReminderSettings,
        openStudyTasks: [Date: Int] = [:],
        dueCards: Int = 0,
        calendar: Calendar = .current
    ) -> [Reminder] {
        var result: [Reminder] = []
        let today = calendar.startOfDay(for: now)

        func at(_ day: Date, hour: Int, minute: Int = 0) -> Date? {
            calendar.date(bySettingHour: hour, minute: minute, second: 0, of: day)
        }
        func time(_ date: Date) -> String { date.formatted(.dateTime.hour().minute().locale(Locale(identifier: "de_DE"))) }

        if settings.cancellations {
            let limit = calendar.date(byAdding: .day, value: lookAheadDays, to: today) ?? today
            for event in timetable.events where event.cancelled && !event.allDay && event.start > now && event.start < limit {
                let day = calendar.startOfDay(for: event.start)
                // At seven in the morning of that day; later in the day only if the lesson is still ahead.
                let morning = at(day, hour: 7) ?? event.start
                let fire = morning > now ? morning : now.addingTimeInterval(5)
                guard fire < event.start else { continue }
                result.append(Reminder(id: "cancel-\(event.id)", fire: fire, title: "Unterricht entfällt", body: "\(event.summary) um \(time(event.start))" + (event.location.map { " in \($0)" } ?? "")))
            }
        }

        if settings.exams {
            for exam in timetable.upcomingExams(from: now, within: examLookAheadDays) {
                let day = calendar.startOfDay(for: exam.date)
                guard let eve = calendar.date(byAdding: .day, value: -1, to: day), let fire = at(eve, hour: 18), fire > now else { continue }
                result.append(Reminder(id: "exam-\(exam.id)", fire: fire, title: "Morgen: \(exam.title)", body: "um \(time(exam.date))" + (exam.event.location.map { " in \($0)" } ?? "") + ". Noch einmal kurz wiederholen?"))
            }
        }

        if settings.studyPlan {
            for (day, count) in openStudyTasks where count > 0 && day >= today && day < (calendar.date(byAdding: .day, value: lookAheadDays, to: today) ?? today) {
                guard let fire = at(day, hour: 16), fire > now else { continue }
                let key = calendar.dateComponents([.year, .month, .day], from: day)
                result.append(Reminder(id: "plan-\(key.year ?? 0)-\(key.month ?? 0)-\(key.day ?? 0)", fire: fire, title: "Lernen für die Arbeit", body: count == 1 ? "Heute steht 1 Aufgabe in deinem Lernplan." : "Heute stehen \(count) Aufgaben in deinem Lernplan."))
            }
        }

        if settings.cards, dueCards > 0 {
            for offset in 0..<3 {
                guard let day = calendar.date(byAdding: .day, value: offset, to: today), let fire = at(day, hour: 17), fire > now else { continue }
                let key = calendar.dateComponents([.year, .month, .day], from: day)
                let body = offset == 0 ? (dueCards == 1 ? "1 Karte wartet auf dich." : "\(dueCards) Karten warten auf dich.") : "Es warten Karteikarten auf dich."
                result.append(Reminder(id: "cards-\(key.year ?? 0)-\(key.month ?? 0)-\(key.day ?? 0)", fire: fire, title: "Karteikarten", body: body))
            }
        }

        return result.sorted { ($0.fire, $0.id) < ($1.fire, $1.id) }
    }

    /// The news that she has not been told about yet. The very first time everything counts as known, so she is not flooded.
    public static func unseenNews(_ items: [SchoolNews], seen: Set<String>?) -> [SchoolNews] {
        guard let seen else { return [] }
        return items.filter { !seen.contains($0.key) }
    }
}
