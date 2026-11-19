import Foundation

/// How the app reaches the server right now.
public enum Connection: Sendable, Equatable {
    case online
    case noNetwork
    case serverUnreachable
}

/// Everything the status line needs to know; the app fills it from the local database and the network monitor.
public struct SyncSnapshot: Sendable, Equatable {
    /// False until the server address and token are entered.
    public var isConfigured: Bool
    public var localWriteFailed: Bool
    public var pendingChanges: Int
    public var lastSyncedAt: Date?
    public var lastBackupAt: Date?
    public var connection: Connection

    public init(isConfigured: Bool = true, localWriteFailed: Bool = false, pendingChanges: Int = 0, lastSyncedAt: Date? = nil, lastBackupAt: Date? = nil, connection: Connection = .online) {
        self.isConfigured = isConfigured
        self.localWriteFailed = localWriteFailed
        self.pendingChanges = pendingChanges
        self.lastSyncedAt = lastSyncedAt
        self.lastBackupAt = lastBackupAt
        self.connection = connection
    }
}

public enum Level: Sendable, Equatable {
    case ok
    case waiting
    case problem
}

public enum SavedState: Sendable, Equatable {
    case saved
    case failed
}

public enum SyncState: Sendable, Equatable {
    case notConfigured
    case synced(at: Date)
    case pending(count: Int)
    case waitingForNetwork(pending: Int)
    case serverUnreachable(pending: Int)
    case never
}

public enum BackupState: Sendable, Equatable {
    case done(at: Date)
    case overdue(since: Date)
    case never
}

/// The always-visible line: saved on the iPad, synced, backed up. Plain states, so the app can show them in any language.
public struct StatusLine: Sendable, Equatable {
    /// A nightly backup older than this counts as overdue.
    public static let backupGrace: TimeInterval = 36 * 3600

    public var saved: SavedState
    public var sync: SyncState
    public var backup: BackupState

    public init(_ snapshot: SyncSnapshot, now: Date) {
        saved = snapshot.localWriteFailed ? .failed : .saved

        switch snapshot.connection {
        case _ where !snapshot.isConfigured:
            sync = .notConfigured
        case .noNetwork:
            sync = .waitingForNetwork(pending: snapshot.pendingChanges)
        case .serverUnreachable:
            sync = .serverUnreachable(pending: snapshot.pendingChanges)
        case .online:
            if snapshot.pendingChanges > 0 {
                sync = .pending(count: snapshot.pendingChanges)
            } else if let at = snapshot.lastSyncedAt {
                sync = .synced(at: at)
            } else {
                sync = .never
            }
        }

        if let at = snapshot.lastBackupAt {
            backup = now.timeIntervalSince(at) > Self.backupGrace ? .overdue(since: at) : .done(at: at)
        } else {
            backup = .never
        }
    }

    public var savedLevel: Level { saved == .saved ? .ok : .problem }

    public var syncLevel: Level {
        switch sync {
        case .synced: .ok
        case .pending, .waitingForNetwork, .never, .notConfigured: .waiting
        case .serverUnreachable: .problem
        }
    }

    public var backupLevel: Level {
        switch backup {
        case .done: .ok
        case .never, .overdue: .waiting
        }
    }

    /// The worst of the three, for a single dot next to the line.
    public var overall: Level {
        let levels = [savedLevel, syncLevel, backupLevel]
        if levels.contains(.problem) { return .problem }
        if levels.contains(.waiting) { return .waiting }
        return .ok
    }

    /// Whether everything she wrote is safe on the device; the sync state never changes that.
    public var isWorkSafe: Bool { saved == .saved }
}
