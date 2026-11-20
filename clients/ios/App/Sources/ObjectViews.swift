import JuNotesCore
import SwiftUI
import UIKit

/// The objects of one page in one layer. Below her handwriting they are only to look at; above it, in editing mode, they can be moved, turned and resized.
/// Recordings are always above the ink, so a tap can play them.
struct ObjectLayer: View {
    let objects: [PageObject]
    let displayWidth: CGFloat
    let logicalSize: CGSize
    let editable: Bool

    var body: some View {
        let scale = displayWidth / logicalSize.width
        ZStack(alignment: .topLeading) {
            ForEach(objects) { object in
                ObjectView(object: object, scale: scale, logicalSize: logicalSize, editable: editable)
            }
        }
        .frame(width: displayWidth, height: logicalSize.height * scale, alignment: .topLeading)
    }
}

struct ObjectView: View {
    let object: PageObject
    let scale: CGFloat
    let logicalSize: CGSize
    let editable: Bool

    @Environment(AppModel.self) private var model
    @State private var drag = CGSize.zero
    @GestureState private var pinch: CGFloat = 1
    @GestureState private var twist = Angle.zero
    @State private var picture: UIImage?

    private var selected: Bool { model.selectedObject?.id == object.id }

    var body: some View {
        let width = object.width * scale * pinch
        let height = object.height * scale * pinch
        content(width: width, height: height)
            .frame(width: width, height: height)
            .overlay {
                if selected && editable { RoundedRectangle(cornerRadius: 4).stroke(Brand.accent, style: StrokeStyle(lineWidth: 2, dash: [6, 4])) }
            }
            .rotationEffect(.degrees(object.rotation) + twist)
            .position(x: (object.x + object.width / 2) * scale + drag.width, y: (object.y + object.height / 2) * scale + drag.height)
            .onTapGesture {
                if editable {
                    model.selectedObject = object
                } else if object.type == .audio, let id = object.assetID {
                    AudioPlayer.shared.toggle(id)
                }
            }
            .gesture(moveGesture, including: editable ? .all : .none)
            .simultaneousGesture(resizeGesture, including: editable ? .all : .none)
            .simultaneousGesture(turnGesture, including: editable ? .all : .none)
            .task(id: object.assetID) {
                guard object.type == .image, let id = object.assetID else { return }
                picture = await Task.detached { AssetFiles.image(for: id) }.value
            }
    }

    // MARK: Gestures

    private var moveGesture: some Gesture {
        DragGesture()
            .onChanged { value in
                if model.selectedObject?.id != object.id { model.selectedObject = object }
                drag = value.translation
            }
            .onEnded { value in
                var moved = object
                moved.x += value.translation.width / scale
                moved.y += value.translation.height / scale
                moved = moved.clamped(toWidth: logicalSize.width, height: logicalSize.height)
                drag = .zero
                Task { await model.save(moved) }
            }
    }

    private var resizeGesture: some Gesture {
        MagnifyGesture()
            .updating($pinch) { value, state, _ in state = value.magnification }
            .onEnded { value in
                var resized = object
                resized.width = max(16, object.width * value.magnification)
                resized.height = max(16, object.height * value.magnification)
                resized.fontSize = max(6, object.fontSize * value.magnification)
                Task { await model.save(resized) }
            }
    }

    private var turnGesture: some Gesture {
        RotateGesture()
            .updating($twist) { value, state, _ in state = value.rotation }
            .onEnded { value in
                var turned = object
                turned.rotation = (object.rotation + value.rotation.degrees).truncatingRemainder(dividingBy: 360)
                Task { await model.save(turned) }
            }
    }

    // MARK: Looks

