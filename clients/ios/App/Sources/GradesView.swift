import JuNotesCore
import SwiftUI

/// Her grades by subject, with the average of each, of the main subjects and of all.
struct GradesView: View {
    enum Period: Hashable {
        case all, current, previous
    }

    @Environment(AppModel.self) private var model
    @State private var editing: GradeEditTarget?
    @State private var settingsFor: Subject?
    @State private var calculating: Subject?
    @State private var period = Period.all

    private var currentTerm: DateInterval { SchoolTerm.interval(containing: Date()) }

    private func grades(of subject: Subject) -> [Grade] {
        let all = model.grades(of: subject)
        switch period {
        case .all: return all
        case .current: return all.filter { currentTerm.contains($0.date) }
        case .previous:
            let previous = SchoolTerm.previous(of: currentTerm)
            return all.filter { previous.contains($0.date) }
        }
    }

    private func average(_ grades: [Grade], _ subject: Subject) -> String {
        subject.grading.scale.display(GradeCalculator.average(grades, settings: subject.grading), decimals: subject.grading.scale == .german ? subject.grading.averageDecimals : 1)
    }

    var body: some View {
        List {
            if model.subjects.isEmpty {
                ContentUnavailableView("Noch kein Fach", systemImage: "list.number", description: Text("Noten gehören zu einem Fach. Lege zuerst ein Fach an: in der Bibliothek über „Neu“, „Neues Fach“."))
            } else {
                Section {
                    Picker("Zeitraum", selection: $period) {
                        Text("Alles").tag(Period.all)
                        Text("Dieses Halbjahr").tag(Period.current)
                        Text("Letztes Halbjahr").tag(Period.previous)
                    }
                    .pickerStyle(.segmented)
                }
                summary
                ForEach(model.subjects) { subject in
                    subjectSection(subject)
                }
            }
        }
        .navigationTitle("Noten")
        .sheet(item: $editing) { GradeSheet(target: $0) }
        .sheet(item: $settingsFor) { GradingSettingsSheet(subject: $0) }
        .sheet(item: $calculating) { TargetSheet(subject: $0, grades: grades(of: $0)) }
    }

    private var summary: some View {
        let items = model.subjects.map { (grades: grades(of: $0), settings: $0.grading) }
        let hasMajor = model.subjects.contains { $0.grading.isMajor }
        return Section {
            row("Gesamtschnitt", GradeCalculator.overall(items), prominent: true)
            if hasMajor {
                row("Hauptfächer", GradeCalculator.overall(items, majorOnly: true), prominent: false)
                row("Übrige Fächer", GradeCalculator.overall(items, majorOnly: false), prominent: false)
            }
        } footer: {
            Text("Fächer mit Punkten zählen im Gesamtschnitt nicht mit, weil sie sich nicht mit Noten mischen lassen.")
        }
    }

    private func row(_ title: String, _ value: Double?, prominent: Bool) -> some View {
        HStack {
            Text(title).font(prominent ? .headline : .body)
            Spacer()
            Text(GradeCalculator.format(value, decimals: 2)).font(prominent ? .title2.weight(.semibold) : .title3).monospacedDigit()
        }
    }

