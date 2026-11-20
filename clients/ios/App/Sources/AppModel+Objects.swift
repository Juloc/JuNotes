import AVFoundation
import JuNotesCore
import UIKit

extension AppModel {
    /// Saves a new or changed object and tells the pages to look again.
    func save(_ object: PageObject) async {
        await write { try await self.store.put(object) }
        objectRevision += 1
        if selectedObject?.id == object.id { selectedObject = object }
    }

    func delete(_ object: PageObject) async {
        await write { try await self.store.write(object.deletion()) }
        if selectedObject?.id == object.id { selectedObject = nil }
        objectRevision += 1
    }

    /// Puts something on the page, in the middle of its upper half, in front of what is there, and selects it so it can be moved at once.
    @discardableResult
    func addObject(_ type: PageObject.Kind, on pageID: UUID, text: String? = nil, assetID: UUID? = nil, color: String? = nil, size: (width: Double, height: Double)? = nil, fontSize: Double = 18) async -> PageObject {
        let existing = (try? await store.objects(pageID: pageID)) ?? []
        let defaults = PageObject.defaultSize(for: type)
        let width = size?.width ?? defaults.width
        let height = size?.height ?? defaults.height
        let object = PageObject(pageID: pageID, type: type, x: (PageGeometry.width - width) / 2, y: 180 + Double(existing.count % 5) * 24, width: width, height: height, text: text, assetID: assetID, color: color, fontSize: fontSize, z: (existing.map(\.z).max() ?? -1) + 1)
        await save(object)
        editingObjects = true
        selectedObject = object
        return object
    }

    /// A photo from the library, made smaller, kept like a scan, and put on the page.
    func addPhoto(_ data: Data, on pageID: UUID) async {
        guard let image = UIImage(data: data), let jpeg = image.scaled(maxDimension: 1600).jpegData(compressionQuality: 0.85) else {
            notice = "Das Bild konnte nicht geladen werden."
            return
        }
        let id = UUID()
        do {
            try AssetFiles.save(jpeg, id: id)
            try await store.registerAsset(id)
        } catch {
            notice = "Das Bild konnte nicht gespeichert werden."
            return
        }
        let size = PageObject.imageSize(pixelWidth: Double(image.size.width), pixelHeight: Double(image.size.height))
        await addObject(.image, on: pageID, assetID: id, size: size)
    }

    /// The text the iPad read from her handwriting on this page, as a text box she can edit: the nearest to turning handwriting into text.
    func addRecognisedText(on pageID: UUID) async {
        let text = ((try? await store.indexedText(pageID: pageID)) ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else {
            notice = "Auf dieser Seite wurde noch keine Schrift erkannt. Schreib etwas und versuche es in ein paar Sekunden noch einmal."
            return
        }
        let lines = max(3, text.split(separator: "\n").count + text.count / 40)
        await addObject(.text, on: pageID, text: text, size: (width: 480, height: min(600, Double(lines) * 22)), fontSize: 16)
    }
}

/// Plays a recording from a page, one at a time.
@MainActor
@Observable
final class AudioPlayer: NSObject, AVAudioPlayerDelegate {
    static let shared = AudioPlayer()
    private(set) var playingID: UUID?
    private var player: AVAudioPlayer?

    func toggle(_ assetID: UUID) {
        if playingID == assetID {
            stop()
            return
        }
        stop()
        guard AssetFiles.exists(assetID), let player = try? AVAudioPlayer(contentsOf: AssetFiles.url(for: assetID)) else { return }
        try? AVAudioSession.sharedInstance().setCategory(.playback)
        try? AVAudioSession.sharedInstance().setActive(true)
        player.delegate = self
        player.play()
        self.player = player
        playingID = assetID
    }

    func stop() {
        player?.stop()
        player = nil
        playingID = nil
    }

    nonisolated func audioPlayerDidFinishPlaying(_ player: AVAudioPlayer, successfully flag: Bool) {
        Task { @MainActor in
            self.player = nil
            self.playingID = nil
        }
    }
}

/// Records speech into a file kept like a scan.
@MainActor
@Observable
final class AudioRecorder {
    private(set) var isRecording = false
    private(set) var seconds = 0
    private var recorder: AVAudioRecorder?
    private var ticker: Task<Void, Never>?
    private var url: URL?

    func start() async -> Bool {
        guard await AVAudioApplication.requestRecordPermission() else { return false }
        let session = AVAudioSession.sharedInstance()
        do {
            try session.setCategory(.playAndRecord, mode: .default, options: [.defaultToSpeaker])
            try session.setActive(true)
            let target = FileManager.default.temporaryDirectory.appendingPathComponent("recording-\(UUID().uuidString).m4a")
            let settings: [String: Any] = [AVFormatIDKey: kAudioFormatMPEG4AAC, AVSampleRateKey: 22_050, AVNumberOfChannelsKey: 1, AVEncoderAudioQualityKey: AVAudioQuality.medium.rawValue]
            let recorder = try AVAudioRecorder(url: target, settings: settings)
            guard recorder.record() else { return false }
            self.recorder = recorder
            url = target
            isRecording = true
            seconds = 0
            ticker = Task { [weak self] in
                while !Task.isCancelled {
                    try? await Task.sleep(for: .seconds(1))
                    self?.seconds += 1
                }
            }
            return true
        } catch {
            return false
        }
    }

    /// The finished recording's bytes, or nil when there is none.
    func stop() -> Data? {
        ticker?.cancel()
        recorder?.stop()
        isRecording = false
        recorder = nil
        defer {
            if let url { try? FileManager.default.removeItem(at: url) }
            url = nil
        }
        return url.flatMap { try? Data(contentsOf: $0) }
    }
}
