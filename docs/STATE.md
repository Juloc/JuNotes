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

Meilenstein 0 (Startzeichen des Orchestrators vom 24.11.2026) ist **fertig**:

- **Probe der Basis unter .NET 11:** Mit dem Level der Basis (`latest`) übersetzen Basis und Start-App unter net11 ohne Fehler und Warnung; mit unserem Level (`latest-recommended`) sind es
  49 Stellen. Entscheidung siehe unten (Level der Basis für `base/`). Geprüft, dass die Bedingung greift: `base/` bekommt `latest`, unser Code `latest-recommended`.
- **`base/` samt `MANIFEST` ist übernommen** (Version 0.3.1, 82 Dateien, byteweise wie im Manifest). `check-app.sh` der Basis meldet für die Kopie „unchanged (81 files)“ und „up to date“. Es meldet
  außerdem zwei Befunde zu **Regeln für die App**, die nicht die Kopie betreffen: `OPS-01` (es gibt keine `docker-compose.yml`; unsere heißt `compose.yaml` und braucht Variablen, `OPS-02`) und
  `CODE-07` (keine `CLAUDE.md`). Beides gehört zum Umzug der Installation und zur Frage, wie dieses Repository seine Arbeitsanweisungen führt; **beides ist nicht angefasst**, die Entscheidung liegt
  beim Orchestrator und beim Eigentümer.
- **Base-Projekte sind noch nicht in `JuNotes.slnx`** und werden von der App nicht gebaut: Sie brauchen die zentrale Paketverwaltung der Basis (`Directory.Packages.props`), die unsere Projekte
  (Versionen im Projekt) stören würde. Das löst sich beim Einhängen in Meilenstein 1.
- **Abgleichvertrag als Tests:** `tests/JuNotes.Backend.Tests/ApiContractTests.cs`, 15 Tests gegen den echten Host mit echter Datenbank (siehe unten). Alle grün. Dazu der Test mit einem
  Datenbankstand durch alle Migrationen.

Dann: das Urteil des Orchestrators über Meilenstein 0 abwarten; Meilenstein 1 beginnt erst mit seinem Startzeichen. Der WebUntis-Client bleibt unangetastet, die Antwort des Eigentümers steht aus.
Umbenennen von Repository und Ordner macht der Orchestrator.

## Entscheidungen, die schon gefallen sind

- Frei verschachtelbare Ordner und Hefte, Fächer sind Ordner mit Eigenschaften (PHASE5.md).
- Öffentliches Repository ohne persönliche Angaben; Schulname und Adressen nur in `LOCAL.md`; Commits als `Juloc`.
- Kostenloses Apple-ID-Sideloading, kein Mac, die CI baut.
- Zugangsdaten tippt immer der Eigentümer selbst.
- Die Entscheidungen 1 bis 8 vom 24.11.2026 (SCOPE.md).
- Orchestrator, 24.11.2026: `notes` bleibt auf net11 (wenn die Probe grün ist); `notes` zählt die KI pro Person selbst; Besitzer leer = „erster Administrator“;
  `/api/v1` ist eine eigene Tür; der Hub-Schlüssel wird in der Verwaltung eingetragen, nicht in der Umgebung; Web-App als Seiten der Basis, Razor fällt weg.
- Orchestrator, 24.11.2026: **`base/` wird mit dem Analyselevel der Basis (`latest`) übersetzt, unser Code bleibt bei `latest-recommended` mit Warnungen als Fehler** (Bedingung auf den Projektpfad in
  `Directory.Build.props`). Begründung: Die Kopie wird in der Basis mit eigenen Regeln und Tests geprüft und bleibt byteweise gleich dem `MANIFEST`, statt in jeder App noch einmal mit anderen Maßstäben.
  Die 49 Stellen bei unserem Level gehen als Liste an die Basis (ein strengerer Level dort ist eine spätere, eigene Entscheidung; CA5350 bei TOTP bliebe eine begründete Ausnahme).
- Das Schulische bleibt aus dem öffentlichen Repository (jetzt auch die Basis öffentlich): kein Name, keine Adresse, kein Passwort.
- **Fragen vor dem Bauen:** Bei mehreren möglichen Entwürfen erst fragen (interaktiv, auf Deutsch), dann einmal bauen.

## Starten, testen, veröffentlichen

**Server testen** (.NET 11 SDK, PostgreSQL 18):

```bash
dotnet build JuNotes.slnx
export JUNOTES_TEST_DB='Host=127.0.0.1;Port=15434;Username=junotes_test;Password=isolated-test-password;Database=junotes_test_admin'
dotnet test tests/JuNotes.Backend.Tests
```

Ohne `JUNOTES_TEST_DB` werden die Datenbanktests übersprungen. Die Testdatenbank startet der Befehl in der README (Docker, Port 15434). Die Backup-Tests brauchen
`pg_dump` und `pg_restore` in Version 18. **Auf dieser Maschine liegt Version 17**; darum schlagen dort 6 Backup-Tests fehl (Stand 24.11.2026: 322 von 328
grün), alle mit „unpassende Serverversion“. Das ist die Umgebung, kein Fehler im Code. Das .NET-11-SDK liegt in `~/.dotnet` (nicht auf dem PATH).

