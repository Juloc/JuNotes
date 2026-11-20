import JuNotesCore
import SwiftUI

/// A plan she kept for one exam, with what she has ticked off.
struct StoredPlan: Codable, Identifiable, Equatable {
    var id: String
    var examTitle: String
    var examDate: Date
    var plan: StudyPlan
    var done: Set<String> = []
}

extension StudyTask {
    var title: String {
        switch kind {
        case .learn: "Lernen: \(topic ?? "")"
        case .practice: "Üben: \(topic ?? "")"
        case .review: "Wiederholen: \(topic ?? "")"
        case .mockTest: "Probetest schreiben"
        case .lastLook: "Letzter Blick auf alles"
        }
    }
}

/// The plans live in a file on the device, next to the timetable cache.
@MainActor
@Observable
final class StudyPlanBook {
    private(set) var plans: [StoredPlan] = []
    private let url: URL

    init(directory: URL) {
        url = directory.appendingPathComponent("plans.json")
        if let data = try? Data(contentsOf: url), let stored = try? JSONDecoder().decode([StoredPlan].self, from: data) { plans = stored }
    }

    func save(_ plan: StoredPlan) {
        plans.removeAll { $0.id == plan.id }
        plans.append(plan)
        persist()
    }

    func remove(_ id: String) {
        plans.removeAll { $0.id == id }
        persist()
    }

    func toggle(planID: String, task key: String) {
        guard let index = plans.firstIndex(where: { $0.id == planID }) else { return }
        if plans[index].done.contains(key) { plans[index].done.remove(key) } else { plans[index].done.insert(key) }
        persist()
    }

    /// Plans of exams that have passed are dropped.
    func dropFinished(now: Date) {
        let before = plans.count
        plans.removeAll { $0.examDate < Calendar.current.startOfDay(for: now) }
        if plans.count != before { persist() }
    }

    private func persist() {
        if let data = try? JSONEncoder().encode(plans) { try? data.write(to: url, options: .atomic) }
    }
}

/// "Lernplan erstellen" for an exam: which topics, how much time per day, then the plan.
struct StudyPlanSheet: View {
    let exam: Exam

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var topics: [(title: String, minutes: Int, on: Bool)] = []
    @State private var newTopic = ""
    @State private var minutesPerDay = 45
    @State private var preview: StudyPlan?

    var body: some View {
        NavigationStack {
            Form {
                Section("Was kommt dran?") {
                    ForEach(topics.indices, id: \.self) { index in
                        Toggle(isOn: $topics[index].on) {
                            VStack(alignment: .leading) {
                                Text(topics[index].title)
                                Stepper("\(topics[index].minutes) Minuten zum Lernen", value: $topics[index].minutes, in: 10...90, step: 5).font(.footnote)
                            }
                        }
                    }
                    HStack {
                        TextField("Thema hinzufügen", text: $newTopic)
                        Button("Hinzufügen") { topics.append((newTopic.trimmingCharacters(in: .whitespaces), 25, true)); newTopic = "" }.disabled(newTopic.trimmingCharacters(in: .whitespaces).isEmpty)
                    }
                }
                Section("Zeit") { Stepper("\(minutesPerDay) Minuten pro Tag", value: $minutesPerDay, in: 20...180, step: 5) }
                Section { Button("Plan erstellen") { preview = StudyPlanner.plan(topics: chosen, examDate: exam.date, today: Date(), minutesPerDay: minutesPerDay) } }

                if let preview {
                    Section("Dein Plan bis \(exam.date.formatted(.dateTime.weekday(.abbreviated).day().month()))") {
                        if preview.days.isEmpty { Text("Die Arbeit ist heute oder schon vorbei.").foregroundStyle(.secondary) }
                        ForEach(preview.days.indices, id: \.self) { index in
                            let day = preview.days[index]
                            if !day.tasks.isEmpty {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(day.date.formatted(.dateTime.weekday(.wide).day().month())).font(.subheadline.weight(.semibold))
                                    ForEach(day.tasks.indices, id: \.self) { task in Text("\(day.tasks[task].title) · \(day.tasks[task].minutes) Min.").font(.footnote) }
                                }
                            }
                        }
                        if !preview.unscheduled.isEmpty {
                            Label("\(preview.unscheduled.count) Aufgaben passen nicht mehr in die Zeit. Nimm mehr Minuten pro Tag oder streiche ein Thema.", systemImage: "exclamationmark.triangle").font(.footnote).foregroundStyle(.orange)
                        }
                        Button("Plan behalten") {
                            model.plans.save(StoredPlan(id: exam.id, examTitle: exam.title, examDate: exam.date, plan: preview))
                            dismiss()
                        }
                        .disabled(preview.days.isEmpty)
                    }
                }
            }
            .navigationTitle("Lernplan")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Schließen") { dismiss() } } }
            .task {
                guard topics.isEmpty, let subject = exam.subject else { return }
                var notebooks: [Notebook] = []
                for id in [subject.id] + Array(model.tree.descendantIDs(of: subject.id)) {
                    notebooks += (try? await model.store.notebooks(subjectID: id)) ?? []
                }
                topics = notebooks.filter { $0.type == .pages }.map { ($0.title, 25, true) }
            }
        }
        .presentationDetents([.large])
    }

    private var chosen: [StudyTopic] { topics.filter(\.on).map { StudyTopic(title: $0.title, minutes: $0.minutes) } }
}
