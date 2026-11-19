import PDFKit
import UIKit

/// Pages of imported PDFs, shown like scans: drawn once at the size needed and kept while the document is open.
@MainActor
final class PDFPages {
    static let shared = PDFPages()
    private var documents: [UUID: PDFDocument] = [:]

    func document(_ assetID: UUID) -> PDFDocument? {
        if let known = documents[assetID] { return known }
        guard AssetFiles.exists(assetID), let document = PDFDocument(url: AssetFiles.url(for: assetID)) else { return nil }
        documents[assetID] = document
        return document
    }

    func pageCount(_ assetID: UUID) -> Int { document(assetID)?.pageCount ?? 0 }

    /// The page as an image `width` points wide, sharp enough for a retina screen. PDFKit takes care of the page's own rotation.
    func image(assetID: UUID, pageIndex: Int, width: CGFloat) -> UIImage? {
        guard let page = document(assetID)?.page(at: pageIndex) else { return nil }
        let box = page.bounds(for: .cropBox)
        guard box.width > 0, box.height > 0 else { return nil }
        let pixels = CGSize(width: width * 2, height: width * 2 * box.height / box.width)
        return page.thumbnail(of: pixels, for: .cropBox)
    }

    /// The text a PDF carries itself, which is better than reading the picture of it when it is there.
    func text(assetID: UUID, pageIndex: Int) -> String? {
        document(assetID)?.page(at: pageIndex)?.string?.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
