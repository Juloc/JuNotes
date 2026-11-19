import Foundation

public enum StrokeTool: String, Sendable, Codable, Equatable {
    case pen
    case marker
    case pencil
    case eraser
}

/// One sample of the Pencil. Pressure and angles are what the web view needs to draw the stroke as the iPad did.
public struct StrokePoint: Sendable, Codable, Equatable {
    public var x: Double
    public var y: Double
    public var pressure: Double
    public var altitude: Double
    public var azimuth: Double
    public var roll: Double
    /// Seconds since the stroke began.
    public var time: Double

    public init(x: Double, y: Double, pressure: Double = 1, altitude: Double = .pi / 2, azimuth: Double = 0, roll: Double = 0, time: Double = 0) {
        self.x = x
        self.y = y
        self.pressure = pressure
        self.altitude = altitude
        self.azimuth = azimuth
        self.roll = roll
        self.time = time
    }
}

public struct Stroke: Sendable, Codable, Equatable, Identifiable {
    /// Stable across devices, so the sync can merge strokes instead of replacing the page.
    public var id: UUID
    public var tool: StrokeTool
    public var color: String
    public var width: Double
    public var points: [StrokePoint]

    public init(id: UUID = UUID(), tool: StrokeTool = .pen, color: String = "#000000", width: Double = 2, points: [StrokePoint]) {
        self.id = id
        self.tool = tool
        self.color = color
        self.width = width
        self.points = points
    }

    /// A plain SVG path for the web app; the full point data stays in the document.
    public var svgPath: String {
        guard let first = points.first else { return "" }
        var path = "M\(Self.format(first.x)) \(Self.format(first.y))"
        for point in points.dropFirst() {
            path += " L\(Self.format(point.x)) \(Self.format(point.y))"
        }
        return path
    }

    private static func format(_ value: Double) -> String {
        let rounded = (value * 100).rounded() / 100
        return rounded == rounded.rounded() ? String(Int(rounded)) : String(rounded)
    }
}

/// The page's strokes as JSON, independent of PencilKit's own format.
public struct StrokeDocument: Sendable, Codable, Equatable {
    public static let currentVersion = 1

    public var version: Int
    public var pageID: UUID
    public var strokes: [Stroke]

    public init(pageID: UUID, strokes: [Stroke] = []) {
        self.version = Self.currentVersion
        self.pageID = pageID
        self.strokes = strokes
    }

    public enum DecodingFailure: Error, Equatable {
        case newerVersion(Int)
    }

    public func encoded() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        return try encoder.encode(self)
    }

    public static func decode(_ data: Data) throws -> StrokeDocument {
        let document = try JSONDecoder().decode(StrokeDocument.self, from: data)
        guard document.version <= currentVersion else { throw DecodingFailure.newerVersion(document.version) }
        return document
    }

    /// Strokes by id: those only one side has are kept, a stroke both have is the same stroke. Order follows `self`, then the new ones.
    public func merged(with other: StrokeDocument) -> StrokeDocument {
        let known = Set(strokes.map(\.id))
        var result = self
        result.strokes += other.strokes.filter { !known.contains($0.id) }
        return result
    }
}
