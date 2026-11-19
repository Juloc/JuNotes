import JuNotesCore
import PencilKit
import UIKit

/// PencilKit's strokes and JuNotes' own stroke format, both ways. The stored format is what the server and the web app understand.
enum StrokeConversion {
    /// PencilKit strokes carry no identity here, so one is recognised by when it was made, how many points it has and where it starts and ends.
    static func fingerprint(_ stroke: PKStroke) -> String {
        let path = stroke.path
        guard path.count > 0 else { return "empty-\(path.creationDate.timeIntervalSince1970)" }
        let first = path[0].location
        let last = path[path.count - 1].location
        return "\(path.creationDate.timeIntervalSince1970)|\(path.count)|\(Int(first.x * 10)),\(Int(first.y * 10))|\(Int(last.x * 10)),\(Int(last.y * 10))"
    }

    static func stroke(from pk: PKStroke) -> Stroke {
        let tool: StrokeTool
        switch pk.ink.inkType {
        case .marker: tool = .marker
        case .pencil: tool = .pencil
        default: tool = .pen
        }

        let path = pk.path
        var points: [StrokePoint] = []
        var totalWidth = 0.0
        for index in 0..<path.count {
            let point = path[index]
            totalWidth += Double(point.size.width)
            points.append(StrokePoint(x: Double(point.location.x), y: Double(point.location.y), pressure: Double(point.force), altitude: Double(point.altitude), azimuth: Double(point.azimuth), roll: 0, time: point.timeOffset))
        }
        return Stroke(tool: tool, color: hex(pk.ink.color), width: path.count == 0 ? 2 : totalWidth / Double(path.count), points: points)
    }

    static func pkStroke(from stroke: Stroke, creationDate: Date) -> PKStroke {
        let inkType: PKInk.InkType
        switch stroke.tool {
        case .marker: inkType = .marker
        case .pencil: inkType = .pencil
        default: inkType = .pen
        }

        let size = CGSize(width: stroke.width, height: stroke.width)
        let controlPoints = stroke.points.map {
            PKStrokePoint(location: CGPoint(x: $0.x, y: $0.y), timeOffset: $0.time, size: size, opacity: 1, force: CGFloat($0.pressure), azimuth: CGFloat($0.azimuth), altitude: CGFloat($0.altitude))
        }
        let path = PKStrokePath(controlPoints: controlPoints, creationDate: creationDate)
        return PKStroke(ink: PKInk(inkType, color: color(hex: stroke.color)), path: path)
    }

    static func hex(_ color: UIColor) -> String {
        let resolved = color.resolvedColor(with: UITraitCollection(userInterfaceStyle: .light))
        var red: CGFloat = 0, green: CGFloat = 0, blue: CGFloat = 0, alpha: CGFloat = 0
        resolved.getRed(&red, green: &green, blue: &blue, alpha: &alpha)
        return String(format: "#%02x%02x%02x", Int((red * 255).rounded()), Int((green * 255).rounded()), Int((blue * 255).rounded()))
    }

    static func color(hex: String) -> UIColor {
        let digits = hex.trimmingCharacters(in: CharacterSet(charactersIn: "#"))
        guard digits.count == 6, let value = UInt32(digits, radix: 16) else { return .black }
        return UIColor(red: CGFloat((value >> 16) & 0xff) / 255, green: CGFloat((value >> 8) & 0xff) / 255, blue: CGFloat(value & 0xff) / 255, alpha: 1)
    }
}
