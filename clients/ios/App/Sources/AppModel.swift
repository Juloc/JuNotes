import Foundation
import JuNotesCore
import Observation

enum SidebarItem: Hashable {
    case today
    case cards
    case folder(UUID)
    case grades
    case trash
    /// The top level of the library: the folders and the notebooks that lie in no folder.
    case root
}

/// What the whole app shares: the local database, the library the sidebar and lists show, and the sync with the server.
@MainActor
@Observable
final class AppModel {
    let store: SQLiteStore
    var snapshot: SyncSnapshot
    /// Every folder, at any depth. A folder that is a subject ("Fach") also shows up in `subjects`.
    var folders: [Subject] = []
    /// How many notebooks lie directly in each folder, for the subtitle of its card.
    var notebookCounts: [UUID: Int] = [:]
    var notebooks: [Notebook] = []
    var trashed: [Notebook] = []
    var grades: [Grade] = []
    var cards: [Flashcard] = []
    /// Counts up when a page was changed by the app (an accepted suggestion, say), so open notebooks reload their pages.
    var pageRevision = 0
    var events: [CalendarEvent] = []
    var news: [SchoolNews] = []
    /// Set when "Neue Seite für jetzt" made a page: the notebook list opens that notebook.
    var pendingNotebookID: UUID?
    var pendingPageID: UUID?
    var scannerOrigin: ScanOrigin?
    var learnRequest: LearnRequest?
    /// A message for her, shown once: the result of sending to Paperless, a failed export.
    var notice: String?
    var shareItem: ShareItem?
    var pendingScan: PendingScan?
    /// Counts up when files arrived from the server, so pages that waited for their scan look again.
    var assetRevision = 0
    var sidebar: SidebarItem?
    /// True while a notebook is open: the sidebar steps aside so the page gets the whole width.
    var writing = false
    /// Locked notebooks she opened since the app was last in the background.
    var unlocked: Set<UUID> = []
    /// Notebooks with data about children that she allowed to go to the AI since the app was last in the background.
    var aiAllowed: Set<UUID> = []
    /// Objects on pages (photos, text boxes, stickers) can be moved, turned and resized while this is on.
    var editingObjects = false
    var selectedObject: PageObject?
    /// Counts up when an object changed, so the pages read their objects again.
    var objectRevision = 0
    var settings: ServerSettings

    private let schoolCache: URL
    let plans: StudyPlanBook
    private var syncTask: Task<Void, Never>?
    private var syncing = false

    init() {
        let base = Demo.isOn ? FileManager.default.temporaryDirectory.appendingPathComponent("JuNotesDemo-\(UUID().uuidString)", isDirectory: true) : FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        let directory = base.appendingPathComponent("JuNotes", isDirectory: true)
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        var failed = false
        if let store = try? SQLiteStore(path: Demo.isOn ? ":memory:" : directory.appendingPathComponent("junotes.sqlite").path) {
            self.store = store
        } else {
            // Without a database the app still opens, and the status line says plainly that nothing is being saved.
            self.store = try! SQLiteStore(path: ":memory:")
            failed = true
        }
        let settings = ServerSettings.load()
        self.settings = settings
        snapshot = SyncSnapshot(isConfigured: settings.isComplete, localWriteFailed: failed)
        schoolCache = directory.appendingPathComponent("school.json")
        plans = StudyPlanBook(directory: directory)
        sidebar = .today
        loadSchoolCache()
    }

    var subjects: [Subject] { folders.filter(\.isSubject) }
    /// The subjects that count now: not of a finished school year, and of the current one when there is a folder for it.
    var activeSubjects: [Subject] { tree.activeSubjects(now: Date()) }
    var activeCards: [Flashcard] {
        let ids = Set(activeSubjects.map(\.id))
        return cards.filter { ids.contains($0.subjectID) }
    }
    var tree: FolderTree { FolderTree(folders) }

    var selectedFolder: Subject? {
        if case .folder(let id) = sidebar { folders.first { $0.id == id } } else { nil }
    }

