import JuNotesCore
import SwiftUI

/// A notebook. Pages run one below the other, and writing near the bottom of the last one adds the next.
struct NotebookView: View {
    let notebook: Notebook

    @Environment(AppModel.self) private var model
    @State private var pages: [Page] = []
    @State private var strokeCounts: [UUID: Int] = [:]
    @State private var currentPageID: UUID?

    var body: some View {
        Group {
            if notebook.type == .whiteboard { whiteboard } else { pageList }
        }
        .background(Color(.systemGroupedBackground))
        .navigationTitle(notebook.title)
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .principal) { positionLabel }
            ToolbarItem(placement: .primaryAction) {
                Menu {
                    Button("Als PDF teilen", systemImage: "square.and.arrow.up") { Task { if let url = await model.exportPDF(notebook) { model.shareItem = ShareItem(url: url) } } }
                    Button("An Paperless senden", systemImage: "archivebox") { Task { await model.sendToPaperless(notebook) } }
                } label: {
                    Label("Teilen", systemImage: "square.and.arrow.up")
                }
            }
            ToolbarItem(placement: .primaryAction) { LearnMenu(notebook: notebook, pageID: currentPageID) }
            ToolbarItem(placement: .primaryAction) {
                Button("Scannen", systemImage: "doc.viewfinder") { model.scannerOrigin = ScanOrigin(notebook: notebook, afterPageID: currentPageID) }
            }
        }
        .task { await load() }
        .onChange(of: model.pageRevision) { Task { await reloadPages() } }
        .onDisappear { Task { await trimAutoPages() } }
    }

    private var pageList: some View {
        GeometryReader { geometry in
            let width = min(geometry.size.width - 48, 820)
            ScrollView {
                LazyVStack(spacing: 24) {
                    ForEach(pages) { page in
                        PageView(page: page, notebook: notebook, displayWidth: width,
                                 onStrokeCount: { strokeCounts[page.id] = $0 },
                                 onNearBottom: { Task { await nearBottom(of: page) } })
                            .id(page.id)
                            .contextMenu {
                                Button("Seite löschen", systemImage: "trash", role: .destructive) { Task { await trash(page) } }
                            }
                    }
                    Button("Neue Seite", systemImage: "plus") { Task { await addPage(auto: false) } }
                        .buttonStyle(.bordered)
                        .padding(.bottom, 120)
                }
                .scrollTargetLayout()
                .padding(.top, 24)
                .frame(maxWidth: .infinity)
            }
            .scrollPosition(id: $currentPageID)
        }
    }

    private var whiteboard: some View {
        ScrollView([.horizontal, .vertical]) {
            if let page = pages.first {
                PageView(page: page, notebook: notebook, displayWidth: PageGeometry.whiteboardWidth,
                         onStrokeCount: { strokeCounts[page.id] = $0 }, onNearBottom: {})
                    .padding(24)
            }
        }
    }

    @ViewBuilder private var positionLabel: some View {
        if notebook.type == .pages, let position = AutoPages.position(of: pages.firstIndex { $0.id == currentPageID } ?? 0, in: pages.count) {
            Text("Seite \(position.current) von \(position.total)")
                .font(.footnote)
                .foregroundStyle(.secondary)
        }
    }

    // MARK: Pages

    private func load() async {
        var loaded = (try? await model.store.pages(notebookID: notebook.id)) ?? []
        if loaded.isEmpty {
            let first = Page(notebookID: notebook.id, sort: PageOrder.key(after: nil, before: nil))
            await model.write { try await model.store.put(first) }
            loaded = [first]
            strokeCounts[first.id] = 0
        }
        pages = loaded
        // A search result or a fresh scan asks for one page.
        if let wanted = model.pendingPageID, loaded.contains(where: { $0.id == wanted }) { currentPageID = wanted } else { currentPageID = loaded.first?.id }
        model.pendingPageID = nil
    }

    /// The app changed a page (an accepted suggestion): read the pages again without losing the place.
    private func reloadPages() async {
        let loaded = (try? await model.store.pages(notebookID: notebook.id)) ?? pages
        if !loaded.isEmpty { pages = loaded }
    }

    private func addPage(auto: Bool) async {
        let page = Page(notebookID: notebook.id, sort: PageOrder.key(after: pages.last?.sort, before: nil), autoCreated: auto)
        await model.write { try await model.store.put(page) }
        strokeCounts[page.id] = 0
        pages.append(page)
        if !auto { currentPageID = page.id }
    }

    private func nearBottom(of page: Page) async {
        guard page.id == pages.last?.id else { return }
        await addPage(auto: true)
    }

    private func trash(_ page: Page) async {
        await model.write { try await model.store.put(page.movedToTrash(at: Date())) }
        pages.removeAll { $0.id == page.id }
        if pages.isEmpty { await load() }
    }

    /// Empty pages the app added itself are removed again when she leaves; pages she added or wrote on stay.
    private func trimAutoPages() async {
        let states = pages.map { PageState(id: $0.id, isEmpty: strokeCounts[$0.id] == 0 && $0.content == .drawing, wasAutoCreated: $0.autoCreated) }
        let kept = Set(AutoPages.trimmed(states).map(\.id))
        for page in pages where !kept.contains(page.id) {
            await model.write { try await model.store.write(page.deletion()) }
        }
    }
}
