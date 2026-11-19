import JuNotesCore
import SwiftUI

struct RootView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.scenePhase) private var scenePhase
    @State private var showNewSubject = false
    @State private var showSettings = false

    var body: some View {
        @Bindable var model = model
        NavigationSplitView {
            List(selection: Binding(get: { model.sidebar }, set: { item in Task { await model.select(item) } })) {
                Section {
                    Label("Heute", systemImage: "sun.max").tag(SidebarItem.today)
                }
                Section("Fächer") {
                    ForEach(model.subjects) { subject in
                        Label { Text(subject.name) } icon: {
                            Image(systemName: subject.symbol).foregroundStyle(Color(uiColor: StrokeConversion.color(hex: subject.color)))
                        }
                        .tag(SidebarItem.subject(subject.id))
                    }
                }
                Section {
                    Label("Noten", systemImage: "list.number").tag(SidebarItem.grades)
                    Label("Papierkorb", systemImage: "trash").tag(SidebarItem.trash)
                }
            }
            .navigationTitle("JuNotes")
            .toolbar {
                ToolbarItem(placement: .primaryAction) { Button("Neues Fach", systemImage: "plus") { showNewSubject = true } }
                ToolbarItem(placement: .bottomBar) { Button("Einstellungen", systemImage: "gearshape") { showSettings = true } }
            }
        } detail: {
            switch model.sidebar {
            case .today: NavigationStack { TodayView() }
            case .grades: NavigationStack { GradesView() }
            case .trash: NavigationStack { TrashView() }
            default: NotebookListView()
            }
        }
        // The status line is always visible: what is saved, synced and backed up.
        .safeAreaInset(edge: .bottom, spacing: 0) { StatusLineView() }
        .task {
            await model.purgeTrash()
            await model.reload()
            await model.syncNow()
        }
        .onChange(of: scenePhase) { _, phase in
            if phase == .active { Task { await model.syncNow() } }
        }
        .sheet(isPresented: $showNewSubject) { NewSubjectSheet() }
        .sheet(isPresented: $showSettings) { SettingsView() }
    }
}
