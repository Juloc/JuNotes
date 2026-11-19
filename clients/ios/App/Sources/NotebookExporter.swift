import JuNotesCore
import PDFKit
import PencilKit
import UIKit

/// A notebook as a PDF: every page with its printed lines, scans, her writing and the text the app printed. Where she can open it in any other app.
enum NotebookExporter {
    @MainActor
    static func pdf(for notebook: Notebook, store: SQLiteStore) async throws -> Data {
        let pages = try await store.pages(notebookID: notebook.id)
        let logical = PageGeometry.size(for: notebook.type)
        let bounds = CGRect(x: 0, y: 0, width: logical.width, height: logical.height)

        var rendered: [(Page, [Stroke])] = []
        for page in pages { rendered.append((page, try await store.strokes(pageID: page.id))) }

        let info: [String: Any] = [kCGPDFContextTitle as String: notebook.title, kCGPDFContextCreator as String: "JuNotes"]
        let format = UIGraphicsPDFRendererFormat()
        format.documentInfo = info
        return UIGraphicsPDFRenderer(bounds: bounds, format: format).pdfData { context in
            for (page, strokes) in rendered {
                context.beginPage()
                draw(page, strokes: strokes, notebook: notebook, in: bounds, context: context.cgContext)
            }
            if rendered.isEmpty { context.beginPage() }
        }
    }

    @MainActor
    private static func draw(_ page: Page, strokes: [Stroke], notebook: Notebook, in bounds: CGRect, context: CGContext) {
        UIColor.white.setFill()
        context.fill(bounds)

        switch page.content {
        case .drawing: drawPaper(page.template ?? notebook.template, in: bounds, context: context)
        case .scan(let id): UIImage.fitting(AssetFiles.image(for: id), in: bounds)
        case .pdf(let id, let index): UIImage.fitting(PDFPages.shared.image(assetID: id, pageIndex: index, width: bounds.width), in: bounds)
        }

        let margin = bounds.width * 0.12
        if let heading = page.heading {
            (heading as NSString).draw(at: CGPoint(x: margin, y: 14), withAttributes: [.font: UIFont.systemFont(ofSize: 10), .foregroundColor: UIColor(white: 0.45, alpha: 1)])
        }
        if let printed = page.printedText {
            (printed as NSString).draw(in: CGRect(x: margin, y: bounds.width * 0.08, width: bounds.width - margin - bounds.width * 0.06, height: bounds.height * 0.5), withAttributes: [.font: UIFont.systemFont(ofSize: 12), .foregroundColor: UIColor.black])
        }

        if !strokes.isEmpty {
            var counter = 0
            let drawing = PKDrawing(strokes: strokes.map { stroke in
                counter += 1
                return StrokeConversion.pkStroke(from: stroke, creationDate: Date(timeIntervalSince1970: 1_000_000_000 + Double(counter)))
            })
            var image = UIImage()
            UITraitCollection(userInterfaceStyle: .light).performAsCurrent { image = drawing.image(from: bounds, scale: 2) }
            image.draw(in: bounds)
        }

        if !page.additions.isEmpty {
            let text = "Ergänzungen\n" + page.additions.joined(separator: "\n")
            let box = CGRect(x: 14, y: bounds.height - 90, width: bounds.width - 28, height: 76)
            UIColor(red: 0.88, green: 0.96, blue: 0.9, alpha: 1).setFill()
            UIBezierPath(roundedRect: box, cornerRadius: 6).fill()
            (text as NSString).draw(in: box.insetBy(dx: 8, dy: 6), withAttributes: [.font: UIFont.systemFont(ofSize: 10), .foregroundColor: UIColor.black])
        }
    }

    /// The same printed paper as on screen: ruled with a margin, grid, dots or the Cornell layout.
    private static func drawPaper(_ template: PageTemplate, in bounds: CGRect, context: CGContext) {
        let line = UIColor(red: 0.78, green: 0.87, blue: 0.96, alpha: 1)
        let margin = UIColor(red: 0.96, green: 0.64, blue: 0.64, alpha: 1)

        func stroke(_ color: UIColor, _ from: CGPoint, _ to: CGPoint, _ width: CGFloat = 0.6) {
            context.setStrokeColor(color.cgColor)
            context.setLineWidth(width)
            context.move(to: from)
            context.addLine(to: to)
            context.strokePath()
        }
        func horizontal(spacing: CGFloat, top: CGFloat) {
            var y = max(top, spacing)
            while y < bounds.height {
                stroke(line, CGPoint(x: 0, y: y), CGPoint(x: bounds.width, y: y))
                y += spacing
            }
        }

        switch template {
        case .blank:
            break
        case .ruled:
            horizontal(spacing: 28, top: 60)
            stroke(margin, CGPoint(x: 64, y: 0), CGPoint(x: 64, y: bounds.height), 1)
        case .grid:
            horizontal(spacing: 20, top: 0)
            var x: CGFloat = 20
            while x < bounds.width { stroke(line, CGPoint(x: x, y: 0), CGPoint(x: x, y: bounds.height)); x += 20 }
        case .dotted:
            context.setFillColor(UIColor(white: 0.7, alpha: 1).cgColor)
            var y: CGFloat = 20
            while y < bounds.height {
                var x: CGFloat = 20
                while x < bounds.width { context.fillEllipse(in: CGRect(x: x - 0.9, y: y - 0.9, width: 1.8, height: 1.8)); x += 20 }
                y += 20
            }
        case .cornell:
            horizontal(spacing: 28, top: 90)
            stroke(margin, CGPoint(x: 170, y: 70), CGPoint(x: 170, y: bounds.height - 150), 1)
            stroke(margin, CGPoint(x: 0, y: bounds.height - 150), CGPoint(x: bounds.width, y: bounds.height - 150), 1)
        }
    }
}

extension UIImage {
    /// Draws `image` as large as possible inside `bounds` without distorting it, centred.
    @discardableResult
    static func fitting(_ image: UIImage?, in bounds: CGRect) -> Bool {
        guard let image, image.size.width > 0, image.size.height > 0 else { return false }
        let scale = min(bounds.width / image.size.width, bounds.height / image.size.height)
        let size = CGSize(width: image.size.width * scale, height: image.size.height * scale)
        image.draw(in: CGRect(x: bounds.midX - size.width / 2, y: bounds.midY - size.height / 2, width: size.width, height: size.height))
        return true
    }
}
