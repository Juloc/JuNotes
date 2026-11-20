import JuNotesCore
import SwiftUI
import UniformTypeIdentifiers

/// A node of the folder tree for the sidebar's outline.
struct FolderNode: Identifiable {
    let folder: Subject
    let children: [FolderNode]?
    var id: UUID { folder.id }
}

/// A folder with its depth, for flat lists that show the nesting.
struct FolderRow: Identifiable {
    let folder: Subject
    let depth: Int
    var id: UUID { folder.id }
}

extension FolderTree {
    var rows: [FolderRow] { flattened().map { FolderRow(folder: $0.folder, depth: $0.depth) } }

    func nodes(of parent: UUID? = nil, visited: Set<UUID> = []) -> [FolderNode] {
        children(of: parent).filter { !visited.contains($0.id) }.map { folder in
            let inner = nodes(of: folder.id, visited: visited.union([folder.id]))
            return FolderNode(folder: folder, children: inner.isEmpty ? nil : inner)
        }
    }
}

struct FolderEditorTarget: Identifiable {
    let id = UUID()
    var folder: Subject?
    var parent: UUID?
    var isSubject: Bool
}

enum MoveSubject {
    case folder(Subject)
    case notebook(Notebook)
}

struct MoveItem: Identifiable {
    let id = UUID()
    let subject: MoveSubject
}

/// What lies in the open folder, or at the top of the library: folders and notebooks as covers on a shelf.
struct FolderView: View {
    @Environment(AppModel.self) private var model
    @State private var showNewNotebook = false
    @State private var showImporter = false
    @State private var path: [UUID] = []
    @State private var editor: FolderEditorTarget?
    @State private var editingNotebook: Notebook?
    @State private var moving: MoveItem?
    @State private var deleting: Subject?

    private let columns = [GridItem(.adaptive(minimum: 170), spacing: 24)]

    var body: some View {
        NavigationStack(path: $path) { content }
            .task(id: model.folders.count) {
                // Only for the demo screenshots: open the cover editor of a folder.
                if Demo.isOn, Demo.screen == "coverEditor", editor == nil, let folder = model.folders.first(where: { $0.name == "Mathe" }) {
                    editor = FolderEditorTarget(folder: folder, parent: folder.folderID, isSubject: true)
                }
            }
            .task(id: model.pendingNotebookID) {
                // "Neue Seite für jetzt" asked for a notebook to be opened.
                if let id = model.pendingNotebookID {
                    model.pendingNotebookID = nil
                    path = [id]
                }
            }
    }

