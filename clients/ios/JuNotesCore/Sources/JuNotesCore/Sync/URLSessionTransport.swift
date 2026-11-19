import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Talks to the server's `/api/v1/sync` endpoints and turns every failure into a `TransportError` the status line understands.
public struct URLSessionTransport: SyncTransport {
    let baseURL: URL
    let token: String
    private let session: URLSession

    public init(baseURL: URL, token: String, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.token = token
        self.session = session
    }

    public func push(_ changes: [SyncChange]) async throws -> PushResponse {
        var request = makeRequest(path: "api/v1/sync/push")
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONEncoder().encode(PushRequest(changes: changes))
        return try await send(request)
    }

    public func pull(since: Int64, limit: Int) async throws -> PullResponse {
        var components = URLComponents(url: baseURL.appendingPathComponent("api/v1/sync/pull"), resolvingAgainstBaseURL: false)!
        components.queryItems = [URLQueryItem(name: "since", value: String(since)), URLQueryItem(name: "limit", value: String(limit))]
        var request = URLRequest(url: components.url!)
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        return try await send(request)
    }

    func makeRequest(path: String) -> URLRequest {
        var request = URLRequest(url: baseURL.appendingPathComponent(path))
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        return request
    }

    func get<T>(path: String, parse: (Data) throws -> T) async throws -> T {
        let request = makeRequest(path: path)
        let data = try await body(of: request)
        do {
            return try parse(data)
        } catch {
            throw TransportError.serverUnreachable
        }
    }

    private func send<Response: Decodable>(_ request: URLRequest) async throws -> Response {
        let data = try await body(of: request)
        do {
            return try JSONDecoder().decode(Response.self, from: data)
        } catch {
            throw TransportError.serverUnreachable
        }
    }

    /// The answer to a request whatever its status; only a failure to get any answer is an error here.
    func exchange(_ request: URLRequest) async throws -> (data: Data, status: Int) {
        do {
            let (data, response) = try await session.data(for: request)
            guard let http = response as? HTTPURLResponse else { throw TransportError.serverUnreachable }
            return (data, http.statusCode)
        } catch let error as URLError {
            throw Self.transportError(for: error)
        } catch let error as TransportError {
            throw error
        } catch {
            throw TransportError.serverUnreachable
        }
    }

    func body(of request: URLRequest) async throws -> Data {
        let (data, status) = try await exchange(request)
        switch status {
        case 200..<300:
            return data
        case 401, 403:
            throw TransportError.unauthorized
        case 400..<500:
            throw TransportError.rejected(Self.detail(in: data) ?? "HTTP \(status)")
        default:
            // 5xx, including the 503 of a server whose sync is not set up yet.
            throw TransportError.serverUnreachable
        }
    }

    static func transportError(for error: URLError) -> TransportError {
        switch error.code {
        case .notConnectedToInternet, .networkConnectionLost, .dataNotAllowed, .internationalRoamingOff, .callIsActive:
            .noNetwork
        default:
            .serverUnreachable
        }
    }

    /// The `detail` of an RFC 9457 problem response, which is how the server explains a 400.
    static func detail(in data: Data) -> String? {
        (try? JSONSerialization.jsonObject(with: data) as? [String: Any])?["detail"] as? String
    }
}
