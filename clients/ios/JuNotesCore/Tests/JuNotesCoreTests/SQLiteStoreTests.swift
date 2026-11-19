#if canImport(SQLite3)
import Foundation
import Testing
@testable import JuNotesCore

struct SQLiteStoreTests {
    let created = Date(timeIntervalSince1970: 1_800_000_000)

    func store() throws -> SQLiteStore { try SQLiteStore(path: ":memory:") }

    @Test func aLocalWriteShowsAtOnceAndWaitsInTheOutbox() async throws {
        let store = try store()
        try await store.put(Subject(id: uuid(1), name: "Mathe"))

        #expect(try await store.subjects().map(\.name) == ["Mathe"])
        #expect(try await store.pendingCount() == 1)
        let pending = try await store.pendingChanges(limit: 10)
        #expect(pending.first?.change.kind == .subject)
    }

    @Test func markingPushedEmptiesTheOutbox() async throws {
        let store = try store()
        try await store.put(Subject(id: uuid(1), name: "Mathe"))
        let pending = try await store.pendingChanges(limit: 10)

        try await store.markPushed(pending.map(\.outboxID))

        #expect(try await store.pendingCount() == 0)
        #expect(try await store.subjects().count == 1)
    }

    @Test func theTreeComesBackInOrder() async throws {
        let store = try store()
        try await store.put(Subject(id: uuid(1), name: "Mathe"))
        try await store.put(Notebook(id: uuid(2), subjectID: uuid(1), title: "Brüche", createdAt: created))
        try await store.put(Notebook(id: uuid(3), subjectID: uuid(1), title: "Gleichungen", createdAt: created + 10))
        try await store.put(Page(id: uuid(10), notebookID: uuid(2), sort: "j"))
        try await store.put(Page(id: uuid(11), notebookID: uuid(2), sort: "b"))

        #expect(try await store.notebooks(subjectID: uuid(1)).map(\.title) == ["Brüche", "Gleichungen"])
        #expect(try await store.pages(notebookID: uuid(2)).map(\.id) == [uuid(11), uuid(10)])
        #expect(try await store.notebooks(subjectID: uuid(99)).isEmpty)
    }

    @Test func trashedNotebooksAreHiddenButListed() async throws {
        let store = try store()
        let notebook = Notebook(id: uuid(2), subjectID: uuid(1), title: "Alt", createdAt: created)
        try await store.put(notebook)
        try await store.put(notebook.movedToTrash(at: created))

        #expect(try await store.notebooks(subjectID: uuid(1)).isEmpty)
        #expect(try await store.notebooks(subjectID: uuid(1), includeTrashed: true).count == 1)
        #expect(try await store.trashedNotebooks().map(\.title) == ["Alt"])
    }

    @Test func strokesAreAddedAndDeleted() async throws {
        let store = try store()
        let first = Stroke(id: uuid(101), points: [StrokePoint(x: 1, y: 2)])
        let second = Stroke(id: uuid(102), points: [StrokePoint(x: 3, y: 4)])
        try await store.put(first, on: uuid(10))
        try await store.put(second, on: uuid(10))
        try await store.remove(first, on: uuid(10))

        #expect(try await store.strokes(pageID: uuid(10)).map(\.id) == [uuid(102)])
    }

    @Test func aStrokeDeletedHereStaysDeletedWhenTheServerSendsItAgain() async throws {
        let store = try store()
        let stroke = Stroke(id: uuid(101), points: [StrokePoint(x: 1, y: 2)])
        try await store.put(stroke, on: uuid(10))
        try await store.remove(stroke, on: uuid(10))
        let record = SyncRecord(kind: .stroke, id: uuid(101), parentID: uuid(10), payload: try stroke.change(pageID: uuid(10)).payload, seq: 5)

        try await store.apply([record], cursor: 5)

        #expect(try await store.strokes(pageID: uuid(10)).isEmpty)
    }

    @Test func aDeletionFromTheServerRemovesAStroke() async throws {
        let store = try store()
        let stroke = Stroke(id: uuid(101), points: [StrokePoint(x: 1, y: 2)])
        try await store.apply([SyncRecord(kind: .stroke, id: uuid(101), parentID: uuid(10), payload: try stroke.change(pageID: uuid(10)).payload, seq: 1)], cursor: 1)
        try await store.apply([SyncRecord(kind: .stroke, id: uuid(101), parentID: uuid(10), payload: nil, deleted: true, seq: 2)], cursor: 2)

        #expect(try await store.strokes(pageID: uuid(10)).isEmpty)
    }

    @Test func applyingRecordsMovesTheCursorWithThem() async throws {
        let store = try store()
        #expect(try await store.cursor() == 0)
        let change = try Subject(id: uuid(1), name: "Mathe").change()

        try await store.apply([SyncRecord(kind: .subject, id: uuid(1), payload: change.payload, seq: 7)], cursor: 7)

        #expect(try await store.cursor() == 7)
        #expect(try await store.subjects().count == 1)
        #expect(try await store.pendingCount() == 0)
    }

