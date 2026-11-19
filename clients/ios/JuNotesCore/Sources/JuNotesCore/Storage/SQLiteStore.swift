#if canImport(SQLite3)
import Foundation
import SQLite3

private let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)

public struct SQLiteFailure: Error, Equatable, CustomStringConvertible {
    public var message: String
    public var description: String { message }
}

enum SQLiteValue {
    case null
    case int(Int64)
    case text(String)
}

/// One row, valid only while the query callback runs.
struct SQLiteRow {
    let statement: OpaquePointer

    func text(_ column: Int32) -> String? {
        sqlite3_column_type(statement, column) == SQLITE_NULL ? nil : String(cString: sqlite3_column_text(statement, column))
    }

    func int(_ column: Int32) -> Int64 { sqlite3_column_int64(statement, column) }
}

/// A thin wrapper around one SQLite connection. Not thread-safe by itself: `SQLiteStore` keeps it inside an actor.
final class SQLiteConnection {
    private var handle: OpaquePointer?

    init(path: String) throws {
        guard sqlite3_open_v2(path, &handle, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, nil) == SQLITE_OK else {
            throw SQLiteFailure(message: "open: " + lastError())
        }
        try execute("pragma journal_mode = wal")
        try execute("pragma synchronous = normal")
    }

    deinit { sqlite3_close(handle) }

    private func lastError() -> String { handle.map { String(cString: sqlite3_errmsg($0)) } ?? "no connection" }

    func execute(_ sql: String) throws {
        guard sqlite3_exec(handle, sql, nil, nil, nil) == SQLITE_OK else { throw SQLiteFailure(message: lastError()) }
    }

    func run(_ sql: String, _ parameters: [SQLiteValue] = []) throws {
        _ = try query(sql, parameters) { _ in () }
    }

    func query<T>(_ sql: String, _ parameters: [SQLiteValue] = [], _ map: (SQLiteRow) throws -> T) throws -> [T] {
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(handle, sql, -1, &statement, nil) == SQLITE_OK, let statement else { throw SQLiteFailure(message: "prepare: " + lastError()) }
        defer { sqlite3_finalize(statement) }

        for (offset, value) in parameters.enumerated() {
            let index = Int32(offset + 1)
            switch value {
            case .null: sqlite3_bind_null(statement, index)
            case .int(let number): sqlite3_bind_int64(statement, index, number)
            case .text(let text): sqlite3_bind_text(statement, index, text, -1, transient)
            }
        }

        var rows: [T] = []
        while true {
            switch sqlite3_step(statement) {
            case SQLITE_ROW: rows.append(try map(SQLiteRow(statement: statement)))
            case SQLITE_DONE: return rows
            default: throw SQLiteFailure(message: "step: " + lastError())
            }
        }
    }

    func transaction<T>(_ body: () throws -> T) throws -> T {
        try execute("begin immediate")
        do {
            let result = try body()
            try execute("commit")
            return result
        } catch {
            try? execute("rollback")
            throw error
        }
    }
}

public struct SearchHit: Sendable, Equatable {
    public var pageID: UUID
    public var notebookID: UUID
    /// A short piece of the page text with the match between « and ».
    public var snippet: String
}

