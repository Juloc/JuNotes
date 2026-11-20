import JuNotesCore
import PDFKit
import UIKit

enum PDFImportError: Error {
    case unreadable
    case tooLarge
    case empty
    case storage
}

extension PDFImportError {
    var sentence: String {
        switch self {
        case .unreadable: "Diese Datei lässt sich nicht als PDF öffnen."
        case .tooLarge: "Die Datei ist größer als 60 MB. Teile sie vorher in kleinere PDFs."
        case .empty: "Das PDF hat keine Seiten."
        case .storage: "Das PDF konnte nicht auf dem iPad gespeichert werden."
        }
    }
}

extension AppModel {
    static let maximumPDFBytes = 60 * 1024 * 1024
    static let maximumPDFPages = 400

    /// A PDF becomes a notebook with one page per PDF page, in the subject that is open. She can write on every page. This is how a notebook leaves GoodNotes:
    /// export it there as PDF and import it here.
    @discardableResult
    func importPDF(from url: URL) async throws -> Notebook {
        try await importPDF(from: url, into: selectedFolder?.id, opening: true)
    }

    /// The same, into a given folder and without opening the notebook, for importing a whole folder at once.
    @discardableResult
    func importPDF(from url: URL, into parent: UUID?, opening: Bool) async throws -> Notebook {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }

        guard let data = try? Data(contentsOf: url) else { throw PDFImportError.unreadable }
        guard data.count <= Self.maximumPDFBytes else { throw PDFImportError.tooLarge }
        guard let document = PDFDocument(data: data) else { throw PDFImportError.unreadable }
        guard document.pageCount > 0 else { throw PDFImportError.empty }

        let assetID = UUID()
        do {
            try AssetFiles.save(data, id: assetID)
            try await store.registerAsset(assetID)
        } catch {
            storeFailed()
            throw PDFImportError.storage
        }

        let count = min(document.pageCount, Self.maximumPDFPages)
        let notebook = Notebook(subjectID: parent, title: NotebookTitle.fromFileName(url.lastPathComponent), template: .blank)
        await write { try await self.store.put(notebook) }
        let keys = PageOrder.keys(appending: count, after: nil)
        var pages: [Page] = []
        for index in 0..<count {
            let page = Page(notebookID: notebook.id, sort: keys[index], content: .pdf(assetID: assetID, pageIndex: index))
            pages.append(page)
            await write { try await self.store.put(page) }
        }

        indexPDF(pages: pages, assetID: assetID, notebookID: notebook.id)
        if opening {
            await reloadNotebooks()
            await open(notebookID: notebook.id, pageID: pages.first?.id)
        }
        return notebook
    }

    /// Makes the text of an imported PDF searchable: from the PDF itself where it has text, from a reading of the picture where it is a scan.
    private func indexPDF(pages: [Page], assetID: UUID, notebookID: UUID) {
        let store = store
        Task { @MainActor in
            for page in pages {
                guard case .pdf(_, let index) = page.content else { continue }
                var text = PDFPages.shared.text(assetID: assetID, pageIndex: index) ?? ""
                if text.count < 10, let image = PDFPages.shared.image(assetID: assetID, pageIndex: index, width: 800) {
                    text = await TextRecognition.recognize(image)
                }
                try? await store.index(pageID: page.id, notebookID: notebookID, text: text)
                await Task.yield()
            }
        }
    }

    // MARK: Export

    /// The notebook as a PDF file in the temporary folder, for the share sheet.
    func exportPDF(_ notebook: Notebook) async -> URL? {
        guard let data = try? await NotebookExporter.pdf(for: notebook, store: store) else {
            notice = "Das PDF konnte nicht erstellt werden."
            return nil
        }
        let name = notebook.title.map { $0.isLetter || $0.isNumber || $0 == " " || $0 == "-" ? $0 : Character("_") }
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(String(name.prefix(80)) + ".pdf")
        do {
            try data.write(to: url, options: .atomic)
            return url
        } catch {
            notice = "Das PDF konnte nicht gespeichert werden."
            return nil
        }
    }

    /// Sends the notebook to Paperless through the server, with its subject as a tag.
    func sendToPaperless(_ notebook: Notebook) async {
        guard !isSensitive(notebook) else {
            notice = "„\(notebook.title)“ enthält Daten von Kindern und wird nicht an Paperless gesendet."
            return
        }
        guard let transport = makeTransport() else {
            notice = "Der Server ist noch nicht eingerichtet. Trage Adresse und Schlüssel in den Einstellungen ein."
            return
        }
        guard let data = try? await NotebookExporter.pdf(for: notebook, store: store) else {
            notice = "Das PDF konnte nicht erstellt werden."
            return
        }
        let subject = subject(of: notebook)?.name
        do {
            try await transport.sendToPaperless(pdf: data, title: notebook.title, tags: [subject, "JuNotes"].compactMap { $0 })
            notice = "„\(notebook.title)“ liegt jetzt in Paperless."
        } catch ExportError.notSetUp {
            notice = "Paperless ist auf dem Server noch nicht eingerichtet. Das macht der Admin."
        } catch ExportError.refused(let text), ExportError.failed(let text) {
            notice = text
        } catch ExportError.noNetwork {
            notice = "Kein Netz. Zum Senden braucht das iPad eine Verbindung zum Server."
        } catch {
            notice = "Der Server antwortet nicht."
        }
    }
}
