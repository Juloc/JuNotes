import JuNotesCore
import PhotosUI
import SwiftUI
import UIKit

/// Choices offered when she designs a folder or a cover.
enum Choices {
    static let colors = ["#0a84ff", "#ff9f0a", "#30d158", "#64d2ff", "#bf5af2", "#ff375f", "#ac8e68", "#8e8e93"]
    static let symbols = [
        "function", "text.book.closed", "character.bubble", "leaf", "atom", "globe.europe.africa", "paintpalette", "music.note", "figure.run", "laptopcomputer",
        "book", "pencil", "graduationcap", "heart", "star", "sparkles", "house", "figure.2.and.child.holdinghands", "teddybear", "puzzlepiece",
        "camera", "folder", "tray.full", "calendar", "flag", "lightbulb", "cross.case", "theatermasks", "hands.clap", "brain.head.profile",
    ]
}

/// The front of a folder or notebook: colour, gradient or her picture, with a pattern and a symbol or emoji on top.
struct CoverView: View {
    let cover: Cover
    var title: String?

    @Environment(AppModel.self) private var model
    @State private var picture: UIImage?

    var body: some View {
        GeometryReader { proxy in
            ZStack {
                background
                patternLayer
                glyph(size: proxy.size.width)
                if let title, !title.isEmpty {
                    VStack {
                        Spacer()
                        Text(title)
                            .font(.system(size: max(11, proxy.size.width * 0.095), weight: .bold, design: .rounded))
                            .foregroundStyle(.white)
                            .shadow(color: .black.opacity(0.35), radius: 2, y: 1)
                            .lineLimit(2)
                            .multilineTextAlignment(.leading)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(max(8, proxy.size.width * 0.07))
                    }
                }
            }
            .frame(width: proxy.size.width, height: proxy.size.height)
        }
        .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
        .shadow(color: .black.opacity(0.16), radius: 3, y: 1.5)
        .task(id: PictureKey(id: cover.assetID, revision: model.assetRevision)) {
            guard let id = cover.assetID else { picture = nil; return }
            picture = await Task.detached { AssetFiles.image(for: id) }.value
        }
    }

    private struct PictureKey: Hashable {
        var id: UUID?
        var revision: Int
    }

    private var gradient: LinearGradient {
        LinearGradient(colors: [Color(hex: cover.colorA), Color(hex: cover.colorB)], startPoint: .topLeading, endPoint: .bottomTrailing)
    }

    @ViewBuilder private var background: some View {
        switch cover.style {
        case .color:
            Color(hex: cover.colorA)
        case .gradient:
            gradient
        case .image:
            if let picture {
                Color.clear.overlay { Image(uiImage: picture).resizable().scaledToFill() }.clipped()
            } else {
                gradient
            }
        }
    }

    @ViewBuilder private var patternLayer: some View {
        if cover.pattern != .none {
            Canvas { context, size in
                let step: CGFloat = max(10, size.width / 12)
                let ink = GraphicsContext.Shading.color(.white.opacity(0.22))
                switch cover.pattern {
                case .none:
                    break
                case .lines:
                    var y = step
                    while y < size.height {
                        context.stroke(Path { $0.move(to: CGPoint(x: 0, y: y)); $0.addLine(to: CGPoint(x: size.width, y: y)) }, with: ink, lineWidth: 1)
                        y += step
                    }
                case .grid:
                    var y = step
                    while y < size.height {
                        context.stroke(Path { $0.move(to: CGPoint(x: 0, y: y)); $0.addLine(to: CGPoint(x: size.width, y: y)) }, with: ink, lineWidth: 0.8)
                        y += step
                    }
                    var x = step
                    while x < size.width {
                        context.stroke(Path { $0.move(to: CGPoint(x: x, y: 0)); $0.addLine(to: CGPoint(x: x, y: size.height)) }, with: ink, lineWidth: 0.8)
                        x += step
                    }
                case .dots:
                    var y = step
                    while y < size.height {
                        var x = step
                        while x < size.width {
                            context.fill(Path(ellipseIn: CGRect(x: x - 1.5, y: y - 1.5, width: 3, height: 3)), with: ink)
                            x += step
                        }
                        y += step
                    }
                }
            }
            .allowsHitTesting(false)
        }
    }

