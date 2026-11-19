# JuNotes – Plan

Eine Notiz- und Lern-App für Schülerinnen und Schüler:
native iPad-App mit vollem Apple-Pencil-Pro-Support, dazu eine Web-App und ein eigener Server auf dem NAS.
Ziel: alles, was GoodNotes kann, aber einfacher, klarer und mit Schule und KI verbunden. Sie soll sich anfühlen wie
eine App von Apple, aber konsequent auf Funktion ausgerichtet sein.

Stand der Versionen: 19.11.2026. Was davon gebaut ist und was nicht, steht in [STATUS.md](STATUS.md).

**Zielgerät:** iPad Air 13" (M2, 2024), 512 GB, nur WLAN, mit Apple Pencil Pro (Squeeze, Barrel Roll, Hover
und Haptik werden unterstützt). Das Display hat **60 Hz** (kein ProMotion). Ohne Mobilfunk gilt: Offline ist der
Normalfall in der Schule, der Sync holt alles nach, sobald WLAN oder ein Hotspot da ist.

## 1. Leitprinzipien

1. **Sie sieht immer, was passiert.** Eine feste Statuszeile zeigt zum Beispiel
   „Auf iPad gespeichert ✓ · Synchronisiert 14:02 ✓ · Backup heute 03:00 ✓“. Fehler stehen dort in Klartext
   („Server nicht erreichbar, alles ist sicher auf dem iPad“).
2. **Nichts geht verloren.** Ein Papierkorb hält 30 Tage, jede Seite hat einen Versionsverlauf, Undo bleibt über
   Neustarts hinweg erhalten. Löschen fragt nie nach, weil es immer rückgängig zu machen ist.
3. **Keine versteckten Menüs.** Wenige große Aktionen, sichtbar beschriftet. Wer etwas einstellen will, ist der
   Admin, und das passiert im Web.
4. **Die KI fasst ihre Handschrift nie an.** Sie schlägt vor, und sie übernimmt. Jeder KI-Inhalt verlinkt die
   Heftstelle, aus der er stammt.
5. **Offline zuerst.** Das iPad funktioniert komplett ohne Netz, Server und KI sind Zusatz.
6. **Immer schnell.** Feste Speed-Budgets (Abschnitt 4), die in CI gemessen werden.
7. **Apple-Look, aber Funktion vor Optik.** Systemkomponenten statt Eigenbau. Glas und Effekte nur dort, wo Apple sie
   vorsieht, nie auf Kosten von Lesbarkeit oder Tempo.

## 2. Versionen (immer die neueste stabile)

| Bereich | Version | Hinweis |
|---|---|---|
| iPadOS (Mindestversion) | **iPadOS 27** | Nur die neueste Version, läuft auf ihrem iPad Air M2. |
| Xcode / Swift | **Xcode 27, Swift 6.4**, Strict Concurrency | Im Cloud-Build |
| UI | **SwiftUI** mit dem Liquid-Glass-Design von iPadOS 27, UIKit nur wo nötig (Canvas) | |
| Zeichnen | **PaperKit + PencilKit** (iPadOS 27 APIs) | Siehe Abschnitt 5 |
| Server | **.NET 11 / C# 15** | Seit 10.11.2026 GA. Der Support läuft bis 11/2028, genauso lange wie .NET 10 LTS. |
| Datenbank | **PostgreSQL 19** | GA für 19.11.2026 geplant. Bis der Server steht, ist das sicher. Sonst 18 und später per Upgrade. |
| Web | **htmx 4, Vite, TypeScript, SCSS** wie in Jularr | |
| Regel | Neue Versionen werden übernommen, sobald sie stabil sind. Renovate oder Dependabot schlägt Updates vor, CI prüft sie. | |

## 3. Architektur

