import Foundation
import Testing
@testable import JuNotesCore

private let berlin: Calendar = {
    var calendar = Calendar(identifier: .gregorian)
    calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
    return calendar
}()

/// 19 Nov 2026 in Berlin at the given time.
private func at(_ hour: Int, _ minute: Int = 0, day: Int = 19) -> Date {
    berlin.date(from: DateComponents(year: 2026, month: 11, day: day, hour: hour, minute: minute))!
}

private func lesson(_ summary: String, _ start: Date, minutes: Int = 45, cancelled: Bool = false, details: String? = nil) -> CalendarEvent {
    CalendarEvent(uid: summary + String(start.timeIntervalSince1970), start: start, end: start.addingTimeInterval(Double(minutes) * 60), summary: summary, details: details, cancelled: cancelled)
}

private let subjects = [Subject(id: uuid(1), name: "Mathe"), Subject(id: uuid(2), name: "Deutsch"), Subject(id: uuid(3), name: "Englisch"), Subject(id: uuid(4), name: "Biologie")]

struct SubjectMatcherTests {
    @Test(arguments: [("Mathematik", 1), ("Mathe", 1), ("M", 1), ("Ma Kl.9a", 1), ("Deutsch", 2), ("D", 2), ("Englisch", 3), ("Engl", 3), ("Bio", 4), ("Biologie 9b", 4), ("MATHEMATIK", 1)])
    func namesOfOneSubjectFindIt(title: String, expected: Int) {
        #expect(SubjectMatcher.match(title, in: subjects)?.id == uuid(expected))
    }

    @Test func aSubjectSheDoesNotHaveIsNotMatched() {
        #expect(SubjectMatcher.match("Latein", in: subjects) == nil)
        #expect(SubjectMatcher.match("", in: subjects) == nil)
    }

    @Test func umlautsAreSpelledTheGermanWay() {
        let list = [Subject(id: uuid(9), name: "Französisch")]
        #expect(SubjectMatcher.match("Franzoesisch", in: list)?.id == uuid(9))
        #expect(SubjectMatcher.match("Franz", in: list)?.id == uuid(9))
    }

    @Test func aPrefixOfThreeLettersMatches() {
        let list = [Subject(id: uuid(9), name: "Gemeinschaftskunde")]
        #expect(SubjectMatcher.match("Gemeinschaft", in: list)?.id == uuid(9))
        #expect(SubjectMatcher.match("Ge", in: list) == nil)
    }
}

struct TimetableTests {
    let day = [lesson("Mathematik", at(8)), lesson("Deutsch", at(8, 50), cancelled: true), lesson("Englisch", at(9, 55))]

    @Test func theRunningLessonIsFoundAndABreakHasNone() {
        let timetable = Timetable(events: day, calendar: berlin)

        #expect(timetable.current(at: at(8, 20))?.summary == "Mathematik")
        #expect(timetable.current(at: at(8, 46)) == nil)
        #expect(timetable.current(at: at(8, 0))?.summary == "Mathematik")
        #expect(timetable.current(at: at(8, 45)) == nil)
    }

    @Test func aCancelledLessonIsNeverCurrentOrNext() {
        let timetable = Timetable(events: day, calendar: berlin)

        #expect(timetable.current(at: at(9, 0)) == nil)
        #expect(timetable.next(after: at(8, 46))?.summary == "Englisch")
    }

    @Test func nothingIsNextAfterTheLastLesson() {
        #expect(Timetable(events: day, calendar: berlin).next(after: at(11)) == nil)
    }

    @Test func aDaysLessonsAreInOrderAndKeepTheCancelledOne() {
        let timetable = Timetable(events: day.reversed() + [lesson("Bio", at(8, day: 20))], calendar: berlin)

        #expect(timetable.lessons(on: at(12)).map(\.summary) == ["Mathematik", "Deutsch", "Englisch"])
    }

    @Test func allDayEventsCoverEveryDayOfTheirSpan() {
        let holiday = CalendarEvent(uid: "h", start: at(0, day: 19), end: at(0, day: 22), summary: "Ferien", allDay: true)
        let timetable = Timetable(events: [holiday], calendar: berlin)

        #expect(timetable.allDayEvents(on: at(12, day: 19)).count == 1)
        #expect(timetable.allDayEvents(on: at(12, day: 21)).count == 1)
        #expect(timetable.allDayEvents(on: at(12, day: 22)).isEmpty)
        #expect(timetable.lessons(on: at(12, day: 19)).isEmpty)
    }

    @Test func examsAreRecognisedByTheirWords() {
        let timetable = Timetable(events: [], calendar: berlin)
        #expect(timetable.isExam(lesson("Klassenarbeit Mathe", at(8))))
        #expect(timetable.isExam(lesson("Mathe", at(8), details: "Schulaufgabe")))
        #expect(timetable.isExam(lesson("Vokabeltest", at(8))) == false)
        #expect(timetable.isExam(lesson("Englisch Test", at(8))))
        #expect(timetable.isExam(lesson("Kontest", at(8))) == false)
        #expect(timetable.isExam(lesson("Mathematik", at(8))) == false)
    }

