import AppIntents
import CoreSpotlight
import JuNotesCore

enum PendingAction {
    case startPageForNow
    case scan
}

/// What a shortcut asked for, kept until the app is up to do it: a shortcut can start the app, and the app is not ready at that moment.
@MainActor
enum PendingActions {
    static var next: PendingAction?
}

struct NewPageForNowIntent: AppIntent {
    static let title: LocalizedStringResource = "Neue Seite für jetzt"
    static let description = IntentDescription("Öffnet das Heft der laufenden Stunde auf einer neuen Seite mit Datum und Fach.")
    static let openAppWhenRun = true

    @MainActor
    func perform() async throws -> some IntentResult {
        PendingActions.next = .startPageForNow
        return .result()
    }
}

struct ScanDocumentIntent: AppIntent {
    static let title: LocalizedStringResource = "Blatt scannen"
    static let description = IntentDescription("Öffnet die Kamera, um ein Arbeitsblatt oder eine Heftseite zu scannen.")
    static let openAppWhenRun = true

    @MainActor
    func perform() async throws -> some IntentResult {
        PendingActions.next = .scan
        return .result()
    }
}

struct JuNotesShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(intent: NewPageForNowIntent(), phrases: ["Neue Seite für jetzt in \(.applicationName)", "Neue Seite in \(.applicationName)"], shortTitle: "Neue Seite für jetzt", systemImageName: "plus.circle")
        AppShortcut(intent: ScanDocumentIntent(), phrases: ["Blatt scannen in \(.applicationName)", "Scanne in \(.applicationName)"], shortTitle: "Blatt scannen", systemImageName: "doc.viewfinder")
    }
}

/// Her notebooks in the system search of the iPad: typing a title in Spotlight opens that notebook.
enum SpotlightIndex {
    static func update(tree: FolderTree, notebooks: [Notebook]) {
        let items = notebooks.map { notebook -> CSSearchableItem in
            let attributes = CSSearchableItemAttributeSet(contentType: .content)
            attributes.title = notebook.title
            let subject = notebook.subjectID.map { tree.pathTitle(of: $0) }.flatMap { $0.isEmpty ? nil : $0 }
            attributes.contentDescription = subject.map { "Heft in \($0)" } ?? "Heft"
            attributes.keywords = [subject, notebook.title, "JuNotes"].compactMap { $0 }
            return CSSearchableItem(uniqueIdentifier: notebook.id.uuidString, domainIdentifier: "notebooks", attributeSet: attributes)
        }
        let index = CSSearchableIndex.default()
        // The old entries go first, so a deleted or renamed notebook is not found any more.
        index.deleteSearchableItems(withDomainIdentifiers: ["notebooks"]) { _ in
            if !items.isEmpty { index.indexSearchableItems(items) }
        }
    }

    /// The notebook a tap in Spotlight asked for.
    static func notebookID(from activity: NSUserActivity) -> UUID? {
        (activity.userInfo?[CSSearchableItemActivityIdentifier] as? String).flatMap(UUID.init(uuidString:))
    }
}
