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
    @State private var pdfImage: UIImage?
    @State private var showSolution = false

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
            if let printed = page.printedText {
                // Printed by the app, such as the tasks of a mock test; she writes around it.
                VStack {
                    Text(printed).font(.system(size: 15 * displayWidth / PageGeometry.width)).foregroundStyle(.black)
                        .padding(.horizontal, displayWidth * 0.12).padding(.top, displayWidth * 0.08)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    Spacer()
                }
                .allowsHitTesting(false)
            }
            if case .pdf(let assetID, _) = page.content {
                if let pdfImage {
                    Image(uiImage: pdfImage).resizable().scaledToFit().frame(maxWidth: .infinity, maxHeight: .infinity)
                } else {
                    VStack(spacing: 8) { ProgressView(); Text(AssetFiles.exists(assetID) ? "PDF-Seite wird geöffnet" : "PDF wird vom Server geholt").font(.footnote).foregroundStyle(.secondary) }
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
        .overlay(alignment: .bottom) { additionsBox }
        .overlay(alignment: .topTrailing) { solutionButton }
        .frame(width: displayWidth, height: height)
        .clipShape(RoundedRectangle(cornerRadius: 4))
        .shadow(color: .black.opacity(0.18), radius: 3, y: 1)
        .task(id: page.id) { strokes = (try? await model.store.strokes(pageID: page.id)) ?? [] }
        .task(id: model.assetRevision) {
            if case .scan(let assetID) = page.content {
                scanImage = await Task.detached { AssetFiles.image(for: assetID) }.value
            }
            if case .pdf(let assetID, let index) = page.content {
                pdfImage = PDFPages.shared.image(assetID: assetID, pageIndex: index, width: displayWidth)
            }
        }
    }

    /// The suggestions she accepted, in a box of their own at the foot of the page.
    @ViewBuilder private var additionsBox: some View {
        if !page.additions.isEmpty {
            VStack(alignment: .leading, spacing: 6) {
                Text("Ergänzungen").font(.caption.weight(.semibold)).foregroundStyle(Color(red: 0.1, green: 0.4, blue: 0.2))
                ForEach(page.additions.indices, id: \.self) { index in
                    HStack(alignment: .top) {
                        Text(page.additions[index]).font(.footnote).foregroundStyle(.black)
                        Spacer()
                        Button { Task { await model.removeAddition(at: index, from: page.id) } } label: { Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary) }.buttonStyle(.plain)
                    }
                }
            }
            .padding(10)
            .background(Color(red: 0.88, green: 0.96, blue: 0.9), in: RoundedRectangle(cornerRadius: 8))
            .padding(12)
        }
    }

    /// The solution stays hidden until she asks.
    @ViewBuilder private var solutionButton: some View {
        if let solution = page.solution {
            VStack(alignment: .trailing, spacing: 8) {
                Button(showSolution ? "Lösung verbergen" : "Lösung zeigen", systemImage: showSolution ? "eye.slash" : "eye") { showSolution.toggle() }
                    .font(.footnote).buttonStyle(.bordered).tint(.gray)
                if showSolution {
                    Text(solution).font(.footnote).foregroundStyle(.black).padding(10)
                        .background(Color(red: 1, green: 0.97, blue: 0.85), in: RoundedRectangle(cornerRadius: 8))
                        .frame(maxWidth: displayWidth * 0.7, alignment: .leading)
                }
            }
            .padding(10)
        }
    }
}
