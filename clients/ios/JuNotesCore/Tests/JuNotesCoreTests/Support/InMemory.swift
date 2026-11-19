import Foundation
@testable import JuNotesCore

struct EntityKey: Hashable, Sendable {
    var kind: EntityKind
    var id: UUID
}

/// The server's rules in memory: strokes are only added or deleted, everything else is last write wins, and an unchanged push makes no new change.
actor InMemoryServer: SyncTransport {
    private(set) var entities: [EntityKey: SyncRecord] = [:]
    private(set) var seq: Int64 = 0
    private(set) var pushCalls = 0
    var delay: Duration?

    func setDelay(_ value: Duration?) { delay = value }

    func push(_ changes: [SyncChange]) async throws -> PushResponse {
        if let delay { try await Task.sleep(for: delay) }
        pushCalls += 1
        for change in changes {
            let key = EntityKey(kind: change.kind, id: change.id)
            if let existing = entities[key] {
                if change.kind == .stroke {
                    guard change.deleted, !existing.deleted else { continue }
                    seq += 1
                    entities[key] = SyncRecord(kind: existing.kind, id: existing.id, parentID: existing.parentID, payload: existing.payload, deleted: true, seq: seq)
                    continue
                }
                if existing.parentID == change.parentID, existing.payload == change.payload, existing.deleted == change.deleted { continue }
            }
            seq += 1
            entities[key] = SyncRecord(kind: change.kind, id: change.id, parentID: change.parentID, payload: change.payload, deleted: change.deleted, seq: seq)
        }
        return PushResponse(accepted: changes.count, seq: seq)
    }

    func pull(since: Int64, limit: Int) async throws -> PullResponse {
        if let delay { try await Task.sleep(for: delay) }
        let newer = entities.values.filter { $0.seq > since }.sorted { $0.seq < $1.seq }
        let page = Array(newer.prefix(limit))
        return PullResponse(changes: page, cursor: page.last?.seq ?? since, more: newer.count > limit)
    }
}

/// Wraps a transport and fails on demand.
actor FlakyTransport: SyncTransport {
    let base: any SyncTransport
    var error: (any Error)?

    init(_ base: any SyncTransport) { self.base = base }

    func fail(with error: (any Error)?) { self.error = error }

    func push(_ changes: [SyncChange]) async throws -> PushResponse {
        if let error { throw error }
        return try await base.push(changes)
    }

    func pull(since: Int64, limit: Int) async throws -> PullResponse {
        if let error { throw error }
        return try await base.pull(since: since, limit: limit)
    }
}

struct StoreFailure: Error {}

/// A device's local database in memory: entities, an outbox and a cursor.
actor InMemoryDevice: SyncStore {
    private(set) var entities: [EntityKey: SyncChange] = [:]
    private var outbox: [PendingChange] = []
    private var nextOutboxID: Int64 = 1
    private var cursorValue: Int64 = 0
    var failApply = false

    func setFailApply(_ value: Bool) { failApply = value }

    /// What she does on the device: it takes effect here at once and waits in the outbox.
    func write(_ change: SyncChange) {
        merge(change)
        outbox.append(PendingChange(outboxID: nextOutboxID, change: change))
        nextOutboxID += 1
    }

    func activeStrokeIDs() -> Set<UUID> {
        Set(entities.values.filter { $0.kind == .stroke && !$0.deleted }.map(\.id))
    }

    func entity(_ kind: EntityKind, _ id: UUID) -> SyncChange? { entities[EntityKey(kind: kind, id: id)] }

    func pendingChanges(limit: Int) async throws -> [PendingChange] { Array(outbox.prefix(limit)) }

    func markPushed(_ outboxIDs: [Int64]) async throws { outbox.removeAll { outboxIDs.contains($0.outboxID) } }

    func cursor() async throws -> Int64 { cursorValue }

    func pendingCount() async throws -> Int { outbox.count }

    func apply(_ records: [SyncRecord], cursor: Int64) async throws {
        if failApply { throw StoreFailure() }
        for record in records {
            merge(SyncChange(kind: record.kind, id: record.id, parentID: record.parentID, payload: record.payload, deleted: record.deleted))
        }
        cursorValue = cursor
    }

    private func merge(_ change: SyncChange) {
        let key = EntityKey(kind: change.kind, id: change.id)
        if change.kind == .stroke, let existing = entities[key] {
            if change.deleted { entities[key] = SyncChange(kind: existing.kind, id: existing.id, parentID: existing.parentID, payload: existing.payload, deleted: true) }
            return
        }
        entities[key] = change
    }
}

func uuid(_ n: Int) -> UUID { UUID(uuidString: String(format: "00000000-0000-0000-0000-%012d", n))! }

func stroke(_ n: Int, page: Int = 1, deleted: Bool = false) -> SyncChange {
    SyncChange(kind: .stroke, id: uuid(100 + n), parentID: uuid(page), payload: deleted ? nil : .object(["n": .number(Double(n))]), deleted: deleted)
}

func notebook(_ title: String) -> SyncChange {
    SyncChange(kind: .notebook, id: uuid(50), payload: .object(["title": .string(title)]))
}
