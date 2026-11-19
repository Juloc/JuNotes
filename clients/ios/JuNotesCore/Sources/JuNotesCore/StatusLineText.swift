import Foundation

/// German wording for the status line. The states stay language-free in `StatusLine`; this is the one place that turns them into words.
public struct StatusLineText: Sendable {
    public var calendar: Calendar
    public var now: Date

    public init(now: Date, calendar: Calendar = .current) {
        self.now = now
        self.calendar = calendar
    }

    public func saved(_ state: SavedState) -> String {
        switch state {
        case .saved: "Auf iPad gespeichert"
        case .failed: "Speichern auf dem iPad fehlgeschlagen"
        }
    }

    public func sync(_ state: SyncState) -> String {
        switch state {
        case .synced(let at): "Synchronisiert \(day(at))"
        case .pending(let count): "Synchronisiere \(changes(count))"
        case .waitingForNetwork(let pending): pending > 0 ? "Wartet auf WLAN, \(changes(pending)) sicher auf dem iPad" : "Offline, alles ist sicher auf dem iPad"
        case .serverUnreachable: "Server nicht erreichbar, alles ist sicher auf dem iPad"
        case .never: "Noch nicht synchronisiert"
        case .notConfigured: "Server noch nicht eingerichtet, alles liegt auf dem iPad"
        }
    }

    public func backup(_ state: BackupState) -> String {
        switch state {
        case .done(let at): "Backup \(day(at))"
        case .overdue(let since): "Backup überfällig, zuletzt \(day(since))"
        case .never: "Noch kein Backup"
        }
    }

    /// "14:02" today, "gestern 14:02", otherwise "12.11. 14:02".
    func day(_ date: Date) -> String {
        let time = String(format: "%02d:%02d", calendar.component(.hour, from: date), calendar.component(.minute, from: date))
        let days = calendar.dateComponents([.day], from: calendar.startOfDay(for: date), to: calendar.startOfDay(for: now)).day ?? 0
        switch days {
        case 0: return "heute \(time)"
        case 1: return "gestern \(time)"
        default: return String(format: "%d.%d. ", calendar.component(.day, from: date), calendar.component(.month, from: date)) + time
        }
    }

    private func changes(_ count: Int) -> String { count == 1 ? "1 Änderung" : "\(count) Änderungen" }
}
