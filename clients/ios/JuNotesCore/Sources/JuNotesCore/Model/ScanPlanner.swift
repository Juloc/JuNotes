import Foundation

/// Where a scan can go. After scanning the app asks once, with the likeliest answer already chosen.
public enum ScanDestination: Sendable, Equatable {
    /// After the page she is on, in the notebook she has open.
    case insertHere(Notebook)
    /// At the end of the newest notebook of the subject the timetable says she is in.
    case newestNotebook(Subject, Notebook)
    /// As a notebook of its own, a worksheet, in that subject (or in none).
    case newWorksheet(Subject?)
    /// She picks the subject and notebook herself.
    case choose
}

public struct ScanContext: Sendable {
    public var openNotebook: Notebook?
    public var suggestedSubject: Subject?
    /// The notebooks of the suggested subject, oldest first.
    public var notebooksOfSuggestedSubject: [Notebook]

    public init(openNotebook: Notebook? = nil, suggestedSubject: Subject? = nil, notebooksOfSuggestedSubject: [Notebook] = []) {
        self.openNotebook = openNotebook
        self.suggestedSubject = suggestedSubject
        self.notebooksOfSuggestedSubject = notebooksOfSuggestedSubject
    }
}

public enum ScanPlanner {
    /// The choices in the order they are shown; the first is the preselected one. "Choose" is always last.
    public static func options(_ context: ScanContext) -> [ScanDestination] {
        var options: [ScanDestination] = []

        if let open = context.openNotebook, open.type == .pages { options.append(.insertHere(open)) }

        if let subject = context.suggestedSubject {
            if let newest = context.notebooksOfSuggestedSubject.last(where: { $0.type == .pages && $0.trashedAt == nil }), newest.id != context.openNotebook?.id {
                options.append(.newestNotebook(subject, newest))
            }
            options.append(.newWorksheet(subject))
        } else if context.openNotebook == nil {
            options.append(.newWorksheet(nil))
        }

        options.append(.choose)
        return options
    }

    public static func defaultOption(_ context: ScanContext) -> ScanDestination { options(context)[0] }

    /// The title of the worksheet notebook a scan creates: "Arbeitsblatt 19.11."
    public static func worksheetTitle(date: Date, calendar: Calendar = .current) -> String {
        let parts = calendar.dateComponents([.day, .month], from: date)
        return "Arbeitsblatt \(parts.day ?? 1).\(parts.month ?? 1)."
    }

    /// Sort keys for `count` scanned pages that go after `after` and before `before`, in order.
    public static func sortKeys(count: Int, after: String?, before: String?) -> [String] {
        var keys: [String] = []
        var previous = after
        for _ in 0..<count {
            let key = PageOrder.key(after: previous, before: before)
            keys.append(key)
            previous = key
        }
        return keys
    }
}
