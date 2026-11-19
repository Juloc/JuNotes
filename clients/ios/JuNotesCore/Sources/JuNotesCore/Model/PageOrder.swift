import Foundation

/// Fractional keys that order pages: a page can go between two others without renumbering anything, so two devices
/// that move different pages never disagree about the rest.
public enum PageOrder {
    private static let digits = Array("0123456789abcdefghijklmnopqrstuvwxyz")

    /// A key that sorts after `lower` and before `upper`; either may be nil for the start or end of the notebook.
    /// If the neighbours are equal or the wrong way round (two devices picked the same key, or a sync reordered them), the new key simply goes after `lower`.
    public static func key(after lower: String?, before upper: String?) -> String {
        guard lower != nil || upper != nil else { return "i" }
        var upper = upper
        if let low = lower, let high = upper, low >= high { upper = nil }
        return midpoint(Array(lower ?? ""), upper.map(Array.init))
    }

    /// Keys for `count` pages appended after `last`, in order.
    public static func keys(appending count: Int, after last: String?) -> [String] {
        var keys: [String] = []
        var previous = last
        for _ in 0..<count {
            let next = key(after: previous, before: nil)
            keys.append(next)
            previous = next
        }
        return keys
    }

    private static func index(_ character: Character) -> Int { digits.firstIndex(of: character) ?? 0 }

    private static func midpoint(_ lower: [Character], _ upper: [Character]?) -> String {
        if let upper {
            // Shared prefix stays; only what differs needs a middle.
            var shared = 0
            while shared < upper.count, (shared < lower.count ? lower[shared] : digits[0]) == upper[shared] { shared += 1 }
            if shared > 0 {
                return String(upper[0..<shared]) + midpoint(Array(lower.dropFirst(shared)), Array(upper.dropFirst(shared)))
            }
        }

        let low = lower.first.map(index) ?? 0
        let high = upper?.first.map(index) ?? digits.count
        if high - low > 1 {
            return String(digits[(low + high) / 2])
        }
        if let upper, upper.count > 1 {
            // The upper key's first digit alone already sorts between the two.
            return String(upper[0])
        }
        return String(digits[low]) + midpoint(Array(lower.dropFirst()), nil)
    }
}

/// The size of a page in its own units, independent of the screen. Strokes are stored in these units, so a page looks the same on any iPad.
public enum PageGeometry {
    public static let width = 595.0
    public static let height = 842.0
    public static let whiteboardWidth = 3000.0
    public static let whiteboardHeight = 2000.0

    public static func size(for kind: NotebookKind) -> (width: Double, height: Double) {
        kind == .whiteboard ? (whiteboardWidth, whiteboardHeight) : (width, height)
    }
}