    /// The folders directly inside the one that is open (the top level on the library's first page).
    var subfolders: [Subject] { tree.children(of: selectedFolder?.id) }

    /// The subject a notebook belongs to: its own folder if that is a subject, or the nearest one above.
    func subject(of notebook: Notebook) -> Subject? { tree.nearestSubject(from: notebook.subjectID) }

    // MARK: Library

    func reload() async {
        folders = (try? await store.subjects()) ?? []
        grades = (try? await store.grades()) ?? []
        cards = (try? await store.cards()) ?? []
        let everyNotebook = (try? await store.allNotebooks()) ?? []
        SpotlightIndex.update(tree: tree, notebooks: everyNotebook)
        notebookCounts = Dictionary(grouping: everyNotebook.compactMap(\.subjectID), by: { $0 }).mapValues(\.count)
        await reloadNotebooks()
        trashed = ((try? await store.trashedNotebooks()) ?? []).sorted { ($0.trashedAt ?? .distantPast) > ($1.trashedAt ?? .distantPast) }
    }

    func reloadNotebooks() async {
        if let id = selectedFolder?.id {
            notebooks = (try? await store.notebooks(subjectID: id)) ?? []
        } else {
            // The top level also shows notebooks whose folder is gone, so none is ever lost from view.
            let everyNotebook = ((try? await store.allNotebooks()) ?? []).sorted { $0.createdAt < $1.createdAt }
            notebooks = everyNotebook.filter { notebook in notebook.subjectID.map { tree.folder($0) == nil } ?? true }
        }
    }

    func select(_ item: SidebarItem?) async {
        sidebar = item
        await reloadNotebooks()
    }

    @discardableResult
    func addFolder(name: String, color: String, symbol: String, isSubject: Bool, cover: Cover?, in parent: UUID?, schoolYear: Int? = nil, closed: Bool = false) async -> Subject {
        let next = (tree.children(of: parent).map(\.sort).max() ?? -1) + 1
        let folder = Subject(name: name, color: color, symbol: symbol, sort: next, folderID: parent, isSubject: isSubject && schoolYear == nil, cover: cover, schoolYear: schoolYear, closed: closed && schoolYear != nil)
        await write { try await self.store.put(folder) }
        await reload()
        return folder
    }

    func update(_ folder: Subject) async {
        await write { try await self.store.put(folder) }
        await reload()
    }

    func update(_ notebook: Notebook) async {
        await write { try await self.store.put(notebook) }
        await reload()
    }

    func move(_ folder: Subject, to parent: UUID?) async {
        guard folder.folderID != parent, tree.canMove(folder.id, into: parent) else { return }
        var moved = folder
        moved.folderID = parent
        moved.sort = (tree.children(of: parent).map(\.sort).max() ?? -1) + 1
        await update(moved)
    }

    func move(_ notebook: Notebook, to parent: UUID?) async {
        guard notebook.subjectID != parent else { return }
        var moved = notebook
        moved.subjectID = parent
        await update(moved)
    }

    /// Only an empty folder can be deleted; marks and cards of a subject go with it. A message says what is in the way.
    func deleteFolder(_ folder: Subject) async {
        let inside = (try? await store.notebooks(subjectID: folder.id, includeTrashed: true)) ?? []
        guard tree.children(of: folder.id).isEmpty, inside.isEmpty else {
            notice = "„\(folder.name)“ ist nicht leer. Verschiebe oder lösche zuerst, was darin liegt."
            return
        }
        for grade in grades where grade.subjectID == folder.id { await write { try await self.store.write(grade.deletion()) } }
        for card in cards where card.subjectID == folder.id { await write { try await self.store.write(card.deletion()) } }
        await write { try await self.store.write(folder.deletion()) }
        if selectedFolder?.id == folder.id { sidebar = folder.folderID.map { .folder($0) } ?? .root }
        await reload()
    }

