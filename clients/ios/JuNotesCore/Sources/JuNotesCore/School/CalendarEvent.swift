import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// One entry of the school calendar as the server delivers it: a lesson, an exam, a holiday.
public struct CalendarEvent: Sendable, Equatable, Identifiable, Codable {
    public var uid: String
    public var start: Date
    public var end: Date
    public var summary: String
    public var location: String?
    public var details: String?
    public var cancelled: Bool
    public var allDay: Bool

    public var id: String { uid + "@" + String(start.timeIntervalSince1970) }

    public init(uid: String, start: Date, end: Date, summary: String, location: String? = nil, details: String? = nil, cancelled: Bool = false, allDay: Bool = false) {
        self.uid = uid
        self.start = start
        self.end = end
        self.summary = summary
        self.location = location
        self.details = details
        self.cancelled = cancelled
        self.allDay = allDay
    }

    /// Decodes `/api/v1/school/calendar`. Dates arrive with seven fractional digits or none.
    public static func decodeList(_ data: Data) throws -> [CalendarEvent] {
        guard let items = try JSONSerialization.jsonObject(with: data) as? [[String: Any]] else { throw TransportError.serverUnreachable }
        return try items.map { item in
            guard let uid = item["uid"] as? String,
                  let start = (item["start"] as? String).flatMap(ServerDate.parse),
                  let end = (item["end"] as? String).flatMap(ServerDate.parse),
                  let summary = item["summary"] as? String else { throw TransportError.serverUnreachable }
            return CalendarEvent(uid: uid, start: start, end: end, summary: summary, location: item["location"] as? String, details: item["description"] as? String,
                                 cancelled: item["cancelled"] as? Bool ?? false, allDay: item["allDay"] as? Bool ?? false)
        }
    }
}

/// A piece of news from the school's website.
public struct SchoolNews: Sendable, Equatable, Identifiable, Codable {
    public var key: String
    public var title: String
    public var link: URL?
    public var publishedAt: Date?
    public var summary: String?

    public var id: String { key }

    public init(key: String, title: String, link: URL? = nil, publishedAt: Date? = nil, summary: String? = nil) {
        self.key = key
        self.title = title
        self.link = link
        self.publishedAt = publishedAt
        self.summary = summary
    }

    public static func decodeList(_ data: Data) throws -> [SchoolNews] {
        guard let items = try JSONSerialization.jsonObject(with: data) as? [[String: Any]] else { throw TransportError.serverUnreachable }
        return try items.map { item in
            guard let key = item["key"] as? String, let title = item["title"] as? String else { throw TransportError.serverUnreachable }
            return SchoolNews(key: key, title: title, link: (item["link"] as? String).flatMap(URL.init(string:)), publishedAt: (item["publishedAt"] as? String).flatMap(ServerDate.parse), summary: item["summary"] as? String)
        }
    }
}

extension URLSessionTransport {
    /// The events that touch the range from `from` to `to`.
    public func calendar(from: Date, to: Date) async throws -> [CalendarEvent] {
        let formatter = ISO8601DateFormatter()
        var path = "api/v1/school/calendar?from=\(formatter.string(from: from))&to=\(formatter.string(from: to))"
        path = path.replacingOccurrences(of: "+", with: "%2B")
        return try await get(path: path) { try CalendarEvent.decodeList($0) }
    }

    public func news(limit: Int = 20) async throws -> [SchoolNews] {
        try await get(path: "api/v1/school/news?limit=\(limit)") { try SchoolNews.decodeList($0) }
    }
}
