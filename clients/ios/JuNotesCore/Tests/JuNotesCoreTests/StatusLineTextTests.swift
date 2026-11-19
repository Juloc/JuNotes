import Foundation
import Testing
@testable import JuNotesCore

struct StatusLineTextTests {
    static let calendar: Calendar = {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC")!
        return calendar
    }()

    // 19 Nov 2026, 15:30 UTC
    let now = Date(timeIntervalSince1970: 1_795_102_200)
    var text: StatusLineText { StatusLineText(now: now, calendar: Self.calendar) }

    @Test func savedStatesAreWorded() {
        #expect(text.saved(.saved) == "Auf iPad gespeichert")
        #expect(text.saved(.failed) == "Speichern auf dem iPad fehlgeschlagen")
    }

    @Test func todayYesterdayAndOlderDays() {
        #expect(text.sync(.synced(at: now - 3600)) == "Synchronisiert heute 14:30")
        #expect(text.sync(.synced(at: now - 86400)) == "Synchronisiert gestern 15:30")
        #expect(text.backup(.done(at: now - 5 * 86400)) == "Backup 14.11. 15:30")
    }

    @Test func pendingChangesUseTheRightNumberWord() {
        #expect(text.sync(.pending(count: 1)) == "Synchronisiere 1 Änderung")
        #expect(text.sync(.pending(count: 4)) == "Synchronisiere 4 Änderungen")
    }

    @Test func offlineAndUnreachableSayTheWorkIsSafe() {
        #expect(text.sync(.waitingForNetwork(pending: 2)) == "Wartet auf WLAN, 2 Änderungen sicher auf dem iPad")
        #expect(text.sync(.waitingForNetwork(pending: 0)) == "Offline, alles ist sicher auf dem iPad")
        #expect(text.sync(.serverUnreachable(pending: 3)) == "Server nicht erreichbar, alles ist sicher auf dem iPad")
    }

    @Test func backupStates() {
        #expect(text.backup(.never) == "Noch kein Backup")
        #expect(text.backup(.overdue(since: now - 3 * 86400)) == "Backup überfällig, zuletzt 16.11. 15:30")
    }
}