```
iPad-App (SwiftUI, PaperKit/PencilKit)        Web-App (Razor + htmx 4, wie Jularr)
  lokale SQLite (GRDB) + Dateien                Lesen, Suchen, Organisieren, Lernen,
  Handschrifterkennung + Scanner auf Gerät      Schule, Admin-Bereich (nur du)
            │  Sync-API (HTTPS, Strich-Deltas)          │
            └──────────────► JuNotes-Server ◄──────────┘
                .NET 11 + PostgreSQL 19, ein Docker-Host auf dem NAS
                (Jularr-Framework: Frontend-Host, Backend, Workers, Integrations)
                     ├─ Codex (Jularr-CodexConnection, JSON-Schema-Muster aus FullWorth)
                     ├─ OCR-Worker für gedruckte Scans (PP-OCRv5 aus Jularr)
                     ├─ Untis- und Schulseiten-Worker
                     └─ Backup-Worker → NAS, Google Drive, Paperless
```

### Server = Jularr-Framework

Der Server übernimmt die Architektur und die Regeln aus Jularr (`docs/DEVELOPMENT.md` dort):

- **Ein Prozess:** `JuNotes.Frontend` ist der einzige Web-Host. `Backend`, `Workers` und `Integrations` sind Bibliotheken darin.
- **Backend:** Services lesen, `Logic` schreibt, ein Service-Aufruf ist eine Transaktion. SQL steht direkt in der Methode,
  mit gebundenen Parametern und ohne EF. Listen lesen eine begrenzte Seite.
- **Datenbank:** eingefrorene Baseline plus nummerierte Migrationen. Vor jeder Migration läuft automatisch ein Backup.
- **Web:** serverseitig gerenderte Razor-Seiten, htmx 4, Vite/TS/SCSS, Design-Tokens, UI-Texte als Ressourcen.
- **Speed-Budget wie Jularr:** Eine Seite antwortet in 0,03 s Serverzeit, ein nachgeladener Teil in 0,1 s.
- **Sync-API:** eigene, schlanke JSON-Endpunkte für die iPad-App. Sie laufen im selben Host über dieselben Backend-Services.
- **Neu ist nur der Look:** Jularr ist bewusst flach („kein Glas“), JuNotes übernimmt Apple-Stil (Abschnitt 6).

### Wiederverwendung

| Baustein | Herkunft | Bewertung |
|---|---|---|
| Host-, Backend-, Worker- und SQL-Infrastruktur, Migrationen, Tests | Jularr | **Grundgerüst.** |
| Codex-Verbindung: CLI-Login, Device-Login, Modellkatalog, Verbrauch | `Jularr…/CodexConnection.cs`, `Jularr.Integrations/Logic/Codex` | **Übernehmen**, läuft im Prozess, kein extra Node-Dienst. |
| Strukturierte KI-Ausgabe per `jsonSchema`, Aufruf-Logs | `FullWorth.CodexBridge` | **Muster übernehmen** in die Jularr-Codex-Anbindung. Ob der Node-Teil nötig bleibt, zeigt Phase 0. |
| Backups mit `pg_dump` und verschlüsseltem Schlüsselbund, Rotation | Jularr | **Übernehmen**, plus Dateien und weitere Ziele. |
| OCR (PP-OCRv5, ONNX) | Jularr | **Nur für gedruckte Scans.** Handschrift erkennt das iPad. |
| Paperless-Client | FullWorth | **Umdrehen:** JuNotes schickt die PDFs hin. |
| Scan-Queue (Job-Store, Worker) | FullWorth | **Muster** für die Server-Verarbeitung. |
| Secret-Bootstrap, Erst-Registrierung = Admin, Passkeys | FullWorth | **Übernehmen.** |

## 4. Performance-Budgets

| Wo | Budget |
|---|---|
| Strich auf dem iPad | Systemlatenz von PencilKit mit Strichvorhersage. Kein eigener Code im Zeichenpfad. |
| App-Start bis Bibliothek | < 0,5 s (warm), < 1 s (kalt) |
| Heft öffnen | < 0,2 s bis zur ersten Seite, auch bei 300 Seiten |
| Scrollen und Zoomen | konstant 60 fps, ohne jeden Ruckler (Display 60 Hz). Ausgelegt auf 120 fps, falls später ein ProMotion-iPad kommt. Seiten werden gekachelt und erst bei Bedarf gerendert, Miniaturen liegen im Cache. |
| Suche (lokal) | < 0,1 s über alle Hefte, mit SQLite FTS5 |
| Sync | im Hintergrund, nur Strich-Deltas, blockiert nie die UI |
| Server | 0,03 s pro Seite und 0,1 s pro Teil (Jularr-Regel). Sync-Endpunkte < 0,05 s. |
| KI, OCR, Backup | immer im Hintergrund mit sichtbarem Status, nie ein Warten ohne Anzeige |