/// The device's database: every entity, the outbox of changes still to send, the sync cursor and a full-text index of what the pages say.
public actor SQLiteStore: SyncStore {
    private let database: SQLiteConnection
    private let encoder = EntityCoding.encoder

    /// Pass ":memory:" for a throwaway database, as the tests do.
    public init(path: String) throws {
        database = try SQLiteConnection(path: path)
        try database.execute("""
        create table if not exists entities (
            kind text not null, id text not null, parent_id text, payload text, deleted integer not null default 0,
            primary key (kind, id));
        create index if not exists entities_parent on entities (kind, parent_id);
        create table if not exists outbox (
            seq integer primary key autoincrement, kind text not null, id text not null, parent_id text, payload text, deleted integer not null);
        create table if not exists meta (key text primary key, value text not null);
        create virtual table if not exists page_text using fts5(page_id unindexed, notebook_id unindexed, body, tokenize = 'unicode61 remove_diacritics 2');
        """)
    }

    // MARK: Local writes

    /// What she does: it shows at once and waits in the outbox until the next sync.
    public func write(_ change: SyncChange) throws {
        try database.transaction {
            try upsert(change, fromServer: false)
            try database.run("insert into outbox (kind, id, parent_id, payload, deleted) values (?, ?, ?, ?, ?)", values(of: change))
        }
    }

    public func write(_ changes: [SyncChange]) throws {
        for change in changes { try write(change) }
    }

    public func put(_ entity: some SyncEntity) throws { try write(entity.change()) }

    public func put(_ stroke: Stroke, on pageID: UUID) throws { try write(stroke.change(pageID: pageID)) }

    public func remove(_ stroke: Stroke, on pageID: UUID) throws { try write(stroke.deletion(pageID: pageID)) }

    // MARK: Reading

    public func subjects() throws -> [Subject] {
        try decoded(kind: .subject, parent: nil, filterParent: false).compactMap { try? Subject.decode($0) }.sorted { ($0.sort, $0.name) < ($1.sort, $1.name) }
    }

    public func notebooks(subjectID: UUID?, includeTrashed: Bool = false) throws -> [Notebook] {
        try decoded(kind: .notebook, parent: subjectID, filterParent: true)
            .compactMap { try? Notebook.decode($0) }
            .filter { includeTrashed || $0.trashedAt == nil }
            .sorted { $0.createdAt < $1.createdAt }
    }

    public func trashedNotebooks() throws -> [Notebook] {
        try decoded(kind: .notebook, parent: nil, filterParent: false).compactMap { try? Notebook.decode($0) }.filter { $0.trashedAt != nil }
    }

    public func pages(notebookID: UUID, includeTrashed: Bool = false) throws -> [Page] {
        try decoded(kind: .page, parent: notebookID, filterParent: true)
            .compactMap { try? Page.decode($0) }
            .filter { includeTrashed || $0.trashedAt == nil }
            .sorted { ($0.sort, $0.id.uuidString) < ($1.sort, $1.id.uuidString) }
    }

    public func strokes(pageID: UUID) throws -> [Stroke] {
        try decoded(kind: .stroke, parent: pageID, filterParent: true).compactMap { try? Stroke.decode($0).stroke }
    }

    // MARK: Search

    /// Replaces what the index knows about a page; call it after the text recognition of that page ran.
    public func index(pageID: UUID, notebookID: UUID, text: String) throws {
        try database.transaction {
            try database.run("delete from page_text where page_id = ?", [.text(pageID.uuidString)])
            if !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                try database.run("insert into page_text (page_id, notebook_id, body) values (?, ?, ?)", [.text(pageID.uuidString), .text(notebookID.uuidString), .text(text)])
            }
        }
    }

    public func search(_ query: String, limit: Int = 30) throws -> [SearchHit] {
        let terms = query.split(whereSeparator: { !$0.isLetter && !$0.isNumber }).map { "\"\($0)\"*" }
        guard !terms.isEmpty else { return [] }
        return try database.query(
            "select page_id, notebook_id, snippet(page_text, 2, '«', '»', ' … ', 12) from page_text where page_text match ? order by bm25(page_text) limit ?",
            [.text(terms.joined(separator: " ")), .int(Int64(limit))]
        ) { row in
            guard let page = row.text(0).flatMap(UUID.init(uuidString:)), let notebook = row.text(1).flatMap(UUID.init(uuidString:)) else { return nil }
            return SearchHit(pageID: page, notebookID: notebook, snippet: row.text(2) ?? "")
        }.compactMap { $0 }
    }

    // MARK: SyncStore

    public func pendingChanges(limit: Int) async throws -> [PendingChange] {
        try database.query("select seq, kind, id, parent_id, payload, deleted from outbox order by seq limit ?", [.int(Int64(limit))]) { row in
            guard let kind = row.text(1).flatMap(EntityKind.init(rawValue:)), let id = row.text(2).flatMap(UUID.init(uuidString:)) else { return nil }
            return PendingChange(outboxID: row.int(0), change: SyncChange(kind: kind, id: id, parentID: row.text(3).flatMap(UUID.init(uuidString:)), payload: try payload(row.text(4)), deleted: row.int(5) != 0))
        }.compactMap { $0 }
    }

    public func markPushed(_ outboxIDs: [Int64]) async throws {
        try database.transaction {
            for id in outboxIDs { try database.run("delete from outbox where seq = ?", [.int(id)]) }
        }
    }

    public func cursor() async throws -> Int64 {
        try database.query("select value from meta where key = 'cursor'") { Int64($0.text(0) ?? "0") ?? 0 }.first ?? 0
    }

    public func pendingCount() async throws -> Int {
        Int(try database.query("select count(*) from outbox") { $0.int(0) }.first ?? 0)
    }

    public func apply(_ records: [SyncRecord], cursor: Int64) async throws {
        try database.transaction {
            for record in records {
                try upsert(SyncChange(kind: record.kind, id: record.id, parentID: record.parentID, payload: record.payload, deleted: record.deleted), fromServer: true)
            }
            try database.run("insert into meta (key, value) values ('cursor', ?) on conflict (key) do update set value = excluded.value", [.text(String(cursor))])
        }
    }

    // MARK: Internals

    /// Strokes are only added or deleted. Anything else the server sends gives way to a change of hers that is still waiting to go out,
    /// which will win on the server anyway; this keeps the screen from jumping back and forth.
    private func upsert(_ change: SyncChange, fromServer: Bool) throws {
        let isStroke = change.kind == .stroke
        if isStroke {
            if change.deleted {
                try database.run("update entities set deleted = 1 where kind = 'stroke' and id = ?", [.text(change.id.uuidString)])
                try database.run("insert or ignore into entities (kind, id, parent_id, payload, deleted) values ('stroke', ?, ?, null, 1)", [.text(change.id.uuidString), change.parentID.map { .text($0.uuidString) } ?? .null])
            } else {
                try database.run("insert or ignore into entities (kind, id, parent_id, payload, deleted) values (?, ?, ?, ?, 0)", values(of: change).dropLast().map { $0 })
            }
            return
        }

        if fromServer {
            let waiting = try database.query("select 1 from outbox where kind = ? and id = ? limit 1", [.text(change.kind.rawValue), .text(change.id.uuidString)]) { _ in true }
            if !waiting.isEmpty { return }
        }
        try database.run(
            "insert into entities (kind, id, parent_id, payload, deleted) values (?, ?, ?, ?, ?) on conflict (kind, id) do update set parent_id = excluded.parent_id, payload = excluded.payload, deleted = excluded.deleted",
            values(of: change)
        )
    }

    private func values(of change: SyncChange) throws -> [SQLiteValue] {
        [
            .text(change.kind.rawValue),
            .text(change.id.uuidString),
            change.parentID.map { .text($0.uuidString) } ?? .null,
            try change.payload.map { .text(String(decoding: try encoder.encode($0), as: UTF8.self)) } ?? .null,
            .int(change.deleted ? 1 : 0),
        ]
    }

    private func payload(_ text: String?) throws -> JSONValue? {
        try text.map { try JSONDecoder().decode(JSONValue.self, from: Data($0.utf8)) }
    }

    private func decoded(kind: EntityKind, parent: UUID?, filterParent: Bool) throws -> [SyncRecord] {
        let sql = "select id, parent_id, payload from entities where kind = ? and deleted = 0" + (filterParent ? (parent == nil ? " and parent_id is null" : " and parent_id = ?") : "")
        var parameters: [SQLiteValue] = [.text(kind.rawValue)]
        if filterParent, let parent { parameters.append(.text(parent.uuidString)) }
        return try database.query(sql, parameters) { row in
            guard let id = row.text(0).flatMap(UUID.init(uuidString:)) else { return nil }
            return SyncRecord(kind: kind, id: id, parentID: row.text(1).flatMap(UUID.init(uuidString:)), payload: try payload(row.text(2)), seq: 0)
        }.compactMap { $0 }
    }
}
#endif
