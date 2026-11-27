import Foundation
import Testing
@testable import JuNotesCore
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Its own stub (not the one of the transport tests), so the two suites never share an answer while they run side by side.
final class PairingStub: URLProtocol {
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

struct PairingRulesTests {
    @Test func aCodeIsReadAsTheServerReadsIt() {
        #expect(Pairing.normalise("abcde-fgh23") == "ABCDEFGH23")
        #expect(Pairing.normalise(" ABCDE FGH23 ") == "ABCDEFGH23")
        #expect(Pairing.normalise("ABCDEFGH2") == nil)
        #expect(Pairing.normalise("ABCDEFGH01") == nil, "0 and 1 are not in the alphabet of the codes")
        #expect(Pairing.formatted("ABCDEFGH23") == "ABCDE-FGH23")
    }

    @Test func onlyHttpsOrThisMachine() {
        #expect(Pairing.allows(URL(string: "https://notes.example")!))
        #expect(Pairing.allows(URL(string: "http://localhost:5000")!))
        #expect(Pairing.allows(URL(string: "http://127.0.0.1:5000")!))
        #expect(!Pairing.allows(URL(string: "http://notes.example")!))
        #expect(!Pairing.allows(URL(string: "http://192.168.1.10:8105")!))
        #expect(!Pairing.allows(URL(string: "ftp://notes.example")!))
    }

    @Test func theQrCodeGivesServerAndCodeAndNothingElse() {
        let link = Pairing.parseLink("https://notes.example/#pair=ABCDEFGH23")
        #expect(link?.server.absoluteString == "https://notes.example")
        #expect(link?.code == "ABCDEFGH23")
        #expect(Pairing.parseLink("https://notes.example/somewhere?x=1#pair=abcde-fgh23")?.server.absoluteString == "https://notes.example")
        #expect(Pairing.parseLink("https://notes.example/#pair=SHORT") == nil)
        #expect(Pairing.parseLink("ABCDEFGH23") == nil)
    }

    @Test func theSignInWindowOpensTheAppLoginOfTheServer() {
        let url = Pairing.signInURL(server: URL(string: "https://notes.example/old/path?x=1")!, state: "S7ate")
        #expect(url?.absoluteString == "https://notes.example/#/app-login?state=S7ate")
    }

    @Test func onlyTheAnswerToThisRequestCounts() {
        let state = Pairing.newState()
        #expect(state.count == 32)
        #expect(Pairing.newState() != state)
        #expect(Pairing.readAnswer(URL(string: "junotes://paired?code=ABCDEFGH23&state=\(state)")!, state: state) == .code("ABCDEFGH23"))
        #expect(Pairing.readAnswer(URL(string: "junotes://paired?error=failed&state=\(state)")!, state: state) == .failed)
        #expect(Pairing.readAnswer(URL(string: "junotes://paired?code=ABCDEFGH23&state=other")!, state: state) == nil)
        #expect(Pairing.readAnswer(URL(string: "junotes://paired?code=ABCDEFGH23")!, state: state) == nil)
        #expect(Pairing.readAnswer(URL(string: "other://paired?code=ABCDEFGH23&state=\(state)")!, state: state) == nil)
    }
}

@Suite(.serialized)
struct PairingClientTests {
    let client: PairingClient

    init() {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [PairingStub.self]
        client = PairingClient(baseURL: URL(string: "https://notes.example/")!, session: URLSession(configuration: configuration))
    }

    @Test func aCodeIsTradedForTheKeyWithoutAnyToken() async throws {
        PairingStub.handler = { request in
            #expect(request.url?.path == "/api/v1/pair")
            #expect(request.httpMethod == "POST")
            #expect(request.value(forHTTPHeaderField: "Authorization") == nil)
            return (200, Data(#"{"token":"nt1_abc","deviceId":"5d0c2b3a-0000-0000-0000-000000000001","personName":"Bea","api":1}"#.utf8))
        }

        let paired = try await client.pair(code: "abcde-fgh23", deviceName: "iPad")

        #expect(paired == PairedDevice(token: "nt1_abc", personName: "Bea"))
    }

    @Test func everyRefusalIsTheSame() async {
        for status in [400, 401, 429] {
            PairingStub.handler = { _ in (status, Data(#"{"detail":"The code is not valid or has expired."}"#.utf8)) }
            await #expect(throws: PairingError.codeRefused) { try await client.pair(code: "ABCDEFGH23", deviceName: "iPad") }
        }
    }

    @Test func aServerWithoutHttpsIsNotAsked() async {
        PairingStub.handler = { _ in Issue.record("no request may leave"); return (200, Data()) }
        let plain = PairingClient(baseURL: URL(string: "http://notes.example/")!)
        await #expect(throws: PairingError.notSecure) { try await plain.pair(code: "ABCDEFGH23", deviceName: "iPad") }
    }

    @Test func signingOutSendsTheOwnKeyAndAGoneKeyCountsAsDone() async throws {
        PairingStub.handler = { request in
            #expect(request.url?.path == "/api/v1/device")
            #expect(request.httpMethod == "DELETE")
            #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer nt1_abc")
            return (204, Data())
        }
        try await client.signOut(token: "nt1_abc")

        PairingStub.handler = { _ in (401, Data()) }
        try await client.signOut(token: "nt1_abc")

        PairingStub.handler = { _ in (500, Data()) }
        await #expect(throws: PairingError.serverUnreachable) { try await client.signOut(token: "nt1_abc") }
    }
}
