import JuNotesCore
import SwiftUI

/// Saved, synced, backed up, always in plain words. Tapping it opens the server settings.
struct StatusLineView: View {
    @Environment(AppModel.self) private var model
    @State private var showSettings = false

    var body: some View {
        // Refreshes every half minute, so "vor 2 Minuten" never goes stale.
        TimelineView(.periodic(from: .now, by: 30)) { context in
            let line = StatusLine(model.snapshot, now: context.date)
            let text = StatusLineText(now: context.date)
            HStack(spacing: 16) {
                segment(text.saved(line.saved), level: line.savedLevel)
                segment(text.sync(line.sync), level: line.syncLevel)
                segment(text.backup(line.backup), level: line.backupLevel)
                Spacer(minLength: 0)
            }
            .font(.footnote)
            .padding(.horizontal, 16)
            .padding(.vertical, 8)
            .frame(maxWidth: .infinity)
            .background(.bar)
            .contentShape(Rectangle())
            .onTapGesture { showSettings = true }
            .accessibilityElement(children: .combine)
            .accessibilityAddTraits(.isButton)
        }
        .sheet(isPresented: $showSettings) { SettingsView() }
    }

    private func segment(_ title: String, level: Level) -> some View {
        Label {
            Text(title)
        } icon: {
            Image(systemName: symbol(level)).foregroundStyle(color(level))
        }
        .labelStyle(.titleAndIcon)
        .lineLimit(1)
    }

    private func symbol(_ level: Level) -> String {
        switch level {
        case .ok: "checkmark.circle.fill"
        case .waiting: "clock.fill"
        case .problem: "exclamationmark.triangle.fill"
        }
    }

    private func color(_ level: Level) -> Color {
        switch level {
        case .ok: .green
        case .waiting: .orange
        case .problem: .red
        }
    }
}
