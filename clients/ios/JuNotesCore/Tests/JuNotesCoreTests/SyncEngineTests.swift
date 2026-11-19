import Foundation
import Testing
@testable import JuNotesCore

struct SyncEngineTests {
    let server = InMemoryServer()

    func engine(_ device: InMemoryDevice, transport: (any SyncTransport)? = nil, batchSize: Int = 200) -> SyncEngine {
        SyncEngine(transport: transport ?? server, store: device, batchSize: batchSize, now: { Date(timeIntervalSince1970: 1_800_000_000) })
    }

    @Test func twoDevicesEndUpWithTheSameStrokes() async {
        let ipad = InMemoryDevice(), other = InMemoryDevice()
        await ipad.write(notebook("Mathe"))
        await ipad.write(stroke(1))
        await ipad.write(stroke(2))
        await other.write(stroke(3))

        _ = await engine(ipad).sync()
        _ = await engine(other).sync()
        _ = await engine(ipad).sync()

        #expect(await ipad.activeStrokeIDs() == [uuid(101), uuid(102), uuid(103)])
        #expect(await other.activeStrokeIDs() == [uuid(101), uuid(102), uuid(103)])
        #expect(await other.entity(.notebook, uuid(50))?.payload == .object(["title": .string("Mathe")]))
    }

    @Test func aDeletedStrokeDisappearsOnTheOtherDevice() async {
        let ipad = InMemoryDevice(), other = InMemoryDevice()
        await ipad.write(stroke(1))
        _ = await engine(ipad).sync()
        _ = await engine(other).sync()
        #expect(await other.activeStrokeIDs() == [uuid(101)])

        await ipad.write(stroke(1, deleted: true))
        _ = await engine(ipad).sync()
        _ = await engine(other).sync()

        #expect(await other.activeStrokeIDs().isEmpty)
    }

    @Test func theLastNotebookTitleWins() async {
        let ipad = InMemoryDevice(), other = InMemoryDevice()
        await ipad.write(notebook("Mathe"))
        _ = await engine(ipad).sync()
        await other.write(notebook("Mathematik"))
        _ = await engine(other).sync()
        _ = await engine(ipad).sync()

        #expect(await ipad.entity(.notebook, uuid(50))?.payload == .object(["title": .string("Mathematik")]))
    }

    @Test func aSyncReportsWhatItSentAndFetched() async throws {
        let ipad = InMemoryDevice()
        await ipad.write(stroke(1))
        await ipad.write(stroke(2))

        let result = await engine(ipad).sync()

        // The two strokes come back in the pull, which is how the device learns the server has them.
        #expect(result == .synced(pushed: 2, pulled: 2, at: Date(timeIntervalSince1970: 1_800_000_000)))
        #expect(try await ipad.pendingCount() == 0)
    }

    @Test func aSecondSyncWithNothingNewDoesNothing() async {
        let ipad = InMemoryDevice()
        await ipad.write(stroke(1))
        let sync = engine(ipad)
        _ = await sync.sync()

        let result = await sync.sync()

        #expect(result == .synced(pushed: 0, pulled: 0, at: Date(timeIntervalSince1970: 1_800_000_000)))
    }

    @Test func pushesGoOutInBatches() async {
        let ipad = InMemoryDevice()
        for n in 1...5 { await ipad.write(stroke(n)) }

        let result = await engine(ipad, batchSize: 2).sync()

        guard case .synced(let pushed, let pulled, _) = result else { Issue.record("not synced: \(result)"); return }
        #expect(pushed == 5)
        #expect(pulled == 5)
        #expect(await server.pushCalls == 3)
        #expect(await server.entities.count == 5)
    }

    @Test func nothingLeavesTheOutboxWhenThePushFails() async throws {
        let ipad = InMemoryDevice()
        await ipad.write(stroke(1))
        let flaky = FlakyTransport(server)
        await flaky.fail(with: TransportError.noNetwork)

        let result = await engine(ipad, transport: flaky).sync()

        #expect(result == .offline)
        #expect(try await ipad.pendingCount() == 1)

        await flaky.fail(with: nil)
        _ = await engine(ipad, transport: flaky).sync()
        #expect(try await ipad.pendingCount() == 0)
        #expect(await server.entities.count == 1)
    }

    @Test(arguments: [
        (TransportError.serverUnreachable, SyncResult.serverUnreachable),
        (TransportError.unauthorized, SyncResult.unauthorized),
        (TransportError.rejected("zu groß"), SyncResult.rejected("zu groß")),
    ])
    func transportErrorsBecomeResults(error: TransportError, expected: SyncResult) async {
        let ipad = InMemoryDevice()
        await ipad.write(stroke(1))
        let flaky = FlakyTransport(server)
        await flaky.fail(with: error)

        #expect(await engine(ipad, transport: flaky).sync() == expected)
    }

    @Test func anUnknownTransportFailureCountsAsAnUnreachableServer() async {
        struct Strange: Error {}
        let ipad = InMemoryDevice()
        await ipad.write(stroke(1))
        let flaky = FlakyTransport(server)
        await flaky.fail(with: Strange())

        #expect(await engine(ipad, transport: flaky).sync() == .serverUnreachable)
    }

    @Test func aFailingLocalDatabaseIsReportedAsThat() async {
        let ipad = InMemoryDevice()
        await ipad.setFailApply(true)
        await ipad.write(stroke(1))

        let result = await engine(ipad).sync()

        guard case .storeFailed = result else { Issue.record("expected storeFailed, got \(result)"); return }
    }

    @Test func twoSyncsAtOnceRunOnlyOnce() async {
        let ipad = InMemoryDevice()
        await ipad.write(stroke(1))
        await server.setDelay(.milliseconds(150))
        let sync = engine(ipad)

        async let first = sync.sync()
        async let second = sync.sync()
        let results = await [first, second]

        #expect(results.contains(.alreadyRunning))
        #expect(results.contains { if case .synced = $0 { true } else { false } })
    }
}
