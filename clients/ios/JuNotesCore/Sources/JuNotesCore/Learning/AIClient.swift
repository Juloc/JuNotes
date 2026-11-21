import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public enum AITask: String, Sendable {
    case quiz, test, cards, check, grade, cover
}

/// What the app tells the server's learning helper. Only the fields a task needs are sent.
public struct AIRequest: Sendable, Encodable, Equatable {
    public var text: String?
    public var count: Int?
    public var durationMinutes: Int?
    public var subject: String?
    public var question: String?
    public var expected: String?
    public var answer: String?

    public init(text: String? = nil, count: Int? = nil, durationMinutes: Int? = nil, subject: String? = nil, question: String? = nil, expected: String? = nil, answer: String? = nil) {
        self.text = text
        self.count = count
        self.durationMinutes = durationMinutes
        self.subject = subject
        self.question = question
        self.expected = expected
        self.answer = answer
    }
}

public struct QuizQuestion: Sendable, Decodable, Equatable {
    public var question: String
    public var answer: String
    public var hint: String?
}

public struct QuizResult: Sendable, Decodable, Equatable {
    public var questions: [QuizQuestion]
}

public struct TestTask: Sendable, Decodable, Equatable {
    public var question: String
    public var points: Int
    public var solution: String
}

public struct TestResult: Sendable, Decodable, Equatable {
    public var title: String
    public var tasks: [TestTask]
    public var totalPoints: Int
}

public struct CardDraft: Sendable, Decodable, Equatable {
    public var front: String
    public var back: String
}

public struct CardsResult: Sendable, Decodable, Equatable {
    public var cards: [CardDraft]
}

public struct CheckNote: Sendable, Decodable, Equatable {
    /// "missing", "error" or "unclear".
    public var kind: String
    public var quote: String?
    public var text: String
    public var suggestion: String?
}

public struct CheckResult: Sendable, Decodable, Equatable {
    public var notes: [CheckNote]
}

public struct GradedAnswer: Sendable, Decodable, Equatable {
    public var correct: Bool
    /// 0 to 1.
    public var score: Double
    public var feedback: String
}

/// A cover the helper designed from a description: what the app can draw itself.
public struct CoverDesign: Sendable, Decodable, Equatable {
    public var colorA: String
    public var colorB: String
    public var emoji: String?
    public var symbol: String?
    public var pattern: String

    /// The same as a cover, made from `base` so a picture or other choice of hers stays out of it: this is always a gradient.
    public var cover: Cover {
        Cover(style: .gradient, colorA: colorA, colorB: colorB, symbol: emoji == nil ? symbol : nil, emoji: emoji, pattern: Cover.Pattern(rawValue: pattern) ?? .none)
    }
}

/// Why the learning helper did not answer, in terms the app can say in a sentence.
public enum AIError: Error, Sendable, Equatable {
    /// The server has no Codex yet, or nobody signed it in.
    case notSetUp
    /// Switched off by the admin or the day's limit is used up; the text says which, in German.
    case refused(String)
    /// The model did not give a usable answer; trying again may work.
    case failed(String)
    case invalid(String)
    case unauthorized
    case noNetwork
    case unreachable
}

extension URLSessionTransport {
    /// Asks the server's learning helper. A request can take a minute or two, so it is allowed five.
    public func ask<Response: Decodable & Sendable>(_ task: AITask, _ request: AIRequest, as type: Response.Type = Response.self) async throws -> Response {
        var urlRequest = makeRequest(path: "api/v1/ai/\(task.rawValue)")
        urlRequest.httpMethod = "POST"
        urlRequest.timeoutInterval = 300
        urlRequest.setValue("application/json", forHTTPHeaderField: "Content-Type")
        urlRequest.httpBody = try JSONEncoder().encode(request)

        let exchange: (data: Data, status: Int)
        do {
            exchange = try await self.exchange(urlRequest)
        } catch TransportError.noNetwork {
            throw AIError.noNetwork
        } catch {
            throw AIError.unreachable
        }

        let detail = Self.detail(in: exchange.data)
        switch exchange.status {
        case 200..<300:
            do {
                return try JSONDecoder().decode(Response.self, from: exchange.data)
            } catch {
                throw AIError.failed("Die Antwort war nicht lesbar.")
            }
        case 401: throw AIError.unauthorized
        case 403, 429: throw AIError.refused(detail ?? "Das ist im Moment nicht möglich.")
        case 400: throw AIError.invalid(detail ?? "Die Anfrage war nicht in Ordnung.")
        case 503: throw AIError.notSetUp
        case 502: throw AIError.failed(detail ?? "Die KI hat nicht richtig geantwortet.")
        default: throw AIError.unreachable
        }
    }
}
