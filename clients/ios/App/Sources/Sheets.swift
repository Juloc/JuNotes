import JuNotesCore
import SwiftUI

struct NewNotebookSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var title = ""
    @State private var type = NotebookKind.pages
    @State private var template = PageTemplate.ruled

    var body: some View {
        NavigationStack {
            Form {
                Section("Name") { TextField("Zum Beispiel Bruchrechnung", text: $title) }
                Section {
                    Picker("Art", selection: $type) {
                        Text("Heft mit Seiten").tag(NotebookKind.pages)
                        Text("Whiteboard, endlos").tag(NotebookKind.whiteboard)
                    }
                    .pickerStyle(.segmented)
                } footer: {
                    Text(type == .pages ? "Die Seiten laufen untereinander. Schreibst du unten weiter, kommt von selbst eine neue." : "Eine große Fläche ohne Seitenränder, zum Beispiel für Mindmaps.")
                }
                Section("Papier") {
                    Picker("Papier", selection: $template) {
                        Text("Liniert").tag(PageTemplate.ruled)
                        Text("Kariert").tag(PageTemplate.grid)
                        Text("Gepunktet").tag(PageTemplate.dotted)
                        Text("Cornell").tag(PageTemplate.cornell)
                        Text("Leer").tag(PageTemplate.blank)
                    }
                    .pickerStyle(.inline)
                    .labelsHidden()
                }
            }
            .navigationTitle("Neues Heft")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Anlegen") {
                        Task { await model.addNotebook(title: title.trimmingCharacters(in: .whitespaces), type: type, template: template); dismiss() }
                    }
                    .disabled(title.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            }
        }
        .presentationDetents([.medium, .large])
    }
}
