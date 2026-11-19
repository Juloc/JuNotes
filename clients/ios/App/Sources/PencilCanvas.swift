import PencilKit
import SwiftUI

/// PencilKit gives the lowest latency and the system tool picker, which already handles squeeze, double tap, hover and haptics of the Pencil Pro.
struct PencilCanvas: UIViewRepresentable {
    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeUIView(context: Context) -> PKCanvasView {
        let canvas = PKCanvasView()
        canvas.drawingPolicy = .anyInput
        canvas.backgroundColor = .systemBackground
        canvas.isOpaque = true
        canvas.alwaysBounceVertical = true

        let picker = PKToolPicker()
        picker.addObserver(canvas)
        context.coordinator.picker = picker

        // The picker shows once the canvas is in a window and first responder.
        Task { @MainActor in
            picker.setVisible(true, forFirstResponder: canvas)
            canvas.becomeFirstResponder()
        }
        return canvas
    }

    func updateUIView(_ canvas: PKCanvasView, context: Context) {}

    final class Coordinator {
        var picker: PKToolPicker?
    }
}
