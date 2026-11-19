import Foundation
import UIKit

/// The scans and PDFs on this device. The server holds a copy of each, found by the same id.
enum AssetFiles {
    static let directory: URL = {
        let url = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("JuNotes/assets", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }()

    static func url(for id: UUID) -> URL { directory.appendingPathComponent(id.uuidString.lowercased()) }

    static func exists(_ id: UUID) -> Bool { FileManager.default.fileExists(atPath: url(for: id).path) }

    static func save(_ data: Data, id: UUID) throws { try data.write(to: url(for: id), options: .atomic) }

    static func data(for id: UUID) -> Data? { try? Data(contentsOf: url(for: id)) }

    static func image(for id: UUID) -> UIImage? { data(for: id).flatMap(UIImage.init(data:)) }
}

extension UIImage {
    /// A copy no larger than `maxDimension` on its longer side: A4 at 300 dpi is plenty for reading and keeps a scan around a megabyte.
    func scaled(maxDimension: CGFloat) -> UIImage {
        let longest = max(size.width, size.height)
        guard longest > maxDimension else { return self }
        let factor = maxDimension / longest
        let target = CGSize(width: size.width * factor, height: size.height * factor)
        let format = UIGraphicsImageRendererFormat.default()
        format.scale = 1
        return UIGraphicsImageRenderer(size: target, format: format).image { _ in draw(in: CGRect(origin: .zero, size: target)) }
    }
}
