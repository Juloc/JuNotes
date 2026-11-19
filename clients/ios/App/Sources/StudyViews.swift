import JuNotesCore
import SwiftUI

struct StudyTarget: Hashable {
    var subjectID: UUID?
}

/// The flashcards by subject, with what is due.
struct StudyHubView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        List {
            if model.cards.isEmpty {
                ContentUnavailableView("Noch keine Karteikarten", systemImage: "rectangle.on.rectangle.angled", description: Text("Öffne ein Heft und wähle „Lernen“, „Karteikarten“. Die KI macht Karten aus deinen Notizen."))
            } else {
                let all = StudyQueue.counts(model.cards, now: Date())
                NavigationLink(value: StudyTarget(subjectID: nil)) { row("Alle Fächer", due: all.due, new: all.new, total: all.total) }
                ForEach(model.subjects) { subject in
                    let cards = model.cards.filter { $0.subjectID == subject.id }
                    if !cards.isEmpty {
                        let counts = StudyQueue.counts(cards, now: Date())
                        NavigationLink(value: StudyTarget(subjectID: subject.id)) { row(subject.name, due: counts.due, new: counts.new, total: counts.total) }
                    }
                }
            }
        }
        .navigationTitle("Karteikarten")
        .navigationDestination(for: StudyTarget.self) { StudySessionView(target: $0) }
    }

    private func row(_ title: String, due: Int, new: Int, total: Int) -> some View {
        HStack {
            VStack(alignment: .leading) {
                Text(title).font(.headline)
                Text("\(total) Karten").font(.footnote).foregroundStyle(.secondary)
            }
            Spacer()
            if due + new > 0 { Text("\(due) fällig · \(new) neu").font(.footnote).foregroundStyle(Color.accentColor) } else { Text("Für heute fertig").font(.footnote).foregroundStyle(.secondary) }
        }
    }
}

/// One session: the cards that are due and a few new ones, each shown, uncovered and rated.
struct StudySessionView: View {
    let target: StudyTarget

    @Environment(AppModel.self) private var model
    @State private var queue: [Flashcard] = []
    @State private var started = false
    @State private var revealed = false
    @State private var learned = 0

    var body: some View {
        Group {
            if let card = queue.first {
                VStack(spacing: 24) {
                    Text("Noch \(queue.count)").font(.footnote).foregroundStyle(.secondary)
                    Text(card.front).font(.title).multilineTextAlignment(.center).padding()
                    if revealed {
                        Divider()
                        Text(card.back).font(.title3).multilineTextAlignment(.center).padding()
                        HStack(spacing: 12) {
                            rating("Nochmal", .again, tint: .red)
                            rating("Schwer", .hard, tint: .orange)
                            rating("Gut", .good, tint: .green)
                            rating("Leicht", .easy, tint: .blue)
                        }
                    } else {
                        Button("Antwort zeigen") { revealed = true }.buttonStyle(.borderedProminent).controlSize(.large)
                    }
                    Spacer()
                }
                .padding(24)
                .frame(maxWidth: 640)
            } else if started {
                ContentUnavailableView { Label("Für heute fertig", systemImage: "checkmark.circle") } description: { Text(learned == 0 ? "Heute ist nichts fällig." : "\(learned) Karten gelernt. Die nächsten kommen zur richtigen Zeit zurück.") }
            } else {
                ProgressView()
            }
        }
        .navigationTitle("Lernen")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            let cards = target.subjectID.map { id in model.cards.filter { $0.subjectID == id } } ?? model.cards
            queue = StudyQueue.today(cards, now: Date())
            started = true
        }
    }

    private func rating(_ title: String, _ value: CardRating, tint: Color) -> some View {
        Button(title) { Task { await rate(value) } }.buttonStyle(.borderedProminent).tint(tint)
    }

    private func rate(_ value: CardRating) async {
        guard let card = queue.first else { return }
        await model.rate(card, value)
        queue.removeFirst()
        learned += 1
        // A forgotten card comes round again before the session ends.
        if value == .again { queue.append(card.rated(.again, now: Date())) }
        revealed = false
    }
}