    private var content: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 28) {
                if model.selectedFolder != nil { breadcrumb }
                if model.subfolders.isEmpty && model.notebooks.isEmpty {
                    ContentUnavailableView {
                        Label(model.selectedFolder == nil ? "Die Bibliothek ist noch leer" : "Hier ist noch nichts", systemImage: "books.vertical")
                    } description: {
                        Text("Lege einen Ordner oder das erste Heft an. Ordner dürfen ineinander liegen, so wie du es magst.")
                    } actions: {
                        HStack {
                            Button("Neuer Ordner", systemImage: "folder.badge.plus") { newFolder(isSubject: false) }.buttonStyle(.bordered)
                            Button("Neues Heft", systemImage: "plus") { showNewNotebook = true }.buttonStyle(.borderedProminent)
                        }
                    }
                    .frame(maxWidth: .infinity)
                }
                if !model.subfolders.isEmpty {
                    VStack(alignment: .leading, spacing: 14) {
                        Text("Ordner").font(.title3.weight(.semibold))
                        LazyVGrid(columns: columns, alignment: .leading, spacing: 28) {
                            ForEach(model.subfolders) { folderCard($0) }
                        }
                    }
                }
                if !model.notebooks.isEmpty {
                    VStack(alignment: .leading, spacing: 14) {
                        if !model.subfolders.isEmpty { Text("Hefte").font(.title3.weight(.semibold)) }
                        LazyVGrid(columns: columns, alignment: .leading, spacing: 28) {
                            ForEach(model.notebooks) { notebookCard($0) }
                        }
                    }
                }
            }
            .padding(24)
        }
        .background(Color(.systemGroupedBackground))
        .navigationTitle(model.selectedFolder?.name ?? "Bibliothek")
        .navigationDestination(for: UUID.self) { id in
            if let notebook = model.notebooks.first(where: { $0.id == id }) { NotebookView(notebook: notebook) }
        }
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button("PDF importieren", systemImage: "square.and.arrow.down") { showImporter = true }
            }
            ToolbarItem(placement: .primaryAction) {
                Button("Scannen", systemImage: "doc.viewfinder") { model.scannerOrigin = ScanOrigin() }
            }
            ToolbarItem(placement: .primaryAction) {
                Menu {
                    Button("Neues Heft", systemImage: "book") { showNewNotebook = true }
                    Button("Neuer Ordner", systemImage: "folder.badge.plus") { newFolder(isSubject: false) }
                    Button("Neues Fach", systemImage: "graduationcap") { newFolder(isSubject: true) }
                } label: {
                    Label("Neu", systemImage: "plus")
                }
            }
        }
        .sheet(isPresented: $showNewNotebook) { NewNotebookSheet() }
        .sheet(item: $editor) { FolderEditorSheet(target: $0) }
        .sheet(item: $editingNotebook) { NotebookEditorSheet(notebook: $0) }
        .sheet(item: $moving) { MoveSheet(item: $0) }
        .confirmationDialog("Ordner löschen?", isPresented: Binding(get: { deleting != nil }, set: { if !$0 { deleting = nil } }), presenting: deleting) { folder in
            Button("„\(folder.name)“ löschen", role: .destructive) { Task { await model.deleteFolder(folder) } }
        } message: { folder in
            Text(folder.isSubject ? "Noten und Karteikarten dieses Fachs werden mit gelöscht. Nur ein leerer Ordner lässt sich löschen." : "Nur ein leerer Ordner lässt sich löschen.")
        }
        .fileImporter(isPresented: $showImporter, allowedContentTypes: [.pdf], allowsMultipleSelection: true) { result in
            guard case .success(let urls) = result else { return }
            Task {
                for url in urls {
                    do { try await model.importPDF(from: url) } catch let error as PDFImportError { model.notice = "\(url.lastPathComponent): \(error.sentence)" } catch { model.notice = "\(url.lastPathComponent) konnte nicht importiert werden." }
                }
            }
        }
    }

    private func newFolder(isSubject: Bool) {
        editor = FolderEditorTarget(folder: nil, parent: model.selectedFolder?.id, isSubject: isSubject)
    }

    // MARK: Breadcrumb

    private var breadcrumb: some View {
        let trail = model.selectedFolder.map { model.tree.path(to: $0.id) } ?? []
        return ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                crumb("Bibliothek", target: nil)
                ForEach(trail) { folder in
                    Image(systemName: "chevron.right").font(.caption2).foregroundStyle(.tertiary)
                    crumb(folder.name, target: folder.id)
                }
            }
        }
    }

    private func crumb(_ title: String, target: UUID?) -> some View {
        Button { Task { await model.select(target.map { .folder($0) } ?? .root) } } label: {
            Text(title).font(.subheadline.weight(.medium)).padding(.horizontal, 10).padding(.vertical, 5)
                .background(Brand.accent.opacity(0.12), in: Capsule())
        }
        .buttonStyle(.plain)
        .foregroundStyle(Brand.accent)
        .dropDestination(for: String.self) { items, _ in
            Task { _ = await model.drop(items, onto: target) }
            return !items.isEmpty
        }
    }

    // MARK: Cards

    private func folderCard(_ folder: Subject) -> some View {
        let cover = folder.cover ?? Cover(style: .gradient, colorA: folder.color, colorB: folder.color, symbol: folder.symbol)
        let notebooks = model.notebookCounts[folder.id] ?? 0
        let inside = model.tree.children(of: folder.id).count
        var parts: [String] = []
        if notebooks > 0 { parts.append(notebooks == 1 ? "1 Heft" : "\(notebooks) Hefte") }
        if inside > 0 { parts.append(inside == 1 ? "1 Ordner" : "\(inside) Ordner") }
        return Button { Task { await model.select(.folder(folder.id)) } } label: {
            VStack(alignment: .leading, spacing: 8) {
                CoverView(cover: cover)
                    .aspectRatio(1.35, contentMode: .fit)
                    .overlay(alignment: .topTrailing) {
                        if folder.isSubject {
                            Image(systemName: "graduationcap.fill").font(.caption).foregroundStyle(.white).padding(6)
                                .background(.black.opacity(0.25), in: Circle()).padding(8)
                        }
                    }
                Text(folder.name).font(.headline).lineLimit(2)
                Text(parts.isEmpty ? "Leer" : parts.joined(separator: " · ")).font(.footnote).foregroundStyle(.secondary)
            }
        }
        .buttonStyle(.plain)
        .draggable("f:\(folder.id.uuidString)")
        .dropDestination(for: String.self) { items, _ in
            Task { _ = await model.drop(items, onto: folder.id) }
            return !items.isEmpty
        }
        .contextMenu {
            Button("Bearbeiten", systemImage: "paintbrush") { editor = FolderEditorTarget(folder: folder, parent: folder.folderID, isSubject: folder.isSubject) }
            Button("Verschieben", systemImage: "folder") { moving = MoveItem(subject: .folder(folder)) }
            Button("Löschen", systemImage: "trash", role: .destructive) { deleting = folder }
        }
    }

    private func notebookCard(_ notebook: Notebook) -> some View {
        NavigationLink(value: notebook.id) { NotebookCover(notebook: notebook, subject: model.subject(of: notebook)) }
            .buttonStyle(.plain)
            .draggable("n:\(notebook.id.uuidString)")
            .contextMenu {
                Button("Name und Cover", systemImage: "paintbrush") { editingNotebook = notebook }
                Button("Verschieben", systemImage: "folder") { moving = MoveItem(subject: .notebook(notebook)) }
                Button("In den Papierkorb", systemImage: "trash", role: .destructive) { Task { await model.moveToTrash(notebook) } }
            }
    }
}

