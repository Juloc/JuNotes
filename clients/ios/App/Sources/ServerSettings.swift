import Foundation

/// The server and this device's own key (ARCHITECTURE 11). The address lives in the preferences, the key in the keychain, the name of the person for the settings page.
/// There is no shared token any more in the app: a device signs in (sign-in window or a pairing code) and gets a key of its own.
struct ServerSettings: Equatable {
    var urlString: String
    /// The key of this device, `nt1_…`; empty while it is not signed in.
    var token: String
    /// The person the device writes for, as the server named her at the sign-in.
    var personName: String

    var url: URL? {
        guard let url = URL(string: urlString.trimmingCharacters(in: .whitespacesAndNewlines)), url.host() != nil, ["http", "https"].contains(url.scheme) else { return nil }
        return url
    }

    var isComplete: Bool { url != nil && !token.isEmpty }

    static func load() -> ServerSettings {
        // The token of the old app (one key for everybody) is not used any more; it is removed once so it does not linger in the keychain.
        Keychain.write("", for: "syncToken")
        return ServerSettings(urlString: UserDefaults.standard.string(forKey: "serverURL") ?? "", token: Keychain.read("deviceToken") ?? "", personName: UserDefaults.standard.string(forKey: "personName") ?? "")
    }

    func save() {
        UserDefaults.standard.set(urlString.trimmingCharacters(in: .whitespacesAndNewlines), forKey: "serverURL")
        UserDefaults.standard.set(personName, forKey: "personName")
        Keychain.write(token, for: "deviceToken")
    }
}
