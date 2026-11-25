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

**Meilenstein 1 und 2 sind fertig** (1a, 1b, 1c; lokal committet, nichts gepusht, kein Release). Der Orchestrator prüft, die Sicherheitsprüfung (eigene Sitzung) liest 1a und 1b vor dem Einspielen
(Liste unten). Meilenstein 2 beginnt erst mit seinem Startzeichen. Das Einspielen auf die laufende Installation ist seine Sache, mit Sicherung davor und dem Ja des Eigentümers.

- **1a Basis eingehängt** (`base/` jetzt **0.3.4**, byteweise wie im Manifest): Konten, Sitzungen, Einrichtungsassistent, Rollen `admin` und `member`, Hub-Anmeldung, `/api/about`, `/api/health`, Einstellungen.
  `/api/v1` ist eine **eigene Tür der Basis** (SEC-07, `BaseAppOptions.Doors`), die Prüfung ist der Instanz-Token. Die Tabelle der Basis heißt `base_migrations` (DATA-04), unsere bleibt
  `schema_migrations`. Die alten Razor-Seiten (`/Notes`, `/Admin`) stehen hinter der Sitzung der Basis, mit **einer** Prüfung (`BaseSessionHandler`); Anmelde- und Abmeldeseiten und die Passwörter
  `ADMIN_PASSWORD`/`WEB_PASSWORD` sind weg. Die Startseite `/` ist eine Seite im Baukasten (Anmeldung, Einrichtung, Einstellungen, Kacheln zu `/Notes` und `/Admin`).
- **1b Besitzer:** Migration `0008_owner.sql` (leeres `owner_id`, Tabelle `assets`), Zuweisung an den ersten Administrator beim ersten Zugriff nach dem ersten Konto, Schreibregeln, Pull nur Eigenes,
  Dateien, Sicherheitsprotokoll mit Drossel, Leser sieht nur Eigenes.
- **1c Nachweis:** siehe unten (Tests, Browser, Altstand).

**Abweichungen vom Entwurf, jede einzeln** (zur Kenntnis und, wo gewünscht, zur Änderung):

1. **Zwei gleichnamige Tabellen.** `schema_migrations` (hatte die Basis anders) ist durch `MigrationTable` der Basis 0.3.2 gelöst. `instance_settings` hat die Basis ebenfalls (Schlüssel und JSON-Wert) wie
   unsere ungenutzte Baseline-Tabelle (Schlüssel und Text-Wert), und dafür gibt es in der Basis keine Einstellung: **Unsere Tabelle wird beim Start einmal in `app_instance_settings` umbenannt**
   (`DatabaseUpgrade.RenameLegacyTableAsync`, nur der Name, die Zeilen bleiben; `database/Schema.sql` heißt sie für neue Datenbanken gleich). Das ist die einzige Änderung an einer vorhandenen Tabelle außer der neuen Spalte.
2. **`/api/v1` antwortet bei fehlendem oder falschem Token jetzt mit dem Fehlerkörper der Basis** (401), vorher mit leerem Körper; und mit 401 statt 503, wenn gar kein `Sync__Token` gesetzt ist. Die App
   prüft nur den Status. Die Vertragstests wurden an dieser einen Stelle geändert (`ApiContractTests.Every_other_route_…`), alle anderen laufen unverändert.
3. **Sicherheitsköpfe der Basis auch auf `/api/v1`:** Jede Antwort bekommt jetzt `Content-Security-Policy`, `X-Content-Type-Options` usw., unter `/api` zusätzlich `Cache-Control: no-store`. Die iPad-App liest keinen davon,
   aber die Antworten sind nicht mehr byteweise die alten Kopfzeilen.
4. **JSON mit Nullwerten bleibt auf `/api/v1`.** Die Basis stellt global ein, dass `null` in Antworten fehlt (`DefaultIgnoreCondition = WhenWritingNull`). Das würde die Antworten an die App verändern (`parentId`, `payload`,
   `lastSuccess` …). `/api/v1` schreibt darum mit den Optionen des Frameworks plus `Never` (`V1Door`). **Für die Basis:** diese globale Einstellung trifft jede App, die schon eigene Antworten hat.
5. **Content-Security-Policy der alten Seiten:** Die Basis erlaubt weder `<style>`, noch `style=`, noch Inline-Skript. Die alten Seiten brauchen das. Die `<style>`-Blöcke und das eine Skript sind in Dateien
   gewandert (`wwwroot/legacy-*.css|js`); nur für `/Notes` und `/Admin` wird zusätzlich `style-src-attr 'unsafe-inline'` erlaubt (Farben von Covern). Skripte bleiben auf `'self'`. Fällt mit den Razor-Seiten am Ende von Meilenstein 2 weg
   (Test: `AdminTests.The_old_pages_get_only_the_one_relaxation_…`).
