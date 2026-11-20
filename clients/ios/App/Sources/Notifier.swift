import BackgroundTasks
import JuNotesCore
import UserNotifications

/// Local notifications: the iPad shows them itself, planned from the timetable, the study plan and the cards.
/// Without a paid developer account there is no push from a server, so the app plans ahead and looks again whenever it gets the chance.
@MainActor
enum Notifier {
    static let refreshTask = "app.junotes.refresh"
    private static let prefix = "junotes."
    private static let settingsKey = "reminderSettings"
    private static let seenNewsKey = "seenNewsKeys"

    static var settings: ReminderSettings {
        get { UserDefaults.standard.data(forKey: settingsKey).flatMap { try? JSONDecoder().decode(ReminderSettings.self, from: $0) } ?? ReminderSettings() }
        set { UserDefaults.standard.set(try? JSONEncoder().encode(newValue), forKey: settingsKey) }
    }

    /// Asks once; returns whether notifications are allowed.
    @discardableResult
    static func requestPermission() async -> Bool {
        (try? await UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound, .badge])) ?? false
    }

    static func authorization() async -> UNAuthorizationStatus {
        await UNUserNotificationCenter.current().notificationSettings().authorizationStatus
    }

    /// Replaces everything planned so far with the reminders for now.
    static func schedule(_ reminders: [Reminder]) async {
        let center = UNUserNotificationCenter.current()
        guard await authorization() == .authorized else { return }
        let pending = await center.pendingNotificationRequests().map(\.identifier).filter { $0.hasPrefix(prefix) }
        center.removePendingNotificationRequests(withIdentifiers: pending)
        // iOS keeps at most 64 pending notifications; the nearest ones matter.
        for reminder in reminders.prefix(60) {
            let content = UNMutableNotificationContent()
            content.title = reminder.title
            content.body = reminder.body
            content.sound = .default
            let parts = Calendar.current.dateComponents([.year, .month, .day, .hour, .minute, .second], from: reminder.fire)
            let trigger = UNCalendarNotificationTrigger(dateMatching: parts, repeats: false)
            try? await center.add(UNNotificationRequest(identifier: prefix + reminder.id, content: content, trigger: trigger))
        }
    }

    /// Tells her about school news that is new since the last look. The first look only learns what is there.
    static func announceNews(_ items: [SchoolNews]) async {
        let known = (UserDefaults.standard.array(forKey: seenNewsKey) as? [String]).map(Set.init)
        UserDefaults.standard.set(Array(Set(items.map(\.key)).union(known ?? [])), forKey: seenNewsKey)
        guard settings.news, await authorization() == .authorized else { return }
        for item in ReminderPlanner.unseenNews(items, seen: known).prefix(3) {
            let content = UNMutableNotificationContent()
            content.title = "Neu von der Schule"
            content.body = item.title
            content.sound = .default
            try? await UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: prefix + "news-" + item.key, content: content, trigger: UNTimeIntervalNotificationTrigger(timeInterval: 2, repeats: false)))
        }
    }

    /// Asks iOS to wake the app now and then, for a look at the timetable and the news.
    static func scheduleBackgroundRefresh() {
        let request = BGAppRefreshTaskRequest(identifier: refreshTask)
        request.earliestBeginDate = Date(timeIntervalSinceNow: 3 * 3600)
        try? BGTaskScheduler.shared.submit(request)
    }
}

extension AppModel {
    var reminderSettings: ReminderSettings {
        get { Notifier.settings }
        set { Notifier.settings = newValue }
    }

    /// Plans the notifications again from what is known now.
    func refreshReminders() async {
        let settings = Notifier.settings
        guard settings.any else {
            await Notifier.schedule([])
            return
        }
        let calendar = Calendar.current
        var open: [Date: Int] = [:]
        for stored in plans.plans {
            for (index, day) in stored.plan.days.enumerated() {
                let remaining = day.tasks.indices.filter { !stored.done.contains("\(index)-\($0)") }.count
                open[calendar.startOfDay(for: day.date), default: 0] += remaining
            }
        }
        let due = StudyQueue.counts(cards, now: Date()).due
        let reminders = ReminderPlanner.plan(now: Date(), timetable: Timetable(events: events), settings: settings, openStudyTasks: open, dueCards: due)
        await Notifier.schedule(reminders)
        await Notifier.announceNews(news)
    }

    /// What the app does when iOS wakes it in the background: fetch, then plan again.
    func backgroundRefresh() async {
        await syncNow()
        await refreshReminders()
        Notifier.scheduleBackgroundRefresh()
    }
}
