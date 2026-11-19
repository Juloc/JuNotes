import JuNotesCore
import PencilKit
import SwiftUI

/// One tool picker for the whole app: it follows whichever page the Pencil last touched, and it is what carries the Pencil Pro's squeeze, double tap and hover.
@MainActor
final class ToolPicker {
    static let shared = ToolPicker()
    private let picker = PKToolPicker()

    func add(_ canvas: PKCanvasView) { picker.addObserver(canvas) }

    func activate(_ canvas: PKCanvasView) {
        canvas.becomeFirstResponder()
        picker.setVisible(true, forFirstResponder: canvas)
    }
}

/// A page's drawing. The Pencil draws, a finger scrolls. Every stroke is saved the moment it is made.
struct PencilCanvas: UIViewRepresentable {
    let pageID: UUID
    let notebookID: UUID
    /// Drawing pages are read for text so they can be found; a scan keeps the text of the scan itself.
    let indexDrawing: Bool
    let logicalSize: CGSize
    let displayWidth: CGFloat
    let initialStrokes: [Stroke]
    let store: SQLiteStore
    let onStrokeCount: (Int) -> Void
    let onNearBottom: () -> Void
    let onStoreFailure: () -> Void
    let onChanged: () -> Void

    func makeCoordinator() -> Coordinator { Coordinator(parent: self) }

    func makeUIView(context: Context) -> PKCanvasView {
        let canvas = PKCanvasView()
        canvas.backgroundColor = .clear
        canvas.isOpaque = false
        // The paper stays white and the ink dark, in dark mode too.
        canvas.overrideUserInterfaceStyle = .light
        canvas.drawingPolicy = .pencilOnly
        canvas.isScrollEnabled = false
        canvas.contentSize = logicalSize
        canvas.minimumZoomScale = 0.05
        canvas.maximumZoomScale = 8
        canvas.delegate = context.coordinator
        context.coordinator.attach(canvas, strokes: initialStrokes)
        applyScale(canvas)
        return canvas
    }

    func updateUIView(_ canvas: PKCanvasView, context: Context) {
        context.coordinator.parent = self
        applyScale(canvas)
    }

    private func applyScale(_ canvas: PKCanvasView) {
        let scale = displayWidth / logicalSize.width
        if abs(canvas.zoomScale - scale) > 0.0001 { canvas.zoomScale = scale }
    }

    @MainActor
    final class Coordinator: NSObject, PKCanvasViewDelegate {
        var parent: PencilCanvas
        private var known: [String: UUID] = [:]
        private var writes: Task<Void, Never>?
        private var counter = 0
        private var loading = false
        private var idleTask: Task<Void, Never>?

        init(parent: PencilCanvas) { self.parent = parent }

        func attach(_ canvas: PKCanvasView, strokes: [Stroke]) {
            loading = true
            var converted: [PKStroke] = []
            for stroke in strokes {
                counter += 1
                let pk = StrokeConversion.pkStroke(from: stroke, creationDate: Date(timeIntervalSince1970: 1_000_000_000 + Double(counter)))
                known[StrokeConversion.fingerprint(pk)] = stroke.id
                converted.append(pk)
            }
            canvas.drawing = PKDrawing(strokes: converted)
            loading = false
            ToolPicker.shared.add(canvas)
            let count = strokes.count
            let report = parent.onStrokeCount
            Task { @MainActor in report(count) }
        }

        nonisolated func canvasViewDidBeginUsingTool(_ canvasView: PKCanvasView) {
            MainActor.assumeIsolated { ToolPicker.shared.activate(canvasView) }
        }

        nonisolated func canvasViewDrawingDidChange(_ canvasView: PKCanvasView) {
            MainActor.assumeIsolated { drawingChanged(canvasView) }
        }

        private func drawingChanged(_ canvas: PKCanvasView) {
            guard !loading else { return }
            let strokes = canvas.drawing.strokes
            var seen = Set<String>()
            var added: [(stroke: Stroke, lowest: CGFloat)] = []
            for pk in strokes {
                let fingerprint = StrokeConversion.fingerprint(pk)
                seen.insert(fingerprint)
                if known[fingerprint] == nil {
                    let stroke = StrokeConversion.stroke(from: pk)
                    known[fingerprint] = stroke.id
                    added.append((stroke, pk.renderBounds.maxY))
                }
            }
            let removed = known.filter { !seen.contains($0.key) }
            for key in removed.keys { known[key] = nil }

            let pageID = parent.pageID
            let store = parent.store
            let failed = parent.onStoreFailure
            let changed = parent.onChanged
            let additions = added.map(\.stroke)
            let deletions = Array(removed.values)
            if !additions.isEmpty || !deletions.isEmpty {
                // One chain, so a stroke drawn and undone at once is never deleted before it was saved.
                let previous = writes
                writes = Task {
                    await previous?.value
                    do {
                        for stroke in additions { try await store.put(stroke, on: pageID) }
                        for id in deletions { try await store.remove(Stroke(id: id, points: []), on: pageID) }
                        await MainActor.run { changed() }
                    } catch {
                        await MainActor.run { failed() }
                    }
                }
            }

            parent.onStrokeCount(strokes.count)
            scheduleIndexing(canvas)
            if let lowest = added.map(\.lowest).max(), AutoPages.needsNewPage(writingAtY: Double(lowest), pageHeight: Double(parent.logicalSize.height), isLastPage: true) {
                parent.onNearBottom()
            }
        }

        /// A few seconds after she stops writing, the page is read for text, so what she wrote can be searched.
        private func scheduleIndexing(_ canvas: PKCanvasView) {
            guard parent.indexDrawing else { return }
            idleTask?.cancel()
            let pageID = parent.pageID
            let notebookID = parent.notebookID
            let store = parent.store
            let size = parent.logicalSize
            idleTask = Task { [weak canvas] in
                try? await Task.sleep(for: .seconds(6))
                guard !Task.isCancelled, let canvas else { return }
                let drawing = canvas.drawing
                var text = ""
                if !drawing.strokes.isEmpty {
                    var ink = UIImage()
                    UITraitCollection(userInterfaceStyle: .light).performAsCurrent { ink = drawing.image(from: CGRect(origin: .zero, size: size), scale: 1.5) }
                    // Text recognition wants dark ink on white, not on transparent.
                    let flat = UIGraphicsImageRenderer(size: ink.size).image { context in
                        UIColor.white.setFill()
                        context.fill(CGRect(origin: .zero, size: ink.size))
                        ink.draw(at: .zero)
                    }
                    text = await TextRecognition.recognize(flat)
                }
                try? await store.index(pageID: pageID, notebookID: notebookID, text: text)
            }
        }
    }
}
