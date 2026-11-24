# Übergabe: Stand und nächster Schritt

Stand: 24.11.2026. Diese Seite ist für jede Sitzung, die hier weitermacht. Sie enthält keine privaten Angaben; Adressen, Schulname und Servereinträge stehen in
`docs/LOCAL.md` (nicht eingecheckt). Ausführlicher Stand der Funktionen: [STATUS.md](STATUS.md). Plan des Umzugs: [SCOPE.md](SCOPE.md) und
[ARCHITECTURE.md](ARCHITECTURE.md).

## Worum es geht

Ein Ersatz für GoodNotes für eine Schülerin der praxisintegrierten Erzieherausbildung: iPad-App (SwiftUI, PencilKit), eigener Server (.NET, PostgreSQL),
Web-Ansicht, KI-Hilfen über Codex, Noten, Stundenplan, Sicherungen. Der Eigentümer ist zugleich Admin. Das Projekt wird zu `notes` und zieht schrittweise
auf die gemeinsame Basis um (Entscheidungen vom 24.11.2026 in SCOPE.md).

## Was fertig ist

- **Server** läuft auf der Docker-VM hinter dem Reverse-Proxy mit HTTPS. Datenbank, Sicherungen und Codex-Anmeldung sind eingerichtet. Echte Antworten von
  Codex wurden geprüft (Cover, Quiz).
- **Web-Ansicht** zeigt Bibliothek, Hefte, Objekte, Cover und Scans. Verwaltung: Übersicht, Codex (Gerätecode, Modell, Aufwand, Tageslimit, Nutzung),
  Profile, Sicherungen.
- **iPad-App** (Ordnerbaum, Cover, Noten, Heute, Objekte, Seitenraster, Erinnerungen): gebaut und in der CI übersetzt, unsignierte `.ipa` als Release
  `latest`.
- **Geschichte bereinigt:** Der Schulname und die Intranetadresse sind aus `main` und dem Tag `latest` entfernt (Umschreibung der Geschichte, Spitze
  `101d54a`, Baum unverändert). Rest: siehe „Offene Punkte“.

## Nächster Schritt

1. Der Orchestrator sieht [SCOPE.md](SCOPE.md) und [ARCHITECTURE.md](ARCHITECTURE.md) durch. **Bis dahin wird nichts gebaut.**
2. Danach Meilenstein 0: Entscheidung über den WebUntis-Client (Empfehlung: entfernen), Probe .NET 10 Basis gegen .NET 11 hier, Abgleichvertrag als Tests
   einfrieren.
3. Umbenennen von Repository und Ordner macht der Orchestrator, nicht die Sitzung im Ordner.

## Entscheidungen, die schon gefallen sind

- Frei verschachtelbare Ordner und Hefte, Fächer sind Ordner mit Eigenschaften (PHASE5.md).
- Öffentliches Repository ohne persönliche Angaben; Schulname und Adressen nur in `LOCAL.md`; Commits als `Juloc`.
- Kostenloses Apple-ID-Sideloading, kein Mac, die CI baut.
- Zugangsdaten tippt immer der Eigentümer selbst.
- Die Entscheidungen 1 bis 8 vom 24.11.2026 (SCOPE.md).
- **Fragen vor dem Bauen:** Bei mehreren möglichen Entwürfen erst fragen (interaktiv, auf Deutsch), dann einmal bauen.

## Starten, testen, veröffentlichen

**Server testen** (.NET 11 SDK, PostgreSQL 18):

```bash
dotnet build JuNotes.slnx
export JUNOTES_TEST_DB='Host=127.0.0.1;Port=15434;Username=junotes_test;Password=isolated-test-password;Database=junotes_test_admin'
dotnet test tests/JuNotes.Backend.Tests
```

Ohne `JUNOTES_TEST_DB` werden die Datenbanktests übersprungen. Die Testdatenbank startet der Befehl in der README (Docker, Port 15434). Die Backup-Tests brauchen
`pg_dump` und `pg_restore` in Version 18.

**Swift-Kern testen:**

```bash
cd clients/ios/JuNotesCore && swift test
```

**Server lokal starten:** `dotnet run --project src/JuNotes.Frontend` (Umgebungsvariablen: `.env.example`).

**App bauen und verteilen:** Die CI (Runner `xcode-27`) übersetzt, macht Bildschirmfotos im Demomodus (`-JuNotesDemo -JuNotesScreen <name>`) und
veröffentlicht `JuNotes.ipa` plus `source.json` als Release `latest`. Installiert wird die `.ipa` per Sideloading; herunterladen mit
`gh release download latest --repo Juloc/JuNotes -p JuNotes.ipa`.

**Server aktualisieren:** Der Server baut sein Image aus dem GitHub-Zweig `main`. Das Aktualisieren geschieht auf der Docker-VM nach dem Eintrag in `docs/LOCAL.md`
(Image neu bauen, Container neu starten). Vor jeder Migration entsteht automatisch eine Sicherung.

**Nicht pushen**, bevor der Orchestrator es sagt: Der Entwurf des Umzugs ist lokal committet.

## Offene Punkte

- **Nie auf einem iPad gestartet.** Weder Pencil-Gefühl noch Oberfläche sind am Gerät geprüft; die App ist nur übersetzt.
- **Sideloading:** Die Anmeldung in SideStore 0.6.x scheitert (503, ADI −45063); die Alpha 0.7.0 ist geladen, das Ergebnis offen.
- **Stundenplan:** Es gibt keine Quelle. Kein Kalender-Link hinterlegt; der WebUntis-Client ist ungeprüft gegen die echte Schule und widerspricht den
  Schulregeln (SCOPE.md).
- **Sicherungsziel** (`BACKUP_COPY_TO`) ist nicht gesetzt: Sicherungen liegen nur im Volume des Servers.
- **Alte Geschichte bei GitHub:** Der alte Commit ist über seine Prüfsumme weiter lesbar, und `refs/pull/1…6/head` zeigen auf alte Stände. Das entfernt nur
  der GitHub-Support. Ein Spiegel der alten Geschichte liegt im Arbeitsordner der Sitzung (Zwischenspeicher) und ist zu löschen, sobald der Eigentümer
  bestätigt.
- **Fehler** in `Profiles.OnPostResetAsync` (doppelter Aufruf), laut Basis-Plan bekannt, nicht behoben.
- **Cookie** der Verwaltung ohne dauerhaften Schlüsselring: Sitzungen enden beim Neustart. Erledigt sich mit der Basis.

## Nicht geprüft (ehrlich)

| Was | Stand |
|---|---|
| iPad-App auf dem Gerät | nie gestartet |
| Texterkennung, Dokumentenkamera, Pencil, Hintergrundabfrage | nur übersetzt, nicht erprobt |
| Google Drive, Paperless, WebUntis | gegen Attrappen oder gar nicht, nicht gegen die echten Dienste |
| Bildererzeugung durch Codex | Cover laufen über SVG-Fallback; echte Bilder nicht erprobt |
| Basis-Code unter .NET 11 | nicht übersetzt |
| Hub-KI (`/api/ai/v1`) | nur die Beschreibung gelesen, nie aufgerufen |
| Abgleich mit mehreren Personen | existiert nicht |
