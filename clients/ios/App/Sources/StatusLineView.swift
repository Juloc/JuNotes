import JuNotesCore
import SwiftUI

/// Saved, synced, backed up, always in plain words. Phase 0 shows a sample state; Phase 1 feeds it from the database and the sync.
struct StatusLineView: View {
    @State private var snapshot = SyncSnapshot(lastSyncedAt: .now.addingTimeInterval(-120), lastBackupAt: .now.addingTimeInterval(-3 * 3600))

    var body: some View {
        let now = Date.now
        let line = StatusLine(snapshot, now: now)
        let text = StatusLineText(now: now)

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
        .accessibilityElement(children: .combine)
    }

    private func segment(_ title: String, level: Level) -> some View {
        Label {
            Text(title)
        } icon: {
            Image(systemName: symbol(level))
                .foregroundStyle(color(level))
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
