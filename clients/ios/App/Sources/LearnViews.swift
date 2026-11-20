import JuNotesCore
import SwiftUI

enum LearnKind: String, Identifiable {
    case quiz, test, cards, check, copy

    var id: String { rawValue }

    var title: String {
        switch self {
        case .quiz: "Abfragen"
        case .test: "Probetest"
        case .cards: "Karteikarten"
        case .check: "Ergänzen und Prüfen"
        case .copy: "Abschreib-Modus"
        }
    }

    var symbol: String {
        switch self {
        case .quiz: "questionmark.bubble"
        case .test: "doc.text"
        case .cards: "rectangle.on.rectangle.angled"
        case .check: "text.badge.checkmark"
        case .copy: "pencil.and.list.clipboard"
        }
    }

    var explanation: String {
        switch self {
        case .quiz: "Fragen zu deinen Notizen. Du antwortest, dann siehst du die Lösung."
        case .test: "Ein Probetest wie eine Klassenarbeit, als eigenes Heft mit Lösungen zum Aufdecken."
        case .cards: "Karteikarten aus deinen Notizen, die dir in der richtigen Zeit wieder vorgelegt werden."
        case .check: "Die KI sucht Lücken, Fehler und Unklares. Du entscheidest, was du übernimmst."
        case .copy: "Der Text erscheint kurz, dann schreibst du ihn aus dem Gedächtnis. Danach siehst du, was fehlt."
        }
    }
}

struct LearnRequest: Identifiable {
    let id = UUID()
    var kind: LearnKind
    var notebook: Notebook
    var pageID: UUID?
}

/// The "Lernen" menu of a notebook.
struct LearnMenu: View {
    let notebook: Notebook
    let pageID: UUID?
    @Environment(AppModel.self) private var model

    var body: some View {
        Menu {
            ForEach([LearnKind.quiz, .test, .cards, .check, .copy]) { kind in
                Button(kind.title, systemImage: kind.symbol) { model.learnRequest = LearnRequest(kind: kind, notebook: notebook, pageID: pageID) }
            }
        } label: {
            Label("Lernen", systemImage: "sparkles")
        }
    }
}

/// Runs one kind of help on a page or a notebook and shows its result.
struct LearnSheet: View {
    let request: LearnRequest

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var wholeNotebook = false
    @State private var material = LearningMaterial(text: "")
    @State private var phase = Phase.choose

    enum Phase {
        case choose
        case working
        case failed(String)
        case quiz([QuizQuestion])
        case cards([CardDraft])
        case check([CheckNote])
        case test(TestResult)
        case copy
    }

    var body: some View {
        NavigationStack {
            Group {
                switch phase {
                case .choose: chooser
                case .working:
                    VStack(spacing: 16) { ProgressView().controlSize(.large); Text("Die KI arbeitet … das kann eine Minute dauern.").foregroundStyle(.secondary) }
                case .failed(let text):
                    ContentUnavailableView { Label("Das hat nicht geklappt", systemImage: "exclamationmark.triangle") } description: { Text(text) } actions: {
                        Button("Zurück") { phase = .choose }
                    }
                case .quiz(let questions): QuizRunner(questions: questions, subject: material.subjectName, onDone: { dismiss() })
                case .cards(let drafts): CardPicker(drafts: drafts, notebook: request.notebook, pageID: request.pageID, onDone: { dismiss() })
                case .check(let notes): CheckList(notes: notes, pageID: request.pageID, onDone: { dismiss() })
                case .test(let result): TestPreview(result: result, notebook: request.notebook, onDone: { dismiss() })
                case .copy: CopyModeView(original: material.text, onDone: { dismiss() })
                }
            }
            .navigationTitle(request.kind.title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Schließen") { dismiss() } } }
            .task { await loadMaterial() }
            .onChange(of: wholeNotebook) { Task { await loadMaterial() } }
        }
        .presentationDetents([.large])
    }

    private var chooser: some View {
        Form {
            Section { Label(request.kind.explanation, systemImage: request.kind.symbol) }
            if request.pageID != nil {
                Section("Stoff") {
                    Picker("Stoff", selection: $wholeNotebook) {
                        Text("Diese Seite").tag(false)
                        Text("Ganzes Heft").tag(true)
                    }
                    .pickerStyle(.segmented)
                }
            }
            Section {
                if material.isEmpty {
                    Text("Hier wurde noch keine Schrift erkannt. Schreibe etwas auf die Seite und warte einen Moment, dann wird sie gelesen.").font(.footnote).foregroundStyle(.secondary)
                } else {
                    Text("\(material.text.split(whereSeparator: \.isWhitespace).count) Wörter Stoff").font(.footnote).foregroundStyle(.secondary)
                }
                Button("Los") { Task { await start() } }.disabled(material.isEmpty)
            }
        }
    }

    private func loadMaterial() async {
        material = await model.material(notebook: request.notebook, pageID: wholeNotebook ? nil : request.pageID)
    }

    private func start() async {
        if request.kind == .copy { phase = .copy; return }
        guard let transport = model.makeTransport() else { phase = .failed("Der Server ist noch nicht eingerichtet. Trage Adresse und Schlüssel in den Einstellungen ein."); return }
        phase = .working
        let body = AIRequest(text: material.text, count: nil, durationMinutes: nil, subject: material.subjectName)
        do {
            switch request.kind {
            case .quiz: phase = .quiz(try await transport.ask(.quiz, body, as: QuizResult.self).questions)
            case .cards: phase = .cards(try await transport.ask(.cards, body, as: CardsResult.self).cards)
            case .check: phase = .check(try await transport.ask(.check, body, as: CheckResult.self).notes)
            case .test: phase = .test(try await transport.ask(.test, body, as: TestResult.self))
            case .copy: break
            }
        } catch {
            phase = .failed(message(for: error))
        }
    }
}

// MARK: Quiz

struct QuizRunner: View {
    let questions: [QuizQuestion]
    let subject: String?
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var index = 0
    @State private var answer = ""
    @State private var revealed = false
    @State private var feedback: GradedAnswer?
    @State private var checking = false
    @State private var known = 0

