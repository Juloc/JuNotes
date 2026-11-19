import Foundation

public enum StudyTaskKind: String, Sendable, Codable, Equatable {
    /// Read and rewrite the topic.
    case learn
    /// Quiz or flashcards on a topic, a day or more after learning it.
    case practice
    /// A short second look a few days later.
    case review
    /// A mock test under exam conditions.
    case mockTest
    /// Light repetition the day before.
    case lastLook
}

public struct StudyTopic: Sendable, Equatable, Codable {
    public var title: String
    /// Minutes she needs to work through it the first time.
    public var minutes: Int

    public init(title: String, minutes: Int = 25) {
        self.title = title
        self.minutes = minutes
    }
}

public struct StudyTask: Sendable, Equatable, Codable {
    public var kind: StudyTaskKind
    public var topic: String?
    public var minutes: Int

    public init(kind: StudyTaskKind, topic: String? = nil, minutes: Int) {
        self.kind = kind
        self.topic = topic
        self.minutes = minutes
    }
}

public struct StudyDay: Sendable, Equatable, Codable {
    public var date: Date
    public var tasks: [StudyTask]

    public var minutes: Int { tasks.reduce(0) { $0 + $1.minutes } }
}

public struct StudyPlan: Sendable, Equatable, Codable {
    public var days: [StudyDay]
    /// What did not fit in the time before the exam; shown so she knows rather than finding out too late.
    public var unscheduled: [StudyTask]

    public var totalMinutes: Int { days.reduce(0) { $0 + $1.minutes } }
}

/// A plan from today to the day before an exam: learn each topic early, practise it the next day, look again a few days later, a mock test two days
/// before and a light last look the evening before. Nothing is put on a day past what she said she has time for.
public enum StudyPlanner {
    public static let practiceMinutes = 10
    public static let reviewMinutes = 8
    public static let lastLookMinutes = 20

    public static func plan(topics: [StudyTopic], examDate: Date, today: Date, minutesPerDay: Int, calendar: Calendar = .current) -> StudyPlan {
        let start = calendar.startOfDay(for: today)
        let examDay = calendar.startOfDay(for: examDate)
        let available = max(0, calendar.dateComponents([.day], from: start, to: examDay).day ?? 0)

        // The days she can study: today up to the day before the exam. An exam today or in the past leaves none.
        var days = (0..<available).compactMap { offset in calendar.date(byAdding: .day, value: offset, to: start).map { StudyDay(date: $0, tasks: []) } }
        let capacity = max(10, minutesPerDay)
        var unscheduled: [StudyTask] = []

        func place(_ task: StudyTask, from first: Int, through last: Int? = nil) -> Int? {
            guard !days.isEmpty else { return nil }
            let end = min(last ?? days.count - 1, days.count - 1)
            guard first <= end else { return nil }
            for index in first...end where days[index].minutes + task.minutes <= capacity {
                days[index].tasks.append(task)
                return index
            }
            return nil
        }

        // The last look sits on the final day, the mock test two days before it, when there is room for them.
        let lastLook = StudyTask(kind: .lastLook, minutes: lastLookMinutes)
        let mockMinutes = min(capacity, 40)
        let mock = StudyTask(kind: .mockTest, minutes: mockMinutes)
        if !days.isEmpty { if place(lastLook, from: days.count - 1) == nil { unscheduled.append(lastLook) } }
        if days.count >= 4 { if place(mock, from: days.count - 3, through: days.count - 2) == nil { unscheduled.append(mock) } }

        let lastLearnDay = max(0, days.count - (days.count >= 4 ? 4 : 1))
        for topic in topics {
            let learn = StudyTask(kind: .learn, topic: topic.title, minutes: min(max(5, topic.minutes), capacity))
            guard let learnDay = place(learn, from: 0, through: lastLearnDay) ?? place(learn, from: 0) else {
                unscheduled.append(learn)
                continue
            }

            let practice = StudyTask(kind: .practice, topic: topic.title, minutes: practiceMinutes)
            guard let practiceDay = place(practice, from: learnDay + 1) ?? (days.count > learnDay ? place(practice, from: learnDay) : nil) else {
                unscheduled.append(practice)
                continue
            }

            let review = StudyTask(kind: .review, topic: topic.title, minutes: reviewMinutes)
            if place(review, from: practiceDay + 2) == nil { unscheduled.append(review) }
        }

        for index in days.indices { days[index].tasks.sort { order($0.kind) < order($1.kind) } }
        return StudyPlan(days: days, unscheduled: unscheduled)
    }

    private static func order(_ kind: StudyTaskKind) -> Int {
        switch kind {
        case .learn: 0
        case .practice: 1
        case .review: 2
        case .mockTest: 3
        case .lastLook: 4
        }
    }
}