6. **Zuweisung nicht im Augenblick der Kontoerstellung,** sondern beim ersten Zugriff danach (die Basis hat keinen Haken dafür): beim Start, bei jeder Anmeldung einer Person und bei jedem Zugriff mit dem App-Token, einmalig
   (`OwnerDirectory`). Bis dahin gehören die Zeilen niemandem und die App gleicht wie bisher ab.
7. **Kein Fremdschlüssel von `owner_id` auf die Personen der Basis.** Sonst würde das Löschen einer Person entweder blockieren oder ihre Hefte löschen. Was mit den Heften einer gelöschten Person passiert, ist offen
   (Meilenstein 3); bis dahin bleiben sie liegen und niemand sieht sie.
8. **Rollen:** `admin` und `member` (die frühere „reader“-Rolle des Passworts entfällt). Wer lesen darf, ist jede angemeldete Person; die Verwaltung nur `admin`.
9. **Titel und Name:** `Notes` (Seiten, Manifest, Titel). Version der App heute `1.0.0` (Standard), noch nicht gesetzt.
10. **`check-app.sh`** meldet für die Kopie „unchanged“ und „up to date“ (0.3.2), und weiter zwei Regeln für die App: `OPS-01` (Compose heißt `compose.yaml`, braucht Variablen) und `CODE-07` (keine `CLAUDE.md`).
    Beides ist nicht angefasst (Einspielen und Arbeitsanweisungen sind eigene Entscheidungen).

## Bekannte Abweichungen von den Regeln der Basis (`check-app.sh`)

„Clean“ heißt für dieses Repository (Orchestrator, 24.11.2026): die Kopie von `base/` ist unverändert und aktuell, und es bleibt **nur der benannte Regelbefund zur Compose-Datei** (OPS-01/OPS-02):

1. **OPS-01 / OPS-02** (`compose.yaml` mit Variablen und `.env`, statt einer Compose-Datei ohne Variablen): **bis zum Umzug der Installation**, den der Orchestrator plant. Nichts anfassen.
2. ~~**CODE-07** (keine `CLAUDE.md`)~~ **erledigt am 25.11.2026:** Der Eigentümer hat es in diesem Chat ausdrücklich bestätigt; `CLAUDE.md` liegt im Repository (Muster der Basis, angepasst).

**Zentrale Paketverwaltung:** Die Versionen stehen jetzt in einer gemeinsamen Datei (`Directory.Packages.props`), ein zweites Paketsystem gibt es nicht; unsere Projekte nennen die Pakete nur noch ohne Version.

## Meilenstein 2, Scheibe 2a: die lesenden Wege `/api/web/v1` (25.11.2026)

Gebaut und getestet (`WebApiTests`, 7 Tests mit zwei Personen, Stichprobe: ohne Besitzer-Filter rot): `folders`, `notebooks` (mit `folder=<id>` oder `root`), `notebooks/{id}`, `notebooks/{id}/pages`, `pages/{id}` (Seite als ein Bild, wie die alte
Web-Ansicht sie zeichnete, plus Angaben zur Seite), `assets/{id}` (nur eigene Dateien, `no-store`), `grades`, `cards`, `search` (`timetable` und `news` waren kurz dabei, siehe unten). Alle hängen an der Sitzung der Basis, liefern nur Eigenes (fremde Dinge sind ein 404 mit demselben
Körper wie nicht vorhandene) und Listen kommen seitenweise (`limit` 1 bis 200, `cursor` ist die id der letzten Zeile). Der App-Token öffnet keinen davon.

**Zuschnitt gegenüber „Noten, Karten, Stundenplan, Suche soweit sie heute geht“:** Im Web gab es davon bisher nichts. Noten und Karten sind **Listen** (ohne Durchschnitt: die Rechenregeln des Schuljahres stehen in der Swift-Logik); der Stundenplan **kam zunächst als Sonderfall dazu und ist auf Wunsch des Orchestrators wieder draußen** (er kommt mit Meilenstein 4); die Suche findet **Namen von Ordnern und Heften** (der erkannte Text liegt nur auf dem iPad).

### Scheibe 2b: die Leseseiten im Baukasten (25.11.2026)

