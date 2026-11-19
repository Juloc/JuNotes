import JuNotesCore
import SwiftUI

/// Phase 0: one page to try the Pencil on. Notebooks, auto pages and saving follow in Phase 1.
struct NotebookView: View {
    let subject: Subject

    var body: some View {
        PencilCanvas()
            .ignoresSafeArea(.container, edges: .bottom)
            .navigationTitle(subject.name)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .principal) {
                    if let position = AutoPages.position(of: 0, in: 1) {
                        Text("Seite \(position.current) von \(position.total)")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    }
                }
            }
    }
}
