import JuNotesCore
import UIKit
import ZIPFoundation

/// What an import of a folder of PDFs brought in.
struct ZIPImportResult {
    var folders = 0
    var notebooks = 0
    var failed: [String] = []

    var sentence: String {
        var parts = ["\(notebooks) \(notebooks == 1 ? "Heft" : "Hefte") und \(folders) \(folders == 1 ? "Ordner" : "Ordner") übernommen."]
        if !failed.isEmpty { parts.append("Nicht gelesen: " + failed.prefix(5).joined(separator: ", ") + (failed.count > 5 ? " und \(failed.count - 5) weitere." : ".")) }
        return parts.joined(separator: " ")
    }
}

extension AppModel {
    /// A ZIP of folders and PDFs, such as GoodNotes exports a folder: every folder becomes a folder, every PDF a notebook,
    /// all inside the folder that is open. The shape of her library comes across as it was.
    func importZIP(from url: URL) async {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }

        let work = FileManager.default.temporaryDirectory.appendingPathComponent("ZIPImport-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: work) }
        do {
            try FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
            try FileManager.default.unzipItem(at: url, to: work)
        } catch {
            notice = "Das ZIP konnte nicht entpackt werden."
            return
        }

        var result = ZIPImportResult()
        await importDirectory(work, into: selectedFolder?.id, result: &result)
        await reload()
        notice = result.notebooks == 0 && result.folders == 0 ? "In diesem ZIP waren keine PDFs." : result.sentence
    }

    private func importDirectory(_ directory: URL, into parent: UUID?, result: inout ZIPImportResult) async {
        let entries = ((try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: [.isDirectoryKey])) ?? [])
            .filter { !$0.lastPathComponent.hasPrefix(".") && $0.lastPathComponent != "__MACOSX" }
            .sorted { $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending }
        for entry in entries {
            let isDirectory = (try? entry.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) ?? false
            if isDirectory {
                let folder = await addFolder(name: entry.lastPathComponent, color: Choices.colors[0], symbol: "folder", isSubject: false, cover: nil, in: parent)
                result.folders += 1
                await importDirectory(entry, into: folder.id, result: &result)
            } else if entry.pathExtension.lowercased() == "pdf" {
                do {
                    try await importPDF(from: entry, into: parent, opening: false)
                    result.notebooks += 1
                } catch {
                    result.failed.append(entry.lastPathComponent)
                }
            }
        }
    }
}
