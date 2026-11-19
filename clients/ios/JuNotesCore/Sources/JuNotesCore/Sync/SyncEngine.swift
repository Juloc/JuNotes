import Foundation

public enum SyncResult: Sendable, Equatable {
    case synced(pushed: Int, pulled: Int, at: Date)
    case offline
    case serverUnreachable
    case unauthorized
    case rejected(String)
    /// The local database failed; the work on the device is the problem here, not the network.
    case storeFailed(String)
    case alreadyRunning
}

/// Sends what is waiting, then fetches what is new, in batches, and never runs twice at once.
public actor SyncEngine {
    public static let defaultBatchSize = 200

    private let transport: any SyncTransport
    private let store: any SyncStore
    private let batchSize: Int
    private let now: @Sendable () -> Date
    private var running = false

    public init(transport: any SyncTransport, store: any SyncStore, batchSize: Int = SyncEngine.defaultBatchSize, now: @escaping @Sendable () -> Date = { Date() }) {
        self.transport = transport
        self.store = store
        self.batchSize = batchSize
        self.now = now
    }

    public func sync() async -> SyncResult {
        guard !running else { return .alreadyRunning }
        running = true
        defer { running = false }

        var pushed = 0
        var pulled = 0
        do {
            while true {
                let pending = try await store.pendingChanges(limit: batchSize)
                if pending.isEmpty { break }
                _ = try await push(pending)
                try await store.markPushed(pending.map(\.outboxID))
                pushed += pending.count
            }

            while true {
                let cursor = try await store.cursor()
                let page = try await pull(since: cursor)
                if !page.changes.isEmpty || page.cursor != cursor {
                    try await store.apply(page.changes, cursor: page.cursor)
                }
                pulled += page.changes.count
                if !page.more { break }
            }
            return .synced(pushed: pushed, pulled: pulled, at: now())
        } catch let error as TransportError {
            switch error {
            case .noNetwork: return .offline
            case .serverUnreachable: return .serverUnreachable
            case .unauthorized: return .unauthorized
            case .rejected(let message): return .rejected(message)
            }
        } catch {
            return .storeFailed(String(describing: error))
        }
    }

    // Transport failures stay transport errors; anything else from the transport means the server answered badly.
    private func push(_ pending: [PendingChange]) async throws -> PushResponse {
        try await wrap { try await self.transport.push(pending.map(\.change)) }
    }

    private func pull(since cursor: Int64) async throws -> PullResponse {
        try await wrap { try await self.transport.pull(since: cursor, limit: self.batchSize) }
    }

    private func wrap<T: Sendable>(_ call: () async throws -> T) async throws -> T {
        do {
            return try await call()
        } catch let error as TransportError {
            throw error
        } catch {
            throw TransportError.serverUnreachable
        }
    }
}

extension SyncSnapshot {
    /// Folds the outcome of a sync run into what the status line shows. `pending` is what still waits in the outbox.
    public mutating func record(_ result: SyncResult, pending: Int) {
        pendingChanges = pending
        switch result {
        case .synced(_, _, let at):
            lastSyncedAt = at
            connection = .online
            localWriteFailed = false
        case .offline:
            connection = .noNetwork
        case .serverUnreachable, .unauthorized, .rejected:
            connection = .serverUnreachable
        case .storeFailed:
            localWriteFailed = true
        case .alreadyRunning:
            break
        }
    }
}
