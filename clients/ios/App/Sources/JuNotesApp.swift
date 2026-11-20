import SwiftUI

@main
struct JuNotesApp: App {
    @State private var model = AppModel()

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(model)
                .tint(Brand.accent)
        }
    }
}
