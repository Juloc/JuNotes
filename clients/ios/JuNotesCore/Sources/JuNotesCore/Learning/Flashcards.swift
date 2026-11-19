import Foundation

public enum CardRating: Int, Sendable, CaseIterable {
    /// "Nochmal": I did not know it.
    case again = 0
    /// "Schwer": I knew it, with effort.
    case hard = 3
    /// "Gut"
    case good = 4
    /// "Leicht"
    case easy = 5
}

/// When a card comes back, worked out with the SM-2 method: right answers push it further out each time, wrong ones bring it back tomorrow.
public struct CardSchedule: Sendable, Codable, Equatable {
    public static let initialEase = 2.5
    public static let minimumEase = 1.3

    public var ease: Double
    /// Days until the next review after the last one.
    public var interval: Int
    public var repetitions: Int
    public var lapses: Int
    /// Nil for a card never reviewed: it is new.
    public var due: Date?

    public init(ease: Double = CardSchedule.initialEase, interval: Int = 0, repetitions: Int = 0, lapses: Int = 0, due: Date? = nil) {
        self.ease = ease
        self.interval = interval
        self.repetitions = repetitions
        self.lapses = lapses
        self.due = due
    }

    public var isNew: Bool { due == nil }

    /// The schedule after she rated the card at `now`. Days are calendar days, so "in 6 days" means that day's study session, not a moment.
    public func reviewed(_ rating: CardRating, now: Date, calendar: Calendar = .current) -> CardSchedule {
        var next = self
        let quality = Double(rating.rawValue)

        if rating == .again {
            next.repetitions = 0
            next.interval = 1
            next.lapses += 1
            // Seen again in this session, and a little less easy from now on.
            next.due = now
            next.ease = max(Self.minimumEase, ease - 0.2)
            return next
        }

        switch repetitions {
        case 0: next.interval = rating == .easy ? 4 : 1
        case 1: next.interval = rating == .easy ? 8 : 6
        default: next.interval = max(interval + 1, Int((Double(interval) * ease * (rating == .hard ? 0.85 : 1)).rounded()))
        }
        next.repetitions = repetitions + 1
        next.ease = max(Self.minimumEase, ease + 0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02))
        next.due = calendar.date(byAdding: .day, value: next.interval, to: calendar.startOfDay(for: now))
        return next
    }

    public func isDue(at now: Date, calendar: Calendar = .current) -> Bool {
        guard let due else { return false }
        return calendar.startOfDay(for: due) <= calendar.startOfDay(for: now) || due <= now
    }
}

/// A flashcard of a subject, made from her notes by the AI or by hand.
public struct Flashcard: SyncEntity {
    public static let kind = EntityKind.card

    public var id: UUID
    public var subjectID: UUID
    public var front: String
    public var back: String
    /// The page it came from, so a wrong answer can lead back to the notes.
    public var sourcePageID: UUID?
    public var schedule: CardSchedule

    public var parentID: UUID? { subjectID }

    public init(id: UUID = UUID(), subjectID: UUID, front: String, back: String, sourcePageID: UUID? = nil, schedule: CardSchedule = CardSchedule()) {
        self.id = id
        self.subjectID = subjectID
        self.front = front
        self.back = back
        self.sourcePageID = sourcePageID
        self.schedule = schedule
    }

    enum CodingKeys: String, CodingKey { case front, back, sourcePageID, schedule }

    public init(from decoder: any Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        id = UUID()
        subjectID = UUID()
        front = try values.decode(String.self, forKey: .front)
        back = try values.decode(String.self, forKey: .back)
        sourcePageID = try values.decodeIfPresent(UUID.self, forKey: .sourcePageID)
        schedule = try values.decodeIfPresent(CardSchedule.self, forKey: .schedule) ?? CardSchedule()
    }

    public static func decode(_ record: SyncRecord) throws -> Flashcard {
        guard record.kind == kind, !record.deleted, let payload = record.payload, let parent = record.parentID else { throw EntityCoding.Failure.notDecodable(record.kind) }
        var card: Flashcard = try EntityCoding.decode(payload)
        card.id = record.id
        card.subjectID = parent
        return card
    }

    public func rated(_ rating: CardRating, now: Date, calendar: Calendar = .current) -> Flashcard {
        var copy = self
        copy.schedule = schedule.reviewed(rating, now: now, calendar: calendar)
        return copy
    }
}

/// Which cards to learn today: what is due, oldest first, plus a few new ones so there is always something fresh but never a flood.
public enum StudyQueue {
    public static let newCardsPerDay = 10

    public static func today(_ cards: [Flashcard], now: Date, calendar: Calendar = .current, newLimit: Int = StudyQueue.newCardsPerDay) -> [Flashcard] {
        let due = cards.filter { $0.schedule.isDue(at: now, calendar: calendar) }.sorted { ($0.schedule.due ?? now) < ($1.schedule.due ?? now) }
        let fresh = cards.filter(\.schedule.isNew).prefix(max(0, newLimit))
        return due + fresh
    }

    /// How many are waiting in each group, for the badge and the start screen.
    public static func counts(_ cards: [Flashcard], now: Date, calendar: Calendar = .current) -> (due: Int, new: Int, total: Int) {
        (cards.filter { $0.schedule.isDue(at: now, calendar: calendar) }.count, cards.filter(\.schedule.isNew).count, cards.count)
    }
}
