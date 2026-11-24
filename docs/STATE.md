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

**Meilenstein 1 ist fertig** (1a, 1b, 1c; lokal committet, nichts gepusht, kein Release). Der Orchestrator prüft, die Sicherheitsprüfung (eigene Sitzung) liest 1a und 1b vor dem Einspielen
(Liste unten). Meilenstein 2 beginnt erst mit seinem Startzeichen. Das Einspielen auf die laufende Installation ist seine Sache, mit Sicherung davor und dem Ja des Eigentümers.

- **1a Basis eingehängt** (`base/` jetzt **0.3.2**, byteweise wie im Manifest): Konten, Sitzungen, Einrichtungsassistent, Rollen `admin` und `member`, Hub-Anmeldung, `/api/about`, `/api/health`, Einstellungen.
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

- **Tests:** 315 Tests, **309 grün**, die 6 Backup-Tests sind die oben genannten (pg_dump 17 gegen Server 18). Neu: `DoorsTests` (6), `OwnerTests` (8). Alle 15 Vertragstests aus Meilenstein 0 laufen
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

## Für das Einspielen (Meilenstein 1 auf die laufende Installation)

Nichts davon ist an der laufenden Installation ausgeführt. Einspielen ist Sache des Orchestrators, mit Sicherung davor und dem Ja des Eigentümers.

**Was sich an Compose-Datei und Volume ändern muss: nichts, was nicht additiv ist.**
- Die **Schlüsseldatei der Basis** (`secret.key`, 32 Zufallsbytes, Rechte nur für den Nutzer) liegt im Ordner `BASE_DATA`. Das **Image setzt `BASE_DATA=/data/base`** (Dockerfile); `/data` ist schon das Volume `junotes-data`.
  Der Ordner wird beim ersten Start angelegt. Die Compose-Datei der Installation braucht dafür **keine** Änderung. Sicherungen des Volumes enthalten den Schlüssel jetzt mit; **ohne diese Datei sind die
  verschlüsselten Werte in der Datenbank (zweite Faktoren) nicht zu lesen**: Datei und Datenbanksicherung gehören zusammen aufbewahrt.
- Die Datenbankverbindung bleibt `ConnectionStrings__JuNotes`; die Basis liest sie, wenn `BASE_DB` fehlt.
- `Admin__Password`, `Web__Password` und ihre Umgebungswerte werden nicht mehr gelesen und können später aus der Compose-Datei verschwinden.
- Der Port, die Gesundheitsprüfung (`/health/live`) und der Reverse-Proxy bleiben. Die Basis glaubt `X-Forwarded-*` von privaten Netzen; die Einstellung „vertrauenswürdige Proxys“ ist in der App einstellbar.

**Was beim ersten Start passiert:** Vor der ersten Migration läuft die automatische Sicherung (`Backup__BeforeMigration`, schlägt sie fehl, startet der Server nicht). Dann: die Tabelle `instance_settings` heißt `app_instance_settings`;
die Basis legt ihre Tabellen an (`users`, `sessions`, … und `base_migrations`); Migration 8 fügt `entities.owner_id` und die Tabelle `assets` hinzu. **Keine Zeile wird verändert.** Im Log steht
„FIRST SETUP … setup code: XXXX-XXXX-XXXX“.

**Was der Eigentümer einmal tun muss:** Adresse des Servers öffnen, den Einrichtungscode aus dem Log eingeben (`docker compose logs junotes`), Benutzername und Passwort wählen (mindestens 12 Zeichen), den zweiten Faktor
einrichten (Authenticator-App, sechs Ziffern), die Wiederherstellungscodes sichern. Das erste Konto ist der Administrator. Danach gehören alle bisherigen Hefte und Dateien diesem Konto.

**Was passiert, solange er es nicht getan hat:** Die iPad-App gleicht **weiter ab**, mit demselben Token, mit denselben Antworten (Vertragstests; Altstand-Probe unten). Die Zeilen, die dabei neu entstehen, gehören niemandem und gehen
beim ersten Konto mit an den Administrator. **Nicht erreichbar sind bis dahin:** die Seiten `/Notes` und `/Admin` (sie schicken zur Einrichtung), also auch die Codex-Anmeldung und die Sicherungsseite im Browser. Wer die Adresse und
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
| `/Notes`, `/Notes/{id}`, `/Notes/Asset/{id}` | Sitzung, Rolle `admin` oder `member`, nur eigene Zeilen | `Admin/AdminSetup.cs` (`BaseSessionHandler`, Richtlinie `Reader`), `LibraryReader.Of(person)` | `WebReaderTests.*`, `OwnerTests.The_notebooks_of_the_reader_…`, `AdminTests.An_ended_session_…` |
| `/Admin/**` | Sitzung, Rolle `admin` | dieselbe Stelle, Richtlinie `Admin` | `AdminTests.A_member_reads_notes_but_does_not_open_the_admin_area_…` |
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
| Die alten Seiten bekommen nur `style-src-attr 'unsafe-inline'` zusätzlich, überall sonst gilt die Richtlinie der Basis unverändert | `AdminTests.The_old_pages_get_only_the_one_relaxation_…` |
| Ein verformter Stapel bleibt 400 und speichert nichts | `ApiContractTests.Push_refuses_a_malformed_batch_…` |

**Bekannte Lücken und Grenzen (ohne Beschönigung):**
- Der App-Token ist weiter **ein gemeinsamer Schlüssel** für das Konto des ersten Administrators. Wer ihn hat, liest und schreibt dessen Hefte. Das war vorher nicht anders; Token je Gerät und Person kommen mit Meilenstein 3.
- `owner_id` hat keinen Fremdschlüssel; Zeilen einer gelöschten Person bleiben liegen.
- Ein `parent_id`, das **noch nicht existiert**, wird angenommen. Legt später eine andere Person eine Zeile mit dieser id an, bleiben beide Zeilen in ihrer Hand, die Filter nach Besitzer verhindern das Mitlesen.
  Geprüft sind die Regeln nur gegen **vorhandene** fremde Zeilen.
- Die Drossel des Sicherheitsprotokolls steht im Speicher (ein Neustart setzt sie zurück); das Schreiben ist „abschicken und weitermachen“ (ein Fehler wird nur ins Log geschrieben).
- Die Zuweisung geschieht beim ersten Zugriff nach dem ersten Konto (Abweichung 6), die Ermittlung des ersten Administrators liest die Tabelle `users` der Basis mit eigenem SQL (Kopplung an deren Schema).
- Die Einrichtungsseite ist offen, bis das erste Konto existiert (Entwurf der Basis); der Code steht im Log.
- Die Seiten `/Notes` und `/Admin` sind alte Razor-Seiten mit Anti-Forgery; ihre Inhalte (`Html.Raw` des gezeichneten SVG) sind wie vorher.
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