`wwwroot/app.js` mit `shell()`, Seiten in `wwwroot/pages/` (`library.js`, `study.js`, `util.js`), Texte `i18n/de.js` und `en.js`. Bibliothek (Ordner und Hefte als Kacheln mit Cover), Ordner (mit Verweisen zu Noten und Karten), Heft (Seitenliste),
Seite (das Bild der Seite wie die alte Web-Ansicht; Scan, PDF-Verweis, Überschrift, gedruckter Text, Lösung, Ergänzungen, vor und zurück), Noten und Karten als Listen, Suche nach Namen, Einstellungen (der Stundenplan war kurz dabei und ist wieder draußen, siehe unten). Das Bild einer Seite kommt
als SVG vom Server und wird als **Knoten** eingesetzt (nie als Markup): Skripte, Ereignis-Attribute und fremde Verweise fallen weg, `style=`-Attribute (die die Richtlinie der Basis ignorieren würde) werden über das Objektmodell gesetzt. Die Farben eines Covers kommen aus den
Daten und werden ebenso über das Objektmodell gesetzt; `app.css` hat die eine Farbvariable (Akzent 75) und sonst nur Form. Der Service Worker nimmt `/pages/` in die statische Hülle auf (`notes-v3`).
Geprüft im Browser mit Testdaten: Bibliothek, Ordner, Heft, Seite (mit Strichen, Objekt, Lösung, Ergänzungen), Whiteboard, Scan, Noten, Karten, Suche, bei breit und 390 px, hell und dunkel (das Blatt bleibt hell). Tests: `WebPagesTests` (Texte vollständig und gleich in beiden Sprachen,
keine Markup-Erzeugung, keine eigene Farbe). **Fehlt dem Baukasten etwas, das auch andere Apps brauchen würden?** Zwei Kleinigkeiten, die ich hier gelöst habe und nenne: ein Kachelraster mit Titelbild (hier `.cover` und `.tile.notebook`) und ein Abschnittstitel (`.section-title`).

**Änderung nach dem Urteil des Orchestrators (25.11.2026):** Der **Stundenplan ist nicht in Meilenstein 2.** Die Wege `timetable` und `news`, die Seite, der Menüpunkt, die Texte, der Test und die Bilder dazu sind wieder entfernt; er kommt mit Meilenstein 4 (SCOPE.md). Die Suchseite sagt in einem Satz, dass der Text in den Seiten noch nicht durchsucht wird.
Die Verwaltung zeigt, wenn die Zuweisung der alten Hefte gescheitert ist (`claimFailed` in der Übersicht, mit dem Hinweis auf Neustart nach Behebung); die Meldung im Protokoll sagt dasselbe. Test: `SecurityFindingsTests.A_claim_that_fails_…` (503 und Hinweis) und `…A_lock_hits_the_one_address_and_no_other`.

### Scheibe 2c und 2d: die Verwaltung im Baukasten, keine Razor-Seite mehr (25.11.2026)

- **Verwaltung als JSON** (`/api/web/v1/admin/...`, `RequireRole("admin")`, Wächter der Basis): `overview`, `school/import`, `codex` (Status, wartender Code, Einstellungen, Nutzung), `codex/login|cancel|logout`, `codex/settings`, `profiles` (lesen, ändern, zurücksetzen), `backups` (lesen, von Hand sichern). Test: `AdminApiTests` (ohne Sitzung 401, als `member` 403 an jedem Weg;
  Einstellungen und Profile speichern und ablehnen; die Anmeldung mit Gerätecode und Abbrechen; Sicherung von Hand mit einem Ersatz für `pg_dump`; Hinweis zum Schlüssel).
- **Seiten:** `pages/admin.js` (Überblick, KI-Verbindung mit Anmeldung per Gerätecode und Nachsehen alle 5 Sekunden, KI-Profile, Sicherungen), eine `capsule` als Unterleiste. Im Menü „Verwaltung“ nur für `admin`.
- **Razor ist weg:** `Pages/`, die Sitzungsprüfung für Razor, `AdminFormat`, die alten Stile und das alte Skript sind gelöscht, ebenso `AddRazorPages`, `UseAuthentication/Authorization` und die gelockerte Richtlinie (`style-src-attr 'unsafe-inline'`). **Überall gilt jetzt die Richtlinie der Basis unverändert** (Test:
  `DoorsTests.What_the_web_api_gives_is_never_kept_…`). Die alten Adressen leiten um: `/Notes` nach `/#/`, `/Notes/{id}` nach `/#/notebook/{id}`, `/Admin` nach `/#/admin`, `/Admin/Codex|Profiles|Backups` nach `/#/admin/…` (ohne Sitzung zeigt die Startseite die Anmeldung). `/Notes/Asset/{id}` gibt es nicht mehr.