**Was die Vertragstests festhalten** (`ApiContractTests`): `/api/v1/status` ist offen, alles andere antwortet ohne oder mit falschem Token mit 401 und leerem Körper;
`push` antwortet `{"accepted":N,"seq":M}` mit N = Anzahl der geschickten Zeilen (auch wenn nichts gespeichert wurde) und M = höchste Nummer; ein verformter Stapel ist 400 und
speichert nichts (auch nicht die guten Zeilen davor); `pull` antwortet `{"changes":[…],"cursor":…,"more":…}` in Nummernfolge, Begrenzung 1 bis 500; ein Strich wird nur
hinzugefügt oder gelöscht, alles andere gilt „letzter schreibt“; **die Nummern haben Lücken** (jede geschickte Zeile verbraucht eine, eine Änderung zwei); Dateien:
204/200/206/404/415/413/400; Schule, KI-Profile und Export haben feste Formen. Ändert sich eine dieser Antworten, ändert sich der Test im selben Commit. Stichprobe: Eine
absichtlich falsche Antwortzahl und ein ausgeschalteter Token-Test ließen genau die zuständigen Tests rot werden (danach zurückgenommen).

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

## Einstellungen aus der Umgebung und wann sie wegfallen

Heute kommen sie aus `.env` über `compose.yaml`. Die Basis verlangt eine Compose-Datei ohne Variablen und ohne `.env`; Geheimnisse liegen verschlüsselt in der
Datenbank. Es kommen keine neuen Geheimnisse in die Umgebung.

| Variable (Umgebungswert) | Wofür | Fällt weg in |
|---|---|---|
| `POSTGRES_PASSWORD` (`ConnectionStrings__JuNotes`) | Datenbank | nicht in 0 bis 2; erst beim Umzug der Installation (siehe „Betriebspunkt“ unten) |
| `SYNC_TOKEN` (`Sync__Token`) | gemeinsamer Token der iPad-App | 3, wenn die App Gerätetoken je Person schickt; abgeschaltet von Hand durch den Eigentümer |
| `ADMIN_PASSWORD` (`Admin__Password`) | Verwaltung im Web | 1, mit dem eigenen Konto |
| `WEB_PASSWORD` (`Web__Password`) | Lesen im Web | 1, mit dem eigenen Konto |
| `UNTIS_SERVER`, `UNTIS_SCHOOL`, `UNTIS_USER`, `UNTIS_PASSWORD` (`School__Untis__*`) | WebUntis-Client | 0, **wenn der Eigentümer das Entfernen entscheidet** |
| `SCHOOL_TIMETABLE_ICS_URL`, `SCHOOL_NEWS_FEED_URL` (`School__TimetableIcsUrl`, `School__NewsFeedUrl`) | Kalender-Abo und Feed, heute einer für alle | 4, dann je Person in der Datenbank |
| `PAPERLESS_BASE_URL`, `PAPERLESS_TOKEN` (`Paperless__*`) | Export | spätestens 5, in der Verwaltung (verschlüsselt) |
| `BACKUP_COPY_TO` (`Backup__CopyTo`) | zweiter Ort für Sicherungen | 5, in der Verwaltung |
| `BACKUP_PASSPHRASE` (`Backup__Passphrase`) | Schlüssel der verschlüsselten Sicherungen | **offen, siehe unten** |
| Codex-Anmeldung (Volume `CODEX_HOME`, kein Umgebungswert) | eigene Brücke | 6, im Modus Hub ist sie aus |

**Benannte Ausnahme `BACKUP_PASSPHRASE`:** Sie darf nicht nur in der Datenbank liegen, die sie sichert; sonst wäre eine Sicherung bei verlorener Datenbank
nicht lesbar. Die Basis hat noch kein Sicherungs-Modul, deshalb bleibt sie vorerst eine Umgebungsvariable. Die richtige Lösung (ein Wiederherstellungsschlüssel,
den der Eigentümer einmal sieht und außerhalb aufbewahrt, während der Server nur das zum Verschlüsseln Nötige hält) kommt mit dem Sicherungs-Modul der Basis.
**Dafür wird hier nichts Eigenes gebaut.**

## Betriebspunkt: die laufende Installation

Die Basis löst das Datenbank-Passwort so (laut Orchestrator, gelesen in ihrem Beispiel `samples/StartApp/docker-compose.yml`): Ein einmaliger Dienst `init`
legt im einen Volume die Ordner an und erzeugt beim allerersten Start ein zufälliges Passwort als Datei (Rechte 400); PostgreSQL liest sie über
`POSTGRES_PASSWORD_FILE`, die App liest dieselbe Datei. In der Compose-Datei steht kein Geheimnis.

**Für die laufende Installation heißt das:** Das heutige Passwort und die heutige Aufteilung der Volumes bleiben, bis der Orchestrator den Umzug der Installation
plant, mit Sicherung davor und dem Ja des Eigentümers. **In den Meilensteinen 0 bis 2 werden Compose-Datei und Volumes der laufenden Installation nicht
angefasst.** Das ist ein offener Betriebspunkt, kein Auftrag an diese Sitzung.

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