    @ViewBuilder private func content(width: CGFloat, height: CGFloat) -> some View {
        switch object.type {
        case .text:
            Text(object.text ?? "")
                .font(.system(size: object.fontSize * scale * pinch))
                .foregroundStyle(Color(hex: object.color ?? "#1d1d1f"))
                .frame(width: width, height: height, alignment: .topLeading)
        case .image:
            if let picture {
                Color.clear.overlay { Image(uiImage: picture).resizable().scaledToFill() }
                    .clipped()
                    .clipShape(RoundedRectangle(cornerRadius: 3))
                    .shadow(color: .black.opacity(0.2), radius: 2, y: 1)
            } else {
                RoundedRectangle(cornerRadius: 3).fill(Color(.secondarySystemFill)).overlay { ProgressView() }
            }
        case .sticker:
            if let symbol = object.text, UIImage(systemName: symbol) != nil {
                Image(systemName: symbol).resizable().scaledToFit().foregroundStyle(Color(hex: object.color ?? "#ff9f0a"))
            } else {
                Text(object.text ?? "⭐️").font(.system(size: min(width, height) * 0.8)).minimumScaleFactor(0.3)
            }
        case .tape:
            ZStack {
                Rectangle().fill(Color(hex: object.color ?? "#ffd15c").opacity(0.62))
                Canvas { context, size in
                    var x: CGFloat = -size.height
                    while x < size.width {
                        context.stroke(Path { $0.move(to: CGPoint(x: x, y: size.height)); $0.addLine(to: CGPoint(x: x + size.height, y: 0)) }, with: .color(.white.opacity(0.35)), lineWidth: 3)
                        x += 10
                    }
                }
            }
            .clipShape(TapeShape())
        case .audio:
            let playing = AudioPlayer.shared.playingID == object.assetID
            HStack(spacing: 8) {
                Image(systemName: playing ? "stop.fill" : "play.fill")
                Image(systemName: "waveform")
                Text(playing ? "Läuft" : "Aufnahme").font(.system(size: 13 * max(0.8, scale)))
            }
            .padding(.horizontal, 12)
            .frame(width: width, height: height)
            .background(Brand.accent, in: Capsule())
            .foregroundStyle(.white)
        }
    }
}

/// A strip of tape with torn ends.
struct TapeShape: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        let zig: CGFloat = 3
        path.move(to: CGPoint(x: rect.minX, y: rect.minY))
        path.addLine(to: CGPoint(x: rect.maxX, y: rect.minY))
        var y = rect.minY
        var toggle = false
        while y < rect.maxY {
            y += zig * 1.5
            path.addLine(to: CGPoint(x: rect.maxX - (toggle ? zig : 0), y: min(y, rect.maxY)))
            toggle.toggle()
        }
        path.addLine(to: CGPoint(x: rect.minX, y: rect.maxY))
        y = rect.maxY
        toggle = false
        while y > rect.minY {
            y -= zig * 1.5
            path.addLine(to: CGPoint(x: rect.minX + (toggle ? zig : 0), y: max(y, rect.minY)))
            toggle.toggle()
        }
        path.closeSubpath()
        return path
    }
}

/// The buttons for the selected object: bigger, smaller, turn, colour, text, forward, back, copy, delete.
struct ObjectInspector: View {
    @Environment(AppModel.self) private var model
    @State private var editingText = false
    @State private var draft = ""

    private let colors = ["#1d1d1f", "#ff375f", "#ff9f0a", "#ffd15c", "#30d158", "#0a84ff", "#bf5af2", "#ffffff"]

