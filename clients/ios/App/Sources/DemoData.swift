import Foundation
import JuNotesCore
import SwiftUI
import UIKit

/// A made-up school week for screenshots and design review: `-JuNotesDemo` fills an empty in-memory library, `-JuNotesScreen <name>` opens one screen.
/// Nothing of it is ever written to the real library, and it does not exist without the launch argument.
enum Demo {
    static let isOn = ProcessInfo.processInfo.arguments.contains("-JuNotesDemo")

    /// The screen to open: today, subject, notebook, cards, grades, settings.
    static var screen: String {
        let arguments = ProcessInfo.processInfo.arguments
        guard let index = arguments.firstIndex(of: "-JuNotesScreen"), arguments.indices.contains(index + 1) else { return "today" }
        return arguments[index + 1]
    }
}

extension AppModel {
    func seedDemo() async {
        let calendar = Calendar.current
        let now = Date()
        let today = calendar.startOfDay(for: now)

        // Folders in folders: three areas, and the subjects inside "Schule".
        let school = Subject(name: "Schule", color: "#5b7cfa", symbol: "graduationcap", sort: 0, isSubject: false,
                             cover: Cover(style: .gradient, colorA: "#8f7cf5", colorB: "#5b4bd6", symbol: "graduationcap", pattern: .lines))
        let kita = Subject(name: "Kindi", color: "#ff9f0a", symbol: "figure.2.and.child.holdinghands", sort: 1, isSubject: false,
                           cover: Cover(style: .gradient, colorA: "#ffd15c", colorB: "#ff9f0a", symbol: "teddybear", pattern: .dots))
        let other = Subject(name: "Sonstiges", color: "#8e8e93", symbol: "tray.full", sort: 2, isSubject: false)
        let year26 = Subject(name: "Schuljahr 2026/27", color: "#5b7cfa", symbol: "calendar", sort: 0, folderID: school.id, isSubject: false, schoolYear: 2026)
        let year25 = Subject(name: "Schuljahr 2025/26", color: "#8e8e93", symbol: "archivebox", sort: 1, folderID: school.id, isSubject: false, schoolYear: 2025, closed: true)
        let oldMath = Subject(name: "Mathe", color: "#5b7cfa", symbol: "function", sort: 0, folderID: year25.id)
        let math = Subject(name: "Mathe", color: "#5b7cfa", symbol: "function", sort: 0, folderID: year26.id,
                           cover: Cover(style: .gradient, colorA: "#6fb1ff", colorB: "#3478f6", symbol: "function", pattern: .grid))
        let german = Subject(name: "Deutsch", color: "#ff9f0a", symbol: "text.book.closed", sort: 1, folderID: year26.id,
                             cover: Cover(style: .gradient, colorA: "#ff9a7a", colorB: "#f2545b", emoji: "📖"))
        let english = Subject(name: "Englisch", color: "#30b0c7", symbol: "globe.europe.africa", sort: 2, folderID: year26.id,
                              cover: Cover(style: .gradient, colorA: "#4fd1d9", colorB: "#1f7fb5", symbol: "globe.europe.africa"))
        let biology = Subject(name: "Biologie", color: "#34c759", symbol: "leaf", sort: 3, folderID: year26.id,
                              cover: Cover(style: .gradient, colorA: "#8fd96b", colorB: "#3fa73e", symbol: "leaf", pattern: .dots))
        let worksheets = Subject(name: "Arbeitsblätter", color: "#5b7cfa", symbol: "folder", sort: 0, folderID: math.id, isSubject: false)
        let subjects = [school, kita, other, year26, year25, oldMath, math, german, english, biology, worksheets]
        for subject in subjects { try? await store.put(subject) }

        // Notebooks, newest last; the first has handwriting on every page.
        let fractions = Notebook(subjectID: math.id, title: "Bruchrechnung", template: .ruled, createdAt: now.addingTimeInterval(-9 * 86400))
        let geometry = Notebook(subjectID: math.id, title: "Geometrie", template: .grid, createdAt: now.addingTimeInterval(-30 * 86400))
        let poems = Notebook(subjectID: german.id, title: "Gedichte", template: .ruled, createdAt: now.addingTimeInterval(-20 * 86400))
        let cell = Notebook(subjectID: biology.id, title: "Die Zelle", template: .dotted, createdAt: now.addingTimeInterval(-14 * 86400))
        let vocabulary = Notebook(subjectID: english.id, title: "Unit 4 Vocabulary", template: .ruled, createdAt: now.addingTimeInterval(-6 * 86400))
        let sheet = Notebook(subjectID: worksheets.id, title: "Übungsblatt 3", template: .blank, createdAt: now.addingTimeInterval(-3 * 86400))
        let report = Notebook(subjectID: kita.id, title: "Praxisbericht", template: .ruled, createdAt: now.addingTimeInterval(-12 * 86400),
                              cover: Cover(style: .gradient, colorA: "#ffd15c", colorB: "#ff9f0a", emoji: "🧸", pattern: .dots))
        let observations = Notebook(subjectID: kita.id, title: "Beobachtungen", template: .ruled, createdAt: now.addingTimeInterval(-5 * 86400),
                                    cover: Cover(style: .gradient, colorA: "#c87cf0", colorB: "#7b3fc4", symbol: "eye", pattern: .lines))
        let recipes = Notebook(subjectID: other.id, title: "Rezepte", template: .dotted, createdAt: now.addingTimeInterval(-40 * 86400),
                               cover: Cover(style: .gradient, colorA: "#ff9fc4", colorB: "#d6478a", emoji: "🍰"))
        let notebooks = [fractions, geometry, poems, cell, vocabulary, sheet, report, observations, recipes]
        for notebook in notebooks { try? await store.put(notebook) }

        var thirdPageID: UUID?
        for (index, key) in PageOrder.keys(appending: 7, after: nil).enumerated() {
            let heading = index == 0 ? "Do 12.11. · Mathe" : index == 2 ? "Mo 16.11. · Mathe" : nil
            let page = Page(notebookID: fractions.id, sort: key, heading: heading)
            try? await store.put(page)
            if index == 2 { thirdPageID = page.id }
            if index < 4 { for stroke in Self.handwriting(seed: index, lines: index == 3 ? 4 : 11) { try? await store.put(stroke, on: page.id) } }
        }
        for notebook in [geometry, poems, cell, vocabulary, sheet, report, observations, recipes] {
            for (index, key) in PageOrder.keys(appending: 3, after: nil).enumerated() {
                let page = Page(notebookID: notebook.id, sort: key)
                try? await store.put(page)
                if index == 0 { for stroke in Self.handwriting(seed: 7 + index, lines: 6) { try? await store.put(stroke, on: page.id) } }
            }
        }

        // A timetable around now: the running lesson began 20 minutes ago.
        func lesson(_ title: String, start: Date, minutes: Int = 45, room: String, cancelled: Bool = false) -> CalendarEvent {
            CalendarEvent(uid: "demo-\(title)-\(start.timeIntervalSince1970)", start: start, end: start.addingTimeInterval(Double(minutes) * 60), summary: title, location: room, cancelled: cancelled)
        }
        let running = now.addingTimeInterval(-20 * 60)
        var events = [
            lesson("Deutsch", start: running.addingTimeInterval(-105 * 60), room: "R 112"),
            lesson("Mathematik", start: running, room: "R 204"),
            lesson("Englisch", start: running.addingTimeInterval(60 * 60), room: "R 118", cancelled: true),
            lesson("Biologie", start: running.addingTimeInterval(115 * 60), room: "Labor"),
        ]
        let exam = calendar.date(byAdding: .day, value: 7, to: today).map { calendar.date(bySettingHour: 8, minute: 0, second: 0, of: $0) ?? $0 } ?? now
        let vocabTest = calendar.date(byAdding: .day, value: 4, to: today).map { calendar.date(bySettingHour: 9, minute: 55, second: 0, of: $0) ?? $0 } ?? now
        events.append(CalendarEvent(uid: "demo-exam-math", start: exam, end: exam.addingTimeInterval(45 * 60), summary: "Klassenarbeit Mathe", location: "R 204"))
        events.append(CalendarEvent(uid: "demo-exam-english", start: vocabTest, end: vocabTest.addingTimeInterval(20 * 60), summary: "Vokabeltest Englisch", location: "R 118"))
        self.events = events
        news = [
            SchoolNews(key: "n1", title: "Elternsprechtag am 3. Dezember", summary: "Die Anmeldung für die Gespräche läuft bis zum 27. November."),
            SchoolNews(key: "n2", title: "Weihnachtskonzert der Musikklassen", summary: "Am 18. Dezember in der Aula, Beginn um 18 Uhr."),
        ]

        let grades: [(Subject, Double, GradeCategory, String, Int)] = [
            (math, 2.0, .written, "Klassenarbeit 1", -60), (math, 2.5, .oral, "Mitarbeit", -30), (math, 1.5, .test, "Kurztest Brüche", -12),
            (german, 2.0, .written, "Aufsatz", -45), (german, 3.0, .oral, "Referat", -20),
            (english, 1.0, .written, "Klassenarbeit 1", -50), (english, 2.0, .test, "Vokabeltest", -9),
            (biology, 2.0, .test, "Test Zellbiologie", -25),
        ]
        try? await store.put(Grade(subjectID: oldMath.id, value: 2.3, category: .written, date: now.addingTimeInterval(-300 * 86400), title: "Abschlussarbeit"))
        for (subject, value, category, title, daysAgo) in grades {
            try? await store.put(Grade(subjectID: subject.id, value: value, category: category, date: now.addingTimeInterval(Double(daysAgo) * 86400), title: title))
        }

        let cards: [(Subject, String, String)] = [
            (math, "Wie addiert man Brüche mit verschiedenen Nennern?", "Erst auf den Hauptnenner erweitern, dann die Zähler addieren."),
            (math, "Was heißt Kürzen?", "Zähler und Nenner durch dieselbe Zahl teilen."),
            (math, "Wie dividiert man durch einen Bruch?", "Mit dem Kehrwert multiplizieren."),
            (biology, "Wozu dient der Zellkern?", "Er enthält die Erbinformation und steuert die Zelle."),
            (biology, "Was machen die Mitochondrien?", "Sie liefern die Energie der Zelle."),
            (english, "Wie lautet die Vergangenheit von „to go“?", "went"),
            (english, "Wie lautet das Partizip von „to write“?", "written"),
            (german, "Was ist eine Metapher?", "Ein Bild: Ein Wort steht für etwas anderes, ohne „wie“."),
        ]
        for (index, card) in cards.enumerated() {
            let due: Date? = index < 5 ? now.addingTimeInterval(-3600) : (index == 7 ? nil : now.addingTimeInterval(86400 * 2))
            try? await store.put(Flashcard(subjectID: card.0.id, front: card.1, back: card.2, schedule: CardSchedule(interval: due == nil ? 0 : 3, repetitions: due == nil ? 0 : 2, due: due)))
        }

        // A study plan for the maths exam, with the first task done.
        let plan = StudyPlanner.plan(topics: [StudyTopic(title: "Brüche erweitern", minutes: 25), StudyTopic(title: "Brüche addieren", minutes: 25), StudyTopic(title: "Kürzen", minutes: 20)], examDate: exam, today: today, minutesPerDay: 45)
        plans.save(StoredPlan(id: "demo-exam-math", examTitle: "Klassenarbeit Mathe", examDate: exam, plan: plan, done: ["0-0"]))

        snapshot.lastBackupAt = now.addingTimeInterval(-9 * 3600)
        await reload()

        switch Demo.screen {
        case "library": await select(.root)
        case "coverEditor": await select(.folder(year26.id))
        case "folder": await select(.folder(school.id))
        case "year": await select(.folder(year26.id))
        case "subject": await select(.folder(math.id))
        case "kindi": await select(.folder(kita.id))
        case "notebook":
            await select(.folder(math.id))
            pendingPageID = thirdPageID
            pendingNotebookID = fractions.id
        case "cards": await select(.cards)
        case "grades": await select(.grades)
        default: await select(.today)
        }
    }

