import JuNotesCore
import SwiftUI

/// One page: paper, printed lines and the drawing. The strokes load from the local database first.
struct PageView: View {
    let page: Page
    let notebook: Notebook
    let displayWidth: CGFloat
    let onStrokeCount: (Int) -> Void
    let onNearBottom: () -> Void

    @Environment(AppModel.self) private var model
    @State private var strokes: [Stroke]?

    var body: some View {
        let logical = PageGeometry.size(for: notebook.type)
        let height = displayWidth * logical.height / logical.width

        ZStack {
            Color.white
            PageBackground(template: page.template ?? notebook.template)
            if let strokes {
                PencilCanvas(
                    pageID: page.id,
                    logicalSize: CGSize(width: logical.width, height: logical.height),
                    displayWidth: displayWidth,
                    initialStrokes: strokes,
                    store: model.store,
                    onStrokeCount: onStrokeCount,
                    onNearBottom: onNearBottom,
                    onStoreFailure: { model.storeFailed() },
                    onChanged: { model.scheduleSync() }
                )
            } else {
                ProgressView()
            }
        }
        .frame(width: displayWidth, height: height)
        .clipShape(RoundedRectangle(cornerRadius: 4))
        .shadow(color: .black.opacity(0.18), radius: 3, y: 1)
        .task(id: page.id) { strokes = (try? await model.store.strokes(pageID: page.id)) ?? [] }
    }
}
