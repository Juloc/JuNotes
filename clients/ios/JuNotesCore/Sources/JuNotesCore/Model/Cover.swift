import Foundation

/// How the front of a folder or notebook looks: a colour, a gradient or her own picture, with a symbol or emoji and a pattern on top.
public struct Cover: Sendable, Codable, Equatable {
    public enum Style: String, Sendable, Codable, Equatable, CaseIterable {
        case color, gradient, image
    }

    public enum Pattern: String, Sendable, Codable, Equatable, CaseIterable {
        case none, lines, grid, dots
    }

    public var style: Style
    /// Hex colours such as "#6c5ce7": the colour, or the two ends of the gradient.
    public var colorA: String
    public var colorB: String
    /// The picture of an image cover; a file kept like a scan, on the device and on the server.
    public var assetID: UUID?
    /// An SF Symbol name; ignored when an emoji is set.
    public var symbol: String?
    public var emoji: String?
    public var pattern: Pattern

    public init(style: Style = .gradient, colorA: String, colorB: String? = nil, assetID: UUID? = nil, symbol: String? = nil, emoji: String? = nil, pattern: Pattern = .none) {
        self.style = style
        self.colorA = colorA
        self.colorB = colorB ?? colorA
        self.assetID = assetID
        self.symbol = symbol
        self.emoji = emoji
        self.pattern = pattern
    }

    enum CodingKeys: String, CodingKey { case style, colorA, colorB, assetID, symbol, emoji, pattern }

    public init(from decoder: any Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        style = (try? values.decodeIfPresent(Style.self, forKey: .style)) ?? .gradient
        colorA = try values.decodeIfPresent(String.self, forKey: .colorA) ?? Cover.fallbackColor
        colorB = try values.decodeIfPresent(String.self, forKey: .colorB) ?? colorA
        assetID = try values.decodeIfPresent(UUID.self, forKey: .assetID)
        symbol = try values.decodeIfPresent(String.self, forKey: .symbol)
        emoji = try values.decodeIfPresent(String.self, forKey: .emoji)
        pattern = (try? values.decodeIfPresent(Pattern.self, forKey: .pattern)) ?? .none
    }

    public static let fallbackColor = "#8e8e93"

    /// A cover that is just a folder's own colour, used while she has not designed one.
    public static func plain(color: String, symbol: String? = nil) -> Cover {
        Cover(style: .color, colorA: color, symbol: symbol)
    }

    /// The same cover with a different picture, or nil when the picture goes away and the cover falls back to a gradient.
    public func withImage(_ id: UUID?) -> Cover {
        var copy = self
        copy.assetID = id
        copy.style = id == nil ? .gradient : .image
        return copy
    }

    /// Gradients to choose from: a name and two colours.
    public static let presets: [(name: String, a: String, b: String)] = [
        ("Lavendel", "#8f7cf5", "#5b4bd6"),
        ("Himmel", "#6fb1ff", "#3478f6"),
        ("Minze", "#5fd8b5", "#12a78a"),
        ("Wiese", "#8fd96b", "#3fa73e"),
        ("Sonne", "#ffd15c", "#ff9f0a"),
        ("Koralle", "#ff9a7a", "#f2545b"),
        ("Rosé", "#ff9fc4", "#d6478a"),
        ("Beere", "#c87cf0", "#7b3fc4"),
        ("Meer", "#4fd1d9", "#1f7fb5"),
        ("Schiefer", "#8e9aaf", "#4a5568"),
        ("Sand", "#e6cfa5", "#b98a52"),
        ("Nacht", "#3c3f6b", "#14152e"),
    ]
}
