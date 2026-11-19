import JuNotesCore
import SwiftUI

struct NotebookListView: View {
    @Environment(AppModel.self) private var model
    @State private var showNew = false
    @State private var path: [UUID] = []

    var body: some View {
        NavigationStack(path: $path) { content }
            .task(id: model.pendingNotebookID) {
                // "Neue Seite für jetzt" asked for a notebook to be opened.
                if let id = model.pendingNotebookID {
                    model.pendingNotebookID = nil
                    path = [id]
                }
            }
    }

    private var content: some View {
        Group {
            if model.selectedSubject != nil || model.sidebar == .loose {
                if model.notebooks.isEmpty {
                    ContentUnavailableView {
                        Label("Noch kein Heft in \(model.selectedSubject?.name ?? "Lose Blätter")", systemImage: "book.closed")
                    } description: {
                        Text("Lege das erste Heft an.")
                    } actions: {
                        Button("Neues Heft", systemImage: "plus") { showNew = true }.buttonStyle(.borderedProminent)
                    }
                } else {
                    ScrollView {
                        LazyVGrid(columns: [GridItem(.adaptive(minimum: 170), spacing: 20)], spacing: 24) {
                            ForEach(model.notebooks) { notebook in
                                NavigationLink(value: notebook.id) { card(notebook, color: model.selectedSubject?.color ?? "#8e8e93") }
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
        .navigationTitle(model.selectedSubject?.name ?? (model.sidebar == .loose ? "Lose Blätter" : "JuNotes"))
        .navigationDestination(for: UUID.self) { id in
            if let notebook = model.notebooks.first(where: { $0.id == id }) { NotebookView(notebook: notebook) }
        }
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button("Scannen", systemImage: "doc.viewfinder") { model.scannerOrigin = ScanOrigin() }
            }
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
