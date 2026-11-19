import JuNotesCore
import SwiftUI

/// The printed part of a page: ruling, grid, dots or the Cornell layout. The drawing lies on top of it.
struct PageBackground: View {
    let template: PageTemplate

    private let line = Color(red: 0.78, green: 0.87, blue: 0.96)
    private let margin = Color(red: 0.96, green: 0.64, blue: 0.64)

    var body: some View {
        Canvas { context, size in
            let scale = size.width / PageGeometry.width
            switch template {
            case .blank:
                break
            case .ruled:
                horizontalLines(&context, size: size, spacing: 28 * scale, top: 60 * scale)
                context.stroke(Path { $0.move(to: CGPoint(x: 64 * scale, y: 0)); $0.addLine(to: CGPoint(x: 64 * scale, y: size.height)) }, with: .color(margin), lineWidth: 1)
            case .grid:
                let step = 20 * scale
                horizontalLines(&context, size: size, spacing: step, top: 0)
                var x = step
                while x < size.width {
                    context.stroke(Path { $0.move(to: CGPoint(x: x, y: 0)); $0.addLine(to: CGPoint(x: x, y: size.height)) }, with: .color(line), lineWidth: 0.6)
                    x += step
                }
            case .dotted:
                let step = 20 * scale
                var y = step
                while y < size.height {
                    var x = step
                    while x < size.width {
                        context.fill(Path(ellipseIn: CGRect(x: x - 0.9, y: y - 0.9, width: 1.8, height: 1.8)), with: .color(Color(white: 0.7)))
                        x += step
                    }
                    y += step
                }
            case .cornell:
                horizontalLines(&context, size: size, spacing: 28 * scale, top: 90 * scale)
                let cue = 170 * scale, summary = size.height - 150 * scale
                context.stroke(Path { $0.move(to: CGPoint(x: cue, y: 70 * scale)); $0.addLine(to: CGPoint(x: cue, y: summary)) }, with: .color(margin), lineWidth: 1)
                context.stroke(Path { $0.move(to: CGPoint(x: 0, y: summary)); $0.addLine(to: CGPoint(x: size.width, y: summary)) }, with: .color(margin), lineWidth: 1)
            }
        }
        .allowsHitTesting(false)
    }

    private func horizontalLines(_ context: inout GraphicsContext, size: CGSize, spacing: CGFloat, top: CGFloat) {
        var y = max(top, spacing)
        while y < size.height {
            context.stroke(Path { $0.move(to: CGPoint(x: 0, y: y)); $0.addLine(to: CGPoint(x: size.width, y: y)) }, with: .color(line), lineWidth: 0.6)
            y += spacing
        }
    }
}
