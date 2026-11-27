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

extension URLRequest {
    /// The body as sent: URLProtocol hands it over as a stream, not as httpBody.
    func httpBodyData() -> Data {
        if let httpBody { return httpBody }
        guard let stream = httpBodyStream else { return Data() }
        stream.open()
        defer { stream.close() }
        var data = Data()
        var buffer = [UInt8](repeating: 0, count: 4096)
        while stream.hasBytesAvailable {
            let read = stream.read(&buffer, maxLength: buffer.count)
            if read <= 0 { break }
            data.append(buffer, count: read)
        }
        return data
    }
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
        let url = Pairing.signInURL(server: URL(string: "https://notes.example/old/path?x=1")!, state: "S7ate", challenge: "C4all")
        #expect(url?.absoluteString == "https://notes.example/#/app-login?state=S7ate&challenge=C4all")
    }

    /// The secret of RFC 7636 (appendix B), checked against Python's hashlib; the server tests use the same secret.
    @Test func theChallengeIsTheSha256OfTheSecretInBase64Url() {
        #expect(Pairing.challenge(for: "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM")
        #expect(SHA256.hash(Data()).map { String(format: "%02x", $0) }.joined() == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
        #expect(SHA256.hash(Data(String(repeating: "a", count: 1000).utf8)).map { String(format: "%02x", $0) }.joined() == "41edece42d63e8d9bf515a9ba6932e1c20cbc9f5a5d134645adb5db1b9737ea3")
        let secret = Pairing.newVerifier()
        #expect(secret.count == 64)
        #expect(Pairing.challenge(for: secret).count == 43)
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
            let body = try JSONSerialization.jsonObject(with: request.httpBodyData()) as? [String: Any]
            #expect(body?["code"] as? String == "ABCDEFGH23")
            #expect(body?["verifier"] as? String == "secret-of-the-app")
            return (200, Data(#"{"token":"nt1_abc","deviceId":"5d0c2b3a-0000-0000-0000-000000000001","personName":"Bea","personId":"7c0c2b3a-0000-0000-0000-000000000002","api":1}"#.utf8))
        }

        let paired = try await client.pair(code: "abcde-fgh23", deviceName: "iPad", verifier: "secret-of-the-app")

        #expect(paired == PairedDevice(token: "nt1_abc", personName: "Bea", personID: "7c0c2b3a-0000-0000-0000-000000000002"))
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