Gemessen wird mit XCTest-Performance-Tests (`XCTOSSignpostMetric`) im Cloud-Build und mit den Jularr-Speed-Tests auf dem Server.

## 5. iPad-App: Technik

- **SwiftUI** für die ganze App, **UIKit** nur für die Zeichenfläche.
- **Zeichnen:** **PaperKit** (Apples Markup-Framework aus Notizen und Freeform) setzt auf PencilKit auf. Es bringt
  Striche, Formen, Textfelder und Bilder in einer Fläche mit, sieht also genau wie Apple aus.
  iPadOS 27 ergänzt **stabile Strich-IDs** (ideal für den Sync auf Strichebene), die **Umwandlung in Bézier-Pfade**
  (die Web-App rendert sie als SVG) und die eingebaute **Handschrifterkennung**.
  **Phase-0-Prototyp entscheidet:** Wenn PaperKit beim Seitenmodell oder Format zu eng ist, wird es reines PencilKit
  mit eigener Ebene für Bilder und Text.
- **Pencil Pro:** Squeeze, Barrel Roll, Hover und Haptik über die Systemschnittstellen. Damit verhält sich der Stift
  so, wie sie es aus Notizen kennt.
- **Daten:** SQLite über **GRDB** (schnell, volle SQL-Kontrolle, FTS5-Suche, passt zur SQL-Linie von Jularr). Große
  Dateien liegen als Dateien. Sync auf Strichebene: Gleichzeitige Änderungen werden zusammengeführt statt dupliziert.
- **Scanner:** VisionKit-Dokumentkamera mit automatischer Kantenerkennung. Gedruckter Text wird per Vision erkannt.
- **Systemintegration:** Spotlight (Hefte und Handschrift durchsuchbar), App Intents und Kurzbefehle („Neue
  Mathe-Seite“), Drag & Drop, Tastaturkurzbefehle, mehrere Fenster (zwei Hefte nebeneinander, gut für den
  Abschreib-Modus) und später Widgets.

### Pencil-Pro-Belegung (Vorschlag, in der App einstellbar)

| Geste | Standard |
|---|---|
| Doppeltipp | Radierer ↔ Stift (Systemeinstellung wird respektiert) |
| Squeeze | Werkzeugpalette am Stift (System-Tool-Picker) |
| Barrel Roll | Winkel bei Textmarker und Füller |
| Hover | Vorschau des Strichs und der Radiererfläche |
| Strich + halten | Form begradigen |

## 6. Design: Apple-Stil, aber funktional

Grundlage sind die Human Interface Guidelines von Apple für iPadOS 27.

- **Systemkomponenten statt Eigenbau:** `NavigationSplitView` mit Seitenleiste (Fächer und Ordner), Toolbars, Menüs,
  Sheets, Suchfeld und Tool-Picker. Sie bekommen Liquid Glass, die Glas-Einstellung des Systems und künftige
  Design-Updates automatisch.
- **Glas nur auf der Navigationsebene**, also Leisten und schwebende Werkzeuge. Seiten und Inhalte bleiben ruhig
  und lesbar.
- **Ausgelegt auf 13 Zoll quer und hoch:** Seitenleiste dauerhaft sichtbar im Querformat, Heft plus Seitenleiste
  oder zwei Hefte nebeneinander. Im Hochformat blendet sich die Seitenleiste aus.
