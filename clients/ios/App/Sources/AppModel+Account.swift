import Foundation
import JuNotesCore
import UIKit

/// Signing this device in and out (ARCHITECTURE 11). The key comes from the server in exchange for a pairing code, made either by the sign-in window or typed or scanned.
/// Why a sign-in was refused by the app itself (the server said yes).
enum SignInError: Error {
    /// The notebooks on this iPad belong to another account (another person, or another server).
    case otherAccount(String)

    var text: String {
        switch self {
        case .otherAccount(let name): "Auf diesem iPad liegen die Hefte von \(name). Melde dich mit diesem Konto an; ein anderes Konto würde die Hefte zweier Menschen vermischen."
        }
    }
}

extension AppModel {
    /// Trades the code for this device's own key, keeps it and syncs at once. The library on the iPad belongs to the first account that signed in here: the cursor and the
    /// changes not yet sent are that account's (rule 6, every row has an owner), so another person or another server is refused, and its new key is given back at once.
    func signIn(code: String, server: URL, verifier: String? = nil) async throws {
        let client = PairingClient(baseURL: server)
        let paired = try await client.pair(code: code, deviceName: UIDevice.current.model, verifier: verifier)
        let account = "\(server.host()?.lowercased() ?? "")|\(paired.personID)"
        let defaults = UserDefaults.standard
        if let owner = defaults.string(forKey: "libraryAccount"), owner != account {
            try? await client.signOut(token: paired.token)
            throw SignInError.otherAccount(defaults.string(forKey: "libraryAccountName") ?? "einem anderen Konto")
        }
        defaults.set(account, forKey: "libraryAccount")
        defaults.set(paired.personName, forKey: "libraryAccountName")
        await apply(ServerSettings(urlString: server.absoluteString, token: paired.token, personName: paired.personName))
    }

    /// Removes the device on the server too, then forgets the key. The notebooks stay on the iPad. Without a network the key is forgotten all the same,
    /// and the device stays in the list on the web until somebody removes it there.
    func signOut() async {
        if let url = settings.url, !settings.token.isEmpty {
            try? await PairingClient(baseURL: url).signOut(token: settings.token)
        }
        await apply(ServerSettings(urlString: settings.urlString, token: "", personName: ""))
    }
}
