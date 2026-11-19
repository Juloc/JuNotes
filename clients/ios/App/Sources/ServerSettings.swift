import Foundation

struct ServerSettings: Equatable {
    var urlString: String
    var token: String

    var url: URL? {
        guard let url = URL(string: urlString.trimmingCharacters(in: .whitespacesAndNewlines)), url.host() != nil, ["http", "https"].contains(url.scheme) else { return nil }
        return url
    }

    var isComplete: Bool { url != nil && !token.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    static func load() -> ServerSettings {
        ServerSettings(urlString: UserDefaults.standard.string(forKey: "serverURL") ?? "", token: Keychain.read("syncToken") ?? "")
    }

    func save() {
        UserDefaults.standard.set(urlString.trimmingCharacters(in: .whitespacesAndNewlines), forKey: "serverURL")
        Keychain.write(token.trimmingCharacters(in: .whitespacesAndNewlines), for: "syncToken")
    }
}
