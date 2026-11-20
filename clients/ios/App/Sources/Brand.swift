import JuNotesCore
import SwiftUI

/// The one colour JuNotes adds to the system look: a soft violet. Everything else is the system's.
enum Brand {
    static let accent = Color(red: 0.424, green: 0.361, blue: 0.906)
}

extension View {
    /// A rounded card on the grouped background, the way Apple's own apps lay out an overview.
    func card() -> some View {
        padding(18)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color(.secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 22, style: .continuous))
    }
}

/// A small heading for a card: symbol and words.
struct CardTitle: View {
    let text: String
    let symbol: String

    init(_ text: String, symbol: String) {
        self.text = text
        self.symbol = symbol
    }

    var body: some View {
        Label(text, systemImage: symbol)
            .font(.subheadline.weight(.semibold))
            .foregroundStyle(.secondary)
    }
}

/// A ring with a number inside, such as the minutes a lesson has left.
struct ProgressRing: View {
    let progress: Double
    let value: String
    let caption: String

    var body: some View {
        ZStack {
            Circle().stroke(Brand.accent.opacity(0.16), lineWidth: 10)
            Circle()
                .trim(from: 0, to: max(0.02, min(1, progress)))
                .stroke(Brand.accent, style: StrokeStyle(lineWidth: 10, lineCap: .round))
                .rotationEffect(.degrees(-90))
            VStack(spacing: 0) {
                Text(value).font(.title2.weight(.bold)).monospacedDigit()
                Text(caption).font(.caption).foregroundStyle(.secondary)
            }
        }
        .frame(width: 104, height: 104)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(value) \(caption)")
    }
}

/// A page in small: the paper, its ruling and the handwriting, drawn from the stored strokes.
struct PageThumbnail: View {
    let page: Page
    let template: PageTemplate

    @Environment(AppModel.self) private var model
    @State private var strokes: [Stroke] = []

    var body: some View {
        ZStack {
            Color.white
            PageBackground(template: template)
            Canvas { context, size in
                let scale = size.width / PageGeometry.width
                for stroke in strokes where stroke.points.count > 1 {
                    var path = Path()
                    for (index, point) in stroke.points.enumerated() {
                        let location = CGPoint(x: point.x * scale, y: point.y * scale)
                        if index == 0 { path.move(to: location) } else { path.addLine(to: location) }
                    }
                    context.stroke(path, with: .color(Color(uiColor: StrokeConversion.color(hex: stroke.color))), lineWidth: max(0.7, stroke.width * scale))
                }
            }
            .allowsHitTesting(false)
        }
        .aspectRatio(PageGeometry.width / PageGeometry.height, contentMode: .fit)
        .clipShape(RoundedRectangle(cornerRadius: 4, style: .continuous))
        .shadow(color: .black.opacity(0.14), radius: 2, y: 1)
        .task(id: page.id) { strokes = (try? await model.store.strokes(pageID: page.id)) ?? [] }
    }
}

/// A notebook as a small first page with the subject's colour as a tag, for the shelf and for "Zuletzt geöffnet".
struct NotebookCover: View {
    let notebook: Notebook
    let subject: Subject?

    @Environment(AppModel.self) private var model
    @State private var firstPage: Page?

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            ZStack(alignment: .topLeading) {
                if let firstPage {
                    PageThumbnail(page: firstPage, template: firstPage.template ?? notebook.template)
                } else {
                    Color.white
                        .aspectRatio(PageGeometry.width / PageGeometry.height, contentMode: .fit)
                        .clipShape(RoundedRectangle(cornerRadius: 4, style: .continuous))
                        .shadow(color: .black.opacity(0.14), radius: 2, y: 1)
                }
                if let subject {
                    Text(subject.name)
                        .font(.caption2.weight(.semibold))
                        .foregroundStyle(.white)
                        .padding(.horizontal, 7).padding(.vertical, 3)
                        .background(Color(uiColor: StrokeConversion.color(hex: subject.color)), in: Capsule())
                        .padding(8)
                }
            }
            Text(notebook.title).font(.headline).lineLimit(2)
            Text(notebook.type == .whiteboard ? "Whiteboard" : "Heft").font(.footnote).foregroundStyle(.secondary)
        }
        .task(id: notebook.id) { firstPage = (try? await model.store.pages(notebookID: notebook.id))?.first }
    }
}
