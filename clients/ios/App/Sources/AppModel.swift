import Foundation
import JuNotesCore
import Observation

enum SidebarItem: Hashable {
    case subject(UUID)
    case trash
}

/// What the whole app shares: the local database, the library the sidebar and lists show, and the sync with the server.
@MainActor
@Observable
final class AppModel {
    let store: SQLiteStore
    var snapshot: SyncSnapshot
    var subjects: [Subject] = []
    var notebooks: [Notebook] = []
    var trashed: [Notebook] = []
    var sidebar: SidebarItem?
    var settings: ServerSettings

    private var syncTask: Task<Void, Never>?
    private var syncing = false

    init() {
        let directory = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("JuNotes", isDirectory: true)
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        var failed = false
        if let store = try? SQLiteStore(path: directory.appendingPathComponent("junotes.sqlite").path) {
            self.store = store
        } else {
            // Without a database the app still opens, and the status line says plainly that nothing is being saved.
            self.store = try! SQLiteStore(path: ":memory:")
            failed = true
        }
        let settings = ServerSettings.load()
        self.settings = settings
        snapshot = SyncSnapshot(isConfigured: settings.isComplete, localWriteFailed: failed)
    }

    var selectedSubject: Subject? {
        if case .subject(let id) = sidebar { subjects.first { $0.id == id } } else { nil }
    }

    // MARK: Library

    func reload() async {
        subjects = (try? await store.subjects()) ?? []
        if sidebar == nil { sidebar = subjects.first.map { .subject($0.id) } }
        await reloadNotebooks()
        trashed = ((try? await store.trashedNotebooks()) ?? []).sorted { ($0.trashedAt ?? .distantPast) > ($1.trashedAt ?? .distantPast) }
    }

    func reloadNotebooks() async {
        notebooks = (try? await store.notebooks(subjectID: selectedSubject?.id)) ?? []
    }

    func select(_ item: SidebarItem?) async {
        sidebar = item
        await reloadNotebooks()
    }

    func addSubject(name: String, color: String, symbol: String) async {
        let subject = Subject(name: name, color: color, symbol: symbol, sort: subjects.count)
        await write { try await self.store.put(subject) }
        await reload()
        sidebar = .subject(subject.id)
        await reloadNotebooks()
    }

    @discardableResult
    func addNotebook(title: String, type: NotebookKind, template: PageTemplate) async -> Notebook {
        let notebook = Notebook(subjectID: selectedSubject?.id, title: title, type: type, template: template)
        await write { try await self.store.put(notebook) }
        await reloadNotebooks()
        return notebook
    }

    func moveToTrash(_ notebook: Notebook) async {
        await write { try await self.store.put(notebook.movedToTrash(at: Date())) }
        await reload()
    }

    func restore(_ notebook: Notebook) async {
        await write { try await self.store.put(notebook.restored()) }
        await reload()
    }

    /// Anything past its thirty days goes for good; called when the app opens.
    func purgeTrash() async {
        for notebook in (try? await store.trashedNotebooks()) ?? [] where Trash.isPurgeable(trashedAt: notebook.trashedAt, now: Date()) {
            await write { try await self.store.write(notebook.deletion()) }
        }
    }

    /// Runs a local write; if the database refuses, the status line says so instead of the app pretending all is well.
    func write(_ operation: () async throws -> Void) async {
        do {
            try await operation()
            scheduleSync()
        } catch {
            storeFailed()
        }
    }

    func storeFailed() { snapshot.localWriteFailed = true }

    // MARK: Sync

    /// A few seconds after the last change, so writing a line of text is one sync and not forty.
    func scheduleSync(after delay: Duration = .seconds(3)) {
        syncTask?.cancel()
        syncTask = Task { [weak self] in
            try? await Task.sleep(for: delay)
            guard !Task.isCancelled else { return }
            await self?.syncNow()
        }
    }

    func syncNow() async {
        guard !syncing else { return }
        syncing = true
        defer { syncing = false }

        snapshot.isConfigured = settings.isComplete
        let pending = (try? await store.pendingCount()) ?? 0
        guard settings.isComplete, let url = settings.url else {
            snapshot.pendingChanges = pending
            return
        }

        let transport = URLSessionTransport(baseURL: url, token: settings.token.trimmingCharacters(in: .whitespacesAndNewlines))
        let result = await SyncEngine(transport: transport, store: store).sync()
        snapshot.record(result, pending: (try? await store.pendingCount()) ?? pending)
        if case .synced = result {
            if let info = try? await transport.info() { snapshot.lastBackupAt = info.lastBackupAt }
            await reload()
        }
    }

    func apply(_ new: ServerSettings) async {
        new.save()
        settings = new
        await syncNow()
    }
}