    /// Ink that looks written: wobbling lines of loops, in blue, so the pages in screenshots are not empty.
    static func handwriting(seed: Int, lines: Int) -> [Stroke] {
        var generator = SeededGenerator(seed: UInt64(seed + 11))
        var strokes: [Stroke] = []
        let left = 92.0
        for line in 0..<lines {
            let baseline = 118.0 + Double(line) * 27.0
            var x = left
            let length = Double.random(in: (line % 4 == 3 ? 150 : 300)...(line % 4 == 3 ? 220 : 400), using: &generator)
            while x < left + length {
                // One "word": a run of small up and down strokes.
                let wordEnd = min(x + Double.random(in: 28...70, using: &generator), left + length)
                var points: [StrokePoint] = []
                var t = 0.0
                var px = x
                while px < wordEnd {
                    let phase = (px - x) / 5.2
                    let y = baseline - 7 - 6 * abs(sin(phase)) * (0.6 + 0.4 * cos(phase * 0.37))
                    points.append(StrokePoint(x: px, y: y + Double.random(in: -0.7...0.7, using: &generator), pressure: 1, time: t))
                    px += 1.6
                    t += 0.004
                }
                if points.count > 2 { strokes.append(Stroke(tool: .pen, color: "#1d2a8f", width: 3.2, points: points)) }
                x = wordEnd + Double.random(in: 7...13, using: &generator)
            }
        }
        return strokes
    }
}

/// The same "handwriting" on every run, so two screenshots of one screen match.
struct SeededGenerator: RandomNumberGenerator {
    private var state: UInt64
    init(seed: UInt64) { state = seed &* 0x9E37_79B9_7F4A_7C15 | 1 }
    mutating func next() -> UInt64 {
        state ^= state << 13
        state ^= state >> 7
        state ^= state << 17
        return state
    }
}
