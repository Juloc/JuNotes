import Foundation
import Testing
@testable import JuNotesCore

private func grade(_ value: Double, _ category: GradeCategory = .written, weight: Double? = nil, subject: Int = 1) -> Grade {
    Grade(id: UUID(), subjectID: uuid(subject), value: value, category: category, weight: weight, date: Date(timeIntervalSince1970: 1_800_000_000), title: "")
}

struct GradesTests {
    @Test func theAverageIsWeighted() {
        // Written counts twice: (2*2 + 3) / 3
        #expect(abs(GradeCalculator.average([grade(2, .written), grade(3, .oral)])! - 7.0 / 3.0) < 0.0001)
    }

    @Test func aSingleGradeIsItsOwnAverage() { #expect(GradeCalculator.average([grade(4, .test)]) == 4) }

    @Test func noGradesHaveNoAverage() {
        #expect(GradeCalculator.average([]) == nil)
        #expect(GradeCalculator.average([grade(2, .other, weight: 0)]) == nil)
    }

    @Test func aSingleGradeCanCountThreeTimes() {
        // (1*3 + 4*1) / 4
        #expect(GradeCalculator.average([grade(1, .written, weight: 3), grade(4, .oral)]) == 1.75)
    }

    @Test func theWeightsAreHers() {
        let equal = GradeWeights(written: 1, oral: 1, test: 1, other: 1)
        #expect(GradeCalculator.average([grade(2, .written), grade(4, .oral)], weights: equal) == 3)
    }

    @Test func theOverallAverageCountsEachSubjectOnce() {
        let math = [grade(1), grade(1), grade(1)]
        let german = [grade(4, subject: 2)]
        #expect(GradeCalculator.overall([math, german]) == 2.5)
        #expect(GradeCalculator.overall([[], []]) == nil)
    }

    @Test func formattingUsesADecimalCommaAndRoundsTheUsualWay() {
        #expect(GradeCalculator.format(2.34) == "2,3")
        #expect(GradeCalculator.format(2.35) == "2,4")
        #expect(GradeCalculator.format(2) == "2,0")
        #expect(GradeCalculator.format(nil) == "–")
        #expect(GradeCalculator.format(2.345, decimals: 2) == "2,35")
    }

    @Test func whatSheNeedsForATargetOnTheGermanScale() {
        // Now (2*2 + 3) / 3 = 2,33. Weight of the next Klassenarbeit: 2. Target 2,0: (2*(3+2) - 7)/2 = 1,5
        let need = GradeCalculator.needed(forTarget: 2, grades: [grade(2, .written), grade(3, .oral)], nextWeight: 2, scale: .german)

        #expect(need == .achievable(1.5))
    }

    @Test func aTargetAlreadyReachedForSure() {
        #expect(GradeCalculator.needed(forTarget: 5, grades: [grade(1, .written), grade(1, .written)], nextWeight: 1, scale: .german) == .alreadySafe)
    }

    @Test func aTargetThatCannotBeReached() {
        #expect(GradeCalculator.needed(forTarget: 1.0, grades: [grade(5, .written), grade(5, .written)], nextWeight: 1, scale: .german) == .impossible)
    }

    @Test func noGradesGiveNoAdvice() {
        #expect(GradeCalculator.needed(forTarget: 2, grades: [], nextWeight: 1, scale: .german) == .noData)
    }

    @Test func pointsAreWholeAndHigherIsBetter() {
        // (10*2 + 12*2 + x*2) / 6 = 11  ->  x = 11*6 - 44 = 22 /2 ... with total weight 4 + 2: x = (11*6 - 44)/2 = 11
        let grades = [grade(10, .written), grade(12, .written)]
        #expect(GradeCalculator.needed(forTarget: 11, grades: grades, nextWeight: 2, scale: .points) == .achievable(11))
        #expect(GradeCalculator.needed(forTarget: 14.9, grades: grades, nextWeight: 2, scale: .points) == .impossible)
        #expect(GradeCalculator.needed(forTarget: 5, grades: grades, nextWeight: 2, scale: .points) == .alreadySafe)
    }

    @Test func aGradeSurvivesTheWireWithItsSubjectAsParent() throws {
        let original = Grade(id: uuid(7), subjectID: uuid(1), value: 2.3, category: .test, weight: 0.5, date: Date(timeIntervalSince1970: 1_800_000_000), title: "Vokabeln")
        let change = try original.change()

        #expect(change.kind == .grade)
        #expect(change.parentID == uuid(1))
        #expect(try Grade.decode(SyncRecord(kind: .grade, id: uuid(7), parentID: uuid(1), payload: change.payload, seq: 1)) == original)
    }
}