    @Test func upcomingExamsAreLimitedByDaysAndListedOnce() {
        let events = [
            lesson("Klassenarbeit Mathe", at(8, day: 24)),
            lesson("Klassenarbeit Mathe", at(9, day: 24)),
            lesson("Klausur Deutsch", at(8, day: 30)),
            lesson("Klassenarbeit Bio", at(8, day: 18)),
            lesson("Test Englisch", at(8, day: 20), cancelled: true),
        ]
        let exams = Timetable(events: events, calendar: berlin).upcomingExams(from: at(7), within: 7, subjects: subjects)

        #expect(exams.map(\.title) == ["Klassenarbeit Mathe"])
        #expect(exams.first?.subject?.id == uuid(1))
    }
}

struct CockpitTests {
    let events = [lesson("Mathematik", at(8)), lesson("Deutsch", at(8, 50)), lesson("Englisch", at(9, 55), cancelled: true), lesson("Biologie", at(11, 0))]

    func cockpit(_ now: Date) -> Cockpit { Cockpit(now: now, timetable: Timetable(events: events, calendar: berlin), subjects: subjects) }

    @Test func duringALessonItsSubjectIsSuggested() {
        let cockpit = cockpit(at(8, 10))

        #expect(cockpit.currentLesson?.summary == "Mathematik")
        #expect(cockpit.suggestedSubject?.id == uuid(1))
        #expect(cockpit.reason == .running(events[0]))
    }

    @Test func justBeforeALessonTheNextOneIsSuggested() {
        let cockpit = cockpit(at(7, 55))

        #expect(cockpit.currentLesson == nil)
        #expect(cockpit.suggestedSubject?.id == uuid(1))
        #expect(cockpit.reason == .startingSoon(events[0]))
    }

    @Test func farBeforeALessonNothingIsSuggested() {
        #expect(cockpit(at(7, 0)).suggestedSubject == nil)
        #expect(cockpit(at(10, 0)).suggestedSubject == nil)
    }

    @Test func aSubjectSheDoesNotHaveGivesNoSuggestion() {
        let unknown = Cockpit(now: at(8, 10), timetable: Timetable(events: [lesson("Latein", at(8))], calendar: berlin), subjects: subjects)

        #expect(unknown.currentLesson?.summary == "Latein")
        #expect(unknown.suggestedSubject == nil)
    }

    @Test func todayShowsCancelledLessonsAndTheNextOne() {
        let cockpit = cockpit(at(9, 0))

        #expect(cockpit.todaysLessons.count == 4)
        #expect(cockpit.cancelledToday.map(\.summary) == ["Englisch"])
        #expect(cockpit.nextLesson?.summary == "Biologie")
    }
}

struct CalendarDecodingTests {
    @Test func theServersCalendarDecodes() throws {
        let json = #"""
        [{"uid":"l1","start":"2026-11-19T07:00:00+00:00","end":"2026-11-19T07:45:00+00:00","summary":"Mathematik","location":"R 204","description":null,"cancelled":false,"allDay":false},
         {"uid":"l2","start":"2026-11-19T07:50:00.1234567+00:00","end":"2026-11-19T08:35:00+00:00","summary":"Deutsch","location":null,"description":"Klassenarbeit","cancelled":true,"allDay":false}]
        """#
        let events = try CalendarEvent.decodeList(Data(json.utf8))

        #expect(events.count == 2)
        #expect(events[0].location == "R 204")
        #expect(events[1].cancelled)
        #expect(events[1].details == "Klassenarbeit")
        #expect(events[0].start == Date(timeIntervalSince1970: 1_795_071_600))
    }

    @Test func theServersNewsDecodes() throws {
        let json = #"[{"key":"g1","title":"Elternabend","link":"https://s.example/n","publishedAt":"2026-11-18T10:00:00+00:00","summary":"Um 19 Uhr."},{"key":"g2","title":"Ohne","link":null,"publishedAt":null,"summary":null}]"#
        let news = try SchoolNews.decodeList(Data(json.utf8))

        #expect(news.map(\.title) == ["Elternabend", "Ohne"])
        #expect(news[0].link?.absoluteString == "https://s.example/n")
        #expect(news[1].publishedAt == nil)
    }

    @Test func nonsenseIsAnUnreachableServer() {
        #expect(throws: TransportError.serverUnreachable) { try CalendarEvent.decodeList(Data("{}".utf8)) }
        #expect(throws: TransportError.serverUnreachable) { try CalendarEvent.decodeList(Data(#"[{"uid":"x"}]"#.utf8)) }
    }
}

struct SubjectMatcherWordsTests {
    @Test func theSubjectCanBeAnyWordOfTheTitle() {
        #expect(SubjectMatcher.match("Klassenarbeit Mathe", in: subjects)?.id == uuid(1))
        #expect(SubjectMatcher.match("Vokabeltest Englisch", in: subjects)?.id == uuid(3))
    }

    @Test func aOneLetterAbbreviationOnlyCountsAtTheStart() {
        #expect(SubjectMatcher.match("D Gedichte", in: subjects)?.id == uuid(2))
        #expect(SubjectMatcher.match("Test D", in: subjects) == nil)
    }
}