    @ViewBuilder private func glyph(size: CGFloat) -> some View {
        if let emoji = cover.emoji, !emoji.isEmpty {
            Text(emoji).font(.system(size: size * 0.34))
        } else if let symbol = cover.symbol, !symbol.isEmpty {
            Image(systemName: symbol)
                .font(.system(size: size * 0.3, weight: .semibold))
                .foregroundStyle(.white.opacity(0.92))
                .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
        }
    }
}

/// A notebook on the shelf: its own cover if she designed one, otherwise its first page. The title stands below.
struct NotebookCover: View {
    let notebook: Notebook
    let subject: Subject?

    @Environment(AppModel.self) private var model
    @State private var firstPage: Page?

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            ZStack(alignment: .topLeading) {
                if let cover = notebook.cover {
                    CoverView(cover: cover)
                        .aspectRatio(PageGeometry.width / PageGeometry.height, contentMode: .fit)
                } else if notebook.locked {
                    Color(.secondarySystemFill)
                        .aspectRatio(PageGeometry.width / PageGeometry.height, contentMode: .fit)
                        .overlay { Image(systemName: "lock.fill").font(.largeTitle).foregroundStyle(.secondary) }
                        .clipShape(RoundedRectangle(cornerRadius: 4, style: .continuous))
                } else if let firstPage {
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
                        .background(Color(hex: subject.color), in: Capsule())
                        .padding(8)
                }
            }
            HStack(spacing: 4) {
                Text(notebook.title).font(.headline).lineLimit(2)
                if notebook.favorite { Image(systemName: "star.fill").font(.caption).foregroundStyle(.yellow) }
                if notebook.locked { Image(systemName: "lock.fill").font(.caption).foregroundStyle(.secondary) }
            }
            Text(notebook.type == .whiteboard ? "Whiteboard" : "Heft").font(.footnote).foregroundStyle(.secondary)
        }
        .task(id: notebook.id) { firstPage = (try? await model.store.pages(notebookID: notebook.id))?.first }
    }
}

/// The sections of a form in which she designs a cover: preview, gradients, own colours, a photo, a symbol or emoji, a pattern.
struct CoverEditor: View {
    @Binding var cover: Cover
    var aspect: CGFloat = PageGeometry.width / PageGeometry.height

    @Environment(AppModel.self) private var model
    @State private var photo: PhotosPickerItem?
    @State private var loadingPhoto = false
    @State private var describing = false
    @State private var wish = ""
    @State private var designing = false

    var body: some View {
        Section {
            HStack {
                Spacer()
                CoverView(cover: cover)
                    .aspectRatio(aspect, contentMode: .fit)
                    .frame(height: 180)
                Spacer()
            }
            .listRowBackground(Color.clear)
        }

        Section {
            Button {
                describing = true
            } label: {
                Label(designing ? "Die KI entwirft …" : "Cover von der KI entwerfen", systemImage: "sparkles")
            }
            .disabled(designing)
        } footer: {
            Text("Beschreibe das Thema, zum Beispiel „Bruchrechnung, ruhig und blau“. Die KI wählt Farben, Emoji oder Symbol und Muster. Das braucht den Server.")
        }
        .alert("Cover entwerfen", isPresented: $describing) {
            TextField("Worum geht es?", text: $wish)
            Button("Abbrechen", role: .cancel) {}
            Button("Entwerfen") { Task { await design() } }
        }

        Section("Verlauf") {
            LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: 6), spacing: 12) {
                ForEach(Cover.presets, id: \.name) { preset in
                    let chosen = cover.style == .gradient && cover.colorA == preset.a && cover.colorB == preset.b
                    Button {
                        cover.style = .gradient
                        cover.colorA = preset.a
                        cover.colorB = preset.b
                    } label: {
                        Circle()
                            .fill(LinearGradient(colors: [Color(hex: preset.a), Color(hex: preset.b)], startPoint: .topLeading, endPoint: .bottomTrailing))
                            .frame(width: 44, height: 44)
                            .overlay(Circle().stroke(Brand.accent, lineWidth: chosen ? 3 : 0).padding(-4))
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel(preset.name)
                }
            }
            .padding(.vertical, 6)
        }

