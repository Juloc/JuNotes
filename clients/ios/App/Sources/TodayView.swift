import JuNotesCore
import SwiftUI

/// "Heute": what is on now and next, what is cancelled, which exams are coming and what the school writes.
struct TodayView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        TimelineView(.periodic(from: .now, by: 60)) { context in
            let cockpit = model.cockpit(now: context.date)
            ScrollView {
                VStack(alignment: .leading, spacing: 24) {
                    if model.events.isEmpty {
                        ContentUnavailableView {
                            Label("Noch kein Stundenplan", systemImage: "calendar")
                        } description: {
                            Text("Der Server holt den Stundenplan, sobald der Link dafür eingetragen ist. Danach erscheint er hier, auch ohne Netz.")
                        }
                    } else {
                        nowCard(cockpit)
                        lessons(cockpit)
                        exams(cockpit)
                    }
                    news
                }
                .padding(24)
                .frame(maxWidth: 760, alignment: .leading)
                .frame(maxWidth: .infinity)
            }
            .navigationTitle(context.date.formatted(.dateTime.weekday(.wide).day().month(.wide)))
            .refreshable { await model.syncNow() }
        }
    }

    // MARK: Now

    @ViewBuilder private func nowCard(_ cockpit: Cockpit) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            switch cockpit.reason {
            case .running(let event):
                Text("Jetzt").font(.footnote).foregroundStyle(.secondary)
                Text(event.summary).font(.title2.weight(.semibold))
                Text("bis \(event.end.formatted(.dateTime.hour().minute()))" + (event.location.map { " · \($0)" } ?? "")).foregroundStyle(.secondary)
            case .startingSoon(let event):
                Text("Gleich").font(.footnote).foregroundStyle(.secondary)
                Text(event.summary).font(.title2.weight(.semibold))
                Text("ab \(event.start.formatted(.dateTime.hour().minute()))" + (event.location.map { " · \($0)" } ?? "")).foregroundStyle(.secondary)
            case nil:
                if let next = cockpit.nextLesson {
                    Text("Nächste Stunde").font(.footnote).foregroundStyle(.secondary)
                    Text(next.summary).font(.title2.weight(.semibold))
                    Text(next.start.formatted(.dateTime.weekday(.wide).hour().minute())).foregroundStyle(.secondary)
                } else {
                    Text("Heute keine Stunde mehr").font(.title3.weight(.semibold))
                }
            }

            if let subject = cockpit.suggestedSubject {
                Button { Task { await model.startPageForNow() } } label: {
                    Label("Neue Seite für \(subject.name)", systemImage: "plus.circle.fill")
                }
                .buttonStyle(.borderedProminent)
            } else if cockpit.reason != nil {
                Text("Zu dieser Stunde gibt es noch kein Fach. Lege es an, dann hilft dir dieser Knopf.").font(.footnote).foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(18)
        .background(.background.secondary, in: RoundedRectangle(cornerRadius: 16))
    }

    // MARK: Lessons

    @ViewBuilder private func lessons(_ cockpit: Cockpit) -> some View {
        let all = cockpit.todaysLessons
        let special = cockpit.todaysAllDayEvents
        if !all.isEmpty || !special.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                Text("Heute").font(.headline)
                ForEach(special) { event in
                    Label(event.summary, systemImage: "flag").foregroundStyle(.secondary)
                }
                ForEach(all) { event in
                    HStack {
                        Text(event.start.formatted(.dateTime.hour().minute())).monospacedDigit().foregroundStyle(.secondary).frame(width: 56, alignment: .leading)
                        Text(event.summary).strikethrough(event.cancelled)
                        if event.cancelled { Text("entfällt").font(.footnote).foregroundStyle(.red) }
                        Spacer()
                        if let location = event.location { Text(location).font(.footnote).foregroundStyle(.secondary) }
                    }
                    .fontWeight(cockpit.currentLesson?.id == event.id ? .semibold : .regular)
                }
            }
        }
    }

    // MARK: Exams

    @ViewBuilder private func exams(_ cockpit: Cockpit) -> some View {
        let upcoming = cockpit.exams()
        if !upcoming.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                Text("Arbeiten und Tests").font(.headline)
                ForEach(upcoming) { exam in
                    HStack {
                        Image(systemName: "exclamationmark.circle").foregroundStyle(.orange)
                        VStack(alignment: .leading) {
                            Text(exam.title)
                            Text(daysUntil(exam.date, now: cockpit.now)).font(.footnote).foregroundStyle(.secondary)
                        }
                    }
                }
            }
        }
    }

    private func daysUntil(_ date: Date, now: Date) -> String {
        let calendar = Calendar.current
        let days = calendar.dateComponents([.day], from: calendar.startOfDay(for: now), to: calendar.startOfDay(for: date)).day ?? 0
        let label = days <= 0 ? "heute" : days == 1 ? "morgen" : "in \(days) Tagen"
        return label + ", " + date.formatted(.dateTime.weekday(.abbreviated).day().month())
    }

    // MARK: News

    @ViewBuilder private var news: some View {
        if !model.news.isEmpty {
            VStack(alignment: .leading, spacing: 12) {
                Text("Von der Schule").font(.headline)
                ForEach(model.news.prefix(5)) { item in
                    VStack(alignment: .leading, spacing: 2) {
                        if let link = item.link { Link(item.title, destination: link).font(.subheadline.weight(.semibold)) } else { Text(item.title).font(.subheadline.weight(.semibold)) }
                        if let summary = item.summary { Text(summary).font(.footnote).foregroundStyle(.secondary).lineLimit(2) }
                    }
                }
            }
        }
    }
}
