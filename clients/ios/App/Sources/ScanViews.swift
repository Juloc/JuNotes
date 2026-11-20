import JuNotesCore
import SwiftUI

/// "Wohin mit dem Scan?", asked once, with the likeliest answer already chosen.
struct ScanDestinationSheet: View {
    let scan: PendingScan

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var options: [ScanDestination] = []
    @State private var selected: ScanDestination?
    @State private var chosenSubjectID: UUID?
    @State private var chosenNotebooks: [Notebook] = []
    /// nil means a new worksheet.
    @State private var chosenNotebookID: UUID?
    @State private var working = false

    var body: some View {
        NavigationStack {
            List {
                Section {
                    ForEach(options.indices, id: \.self) { index in
                        let option = options[index]
                        Button { selected = option } label: {
                            HStack(spacing: 12) {
                                Image(systemName: selected == option ? "largecircle.fill.circle" : "circle").foregroundStyle(Color.accentColor)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(title(option)).foregroundStyle(.primary)
                                    if let detail = detail(option) { Text(detail).font(.footnote).foregroundStyle(.secondary) }
                                }
                            }
                        }
                    }
                } header: {
                    Text(scan.images.count == 1 ? "1 Seite gescannt" : "\(scan.images.count) Seiten gescannt")
                }

                if selected == .choose {
                    Section("Ordner und Heft") {
                        Picker("Ordner", selection: $chosenSubjectID) {
                            Text("Ohne Ordner").tag(UUID?.none)
                            ForEach(model.tree.rows) { Text(String(repeating: "    ", count: $0.depth) + $0.folder.name).tag(UUID?.some($0.folder.id)) }
                        }
                        Picker("Heft", selection: $chosenNotebookID) {
                            Text("Neues Arbeitsblatt").tag(UUID?.none)
                            ForEach(chosenNotebooks) { Text($0.title).tag(UUID?.some($0.id)) }
                        }
                    }
                }
            }
            .navigationTitle("Wohin mit dem Scan?")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Verwerfen") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Einfügen") { Task { await insert() } }.disabled(selected == nil || working)
                }
            }
            .task { await prepare() }
            .onChange(of: chosenSubjectID) { _, subjectID in
                Task {
                    if let subjectID {
                        chosenNotebooks = ((try? await model.store.notebooks(subjectID: subjectID)) ?? []).filter { $0.type == .pages }
                    } else {
                        chosenNotebooks = []
                    }
                    chosenNotebookID = nil
                }
            }
        }
        .presentationDetents([.medium, .large])
        .interactiveDismissDisabled(working)
    }

    private func prepare() async {
        let context = await model.scanContext(for: scan.origin)
        options = ScanPlanner.options(context)
        selected = options.first
    }

    private func title(_ option: ScanDestination) -> String {
        switch option {
        case .insertHere(let notebook): "In „\(notebook.title)“ einfügen"
        case .newestNotebook(let subject, let notebook): "\(subject.name): „\(notebook.title)“"
        case .newWorksheet: "Als neues Arbeitsblatt"
        case .choose: "Anderes Heft wählen"
        }
    }

    private func detail(_ option: ScanDestination) -> String? {
        switch option {
        case .insertHere: "Nach der Seite, auf der du gerade bist"
        case .newestNotebook: "Am Ende des Hefts, passend zur Stunde laut Stundenplan"
        case .newWorksheet(let subject): subject.map { "Als eigenes Heft im Fach \($0.name)" } ?? "Als eigenes Heft ohne Fach"
        case .choose: nil
        }
    }

    private func insert() async {
        guard let selected else { return }
        working = true
        let images = scan.images
        switch selected {
        case .insertHere(let notebook): await model.insertScan(images, into: notebook, worksheetIn: nil, afterPageID: scan.origin.afterPageID)
        case .newestNotebook(_, let notebook): await model.insertScan(images, into: notebook, worksheetIn: nil, afterPageID: nil)
        case .newWorksheet(let subject): await model.insertScan(images, into: nil, worksheetIn: subject, afterPageID: nil)
        case .choose:
            if let notebook = chosenNotebooks.first(where: { $0.id == chosenNotebookID }) {
                await model.insertScan(images, into: notebook, worksheetIn: nil, afterPageID: nil)
            } else {
                await model.insertScan(images, into: nil, worksheetIn: model.subjects.first { $0.id == chosenSubjectID }, afterPageID: nil)
            }
        }
        dismiss()
    }
}

/// One search over all notebooks: what she wrote by hand, what she scanned, the names of notebooks.
struct SearchView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var query = ""
    @State private var results: [SearchResult] = []
    @State private var libraryHits: [LibraryHit] = []
    @FocusState private var focused: Bool

    var body: some View {
        NavigationStack {
            List {
                if !query.isEmpty && results.isEmpty && libraryHits.isEmpty {
                    ContentUnavailableView.search(text: query)
                }
                if !libraryHits.isEmpty {
                    Section("Hefte und Ordner") {
                        ForEach(libraryHits) { hit in
                            Button {
                                Task {
                                    if let notebook = hit.notebook { await model.open(notebookID: notebook.id, pageID: nil) } else if let folder = hit.folder { await model.select(.folder(folder.id)) }
                                    dismiss()
                                }
                            } label: {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(hit.title).font(.headline)
                                    Text(hit.subtitle).font(.subheadline).foregroundStyle(.secondary)
                                }
                            }
                            .foregroundStyle(.primary)
                        }
                    }
                }
                ForEach(results) { result in
                    Button {
                        Task { await model.open(notebookID: result.notebook.id, pageID: result.hit.pageID); dismiss() }
                    } label: {
                        VStack(alignment: .leading, spacing: 4) {
                            Text(result.subjectName.map { "\($0) · \(result.notebook.title)" } ?? result.notebook.title).font(.headline)
                            Text(Self.highlighted(result.hit.snippet)).font(.subheadline).foregroundStyle(.secondary).lineLimit(3)
                        }
                    }
                    .foregroundStyle(.primary)
                }
            }
            .navigationTitle("Suchen")
            .navigationBarTitleDisplayMode(.inline)
            .searchable(text: $query, placement: .navigationBarDrawer(displayMode: .always), prompt: "Handschrift, Scans, Hefte")
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Schließen") { dismiss() } } }
            .task(id: query) {
                // Waits a moment after the last keystroke.
                try? await Task.sleep(for: .milliseconds(250))
                guard !Task.isCancelled else { return }
                let trimmed = query.trimmingCharacters(in: .whitespaces)
                results = trimmed.isEmpty ? [] : await model.search(query)
                libraryHits = trimmed.isEmpty ? [] : await model.searchLibrary(query)
            }
        }
        .presentationDetents([.large])
    }

    /// The match is wrapped in « and » by the search; here it becomes bold.
    static func highlighted(_ snippet: String) -> AttributedString {
        var result = AttributedString()
        var rest = Substring(snippet)
        while let open = rest.firstIndex(of: "«") {
            result += AttributedString(rest[..<open])
            rest = rest[rest.index(after: open)...]
            guard let close = rest.firstIndex(of: "»") else { break }
            var match = AttributedString(rest[..<close])
            match.font = .subheadline.bold()
            match.foregroundColor = .primary
            result += match
            rest = rest[rest.index(after: close)...]
        }
        result += AttributedString(rest)
        return result
    }
}
