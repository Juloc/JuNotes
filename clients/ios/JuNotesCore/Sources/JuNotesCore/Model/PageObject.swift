import Foundation

/// Something she put on a page besides her handwriting: a photo, a text box, a sticker, a strip of tape or a recording.
/// Positions are in page points (a sheet is 595 by 842), measured from the top left corner of the object before it is turned.
public struct PageObject: SyncEntity {
    public static let kind = EntityKind.object

    public enum Kind: String, Sendable, Codable, Equatable, CaseIterable {
        case text, image, sticker, tape, audio
    }

    public var id: UUID
    public var pageID: UUID
    public var type: Kind
    public var x: Double
    public var y: Double
    public var width: Double
    public var height: Double
    /// Degrees, clockwise.
    public var rotation: Double
    /// The words of a text box; an emoji or an SF Symbol name for a sticker.
    public var text: String?
    /// The file of a photo or of a recording; it travels like a scan does.
    public var assetID: UUID?
    /// A hex colour: of the text, of a symbol sticker or of the tape.
    public var color: String?
    public var fontSize: Double
    /// Higher stands in front.
    public var z: Int

    public var parentID: UUID? { pageID }

    public init(id: UUID = UUID(), pageID: UUID, type: Kind, x: Double, y: Double, width: Double? = nil, height: Double? = nil, rotation: Double = 0, text: String? = nil, assetID: UUID? = nil, color: String? = nil, fontSize: Double = 16, z: Int = 0) {
        let size = Self.defaultSize(for: type)
        self.id = id
        self.pageID = pageID
        self.type = type
        self.x = x
        self.y = y
        self.width = width ?? size.width
        self.height = height ?? size.height
        self.rotation = rotation
        self.text = text
        self.assetID = assetID
        self.color = color
        self.fontSize = fontSize
        self.z = z
    }

    public static func defaultSize(for kind: Kind) -> (width: Double, height: Double) {
        switch kind {
        case .text: (220, 60)
        case .image: (240, 180)
        case .sticker: (64, 64)
        case .tape: (150, 28)
        case .audio: (150, 40)
        }
    }

    /// A photo is made to fit a width and keeps its proportions.
    public static func imageSize(pixelWidth: Double, pixelHeight: Double, fitting maximum: Double = 300) -> (width: Double, height: Double) {
        guard pixelWidth > 0, pixelHeight > 0 else { return defaultSize(for: .image) }
        let scale = min(1, maximum / max(pixelWidth, pixelHeight))
        return (pixelWidth * scale, pixelHeight * scale)
    }

    /// The same object moved so that it cannot be lost off the page: at least 24 points of it stay inside.
    public func clamped(toWidth pageWidth: Double, height pageHeight: Double) -> PageObject {
        var copy = self
        let keep = 24.0
        copy.x = min(max(x, keep - width), pageWidth - keep)
        copy.y = min(max(y, keep - height), pageHeight - keep)
        copy.width = max(16, width)
        copy.height = max(16, height)
        return copy
    }

    enum CodingKeys: String, CodingKey { case type, x, y, width, height, rotation, text, assetID, color, fontSize, z }

    public init(from decoder: any Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        id = UUID()
        pageID = UUID()
        type = try values.decode(Kind.self, forKey: .type)
        x = try values.decodeIfPresent(Double.self, forKey: .x) ?? 0
        y = try values.decodeIfPresent(Double.self, forKey: .y) ?? 0
        let size = Self.defaultSize(for: type)
        width = try values.decodeIfPresent(Double.self, forKey: .width) ?? size.width
        height = try values.decodeIfPresent(Double.self, forKey: .height) ?? size.height
        rotation = try values.decodeIfPresent(Double.self, forKey: .rotation) ?? 0
        text = try values.decodeIfPresent(String.self, forKey: .text)
        assetID = try values.decodeIfPresent(UUID.self, forKey: .assetID)
        color = try values.decodeIfPresent(String.self, forKey: .color)
        fontSize = try values.decodeIfPresent(Double.self, forKey: .fontSize) ?? 16
        z = try values.decodeIfPresent(Int.self, forKey: .z) ?? 0
    }

    public static func decode(_ record: SyncRecord) throws -> PageObject {
        guard record.kind == kind, !record.deleted, let payload = record.payload, let parent = record.parentID else { throw EntityCoding.Failure.notDecodable(record.kind) }
        var object: PageObject = try EntityCoding.decode(payload)
        object.id = record.id
        object.pageID = parent
        return object
    }
}
