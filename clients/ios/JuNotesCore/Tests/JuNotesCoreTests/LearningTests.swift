import Foundation
import Testing
@testable import JuNotesCore

private let berlin: Calendar = {
    var calendar = Calendar(identifier: .gregorian)
    calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
    return calendar
}()

private func day(_ d: Int, hour: Int = 9, month: Int = 11) -> Date {
    berlin.date(from: DateComponents(year: 2026, month: month, day: d, hour: hour))!
}

struct SpacedRepetitionTests {
    let now = day(19)

    @Test func aNewCardIsNewAndNeverDue() {
        let schedule = CardSchedule()
        #expect(schedule.isNew)
        #expect(!schedule.isDue(at: now, calendar: berlin))
    }

    @Test func goodAnswersPushTheCardFurtherOutEachTime() {
        var schedule = CardSchedule()
        var intervals: [Int] = []
        var moment = now
        for _ in 0..<5 {
            schedule = schedule.reviewed(.good, now: moment, calendar: berlin)
            intervals.append(schedule.interval)
            moment = schedule.due!
        }

        #expect(intervals[0] == 1)
        #expect(intervals[1] == 6)
        #expect(intervals == intervals.sorted())
        #expect(intervals[4] > intervals[3] && intervals[3] > intervals[2])
    }

    @Test func theDueDateIsACalendarDay() {
        let schedule = CardSchedule().reviewed(.good, now: day(19, hour: 23), calendar: berlin)

        #expect(berlin.component(.day, from: schedule.due!) == 20)
        #expect(berlin.component(.hour, from: schedule.due!) == 0)
        // It is due all of that day, from the first minute.
        #expect(schedule.isDue(at: day(20, hour: 0), calendar: berlin))
        #expect(!schedule.isDue(at: day(19, hour: 23), calendar: berlin))
    }

    @Test func aForgottenCardComesBackAtOnceAndGetsHarder() {
        let learned = CardSchedule().reviewed(.good, now: now, calendar: berlin).reviewed(.good, now: day(20), calendar: berlin)
        let forgotten = learned.reviewed(.again, now: day(26), calendar: berlin)

        #expect(forgotten.repetitions == 0)
        #expect(forgotten.lapses == 1)
        #expect(forgotten.ease < learned.ease)
        #expect(forgotten.isDue(at: day(26), calendar: berlin))
        // After it is known again it starts over with short steps.
        #expect(forgotten.reviewed(.good, now: day(26), calendar: berlin).interval == 1)
    }

    @Test func theEaseNeverFallsBelowTheMinimum() {
        var schedule = CardSchedule()
        for _ in 0..<30 { schedule = schedule.reviewed(.again, now: now, calendar: berlin) }

        #expect(schedule.ease == CardSchedule.minimumEase)
    }

    @Test func easyJumpsAheadAndHardHolds() {
        let base = CardSchedule(ease: 2.5, interval: 10, repetitions: 3)

        let easy = base.reviewed(.easy, now: now, calendar: berlin)
        let good = base.reviewed(.good, now: now, calendar: berlin)
        let hard = base.reviewed(.hard, now: now, calendar: berlin)

        #expect(easy.interval >= good.interval)
        #expect(good.interval > hard.interval)
        #expect(hard.interval > base.interval)
        #expect(easy.ease > good.ease && good.ease > hard.ease)
    }

    @Test func aCardSurvivesTheWire() throws {
        let card = Flashcard(id: uuid(1), subjectID: uuid(2), front: "Nenner?", back: "unten", sourcePageID: uuid(3), schedule: CardSchedule().reviewed(.good, now: now, calendar: berlin))
        let change = try card.change()

        #expect(change.kind == .card)
        #expect(change.parentID == uuid(2))
        #expect(try Flashcard.decode(SyncRecord(kind: .card, id: uuid(1), parentID: uuid(2), payload: change.payload, seq: 1)) == card)
    }

