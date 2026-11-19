import JuNotesCore
import SwiftUI

struct NotebookListView: View {
    @Environment(AppModel.self) private var model
    @State private var showNew = false

    var body: some View {
        Group {
            if let subject = model.selectedSubject {
                if model.notebooks.isEmpty {
                    ContentUnavailableView {
                        Label("Noch kein Heft in \(subject.name)", systemImage: "book.closed")
                    } description: {
                        Text("Lege das erste Heft an.")
                    } actions: {
                        Button("Neues Heft", systemImage: "plus") { showNew = true }.buttonStyle(.borderedProminent)
                    }
                } else {
                    ScrollView {
                        LazyVGrid(columns: [GridItem(.adaptive(minimum: 170), spacing: 20)], spacing: 24) {
                            ForEach(model.notebooks) { notebook in
                                NavigationLink(value: notebook.id) { card(notebook, color: subject.color) }
                                    .buttonStyle(.plain)
                                    .contextMenu {
                                        Button("In den Papierkorb", systemImage: "trash", role: .destructive) { Task { await model.moveToTrash(notebook) } }
                                    }
                            }
                        }
                        .padding(24)
                    }
                }
            } else {
                ContentUnavailableView("Kein Fach gewählt", systemImage: "books.vertical", description: Text("Lege links ein Fach an oder wähle eines aus."))
            }
        }
        .navigationTitle(model.selectedSubject?.name ?? "JuNotes")
        .navigationDestination(for: UUID.self) { id in
            if let notebook = model.notebooks.first(where: { $0.id == id }) { NotebookView(notebook: notebook) }
        }
        .toolbar {
            if model.selectedSubject != nil {
                ToolbarItem(placement: .primaryAction) {
                    Button("Neues Heft", systemImage: "plus") { showNew = true }
                }
            }
        }
        .sheet(isPresented: $showNew) { NewNotebookSheet() }
    }

    private func card(_ notebook: Notebook, color: String) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            RoundedRectangle(cornerRadius: 10)
                .fill(Color(uiColor: StrokeConversion.color(hex: color)).gradient)
                .aspectRatio(0.78, contentMode: .fit)
                .overlay(alignment: .bottomLeading) {
                    Image(systemName: notebook.type == .whiteboard ? "rectangle.dashed" : "text.book.closed")
                        .font(.title2).foregroundStyle(.white.opacity(0.9)).padding(12)
                }
            Text(notebook.title).font(.headline).lineLimit(2)
            Text(notebook.type == .whiteboard ? "Whiteboard" : "Heft").font(.footnote).foregroundStyle(.secondary)
        }
    }
}
