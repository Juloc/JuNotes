import JuNotesCore
import SwiftUI

struct SettingsView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var draft = ServerSettings(urlString: "", token: "")
    @State private var testResult: String?
    @State private var testing = false

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("https://notizen.example.de", text: $draft.urlString)
                        .keyboardType(.URL).textInputAutocapitalization(.never).autocorrectionDisabled()
                    SecureField("Zugangsschlüssel", text: $draft.token)
                        .textInputAutocapitalization(.never).autocorrectionDisabled()
                } header: {
                    Text("Server")
                } footer: {
                    Text("Adresse und Schlüssel bekommst du von dem, der den Server eingerichtet hat. Ohne Server funktioniert alles weiter, nur das Sichern auf dem Server fehlt.")
                }

                Section {
                    Button { Task { await test() } } label: {
                        HStack { Text("Verbindung testen"); if testing { Spacer(); ProgressView() } }
                    }
                    .disabled(!draft.isComplete || testing)
                    if let testResult { Text(testResult).font(.footnote).foregroundStyle(.secondary) }
                }
            }
            .navigationTitle("Einstellungen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await model.apply(draft); dismiss() } }
                }
            }
            .onAppear { draft = model.settings }
        }
        .presentationDetents([.medium])
    }

    private func test() async {
        guard let url = draft.url else { return }
        testing = true
        defer { testing = false }
        do {
            let info = try await URLSessionTransport(baseURL: url, token: draft.token.trimmingCharacters(in: .whitespacesAndNewlines)).info()
            testResult = info.lastBackupAt == nil ? "Verbunden. Es gibt noch kein Backup auf dem Server." : "Verbunden. Der Server sichert regelmäßig."
        } catch TransportError.unauthorized {
            testResult = "Der Server ist erreichbar, aber der Schlüssel stimmt nicht."
        } catch TransportError.noNetwork {
            testResult = "Kein Netz. Prüfe das WLAN."
        } catch {
            testResult = "Der Server antwortet nicht. Prüfe die Adresse."
        }
    }
}