    var body: some View {
        if index >= questions.count {
            ContentUnavailableView {
                Label("Geschafft", systemImage: "checkmark.circle")
            } description: {
                Text("\(known) von \(questions.count) wusstest du.")
            } actions: {
                Button("Fertig", action: onDone).buttonStyle(.borderedProminent)
            }
        } else {
            let question = questions[index]
            Form {
                Section("Frage \(index + 1) von \(questions.count)") { Text(question.question).font(.title3) }
                if let hint = question.hint, !revealed { Section("Hinweis") { Text(hint).foregroundStyle(.secondary) } }
                Section("Deine Antwort") {
                    TextField("Schreibe oder diktiere deine Antwort", text: $answer, axis: .vertical).lineLimit(2...6)
                }
                if revealed {
                    Section("Richtig wäre") { Text(question.answer) }
                    if let feedback {
                        Section("Rückmeldung der KI") { Text(feedback.feedback); Text(feedback.correct ? "Richtig" : "Noch nicht ganz").foregroundStyle(feedback.correct ? .green : .orange) }
                    }
                    Section("Wusstest du es?") {
                        HStack {
                            Button("Nein") { next(knew: false) }.buttonStyle(.bordered)
                            Button("Fast") { next(knew: false) }.buttonStyle(.bordered)
                            Button("Ja") { next(knew: true) }.buttonStyle(.borderedProminent)
                        }
                    }
                } else {
                    Section {
                        Button("Lösung zeigen") { revealed = true }
                        if !answer.trimmingCharacters(in: .whitespaces).isEmpty {
                            Button { Task { await check(question) } } label: { HStack { Text("Von der KI prüfen lassen"); if checking { Spacer(); ProgressView() } } }.disabled(checking)
                        }
                    }
                }
            }
        }
    }

    private func check(_ question: QuizQuestion) async {
        guard let transport = model.makeTransport() else { return }
        checking = true
        defer { checking = false }
        feedback = try? await transport.ask(.grade, AIRequest(subject: subject, question: question.question, expected: question.answer, answer: answer), as: GradedAnswer.self)
        revealed = true
    }

    private func next(knew: Bool) {
        if knew { known += 1 }
        index += 1
        answer = ""
        revealed = false
        feedback = nil
    }
}

// MARK: Cards

struct CardPicker: View {
    let drafts: [CardDraft]
    let notebook: Notebook
    let pageID: UUID?
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var chosen: Set<Int> = []

