import JuNotesCore
import SwiftUI

/// "Heute": what is on now and next, the week, exams, the cards that are due and the notebooks she opened last.
/// Cards on the grouped background, two columns on a wide iPad and one on a narrow one.
struct TodayView: View {
    @Environment(AppModel.self) private var model
    @State private var planning: Exam?
    @State private var width: CGFloat = 1000
    @State private var recent: [Notebook] = []

    var body: some View {
        TimelineView(.periodic(from: .now, by: 60)) { context in
            let cockpit = model.cockpit(now: context.date)
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    if model.events.isEmpty {
                        ContentUnavailableView {
                            Label("Noch kein Stundenplan", systemImage: "calendar")
                        } description: {
                            Text("Der Server holt den Stundenplan, sobald der Link dafür eingetragen ist. Danach erscheint er hier, auch ohne Netz.")
                        }
                        cardsCard
                        recentCard
                    } else if width > 820 {
                        HStack(alignment: .top, spacing: 16) {
                            VStack(spacing: 16) {
                                hero(cockpit, now: context.date)
                                lessonsCard(cockpit)
                                studyTodayCard(cockpit.now)
                                recentCard
                            }
                            VStack(spacing: 16) {
                                weekCard(cockpit)
                                examsCard(cockpit)
                                cardsCard
                            }
                            .frame(maxWidth: 400)
                        }
                    } else {
                        hero(cockpit, now: context.date)
                        weekCard(cockpit)
                        lessonsCard(cockpit)
                        examsCard(cockpit)
                        studyTodayCard(cockpit.now)
                        cardsCard
                        recentCard
                    }
                    newsCard
                }
                .padding(24)
            }
            .background(Color(.systemGroupedBackground))
            .navigationTitle(context.date.formatted(.dateTime.weekday(.wide).day().month(.wide)))
            .refreshable { await model.syncNow() }
            .sheet(item: $planning) { StudyPlanSheet(exam: $0) }
            .task { model.plans.dropFinished(now: Date()) }
            .toolbar { ToolbarItem(placement: .primaryAction) { Button("Scannen", systemImage: "doc.viewfinder") { model.scannerOrigin = ScanOrigin() } } }
        }
        .onGeometryChange(for: CGFloat.self, of: { $0.size.width }) { width = $0 }
        .task(id: model.subjects.count) { await loadRecent() }
    }

    private func loadRecent() async {
        let all = (try? await model.store.allNotebooks()) ?? []
        recent = Array(all.filter { $0.trashedAt == nil }.sorted { $0.createdAt > $1.createdAt }.prefix(4))
    }

    // MARK: Now

    @ViewBuilder private func hero(_ cockpit: Cockpit, now: Date) -> some View {
        let running: CalendarEvent? = { if case .running(let event) = cockpit.reason { event } else { nil } }()
        HStack(alignment: .center, spacing: 20) {
            VStack(alignment: .leading, spacing: 10) {
                switch cockpit.reason {
                case .running(let event):
                    CardTitle("Jetzt", symbol: "clock")
                    Text(event.summary).font(.title.weight(.bold))
                    Text("bis \(event.end.formatted(.dateTime.hour().minute()))" + (event.location.map { " · \($0)" } ?? "")).foregroundStyle(.secondary)
                case .startingSoon(let event):
                    CardTitle("Gleich", symbol: "clock")
                    Text(event.summary).font(.title.weight(.bold))
                    Text("ab \(event.start.formatted(.dateTime.hour().minute()))" + (event.location.map { " · \($0)" } ?? "")).foregroundStyle(.secondary)
                case nil:
                    if let next = cockpit.nextLesson {
                        CardTitle("Nächste Stunde", symbol: "clock")
                        Text(next.summary).font(.title.weight(.bold))
                        Text(next.start.formatted(.dateTime.weekday(.wide).hour().minute())).foregroundStyle(.secondary)
                    } else {
                        CardTitle("Heute", symbol: "checkmark.circle")
                        Text("Heute keine Stunde mehr").font(.title2.weight(.bold))
                    }
                }

                if let subject = cockpit.suggestedSubject {
                    HStack {
                        Button { Task { await model.startPageForNow() } } label: {
                            Label("Neue Seite für \(subject.name)", systemImage: "plus")
                        }
                        .buttonStyle(.borderedProminent)
                        Button { Task { await model.select(.subject(subject.id)) } } label: {
                            Label("Fach öffnen", systemImage: "folder")
                        }
                        .buttonStyle(.bordered)
                    }
                    .padding(.top, 4)
                } else if cockpit.reason != nil {
                    Text("Zu dieser Stunde gibt es noch kein Fach. Lege es an, dann hilft dir dieser Knopf.").font(.footnote).foregroundStyle(.secondary)
                }
            }
            Spacer(minLength: 0)
            if let running {
                let total = running.end.timeIntervalSince(running.start)
                let left = max(0, running.end.timeIntervalSince(now))
                ProgressRing(progress: total > 0 ? 1 - left / total : 0, value: String(Int((left / 60).rounded(.up))), caption: "Min.")
            }
        }
        .padding(20)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(
            LinearGradient(colors: [Brand.accent.opacity(0.20), Brand.accent.opacity(0.07)], startPoint: .topLeading, endPoint: .bottomTrailing),
            in: RoundedRectangle(cornerRadius: 22, style: .continuous)
        )
    }

    // MARK: Week

    @ViewBuilder private func weekCard(_ cockpit: Cockpit) -> some View {
        let calendar = Calendar.current
        let start = calendar.dateInterval(of: .weekOfYear, for: cockpit.now)?.start ?? cockpit.now
        let days = (0..<7).compactMap { calendar.date(byAdding: .day, value: $0, to: start) }
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                CardTitle("Diese Woche", symbol: "calendar")
                Spacer()
                Text("KW \(calendar.component(.weekOfYear, from: cockpit.now))").font(.footnote).foregroundStyle(Brand.accent)
            }
            HStack(spacing: 4) {
                ForEach(days, id: \.self) { day in
                    let isToday = calendar.isDate(day, inSameDayAs: cockpit.now)
                    let hasExam = cockpit.timetable.events.contains { calendar.isDate($0.start, inSameDayAs: day) && cockpit.timetable.isExam($0) }
                    VStack(spacing: 6) {
                        Text(day.formatted(.dateTime.weekday(.abbreviated))).font(.caption).foregroundStyle(.secondary)
                        Text(day.formatted(.dateTime.day()))
                            .font(.body.weight(.semibold))
                            .frame(width: 36, height: 36)
                            .background(isToday ? Brand.accent : Color.clear, in: Circle())
                            .foregroundStyle(isToday ? Color.white : Color.primary)
                        Circle().fill(hasExam ? Color.orange : Color.clear).frame(width: 6, height: 6)
                    }
                    .frame(maxWidth: .infinity)
                }
            }
            if let exam = cockpit.exams().first {
                HStack(spacing: 8) {
                    Circle().fill(Color.orange).frame(width: 7, height: 7)
                    Text("\(exam.title) \(daysUntil(exam.date, now: cockpit.now, short: true))").font(.footnote).foregroundStyle(.secondary)
                }
            }
        }
        .card()
    }

    // MARK: Lessons

    @ViewBuilder private func lessonsCard(_ cockpit: Cockpit) -> some View {
        let all = cockpit.todaysLessons
        let special = cockpit.todaysAllDayEvents
        if !all.isEmpty || !special.isEmpty {
            VStack(alignment: .leading, spacing: 10) {
                CardTitle("Stundenplan heute", symbol: "list.bullet.rectangle")
                ForEach(special) { event in
                    Label(event.summary, systemImage: "flag").foregroundStyle(.secondary)
                }
                ForEach(all) { event in
                    let current = cockpit.currentLesson?.id == event.id
                    HStack {
                        Text(event.start.formatted(.dateTime.hour().minute())).monospacedDigit().foregroundStyle(.secondary).lineLimit(1).fixedSize().frame(minWidth: 56, alignment: .leading)
                        Text(event.summary).strikethrough(event.cancelled).fontWeight(current ? .semibold : .regular)
                        if event.cancelled { Text("entfällt").font(.caption.weight(.medium)).foregroundStyle(.red).padding(.horizontal, 7).padding(.vertical, 2).background(Color.red.opacity(0.12), in: Capsule()) }
                        Spacer()
                        if let location = event.location { Text(location).font(.footnote).foregroundStyle(.secondary) }
                    }
                    .padding(.horizontal, 10).padding(.vertical, 9)
                    .background(current ? Brand.accent.opacity(0.14) : Color(.tertiarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                }
            }
            .card()
        }
    }

    // MARK: Study plan

    @ViewBuilder private func studyTodayCard(_ now: Date) -> some View {
        let calendar = Calendar.current
        let items: [(plan: StoredPlan, dayIndex: Int, day: StudyDay)] = model.plans.plans.compactMap { plan in
            plan.plan.days.enumerated().first { calendar.isDate($0.element.date, inSameDayAs: now) && !$0.element.tasks.isEmpty }.map { (plan, $0.offset, $0.element) }
        }
        if !items.isEmpty {
            VStack(alignment: .leading, spacing: 10) {
                CardTitle("Lernen für die Arbeit", symbol: "graduationcap")
                ForEach(items, id: \.plan.id) { item in
                    Text("\(item.plan.examTitle) · heute \(item.day.minutes) Min.").font(.footnote).foregroundStyle(.secondary)
                    ForEach(item.day.tasks.indices, id: \.self) { index in
                        let key = "\(item.dayIndex)-\(index)"
                        let done = item.plan.done.contains(key)
                        Button { model.plans.toggle(planID: item.plan.id, task: key) } label: {
                            HStack {
                                Image(systemName: done ? "checkmark.circle.fill" : "circle").foregroundStyle(done ? Color.green : Color.secondary)
                                Text(item.day.tasks[index].title).strikethrough(done).foregroundStyle(done ? Color.secondary : Color.primary)
                                Spacer()
                                Text("\(item.day.tasks[index].minutes) Min.").font(.footnote).foregroundStyle(.secondary)
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            .card()
        }
    }

    // MARK: Exams

    @ViewBuilder private func examsCard(_ cockpit: Cockpit) -> some View {
        let upcoming = cockpit.exams()
        if !upcoming.isEmpty {
            VStack(alignment: .leading, spacing: 14) {
                CardTitle("Arbeiten und Tests", symbol: "flag")
                ForEach(upcoming) { exam in
                    let stored = model.plans.plans.first { $0.id == exam.id }
                    HStack(alignment: .center, spacing: 12) {
                        VStack(spacing: 0) {
                            Text(exam.date.formatted(.dateTime.day())).font(.title3.weight(.bold))
                            Text(exam.date.formatted(.dateTime.month(.abbreviated))).font(.caption2.weight(.semibold)).foregroundStyle(.secondary)
                        }
                        .frame(width: 52, height: 52)
                        .background(Color.orange.opacity(0.16), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                        VStack(alignment: .leading, spacing: 4) {
                            Text(exam.title).fontWeight(.medium)
                            Text(daysUntil(exam.date, now: cockpit.now, short: false)).font(.footnote).foregroundStyle(.secondary)
                            if let stored {
                                let total = stored.plan.days.reduce(0) { $0 + $1.tasks.count }
                                ProgressView(value: Double(stored.done.count), total: Double(max(1, total))).tint(Brand.accent)
                            }
                        }
                        Spacer(minLength: 8)
                        Button(stored == nil ? "Plan erstellen" : "Lernplan") { planning = exam }
                            .buttonStyle(.bordered)
                            .font(.footnote)
                    }
                }
            }
            .card()
        }
    }

    private func daysUntil(_ date: Date, now: Date, short: Bool) -> String {
        let calendar = Calendar.current
        let days = calendar.dateComponents([.day], from: calendar.startOfDay(for: now), to: calendar.startOfDay(for: date)).day ?? 0
        let label = days <= 0 ? "heute" : days == 1 ? "morgen" : "in \(days) Tagen"
        return short ? label : label + ", " + date.formatted(.dateTime.weekday(.abbreviated).day().month())
    }

    // MARK: Cards

    @ViewBuilder private var cardsCard: some View {
        let counts = StudyQueue.counts(model.cards, now: Date())
        VStack(alignment: .leading, spacing: 12) {
            CardTitle("Karteikarten", symbol: "rectangle.on.rectangle.angled")
            if model.cards.isEmpty {
                Text("Noch keine Karten. Öffne ein Heft und wähle „Lernen“, „Karteikarten“.").font(.footnote).foregroundStyle(.secondary)
            } else {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text("\(counts.due)").font(.system(size: 44, weight: .bold, design: .rounded)).foregroundStyle(Brand.accent)
                    VStack(alignment: .leading) {
                        Text(counts.due == 1 ? "Karte fällig" : "Karten fällig").fontWeight(.medium)
                        Text("dazu \(min(counts.new, StudyQueue.newCardsPerDay)) neue").font(.footnote).foregroundStyle(.secondary)
                    }
                }
                Button { Task { await model.select(.cards) } } label: {
                    Text(counts.due + counts.new > 0 ? "Jetzt lernen" : "Alle Karten ansehen").frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
            }
        }
        .card()
    }

    // MARK: Recent

    @ViewBuilder private var recentCard: some View {
        if !recent.isEmpty {
            VStack(alignment: .leading, spacing: 14) {
                CardTitle("Zuletzt geöffnet", symbol: "clock.arrow.circlepath")
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 120), spacing: 16)], alignment: .leading, spacing: 16) {
                    ForEach(recent) { notebook in
                        Button { Task { await model.open(notebookID: notebook.id, pageID: nil) } } label: {
                            NotebookCover(notebook: notebook, subject: model.subjects.first { $0.id == notebook.subjectID })
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            .card()
        }
    }

    // MARK: News

    @ViewBuilder private var newsCard: some View {
        if !model.news.isEmpty {
            VStack(alignment: .leading, spacing: 12) {
                CardTitle("Von der Schule", symbol: "building.columns")
                ForEach(model.news.prefix(5)) { item in
                    VStack(alignment: .leading, spacing: 2) {
                        if let link = item.link { Link(item.title, destination: link).font(.subheadline.weight(.semibold)) } else { Text(item.title).font(.subheadline.weight(.semibold)) }
                        if let summary = item.summary { Text(summary).font(.footnote).foregroundStyle(.secondary).lineLimit(2) }
                    }
                }
            }
            .card()
        }
    }
}
