import Foundation
import Testing
@testable import JuNotesCore

struct ModelTests {
    let created = Date(timeIntervalSince1970: 1_800_000_000)

    @Test func aSubjectSurvivesTheWire() throws {
        let subject = Subject(id: uuid(1), name: "Mathe", color: "#0a84ff", symbol: "function", sort: 2)
        let change = try subject.change()

        #expect(change.kind == .subject)
        #expect(change.parentID == nil)
        #expect(!change.deleted)
        let record = SyncRecord(kind: .subject, id: uuid(1), payload: change.payload, seq: 1)
        #expect(try Subject.decode(record) == subject)
    }

    @Test func theIdComesFromTheRecordNotThePayload() throws {
        let change = try Subject(id: uuid(1), name: "Mathe").change()
        let record = SyncRecord(kind: .subject, id: uuid(9), payload: change.payload, seq: 1)

        #expect(try Subject.decode(record).id == uuid(9))
    }

    @Test func aNotebookKeepsItsSubjectAsParent() throws {
        let notebook = Notebook(id: uuid(2), subjectID: uuid(1), title: "Bruchrechnung", type: .pages, template: .grid, createdAt: created)
        let change = try notebook.change()

        #expect(change.parentID == uuid(1))
        let record = SyncRecord(kind: .notebook, id: uuid(2), parentID: uuid(1), payload: change.payload, seq: 1)
        #expect(try Notebook.decode(record) == notebook)
    }

    @Test func aNotebookWithoutASubjectHasNoParent() throws {
        let notebook = Notebook(id: uuid(2), title: "Lose Blätter", createdAt: created)
        #expect(try notebook.change().parentID == nil)
    }

    @Test func aPageSurvivesTheWireIncludingScanContent() throws {
        let page = Page(id: uuid(3), notebookID: uuid(2), sort: "i", template: .ruled, content: .scan(assetID: uuid(77)), autoCreated: true)
        let change = try page.change()

        #expect(change.parentID == uuid(2))
        let record = SyncRecord(kind: .page, id: uuid(3), parentID: uuid(2), payload: change.payload, seq: 1)
        #expect(try Page.decode(record) == page)
    }

    @Test func pdfContentKeepsItsPageIndex() throws {
        let page = Page(id: uuid(3), notebookID: uuid(2), sort: "i", content: .pdf(assetID: uuid(5), pageIndex: 4))
        let record = SyncRecord(kind: .page, id: uuid(3), parentID: uuid(2), payload: try page.change().payload, seq: 1)

        #expect(try Page.decode(record).content == .pdf(assetID: uuid(5), pageIndex: 4))
    }

    @Test func aStrokeBelongsToAPage() throws {
        let stroke = Stroke(id: uuid(101), tool: .marker, color: "#ffcc00", width: 12, points: [StrokePoint(x: 1, y: 2, pressure: 0.5)])
        let change = try stroke.change(pageID: uuid(3))

        #expect(change.parentID == uuid(3))
        let record = SyncRecord(kind: .stroke, id: uuid(101), parentID: uuid(3), payload: change.payload, seq: 1)
        let decoded = try Stroke.decode(record)
        #expect(decoded.stroke == stroke)
        #expect(decoded.pageID == uuid(3))
        #expect(stroke.deletion(pageID: uuid(3)).deleted)
    }

    @Test func deletionHasNoPayload() {
        let change = Subject(id: uuid(1), name: "Mathe").deletion()
        #expect(change.deleted)
        #expect(change.payload == nil)
    }

    @Test func decodingTheWrongKindOrADeletionFails() throws {
        let change = try Subject(name: "Mathe").change()
        #expect(throws: EntityCoding.Failure.notDecodable(.page)) {
            try Subject.decode(SyncRecord(kind: .page, id: uuid(1), payload: change.payload, seq: 1))
        }
        #expect(throws: EntityCoding.Failure.notDecodable(.subject)) {
            try Subject.decode(SyncRecord(kind: .subject, id: uuid(1), payload: nil, deleted: true, seq: 1))
        }
    }

    @Test func missingOptionalFieldsGetDefaults() throws {
        let record = SyncRecord(kind: .notebook, id: uuid(2), payload: .object(["title": .string("Alt"), "createdAt": .string("2026-11-19T10:00:00Z")]), seq: 1)
        let notebook = try Notebook.decode(record)

        #expect(notebook.type == .pages)
        #expect(notebook.template == .ruled)
        #expect(notebook.trashedAt == nil)
    }
}

struct PageHeadingTests {
    @Test func aPageKeepsItsHeadingOnTheWire() throws {
        let page = Page(id: uuid(3), notebookID: uuid(2), sort: "i", heading: "Do 19.11. · Mathe")
        let record = SyncRecord(kind: .page, id: uuid(3), parentID: uuid(2), payload: try page.change().payload, seq: 1)

        #expect(try Page.decode(record).heading == "Do 19.11. · Mathe")
    }

    @Test func aPageWithoutOneHasNone() throws {
        let record = SyncRecord(kind: .page, id: uuid(3), parentID: uuid(2), payload: .object(["sort": .string("i")]), seq: 1)
        #expect(try Page.decode(record).heading == nil)
    }
}

struct PageLearningFieldsTests {
    @Test func printedTextSolutionAndAdditionsSurviveTheWire() throws {
        let page = Page(id: uuid(3), notebookID: uuid(2), sort: "i", heading: "Probetest", printedText: "1) 1/2 + 1/3 = ?", solution: "5/6", additions: ["Beim Kürzen den ggT nutzen."])
        let record = SyncRecord(kind: .page, id: uuid(3), parentID: uuid(2), payload: try page.change().payload, seq: 1)

        let decoded = try Page.decode(record)

        #expect(decoded == page)
        #expect(decoded.additions == ["Beim Kürzen den ggT nutzen."])
    }

    @Test func anOlderPageWithoutThemStillDecodes() throws {
        let record = SyncRecord(kind: .page, id: uuid(3), parentID: uuid(2), payload: .object(["sort": .string("i")]), seq: 1)
        let page = try Page.decode(record)

        #expect(page.printedText == nil)
        #expect(page.solution == nil)
        #expect(page.additions.isEmpty)
    }

    @Test func aStudyPlanCanBeKeptAndRead() throws {
        let plan = StudyPlan(days: [StudyDay(date: Date(timeIntervalSince1970: 1_800_000_000), tasks: [StudyTask(kind: .learn, topic: "Brüche", minutes: 25)])], unscheduled: [StudyTask(kind: .mockTest, minutes: 40)])

        let data = try JSONEncoder().encode(plan)

        #expect(try JSONDecoder().decode(StudyPlan.self, from: data) == plan)
    }
}