// MARK: Editing a folder

struct FolderEditorSheet: View {
    let target: FolderEditorTarget

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var name: String
    @State private var color: String
    @State private var symbol: String
    @State private var isSubject: Bool
    @State private var customCover: Bool
    @State private var cover: Cover

    init(target: FolderEditorTarget) {
        self.target = target
        let folder = target.folder
        let firstColor = folder?.color ?? Choices.colors[0]
        _name = State(initialValue: folder?.name ?? "")
        _color = State(initialValue: firstColor)
        _symbol = State(initialValue: folder?.symbol ?? (target.isSubject ? "book" : "folder"))
        _isSubject = State(initialValue: folder?.isSubject ?? target.isSubject)
        _customCover = State(initialValue: folder?.cover != nil)
        _cover = State(initialValue: folder?.cover ?? Cover(style: .gradient, colorA: Cover.presets[0].a, colorB: Cover.presets[0].b, symbol: folder?.symbol))
    }

    var body: some View {
        NavigationStack {
            Form {
                Section("Name") { TextField(isSubject ? "Zum Beispiel Mathe" : "Zum Beispiel Schule", text: $name) }
                Section {
                    Toggle("Ist ein Fach", isOn: $isSubject)
                } footer: {
                    Text("Ein Fach hat Noten und Karteikarten und passt zum Stundenplan. Ein Ordner hält nur Hefte und andere Ordner zusammen.")
                }
                Section("Farbe in der Seitenleiste") {
                    LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: 8)) {
                        ForEach(Choices.colors, id: \.self) { value in
                            Circle().fill(Color(hex: value)).frame(width: 34, height: 34)
                                .overlay { if value == color { Image(systemName: "checkmark").foregroundStyle(.white) } }
                                .onTapGesture { color = value }
                        }
                    }
                }
                Section("Symbol in der Seitenleiste") {
                    LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: 6)) {
                        ForEach(Choices.symbols, id: \.self) { value in
                            Image(systemName: value).font(.title3).frame(width: 44, height: 44)
                                .background(value == symbol ? Brand.accent.opacity(0.2) : .clear, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                                .onTapGesture { symbol = value }
                        }
                    }
                }
                Section {
                    Toggle("Eigenes Cover", isOn: $customCover)
                } footer: {
                    Text("Ohne eigenes Cover zeigt der Ordner seine Farbe und sein Symbol.")
                }
                if customCover { CoverEditor(cover: $cover, aspect: 1.35) }
            }
            .navigationTitle(target.folder == nil ? (isSubject ? "Neues Fach" : "Neuer Ordner") : "Bearbeiten")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button(target.folder == nil ? "Anlegen" : "Sichern") { Task { await save() } }
                        .disabled(name.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            }
        }
        .presentationDetents([.large])
    }

    private func save() async {
        let trimmed = name.trimmingCharacters(in: .whitespaces)
        let finalCover = customCover ? cover : nil
        if var folder = target.folder {
            folder.name = trimmed
            folder.color = color
            folder.symbol = symbol
            folder.isSubject = isSubject
            folder.cover = finalCover
            await model.update(folder)
        } else {
            await model.addFolder(name: trimmed, color: color, symbol: symbol, isSubject: isSubject, cover: finalCover, in: target.parent)
        }
        dismiss()
    }
}

