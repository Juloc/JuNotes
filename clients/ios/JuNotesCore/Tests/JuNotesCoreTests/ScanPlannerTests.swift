import Foundation
import Testing
@testable import JuNotesCore

struct ScanPlannerTests {
    let created = Date(timeIntervalSince1970: 1_800_000_000)
    let math = Subject(id: uuid(1), name: "Mathe")

    func notebook(_ n: Int, _ title: String, type: NotebookKind = .pages, trashed: Bool = false) -> Notebook {
        Notebook(id: uuid(n), subjectID: uuid(1), title: title, type: type, createdAt: created + Double(n), trashedAt: trashed ? created : nil)
    }

    @Test func insideANotebookItIsTheFirstChoice() {
        let open = notebook(2, "Brüche")
        let options = ScanPlanner.options(ScanContext(openNotebook: open, suggestedSubject: math, notebooksOfSuggestedSubject: [open]))

        #expect(options.first == .insertHere(open))
        #expect(options.last == .choose)
    }

    @Test func thePlannedLessonsNewestNotebookComesNextWhenElsewhere() {
        let old = notebook(2, "Alt"), new = notebook(3, "Neu")
        let options = ScanPlanner.options(ScanContext(openNotebook: nil, suggestedSubject: math, notebooksOfSuggestedSubject: [old, new]))

        #expect(options == [.newestNotebook(math, new), .newWorksheet(math), .choose])
    }

    @Test func theOpenNotebookIsNotOfferedTwice() {
        let open = notebook(3, "Neu")
        let options = ScanPlanner.options(ScanContext(openNotebook: open, suggestedSubject: math, notebooksOfSuggestedSubject: [open]))

        #expect(options == [.insertHere(open), .newWorksheet(math), .choose])
    }

    @Test func whiteboardsAndTrashedNotebooksAreNeverTheTarget() {
        let board = notebook(2, "Board", type: .whiteboard), trashed = notebook(3, "Weg", trashed: true), good = notebook(1, "Gut")
        let options = ScanPlanner.options(ScanContext(openNotebook: board, suggestedSubject: math, notebooksOfSuggestedSubject: [good, board, trashed]))

        #expect(options.first == .newestNotebook(math, good))
        #expect(!options.contains(.insertHere(board)))
    }

    @Test func withoutAnyHintAWorksheetWithoutASubjectIsOffered() {
        #expect(ScanPlanner.options(ScanContext()) == [.newWorksheet(nil), .choose])
        #expect(ScanPlanner.defaultOption(ScanContext()) == .newWorksheet(nil))
    }

    @Test func aWorksheetIsNamedByTheDay() {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
        let day = calendar.date(from: DateComponents(year: 2026, month: 11, day: 19, hour: 8))!

        #expect(ScanPlanner.worksheetTitle(date: day, calendar: calendar) == "Arbeitsblatt 19.11.")
    }

    @Test func scannedPagesGetKeysInOrderBetweenTheNeighbours() {
        let keys = ScanPlanner.sortKeys(count: 5, after: "i", before: "j")

        #expect(keys.count == 5)
        #expect(keys == keys.sorted())
        #expect(keys.allSatisfy { "i" < $0 && $0 < "j" })
    }
}
