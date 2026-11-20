import JuNotesCore
import SwiftUI

/// The printed part of a page: ruling, grid, dots, the Cornell layout, a checklist or a week planner. The drawing lies on top of it.
struct PageBackground: View {
    let template: PageTemplate

    private let line = Color(red: 0.78, green: 0.87, blue: 0.96)
    private let margin = Color(red: 0.96, green: 0.64, blue: 0.64)
    private let label = Color(white: 0.55)

    private static let days = ["Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag"]

    var body: some View {
        Canvas { context, size in
            let scale = size.width / PageGeometry.width
            if let spacing = template.lineSpacing {
                horizontalLines(&context, size: size, spacing: spacing * scale, top: template.lineTop * scale)
            }
            if template.hasMargin {
                context.stroke(Path { $0.move(to: CGPoint(x: 64 * scale, y: 0)); $0.addLine(to: CGPoint(x: 64 * scale, y: size.height)) }, with: .color(margin), lineWidth: 1)
            }
            if let step = template.gridSpacing {
                let spacing = step * scale
                horizontalLines(&context, size: size, spacing: spacing, top: 0)
                var x = spacing
                while x < size.width {
                    context.stroke(Path { $0.move(to: CGPoint(x: x, y: 0)); $0.addLine(to: CGPoint(x: x, y: size.height)) }, with: .color(line), lineWidth: 0.6)
                    x += spacing
                }
            }
            if let step = template.dotSpacing {
                let spacing = step * scale
                var y = spacing
                while y < size.height {
                    var x = spacing
                    while x < size.width {
                        context.fill(Path(ellipseIn: CGRect(x: x - 0.9, y: y - 0.9, width: 1.8, height: 1.8)), with: .color(Color(white: 0.7)))
                        x += spacing
                    }
                    y += spacing
                }
            }
            switch template {
            case .cornell:
                let cue = 170 * scale, summary = size.height - 150 * scale
                context.stroke(Path { $0.move(to: CGPoint(x: cue, y: 70 * scale)); $0.addLine(to: CGPoint(x: cue, y: summary)) }, with: .color(margin), lineWidth: 1)
                context.stroke(Path { $0.move(to: CGPoint(x: 0, y: summary)); $0.addLine(to: CGPoint(x: size.width, y: summary)) }, with: .color(margin), lineWidth: 1)
            case .checklist:
                let row = 38 * scale
                var y = 70 * scale
                while y < size.height - 30 * scale {
                    context.stroke(Path { $0.move(to: CGPoint(x: 70 * scale, y: y)); $0.addLine(to: CGPoint(x: size.width - 40 * scale, y: y)) }, with: .color(line), lineWidth: 0.8)
                    let box = CGRect(x: 40 * scale, y: y - 22 * scale, width: 18 * scale, height: 18 * scale)
                    context.stroke(Path(roundedRect: box, cornerRadius: 3 * scale), with: .color(Color(white: 0.6)), lineWidth: 1)
                    y += row
                }
            case .planner:
                let top = 60 * scale, bottom = size.height - 40 * scale
                let block = (bottom - top) / 7
                for index in 0..<7 {
                    let y = top + block * CGFloat(index)
                    context.stroke(Path { $0.move(to: CGPoint(x: 30 * scale, y: y)); $0.addLine(to: CGPoint(x: size.width - 30 * scale, y: y)) }, with: .color(margin.opacity(0.8)), lineWidth: 1)
                    context.draw(Text(Self.days[index]).font(.system(size: max(8, 12 * scale), weight: .semibold)).foregroundStyle(label), at: CGPoint(x: 36 * scale, y: y + 6 * scale), anchor: .topLeading)
                }
                context.stroke(Path { $0.move(to: CGPoint(x: 30 * scale, y: bottom)); $0.addLine(to: CGPoint(x: size.width - 30 * scale, y: bottom)) }, with: .color(margin.opacity(0.8)), lineWidth: 1)
            default:
                break
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
