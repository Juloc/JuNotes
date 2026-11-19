import Foundation

public struct PageState: Sendable, Equatable, Identifiable {
    public var id: UUID
    public var isEmpty: Bool
    /// Created by the app while she kept writing, not by her.
    public var wasAutoCreated: Bool

    public init(id: UUID = UUID(), isEmpty: Bool, wasAutoCreated: Bool) {
        self.id = id
        self.isEmpty = isEmpty
        self.wasAutoCreated = wasAutoCreated
    }
}

/// Continuous notebooks: writing near the bottom of the last page appends a page, and empty auto pages disappear again on close.
public enum AutoPages {
    /// Share of the page height after which the next page is added.
    public static let threshold = 0.85

    public static func needsNewPage(writingAtY y: Double, pageHeight: Double, isLastPage: Bool) -> Bool {
        guard isLastPage, pageHeight > 0 else { return false }
        return y / pageHeight >= threshold
    }

    /// Drops trailing empty pages the app made itself. A page she added on purpose stays, and one page always remains.
    public static func trimmed(_ pages: [PageState]) -> [PageState] {
        var result = pages
        while result.count > 1, let last = result.last, last.isEmpty, last.wasAutoCreated {
            result.removeLast()
        }
        return result
    }

    /// "3 von 7" for the position display; pages count from one.
    public static func position(of index: Int, in count: Int) -> (current: Int, total: Int)? {
        guard count > 0, (0..<count).contains(index) else { return nil }
        return (index + 1, count)
    }
}
