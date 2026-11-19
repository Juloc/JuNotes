import Foundation

/// Nothing is gone at once: deleting moves a notebook or page to the trash, and it stays there for thirty days.
public enum Trash {
    public static let retention: TimeInterval = 30 * 24 * 3600

    public static func isTrashed(_ trashedAt: Date?) -> Bool { trashedAt != nil }

    /// The day it will be deleted for good, for a line such as "wird am 12.12. endgültig gelöscht".
    public static func purgeDate(trashedAt: Date) -> Date { trashedAt.addingTimeInterval(retention) }

    public static func isPurgeable(trashedAt: Date?, now: Date) -> Bool {
        guard let trashedAt else { return false }
        return now >= purgeDate(trashedAt: trashedAt)
    }

    /// Whole days left before it is gone, never below zero.
    public static func daysLeft(trashedAt: Date, now: Date) -> Int {
        let left = purgeDate(trashedAt: trashedAt).timeIntervalSince(now)
        return max(0, Int((left / 86400).rounded(.up)))
    }
}

extension Notebook {
    public func movedToTrash(at date: Date) -> Notebook {
        var copy = self
        copy.trashedAt = date
        return copy
    }

    public func restored() -> Notebook {
        var copy = self
        copy.trashedAt = nil
        return copy
    }
}

extension Page {
    public func movedToTrash(at date: Date) -> Page {
        var copy = self
        copy.trashedAt = date
        return copy
    }

    public func restored() -> Page {
        var copy = self
        copy.trashedAt = nil
        return copy
    }
}

/// The line "Neue Seite für jetzt" puts at the top of a page.
public enum PageHeading {
    private static let weekdays = ["So", "Mo", "Di", "Mi", "Do", "Fr", "Sa"]

    /// "Do 19.11. · Mathe"
    public static func make(date: Date, subject: String, calendar: Calendar = .current) -> String {
        let parts = calendar.dateComponents([.weekday, .day, .month], from: date)
        let weekday = weekdays[((parts.weekday ?? 1) - 1) % 7]
        return "\(weekday) \(parts.day ?? 1).\(parts.month ?? 1). · \(subject)"
    }
}
