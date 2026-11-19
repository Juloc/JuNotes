import SwiftUI
import UIKit

@main
struct JuNotesApp: App {
    @State private var model = AppModel()

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(model)
                .task { if Demo.isOn && Demo.wantsLandscape { DemoOrientation.landscape() } }
        }
    }
}

/// Only for the screenshots: the simulator starts upright, and the design is made for the landscape iPad.
@MainActor
enum DemoOrientation {
    static func landscape() {
        guard let scene = UIApplication.shared.connectedScenes.first as? UIWindowScene else { return }
        scene.requestGeometryUpdate(.iOS(interfaceOrientations: .landscape))
    }
}