// MARK: Editing a notebook

struct NotebookEditorSheet: View {
    let notebook: Notebook

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var title: String
    @State private var customCover: Bool
    @State private var cover: Cover

    init(notebook: Notebook) {
        self.notebook = notebook
        _title = State(initialValue: notebook.title)
        _customCover = State(initialValue: notebook.cover != nil)
        _cover = State(initialValue: notebook.cover ?? Cover(style: .gradient, colorA: Cover.presets[0].a, colorB: Cover.presets[0].b))
    }

    var body: some View {
        NavigationStack {
            Form {
                Section("Name") { TextField("Name des Hefts", text: $title) }
                Section {
                    Toggle("Eigenes Cover", isOn: $customCover)
                } footer: {
                    Text("Ohne eigenes Cover zeigt das Heft seine erste Seite.")
                }
                if customCover { CoverEditor(cover: $cover) }
            }
            .navigationTitle("Name und Cover")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") {
                        Task {
                            var changed = notebook
                            changed.title = title.trimmingCharacters(in: .whitespaces)
                            changed.cover = customCover ? cover : nil
                            await model.update(changed)
                            dismiss()
                        }
                    }
                    .disabled(title.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            }
        }
        .presentationDetents([.large])
    }
}

// MARK: Moving

/// Where to put a folder or notebook: the top of the library or any folder, shown as the tree it is.
struct MoveSheet: View {
    let item: MoveItem

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss

    private var currentParent: UUID? {
        switch item.subject {
        case .folder(let folder): folder.folderID
        case .notebook(let notebook): notebook.subjectID
        }
    }

    private var title: String {
        switch item.subject {
        case .folder(let folder): folder.name
        case .notebook(let notebook): notebook.title
        }
    }

    var body: some View {
        NavigationStack {
            List {
                row("Bibliothek, oberste Ebene", symbol: "books.vertical", depth: 0, target: nil, allowed: true)
                ForEach(model.tree.rows) { entry in
                    row(entry.folder.name, symbol: entry.folder.symbol, depth: entry.depth + 1, target: entry.folder.id, allowed: allowed(entry.folder.id))
                }
            }
            .navigationTitle("„\(title)“ verschieben")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } } }
        }
        .presentationDetents([.medium, .large])
    }

    private func allowed(_ id: UUID) -> Bool {
        if case .folder(let folder) = item.subject { return model.tree.canMove(folder.id, into: id) }
        return true
    }

    private func row(_ name: String, symbol: String, depth: Int, target: UUID?, allowed: Bool) -> some View {
        Button {
            Task {
                switch item.subject {
                case .folder(let folder): await model.move(folder, to: target)
                case .notebook(let notebook): await model.move(notebook, to: target)
                }
                dismiss()
            }
        } label: {
            HStack {
                Image(systemName: symbol).frame(width: 26)
                Text(name)
                Spacer()
                if target == currentParent { Image(systemName: "checkmark").foregroundStyle(Brand.accent) }
            }
            .padding(.leading, CGFloat(depth) * 20)
        }
        .disabled(!allowed)
    }
}