    /// Something dragged onto a folder (or the top level): `n:<id>` is a notebook, `f:<id>` a folder.
    func drop(_ items: [String], onto parent: UUID?) async -> Bool {
        var moved = false
        for item in items {
            guard item.count > 2, let id = UUID(uuidString: String(item.dropFirst(2))) else { continue }
            if item.hasPrefix("n:"), let notebook = try? await store.notebook(id: id) {
                await move(notebook, to: parent)
                moved = true
            } else if item.hasPrefix("f:"), let folder = tree.folder(id) {
                await move(folder, to: parent)
                moved = true
            }
        }
        return moved
    }

    @discardableResult
    func addNotebook(title: String, type: NotebookKind, template: PageTemplate, cover: Cover? = nil) async -> Notebook {
        let notebook = Notebook(subjectID: selectedFolder?.id, title: title, type: type, template: template, cover: cover)
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
            await refreshSchool(using: transport)
            await syncAssets(using: transport)
            await reload()
        }
        await refreshReminders()
    }

    func apply(_ new: ServerSettings) async {
        new.save()
        settings = new
        await syncNow()
    }

    // MARK: School

    private struct SchoolCache: Codable {
        var events: [CalendarEvent]
        var news: [SchoolNews]
    }

    private func loadSchoolCache() {
        guard let data = try? Data(contentsOf: schoolCache), let cache = try? JSONDecoder().decode(SchoolCache.self, from: data) else { return }
        events = cache.events
        news = cache.news
    }

    /// The timetable and news from the server; the last good copy stays on the device, so "Heute" works offline.
    private func refreshSchool(using transport: URLSessionTransport) async {
        let now = Date()
        guard let fetched = try? await transport.calendar(from: now.addingTimeInterval(-86400), to: now.addingTimeInterval(60 * 86400)) else { return }
        events = fetched
        if let items = try? await transport.news(limit: 20) { news = items }
        if let data = try? JSONEncoder().encode(SchoolCache(events: events, news: news)) { try? data.write(to: schoolCache, options: .atomic) }
    }

    func cockpit(now: Date = Date()) -> Cockpit {
        Cockpit(now: now, timetable: Timetable(events: events), subjects: activeSubjects)
    }

    /// "Neue Seite für jetzt": the notebook of the lesson that runs, with a fresh page headed by the day and the subject.
    func startPageForNow() async {
        let now = Date()
        guard let subject = cockpit(now: now).suggestedSubject else { return }

        let existing = ((try? await store.notebooks(subjectID: subject.id)) ?? []).filter { $0.type == .pages }
        let notebook: Notebook
        if let latest = existing.last {
            notebook = latest
        } else {
            notebook = Notebook(subjectID: subject.id, title: subject.name)
            await write { try await self.store.put(notebook) }
        }

        let pages = (try? await store.pages(notebookID: notebook.id)) ?? []
        let heading = PageHeading.make(date: now, subject: subject.name)
        let page = Page(notebookID: notebook.id, sort: PageOrder.key(after: pages.last?.sort, before: nil), heading: heading)
        await write { try await self.store.put(page) }

        sidebar = .folder(subject.id)
        await reloadNotebooks()
        pendingNotebookID = notebook.id
    }

    // MARK: Grades

    func grades(of subject: Subject) -> [Grade] { grades.filter { $0.subjectID == subject.id } }

    func addGrade(_ grade: Grade) async {
        await write { try await self.store.put(grade) }
        grades = (try? await store.grades()) ?? grades
    }

    func deleteGrade(_ grade: Grade) async {
        await write { try await self.store.write(grade.deletion()) }
        grades = (try? await store.grades()) ?? grades
    }

    /// Does what a shortcut asked for, once the app is up.
    func consumePendingAction() async {
        guard let action = PendingActions.next else { return }
        PendingActions.next = nil
        switch action {
        case .startPageForNow:
            if cockpit().suggestedSubject == nil { notice = "Zu dieser Zeit läuft keine Stunde, die zu einem deiner Fächer passt." } else { await startPageForNow() }
        case .scan:
            scannerOrigin = ScanOrigin()
        }
    }
}