- **Tests:** 326, davon **321 grün**; die 5 roten sind Backup-Tests, die das `pg_dump` der Version 18 brauchen (siehe oben). Weggefallen sind die Tests der Razor-Seiten (`AdminTests`, `WebReaderTests`); ihre Fälle stehen als `AdminApiTests`, `WebApiTests`, `OwnerTests`, `DoorsTests`. `AdminHost` steht jetzt in `AdminHost.cs`.
- **Bilder** in `docs/screenshots/` (`web-*.png`, headless Chromium gegen den Server mit Testdaten): breit (1280 × 800) Bibliothek, Ordner, Heft, Seite, Whiteboard, Noten, Karten, Suche, Verwaltung, KI-Verbindung, KI-Profile, Sicherungen; Telefon (390 × 844) Bibliothek, Seite, Noten, Verwaltung. Das dunkle Aussehen habe ich **im Browserfenster** gesehen (Telefon und breit, auch das Blatt bleibt hell), aber **nicht als Datei festgehalten**: Der Headless-Browser ignoriert hier die Einstellung für dunkel.
- **Nicht geprüft:** die Anmeldung über den Hub, der echte Abgleich mit dem iPad, HTTPS; „Jetzt abrufen“ der Schule und die echte Codex-Anmeldung (nur gegen Ersatzprogramme); die Sicherung mit dem echten `pg_dump`.
- **Was dem Baukasten fehlt und ich hier gelöst habe** (zur Prüfung, ob es in die Basis gehört): ein Kachelraster mit Titelbild (`.cover`, `.tile.notebook`), ein Abschnittstitel (`.section-title`), eine Seitenansicht (`.sheet`, nur `notes`), eine Codeanzeige (`.code`).

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
| `POSTGRES_PASSWORD` (`ConnectionStrings__JuNotes`) | Datenbank (die Basis nimmt sie als `BASE_DB`, wenn nur `ConnectionStrings__JuNotes` gesetzt ist) | nicht in 0 bis 2; erst beim Umzug der Installation (siehe „Betriebspunkt“ unten) |
| `SYNC_TOKEN` (`Sync__Token`) | gemeinsamer Token der iPad-App | 3, wenn die App Gerätetoken je Person schickt; abgeschaltet von Hand durch den Eigentümer |
| `ADMIN_PASSWORD` (`Admin__Password`) | Verwaltung im Web | **weg in 1** (aus `compose.yaml`, `.env.example` und README entfernt; in einer laufenden Installation harmlos, wird ignoriert) |
| `WEB_PASSWORD` (`Web__Password`) | Lesen im Web | **weg in 1** (wie oben) |
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

## Nachweis Meilenstein 1 (1c)

- **Tests:** 316 Tests, **310 grün**, die 6 Backup-Tests sind die oben genannten (pg_dump 17 gegen Server 18). Neu: `DoorsTests` (6), `OwnerTests` (8). Alle 15 Vertragstests aus Meilenstein 0 laufen
  **unverändert**, bis auf die eine Abweichung 2 (Fehlerkörper bei 401). Geändert wurden außerdem Tests, die zum alten Passwort-Anmeldeweg gehörten (entfallen: Anmeldeseiten, Sperre nach fünf Versuchen, Rückkehr-Adresse;
  die Anmeldung der Basis prüft die Basis) und `DatabaseUpgradeTests` (Tabellenname). Der Migrationstest läuft über die neuen Migrationen (Altstand nach Migration 5, `instance_settings` wieder unter dem alten Namen).
  Stichproben: Ein ausgeschalteter Eltern-Test und ein abgeschalteter Besitzer-Filter ließen genau die zuständigen Tests rot werden.
- **Browser (lokal, Wegwerfdaten auf der Testdatenbank, nichts echtes):** frischer Start mit Einrichtungscode aus dem Log, Konto mit zweitem Faktor (aus dem angezeigten Schlüssel berechnet), Wiederherstellungscodes, Abmelden und
  Anmelden, `/Notes` und `/Admin` hinter der neuen Anmeldung (ohne Sitzung zur Startseite), bei 1024 px und bei 390 px, hell und dunkel. Gesehen: Start, Hefte (leer), Verwaltung, KI-Verbindung. Die alten Seiten haben ihr
  altes Aussehen (blau, Blattstil) und werden mit Meilenstein 2 ersetzt; bei 390 px sind sie lesbar, aber nicht schön.
- **Altstand:** Eine Datenbank im Zustand vor der Basis (Schema des Servers von vor Meilenstein 1, Migrationen 0 bis 7 in der alten Tabelle, 8 Zeilen jeder Art, eine Datei; Testdaten) wurde von einem frisch gestarteten Server
  hochgezogen. Vorher und nach dem ersten Konto liefert der Pull mit dem alten Token alle 8 Zeilen mit denselben `seq` (1 bis 8) und demselben `cursor`; die Datei ist lesbar; ein Push danach zählt bei 9 weiter; nach dem
  Konto gehört alles dem Administrator (0 ohne Besitzer, 8 mit) und die Datei hat ihren Besitzer; `/Notes` mit der Sitzung zeigt das Heft. Der Rückweg (Umkehr-SQL unten) brachte das Schema auf den Altstand zurück.
- **Nicht gezeigt:** Anmeldung über den Hub, HTTPS, ein echter iPad-Abgleich (die App wurde nicht angefasst und nicht gestartet).

