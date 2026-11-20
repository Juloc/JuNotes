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
                    Toggle("Unterricht entfällt", isOn: Binding(get: { model.reminderSettings.cancellations }, set: { model.reminderSettings.cancellations = $0; Task { await model.refreshReminders() } }))
                    Toggle("Neuigkeiten der Schule", isOn: Binding(get: { model.reminderSettings.news }, set: { model.reminderSettings.news = $0 }))
                    Toggle("Arbeit morgen", isOn: Binding(get: { model.reminderSettings.exams }, set: { model.reminderSettings.exams = $0; Task { await model.refreshReminders() } }))
                    Toggle("Lernplan heute", isOn: Binding(get: { model.reminderSettings.studyPlan }, set: { model.reminderSettings.studyPlan = $0; Task { await model.refreshReminders() } }))
                    Toggle("Karteikarten fällig", isOn: Binding(get: { model.reminderSettings.cards }, set: { model.reminderSettings.cards = $0; Task { await model.refreshReminders() } }))
                } header: {
                    Text("Benachrichtigungen")
                } footer: {
                    Text("Das iPad meldet sich selbst. Es schaut alle paar Stunden im Hintergrund nach, die Meldungen kommen also nicht auf die Sekunde genau. Erlaubnis dafür steht unter Einstellungen des iPads, JuNotes, Mitteilungen.")
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
        .presentationDetents([.large])
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
