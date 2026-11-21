import Foundation
import Testing
@testable import JuNotesCore

private func grade(_ value: Double, _ category: GradeCategory = .written, weight: Double? = nil, subject: Int = 1) -> Grade {
    Grade(id: UUID(), subjectID: uuid(subject), value: value, category: category, weight: weight, date: Date(timeIntervalSince1970: 1_800_000_000), title: "")
}

struct GradesTests {
    @Test func writtenAndOralAreMixedByHerShares() {
        // Written part: Klassenarbeit 2. Oral part: Mitarbeit 4. Shares oral 30, written 70: 0,7 * 2 + 0,3 * 4 = 2,6
        let settings = GradingSettings(oralPercent: 30)
        let value = GradeCalculator.average([grade(2, .written), grade(4, .oral)], settings: settings)!
        #expect(abs(value - 2.6) < 0.0001)
    }

    @Test func insideAPartTheWeightsDecide() {
        // Klassenarbeit counts 2, Test counts 1: (1*2 + 4*1) / 3 = 2
        #expect(GradeCalculator.average([grade(1, .written), grade(4, .test)]) == 2)
        // A single grade can count three times: (1*3 + 4*1) / 4
        #expect(GradeCalculator.average([grade(1, .written, weight: 3), grade(4, .test, weight: 1)]) == 1.75)
    }

    @Test func aPartWithoutGradesIsLeftOut() {
        #expect(GradeCalculator.average([grade(3, .oral)], settings: GradingSettings(oralPercent: 30)) == 3)
        #expect(GradeCalculator.average([grade(2, .written)], settings: GradingSettings(oralPercent: 30)) == 2)
    }

    @Test func noGradesHaveNoAverage() {
        #expect(GradeCalculator.average([]) == nil)
        #expect(GradeCalculator.average([grade(2, .other, weight: 0)]) == nil)
    }

    @Test func zeroSharesFallBackToAPlainMean() {
        var settings = GradingSettings()
        settings.oralPercent = 0
        // Written has 100 percent: oral grades then do not count once there are written ones.
        #expect(GradeCalculator.average([grade(2, .written), grade(4, .oral)], settings: settings) == 2)
    }

    @Test func theOverallAverageUsesEachSubjectsFactor() {
        let math = (grades: [grade(1)], settings: GradingSettings(factor: 2))
        let german = (grades: [grade(4, subject: 2)], settings: GradingSettings())
        // (1*2 + 4*1) / 3 = 2
        #expect(GradeCalculator.overall([math, german]) == 2)
        #expect(GradeCalculator.overall([]) == nil)
    }

    @Test func mainSubjectsHaveAnAverageOfTheirOwn() {
        let math = (grades: [grade(1)], settings: GradingSettings(isMajor: true))
        let sport = (grades: [grade(3, subject: 2)], settings: GradingSettings())
        #expect(GradeCalculator.overall([math, sport], majorOnly: true) == 1)
        #expect(GradeCalculator.overall([math, sport], majorOnly: false) == 3)
        #expect(GradeCalculator.overall([math, sport]) == 2)
    }

    @Test func subjectsInPointsAreNotMixedIntoTheOverallAverage() {
        let math = (grades: [grade(2)], settings: GradingSettings())
        let points = (grades: [grade(13, subject: 2)], settings: GradingSettings(scale: .points))
        #expect(GradeCalculator.overall([math, points]) == 2)
    }

    @Test func formattingUsesADecimalCommaAndRoundsTheUsualWay() {
        #expect(GradeCalculator.format(2.34) == "2,3")
        #expect(GradeCalculator.format(2.35) == "2,4")
        #expect(GradeCalculator.format(2) == "2,0")
        #expect(GradeCalculator.format(nil) == "–")
        #expect(GradeCalculator.format(2.345, decimals: 2) == "2,35")
    }

    @Test func tendenciesReadLikeOnAReport() {
        let scale = GradeScale.tendency
        #expect(scale.display(2.0) == "2")
        #expect(scale.display(1.7) == "2+")
        #expect(scale.display(2.3) == "2−")
        #expect(scale.display(nil) == "–")
        #expect(scale.choices().count == 16)
    }

    @Test func theChoicesFollowTheScaleAndTheFineness() {
        #expect(GradeScale.german.choices().count == 51)
        #expect(GradeScale.german.choices()[1] == 1.1)
        #expect(GradeScale.german.choices(decimals: 2).count == 501)
        #expect(GradeScale.german.choices(decimals: 2)[1] == 1.01)
        #expect(GradeScale.points.choices().count == 16)
    }

