import SwiftUI
import UIKit

@main
struct JuNotesApp: App {
    @State private var model = AppModel()

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(model)
                .task {
                    guard Demo.isOn && Demo.wantsLandscape else { return }
                    // The scene is not ready the instant the app starts.
                    try? await Task.sleep(for: .seconds(1.5))
                    DemoOrientation.landscape()
                }
        }
    }
}

/// Only for the screenshots: the simulator starts upright, and the design is made for the landscape iPad.
@MainActor
enum DemoOrientation {
    static func landscape() {
        guard let scene = UIApplication.shared.connectedScenes.first as? UIWindowScene else { NSLog("JuNotes demo: no window scene"); return }
        scene.requestGeometryUpdate(.iOS(interfaceOrientations: .landscape)) { error in NSLog("JuNotes demo: landscape refused: \(error)") }
        // The window controller has to agree to the new orientation.
        scene.keyWindow?.rootViewController?.setNeedsUpdateOfSupportedInterfaceOrientations()
    }
}
