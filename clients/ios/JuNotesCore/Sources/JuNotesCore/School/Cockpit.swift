import Foundation

/// What the "Heute" screen shows, worked out from the timetable at one moment.
public struct Cockpit: Sendable {
    public var now: Date
    public var timetable: Timetable
    public var subjects: [Subject]

    /// A new page or a scan is offered the subject of a lesson that starts within this many minutes.
    public static let lookAheadMinutes = 10.0

    public init(now: Date, timetable: Timetable, subjects: [Subject]) {
        self.now = now
        self.timetable = timetable
        self.subjects = subjects
    }

    public var currentLesson: CalendarEvent? { timetable.current(at: now) }
    public var nextLesson: CalendarEvent? { timetable.next(after: now) }
    public var todaysLessons: [CalendarEvent] { timetable.lessons(on: now) }
    public var todaysAllDayEvents: [CalendarEvent] { timetable.allDayEvents(on: now) }
    public var cancelledToday: [CalendarEvent] { todaysLessons.filter(\.cancelled) }

    public func exams(within days: Int = 14) -> [Exam] {
        timetable.upcomingExams(from: now, within: days, subjects: subjects)
    }

    public func subject(for event: CalendarEvent) -> Subject? { SubjectMatcher.match(event.summary, in: subjects) }

    public enum Reason: Sendable, Equatable {
        case running(CalendarEvent)
        case startingSoon(CalendarEvent)
    }

    /// The lesson she is most likely in: the running one, or the next one if it starts within ten minutes.
    public var reason: Reason? {
        if let current = currentLesson { return .running(current) }
        if let next = nextLesson, next.start.timeIntervalSince(now) <= Self.lookAheadMinutes * 60, calendarDay(next.start) == calendarDay(now) { return .startingSoon(next) }
        return nil
    }

    /// Her subject for the lesson from `reason`, for "Neue Seite für jetzt" and the destination of a scan. Nil when nothing fits.
    public var suggestedSubject: Subject? {
        switch reason {
        case .running(let event), .startingSoon(let event): subject(for: event)
        case nil: nil
        }
    }

    private func calendarDay(_ date: Date) -> Date { timetable.calendar.startOfDay(for: date) }
}
