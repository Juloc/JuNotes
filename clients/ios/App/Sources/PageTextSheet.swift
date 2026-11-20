import JuNotesCore
import SwiftUI
import UIKit

/// The text the iPad read from her handwriting, scans and PDFs: for one page or the whole notebook, to select, copy or share.
struct PageTextSheet: View {
    let notebook: Notebook
    let pageID: UUID?

    enum Scope: Hashable {
        case page, notebook
    }

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var scope = Scope.page
    @State private var text = ""
    @State private var loaded = false

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    Picker("Umfang", selection: $scope) {
                        Text("Diese Seite").tag(Scope.page)
                        Text("Ganzes Heft").tag(Scope.notebook)
                    }
                    .pickerStyle(.segmented)
                    if text.isEmpty && loaded {
                        ContentUnavailableView("Noch kein Text erkannt", systemImage: "text.viewfinder", description: Text("Das iPad liest die Schrift im Hintergrund, kurz nachdem du aufgehört hast zu schreiben. Schau gleich noch einmal."))
                    } else {
                        Text(text)
                            .textSelection(.enabled)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                .padding(20)
            }
            .navigationTitle("Erkannter Text")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Schließen") { dismiss() } }
                ToolbarItem(placement: .primaryAction) {
                    Button("Kopieren", systemImage: "doc.on.doc") { UIPasteboard.general.string = text }.disabled(text.isEmpty)
                }
                ToolbarItem(placement: .primaryAction) {
                    ShareLink(item: text) { Label("Teilen", systemImage: "square.and.arrow.up") }.disabled(text.isEmpty)
                }
            }
            .task(id: scope) { await load() }
        }
        .presentationDetents([.medium, .large])
    }

    private func load() async {
        switch scope {
        case .page:
            if let pageID { text = (try? await model.store.indexedText(pageID: pageID)) ?? "" } else { text = "" }
        case .notebook:
            let pages = (try? await model.store.indexedTexts(notebookID: notebook.id)) ?? []
            text = pages.map(\.text).filter { !$0.isEmpty }.joined(separator: "\n\n")
        }
        loaded = true
    }
}
