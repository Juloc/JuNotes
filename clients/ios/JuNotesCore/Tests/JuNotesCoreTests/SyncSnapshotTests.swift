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

    @Test func aRefusedServerShowsAsUnreachable() {
        for result in [SyncResult.serverUnreachable, .rejected("x")] {
            var snapshot = SyncSnapshot()
            snapshot.record(result, pending: 1)
            #expect(StatusLine(snapshot, now: now).sync == .serverUnreachable(pending: 1))
        }
    }

    /// A key the server no longer knows (device removed, person blocked) asks her to sign in again, and the work stays safe on the iPad.
    @Test func anUnknownKeyAsksToSignInAgain() {
        var snapshot = SyncSnapshot()
        snapshot.record(.unauthorized, pending: 2)
        let line = StatusLine(snapshot, now: now)
        #expect(line.sync == .signedOut(pending: 2))
        #expect(line.syncLevel == .problem)
        #expect(line.isWorkSafe)
        #expect(StatusLineText(now: now).sync(line.sync) == "Abgemeldet, bitte in den Einstellungen neu anmelden. Alles ist sicher auf dem iPad")

        snapshot.record(.synced(pushed: 2, pulled: 0, at: now), pending: 0)
        #expect(StatusLine(snapshot, now: now).sync == .synced(at: now))
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
