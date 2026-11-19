import UIKit
import Vision

/// Reads the text in an image on the device, in German and English: printed worksheets and handwriting. Nothing leaves the iPad.
enum TextRecognition {
    static func recognize(_ image: UIImage) async -> String {
        guard let cgImage = image.cgImage else { return "" }
        return await Task.detached(priority: .utility) {
            let request = VNRecognizeTextRequest()
            request.recognitionLevel = .accurate
            request.recognitionLanguages = ["de-DE", "en-US"]
            request.usesLanguageCorrection = true
            do {
                try VNImageRequestHandler(cgImage: cgImage).perform([request])
            } catch {
                return ""
            }
            return (request.results ?? []).compactMap { $0.topCandidates(1).first?.string }.joined(separator: "\n")
        }.value
    }
}

extension UIImage {
    /// Ink on a transparent background put on white: text recognition wants dark on light.
    func onWhite() -> UIImage {
        UIGraphicsImageRenderer(size: size).image { context in
            UIColor.white.setFill()
            context.fill(CGRect(origin: .zero, size: size))
            draw(at: .zero)
        }
    }
}
