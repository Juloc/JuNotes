import Foundation

/// A thing that syncs: it knows its kind, its parent and how it becomes the payload of a `SyncChange`.
public protocol SyncEntity: Codable, Sendable, Equatable, Identifiable where ID == UUID {
    static var kind: EntityKind { get }
    var parentID: UUID? { get }
}

extension SyncEntity {
    /// The change that creates or updates this entity on the server.
    public func change() throws -> SyncChange {
        SyncChange(kind: Self.kind, id: id, parentID: parentID, payload: try EntityCoding.payload(self))
    }

    /// The change that removes it for good. Moving it to the trash is an update with `trashedAt`, not this.
    public func deletion() -> SyncChange {
        SyncChange(kind: Self.kind, id: id, parentID: parentID, payload: nil, deleted: true)
    }
}

public enum EntityCoding {
    public enum Failure: Error, Equatable {
        case notDecodable(EntityKind)
    }

    static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.sortedKeys]
        return encoder
    }()

    static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }()

    static func payload(_ value: some Encodable) throws -> JSONValue {
        try decoder.decode(JSONValue.self, from: encoder.encode(value))
    }

    static func decode<T: Decodable>(_ payload: JSONValue) throws -> T {
        try decoder.decode(T.self, from: encoder.encode(payload))
    }
}

public struct Subject: SyncEntity {
    public static let kind = EntityKind.subject

    public var id: UUID
    public var name: String
    /// A hex colour such as "#0a84ff".
    public var color: String
    /// An SF Symbol name.
    public var symbol: String
    public var sort: Int

    public var parentID: UUID? { nil }

    public init(id: UUID = UUID(), name: String, color: String = "#0a84ff", symbol: String = "book", sort: Int = 0) {
        self.id = id
        self.name = name
        self.color = color
        self.symbol = symbol
        self.sort = sort
    }

    enum CodingKeys: String, CodingKey { case name, color, symbol, sort }

    public init(from decoder: any Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        id = UUID()
        name = try values.decode(String.self, forKey: .name)
        color = try values.decodeIfPresent(String.self, forKey: .color) ?? "#0a84ff"
        symbol = try values.decodeIfPresent(String.self, forKey: .symbol) ?? "book"
        sort = try values.decodeIfPresent(Int.self, forKey: .sort) ?? 0
    }

    public static func decode(_ record: SyncRecord) throws -> Subject {
        guard record.kind == kind, !record.deleted, let payload = record.payload else { throw EntityCoding.Failure.notDecodable(record.kind) }
        var subject: Subject = try EntityCoding.decode(payload)
        subject.id = record.id
        return subject
    }
}

public enum NotebookKind: String, Sendable, Codable, Equatable {
    /// Pages one below the other, with pages added as she writes.
    case pages
    /// One endless surface.
    case whiteboard
}

public enum PageTemplate: String, Sendable, Codable, Equatable, CaseIterable {
    case blank, ruled, grid, dotted, cornell
}

public struct Notebook: SyncEntity {
    public static let kind = EntityKind.notebook

    public var id: UUID
    public var subjectID: UUID?
    public var title: String
    public var type: NotebookKind
    public var template: PageTemplate
    public var createdAt: Date
    /// Set when it went to the trash; it is deleted for good after `Trash.retention`.
    public var trashedAt: Date?

    public var parentID: UUID? { subjectID }

    public init(id: UUID = UUID(), subjectID: UUID? = nil, title: String, type: NotebookKind = .pages, template: PageTemplate = .ruled, createdAt: Date = Date(), trashedAt: Date? = nil) {
        self.id = id
        self.subjectID = subjectID
        self.title = title
        self.type = type
        self.template = template
        self.createdAt = createdAt
        self.trashedAt = trashedAt
    }

    enum CodingKeys: String, CodingKey { case title, type, template, createdAt, trashedAt }

    public init(from decoder: any Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        id = UUID()
        subjectID = nil
        title = try values.decode(String.self, forKey: .title)
        type = try values.decodeIfPresent(NotebookKind.self, forKey: .type) ?? .pages
        template = try values.decodeIfPresent(PageTemplate.self, forKey: .template) ?? .ruled
        createdAt = try values.decode(Date.self, forKey: .createdAt)
        trashedAt = try values.decodeIfPresent(Date.self, forKey: .trashedAt)
    }

    /// The record's parent is the subject, which the payload does not repeat.
    public static func decode(_ record: SyncRecord) throws -> Notebook {
        guard record.kind == kind, !record.deleted, let payload = record.payload else { throw EntityCoding.Failure.notDecodable(record.kind) }
        var notebook: Notebook = try EntityCoding.decode(payload)
        notebook.id = record.id
        notebook.subjectID = record.parentID
        return notebook
    }
}

