import Foundation
import Testing
@testable import JuNotesCore

struct SyncSnapshotTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)

    @Test func aSuccessfulSyncClearsTheWaitingState() {
        var snapshot = SyncSnapshot(pendingChanges: 4, connection: .noNetwork)
        snapshot.record(.synced(pushed: 4, pulled: 0, at: now), pending: 0)

        #expect(snapshot.connection == .online)
        #expect(snapshot.lastSyncedAt == now)
        #expect(StatusLine(snapshot, now: now).sync == .synced(at: now))
    }

    @Test func offlineKeepsThePendingCount() {
        var snapshot = SyncSnapshot()
        snapshot.record(.offline, pending: 3)

        #expect(StatusLine(snapshot, now: now).sync == .waitingForNetwork(pending: 3))
    }

    @Test func aRefusedOrUnauthorizedServerShowsAsUnreachable() {
        for result in [SyncResult.serverUnreachable, .unauthorized, .rejected("x")] {
            var snapshot = SyncSnapshot()
            snapshot.record(result, pending: 1)
            #expect(StatusLine(snapshot, now: now).sync == .serverUnreachable(pending: 1))
        }
    }

    @Test func aLocalFailureIsTheWorstCaseAndClearsOnTheNextGoodSync() {
        var snapshot = SyncSnapshot()
        snapshot.record(.storeFailed("disk full"), pending: 1)
        #expect(!StatusLine(snapshot, now: now).isWorkSafe)

        snapshot.record(.synced(pushed: 1, pulled: 0, at: now), pending: 0)
        #expect(StatusLine(snapshot, now: now).isWorkSafe)
    }

    @Test func aRunThatDidNotStartChangesNothingButThePendingCount() {
        var snapshot = SyncSnapshot(lastSyncedAt: now, connection: .online)
        snapshot.record(.alreadyRunning, pending: 2)

        #expect(snapshot.connection == .online)
        #expect(snapshot.lastSyncedAt == now)
        #expect(snapshot.pendingChanges == 2)
    }
}
