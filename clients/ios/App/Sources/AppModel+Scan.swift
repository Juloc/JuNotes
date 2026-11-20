import JuNotesCore
import UIKit

/// Where a scan was started: inside a notebook (after this page) or from somewhere else.
struct ScanOrigin: Identifiable {
    let id = UUID()
    var notebook: Notebook?
    var afterPageID: UUID?
}

/// A finished scan waiting for the answer to "Wohin?".
struct PendingScan: Identifiable {
    let id = UUID()
    var images: [UIImage]
    var origin: ScanOrigin
}

struct SearchResult: Identifiable {
    let hit: SearchHit
    let notebook: Notebook
    let subjectName: String?
    var id: UUID { hit.pageID }
}

extension AppModel {
    // MARK: Scanning

    func scanContext(for origin: ScanOrigin) async -> ScanContext {
        let subject = cockpit().suggestedSubject
        var notebooks: [Notebook] = []
        if let subject { notebooks = (try? await store.notebooks(subjectID: subject.id)) ?? [] }
        return ScanContext(openNotebook: origin.notebook, suggestedSubject: subject, notebooksOfSuggestedSubject: notebooks)
    }

    /// Puts the scanned sheets into a notebook as pages (after `afterPageID`, or at the end), or into a new worksheet notebook.
    /// Each sheet is saved on the device first, then read for text in the background, then sent to the server with the next sync.
    func insertScan(_ images: [UIImage], into existing: Notebook?, worksheetIn subject: Subject?, afterPageID: UUID?) async {
        guard !images.isEmpty else { return }

        let notebook: Notebook
        if let existing {
            notebook = existing
        } else {
            notebook = Notebook(subjectID: subject?.id, title: ScanPlanner.worksheetTitle(date: Date()))
            await write { try await self.store.put(notebook) }
        }

        let pages = (try? await store.pages(notebookID: notebook.id)) ?? []
        var after = pages.last?.sort
        var before: String?
        if let afterPageID, let index = pages.firstIndex(where: { $0.id == afterPageID }) {
            after = pages[index].sort
            before = index + 1 < pages.count ? pages[index + 1].sort : nil
        }

        var firstPage: UUID?
        for (image, key) in zip(images, ScanPlanner.sortKeys(count: images.count, after: after, before: before)) {
            let assetID = UUID()
            let scaled = image.scaled(maxDimension: 2480)
            guard let data = scaled.jpegData(compressionQuality: 0.75) else { continue }
            do {
                try AssetFiles.save(data, id: assetID)
                try await store.registerAsset(assetID)
            } catch {
                storeFailed()
                continue
            }

            let page = Page(notebookID: notebook.id, sort: key, content: .scan(assetID: assetID))
            await write { try await self.store.put(page) }
            firstPage = firstPage ?? page.id

            let store = store
            Task {
                let text = await TextRecognition.recognize(scaled)
                try? await store.index(pageID: page.id, notebookID: notebook.id, text: text)
            }
        }

        await open(notebookID: notebook.id, pageID: firstPage)
    }

    /// Shows a notebook, and a page in it if given: from a search result or after a scan.
    func open(notebookID: UUID, pageID: UUID?) async {
        guard let notebook = try? await store.notebook(id: notebookID) else { return }
        sidebar = notebook.subjectID.map { .folder($0) } ?? .root
        await reloadNotebooks()
        pendingPageID = pageID
        pendingNotebookID = notebook.id
    }

    // MARK: Assets

    /// Sends the files made here and fetches the ones other devices made. A failure is simply tried again at the next sync.
    func syncAssets(using transport: URLSessionTransport) async {
        for id in (try? await store.pendingAssetUploads()) ?? [] {
            guard let data = AssetFiles.data(for: id) else { continue }
            do {
                try await transport.uploadAsset(id: id, data: data)
                try await store.markAssetUploaded(id)
            } catch {
                break
            }
        }

        var fetched = false
        for id in (try? await store.referencedAssetIDs()) ?? [] where !AssetFiles.exists(id) {
            guard let data = try? await transport.downloadAsset(id: id) else { continue }
            if (try? AssetFiles.save(data, id: id)) != nil {
                try? await store.markAssetUploaded(id)
                fetched = true
            }
        }
        if fetched { assetRevision += 1 }
    }

    // MARK: Search

    func search(_ text: String) async -> [SearchResult] {
        guard let hits = try? await store.search(text) else { return [] }
        var results: [SearchResult] = []
        for hit in hits {
            guard let notebook = try? await store.notebook(id: hit.notebookID), notebook.trashedAt == nil else { continue }
            var name: String?
            if notebook.subjectID != nil { name = tree.pathTitle(of: notebook.subjectID) }
            results.append(SearchResult(hit: hit, notebook: notebook, subjectName: name))
        }
        return results
    }
}
