import JuNotesCore
import Foundation

/// A piece of the notes handed to the learning helper.
struct LearningMaterial {
    var text: String
    var subjectName: String?
    var isEmpty: Bool { text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
}

extension AppModel {
    /// The server's connection, or nil while no server is set up.
    func makeTransport() -> URLSessionTransport? {
        guard settings.isComplete, let url = settings.url else { return nil }
        return URLSessionTransport(baseURL: url, token: settings.token.trimmingCharacters(in: .whitespacesAndNewlines))
    }

    /// What she wrote, as text: of one page or of the whole notebook, never more than the server accepts.
    func material(notebook: Notebook, pageID: UUID?) async -> LearningMaterial {
        var text = ""
        if let pageID {
            text = (try? await store.indexedText(pageID: pageID)) ?? ""
        } else {
            text = ((try? await store.indexedTexts(notebookID: notebook.id)) ?? []).map(\.text).joined(separator: "\n\n")
        }
        var name: String?
        if let subjectID = notebook.subjectID { name = subjects.first { $0.id == subjectID }?.name }
        return LearningMaterial(text: String(text.prefix(28_000)), subjectName: name)
    }

    // MARK: Cards

    func saveCards(_ drafts: [CardDraft], subjectID: UUID, sourcePageID: UUID?) async {
        for draft in drafts {
            let card = Flashcard(subjectID: subjectID, front: draft.front, back: draft.back, sourcePageID: sourcePageID)
            await write { try await self.store.put(card) }
        }
        cards = (try? await store.cards()) ?? cards
    }

    func rate(_ card: Flashcard, _ rating: CardRating) async {
        let updated = card.rated(rating, now: Date())
        await write { try await self.store.put(updated) }
        if let index = cards.firstIndex(where: { $0.id == card.id }) { cards[index] = updated }
    }

    func deleteCard(_ card: Flashcard) async {
        await write { try await self.store.write(card.deletion()) }
        cards.removeAll { $0.id == card.id }
    }

    // MARK: Pages the app writes on

    /// A mock test as a notebook of its own: the tasks printed on the pages, her room to write around them, the solutions behind a button.
    @discardableResult
    func createTestNotebook(_ result: TestResult, subjectID: UUID?) async -> Notebook {
        let notebook = Notebook(subjectID: subjectID, title: result.title, template: .grid)
        await write { try await self.store.put(notebook) }

        let perPage = 3
        let groups = stride(from: 0, to: result.tasks.count, by: perPage).map { Array(result.tasks[$0..<min($0 + perPage, result.tasks.count)]) }
        let keys = PageOrder.keys(appending: groups.count, after: nil)
        var number = 0
        for (group, key) in zip(groups, keys) {
            var printed: [String] = []
            var solutions: [String] = []
            for task in group {
                number += 1
                printed.append("Aufgabe \(number) (\(task.points) \(task.points == 1 ? "Punkt" : "Punkte"))\n\(task.question)")
                solutions.append("Aufgabe \(number): \(task.solution)")
            }
            let page = Page(notebookID: notebook.id, sort: key, heading: "\(result.title) · \(result.totalPoints) Punkte", printedText: printed.joined(separator: "\n\n"), solution: solutions.joined(separator: "\n\n"))
            await write { try await self.store.put(page) }
        }
        await reloadNotebooks()
        return notebook
    }

    /// An accepted suggestion goes in the box on the page, apart from her own writing.
    func addAddition(_ text: String, to pageID: UUID) async {
        guard var page = try? await store.page(id: pageID), !page.additions.contains(text) else { return }
        page.additions.append(text)
        let updated = page
        await write { try await self.store.put(updated) }
        pageRevision += 1
    }

    func removeAddition(at index: Int, from pageID: UUID) async {
        guard var page = try? await store.page(id: pageID), page.additions.indices.contains(index) else { return }
        page.additions.remove(at: index)
        let updated = page
        await write { try await self.store.put(updated) }
        pageRevision += 1
    }
}

/// A sentence for each way the learning helper can fail.
func message(for error: Error) -> String {
    switch error {
    case AIError.notSetUp: "Die KI ist auf dem Server noch nicht eingerichtet. Das macht der Admin in der Weboberfläche."
    case AIError.refused(let text), AIError.failed(let text), AIError.invalid(let text): text
    case AIError.unauthorized: "Der Zugangsschlüssel stimmt nicht. Prüfe ihn in den Einstellungen."
    case AIError.noNetwork: "Kein Netz. Die KI braucht eine Verbindung zum Server."
    case AIError.unreachable: "Der Server antwortet nicht."
    default: "Das hat nicht geklappt. Versuche es gleich noch einmal."
    }
}
