import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public enum ExportError: Error, Sendable, Equatable {
    /// Paperless is not set up on the server.
    case notSetUp
    case refused(String)
    case failed(String)
    case unauthorized
    case noNetwork
    case unreachable
}

extension URLSessionTransport {
    /// Whether the server can send documents to Paperless.
    public func paperlessAvailable() async throws -> Bool {
        try await get(path: "api/v1/export/status") { data in
            (try JSONSerialization.jsonObject(with: data) as? [String: Any])?["paperless"] as? Bool ?? false
        }
    }

    /// Sends a notebook as PDF to Paperless through the server, with the subject as a tag. Returns when Paperless has taken it.
    public func sendToPaperless(pdf: Data, title: String, tags: [String]) async throws {
        var components = URLComponents(url: baseURL.appendingPathComponent("api/v1/export/paperless"), resolvingAgainstBaseURL: false)!
        components.queryItems = [URLQueryItem(name: "title", value: title), URLQueryItem(name: "tags", value: tags.joined(separator: ","))]
        var request = URLRequest(url: components.url!)
        request.httpMethod = "POST"
        request.timeoutInterval = 300
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        request.setValue("application/pdf", forHTTPHeaderField: "Content-Type")
        request.httpBody = pdf

        let exchange: (data: Data, status: Int)
        do {
            exchange = try await self.exchange(request)
        } catch TransportError.noNetwork {
            throw ExportError.noNetwork
        } catch {
            throw ExportError.unreachable
        }

        let detail = Self.detail(in: exchange.data)
        switch exchange.status {
        case 200..<300: return
        case 401: throw ExportError.unauthorized
        case 503: throw ExportError.notSetUp
        case 502: throw ExportError.failed(detail ?? "Paperless hat die Datei nicht angenommen.")
        case 400..<500: throw ExportError.refused(detail ?? "Die Datei wurde abgelehnt.")
        default: throw ExportError.unreachable
        }
    }
}

public enum NotebookTitle {
    /// A notebook title from the name of an imported file: "GoodNotes_-_Mathe-Heft_2026.pdf" becomes "GoodNotes - Mathe-Heft 2026".
    public static func fromFileName(_ fileName: String) -> String {
        var name = fileName
        // ".pdf" on its own counts as a hidden file without an ending for the path functions, so the ending is taken off by hand.
        if name.lowercased().hasSuffix(".pdf") { name.removeLast(4) } else { name = (name as NSString).deletingPathExtension }
        name = name.replacingOccurrences(of: "_", with: " ")
        while name.contains("  ") { name = name.replacingOccurrences(of: "  ", with: " ") }
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? "Importiertes Heft" : String(trimmed.prefix(120))
    }
}