    private func subjectSection(_ subject: Subject) -> some View {
        let list = grades(of: subject)
        return Section {
            ForEach(list) { grade in
                Button { editing = GradeEditTarget(subject: subject, grade: grade) } label: {
                    HStack {
                        VStack(alignment: .leading) {
                            Text(grade.title.isEmpty ? Self.name(grade.category) : grade.title).foregroundStyle(.primary)
                            Text(Self.name(grade.category) + " · " + grade.date.formatted(.dateTime.day().month()) + (grade.weight.map { " · zählt ×\(GradeCalculator.format($0, decimals: $0.rounded() == $0 ? 0 : 1))" } ?? ""))
                                .font(.footnote).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Text(subject.grading.scale.display(grade.value, decimals: subject.grading.decimals)).font(.title3.weight(.semibold)).monospacedDigit().foregroundStyle(.primary)
                    }
                }
                .swipeActions { Button("Löschen", systemImage: "trash", role: .destructive) { Task { await model.deleteGrade(grade) } } }
            }
            Button("Note eintragen", systemImage: "plus") { editing = GradeEditTarget(subject: subject, grade: nil) }
        } header: {
            HStack {
                Text(subject.name)
                if subject.grading.isMajor { Text("Hauptfach").font(.caption2.weight(.semibold)).padding(.horizontal, 6).padding(.vertical, 2).background(Brand.accent.opacity(0.15), in: Capsule()) }
                Spacer()
                if !list.isEmpty {
                    Text("Schnitt \(average(list, subject))").monospacedDigit()
                    Button("Ziel", systemImage: "target") { calculating = subject }.buttonStyle(.borderless).labelStyle(.iconOnly)
                }
                Button("Einstellungen", systemImage: "slider.horizontal.3") { settingsFor = subject }.buttonStyle(.borderless).labelStyle(.iconOnly)
            }
        } footer: {
            Text("Mündlich \(subject.grading.oralPercent) % · schriftlich \(subject.grading.writtenPercent) %")
        }
    }

    static func name(_ category: GradeCategory) -> String {
        switch category {
        case .written: "Klassenarbeit"
        case .oral: "Mündlich"
        case .test: "Test"
        case .other: "Referat, Praxis"
        }
    }
}

struct GradeEditTarget: Identifiable {
    let id = UUID()
    let subject: Subject
    let grade: Grade?
}

/// Adding or changing one grade. The way the value is typed follows the subject's scale.
struct GradeSheet: View {
    let target: GradeEditTarget

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var text: String
    @State private var choice: Double
    @State private var category: GradeCategory
    @State private var title: String
    @State private var date: Date
    @State private var customWeight: Bool
    @State private var weight: Double

    init(target: GradeEditTarget) {
        self.target = target
        let grade = target.grade
        let grading = target.subject.grading
        _text = State(initialValue: grade.map { GradeCalculator.format($0.value, decimals: grading.decimals) } ?? "")
        _choice = State(initialValue: grade?.value ?? (grading.scale == .points ? 10 : 2))
        _category = State(initialValue: grade?.category ?? .written)
        _title = State(initialValue: grade?.title ?? "")
        _date = State(initialValue: grade?.date ?? Date())
        _customWeight = State(initialValue: grade?.weight != nil)
        _weight = State(initialValue: grade?.weight ?? (grade?.category ?? .written).defaultWeight)
    }

    private var grading: GradingSettings { target.subject.grading }

    private var value: Double? {
        switch grading.scale {
        case .german:
            let cleaned = text.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: ".")
            return Double(cleaned).flatMap { grading.scale.normalized($0, decimals: grading.decimals) }
        case .tendency, .points:
            return choice
        }
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    if grading.scale == .german {
                        TextField(grading.decimals >= 2 ? "Zum Beispiel 2,37" : "Zum Beispiel 2,3", text: $text)
                            .keyboardType(.decimalPad)
                            .font(.title2.weight(.semibold))
                    } else {
                        Picker("Note", selection: $choice) {
                            ForEach(grading.scale.choices(), id: \.self) { Text(grading.scale.display($0)).tag($0) }
                        }
                        .pickerStyle(.wheel)
                        .frame(height: 120)
                    }
                } header: {
                    Text("Note")
                } footer: {
                    if grading.scale == .german, !text.isEmpty, value == nil { Text("Das ist keine Note zwischen 1 und 6.").foregroundStyle(.red) }
                }
                Section {
                    Picker("Art", selection: $category) {
                        ForEach(GradeCategory.allCases, id: \.self) { Text(GradesView.name($0)).tag($0) }
                    }
                    TextField("Titel, zum Beispiel Bruchrechnung", text: $title)
                    DatePicker("Datum", selection: $date, displayedComponents: .date)
                }
                Section {
                    Toggle("Zählt anders als üblich", isOn: $customWeight)
                    if customWeight {
                        Stepper("Zählt ×\(GradeCalculator.format(weight, decimals: weight.rounded() == weight ? 0 : 1))", value: $weight, in: 0.5...5, step: 0.5)
                    }
                } footer: {
                    Text("Üblich: eine Klassenarbeit zählt ×2, alles andere ×1 innerhalb seines Teils (schriftlich oder mündlich).")
                }
            }
            .navigationTitle(target.grade == nil ? "Note in \(target.subject.name)" : "Note ändern")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button(target.grade == nil ? "Eintragen" : "Sichern") { Task { await save() } }.disabled(value == nil)
                }
            }
        }
        .presentationDetents([.large])
    }

    private func save() async {
        guard let value else { return }
        let trimmed = title.trimmingCharacters(in: .whitespaces)
        let finalWeight = customWeight ? weight : nil
        if var grade = target.grade {
            grade.value = value
            grade.category = category
            grade.title = trimmed
            grade.date = date
            grade.weight = finalWeight
            await model.addGrade(grade)
        } else {
            await model.addGrade(Grade(subjectID: target.subject.id, value: value, category: category, weight: finalWeight, date: date, title: trimmed))
        }
        dismiss()
    }
}