    @Test func todaysQueueHasWhatIsDueOldestFirstThenNewOnesUpToTheLimit() {
        let overdue = Flashcard(id: uuid(1), subjectID: uuid(9), front: "a", back: "b", schedule: CardSchedule(interval: 3, repetitions: 2, due: day(10)))
        let dueToday = Flashcard(id: uuid(2), subjectID: uuid(9), front: "c", back: "d", schedule: CardSchedule(interval: 3, repetitions: 2, due: day(19, hour: 0)))
        let later = Flashcard(id: uuid(3), subjectID: uuid(9), front: "e", back: "f", schedule: CardSchedule(interval: 3, repetitions: 2, due: day(25)))
        let fresh = (4...8).map { Flashcard(id: uuid($0), subjectID: uuid(9), front: "n\($0)", back: "x") }

        let queue = StudyQueue.today([later, dueToday, overdue] + fresh, now: now, calendar: berlin, newLimit: 3)

        #expect(queue.map(\.id) == [uuid(1), uuid(2), uuid(4), uuid(5), uuid(6)])
        let counts = StudyQueue.counts([later, dueToday, overdue] + fresh, now: now, calendar: berlin)
        #expect(counts.due == 2)
        #expect(counts.new == 5)
        #expect(counts.total == 8)
    }
}

struct StudyPlannerTests {
    let topics = [StudyTopic(title: "Brüche", minutes: 30), StudyTopic(title: "Gleichungen", minutes: 40), StudyTopic(title: "Prozent", minutes: 25)]

    func plan(daysAhead: Int, perDay: Int = 45, topics: [StudyTopic]? = nil) -> StudyPlan {
        StudyPlanner.plan(topics: topics ?? self.topics, examDate: day(19 + daysAhead), today: day(19), minutesPerDay: perDay, calendar: berlin)
    }

    func days(of kind: StudyTaskKind, topic: String? = nil, in plan: StudyPlan) -> [Int] {
        plan.days.enumerated().filter { $0.element.tasks.contains { $0.kind == kind && (topic == nil || $0.topic == topic) } }.map(\.offset)
    }

    @Test func noDayHasMoreThanThePersonHasTimeFor() {
        for daysAhead in [3, 5, 8, 14] {
            #expect(plan(daysAhead: daysAhead).days.allSatisfy { $0.minutes <= 45 })
        }
    }

    @Test func thePlanRunsFromTodayToTheDayBeforeTheExam() {
        let plan = plan(daysAhead: 7)

        #expect(plan.days.count == 7)
        #expect(plan.days.first?.date == berlin.startOfDay(for: day(19)))
        #expect(plan.days.last?.date == berlin.startOfDay(for: day(25)))
    }

    @Test func eachTopicIsLearnedThenPractisedThenReviewedInThatOrder() {
        let plan = plan(daysAhead: 14)

        for topic in ["Brüche", "Gleichungen", "Prozent"] {
            let learn = days(of: .learn, topic: topic, in: plan)
            let practice = days(of: .practice, topic: topic, in: plan)
            let review = days(of: .review, topic: topic, in: plan)
            #expect(learn.count == 1 && practice.count == 1 && review.count == 1)
            #expect(learn[0] < practice[0])
            #expect(practice[0] + 2 <= review[0])
        }
        #expect(plan.unscheduled.isEmpty)
    }

    @Test func theMockTestIsTwoDaysBeforeTheEndAndTheLastLookOnTheFinalDay() {
        let plan = plan(daysAhead: 10)

        #expect(days(of: .lastLook, in: plan) == [9])
        let mock = days(of: .mockTest, in: plan)
        #expect(mock.count == 1 && mock[0] >= 7 && mock[0] <= 8)
    }

    @Test func aTightPlanSaysWhatDidNotFitInsteadOfOverfillingDays() {
        let plan = plan(daysAhead: 2, perDay: 30, topics: [StudyTopic(title: "Alles", minutes: 60), StudyTopic(title: "Noch mehr", minutes: 60)])

        #expect(plan.days.allSatisfy { $0.minutes <= 30 })
        #expect(!plan.unscheduled.isEmpty)
    }

    @Test func anExamTodayOrInThePastHasNoDaysToPlan() {
        #expect(plan(daysAhead: 0).days.isEmpty)
        #expect(StudyPlanner.plan(topics: topics, examDate: day(10), today: day(19), minutesPerDay: 45, calendar: berlin).days.isEmpty)
    }

    @Test func theDayBeforeTheExamStillGetsALastLook() {
        let plan = plan(daysAhead: 1)

        #expect(plan.days.count == 1)
        #expect(plan.days[0].tasks.contains { $0.kind == .lastLook })
    }

    @Test func tasksOfADayAreInTheOrderLearnPracticeReviewMockLastLook() {
        let plan = plan(daysAhead: 3, perDay: 120)
        let order: [StudyTaskKind] = [.learn, .practice, .review, .mockTest, .lastLook]

        for day in plan.days {
            let indexes = day.tasks.map { order.firstIndex(of: $0.kind)! }
            #expect(indexes == indexes.sorted())
        }
    }

