import JuNotesCore
import SwiftUI

/// A notebook. Pages run one below the other, and writing near the bottom of the last one adds the next.
struct NotebookView: View {
    let notebook: Notebook

    @Environment(AppModel.self) private var model
    @State private var pages: [Page] = []
    @State private var strokeCounts: [UUID: Int] = [:]
    @State private var currentPageID: UUID?
    @State private var showText = false

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
            ToolbarItem(placement: .primaryAction) { Button("Erkannter Text", systemImage: "text.viewfinder") { showText = true } }
            ToolbarItem(placement: .primaryAction) { LearnMenu(notebook: notebook, pageID: currentPageID) }
            ToolbarItem(placement: .primaryAction) {
                Button("Scannen", systemImage: "doc.viewfinder") { model.scannerOrigin = ScanOrigin(notebook: notebook, afterPageID: currentPageID) }
            }
        }
        .task { await load() }
        .sheet(isPresented: $showText) { PageTextSheet(notebook: notebook, pageID: currentPageID) }
        .onAppear { model.writing = true }
        .onChange(of: model.pageRevision) { Task { await reloadPages() } }
        .onDisappear {
            model.writing = false
            Task { await trimAutoPages() }
        }
    }

    private var pageList: some View {
        GeometryReader { geometry in
            // On a wide iPad a rail of small pages stands left of the sheet, the way pages of a book are leafed through.
            let showsRail = geometry.size.width > 760 && pages.count > 1
            let railWidth: CGFloat = showsRail ? 112 : 0
            let width = min(geometry.size.width - 48 - railWidth, 820)
            HStack(spacing: 0) {
                if showsRail { pageRail }
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
    }

    /// Small pages with their numbers; the one she is on is framed. A tap goes there.
    private var pageRail: some View {
        ScrollView {
            LazyVStack(spacing: 14) {
                ForEach(Array(pages.enumerated()), id: \.element.id) { index, page in
                    let current = page.id == currentPageID
                    VStack(spacing: 4) {
                        PageThumbnail(page: page, template: page.template ?? notebook.template)
                            .overlay(RoundedRectangle(cornerRadius: 4, style: .continuous).stroke(Brand.accent, lineWidth: current ? 2.5 : 0))
                        Text("\(index + 1)").font(.caption2.weight(current ? .bold : .regular)).foregroundStyle(current ? Brand.accent : Color.secondary)
                    }
                    .contentShape(Rectangle())
                    .onTapGesture { withAnimation { currentPageID = page.id } }
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel("Seite \(index + 1)")
                    .accessibilityAddTraits(.isButton)
                }
            }
            .padding(.vertical, 24)
            .padding(.horizontal, 12)
        }
        .frame(width: 112)
        .background(.bar)
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