## Sicherheitsprüfung Meilenstein 1: Befunde und Behebung (25.11.2026)

Die unabhängige Prüfung von 1a und 1b fand keinen Weg, auf dem jemand ohne Zugang oder eine zweite angemeldete Person an Hefte, Dateien oder Verwaltung kommt, und zwei Befunde, mit denen nicht eingespielt wird. Alle sind behoben,
je Punkt ein Commit (Zeitfolge `eec8b5a`, `5ff1e62`, danach Sicherungen und Sync der Basis) und ein Test. Die Basis liegt jetzt auf **0.3.4** (0.3.3 bringt SEC-08 und die zwei Basis-Punkte).

| Nr. | Befund | Behebung | Test |
|---|---|---|---|
| 1 | Der Service Worker hob private Seiten auf | `sw.js` nach dem Muster der Basis: nur die statische Hülle, `notes-v2`; `Cache-Control: no-store` auf `/Notes`, `/Admin` und Scans; beim Abmelden werden die Zwischenspeicher geleert | `DoorsTests.The_service_worker_names_only_the_static_shell_…`, `…Private_pages_and_scans_are_never_kept_…` |
| 2 | Person hinter dem Token wurde bei jeder Anfrage neu bestimmt | Beim ersten Zuweisen einmal festgeschrieben (`app_instance_settings`, Schlüssel `token_owner`); ist die Person nicht aktiv, antwortet `/api/v1` mit 401, nie wird jemand anderes gewählt | `SecurityFindingsTests.The_person_behind_the_token_is_written_down_once_…` (herabgestuft: weiter dieselbe Person; gesperrt oder gelöscht: 401, zweite Person bekommt nichts) |
| 3 | `ForeignAsync` prüfte Art und id, `ForeignParentsAsync` nur die id | Beide prüfen die id allein: niemand legt eine Zeile unter einer id an, die jemand anderes benutzt | `SecurityFindingsTests.Nobody_puts_a_row_of_any_kind_under_an_id_…` |
| 4 | „Kein Besitzer“-Modus, wann immer kein Administrator gefunden wurde | Nur solange **kein** Konto existiert, das sich anmelden kann; sonst 401 | `SecurityFindingsTests.The_token_works_without_an_owner_only_while_no_account_exists` |
| 5 | Zuweisung und Migration unter der 5-Sekunden-Grenze | Eigene Verbindung ohne Zeitgrenze (`MaintenanceData`) für die Zuweisung und für die Migration unserer Folge; eine gescheiterte Zuweisung wird protokolliert, nicht neu versucht (bis zum nächsten Start) und lässt **keine** Anfrage mit 500 scheitern: `/api/v1` antwortet 503 mit einem Satz, es wird nichts auf besitzerlosen Zeilen geschrieben | `SecurityFindingsTests.A_claim_that_fails_is_a_503_…` |
| 6 | Umbenennen vor der Sicherung | `PendingAsync` ist nur lesend, die Umbenennung kommt nach der Sicherung | `DatabaseUpgradeTests.Looking_at_what_is_pending_changes_nothing_…` |
| 7 | Dateien: Besitz nach der Datei geschrieben | Erst der Körper wird geprüft (gleiche Antwort für alle), dann wird die id in `assets` beansprucht, dann die Datei an ihren Platz gelegt; zwei gleichzeitige PUTs können Besitz und Inhalt nicht vertauschen | `SecurityFindingsTests.A_file_for_an_id_of_somebody_else_gets_the_same_answers_…` |
| 8 | Falsche Token ohne Bremse, Protokoll, Mindestlänge | Start verweigert bei einem Token unter **32 Zeichen** (klare Meldung); mehr als 10 falsche Token je Adresse und Minute sperren die Adresse bis zum Ende der Minute (auch für das richtige Token) mit dem `RateLimit` der Basis und einer eigenen Sperrliste; eine Zeile `api.token_refused` im Sicherheitsprotokoll, nie das Token | `SyncAccessTests` (Länge), `SecurityFindingsTests.Too_many_wrong_tokens_lock_the_address_…` |
| – | `on conflict … do update` ohne Besitzbedingung | Beide Anweisungen tragen `owner_id is not distinct from excluded.owner_id` | durch die Zwei-Personen-Tests in `OwnerTests` mit abgedeckt (die Vorprüfung fängt zuerst; der Wächter in der Anweisung selbst ist **nicht einzeln getestet**, weil er nur bei einem Wettlauf greift) |
| – | `/status` war ein Präfix | exakter Pfad | `SecurityFindingsTests.Only_the_exact_status_path_is_open` |
| – | Anmelde-Middleware las das Cookie auch unter `/api` | `NoResult()` für alles unter `/api` | durch `DoorsTests.A_session_opens_nothing_under_api_v1` und die Tests der Basis-Pfade mit abgedeckt |
| – | Razor-Fangnetz | `AuthorizeFolder("/")` | `AdminTests` (Rollen, ohne Sitzung) |
| – | Sicherungen ohne `secret.key` | Mit Passphrase geht der Schlüssel verschlüsselt (`….key.enc`) zu jeder Kopie, wird zusammen mit seiner Sicherung gelöscht und liegt nie im Klartext neben einem Dump; ohne Passphrase kommt er nicht hinein und die Sicherungsseite warnt deutlich | `BackupKeyTests` (4; mit einem Ersatz für `pg_dump`, laufen deshalb auch hier) |

