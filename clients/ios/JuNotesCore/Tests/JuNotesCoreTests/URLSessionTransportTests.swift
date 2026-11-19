import Foundation
import Testing
@testable import JuNotesCore
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Answers every request from a closure, so the transport is tested without a server.
final class StubProtocol: URLProtocol {
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

struct ServerInfoTests {
    @Test func theServersSevenFractionalDigitsParse() throws {
        let info = try ServerInfo.decode(Data(#"{"serverTime":"2026-11-19T20:00:00.1234567+00:00","lastBackupAt":"2026-11-19T02:00:03.5+00:00"}"#.utf8))

        #expect(info.serverTime == Date(timeIntervalSince1970: 1_795_118_400.123))
        #expect(info.lastBackupAt == Date(timeIntervalSince1970: 1_795_053_603.5))
    }

    @Test func noBackupYetIsNil() throws {
        let info = try ServerInfo.decode(Data(#"{"serverTime":"2026-11-19T20:00:00Z","lastBackupAt":null}"#.utf8))
        #expect(info.lastBackupAt == nil)
    }

    @Test func datesWithOffsetsAndWithoutFractionsParse() {
        #expect(ServerDate.parse("2026-11-19T21:00:00+01:00") == ServerDate.parse("2026-11-19T20:00:00Z"))
        #expect(ServerDate.parse("not a date") == nil)
    }

    @Test func nonsenseIsAnUnreachableServer() {
        #expect(throws: TransportError.serverUnreachable) { try ServerInfo.decode(Data("[]".utf8)) }
    }
}

extension URLSessionTransportTests {
    @Test func infoReadsTheBackupTime() async throws {
        StubProtocol.handler = { request in
            #expect(request.url?.path == "/api/v1/sync/info")
            #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer t0k")
            return (200, Data(#"{"serverTime":"2026-11-19T20:00:00.0000000+00:00","lastBackupAt":"2026-11-19T02:00:00.0000000+00:00"}"#.utf8))
        }

        let info = try await transport.info()

        #expect(info.lastBackupAt != nil)
    }
}

extension URLSessionTransportTests {
    @Test func anUploadIsAPutWithTheFileAndTheToken() async throws {
        let id = uuid(5)
        StubProtocol.handler = { request in
            #expect(request.httpMethod == "PUT")
            #expect(request.url?.path == "/api/v1/assets/\(id.uuidString.lowercased())")
            #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer t0k")
            return (204, Data())
        }

        try await transport.uploadAsset(id: id, data: Data([0xFF, 0xD8, 0xFF]))
    }

    @Test func aTooBigOrWrongFileIsRefusedForGood() async {
        StubProtocol.handler = { _ in (413, Data(#"{"detail":"The file is larger than 20 MB."}"#.utf8)) }
        await #expect(throws: TransportError.rejected("The file is larger than 20 MB.")) { try await transport.uploadAsset(id: uuid(5), data: Data([1])) }
    }

    @Test func aDownloadReturnsTheBytesOrNilWhenTheServerHasNone() async throws {
        StubProtocol.handler = { _ in (200, Data([1, 2, 3])) }
        #expect(try await transport.downloadAsset(id: uuid(5)) == Data([1, 2, 3]))

        StubProtocol.handler = { _ in (404, Data()) }
        #expect(try await transport.downloadAsset(id: uuid(5)) == nil)
    }
}

extension URLSessionTransportTests {
    @Test func aQuizIsAskedWithOnlyTheFieldsItNeedsAndDecoded() async throws {
        StubProtocol.handler = { request in
            #expect(request.url?.path == "/api/v1/ai/quiz")
            #expect(request.httpMethod == "POST")
            #expect(request.timeoutInterval == 300)
            #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer t0k")
            let sent = try JSONSerialization.jsonObject(with: request.bodyData()) as? [String: Any]
            #expect(sent?["text"] as? String == "Brüche")
            #expect(sent?["count"] as? Int == 3)
            #expect(sent?["answer"] == nil)
            return (200, Data(#"{"questions":[{"question":"Was ist ein Nenner?","answer":"unten","hint":null}]}"#.utf8))
        }

        let result: QuizResult = try await transport.ask(.quiz, AIRequest(text: "Brüche", count: 3))

        #expect(result.questions == [QuizQuestion(question: "Was ist ein Nenner?", answer: "unten", hint: nil)])
    }

    @Test func everyKindOfAnswerDecodes() async throws {
        StubProtocol.handler = { _ in (200, Data(#"{"title":"T","tasks":[{"question":"a","points":3,"solution":"x"}],"totalPoints":3}"#.utf8)) }
        #expect(try await transport.ask(.test, AIRequest(text: "x"), as: TestResult.self).totalPoints == 3)

        StubProtocol.handler = { _ in (200, Data(#"{"cards":[{"front":"a","back":"b"}]}"#.utf8)) }
        #expect(try await transport.ask(.cards, AIRequest(text: "x"), as: CardsResult.self).cards == [CardDraft(front: "a", back: "b")])

        StubProtocol.handler = { _ in (200, Data(#"{"notes":[{"kind":"error","quote":"q","text":"t","suggestion":null}]}"#.utf8)) }
        #expect(try await transport.ask(.check, AIRequest(text: "x"), as: CheckResult.self).notes.first?.kind == "error")

        StubProtocol.handler = { _ in (200, Data(#"{"correct":true,"score":0.8,"feedback":"gut"}"#.utf8)) }
        #expect(try await transport.ask(.grade, AIRequest(question: "q", answer: "a"), as: GradedAnswer.self).score == 0.8)
    }

    @Test(arguments: [
        (400, #"{"detail":"There is no material to work with."}"#, AIError.invalid("There is no material to work with.")),
        (403, #"{"detail":"„Probetest“ ist im Moment ausgeschaltet."}"#, AIError.refused("„Probetest“ ist im Moment ausgeschaltet.")),
        (429, #"{"detail":"Das Tageslimit für die KI ist erreicht."}"#, AIError.refused("Das Tageslimit für die KI ist erreicht.")),
        (502, #"{"detail":"Codex took too long."}"#, AIError.failed("Codex took too long.")),
        (503, #"{"detail":"Die KI ist auf dem Server noch nicht eingerichtet."}"#, AIError.notSetUp),
        (401, "", AIError.unauthorized),
        (500, "", AIError.unreachable),
    ])
    func theServersRefusalsBecomeSentences(status: Int, body: String, expected: AIError) async {
        StubProtocol.handler = { _ in (status, Data(body.utf8)) }
        await #expect(throws: expected) { try await transport.ask(.quiz, AIRequest(text: "x"), as: QuizResult.self) }
    }

    @Test func noNetworkAndAnUnreachableServerAreTold() async {
        StubProtocol.handler = { _ in throw URLError(.notConnectedToInternet) }
        await #expect(throws: AIError.noNetwork) { try await transport.ask(.quiz, AIRequest(text: "x"), as: QuizResult.self) }

        StubProtocol.handler = { _ in throw URLError(.cannotConnectToHost) }
        await #expect(throws: AIError.unreachable) { try await transport.ask(.quiz, AIRequest(text: "x"), as: QuizResult.self) }
    }

    @Test func anAnswerOfTheWrongShapeIsAFailedAnswer() async {
        StubProtocol.handler = { _ in (200, Data(#"{"something":"else"}"#.utf8)) }
        await #expect(throws: AIError.failed("Die Antwort war nicht lesbar.")) { try await transport.ask(.quiz, AIRequest(text: "x"), as: QuizResult.self) }
    }
}

extension URLRequest {
    /// The body of a request that went through URLSession, which delivers it as a stream.
    func bodyData() -> Data {
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

extension URLSessionTransportTests {
    @Test func paperlessAvailabilityIsReadFromTheServer() async throws {
        StubProtocol.handler = { request in
            #expect(request.url?.path == "/api/v1/export/status")
            return (200, Data(#"{"paperless":true}"#.utf8))
        }
        #expect(try await transport.paperlessAvailable())

        StubProtocol.handler = { _ in (200, Data(#"{"paperless":false}"#.utf8)) }
        #expect(try await transport.paperlessAvailable() == false)
    }

    @Test func aPdfIsSentWithTitleAndTagsInTheQuery() async throws {
        StubProtocol.handler = { request in
            let items = URLComponents(url: request.url!, resolvingAgainstBaseURL: false)!.queryItems ?? []
            #expect(request.url?.path == "/api/v1/export/paperless")
            #expect(request.httpMethod == "POST")
            #expect(items.first { $0.name == "title" }?.value == "Brüche & mehr")
            #expect(items.first { $0.name == "tags" }?.value == "Mathe,Schule")
            #expect(request.value(forHTTPHeaderField: "Content-Type") == "application/pdf")
            #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer t0k")
            #expect(request.timeoutInterval == 300)
            #expect(request.bodyData() == Data("%PDF-1.7".utf8))
            return (202, Data(#"{"task":"abc"}"#.utf8))
        }

        try await transport.sendToPaperless(pdf: Data("%PDF-1.7".utf8), title: "Brüche & mehr", tags: ["Mathe", "Schule"])
    }

    @Test(arguments: [
        (503, #"{"detail":"Paperless ist auf dem Server nicht eingerichtet."}"#, ExportError.notSetUp),
        (502, #"{"detail":"Paperless refused uploading (403)."}"#, ExportError.failed("Paperless refused uploading (403).")),
        (415, #"{"detail":"Das ist keine PDF-Datei."}"#, ExportError.refused("Das ist keine PDF-Datei.")),
        (401, "", ExportError.unauthorized),
        (500, "", ExportError.unreachable),
    ])
    func paperlessRefusalsBecomeSentences(status: Int, body: String, expected: ExportError) async {
        StubProtocol.handler = { _ in (status, Data(body.utf8)) }
        await #expect(throws: expected) { try await transport.sendToPaperless(pdf: Data("%PDF".utf8), title: "x", tags: []) }
    }

    @Test func noNetworkWhileSendingIsTold() async {
        StubProtocol.handler = { _ in throw URLError(.notConnectedToInternet) }
        await #expect(throws: ExportError.noNetwork) { try await transport.sendToPaperless(pdf: Data("%PDF".utf8), title: "x", tags: []) }
    }
}

struct NotebookTitleTests {
    @Test(arguments: [
        ("Mathe-Heft.pdf", "Mathe-Heft"),
        ("GoodNotes_-_Mathe_2026.pdf", "GoodNotes - Mathe 2026"),
        ("  Biologie  Zelle .pdf", "Biologie Zelle"),
        (".pdf", "Importiertes Heft"),
        ("ohne Endung", "ohne Endung"),
    ])
    func aTitleFromAFileName(name: String, expected: String) {
        #expect(NotebookTitle.fromFileName(name) == expected)
    }

    @Test func aVeryLongNameIsCut() {
        #expect(NotebookTitle.fromFileName(String(repeating: "a", count: 300) + ".pdf").count == 120)
    }
}