- **SF Symbols**, Systemschrift, Systemfarben pro Fach, Dark Mode, Dynamic Type, VoiceOver, „Bewegung reduzieren“.
- **Funktion zuerst, wo Apple zu sparsam ist:**
  - Wichtige Aktionen tragen eine Beschriftung, nicht nur ein Symbol („Scannen“, „Neue Seite“, „Lernen“).
  - Die Statuszeile für Speichern, Sync und Backup ist immer sichtbar.
  - Seitenzahl und Position stehen immer da.
  - Nichts versteckt sich hinter langem Drücken, das ist nur eine Abkürzung.
- **Web-App:** derselbe Aufbau (Seitenleiste, Toolbar), Systemschrift-Stack, Apple-ähnliche Tokens, hell und dunkel.
  Liquid Glass wird im Web nicht nachgebaut, nur dezente Transparenz in den Leisten.

## 7. Bauen und Installieren ohne Mac

- **Kernlogik als Swift-Paket** (`JuNotesCore`): Datenmodell, Sync, Strichformat, Suche, API-Client. Ohne UIKit,
  SwiftUI und PencilKit, damit es auf Linux mit `swift build` und `swift test` läuft.
- **App-Hülle** möglichst dünn, Projektdatei per **XcodeGen** (`project.yml`).
- **Build:** GitHub Actions auf einem macOS-Runner mit Xcode 27.

### Stufe A: kostenlos testen (ab Phase 0)

1. CI baut eine **unsignierte `.ipa`**.
2. Auf deinem Linux signiert und installiert **iloader** oder **Impactor** (beide Open Source, laufen unter Linux)
   sie per USB mit einer **kostenlosen Apple-ID**. Am besten nimmst du dafür eine eigene Apple-ID statt deiner
   Haupt-ID.
3. Einschränkungen der kostenlosen Apple-ID:
   - Die App läuft **7 Tage** und muss dann neu signiert werden. Mit **SideStore** geht das danach per WLAN direkt
     auf dem iPad.
   - Es gibt **keine iCloud/CloudKit-Rechte**. Das stört nicht, der Sync läuft über deinen Server.
   - Es sind nur wenige Apps gleichzeitig möglich.
4. Kosten: GitHub Actions auf macOS ist bei **öffentlichen Repos kostenlos**. Bei **privaten Repos** reicht das
   kostenlose Kontingent für rund 200 macOS-Minuten im Monat, also einige Dutzend Builds. Das genaue Abrechnungsmodell
   hat sich laut Berichten im Oktober 2026 geändert und wird beim Einrichten geprüft.

### Stufe B: Alltag (spätestens ab Phase 1, wenn sie umsteigt)

Ab hier kommt das **Apple Developer Program** (99 €/Jahr) mit **TestFlight**. Damit gibt es Updates per Knopfdruck
und keinen 7-Tage-Ablauf, und iCloud-Backup sowie Push werden möglich. Signiert wird per
App-Store-Connect-API-Key in CI, ohne Mac. Xcode Cloud (Build-Stunden inklusive) wird dann zur Alternative zu
GitHub Actions.

**Fehlersuche ohne Mac:** Logs und Absturzberichte gehen an den eigenen Server, ab Stufe B zusätzlich an TestFlight.
Reicht das nicht, mieten wir stundenweise einen Mac in der Cloud.

## 8. Struktur und Hefte

```
Schuljahr 2026/27
 └─ Fach (Farbe, aus Untis übernommen)
     └─ Ordner (optional)
         └─ Heft: Typ „Seiten“ oder „Whiteboard (endlos)“
             └─ Seiten / Scans / PDFs
```

- **Fächer kommen aus Untis.** Neue Fächer werden einmal vorgeschlagen und nicht still angelegt.
- **„Jetzt“-Knopf:** In der Stunde öffnet ein Tipp das passende Fachheft auf einer neuen Seite mit Datum und
  Stundenthema als Kopfzeile.
- **Seiten-Hefte:** Die Seiten laufen untereinander. Schreibt sie unten weiter, entsteht automatisch eine neue Seite.
  Eine Seitenleiste zeigt Miniaturen, Seitenzahl „3 von 7“ und Sortieren per Drag. Leere Auto-Seiten werden beim
  Schließen still entfernt.