/// How one subject is graded: scale, fineness, shares of oral and written, main subject, factor.
struct GradingSettingsSheet: View {
    let subject: Subject

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var settings: GradingSettings

    init(subject: Subject) {
        self.subject = subject
        _settings = State(initialValue: subject.grading)
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Picker("Notensystem", selection: $settings.scale) {
                        Text("1 bis 6").tag(GradeScale.german)
                        Text("1 bis 6 mit Tendenz (2+, 2−)").tag(GradeScale.tendency)
                        Text("Punkte 0 bis 15").tag(GradeScale.points)
                    }
                    if settings.scale == .german {
                        Picker("Genauigkeit", selection: $settings.decimals) {
                            Text("Zehntel, 1,1").tag(1)
                            Text("Hundertstel, 1,13").tag(2)
                        }
                    }
                } header: {
                    Text("Notensystem")
                }

                Section {
                    Stepper("Mündlich \(settings.oralPercent) %", value: $settings.oralPercent, in: 0...100, step: 5)
                    Text("Schriftlich \(settings.writtenPercent) %").foregroundStyle(.secondary)
                } header: {
                    Text("Gewichtung")
                } footer: {
                    Text("Schriftlich sind Klassenarbeiten und Tests, mündlich sind Mitarbeit, Referate und Praxisnoten. Fehlt ein Teil, zählt der andere allein.")
                }

                Section {
                    Toggle("Hauptfach (maßgebendes Fach)", isOn: $settings.isMajor)
                    Stepper("Zählt im Gesamtschnitt ×\(GradeCalculator.format(settings.factor, decimals: settings.factor.rounded() == settings.factor ? 0 : 1))", value: $settings.factor, in: 0...5, step: 0.5)
                } footer: {
                    Text("Hauptfächer bekommen einen eigenen Schnitt. Mit dem Faktor zählt ein Fach mehr oder weniger.")
                }

                Section("Anzeige") {
                    Stepper("Schnitt mit \(settings.averageDecimals) Nachkommastellen", value: $settings.averageDecimals, in: 0...3)
                }
            }
            .navigationTitle("Noten in \(subject.name)")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") {
                        Task {
                            var changed = subject
                            changed.grading = GradingSettings(scale: settings.scale, decimals: settings.decimals, oralPercent: settings.oralPercent, isMajor: settings.isMajor, factor: settings.factor, averageDecimals: settings.averageDecimals)
                            await model.update(changed)
                            dismiss()
                        }
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
    let grades: [Grade]

    @Environment(\.dismiss) private var dismiss
    @State private var target = 2.0
    @State private var category = GradeCategory.written

    var body: some View {
        NavigationStack {
            Form {
                Section("Mein Ziel für \(subject.name)") {
                    Stepper("Schnitt \(GradeCalculator.format(target))", value: $target, in: subject.grading.scale.range, step: subject.grading.scale == .points ? 1 : 0.1)
                    Picker("Nächste Note ist", selection: $category) {
                        ForEach(GradeCategory.allCases, id: \.self) { Text(GradesView.name($0)).tag($0) }
                    }
                }
                Section("Ergebnis") { Text(answer).font(.headline) }
            }
            .navigationTitle("Was brauche ich?")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Fertig") { dismiss() } } }
            .onAppear { target = subject.grading.scale == .points ? 12 : 2 }
        }
        .presentationDetents([.medium])
    }

    private var answer: String {
        let scale = subject.grading.scale
        switch GradeCalculator.needed(forTarget: target, grades: grades, next: category, settings: subject.grading) {
        case .achievable(let value): return "Du brauchst mindestens eine \(scale.display(value, decimals: subject.grading.decimals))."
        case .alreadySafe: return "Du schaffst das Ziel sicher, egal wie die nächste Note wird."
        case .impossible: return "Mit der nächsten Note allein ist das Ziel nicht mehr zu schaffen."
        case .noData: return "Trag erst eine Note ein."
        }
    }
}
