import JuNotesCore
import SwiftUI

struct TrashView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        Group {
            if model.trashed.isEmpty {
                ContentUnavailableView("Der Papierkorb ist leer", systemImage: "trash", description: Text("Gelöschte Hefte bleiben hier 30 Tage und lassen sich zurückholen."))
            } else {
                List(model.trashed) { notebook in
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(notebook.title).font(.headline)
                            if let trashedAt = notebook.trashedAt {
                                let days = Trash.daysLeft(trashedAt: trashedAt, now: Date())
                                Text(days == 1 ? "Wird morgen endgültig gelöscht" : "Wird in \(days) Tagen endgültig gelöscht").font(.footnote).foregroundStyle(.secondary)
                            }
                        }
                        Spacer()
                        Button("Zurückholen") { Task { await model.restore(notebook) } }.buttonStyle(.bordered)
                    }
                }
            }
        }
        .navigationTitle("Papierkorb")
    }
}