- **Whiteboard:** Unendliche Fläche mit Minikarte, exportierbar als PDF über einen Ausschnitt.
- **Vorlagen** pro Heft (liniert, kariert, gepunktet, Cornell, leer). Das Wechseln gilt für „ab hier“ oder „ganzes
  Heft“, und es ist klar sichtbar, was passiert.

### Scannen (der GoodNotes-Schmerzpunkt)

1. Ein großer Knopf „Scannen“ ist überall erreichbar.
2. Kamera mit Auto-Erkennung, mehrere Blätter am Stück.
3. Danach fragt die App einmal: **„Wohin?“** Als Vorschlag steht das aktuelle Fach laut Stundenplan (das Heft ist
   schon ausgewählt), mit „Hier einfügen“ oder „Als neues Arbeitsblatt“.
4. Der Scan ist sofort beschreibbar. Die Texterkennung läuft im Hintergrund, der Status ist sichtbar.
5. Optional geht eine Kopie automatisch an Paperless, mit Fach als Tag.

### Suche

- Eine Suchleiste für alles: Handschrift, Scans, PDFs, Heftnamen, Fach und Datum.
- Treffer zeigen einen **Ausschnitt der Seite mit Markierung**.
- Filter-Chips: Fach, Zeitraum, nur Scans, nur Markiertes.
- Auch über Spotlight auf dem iPad erreichbar.
- Phase 3: inhaltliche Suche per KI („wo hatten wir die Photosynthese?“).

## 9. Schule

| Quelle | Was | Wie |
|---|---|---|
| **WebUntis** (ihr Login) | Stundenplan, Vertretungen, Prüfungen und Klassenarbeiten, Hausaufgaben | Inoffizielle JSON-RPC-API, abgerufen vom Server-Worker. Die Zugangsdaten liegen verschlüsselt. |
| **Schul-Website** (Adresse als Instanz-Einstellung) | Termine, News, eventuell Vertretungsplan | Ein Adapter pro Schule. Noch zu prüfen: Was steht dort, braucht es einen Login, gibt es RSS oder iCal? |
| **Noten** | Erfassen, Schnitt pro Fach, Gewichtung, Verlauf | Selbst eintragen. Eine digitale Quelle wird angebunden, wenn klar ist, wo sie liegt. |

Daraus entsteht ein **Schul-Cockpit**: heute, nächste Stunde, Vertretungen, anstehende Prüfungen mit Lernstand und
Noten auf einen Blick.

## 10. KI

### Für sie: nur Knöpfe

Auf jeder Seite oder jedem Heft gibt es ein Menü „Lernen“:

- **Abfragen:** Die KI stellt Fragen zum markierten Stoff, sie antwortet schriftlich oder mündlich, und die KI bewertet freundlich.
- **Probetest:** ein Test als neues Heft zum Beschreiben, mit Lösung. Die Korrektur übernimmt die KI.
- **Karteikarten:** automatisch erstellt, mit Wiederholungsplan (Spaced Repetition).
- **Ergänzen & Prüfen:** Die KI markiert Lücken, Fehler und Unklares am Rand. Sie tippt „übernehmen“ oder „weg“.
- **Abschreib-Modus:** Das Original wird kurz gezeigt und dann ausgeblendet, sie schreibt aus dem Gedächtnis,
  danach markiert ein Vergleich, was fehlt.
- **Lernplan:** Aus der Prüfung in Untis und dem Stoff im Heft entsteht ein Plan mit Tagesaufgaben bis zum Termin.

### Für dich: Admin im Web

- Codex-Verbindung (Device-Login wie in Jularr), Modell, Tageslimit und Verbrauch.
- **Profile** (Lernen, Test, Zusammenfassen, Ergänzen, Abschreib-Check) mit Basis-Prompt, Schwierigkeit,
  Tonfall und Ausgabeschema. Du änderst sie, sie sieht nur die Namen.
- Ein Protokoll der KI-Aufrufe zur Fehlersuche.

## 11. Speicher, Sync und Backup