    var body: some View {
        if model.subject(of: notebook) == nil {
            ContentUnavailableView("Dieses Heft liegt in keinem Fach", systemImage: "rectangle.on.rectangle.angled", description: Text("Karteikarten gehören zu einem Fach. Verschiebe das Heft in einen Ordner, der ein Fach ist."))
        } else {
            List {
                Section("Welche Karten willst du behalten?") {
                    ForEach(drafts.indices, id: \.self) { index in
                        Button { toggle(index) } label: {
                            HStack(alignment: .top) {
                                Image(systemName: chosen.contains(index) ? "checkmark.circle.fill" : "circle").foregroundStyle(Color.accentColor)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(drafts[index].front).foregroundStyle(.primary)
                                    Text(drafts[index].back).font(.footnote).foregroundStyle(.secondary)
                                }
                            }
                        }
                    }
                }
                Section {
                    Button("\(chosen.count) Karten speichern") {
                        Task {
                            if let subject = model.subject(of: notebook) { await model.saveCards(chosen.sorted().map { drafts[$0] }, subjectID: subject.id, sourcePageID: pageID) }
                            onDone()
                        }
                    }
                    .disabled(chosen.isEmpty)
                }
            }
            .onAppear { chosen = Set(drafts.indices) }
        }
    }

    private func toggle(_ index: Int) {
        if chosen.contains(index) { chosen.remove(index) } else { chosen.insert(index) }
    }
}

// MARK: Check

struct CheckList: View {
    let notes: [CheckNote]
    let pageID: UUID?
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var handled: Set<Int> = []

    var body: some View {
        if notes.isEmpty {
            ContentUnavailableView {
                Label("Alles in Ordnung", systemImage: "checkmark.seal")
            } description: {
                Text("Die KI hat nichts gefunden, was fehlt oder falsch ist.")
            } actions: {
                Button("Fertig", action: onDone).buttonStyle(.borderedProminent)
            }
        } else {
            List {
                ForEach(notes.indices, id: \.self) { index in
                    if !handled.contains(index) {
                        VStack(alignment: .leading, spacing: 8) {
                            Label(Self.title(notes[index].kind), systemImage: Self.symbol(notes[index].kind)).font(.subheadline.weight(.semibold)).foregroundStyle(Self.color(notes[index].kind))
                            if let quote = notes[index].quote { Text("„\(quote)“").font(.footnote).italic().foregroundStyle(.secondary) }
                            Text(notes[index].text)
                            if let suggestion = notes[index].suggestion {
                                Text(suggestion).padding(8).frame(maxWidth: .infinity, alignment: .leading).background(.green.opacity(0.12), in: RoundedRectangle(cornerRadius: 8))
                            }
                            HStack {
                                if pageID != nil, let suggestion = notes[index].suggestion {
                                    Button("Übernehmen") { Task { if let pageID { await model.addAddition(suggestion, to: pageID) }; handled.insert(index) } }.buttonStyle(.borderedProminent)
                                }
                                Button("Weg damit") { handled.insert(index) }.buttonStyle(.bordered)
                            }
                        }
                        .padding(.vertical, 4)
                    }
                }
                if handled.count == notes.count { Button("Fertig", action: onDone) }
            }
        }
    }

    static func title(_ kind: String) -> String { kind == "missing" ? "Fehlt" : kind == "error" ? "Fehler" : "Unklar" }
    static func symbol(_ kind: String) -> String { kind == "missing" ? "plus.circle" : kind == "error" ? "xmark.circle" : "questionmark.circle" }
    static func color(_ kind: String) -> Color { kind == "missing" ? .blue : kind == "error" ? .red : .orange }
}

// MARK: Test

struct TestPreview: View {
    let result: TestResult
    let notebook: Notebook
    let onDone: () -> Void

    @Environment(AppModel.self) private var model
    @State private var creating = false

    var body: some View {
        List {
            Section {
                Text(result.title).font(.headline)
                Text("\(result.tasks.count) Aufgaben · \(result.totalPoints) Punkte")
            }
            Section("Aufgaben") {
                ForEach(result.tasks.indices, id: \.self) { index in
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Aufgabe \(index + 1) · \(result.tasks[index].points) P.").font(.footnote).foregroundStyle(.secondary)
                        Text(result.tasks[index].question)
                    }
                }
            }
            Section {
                Button { Task { creating = true; let created = await model.createTestNotebook(result, subjectID: notebook.subjectID); await model.open(notebookID: created.id, pageID: nil); onDone() } } label: {
                    HStack { Text("Als Heft anlegen"); if creating { Spacer(); ProgressView() } }
                }
                .disabled(creating)
            } footer: {
                Text("Die Aufgaben stehen gedruckt auf den Seiten, die Lösungen kannst du dort aufdecken.")
            }
        }
    }
}