public enum PageContent: Sendable, Codable, Equatable {
    case drawing
    /// A scanned sheet; the image is a file on the device and on the server.
    case scan(assetID: UUID)
    case pdf(assetID: UUID, pageIndex: Int)

    private enum CodingKeys: String, CodingKey { case type, assetID, pageIndex }

    public init(from decoder: any Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        switch try values.decode(String.self, forKey: .type) {
        case "scan": self = .scan(assetID: try values.decode(UUID.self, forKey: .assetID))
        case "pdf": self = .pdf(assetID: try values.decode(UUID.self, forKey: .assetID), pageIndex: try values.decode(Int.self, forKey: .pageIndex))
        default: self = .drawing
        }
    }

    public func encode(to encoder: any Encoder) throws {
        var values = encoder.container(keyedBy: CodingKeys.self)
        switch self {
        case .drawing: try values.encode("drawing", forKey: .type)
        case .scan(let assetID):
            try values.encode("scan", forKey: .type)
            try values.encode(assetID, forKey: .assetID)
        case .pdf(let assetID, let pageIndex):
            try values.encode("pdf", forKey: .type)
            try values.encode(assetID, forKey: .assetID)
            try values.encode(pageIndex, forKey: .pageIndex)
        }
    }
}

public struct Page: SyncEntity {
    public static let kind = EntityKind.page

    public var id: UUID
    public var notebookID: UUID
    /// Orders the pages of a notebook; see `PageOrder`.
    public var sort: String
    public var template: PageTemplate?
    public var content: PageContent
    public var autoCreated: Bool
    /// A line at the top of the page, such as "Do 19.11. · Mathe", which "Neue Seite für jetzt" writes.
    public var heading: String?
    /// Text printed on the page by the app, such as the tasks of a mock test; she writes around it.
    public var printedText: String?
    /// The solution to what is printed, which she can uncover after trying.
    public var solution: String?
    /// Suggestions from "Ergänzen und Prüfen" that she accepted; shown in a box on the page and never mixed into her handwriting.
    public var additions: [String]
    public var trashedAt: Date?

    public var parentID: UUID? { notebookID }

    public init(id: UUID = UUID(), notebookID: UUID, sort: String, template: PageTemplate? = nil, content: PageContent = .drawing, autoCreated: Bool = false, heading: String? = nil, printedText: String? = nil, solution: String? = nil, additions: [String] = [], trashedAt: Date? = nil) {
        self.id = id
        self.notebookID = notebookID
        self.sort = sort
        self.template = template
        self.content = content
        self.autoCreated = autoCreated
        self.heading = heading
        self.printedText = printedText
        self.solution = solution
        self.additions = additions
        self.trashedAt = trashedAt
    }

    enum CodingKeys: String, CodingKey { case sort, template, content, autoCreated, heading, printedText, solution, additions, trashedAt }

    public init(from decoder: any Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        id = UUID()
        notebookID = UUID()
        sort = try values.decode(String.self, forKey: .sort)
        template = try values.decodeIfPresent(PageTemplate.self, forKey: .template)
        content = try values.decodeIfPresent(PageContent.self, forKey: .content) ?? .drawing
        autoCreated = try values.decodeIfPresent(Bool.self, forKey: .autoCreated) ?? false
        heading = try values.decodeIfPresent(String.self, forKey: .heading)
        printedText = try values.decodeIfPresent(String.self, forKey: .printedText)
        solution = try values.decodeIfPresent(String.self, forKey: .solution)
        additions = try values.decodeIfPresent([String].self, forKey: .additions) ?? []
        trashedAt = try values.decodeIfPresent(Date.self, forKey: .trashedAt)
    }

    public static func decode(_ record: SyncRecord) throws -> Page {
        guard record.kind == kind, !record.deleted, let payload = record.payload, let parent = record.parentID else { throw EntityCoding.Failure.notDecodable(record.kind) }
        var page: Page = try EntityCoding.decode(payload)
        page.id = record.id
        page.notebookID = parent
        return page
    }
}

extension Stroke {
    /// Strokes belong to a page, which the stroke itself does not know.
    public func change(pageID: UUID) throws -> SyncChange {
        SyncChange(kind: .stroke, id: id, parentID: pageID, payload: try EntityCoding.payload(self))
    }

    public func deletion(pageID: UUID) -> SyncChange {
        SyncChange(kind: .stroke, id: id, parentID: pageID, payload: nil, deleted: true)
    }

    public static func decode(_ record: SyncRecord) throws -> (stroke: Stroke, pageID: UUID) {
        guard record.kind == .stroke, !record.deleted, let payload = record.payload, let page = record.parentID else { throw EntityCoding.Failure.notDecodable(record.kind) }
        var stroke: Stroke = try EntityCoding.decode(payload)
        stroke.id = record.id
        return (stroke, page)
    }
}
