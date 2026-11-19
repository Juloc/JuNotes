import Foundation
import Testing
@testable import JuNotesCore

struct StatusLineTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)

    @Test func everythingDoneIsAllGreen() {
        let line = StatusLine(SyncSnapshot(lastSyncedAt: now - 60, lastBackupAt: now - 3600), now: now)
        #expect(line.saved == .saved)
        #expect(line.sync == .synced(at: now - 60))
        #expect(line.backup == .done(at: now - 3600))
        #expect(line.overall == .ok)
    }

    @Test func pendingChangesWaitWhileOnline() {
        let line = StatusLine(SyncSnapshot(pendingChanges: 3, lastSyncedAt: now - 60, lastBackupAt: now), now: now)
        #expect(line.sync == .pending(count: 3))
        #expect(line.syncLevel == .waiting)
    }

    @Test func noNetworkIsWaitingNotAProblem() {
        let line = StatusLine(SyncSnapshot(pendingChanges: 5, lastBackupAt: now, connection: .noNetwork), now: now)
        #expect(line.sync == .waitingForNetwork(pending: 5))
        #expect(line.overall == .waiting)
        #expect(line.isWorkSafe)
    }

    @Test func anUnreachableServerIsAProblemButTheWorkStaysSafe() {
        let line = StatusLine(SyncSnapshot(pendingChanges: 1, lastBackupAt: now, connection: .serverUnreachable), now: now)
        #expect(line.sync == .serverUnreachable(pending: 1))
        #expect(line.overall == .problem)
        #expect(line.isWorkSafe)
    }

    @Test func aFailedLocalWriteIsTheWorstCase() {
        let line = StatusLine(SyncSnapshot(localWriteFailed: true, lastSyncedAt: now, lastBackupAt: now), now: now)
        #expect(line.saved == .failed)
        #expect(!line.isWorkSafe)
        #expect(line.overall == .problem)
    }

    @Test func aBackupOlderThanThirtySixHoursIsOverdue() {
        let old = now - 37 * 3600
        #expect(StatusLine(SyncSnapshot(lastSyncedAt: now, lastBackupAt: old), now: now).backup == .overdue(since: old))
        let recent = now - 35 * 3600
        #expect(StatusLine(SyncSnapshot(lastSyncedAt: now, lastBackupAt: recent), now: now).backup == .done(at: recent))
    }

    @Test func aFreshInstallHasNeverSyncedOrBackedUp() {
        let line = StatusLine(SyncSnapshot(), now: now)
        #expect(line.sync == .never)
        #expect(line.backup == .never)
        #expect(line.overall == .waiting)
    }
}

struct StatusLineSetupTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)

    @Test func withoutAServerTheLineSaysSoAndTheWorkIsSafe() {
        let line = StatusLine(SyncSnapshot(isConfigured: false, pendingChanges: 4, lastBackupAt: nil, connection: .online), now: now)

        #expect(line.sync == .notConfigured)
        #expect(line.syncLevel == .waiting)
        #expect(line.isWorkSafe)
        #expect(StatusLineText(now: now).sync(.notConfigured) == "Server noch nicht eingerichtet, alles liegt auf dem iPad")
    }

    @Test func pageGeometryDependsOnTheNotebookKind() {
        #expect(PageGeometry.size(for: .pages) == (595, 842))
        #expect(PageGeometry.size(for: .whiteboard) == (3000, 2000))
    }
}