    @Test func aTypedGradeIsSnappedToTheScaleOrRefused() {
        #expect(GradeScale.german.normalized(2.34) == 2.3)
        #expect(GradeScale.german.normalized(2.34, decimals: 2) == 2.34)
        #expect(GradeScale.german.normalized(6.5) == nil)
        #expect(GradeScale.german.normalized(0.9) == nil)
        #expect(GradeScale.tendency.normalized(2.2) == 2.3)
        #expect(GradeScale.points.normalized(12.4) == 12)
        #expect(GradeScale.points.normalized(16) == nil)
    }

    @Test func whatSheNeedsForATargetOnTheGermanScale() {
        // Written 2 (Klassenarbeit), oral 3, shares 50/50: now 2,5. A next Klassenarbeit of x with weight 2 joins the written part:
        // written = (2*2 + x*2) / 4, final = (written + 3) / 2. For 2,0: written = 1 -> x = 0 ... so even a 1 gives 1,5 + 1,5 = 2,25 at best, target 2 is out of reach.
        let grades = [grade(2, .written), grade(3, .oral)]
        #expect(GradeCalculator.needed(forTarget: 2, grades: grades, next: .written) == .impossible)
        // Target 2,3 needs written = 1,6 -> x = 1,2
        #expect(GradeCalculator.needed(forTarget: 2.3, grades: grades, next: .written) == .achievable(1.2))
    }

    @Test func aTargetAlreadyReachedForSure() {
        #expect(GradeCalculator.needed(forTarget: 5, grades: [grade(1, .written), grade(1, .written)], next: .written) == .alreadySafe)
    }

    @Test func aTargetThatCannotBeReached() {
        #expect(GradeCalculator.needed(forTarget: 1.0, grades: [grade(5, .written), grade(5, .written)], next: .written) == .impossible)
    }

    @Test func noGradesGiveNoAdvice() {
        #expect(GradeCalculator.needed(forTarget: 2, grades: [], next: .written) == .noData)
    }

    @Test func pointsAreWholeAndHigherIsBetter() {
        // Both grades written, weight 2 each; next written grade weight 2: (10*2 + 12*2 + x*2) / 6 = 11 -> x = 11
        let grades = [grade(10, .written), grade(12, .written)]
        let settings = GradingSettings(scale: .points)
        #expect(GradeCalculator.needed(forTarget: 11, grades: grades, next: .written, settings: settings) == .achievable(11))
        #expect(GradeCalculator.needed(forTarget: 14.9, grades: grades, next: .written, settings: settings) == .impossible)
        #expect(GradeCalculator.needed(forTarget: 5, grades: grades, next: .written, settings: settings) == .alreadySafe)
    }

    @Test func aGradeSurvivesTheWireWithItsSubjectAsParent() throws {
        let original = Grade(id: uuid(7), subjectID: uuid(1), value: 2.3, category: .test, weight: 0.5, date: Date(timeIntervalSince1970: 1_800_000_000), title: "Vokabeln")
        let change = try original.change()

        #expect(change.kind == .grade)
        #expect(change.parentID == uuid(1))
        #expect(try Grade.decode(SyncRecord(kind: .grade, id: uuid(7), parentID: uuid(1), payload: change.payload, seq: 1)) == original)
    }

