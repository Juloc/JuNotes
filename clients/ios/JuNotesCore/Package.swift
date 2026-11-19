// swift-tools-version: 6.4
import PackageDescription

// Pure Swift without UIKit, SwiftUI or PencilKit, so it builds and tests on Linux as well as in the app.
let package = Package(
    name: "JuNotesCore",
    platforms: [.iOS(.v27), .macOS(.v27)],
    products: [
        .library(name: "JuNotesCore", targets: ["JuNotesCore"]),
    ],
    targets: [
        .target(name: "JuNotesCore"),
        .testTarget(name: "JuNotesCoreTests", dependencies: ["JuNotesCore"]),
    ]
)