- **Wahrheit:** iPad lokal, gespiegelt auf den Server auf dem NAS. Sync auf Strichebene, gleichzeitige Änderungen
  werden zusammengeführt. Wo das nicht geht (zum Beispiel wenn dieselbe Seite gelöscht und bearbeitet wurde), bleiben
  beide Fassungen erhalten.
- **Backups (automatisch, nachts):**
  - Server: Datenbank und Dateien auf ein NAS-Backup-Verzeichnis (Rotation wie in Jularr)
  - Server: verschlüsselte Kopie nach **Google Drive**
  - iPad: Kopie nach **iCloud Drive** (ab Stufe B mit Developer Account)
  - **Paperless:** fertige Hefte und Scans als durchsuchbare PDFs mit Tags. Das ist ein Archiv, kein Backup.
- Wiederherstellen per Knopf im Admin-Bereich, regelmäßig getestet.
- **Import aus GoodNotes:** Export als PDF, Import in JuNotes, Struktur nach Fach übernehmen.

## 12. Phasen

| Phase | Inhalt | Ergebnis |
|---|---|---|
| **0 Fundament** | Repo, Server-Skelett aus Jularr, `JuNotesCore` mit Linux-Tests, XcodeGen, CI-Build einer `.ipa`, Installation per iloader, **Prototyp PaperKit vs. PencilKit** | App auf ihrem iPad, kostenlos |
| **1 MVP** | Schreiben mit Pencil Pro, Fächer, Ordner und Hefte, Auto-Seiten, Whiteboard, Scannen, Suche, Papierkorb, Statuszeile, Sync, NAS-Backup, GoodNotes-Import, Developer Account + TestFlight | **Sie kann GoodNotes ersetzen.** |
| **2 Schule** | Untis, Schul-Cockpit, „Jetzt“-Knopf, Noten, Schul-Website, Web-App zum Lesen und Suchen | Schulalltag an einem Ort |
| **3 KI und Lernen** | Codex, Profile, Abfragen, Tests, Karteikarten, Ergänzen, Abschreib-Modus, Lernplan | Lernhilfe |
| **4 Feinschliff** | Google Drive, iCloud, Paperless, KI-Suche, Bearbeiten im Web, Widgets, Kurzbefehle | Komplett |

Nach jeder Phase nutzt sie die App eine Woche, und erst ihr Feedback steuert die nächste Phase.

## 13. Öffentliches Repo

Das Repo auf GitHub ist öffentlich. Dadurch sind die macOS-Builds auf den Standard-Runnern kostenlos.

- **Keine persönlichen Daten im Repo:** Schule, Untis-Server, Website-Adressen, Namen und Zugangsdaten sind
  **Instanz-Einstellungen** im Admin-Bereich oder stehen in `docs/LOCAL.md`, die per `.gitignore` ausgeschlossen ist.
- **Keine Secrets:** Signier-Schlüssel und API-Keys liegen nur in GitHub-Secrets oder auf dem Server
  (Secret-Bootstrap wie in FullWorth). Workflows aus Forks bekommen keine Secrets.
- **Schul-Anbindungen als Adapter:** WebUntis ist allgemein. Die Schul-Website ist ein austauschbarer Adapter, damit
  nichts schulspezifisch fest im Code steht.
- **Lizenz:** noch festlegen (zum Beispiel wie bei FullWorth).
- **Jularr-Code:** Teile werden kopiert und angepasst, nicht als Abhängigkeit eingebunden. Die Lizenz von Jularr muss
  das erlauben, es ist dein eigenes Repo.

## 14. Offene Fragen

1. **NAS:** Welches Modell (Synology?), und wie erreicht sie es von unterwegs (Tailscale, Domain + Caddy)?
2. **Untis:** Schulname und Server in WebUntis (steht in der Login-URL).
3. **Schul-Website:** Was steht dort genau, und braucht es ihren Login?
4. **Noten digital:** Wo genau, also in Untis, Moodle oder einem Portal?
5. **Web-App schreiben:** Soll sie im Web auch handschriftlich schreiben, oder reicht dort Lesen, Organisieren und Lernen?
