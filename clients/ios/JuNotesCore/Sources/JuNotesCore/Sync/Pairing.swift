import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// What a pairing gives the device: its own key and the name of the person it now writes for (ARCHITECTURE 11).
public struct PairedDevice: Equatable, Sendable {
    public let token: String
    public let personName: String
    /// The id of the person on the server: the app keeps it, so the notebooks of two people never mix on one iPad.
    public let personID: String
}

public enum PairingError: Error, Equatable, Sendable {
    /// Every answer without a key: a wrong, expired or used code, too many tries, a server that only pairs over https. The server says no more than that on purpose.
    case codeRefused
    case noNetwork
    case serverUnreachable
    /// Pairing goes over https only (the one exception is a server on this machine, for tests).
    case notSecure
}

/// The pure rules of pairing, without any network, so they are tested on Linux too.
public enum Pairing {
    public static let codeLength = 10
    /// The letters of the codes the server makes (Base32: A to Z and 2 to 7).
    private static let alphabet = Set("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567")

    /// A typed or pasted code as the server reads it: spaces and dashes left out, capital letters. Nil when it cannot be a code.
    public static func normalise(_ text: String) -> String? {
        let code = String(text.filter { $0 != " " && $0 != "-" }).uppercased()
        guard code.count == codeLength, code.allSatisfy({ alphabet.contains($0) }) else { return nil }
        return code
    }

    /// `ABCDE-FGHIJ`, as the web page shows it.
    public static func formatted(_ code: String) -> String {
        guard code.count == codeLength else { return code }
        return String(code.prefix(5)) + "-" + String(code.suffix(5))
    }

    /// Only https, or a server on this machine (tests). Never plain http to another machine: the key would travel in clear text.
    public static func allows(_ server: URL) -> Bool {
        switch server.scheme?.lowercased() {
        case "https": true
        case "http": ["localhost", "127.0.0.1", "::1"].contains(server.host()?.lowercased() ?? "")
        default: false
        }
    }

    /// The QR code of the web page: `<server>/#pair=<CODE>`. The server is the part before the `#`; nothing else is read from it.
    public static func parseLink(_ text: String) -> (server: URL, code: String)? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let marker = trimmed.range(of: "#pair="),
              let code = normalise(String(trimmed[marker.upperBound...])),
              var parts = URLComponents(string: String(trimmed[..<marker.lowerBound])),
              parts.host != nil else { return nil }
        parts.path = ""
        parts.query = nil
        guard let server = parts.url else { return nil }
        return (server, code)
    }

    /// A value only this request knows; the sign-in page hands it back unchanged.
    public static func newState() -> String { random(32) }

    /// The secret of one sign-in (PKCE, RFC 7636): it never leaves the app until the code is redeemed, so a code caught on its way back is worth nothing.
    public static func newVerifier() -> String { random(64) }

    /// What the sign-in page gets instead of the secret: base64url of its SHA-256, without padding (43 characters).
    public static func challenge(for verifier: String) -> String {
        SHA256.hash(Data(verifier.utf8)).base64EncodedString().replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_").replacingOccurrences(of: "=", with: "")
    }

    private static func random(_ count: Int) -> String {
        var generator = SystemRandomNumberGenerator()
        let letters = Array("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789")
        return String((0..<count).map { _ in letters[Int(generator.next(upperBound: UInt64(letters.count)))] })
    }

    /// The address of the scheme the sign-in page sends its answer to.
    public static let callbackScheme = "junotes"

    /// Where the sign-in window opens: the normal sign-in of the web app, which answers to the app afterwards.
    public static func signInURL(server: URL, state: String, challenge: String) -> URL? {
        guard var parts = URLComponents(url: server, resolvingAgainstBaseURL: false) else { return nil }
        parts.path = "/"
        parts.query = nil
        parts.fragment = "/app-login?state=\(state)&challenge=\(challenge)"
        return parts.url
    }

    public enum SignInAnswer: Equatable, Sendable {
        case code(String)
        case failed
    }

    /// Reads `junotes://paired?code=…&state=…`. Nil when the answer is not to this request (another state, another address): then it is ignored.
    public static func readAnswer(_ url: URL, state: String) -> SignInAnswer? {
        guard url.scheme == callbackScheme, url.host() == "paired",
              let items = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems,
              items.first(where: { $0.name == "state" })?.value == state else { return nil }
        if let code = items.first(where: { $0.name == "code" })?.value.flatMap(normalise) { return .code(code) }
        return .failed
    }
}

/// `POST /api/v1/pair` and `DELETE /api/v1/device`: the only two calls about the key itself.
public struct PairingClient: Sendable {
    let baseURL: URL
    private let session: URLSession

    public init(baseURL: URL, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.session = session
    }

    private struct PairRequest: Encodable {
        let code: String
        let deviceName: String
        let verifier: String?
    }

    private struct PairAnswer: Decodable {
        let token: String
        let personName: String
        let personId: String
    }

    /// Trades a code for the device's own key. The code is good once and for ten minutes. A code from the sign-in window needs the secret of that sign-in.
    public func pair(code: String, deviceName: String, verifier: String? = nil) async throws -> PairedDevice {
        guard Pairing.allows(baseURL) else { throw PairingError.notSecure }
        guard let code = Pairing.normalise(code) else { throw PairingError.codeRefused }
        var request = URLRequest(url: baseURL.appendingPathComponent("api/v1/pair"))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONEncoder().encode(PairRequest(code: code, deviceName: deviceName, verifier: verifier))
        let (data, status) = try await exchange(request)
        guard status == 200, let answer = try? JSONDecoder().decode(PairAnswer.self, from: data), !answer.token.isEmpty else {
            throw status >= 500 ? PairingError.serverUnreachable : PairingError.codeRefused
        }
        return PairedDevice(token: answer.token, personName: answer.personName, personID: answer.personId)
    }

    /// Signs this device out on the server: its key stops working. A key the server no longer knows counts as signed out.
    public func signOut(token: String) async throws {
        var request = URLRequest(url: baseURL.appendingPathComponent("api/v1/device"))
        request.httpMethod = "DELETE"
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        let (_, status) = try await exchange(request)
        guard [204, 401, 404].contains(status) else { throw PairingError.serverUnreachable }
    }

    private func exchange(_ request: URLRequest) async throws -> (Data, Int) {
        do {
            let (data, response) = try await session.data(for: request)
            guard let http = response as? HTTPURLResponse else { throw PairingError.serverUnreachable }
            return (data, http.statusCode)
        } catch let error as URLError {
            throw URLSessionTransport.transportError(for: error) == .noNetwork ? PairingError.noNetwork : PairingError.serverUnreachable
        } catch let error as PairingError {
            throw error
        } catch {
            throw PairingError.serverUnreachable
        }
    }
}
