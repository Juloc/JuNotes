import AuthenticationServices
import JuNotesCore
import SwiftUI

struct SettingsView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @Environment(\.webAuthenticationSession) private var webAuthenticationSession
    @State private var address = ""
    @State private var message: String?
    @State private var working = false
    @State private var showCode = false
    @State private var confirmSignOut = false
    @State private var testResult: String?

    private var server: URL? { ServerSettings(urlString: address, token: "", personName: "").url }

    var body: some View {
        NavigationStack {
            Form {
                if model.settings.isComplete { signedIn } else { signIn }

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
            }
            .navigationTitle("Einstellungen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) { Button("Fertig") { dismiss() } }
            }
            .onAppear { address = model.settings.urlString }
            .sheet(isPresented: $showCode) {
                PairingCodeView(server: server) { code, server in
                    try await model.signIn(code: code, server: server)
                    address = server.absoluteString
                }
            }
            .confirmationDialog("Abmelden?", isPresented: $confirmSignOut, titleVisibility: .visible) {
                Button("Abmelden", role: .destructive) { Task { working = true; await model.signOut(); working = false } }
                Button("Abbrechen", role: .cancel) {}
            } message: {
                Text("Das iPad wird auch auf dem Server entfernt. Deine Hefte bleiben auf dem iPad; was noch nicht abgeglichen ist, geht beim nächsten Anmelden mit.")
            }
        }
        .presentationDetents([.large])
    }

    // MARK: Signed in

    @ViewBuilder private var signedIn: some View {
        Section {
            LabeledContent("Angemeldet als", value: model.settings.personName.isEmpty ? "–" : model.settings.personName)
            LabeledContent("Server", value: model.settings.url?.host() ?? model.settings.urlString)
            Button { Task { await test() } } label: {
                HStack { Text("Verbindung testen"); if working { Spacer(); ProgressView() } }
            }
            .disabled(working)
            if let testResult { Text(testResult).font(.footnote).foregroundStyle(.secondary) }
            Button("Abmelden", role: .destructive) { confirmSignOut = true }
                .disabled(working)
        } header: {
            Text("Server")
        } footer: {
            Text("Dieses iPad hat einen eigenen Schlüssel. Auf dem Server siehst du es unter Einstellungen, Geräte, und kannst es dort auch entfernen.")
        }
    }

    // MARK: Not signed in

    @ViewBuilder private var signIn: some View {
        Section {
            TextField("https://notizen.example.de", text: $address)
                .keyboardType(.URL).textInputAutocapitalization(.never).autocorrectionDisabled()
            Button { Task { await signInWithWindow() } } label: {
                HStack { Text("Anmelden"); if working { Spacer(); ProgressView() } }
            }
            .disabled(working || !canSignIn)
            Button("Mit Code verbinden") { showCode = true }
                .disabled(working)
            if let message { Text(message).font(.footnote).foregroundStyle(.red) }
        } header: {
            Text("Server")
        } footer: {
            Text(addressHint)
        }
    }

    private var canSignIn: Bool {
        guard let server else { return false }
        return Pairing.allows(server)
    }

    private var addressHint: String {
        if let server, !Pairing.allows(server) { return "Anmelden geht nur über https. Prüfe die Adresse." }
        return "Trag einmal die Adresse deines Servers ein und melde dich an, wie im Browser. Ohne Server funktioniert alles weiter, nur das Sichern auf dem Server fehlt."
    }

    /// The sign-in window: the normal sign-in of the server in a private window of the system (nothing from Safari is taken over), which hands a pairing code back.
    private func signInWithWindow() async {
        guard let server, Pairing.allows(server) else { return }
        let state = Pairing.newState()
        guard let start = Pairing.signInURL(server: server, state: state) else { return }
        message = nil
        working = true
        defer { working = false }
        do {
            let answer = try await webAuthenticationSession.authenticate(using: start, callback: .customScheme(Pairing.callbackScheme), preferredBrowserSession: .ephemeral, additionalHeaderFields: [:])
            switch Pairing.readAnswer(answer, state: state) {
            case .code(let code):
                try await model.signIn(code: code, server: server)
            case .failed, nil:
                message = "Die Anmeldung hat nicht geklappt. Versuch es noch einmal."
            }
        } catch let error as ASWebAuthenticationSessionError where error.code == .canceledLogin {
            // she closed the window: nothing to say
        } catch let error as PairingError {
            message = PairingCodeView.text(for: error)
        } catch {
            message = "Das Anmeldefenster ließ sich nicht öffnen. Prüfe die Adresse."
        }
    }

    private func test() async {
        guard let url = model.settings.url else { return }
        working = true
        defer { working = false }
        do {
            let info = try await URLSessionTransport(baseURL: url, token: model.settings.token).info()
            testResult = info.lastBackupAt == nil ? "Verbunden. Es gibt noch kein Backup auf dem Server." : "Verbunden. Der Server sichert regelmäßig."
        } catch TransportError.unauthorized {
            testResult = "Der Server kennt dieses iPad nicht mehr. Bitte abmelden und neu anmelden."
        } catch TransportError.noNetwork {
            testResult = "Kein Netz. Prüfe das WLAN."
        } catch {
            testResult = "Der Server antwortet nicht. Prüfe die Adresse."
        }
    }
}