    var body: some View {
        if let object = model.selectedObject {
            VStack(spacing: 10) {
                HStack(spacing: 14) {
                    button("Kleiner", "minus.magnifyingglass") { change(object) { $0.width *= 0.85; $0.height *= 0.85; $0.fontSize *= 0.85 } }
                    button("Größer", "plus.magnifyingglass") { change(object) { $0.width *= 1.18; $0.height *= 1.18; $0.fontSize *= 1.18 } }
                    button("Links drehen", "rotate.left") { change(object) { $0.rotation -= 15 } }
                    button("Rechts drehen", "rotate.right") { change(object) { $0.rotation += 15 } }
                    Divider().frame(height: 24)
                    button("Nach vorn", "square.2.layers.3d.top.filled") { change(object) { $0.z += 1 } }
                    button("Nach hinten", "square.2.layers.3d.bottom.filled") { change(object) { $0.z -= 1 } }
                    button("Kopieren", "plus.square.on.square") {
                        Task {
                            var copy = object
                            copy.id = UUID()
                            copy.x += 20
                            copy.y += 20
                            copy.z += 1
                            await model.save(copy)
                            model.selectedObject = copy
                        }
                    }
                    if object.type == .text {
                        button("Text ändern", "character.cursor.ibeam") {
                            draft = object.text ?? ""
                            editingText = true
                        }
                    }
                    Spacer(minLength: 8)
                    button("Löschen", "trash") { Task { await model.delete(object) } }.tint(.red)
                    button("Fertig", "checkmark.circle.fill") {
                        model.selectedObject = nil
                        model.editingObjects = false
                    }
                }
                if object.type == .text || object.type == .tape || (object.type == .sticker && UIImage(systemName: object.text ?? "") != nil) {
                    HStack(spacing: 10) {
                        ForEach(colors, id: \.self) { value in
                            Circle().fill(Color(hex: value)).frame(width: 28, height: 28)
                                .overlay(Circle().stroke(Color.secondary.opacity(0.5), lineWidth: 1))
                                .overlay { if (object.color ?? "") == value { Image(systemName: "checkmark").font(.caption.bold()).foregroundStyle(value == "#ffffff" || value == "#ffd15c" ? .black : .white) } }
                                .onTapGesture { change(object) { $0.color = value } }
                        }
                    }
                }
            }
            .padding(.horizontal, 16).padding(.vertical, 10)
            .background(.bar)
            .overlay(alignment: .top) { Divider() }
            .alert("Text", isPresented: $editingText) {
                TextField("Text", text: $draft, axis: .vertical)
                Button("Abbrechen", role: .cancel) {}
                Button("Sichern") { change(object) { $0.text = draft } }
            }
        }
    }

    private func change(_ object: PageObject, _ edit: (inout PageObject) -> Void) {
        var changed = object
        edit(&changed)
        Task { await model.save(changed) }
    }

    private func button(_ title: String, _ symbol: String, action: @escaping () -> Void) -> some View {
        Button(action: action) { Label(title, systemImage: symbol).labelStyle(.iconOnly).font(.title3).frame(width: 36, height: 36) }
            .accessibilityLabel(title)
    }
}

// MARK: Adding

/// Stickers, emoji and strips of washi tape to choose from.
struct StickerSheet: View {
    let onPick: (PageObject.Kind, String?, String?) -> Void

    @Environment(\.dismiss) private var dismiss

    private let emoji = ["⭐️", "❤️", "✅", "❗️", "❓", "💡", "📌", "📎", "🔥", "👍", "🎉", "🌈", "🌻", "🍎", "🧸", "🎨", "✏️", "📚", "🔔", "⏰", "😀", "🤔", "😴", "💪"]
    private let symbols = ["star.fill", "heart.fill", "flag.fill", "bookmark.fill", "lightbulb.fill", "exclamationmark.triangle.fill", "checkmark.seal.fill", "arrow.right", "arrow.up.right", "pin.fill", "bell.fill", "sparkles"]
    private let tapes = ["#ffd15c", "#ff9fc4", "#8fd96b", "#6fb1ff", "#c87cf0", "#ff9a7a", "#4fd1d9", "#e6cfa5"]

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 22) {
                    section("Emoji") {
                        LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: 8), spacing: 10) {
                            ForEach(emoji, id: \.self) { value in
                                Button { pick(.sticker, value, nil) } label: { Text(value).font(.largeTitle) }.buttonStyle(.plain)
                            }
                        }
                    }
                    section("Symbole") {
                        LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: 6), spacing: 14) {
                            ForEach(symbols, id: \.self) { value in
                                Button { pick(.sticker, value, "#ff9f0a") } label: { Image(systemName: value).font(.title).foregroundStyle(Color(hex: "#ff9f0a")).frame(height: 44) }.buttonStyle(.plain)
                            }
                        }
                    }
                    section("Washi-Tape") {
                        LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: 4), spacing: 12) {
                            ForEach(tapes, id: \.self) { value in
                                Button { pick(.tape, nil, value) } label: {
                                    Rectangle().fill(Color(hex: value).opacity(0.65)).frame(height: 26).clipShape(TapeShape())
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }
                }
                .padding(20)
            }
            .navigationTitle("Sticker")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Schließen") { dismiss() } } }
        }
        .presentationDetents([.medium, .large])
    }

    private func pick(_ kind: PageObject.Kind, _ text: String?, _ color: String?) {
        onPick(kind, text, color)
        dismiss()
    }

    private func section<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title).font(.headline)
            content()
        }
    }
}

