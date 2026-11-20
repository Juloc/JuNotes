import JuNotesCore
import LocalAuthentication
import UIKit

/// Face ID or the code of the iPad, for notebooks she locked.
enum Biometrics {
    @MainActor
    static func unlock(reason: String) async -> Bool {
        let context = LAContext()
        var error: NSError?
        guard context.canEvaluatePolicy(.deviceOwnerAuthentication, error: &error) else {
            // An iPad without a code cannot ask for one: the lock cannot work there, so the notebook opens.
            return error?.code == LAError.passcodeNotSet.rawValue
        }
        return (try? await context.evaluatePolicy(.deviceOwnerAuthentication, localizedReason: reason)) ?? false
    }
}

/// A folder or notebook found by its name or tag.
struct LibraryHit: Identifiable {
    let id: UUID
    let title: String
    let subtitle: String
    let folder: Subject?
    let notebook: Notebook?
}

extension AppModel {
    /// True when the notebook may be shown: it is not locked, or she has opened it since the app was last in the background.
    func unlock(_ notebook: Notebook) async -> Bool {
        if !notebook.locked || unlocked.contains(notebook.id) { return true }
        guard await Biometrics.unlock(reason: "„\(notebook.title)“ ist gesperrt.") else { return false }
        unlocked.insert(notebook.id)
        return true
    }

    /// Hefte und Ordner by name, tag or place, for the search; a locked notebook shows with its name only.
    func searchLibrary(_ text: String) async -> [LibraryHit] {
        let words = text.lowercased().split(separator: " ").map(String.init)
        guard !words.isEmpty else { return [] }
        func matches(_ haystack: String) -> Bool { words.allSatisfy { haystack.lowercased().contains($0) } }
        var hits: [LibraryHit] = []
        for folder in folders {
            let place = tree.pathTitle(of: folder.id)
            if matches(place + " " + folder.tags.joined(separator: " ")) {
                hits.append(LibraryHit(id: folder.id, title: folder.name, subtitle: folder.isSubject ? "Fach · \(place)" : "Ordner · \(place)", folder: folder, notebook: nil))
            }
        }
        for notebook in (try? await store.allNotebooks()) ?? [] {
            let place = tree.pathTitle(of: notebook.subjectID)
            if matches(notebook.title + " " + place + " " + notebook.tags.joined(separator: " ")) {
                hits.append(LibraryHit(id: notebook.id, title: notebook.title, subtitle: place.isEmpty ? "Heft" : "Heft · \(place)", folder: nil, notebook: notebook))
            }
        }
        return Array(hits.prefix(30))
    }

    func toggleFavorite(_ folder: Subject) async {
        var changed = folder
        changed.favorite.toggle()
        await update(changed)
    }

    func toggleFavorite(_ notebook: Notebook) async {
        var changed = notebook
        changed.favorite.toggle()
        await update(changed)
    }
}