    @Test func gradingSettingsSurviveTheWireAndOldSubjectsGetTheDefaults() throws {
        let settings = GradingSettings(scale: .tendency, decimals: 2, oralPercent: 30, isMajor: true, factor: 2, averageDecimals: 1)
        let subject = Subject(id: uuid(3), name: "Mathe", grading: settings)
        let change = try subject.change()
        #expect(try Subject.decode(SyncRecord(kind: .subject, id: uuid(3), payload: change.payload, seq: 1)).grading == settings)

        let old = try JSONDecoder().decode(JSONValue.self, from: Data(##"{"name":"Alt","color":"#000000","symbol":"book","sort":0}"##.utf8))
        #expect(try Subject.decode(SyncRecord(kind: .subject, id: uuid(4), payload: old, seq: 1)).grading == GradingSettings())
    }

    @Test func settingsAreClampedToWhatMakesSense() {
        let settings = GradingSettings(decimals: 9, oralPercent: 140, factor: -1, averageDecimals: 7)
        #expect(settings.decimals == 2)
        #expect(settings.oralPercent == 100)
        #expect(settings.writtenPercent == 0)
        #expect(settings.factor == 0)
        #expect(settings.averageDecimals == 3)
    }
}

struct SchoolTermTests {
    private var calendar: Calendar {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
        return calendar
    }

    private func date(_ year: Int, _ month: Int, _ day: Int) -> Date {
        calendar.date(from: DateComponents(year: year, month: month, day: day, hour: 12))!
    }

    @Test func novemberIsInTheFirstHalfThatStartedInAugust() {
        let term = SchoolTerm.interval(containing: date(2026, 11, 20), calendar: calendar)
        #expect(term.start == date(2026, 8, 1).addingTimeInterval(-12 * 3600))
        #expect(SchoolTerm.number(of: term, calendar: calendar) == 1)
        #expect(term.contains(date(2027, 1, 31)))
        #expect(!term.contains(date(2027, 2, 1).addingTimeInterval(12 * 3600)))
    }

    @Test func januaryStillBelongsToTheHalfFromTheYearBefore() {
        let term = SchoolTerm.interval(containing: date(2027, 1, 15), calendar: calendar)
        #expect(calendar.component(.year, from: term.start) == 2026)
        #expect(SchoolTerm.number(of: term, calendar: calendar) == 1)
    }

    @Test func springIsTheSecondHalf() {
        let term = SchoolTerm.interval(containing: date(2027, 4, 10), calendar: calendar)
        #expect(SchoolTerm.number(of: term, calendar: calendar) == 2)
        #expect(calendar.component(.month, from: term.start) == 2)
    }

    @Test func thePreviousHalfIsTheOneBefore() {
        let current = SchoolTerm.interval(containing: date(2027, 4, 10), calendar: calendar)
        let before = SchoolTerm.previous(of: current, calendar: calendar)
        #expect(SchoolTerm.number(of: before, calendar: calendar) == 1)
        #expect(calendar.component(.year, from: before.start) == 2026)
        let beforeThat = SchoolTerm.previous(of: before, calendar: calendar)
        #expect(SchoolTerm.number(of: beforeThat, calendar: calendar) == 2)
    }
}

struct WheelStepTests {
    @Test func theWheelCanGoInTenthsHalfTenthsOrHundredths() {
        let tenths = GradeScale.german.choices(stepHundredths: 10)
        #expect(tenths.count == 51 && tenths[1] == 1.1)
        let halves = GradeScale.german.choices(stepHundredths: 5)
        #expect(halves.count == 101)
        #expect(Array(halves.prefix(5)) == [1.0, 1.05, 1.1, 1.15, 1.2])
        let hundredths = GradeScale.german.choices(stepHundredths: 1)
        #expect(hundredths.count == 501 && hundredths[1] == 1.01 && hundredths.last == 6.0)
        #expect(GradeScale.tendency.choices(stepHundredths: 5).count == 16)
    }

    @Test func aGradeShowsOnlyAsManyPlacesAsItNeeds() {
        let scale = GradeScale.german
        #expect(scale.display(1.2) == "1,2")
        #expect(scale.display(1.15) == "1,15")
        #expect(scale.display(1.0) == "1,0")
        #expect(scale.display(2.37) == "2,37")
        #expect(scale.display(1.2, decimals: 2) == "1,20")
    }

    @Test func anExactTypedNumberIsKeptToTheHundredth() {
        #expect(GradeScale.german.normalized(1.37, decimals: 2) == 1.37)
        #expect(GradeScale.german.normalized(1.375, decimals: 2) == 1.38)
        #expect(GradeScale.german.normalized(1.37, decimals: 1) == 1.4)
    }

    @Test func theWheelStepIsLimitedAndOldSettingsKeepTheirFineness() throws {
        #expect(GradingSettings(wheelStep: 7).wheelStep == 10)
        #expect(GradingSettings(wheelStep: 3).wheelStep == 5)
        #expect(GradingSettings(wheelStep: 0).wheelStep == 1)
        #expect(GradingSettings().wheelStep == 10)
        #expect(GradingSettings(decimals: 2).wheelStep == 1)
        let old = try JSONDecoder().decode(GradingSettings.self, from: Data(##"{"scale":"german","decimals":2}"##.utf8))
        #expect(old.wheelStep == 1)
        let newer = GradingSettings(wheelStep: 5)
        let back = try JSONDecoder().decode(GradingSettings.self, from: JSONEncoder().encode(newer))
        #expect(back == newer)
    }
}