**Nicht gefolgt / anders gelöst:** Bei Punkt 8 zählt die Bremse **falsche Versuche** mit dem `RateLimit` der Basis (es kann nur zählen, nicht nachsehen) und führt die Sperre selbst. Bei Punkt 5 wird die Migration **unserer** Folge über die Verbindung ohne Zeitgrenze
gefahren; die Skripte der Basis laufen über die normale Verbindung (kleine neue Tabellen).

**Offene Frage des Orchestrators an den Eigentümer** (nichts gebaut): Die Zuweisung gibt alle heutigen Hefte der Person, die die Einrichtung abschließt; der Besitzer ändert sich später nicht mehr.

## Für das Einspielen (Meilenstein 1 auf die laufende Installation)

Nichts davon ist an der laufenden Installation ausgeführt. Einspielen ist Sache des Orchestrators, mit Sicherung davor und dem Ja des Eigentümers.

**Was sich an Compose-Datei und Volume ändern muss: nichts, was nicht additiv ist.**
- Die **Schlüsseldatei der Basis** (`secret.key`, 32 Zufallsbytes, Rechte nur für den Nutzer) liegt im Ordner `BASE_DATA`. Das **Image setzt `BASE_DATA=/data/base`** (Dockerfile); `/data` ist schon das Volume `junotes-data`.
  Der Ordner wird beim ersten Start angelegt. Die Compose-Datei der Installation braucht dafür **keine** Änderung. Sicherungen des Volumes enthalten den Schlüssel jetzt mit; **ohne diese Datei sind die
  verschlüsselten Werte in der Datenbank (zweite Faktoren) nicht zu lesen**: Datei und Datenbanksicherung gehören zusammen aufbewahrt.
- Die Datenbankverbindung bleibt `ConnectionStrings__JuNotes`; die Basis liest sie, wenn `BASE_DB` fehlt.
- `Admin__Password`, `Web__Password` und ihre Umgebungswerte werden nicht mehr gelesen und können später aus der Compose-Datei verschwinden.
- Der Port, die Gesundheitsprüfung (`/health/live`) und der Reverse-Proxy bleiben. Die Basis glaubt `X-Forwarded-*` von privaten Netzen; die Einstellung „vertrauenswürdige Proxys“ ist in der App einstellbar.

**Zusätzlich nach der Prüfung (25.11.2026):**
- **`Sync__Token` muss mindestens 32 Zeichen haben,** sonst startet der Server nicht (Meldung nennt `Sync__Token` und die Länge, nicht den Wert). **Der Orchestrator prüft die Länge des echten Tokens vor dem Einspielen** und tauscht es bei Bedarf (in der App und im Server zugleich).
- **Sperre je Adresse:** Sie hängt daran, dass der Server die **Adresse des Aufrufers** sieht und nicht die des Proxys (sonst könnte ein Fremder mit zehn falschen Token pro Minute das iPad aussperren). **Vor dem Einspielen prüft der Orchestrator, dass die vertrauenswürdigen Proxys stimmen und im Sicherheitsprotokoll (`api.token_refused`) die Adresse des Geräts steht, nicht die des Proxys.**
- **Der App-Token steht danach für eine festgeschriebene Person** (`token_owner` in `app_instance_settings`), die beim ersten Zuweisen bestimmt wird. Wird sie gesperrt oder gelöscht, antwortet `/api/v1` mit 401; es wird nie jemand anderes gewählt.
- **Sicherung:** `secret.key` geht nur mit einer Passphrase (`Backup__Passphrase`) und einem zweiten Ort verschlüsselt mit; ohne beides warnt die Sicherungsseite, und der Schlüssel muss getrennt aufgehoben werden (Datei im Volume unter `/data/base`).
- **Zuweisung:** läuft auf einer eigenen Verbindung ohne Zeitgrenze, beim Start und bei der ersten Anmeldung; scheitert sie, antwortet `/api/v1` mit 503, bis der Server neu gestartet wird (es steht im Log).
- Der Service Worker heißt jetzt `notes-v2`; Browser, die noch die alte Fassung haben, räumen beim ersten Besuch auf.

