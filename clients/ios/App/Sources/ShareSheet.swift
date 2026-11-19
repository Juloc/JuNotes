import SwiftUI
import UIKit

/// A file prepared for sharing.
struct ShareItem: Identifiable {
    let id = UUID()
    let url: URL
}

/// The system's share sheet: save to Files, print, send by mail or message.
struct ShareSheet: UIViewControllerRepresentable {
    let url: URL

    func makeUIViewController(context: Context) -> UIActivityViewController {
        UIActivityViewController(activityItems: [url], applicationActivities: nil)
    }

    func updateUIViewController(_ controller: UIActivityViewController, context: Context) {}
}