/// Records speech and puts it on the page as a chip that plays it.
struct AudioRecorderSheet: View {
    let pageID: UUID

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var recorder = AudioRecorder()
    @State private var denied = false

    var body: some View {
        NavigationStack {
            VStack(spacing: 24) {
                Spacer()
                Text(String(format: "%d:%02d", recorder.seconds / 60, recorder.seconds % 60)).font(.system(size: 56, weight: .semibold, design: .rounded)).monospacedDigit()
                if denied {
                    Text("Ohne Erlaubnis für das Mikrofon geht keine Aufnahme. Du kannst sie in den Einstellungen des iPads unter JuNotes erteilen.").multilineTextAlignment(.center).foregroundStyle(.secondary)
                }
                Button {
                    if recorder.isRecording { finish() } else { Task { denied = !(await recorder.start()) } }
                } label: {
                    Label(recorder.isRecording ? "Beenden und einfügen" : "Aufnahme starten", systemImage: recorder.isRecording ? "stop.circle.fill" : "record.circle")
                        .font(.title3.weight(.semibold))
                        .padding(.horizontal, 24).padding(.vertical, 14)
                        .background(recorder.isRecording ? Color.red : Brand.accent, in: Capsule())
                        .foregroundStyle(.white)
                }
                Spacer()
            }
            .padding()
            .navigationTitle("Aufnahme")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { _ = recorder.stop(); dismiss() } } }
        }
        .presentationDetents([.medium])
        .interactiveDismissDisabled(recorder.isRecording)
    }

    private func finish() {
        guard let data = recorder.stop(), !data.isEmpty else { dismiss(); return }
        Task {
            let id = UUID()
            do {
                try AssetFiles.save(data, id: id)
                try await model.store.registerAsset(id)
                await model.addObject(.audio, on: pageID, assetID: id)
            } catch {
                model.notice = "Die Aufnahme konnte nicht gespeichert werden."
            }
            dismiss()
        }
    }
}

// MARK: Laser pointer

/// A red dot that follows the finger or the Pencil and fades behind it; nothing is written. For showing something on the page.
struct LaserOverlay: View {
    @State private var trail: [(point: CGPoint, time: Date)] = []
    @State private var active = false

    var body: some View {
        TimelineView(.animation) { timeline in
            Canvas { context, _ in
                let now = timeline.date
                for index in trail.indices.dropFirst() {
                    let age = now.timeIntervalSince(trail[index].time)
                    let alpha = max(0, 1 - age / 0.9)
                    guard alpha > 0 else { continue }
                    var path = Path()
                    path.move(to: trail[index - 1].point)
                    path.addLine(to: trail[index].point)
                    context.stroke(path, with: .color(.red.opacity(alpha * 0.9)), style: StrokeStyle(lineWidth: 5 + 5 * alpha, lineCap: .round))
                }
                if active, let last = trail.last {
                    context.fill(Path(ellipseIn: CGRect(x: last.point.x - 11, y: last.point.y - 11, width: 22, height: 22)), with: .color(.red.opacity(0.35)))
                    context.fill(Path(ellipseIn: CGRect(x: last.point.x - 6, y: last.point.y - 6, width: 12, height: 12)), with: .color(.red))
                }
            }
            .onChange(of: timeline.date) { _, now in
                trail.removeAll { now.timeIntervalSince($0.time) > 1 }
            }
        }
        .contentShape(Rectangle())
        .gesture(
            DragGesture(minimumDistance: 0)
                .onChanged { value in
                    active = true
                    trail.append((value.location, Date()))
                }
                .onEnded { _ in active = false }
        )
        .accessibilityLabel("Laserpointer")
    }
}
