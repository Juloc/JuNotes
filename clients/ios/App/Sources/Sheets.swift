import JuNotesCore
import SwiftUI

struct NewSubjectSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var name = ""
    @State private var color = Self.colors[0]
    @State private var symbol = Self.symbols[0]

    static let colors = ["#0a84ff", "#ff9f0a", "#30d158", "#64d2ff", "#bf5af2", "#ff375f", "#ac8e68", "#8e8e93"]
    static let symbols = ["function", "text.book.closed", "character.bubble", "leaf", "atom", "globe.europe.africa", "paintpalette", "music.note", "figure.run", "laptopcomputer"]

    var body: some View {
        NavigationStack {
            Form {
                Section("Name") { TextField("Zum Beispiel Mathe", text: $name) }
                Section("Farbe") {
                    LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: 8)) {
                        ForEach(Self.colors, id: \.self) { value in
                            Circle().fill(Color(uiColor: StrokeConversion.color(hex: value))).frame(width: 34, height: 34)
                                .overlay { if value == color { Image(systemName: "checkmark").foregroundStyle(.white) } }
                                .onTapGesture { color = value }
                        }
                    }
                }
                Section("Symbol") {
                    LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: 5)) {
                        ForEach(Self.symbols, id: \.self) { value in
                            Image(systemName: value).font(.title2).frame(width: 44, height: 44)
                                .background(value == symbol ? Color.accentColor.opacity(0.2) : .clear, in: RoundedRectangle(cornerRadius: 10))
                                .onTapGesture { symbol = value }
                        }
                    }
                }
            }
            .navigationTitle("Neues Fach")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Anlegen") {
                        Task { await model.addSubject(name: name.trimmingCharacters(in: .whitespaces), color: color, symbol: symbol); dismiss() }
                    }
                    .disabled(name.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            }
        }
        .presentationDetents([.medium, .large])
    }
}

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