    @Test func noTopicsStillGivesALastLook() {
        let plan = plan(daysAhead: 5, topics: [])

        #expect(days(of: .lastLook, in: plan) == [4])
        #expect(plan.totalMinutes >= StudyPlanner.lastLookMinutes)
    }
}

struct TextCompareTests {
    @Test func aPerfectCopyScoresOne() {
        let result = TextCompare.compare(original: "Gleiche Nenner: nur die Zähler addieren.", recalled: "Gleiche Nenner nur die Zähler addieren")

        #expect(result.score == 1)
        #expect(result.missing.isEmpty)
        #expect(result.extra.isEmpty)
    }

    @Test func leftOutWordsAreMarkedWhereTheyWere() {
        let result = TextCompare.compare(original: "Die Photosynthese braucht Licht, Wasser und Kohlendioxid.", recalled: "Die Photosynthese braucht Licht und Kohlendioxid")

        #expect(result.missing == ["Wasser"])
        #expect(result.words.map(\.text) == ["Die", "Photosynthese", "braucht", "Licht", "Wasser", "und", "Kohlendioxid"])
        #expect(abs(result.score - 6.0 / 7.0) < 0.0001)
    }

    @Test func wordsThatAreNotInTheOriginalAreListedAsExtra() {
        let result = TextCompare.compare(original: "Der Nenner steht unten", recalled: "Der Nenner steht immer unten")

        #expect(result.extra == ["immer"])
        #expect(result.missing.isEmpty)
    }

    @Test func smallRecognitionMistakesInLongerWordsDoNotCount() {
        // "Photosynthese" read as "Photosynthase", "Kohlendioxid" as "Kohlendioxld", a missed umlaut.
        let result = TextCompare.compare(original: "Photosynthese braucht Kohlendioxid und Zähler", recalled: "Photosynthase braucht Kohlendioxld und Zahler")

        #expect(result.missing.isEmpty)
    }

    @Test func umlautsMaySpellTheGermanWay() {
        #expect(TextCompare.compare(original: "größer als Zähler", recalled: "groesser als Zaehler").missing.isEmpty)
    }

    @Test func shortWordsMustMatchExactly() {
        let result = TextCompare.compare(original: "Das ist rot", recalled: "Das ost rot")

        #expect(result.missing == ["ist"])
    }

    @Test func capitalisationAndPunctuationAreIgnored() {
        #expect(TextCompare.compare(original: "Ja, genau!", recalled: "ja genau").score == 1)
    }

    @Test func writingNothingScoresZeroAndAnEmptyOriginalScoresOne() {
        #expect(TextCompare.compare(original: "Ein kurzer Satz hier", recalled: "").score == 0)
        #expect(TextCompare.compare(original: "", recalled: "irgendwas").score == 1)
    }

    @Test func wordsInTheWrongOrderCountAsMissingWhereTheyBreakTheSequence() {
        let result = TextCompare.compare(original: "erst addieren dann kürzen", recalled: "erst kürzen dann addieren")

        #expect(result.score < 1)
    }

    @Test func aLongTextIsComparedQuickly() {
        let text = (0..<1500).map { "wort\($0 % 40)" }.joined(separator: " ")
        let start = Date()

        let result = TextCompare.compare(original: text, recalled: text)

        #expect(result.score == 1)
        #expect(Date().timeIntervalSince(start) < 5)
    }
}

struct TextCompareLimitsTests {
    @Test func aLongTextIsCutOffAtTheLimitInsteadOfExhaustingMemory() {
        let text = (0..<400).map { "wort\($0)" }.joined(separator: " ")

        let result = TextCompare.compare(original: text, recalled: text, limit: 60)

        #expect(result.words.count == 60)
        #expect(result.score == 1)
        #expect(TextCompare.maximumWords == 1500)
    }

    @Test func aRealisticPageOfNoisyTextIsFast() {
        let words = (0..<400).map { "Begriff\($0 % 90)abc" }
        let noisy = words.map { $0.count > 8 ? String($0.dropLast()) + "x" : $0 }
        let start = Date()

        let result = TextCompare.compare(original: words.joined(separator: " "), recalled: noisy.joined(separator: " "))

        #expect(result.score > 0.99)
        #expect(Date().timeIntervalSince(start) < 3)
    }
}
