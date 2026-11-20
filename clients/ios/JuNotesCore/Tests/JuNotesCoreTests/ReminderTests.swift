import Foundation
import Testing
@testable import JuNotesCore

struct ReminderTests {
    private var calendar: Calendar {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
        return calendar
    }

    private func date(_ month: Int, _ day: Int, _ hour: Int = 0, _ minute: Int = 0) -> Date {
        calendar.date(from: DateComponents(year: 2026, month: month, day: day, hour: hour, minute: minute))!
    }

    private func event(_ title: String, _ start: Date, cancelled: Bool = false, room: String? = nil) -> CalendarEvent {
        CalendarEvent(uid: title + String(start.timeIntervalSince1970), start: start, end: start.addingTimeInterval(2700), summary: title, location: room, cancelled: cancelled)
    }

    private func plan(_ events: [CalendarEvent], now: Date, settings: ReminderSettings = ReminderSettings(), tasks: [Date: Int] = [:], cards: Int = 0) -> [Reminder] {
        ReminderPlanner.plan(now: now, timetable: Timetable(events: events, calendar: calendar), settings: settings, openStudyTasks: tasks, dueCards: cards, calendar: calendar)
    }

    @Test func aCancelledLessonIsAnnouncedInTheMorning() {
        let lesson = event("Englisch", date(11, 23, 9, 55), cancelled: true, room: "R 118")
        let reminders = plan([lesson], now: date(11, 22, 20))
        #expect(reminders.count == 1)
        #expect(reminders[0].fire == date(11, 23, 7))
        #expect(reminders[0].title == "Unterricht entfällt")
        #expect(reminders[0].body.contains("Englisch") && reminders[0].body.contains("R 118"))
    }

    @Test func aLessonThatIsCancelledForTodayIsAnnouncedAtOnceIfItIsStillAhead() {
        let now = date(11, 23, 8)
        let reminders = plan([event("Englisch", date(11, 23, 9, 55), cancelled: true)], now: now)
        #expect(reminders.count == 1)
        #expect(reminders[0].fire > now && reminders[0].fire < now.addingTimeInterval(60))
        #expect(plan([event("Englisch", date(11, 23, 7, 30), cancelled: true)], now: now).isEmpty)
    }

    @Test func normalLessonsAndFarAwayCancellationsAreNotAnnounced() {
        #expect(plan([event("Mathe", date(11, 23, 8))], now: date(11, 22, 20)).isEmpty)
        #expect(plan([event("Mathe", date(12, 20, 8), cancelled: true)], now: date(11, 22, 20)).isEmpty)
    }

    @Test func anExamIsAnnouncedTheEveningBefore() {
        let exam = event("Klassenarbeit Mathe", date(11, 27, 8), room: "R 204")
        let reminders = plan([exam], now: date(11, 20, 10))
        #expect(reminders.count == 1)
        #expect(reminders[0].fire == date(11, 26, 18))
        #expect(reminders[0].title == "Morgen: Klassenarbeit Mathe")
        #expect(plan([exam], now: date(11, 26, 19)).isEmpty)
    }

    @Test func studyTasksRemindAtFourInTheAfternoonOnlyWhereThereAreOpenOnes() {
        let today = calendar.startOfDay(for: date(11, 20))
        let tomorrow = calendar.startOfDay(for: date(11, 21))
        let reminders = plan([], now: date(11, 20, 10), tasks: [today: 2, tomorrow: 0])
        #expect(reminders.map(\.fire) == [date(11, 20, 16)])
        #expect(reminders[0].body == "Heute stehen 2 Aufgaben in deinem Lernplan.")
        #expect(plan([], now: date(11, 20, 17), tasks: [today: 2]).isEmpty)
    }

    @Test func dueCardsRemindForThreeDays() {
        let reminders = plan([], now: date(11, 20, 10), cards: 5)
        #expect(reminders.count == 3)
        #expect(reminders[0].body == "5 Karten warten auf dich.")
        #expect(reminders[1].body == "Es warten Karteikarten auf dich.")
        #expect(plan([], now: date(11, 20, 10), cards: 0).isEmpty)
    }

    @Test func eachKindCanBeSwitchedOff() {
        let events = [event("Englisch", date(11, 23, 9), cancelled: true), event("Klausur Mathe", date(11, 27, 8))]
        let none = ReminderSettings(cancellations: false, exams: false, studyPlan: false, cards: false, news: false)
        #expect(plan(events, now: date(11, 20, 10), settings: none, tasks: [calendar.startOfDay(for: date(11, 20)): 1], cards: 3).isEmpty)
        #expect(!none.any)
        #expect(ReminderSettings().any)
    }

    @Test func theRemindersComeInTimeOrder() {
        let events = [event("Klausur Mathe", date(11, 27, 8)), event("Englisch", date(11, 21, 9), cancelled: true)]
        let reminders = plan(events, now: date(11, 20, 10), cards: 1)
        #expect(reminders.map(\.fire) == reminders.map(\.fire).sorted())
    }

    @Test func newsIsOnlyAnnouncedOnceItIsKnownWhatWasSeen() {
        let items = [SchoolNews(key: "a", title: "A"), SchoolNews(key: "b", title: "B")]
        #expect(ReminderPlanner.unseenNews(items, seen: nil).isEmpty)
        #expect(ReminderPlanner.unseenNews(items, seen: ["a"]).map(\.key) == ["b"])
        #expect(ReminderPlanner.unseenNews(items, seen: ["a", "b"]).isEmpty)
    }
}