    @Test func aWaitingLocalEditIsNotOverwrittenByAnOlderServerVersion() async throws {
        let store = try store()
        let old = try Subject(id: uuid(1), name: "Mathe").change()
        try await store.apply([SyncRecord(kind: .subject, id: uuid(1), payload: old.payload, seq: 1)], cursor: 1)
        try await store.put(Subject(id: uuid(1), name: "Mathematik"))

        try await store.apply([SyncRecord(kind: .subject, id: uuid(1), payload: old.payload, seq: 2)], cursor: 2)

        #expect(try await store.subjects().map(\.name) == ["Mathematik"])
    }

    @Test func theStoreWorksAsTheSyncEnginesLocalSide() async throws {
        let server = InMemoryServer()
        let store = try store()
        try await store.put(Subject(id: uuid(1), name: "Mathe"))
        try await store.put(Stroke(id: uuid(101), points: [StrokePoint(x: 1, y: 2)]), on: uuid(10))

        let result = await SyncEngine(transport: server, store: store).sync()

        guard case .synced(let pushed, _, _) = result else { Issue.record("not synced: \(result)"); return }
        #expect(pushed == 2)
        #expect(try await store.pendingCount() == 0)
        #expect(await server.entities.count == 2)
        #expect(try await store.strokes(pageID: uuid(10)).count == 1)
    }

    @Test func searchFindsHandwritingByPrefixAndShowsTheMatch() async throws {
        let store = try store()
        try await store.index(pageID: uuid(10), notebookID: uuid(2), text: "Gleiche Nenner: nur die Zähler addieren")
        try await store.index(pageID: uuid(11), notebookID: uuid(3), text: "Photosynthese braucht Licht")

        let hits = try await store.search("nenn")

        #expect(hits.map(\.pageID) == [uuid(10)])
        #expect(hits.first?.snippet.contains("«Nenner»") == true)
        // Accents are ignored, so "zahler" finds "Zähler". Writing "ae" for "ä" is not matched.
        #expect(try await store.search("zahler").map(\.pageID) == [uuid(10)])
        #expect(try await store.search("   ").isEmpty)
    }

    @Test func indexingAPageAgainReplacesItsText() async throws {
        let store = try store()
        try await store.index(pageID: uuid(10), notebookID: uuid(2), text: "alt")
        try await store.index(pageID: uuid(10), notebookID: uuid(2), text: "neu")

        #expect(try await store.search("alt").isEmpty)
        #expect(try await store.search("neu").count == 1)
    }

    @Test func gradesComeBackPerSubjectNewestFirst() async throws {
        let store = try store()
        let old = Grade(id: uuid(1), subjectID: uuid(10), value: 3, date: created, title: "alt")
        let new = Grade(id: uuid(2), subjectID: uuid(10), value: 2, date: created + 100, title: "neu")
        let other = Grade(id: uuid(3), subjectID: uuid(11), value: 1, date: created)
        try await store.put(old)
        try await store.put(new)
        try await store.put(other)

        #expect(try await store.grades(subjectID: uuid(10)).map(\.title) == ["neu", "alt"])
        #expect(try await store.grades().count == 3)
    }

    @Test func singleEntitiesCanBeLookedUp() async throws {
        let store = try store()
        try await store.put(Subject(id: uuid(1), name: "Mathe"))
        let notebook = Notebook(id: uuid(2), subjectID: uuid(1), title: "Brüche", createdAt: created)
        try await store.put(notebook)
        try await store.put(Page(id: uuid(3), notebookID: uuid(2), sort: "i"))

        #expect(try await store.subject(id: uuid(1))?.name == "Mathe")
        #expect(try await store.notebook(id: uuid(2)) == notebook)
        #expect(try await store.page(id: uuid(3))?.notebookID == uuid(2))
        #expect(try await store.notebook(id: uuid(99)) == nil)
    }

    @Test func assetsAreSentOnceAndAllReferencedOnesAreListed() async throws {
        let store = try store()
        try await store.registerAsset(uuid(7))
        try await store.registerAsset(uuid(8))
        try await store.markAssetUploaded(uuid(7))
        try await store.registerAsset(uuid(7))

        #expect(try await store.pendingAssetUploads() == [uuid(8)])

        try await store.put(Page(id: uuid(3), notebookID: uuid(2), sort: "i", content: .scan(assetID: uuid(7))))
        try await store.put(Page(id: uuid(4), notebookID: uuid(2), sort: "j", content: .pdf(assetID: uuid(9), pageIndex: 2)))
        try await store.put(Page(id: uuid(5), notebookID: uuid(2), sort: "k"))

        #expect(Set(try await store.referencedAssetIDs()) == [uuid(7), uuid(9)])
    }
}
#endif
