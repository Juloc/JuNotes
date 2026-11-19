import JuNotesCore
import PencilKit
import SwiftUI

/// A blank sheet for the copy-from-memory mode. Unlike a page it is not saved; what she writes here is only compared.
struct ScratchCanvas: UIViewRepresentable {
    @Binding var drawing: PKDrawing

    func makeCoordinator() -> Coordinator { Coordinator(drawing: $drawing) }

    func makeUIView(context: Context) -> PKCanvasView {
        let canvas = PKCanvasView()
        canvas.backgroundColor = .white
        canvas.overrideUserInterfaceStyle = .light
        canvas.drawingPolicy = .anyInput
        canvas.delegate = context.coordinator
        ToolPicker.shared.add(canvas)
        Task { @MainActor in ToolPicker.shared.activate(canvas) }
        return canvas
    }

    func updateUIView(_ canvas: PKCanvasView, context: Context) {}

    @MainActor
    final class Coordinator: NSObject, PKCanvasViewDelegate {
        let drawing: Binding<PKDrawing>

        init(drawing: Binding<PKDrawing>) { self.drawing = drawing }

        nonisolated func canvasViewDrawingDidChange(_ canvasView: PKCanvasView) {
            MainActor.assumeIsolated { drawing.wrappedValue = canvasView.drawing }
        }
    }
}

/// Copying that teaches: the text is shown for a short time, then hidden, she writes it from memory, and a comparison shows what she left out.
struct CopyModeView: View {
    let original: String
    let onDone: () -> Void

    private enum Phase {
        case reading
        case writing
        case checking
        case result(TextComparison)
    }

    @State private var phase = Phase.reading
    @State private var secondsLeft = 45
    @State private var drawing = PKDrawing()

    var body: some View {
        switch phase {
        case .reading:
            VStack(spacing: 20) {
                ScrollView { Text(original).font(.title3).padding(24).frame(maxWidth: .infinity, alignment: .leading) }
                Text("Noch \(secondsLeft) Sekunden").font(.headline).monospacedDigit().foregroundStyle(.secondary)
                HStack {
                    Button("Mehr Zeit (+15 s)") { secondsLeft += 15 }.buttonStyle(.bordered)
                    Button("Ich kann es") { phase = .writing }.buttonStyle(.borderedProminent)
                }
                .padding(.bottom)
            }
            .task(id: secondsLeft > 0) {
                // Counts down one second at a time and hides the text at zero.
                while secondsLeft > 0 {
                    try? await Task.sleep(for: .seconds(1))
                    if Task.isCancelled { return }
                    secondsLeft -= 1
                }
                phase = .writing
            }
        case .writing:
            VStack(spacing: 0) {
                Text("Schreibe den Text aus dem Gedächtnis.").font(.headline).padding()
                ScratchCanvas(drawing: $drawing)
                Button("Fertig, vergleichen") { Task { await compare() } }
                    .buttonStyle(.borderedProminent)
                    .padding()
                    .disabled(drawing.strokes.isEmpty)
            }
        case .checking:
            VStack(spacing: 16) { ProgressView().controlSize(.large); Text("Deine Schrift wird gelesen …").foregroundStyle(.secondary) }
        case .result(let comparison):
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    Text("Du hast \(Int((comparison.score * 100).rounded())) Prozent behalten").font(.title2.weight(.semibold))
                    Text(highlighted(comparison)).font(.title3)
                    if !comparison.missing.isEmpty {
                        Text("Das hat gefehlt: " + comparison.missing.joined(separator: ", ")).font(.footnote).foregroundStyle(.secondary)
                    }
                    HStack {
                        Button("Noch einmal") { secondsLeft = 45; drawing = PKDrawing(); phase = .reading }.buttonStyle(.bordered)
                        Button("Fertig", action: onDone).buttonStyle(.borderedProminent)
                    }
                }
                .padding(24)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
    }

    private func compare() async {
        phase = .checking
        var ink = UIImage()
        let rect = drawing.bounds.insetBy(dx: -24, dy: -24)
        UITraitCollection(userInterfaceStyle: .light).performAsCurrent { ink = drawing.image(from: rect, scale: 2) }
        let recalled = await TextRecognition.recognize(ink.onWhite())
        phase = .result(TextCompare.compare(original: original, recalled: recalled))
    }

    /// The original, with the words she forgot in red.
    private func highlighted(_ comparison: TextComparison) -> AttributedString {
        var result = AttributedString()
        for word in comparison.words {
            var part = AttributedString(word.text + " ")
            if word.missing {
                part.foregroundColor = .red
                part.font = .title3.bold()
            }
            result += part
        }
        return result
    }
}
