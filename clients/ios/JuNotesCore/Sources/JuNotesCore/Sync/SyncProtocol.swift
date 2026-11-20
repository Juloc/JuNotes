import Foundation

public enum EntityKind: String, Sendable, Codable, Equatable {
    case subject
    case notebook
    case page
    case stroke
    case grade
    case card
    /// A picture, text box, sticker, tape or recording on a page.
    case object
}

/// One entity created, changed or deleted. The wire format of the server's `/api/v1/sync` endpoints.
public struct SyncChange: Sendable, Codable, Equatable {
    public var kind: EntityKind
    public var id: UUID
    public var parentID: UUID?
    public var payload: JSONValue?
    public var deleted: Bool

    public init(kind: EntityKind, id: UUID, parentID: UUID? = nil, payload: JSONValue? = nil, deleted: Bool = false) {
        self.kind = kind
        self.id = id
        self.parentID = parentID
        self.payload = payload
        self.deleted = deleted
    }

    enum CodingKeys: String, CodingKey {
        case kind, id, payload, deleted
        case parentID = "parentId"
    }
}

/// A change as the server returns it, with its place in the order of changes.
public struct SyncRecord: Sendable, Codable, Equatable {
    public var kind: EntityKind
    public var id: UUID
    public var parentID: UUID?
    public var payload: JSONValue?
    public var deleted: Bool
    public var seq: Int64

    public init(kind: EntityKind, id: UUID, parentID: UUID? = nil, payload: JSONValue? = nil, deleted: Bool = false, seq: Int64) {
        self.kind = kind
        self.id = id
        self.parentID = parentID
        self.payload = payload
        self.deleted = deleted
        self.seq = seq
    }

    enum CodingKeys: String, CodingKey {
        case kind, id, payload, deleted, seq
        case parentID = "parentId"
    }
}

public struct PushRequest: Sendable, Codable, Equatable {
    public var changes: [SyncChange]

    public init(changes: [SyncChange]) { self.changes = changes }
}

public struct PushResponse: Sendable, Codable, Equatable {
    public var accepted: Int
    public var seq: Int64

    public init(accepted: Int, seq: Int64) {
        self.accepted = accepted
        self.seq = seq
    }
}

public struct PullResponse: Sendable, Codable, Equatable {
    public var changes: [SyncRecord]
    public var cursor: Int64
    public var more: Bool

    public init(changes: [SyncRecord], cursor: Int64, more: Bool) {
        self.changes = changes
        self.cursor = cursor
        self.more = more
    }
}

/// Why a request did not get through, in terms the status line can show.
public enum TransportError: Error, Sendable, Equatable {
    case noNetwork
    case serverUnreachable
    case unauthorized
    /// The server understood and refused the request; sending the same changes again will not help.
    case rejected(String)
}

/// The network side. The app implements it with URLSession; tests use an in-memory server.
public protocol SyncTransport: Sendable {
    func push(_ changes: [SyncChange]) async throws -> PushResponse
    func pull(since: Int64, limit: Int) async throws -> PullResponse
}

/// A change waiting to be sent, with the row it came from.
public struct PendingChange: Sendable, Equatable {
    public var outboxID: Int64
    public var change: SyncChange

    public init(outboxID: Int64, change: SyncChange) {
        self.outboxID = outboxID
        self.change = change
    }
}

/// The local database side.
public protocol SyncStore: Sendable {
    /// The oldest changes not yet sent, at most `limit`.
    func pendingChanges(limit: Int) async throws -> [PendingChange]
    func markPushed(_ outboxIDs: [Int64]) async throws
    func cursor() async throws -> Int64
    func pendingCount() async throws -> Int
    /// Applies the records and moves the cursor in one step, so a crash never leaves one without the other.
    func apply(_ records: [SyncRecord], cursor: Int64) async throws
}
