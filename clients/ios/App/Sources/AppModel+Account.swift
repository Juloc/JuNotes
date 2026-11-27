import Foundation
import JuNotesCore
import UIKit

/// Signing this device in and out (ARCHITECTURE 11). The key comes from the server in exchange for a pairing code, made either by the sign-in window or typed or scanned.
extension AppModel {
    /// Trades the code for this device's own key, keeps it and syncs at once.
    func signIn(code: String, server: URL) async throws {
        let paired = try await PairingClient(baseURL: server).pair(code: code, deviceName: UIDevice.current.model)
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