**Was beim ersten Start passiert:** Vor der ersten Migration läuft die automatische Sicherung (`Backup__BeforeMigration`, schlägt sie fehl, startet der Server nicht). Dann: die Tabelle `instance_settings` heißt `app_instance_settings`;
die Basis legt ihre Tabellen an (`users`, `sessions`, … und `base_migrations`); Migration 8 fügt `entities.owner_id` und die Tabelle `assets` hinzu. **Keine Zeile wird verändert.** Im Log steht
„FIRST SETUP … setup code: XXXX-XXXX-XXXX“.

**Was der Eigentümer einmal tun muss:** Adresse des Servers öffnen, den Einrichtungscode aus dem Log eingeben (`docker compose logs junotes`), Benutzername und Passwort wählen (mindestens 12 Zeichen), den zweiten Faktor
einrichten (Authenticator-App, sechs Ziffern), die Wiederherstellungscodes sichern. Das erste Konto ist der Administrator. Danach gehören alle bisherigen Hefte und Dateien diesem Konto.

**Was passiert, solange er es nicht getan hat:** Die iPad-App gleicht **weiter ab**, mit demselben Token, mit denselben Antworten (Vertragstests; Altstand-Probe unten). Die Zeilen, die dabei neu entstehen, gehören niemandem und gehen
beim ersten Konto mit an den Administrator. **Nicht erreichbar sind bis dahin:** die Seiten und Wege der Web-App (sie brauchen ein Konto; die Startseite zeigt die Einrichtung), also auch die Codex-Anmeldung und die Sicherungsseite im Browser. Wer die Adresse und
den Einrichtungscode aus dem Log hat, kann das erste Konto anlegen: Die Einrichtung nicht liegen lassen.

**Wie man zurückgeht:**
1. **Sicher:** das alte Image starten und die Datenbank aus der Sicherung vor dem Update zurückspielen (`pg_restore`). Das ist der Weg, den man wirklich geht.
2. **Von Hand (an einer Kopie mit Testdaten geprüft, nicht an echten Daten):** Das Schema geht damit in den Stand vor der Basis zurück (`pg_dump -s` beider Datenbanken war bis auf die Zufallsmarken gleich); die Zeilen
   in `entities` bleiben, auch die neuen. Das alte Image startet dann wieder.
   ```sql
   begin;
   drop table recovery_codes, sessions, account_audit, user_settings, invites, hub_flows, hub_logout_tokens, users cascade;
   drop table instance_settings;
   drop table base_migrations;
   alter table app_instance_settings rename to instance_settings;
   alter index app_instance_settings_pkey rename to instance_settings_pkey;
   delete from schema_migrations where number = 8;
   drop table assets;
   drop index entities_owner_seq, entities_unowned;
   alter table entities drop column owner_id;
   commit;
   ```
   Dabei gehen die Konten, Sitzungen und der zweite Faktor der Basis verloren (nicht die Hefte).

## Für die Sicherheitsprüfung (1a und 1b)

**Jede neue Tür:**

| Tür | Wer kommt hinein | Wo der Code steht | Wo der Test steht |
|---|---|---|---|
| `/api/v1/*` (Prefix, ohne Rücksicht auf Groß- und Kleinschreibung) | nur mit `Authorization: Bearer <Sync__Token>`; kein Cookie, kein `X-Base`, kein Origin; nur `/status` offen | `Api/V1Door.cs` (`CheckAsync`), Deklaration in `Program.cs` (`Doors`) | `ApiContractTests.Every_other_route_answers_401_…` (14 Wege × 3 Köpfe), `DoorsTests.A_session_opens_nothing_under_api_v1`, `…The_instance_token_opens_nothing_outside_api_v1`, `…The_token_door_needs_no_header…`, `…The_door_is_the_same_door_in_any_spelling_of_its_path` |
| `/api/**` der Basis (Anmeldung, Einrichtung, Personen, Einstellungen, Konto) | Sitzung der Basis, `X-Base: 1` und Origin bei Änderungen | `base/` (unverändert, Tests der Basis) | `DoorsTests.The_base_answers_health_about_…` |
| `/api/web/v1/**` (Bibliothek, Hefte, Seiten, Dateien, Noten, Karten, Suche) | Sitzung der Basis (Wächter der Basis), nur eigene Zeilen und Dateien; der App-Token öffnet nichts davon | `Api/WebApi.cs`, `LibraryReader.Of(person)`, `AssetService.OpenAsync(id, person)` | `WebApiTests.*`, `OwnerTests.The_notebooks_of_the_web_…`, `DoorsTests.The_instance_token_opens_nothing_outside_api_v1` |
| `/api/web/v1/admin/**` | Sitzung der Basis und Rolle `admin` (`RequireRole`, geprüft im Wächter der Basis) | `Api/AdminApi.cs` | `AdminApiTests.Only_an_administrator_gets_in_at_any_path` (ohne Sitzung 401, als `member` 403) |
| `/Notes`, `/Notes/{id}`, `/Admin`, `/Admin/Codex|Profiles|Backups` | nur noch Weiterleitungen auf die neuen Adressen, ohne Inhalt | `Program.cs` | `DoorsTests.What_the_web_api_gives_is_never_kept_…` |
| `/health/live`, `/health/ready`, Dateien in `wwwroot/` | offen | `Program.cs` | `AdminTests.The_open_endpoints_stay_open_…` |

