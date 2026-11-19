import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// What `/api/v1/sync/info` says: the server's clock and when it last backed itself up.
public struct ServerInfo: Sendable, Equatable {
    public var serverTime: Date
    public var lastBackupAt: Date?

    public init(serverTime: Date, lastBackupAt: Date?) {
        self.serverTime = serverTime
        self.lastBackupAt = lastBackupAt
    }

    /// Decodes the server's JSON. Its dates carry seven fractional digits, which Foundation's parser does not take as they are.
    public static func decode(_ data: Data) throws -> ServerInfo {
        guard let object = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let time = (object["serverTime"] as? String).flatMap(ServerDate.parse) else {
            throw TransportError.serverUnreachable
        }
        return ServerInfo(serverTime: time, lastBackupAt: (object["lastBackupAt"] as? String).flatMap(ServerDate.parse))
    }
}

public enum ServerDate {
    /// "2026-11-19T20:00:00.1234567+00:00", with or without fraction or with a Z.
    public static func parse(_ text: String) -> Date? {
        // Foundation takes at most three fractional digits.
        let trimmed = text.replacingOccurrences(of: #"(\.\d{3})\d+"#, with: "$1", options: .regularExpression)
        let withFraction = ISO8601DateFormatter()
        withFraction.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = withFraction.date(from: trimmed) { return date }
        let plain = ISO8601DateFormatter()
        plain.formatOptions = [.withInternetDateTime]
        return plain.date(from: trimmed)
    }
}

extension URLSessionTransport {
    /// The server's clock and last backup. Failures use the same errors as a sync, so the status line treats them alike.
    public func info() async throws -> ServerInfo {
        try await get(path: "api/v1/sync/info") { try ServerInfo.decode($0) }
    }
}
