import Foundation

/// A kind of event that is neither a lesson nor a holiday: an exam, shown on its own.
public struct Exam: Sendable, Equatable, Identifiable {
    public var event: CalendarEvent
    public var subject: Subject?

    public var id: String { event.id }
    public var date: Date { event.start }
    public var title: String { event.summary }
}

/// The timetable and everything the school plans, with the questions the app asks of it: what is on now, what is next, what is coming.
public struct Timetable: Sendable {
    public var events: [CalendarEvent]
    public var calendar: Calendar

    public init(events: [CalendarEvent], calendar: Calendar = .current) {
        self.events = events.sorted { ($0.start, $0.summary) < ($1.start, $1.summary) }
        self.calendar = calendar
    }

    /// Words that make an entry an exam. Schools write them in the title or the description.
    private static let examPrefixes = ["klassenarbeit", "klausur", "schulaufgabe", "lernzielkontrolle", "prüfung", "pruefung", "kursarbeit", "extemporale", "klarbeit"]
    /// Short words that only count when they stand alone ("Test", not "Kontest").
    private static let examWords: Set<String> = ["test", "ex", "lek"]

    public func isExam(_ event: CalendarEvent) -> Bool {
        let words = (event.summary + " " + (event.details ?? "")).lowercased().split { !$0.isLetter }.map(String.init)
        return words.contains { word in Self.examWords.contains(word) || Self.examPrefixes.contains { word.hasPrefix($0) } }
    }

    /// The lessons on a day, in order. Holidays and all-day entries are not lessons.
    public func lessons(on day: Date) -> [CalendarEvent] {
        events.filter { !$0.allDay && calendar.isDate($0.start, inSameDayAs: day) }
    }

    public func allDayEvents(on day: Date) -> [CalendarEvent] {
        events.filter { event in
            guard event.allDay else { return false }
            let start = calendar.startOfDay(for: event.start)
            let last = calendar.startOfDay(for: event.end.addingTimeInterval(-1))
            return calendar.startOfDay(for: day) >= start && calendar.startOfDay(for: day) <= max(start, last)
        }
    }

    /// The lesson running at `date`, if it has not been cancelled.
    public func current(at date: Date) -> CalendarEvent? {
        events.first { !$0.allDay && !$0.cancelled && $0.start <= date && date < $0.end }
    }

    /// The next lesson that still takes place, today or later.
    public func next(after date: Date) -> CalendarEvent? {
        events.first { !$0.allDay && !$0.cancelled && $0.start > date }
    }

    /// Exams from `date` on, within `days` days. The same title on the same day counts once.
    public func upcomingExams(from date: Date, within days: Int, subjects: [Subject] = []) -> [Exam] {
        let limit = calendar.date(byAdding: .day, value: days, to: calendar.startOfDay(for: date)) ?? date
        var seen = Set<String>()
        var exams: [Exam] = []
        for event in events where isExam(event) && !event.cancelled && event.end >= date && event.start < limit {
            let key = event.summary + "|" + String(calendar.startOfDay(for: event.start).timeIntervalSince1970)
            if seen.insert(key).inserted { exams.append(Exam(event: event, subject: SubjectMatcher.match(event.summary, in: subjects))) }
        }
        return exams
    }
}