**Jede neue Regel:**

| Regel | Test |
|---|---|
| Eine vorhandene Zeile einer anderen Person wird nicht überschrieben (Zeile bleibt byteweise gleich), der Rest des Stapels wird angenommen, `accepted` nennt nur die angenommenen | `OwnerTests.A_batch_with_one_forbidden_and_two_allowed_rows_…` |
| Eine Zeile darf nicht unter ein fremdes `parent_id`-Ziel (beliebige Art) | `OwnerTests.A_row_may_not_be_hung_under_a_parent_…` |
| Fremde Zeilen löschen oder einen fremden Strich wiederbeleben geht nicht | `OwnerTests.Another_person_can_neither_delete_…` |
| Pull liefert nur Eigenes; die `seq` in der Push-Antwort ist die höchste der eigenen Zeilen | `OwnerTests.A_pull_gives_only_the_persons_own_rows_…` |
| Dateien: fremde nicht ersetzen, nicht lesen, nicht finden (gleiche Antwort wie „nicht vorhanden“) | `OwnerTests.A_file_of_somebody_else_…` |
| Zeilen und Dateien ohne Besitzer gehen genau einmal an den ersten Administrator, an niemanden sonst; der Token steht danach für ihn, auch wenn weitere Administratoren kommen | `OwnerTests.Rows_and_files_that_nobody_owned_…` |
| Ein Verstoß über die Tür der App ist still ausgelassen, die Antwort verrät nichts, das Sicherheitsprotokoll nennt Person, Gerät, Art, **keine Inhalte**, höchstens eine Zeile je Minute | `OwnerTests.A_broken_rule_through_the_door_of_the_app_…` |
| Überall gilt die Richtlinie der Basis unverändert (die Lockerung der alten Seiten ist mit ihnen verschwunden) | `DoorsTests.What_the_web_api_gives_is_never_kept_…` |
| Ein verformter Stapel bleibt 400 und speichert nichts | `ApiContractTests.Push_refuses_a_malformed_batch_…` |

**Bekannte Lücken und Grenzen (ohne Beschönigung):**
- Der App-Token ist weiter **ein gemeinsamer Schlüssel** für das Konto des ersten Administrators. Wer ihn hat, liest und schreibt dessen Hefte. Das war vorher nicht anders; Token je Gerät und Person kommen mit Meilenstein 3.
- `owner_id` hat keinen Fremdschlüssel; Zeilen einer gelöschten Person bleiben liegen.
- Ein `parent_id`, das **noch nicht existiert**, wird angenommen. Legt später eine andere Person eine Zeile mit dieser id an, bleiben beide Zeilen in ihrer Hand, die Filter nach Besitzer verhindern das Mitlesen.
  Geprüft sind die Regeln nur gegen **vorhandene** fremde Zeilen.
- Die Drossel des Sicherheitsprotokolls steht im Speicher (ein Neustart setzt sie zurück); das Schreiben ist „abschicken und weitermachen“ (ein Fehler wird nur ins Log geschrieben).
- Die Zuweisung geschieht beim ersten Zugriff nach dem ersten Konto (Abweichung 6), die Ermittlung des ersten Administrators liest die Tabelle `users` der Basis mit eigenem SQL (Kopplung an deren Schema).
- Die Einrichtungsseite ist offen, bis das erste Konto existiert (Entwurf der Basis); der Code steht im Log.
- Das gezeichnete Bild einer Seite kommt als SVG vom Server und wird im Browser als Knoten eingesetzt (kein `innerHTML`); `<foreignObject>` mit dem Text eines Textfelds bleibt erhalten (der Text ist dort HTML-kodiert, `PageRenderer`).
- Nicht geprüft: Anmeldung über den **Hub** (kein Hub in dieser Umgebung), echte Proxys und HTTPS (das `__Host-`-Cookie gilt nur unter HTTPS), Lasttest.

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

## Hier nicht prüfbar

- **Sechs Backup-Tests** (`BackupServiceTests`, `BackupMirrorIntegrationTests`, `AdminTests.A_manual_backup_is_written_and_listed`): Auf dieser Maschine liegt `pg_dump` in Version 17, der Testserver ist 18
  („unpassende Serverversion“). Das ist die Umgebung. Sie bleiben, wie sie sind; in der CI (mit Version 18) laufen sie.

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
