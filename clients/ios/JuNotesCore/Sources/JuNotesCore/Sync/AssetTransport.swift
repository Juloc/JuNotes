import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

extension URLSessionTransport {
    /// Sends a scan or a PDF. The same id always holds the same file, so sending it twice does no harm.
    public func uploadAsset(id: UUID, data: Data) async throws {
        var request = makeRequest(path: "api/v1/assets/\(id.uuidString.lowercased())")
        request.httpMethod = "PUT"
        request.setValue("application/octet-stream", forHTTPHeaderField: "Content-Type")
        request.httpBody = data
        _ = try await body(of: request)
    }

    /// The file for an id, or nil if the server does not have it.
    public func downloadAsset(id: UUID) async throws -> Data? {
        do {
            return try await body(of: makeRequest(path: "api/v1/assets/\(id.uuidString.lowercased())"))
        } catch TransportError.rejected(let message) where message == "HTTP 404" {
            return nil
        }
    }
}
