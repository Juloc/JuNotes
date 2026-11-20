import SwiftUI

@main
struct JuNotesApp: App {
    @State private var model = AppModel()

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(model)
                .tint(Brand.accent)
                .task {
                    guard !Demo.isOn else { return }
                    if await Notifier.authorization() == .notDetermined { await Notifier.requestPermission() }
                    await model.refreshReminders()
                    Notifier.scheduleBackgroundRefresh()
                }
        }
        .backgroundTask(.appRefresh(Notifier.refreshTask)) { await model.backgroundRefresh() }
    }
}
