import JuNotesCore
import SwiftUI

/// All pages of a notebook as small pages: jump to one, reorder by dragging, duplicate, bookmark, move to another notebook or delete.
struct PageGridSheet: View {
    let notebook: Notebook
    let onSelect: (UUID) -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var pages: [Page] = []
    @State private var onlyBookmarks = false
    @State private var movingPage: Page?

    private let columns = [GridItem(.adaptive(minimum: 130), spacing: 16)]

    private var shown: [(number: Int, page: Page)] {
        pages.enumerated().map { (number: $0.offset + 1, page: $0.element) }.filter { !onlyBookmarks || $0.page.bookmarked }
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 16) {
                    Picker("Anzeige", selection: $onlyBookmarks) {
                        Text("Alle Seiten").tag(false)
                        Text("Lesezeichen").tag(true)
                    }
                    .pickerStyle(.segmented)
                    if shown.isEmpty {
                        ContentUnavailableView(onlyBookmarks ? "Keine Lesezeichen" : "Keine Seiten", systemImage: "bookmark", description: Text(onlyBookmarks ? "Halte eine Seite gedrückt und wähle „Lesezeichen“." : ""))
                    }
                    LazyVGrid(columns: columns, spacing: 20) {
                        ForEach(shown, id: \.page.id) { entry in cell(entry.page, number: entry.number) }
                    }
                    if !onlyBookmarks, pages.count > 1 {
                        Text("Zum Umsortieren eine Seite auf eine andere ziehen. Sie landet davor.")
                            .font(.footnote).foregroundStyle(.secondary)
                    }
                }
                .padding(20)
            }
            .navigationTitle(notebook.title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Fertig") { dismiss() } } }
            .task { await refresh() }
            .sheet(item: $movingPage) { page in
                OtherNotebookSheet(excluding: notebook.id) { target in Task { await move(page, to: target) } }
            }
        }
        .presentationDetents([.large])
    }

    private func cell(_ page: Page, number: Int) -> some View {
        VStack(spacing: 6) {
            PageThumbnail(page: page, template: page.template ?? notebook.template)
                .overlay(alignment: .topTrailing) {
                    if page.bookmarked { Image(systemName: "bookmark.fill").foregroundStyle(Brand.accent).padding(4) }
                }
            Text(page.heading.map { "\(number) · \($0)" } ?? "\(number)").font(.caption).foregroundStyle(.secondary).lineLimit(1)
        }
        .contentShape(Rectangle())
        .onTapGesture { onSelect(page.id); dismiss() }
        .draggable("p:\(page.id.uuidString)")
        .dropDestination(for: String.self) { items, _ in
            guard let item = items.first, item.hasPrefix("p:"), let id = UUID(uuidString: String(item.dropFirst(2))) else { return false }
            Task { await reorder(id, before: page) }
            return true
        }
        .contextMenu {
            Button(page.bookmarked ? "Lesezeichen entfernen" : "Lesezeichen", systemImage: page.bookmarked ? "bookmark.slash" : "bookmark") { Task { await toggleBookmark(page) } }
            Button("Duplizieren", systemImage: "plus.square.on.square") { Task { await duplicate(page) } }
            Button("In anderes Heft", systemImage: "arrowshape.turn.up.right") { movingPage = page }
            Button("Löschen", systemImage: "trash", role: .destructive) { Task { await trash(page) } }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Seite \(number)")
        .accessibilityAddTraits(.isButton)
    }

    // MARK: Actions

    private func refresh() async {
        pages = (try? await model.store.pages(notebookID: notebook.id)) ?? []
    }

    private func changed() async {
        await refresh()
        model.pageRevision += 1
    }

    private func toggleBookmark(_ page: Page) async {
        var copy = page
        copy.bookmarked.toggle()
        await model.write { try await model.store.put(copy) }
        await changed()
    }

    private func reorder(_ id: UUID, before target: Page) async {
        guard id != target.id, let moving = pages.first(where: { $0.id == id }), let index = pages.firstIndex(where: { $0.id == target.id }) else { return }
        let previous = index > 0 ? pages[index - 1] : nil
        if previous?.id == id { return }
        var copy = moving
        copy.sort = PageOrder.key(after: previous?.sort, before: target.sort)
        await model.write { try await model.store.put(copy) }
        await changed()
    }

    private func duplicate(_ page: Page) async {
        guard let index = pages.firstIndex(where: { $0.id == page.id }) else { return }
        var copy = page
        copy.id = UUID()
        copy.bookmarked = false
        copy.sort = PageOrder.key(after: page.sort, before: index + 1 < pages.count ? pages[index + 1].sort : nil)
        await model.write { try await model.store.put(copy) }
        for stroke in (try? await model.store.strokes(pageID: page.id)) ?? [] {
            var duplicate = stroke
            duplicate.id = UUID()
            await model.write { try await model.store.put(duplicate, on: copy.id) }
        }
        await changed()
    }

    private func trash(_ page: Page) async {
        guard pages.count > 1 else {
            model.notice = "Ein Heft braucht mindestens eine Seite."
            return
        }
        await model.write { try await model.store.put(page.movedToTrash(at: Date())) }
        await changed()
    }

    private func move(_ page: Page, to target: Notebook) async {
        let last = ((try? await model.store.pages(notebookID: target.id)) ?? []).last?.sort
        var copy = page
        copy.notebookID = target.id
        copy.sort = PageOrder.key(after: last, before: nil)
        copy.bookmarked = false
        await model.write { try await model.store.put(copy) }
        // The text of the page belongs to the new notebook now, or a search would lead to the old one.
        if let text = try? await model.store.indexedText(pageID: page.id) { try? await model.store.index(pageID: page.id, notebookID: target.id, text: text) }
        movingPage = nil
        await changed()
    }
}

/// A list of all notebooks to pick one, with the place they lie in.
struct OtherNotebookSheet: View {
    let excluding: UUID
    let onPick: (Notebook) -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var notebooks: [Notebook] = []

    var body: some View {
        NavigationStack {
            List(notebooks) { notebook in
                Button {
                    onPick(notebook)
                    dismiss()
                } label: {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(notebook.title).foregroundStyle(.primary)
                        let place = model.tree.pathTitle(of: notebook.subjectID)
                        if !place.isEmpty { Text(place).font(.footnote).foregroundStyle(.secondary) }
                    }
                }
            }
            .navigationTitle("In welches Heft?")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } } }
            .task {
                notebooks = ((try? await model.store.allNotebooks()) ?? []).filter { $0.id != excluding && $0.type == .pages && !$0.locked }.sorted { $0.title.localizedStandardCompare($1.title) == .orderedAscending }
            }
        }
        .presentationDetents([.medium, .large])
    }
}
