import SwiftUI

/// A subject in the sidebar. Phase 1 reads these from the local database; Phase 2 takes them from the timetable.
struct Subject: Identifiable, Hashable {
    let id: String
    let name: String
    let symbol: String

    static let samples = [
        Subject(id: "math", name: "Mathe", symbol: "function"),
        Subject(id: "german", name: "Deutsch", symbol: "text.book.closed"),
        Subject(id: "english", name: "Englisch", symbol: "character.bubble"),
    ]
}

struct RootView: View {
    @State private var selection: Subject? = Subject.samples.first

    var body: some View {
        NavigationSplitView {
            List(Subject.samples, selection: $selection) { subject in
                Label(subject.name, systemImage: subject.symbol)
                    .tag(subject)
            }
            .navigationTitle("Fächer")
        } detail: {
            if let selection {
                NotebookView(subject: selection)
            } else {
                ContentUnavailableView("Kein Fach gewählt", systemImage: "books.vertical")
            }
        }
        // The status line is always visible: what is saved, synced and backed up.
        .safeAreaInset(edge: .bottom, spacing: 0) {
            StatusLineView()
        }
    }
}
