import JuNotesCore
import SwiftUI
import UIKit

/// One page: paper, printed lines and the drawing. The strokes load from the local database first.
struct PageView: View {
    let page: Page
    let notebook: Notebook
    let displayWidth: CGFloat
    let onStrokeCount: (Int) -> Void
    let onNearBottom: () -> Void

    @Environment(AppModel.self) private var model
    @State private var strokes: [Stroke]?
    @State private var scanImage: UIImage?

    var body: some View {
        let logical = PageGeometry.size(for: notebook.type)
        let height = displayWidth * logical.height / logical.width

        ZStack {
            Color.white
            PageBackground(template: page.template ?? notebook.template)
            if case .scan(let assetID) = page.content {
                if let scanImage {
                    Image(uiImage: scanImage).resizable().scaledToFit().frame(maxWidth: .infinity, maxHeight: .infinity)
                } else {
                    VStack(spacing: 8) { ProgressView(); Text(AssetFiles.exists(assetID) ? "Scan wird geöffnet" : "Scan wird vom Server geholt").font(.footnote).foregroundStyle(.secondary) }
                }
            }
            if let heading = page.heading {
                VStack {
                    Text(heading).font(.footnote).foregroundStyle(Color(white: 0.45)).padding(.leading, displayWidth * 0.12).padding(.top, 14)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    Spacer()
                }
                .allowsHitTesting(false)
            }
            if let strokes {
                PencilCanvas(
                    pageID: page.id,
                    notebookID: notebook.id,
                    indexDrawing: page.content == .drawing,
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
        .task(id: model.assetRevision) {
            if case .scan(let assetID) = page.content {
                scanImage = await Task.detached { AssetFiles.image(for: assetID) }.value
            }
        }
    }
}
