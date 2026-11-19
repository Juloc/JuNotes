import Foundation
import Testing
@testable import JuNotesCore
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Answers every request from a closure, so the transport is tested without a server.
final class StubProtocol: URLProtocol, @unchecked Sendable {
    nonisolated(unsafe) static var handler: (@Sendable (URLRequest) throws -> (Int, Data))?

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        do {
            let (status, body) = try Self.handler!(request)
            let response = HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: nil, headerFields: nil)!
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: body)
            client?.urlProtocolDidFinishLoading(self)
        } catch {
            client?.urlProtocol(self, didFailWithError: error)
        }
    }

    override func stopLoading() {}
}

@Suite(.serialized)
struct URLSessionTransportTests {
    let transport: URLSessionTransport

    init() {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubProtocol.self]
        transport = URLSessionTransport(baseURL: URL(string: "https://notes.example/")!, token: "t0k", session: URLSession(configuration: configuration))
    }

    @Test func aPushSendsTheTokenAndTheChanges() async throws {
        StubProtocol.handler = { request in
            #expect(request.url?.path == "/api/v1/sync/push")
            #expect(request.httpMethod == "POST")
            #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer t0k")
            #expect(request.value(forHTTPHeaderField: "Content-Type") == "application/json")
            return (200, Data(#"{"accepted":1,"seq":4}"#.utf8))
        }

        let response = try await transport.push([stroke(1)])

        #expect(response == PushResponse(accepted: 1, seq: 4))
    }

    @Test func aPullAsksForTheCursorAndTheLimit() async throws {
        StubProtocol.handler = { request in
            let items = URLComponents(url: request.url!, resolvingAgainstBaseURL: false)!.queryItems ?? []
            #expect(items.first { $0.name == "since" }?.value == "12")
            #expect(items.first { $0.name == "limit" }?.value == "50")
            #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer t0k")
            return (200, Data(#"{"changes":[],"cursor":12,"more":false}"#.utf8))
        }

        let response = try await transport.pull(since: 12, limit: 50)

        #expect(response == PullResponse(changes: [], cursor: 12, more: false))
    }

    @Test func aWrongTokenIsUnauthorized() async {
        StubProtocol.handler = { _ in (401, Data()) }
        await #expect(throws: TransportError.unauthorized) { try await transport.pull(since: 0, limit: 10) }
    }

    @Test func aRefusedPushCarriesTheServersExplanation() async {
        StubProtocol.handler = { _ in (400, Data(#"{"title":"Bad Request","detail":"Unknown kind 'folder'."}"#.utf8)) }
        await #expect(throws: TransportError.rejected("Unknown kind 'folder'.")) { try await transport.push([stroke(1)]) }
    }

    @Test(arguments: [500, 502, 503])
    func serverErrorsCountAsUnreachable(status: Int) async {
        StubProtocol.handler = { _ in (status, Data()) }
        await #expect(throws: TransportError.serverUnreachable) { try await transport.pull(since: 0, limit: 10) }
    }

    @Test func noInternetIsTheOfflineCase() async {
        StubProtocol.handler = { _ in throw URLError(.notConnectedToInternet) }
        await #expect(throws: TransportError.noNetwork) { try await transport.pull(since: 0, limit: 10) }
    }

    @Test func aServerThatCannotBeReachedIsTheUnreachableCase() async {
        StubProtocol.handler = { _ in throw URLError(.cannotConnectToHost) }
        await #expect(throws: TransportError.serverUnreachable) { try await transport.pull(since: 0, limit: 10) }
    }

    @Test func anAnswerThatIsNotTheExpectedJSONIsUnreachable() async {
        StubProtocol.handler = { _ in (200, Data("<html>captive portal</html>".utf8)) }
        await #expect(throws: TransportError.serverUnreachable) { try await transport.pull(since: 0, limit: 10) }
    }
}