        Section("Eigene Farben") {
            Picker("Art", selection: $cover.style) {
                Text("Verlauf").tag(Cover.Style.gradient)
                Text("Farbe").tag(Cover.Style.color)
                Text("Bild").tag(Cover.Style.image)
            }
            .pickerStyle(.segmented)
            ColorPicker("Farbe", selection: colorBinding(\.colorA), supportsOpacity: false)
            if cover.style == .gradient { ColorPicker("Zweite Farbe", selection: colorBinding(\.colorB), supportsOpacity: false) }
        }

        Section {
            PhotosPicker(selection: $photo, matching: .images) {
                Label(loadingPhoto ? "Bild wird geladen" : "Foto aus der Mediathek wählen", systemImage: "photo")
            }
            if cover.style == .image {
                Button("Bild entfernen", systemImage: "trash", role: .destructive) { cover = cover.withImage(nil) }
            }
        } header: {
            Text("Bild")
        } footer: {
            Text("Das Foto wird auf eine handliche Größe verkleinert und mit dem nächsten Sync auf den Server gelegt.")
        }
        .onChange(of: photo) { _, item in
            guard let item else { return }
            Task { await load(item) }
        }

        Section("Symbol") {
            LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: 6), spacing: 8) {
                ForEach(Choices.symbols, id: \.self) { name in
                    let chosen = cover.symbol == name && (cover.emoji ?? "").isEmpty
                    Image(systemName: name)
                        .font(.title3)
                        .frame(width: 44, height: 44)
                        .background(chosen ? Brand.accent.opacity(0.2) : Color.clear, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                        .contentShape(Rectangle())
                        .onTapGesture {
                            cover.symbol = name
                            cover.emoji = nil
                        }
                        .accessibilityLabel(name)
                        .accessibilityAddTraits(.isButton)
                }
            }
            TextField("Oder ein Emoji", text: Binding(get: { cover.emoji ?? "" }, set: { cover.emoji = String($0.prefix(2)).isEmpty ? nil : String($0.prefix(2)) }))
            if cover.symbol != nil || (cover.emoji ?? "").isEmpty == false {
                Button("Kein Symbol", role: .destructive) {
                    cover.symbol = nil
                    cover.emoji = nil
                }
            }
        }

        Section("Muster") {
            Picker("Muster", selection: $cover.pattern) {
                Text("Ohne").tag(Cover.Pattern.none)
                Text("Linien").tag(Cover.Pattern.lines)
                Text("Karo").tag(Cover.Pattern.grid)
                Text("Punkte").tag(Cover.Pattern.dots)
            }
            .pickerStyle(.segmented)
        }
    }

    private func colorBinding(_ keyPath: WritableKeyPath<Cover, String>) -> Binding<Color> {
        Binding(
            get: { Color(hex: cover[keyPath: keyPath]) },
            set: {
                cover[keyPath: keyPath] = StrokeConversion.hex(UIColor($0))
                if keyPath == \.colorA, cover.style == .color { cover.colorB = cover.colorA }
            }
        )
    }

    private func design() async {
        let description = wish.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !description.isEmpty else { return }
        guard let transport = model.makeTransport() else {
            model.notice = "Der Server ist noch nicht eingerichtet. Trage Adresse und Schlüssel in den Einstellungen ein."
            return
        }
        designing = true
        defer { designing = false }
        do {
            let design = try await transport.ask(.cover, AIRequest(text: description), as: CoverDesign.self)
            cover = design.cover
        } catch {
            model.notice = message(for: error)
        }
    }

    private func load(_ item: PhotosPickerItem) async {
        loadingPhoto = true
        defer {
            loadingPhoto = false
            photo = nil
        }
        guard let data = try? await item.loadTransferable(type: Data.self), let image = UIImage(data: data) else {
            model.notice = "Das Bild konnte nicht geladen werden."
            return
        }
        guard let jpeg = image.scaled(maxDimension: 1000).jpegData(compressionQuality: 0.85) else {
            model.notice = "Das Bild konnte nicht verkleinert werden."
            return
        }
        let id = UUID()
        do {
            try AssetFiles.save(jpeg, id: id)
            try await model.store.registerAsset(id)
            cover = cover.withImage(id)
        } catch {
            model.notice = "Das Bild konnte nicht gespeichert werden."
        }
    }
}
