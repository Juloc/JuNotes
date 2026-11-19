import JuNotesCore
import SwiftUI

/// Her grades by subject, with the average of each and of all.
struct GradesView: View {
    @Environment(AppModel.self) private var model
    @State private var adding: Subject?
    @State private var calculating: Subject?

    var body: some View {
        List {
            if model.subjects.isEmpty {
                ContentUnavailableView("Noch kein Fach", systemImage: "list.number", description: Text("Noten gehören zu einem Fach. Lege zuerst ein Fach an."))
            } else {
                Section {
                    HStack {
                        Text("Gesamtschnitt").font(.headline)
                        Spacer()
                        Text(GradeCalculator.format(GradeCalculator.overall(model.subjects.map { model.grades(of: $0) }))).font(.title2.weight(.semibold)).monospacedDigit()
                    }
                }
                ForEach(model.subjects) { subject in
                    Section {
                        ForEach(model.grades(of: subject)) { grade in
                            HStack {
                                VStack(alignment: .leading) {
                                    Text(grade.title.isEmpty ? Self.name(grade.category) : grade.title)
                                    Text(Self.name(grade.category) + " · " + grade.date.formatted(.dateTime.day().month()) + (grade.weight.map { " · zählt ×\(GradeCalculator.format($0, decimals: 0))" } ?? ""))
                                        .font(.footnote).foregroundStyle(.secondary)
                                }
                                Spacer()
                                Text(GradeCalculator.format(grade.value)).font(.title3.weight(.semibold)).monospacedDigit()
                            }
                            .swipeActions { Button("Löschen", systemImage: "trash", role: .destructive) { Task { await model.deleteGrade(grade) } } }
                        }
                        Button("Note eintragen", systemImage: "plus") { adding = subject }
                    } header: {
                        HStack {
                            Text(subject.name)
                            Spacer()
                            let grades = model.grades(of: subject)
                            if !grades.isEmpty {
                                Text("Schnitt \(GradeCalculator.format(GradeCalculator.average(grades)))").monospacedDigit()
                                Button("Ziel", systemImage: "target") { calculating = subject }.buttonStyle(.borderless).labelStyle(.iconOnly)
                            }
                        }
                    }
                }
            }
        }
        .navigationTitle("Noten")
        .sheet(item: $adding) { NewGradeSheet(subject: $0) }
        .sheet(item: $calculating) { TargetSheet(subject: $0) }
    }

    static func name(_ category: GradeCategory) -> String {
        switch category {
        case .written: "Klassenarbeit"
        case .oral: "Mündlich"
        case .test: "Test"
        case .other: "Sonstiges"
        }
    }
}

struct NewGradeSheet: View {
    let subject: Subject
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var value = 2.0
    @State private var category = GradeCategory.written
    @State private var title = ""
    @State private var date = Date()
    @State private var heavier = false

    private static let values: [Double] = [1.0, 1.3, 1.7, 2.0, 2.3, 2.7, 3.0, 3.3, 3.7, 4.0, 4.3, 4.7, 5.0, 5.3, 5.7, 6.0]

    var body: some View {
        NavigationStack {
            Form {
                Section("Note") {
                    Picker("Note", selection: $value) {
                        ForEach(Self.values, id: \.self) { Text(GradeCalculator.format($0)).tag($0) }
                    }
                    .pickerStyle(.wheel)
                    .frame(height: 120)
                }
                Section {
                    Picker("Art", selection: $category) {
                        ForEach(GradeCategory.allCases, id: \.self) { Text(GradesView.name($0)).tag($0) }
                    }
                    TextField("Titel, zum Beispiel Bruchrechnung", text: $title)
                    DatePicker("Datum", selection: $date, displayedComponents: .date)
                    Toggle("Zählt doppelt", isOn: $heavier)
                }
            }
            .navigationTitle("Note in \(subject.name)")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Eintragen") {
                        let base = GradeWeights.standard.weight(for: category)
                        let grade = Grade(subjectID: subject.id, value: value, category: category, weight: heavier ? base * 2 : nil, date: date, title: title.trimmingCharacters(in: .whitespaces))
                        Task { await model.addGrade(grade); dismiss() }
                    }
                }
            }
        }
        .presentationDetents([.large])
    }
}

/// "Was brauche ich?": the grade needed in the next piece of work to end at a target average.
struct TargetSheet: View {
    let subject: Subject
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var target = 2.0
    @State private var category = GradeCategory.written

    var body: some View {
        NavigationStack {
            Form {
                Section("Mein Ziel für \(subject.name)") {
                    Stepper("Schnitt \(GradeCalculator.format(target))", value: $target, in: 1...6, step: 0.1)
                    Picker("Nächste Note ist", selection: $category) {
                        ForEach(GradeCategory.allCases, id: \.self) { Text(GradesView.name($0)).tag($0) }
                    }
                }
                Section("Ergebnis") { Text(answer).font(.headline) }
            }
            .navigationTitle("Was brauche ich?")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Fertig") { dismiss() } } }
        }
        .presentationDetents([.medium])
    }

    private var answer: String {
        let need = GradeCalculator.needed(forTarget: target, grades: model.grades(of: subject), nextWeight: GradeWeights.standard.weight(for: category), scale: .german)
        switch need {
        case .achievable(let value): return "Du brauchst mindestens eine \(GradeCalculator.format(value))."
        case .alreadySafe: return "Du schaffst das Ziel sicher, egal wie die nächste Note wird."
        case .impossible: return "Mit der nächsten Note allein ist das Ziel nicht mehr zu schaffen."
        case .noData: return "Trag erst eine Note ein."
        }
    }
}
