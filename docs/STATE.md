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

**Scheibe 3a (Gerätetoken und Kopplung), 3c (Freigaben im Web) und 3d (Geräte in der Verwaltung, gemeinsamer Token abschaltbar) sind gebaut und lokal committet; siehe die Abschnitte „Meilenstein 3, Scheibe …“ und die Listen „Für die Sicherheitsprüfung (3a)“, „(3c)“ und „(3d)“. Als Nächstes laut Orchestrator: 3b klein (Löschprüfung); die Sicherheitsprüfung liest 3a und 3c zusammen; die App-Änderung (ARCHITECTURE 11) bleibt ungebaut, bis das Einspielen von 1 und 2 durch ist. Das Einspielen von Meilenstein 1 und 2 steht weiter aus (Sache des Orchestrators, Liste „Für das Einspielen“).** Zuvor gilt: **Meilenstein 1 und 2 sind fertig und von zwei Sicherheitsprüfungen gelesen; als Nächstes kommt das Einspielen, danach Meilenstein 3** (Entwurf in `docs/ARCHITECTURE.md` 10 und 11; Scheiben 3a, 3c, 3d, 3b klein; die Übergabe von Heften ist zurückgestellt). **Meilenstein 1 und 2 sind fertig** (1a, 1b, 1c; lokal committet, nichts gepusht, kein Release). Der Orchestrator prüft, die Sicherheitsprüfung (eigene Sitzung) liest 1a und 1b vor dem Einspielen
(Liste unten). **Meilenstein 2 ist gebaut** (siehe unten); Meilenstein 3 beginnt erst nach den Behebungen aus der zweiten Sicherheitsprüfung und dem Startzeichen des Orchestrators. Das Einspielen auf die laufende Installation ist seine Sache, mit Sicherung davor und dem Ja des Eigentümers.

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
   gewandert (`wwwroot/legacy-*.css|js`); nur für `/Notes` und `/Admin` wurde zusätzlich `style-src-attr 'unsafe-inline'` erlaubt (Farben von Covern). Skripte blieben auf `'self'`. **Mit den Razor-Seiten am Ende von Meilenstein 2 weggefallen**
   (jetzt gilt überall die Richtlinie der Basis; Test: `DoorsTests.What_the_web_api_gives_is_never_kept_…`).
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

## Meilenstein 3, Scheibe 3a: Gerätetoken und Kopplung (25.11.2026, nur der Server und die Seite im Web; lokal committet, nichts gepusht)

Der Stand davor ist als lokaler Tag `m2-geprueft` markiert (letzter Commit vor 3a: `0859332`). Entwurf: ARCHITECTURE 10.1 (dort steht kursiv, wo der Bau vom Entwurf abweicht) und 11 (die App-Änderung, **nicht gebaut**).

**Was gebaut ist:**
- **Migration `0009_devices.sql`:** `device_tokens` (nur der SHA-256 des Tokens, `revoked_at`, `last_seen` höchstens einmal je Minute) und `pairing_codes` (SHA-256, `expires_at`, eindeutig je Person). Kein Fremdschlüssel zu den Personen (wie `owner_id`).
- **`DeviceStore`** (`Admin/DeviceStore.cs`): Code erzeugen (10 Zeichen Base32 = 50 Bit, zehn Minuten, ein gültiger Code je Person), Code einlösen (alle gültigen Codes werden in fester Zeit verglichen; der Treffer wird mit `delete … returning` verbraucht, zwei gleichzeitige Anfragen bekommen höchstens ein Token), Token suchen, auflisten, entfernen.
- **Die Tür `/api/v1`:** (1) `POST /api/v1/pair` ist die **einzige** Ausnahme außer `/status`: genau dieser Pfad (Ordinal, keine andere Schreibweise, kein Schrägstrich am Ende), nur `POST`; jede Ablehnung hat dieselbe Antwort (401, ein Körper), nur die Bremse antwortet 429; nur über https (nach den vertrauenswürdigen Proxys) oder `localhost` von der Maschine selbst; Körper höchstens 2 KB, selbst gelesen. (2) Ein Token mit `nt1_` wird zuerst unter den Gerätetoken gesucht (nicht entfernt, Person aktiv), sonst gilt die alte Prüfung mit dem Instanz-Token (unverändert). Das Gerät steht für **seine** Person, auch ein Mitglied; `SyncOwner.Device` ist der Gerätename, also nennt das Sicherheitsprotokoll das Gerät.
- **Bremse für die Kopplung:** drei falsche Codes von einer Adresse sperren die Adresse bis zum Ende der Minute, **auch für einen richtigen Code** (429); eine Zeile `api.pair_refused` je Adresse und Stunde.
- **Web `/api/web/v1/devices`** (Sitzung, nur eigene Geräte): `GET` (Name, gekoppelt am, zuletzt gesehen, Adresse gekürzt), `POST /pairing` (frischer Code des zweiten Faktors, wird verbraucht, falsche zählen mit der Bremse der Basis für das Konto; Hub-Personen ohne eigenen zweiten Faktor: 409 `pairing.needs_totp`), `DELETE /{id}` (fremde oder unbekannte id: 404). Protokollzeilen `pairing.code_made`, `device.paired`, `device.removed`.
- **Seite:** Karte „Gerät koppeln“ in den Einstellungen (`pages/devices.js`, Texte de/en): Liste der Geräte mit „Entfernen“ (Rückfrage), Eingabe des Codes, danach der Kopplungscode groß, als QR-Code (`<Adresse>/#pair=<CODE>`) und mit Rückwärtszähler; die Seite schaut alle 4 Sekunden in die Liste und beendet die Anzeige, sobald ein neues Gerät da ist. Bei `http` (außer `localhost`) steht ein Hinweis statt des Formulars. Im Browser angesehen: breit hell, 390 px dunkel; Kopplung mit `curl` gegen den lokalen Server, Entfernen mit Rückfrage; keine Fehler in der Konsole.

**Nicht gebaut in 3a (bewusst):** die App-Änderung (Abschnitt 11, auch das zusätzliche Feld `pairing` in `/api/v1/status`, das erst mit ihr kommt), das Abschalten des gemeinsamen Tokens und die Geräteübersicht des Administrators (3d), die Löschprüfung (3b); Freigaben (3c) kamen danach. **Der gemeinsame Token gilt unverändert weiter.**

**Tests:** `DeviceTests` (21, davon 2 ohne Datenbank; der Test des Zählers der Bremse gibt ihr eine unerreichbare Datenbank für die Protokollzeile); der Testhost kann mit `X-Test-Remote` als eine andere Adresse sprechen (`AdminHost`). **Gesamtlauf nach 3a: 400 von 405 grün; rot sind dieselben fünf Backup-Tests wie vorher (`pg_dump` 17 gegen Server 18, in der CI grün).** Geprüft, dass sie beißen: mit einer Schreibweise ohne Rücksicht auf Groß- und Kleinschreibung und ohne https-Prüfung schlagen zwei Tests fehl.

### Für die Sicherheitsprüfung (3a)

| Tür oder Regel | Was gilt | Wo der Code steht | Wo der Test steht |
|---|---|---|---|
| `POST /api/v1/pair` | ohne Token offen, aber **nur** dieser Pfad und nur `POST`; alles andere (`GET`, `PUT`, `HEAD`, `DELETE`, `PATCH`, `OPTIONS`; `/API/V1/PAIR`, `/api/v1/pair/`, `/pair/x`, `/pairs`, `//`, `%2F`) bleibt 401, und ein Code wird dabei nie berührt | `V1Door.IsPairRequest`, `PairAsync` | `DeviceTests.Only_exactly_POST_api_v1_pair_is_open_…` |
| Nur über https | über http (auch mit `X-Forwarded-Proto: https` von einer Adresse, der der Server nicht traut): abgelehnt, **Code bleibt unverbraucht**, zählt nicht für die Bremse; mit dem Kopf von einem vertrauenswürdigen Proxy oder von `localhost` (Loopback): geht | `V1Door.ArrivedSecurely` | `…Pairing_is_refused_over_http_…` |
| Antwort verrät nichts | nie gewesen, abgelaufen, benutzt, über http, leerer Körper: gleicher Status, gleicher Körper (bis auf die Anfragekennung), gleicher Inhaltstyp | `PairRefused` | `…The_answer_is_the_same_…` |
| Ein Code, einmal | zweite Einlösung 401; sechs gleichzeitige Einlösungen: ein Token; ein neuer Code ersetzt den alten | `DeviceStore.RedeemAsync`, Index `pairing_codes_person` | `…A_code_works_exactly_once_…`, `…Two_requests_with_the_same_code_at_once_…` |
| Zehn Minuten | bis zur letzten Sekunde, danach nicht; abgelaufene werden beim nächsten Erzeugen gelöscht | `DeviceStore` (Uhr: `TimeProvider`) | `…A_code_works_for_ten_minutes_…` (mit `ManualTime`) |
| Nur Hashes | in der Datenbank steht weder Code noch Token im Klartext; ein verbrauchter Code ist **gelöscht**; die Antwort hat genau vier Felder | `DeviceStore` | `…Only_the_hash_of_a_token_and_of_a_code_is_stored_…` |
| Bremse | drei falsche Codes sperren die Adresse für den Rest der Minute, auch für den richtigen Code (429, `Retry-After`); andere Adressen sind frei; eine Protokollzeile ohne Code | `TokenBrake.PairFailed`, `PairWait` | `…Three_wrong_codes_…`, `…The_pairing_brake_locks_on_the_third_…` (mit `ManualTime`) |
| Körper | über 2 KB, kein JSON, `null`, Feld mit falscher Art: wie ein falscher Code | `ReadPairRequestAsync` | `…A_body_that_is_too_large_or_not_ours_…` |
| Gerätetoken öffnet nur `/api/v1` | an `/api/web/v1/**`, `/api/people`, `/api/auth/**`, `/admin`: 401; eine Sitzung öffnet nichts unter `/api/v1` | `V1Door.CheckAsync` | `…A_device_token_opens_nothing_outside_api_v1_…` |
| Gerät und Person | jedes Gerät schreibt und liest nur für **seine** Person (Zeilen, Pull, Dateien); ein fremdes Gerät kann eine fremde Zeile nicht überschreiben (die Zeile bleibt, `accepted` 0), eine fremde Datei nicht finden; der alte Token steht weiter für den ersten Administrator | `OwnerDirectory.ForDeviceAsync`, `SyncOwner` | `…A_device_writes_and_reads_only_for_its_person_…` (zwei Personen, zwei Geräte, alter Token) |
| Entfernen | ein entferntes Gerät bekommt sofort 401 (keine Zwischenspeicherung), das andere Gerät der Person geht weiter; eine Person kann nur eigene Geräte entfernen (fremd und unbekannt: gleiche 404); die Liste zeigt nur Eigenes, nie Token oder Hash | `DeviceStore.RemoveAsync`, `DevicesApi` | `…A_removed_device_gets_401_at_once_…` |
| Gesperrte Person | ihr Gerät bekommt 401, ein Code von ihr gibt kein Token (und ist verbraucht); die Geräte der anderen gehen weiter | `DeviceStore.FindAsync`, `RedeemAsync` | `…The_device_of_a_blocked_person_…`, `…A_code_of_a_person_who_has_been_blocked_…` |
| Falsche Gerätetoken | 401, zählen wie jedes falsche Token (Bremse je Adresse), ein richtiges geht von der gesperrten Adresse trotzdem durch | `V1Door.CheckAsync` | `…A_wrong_device_token_is_401_…` |
| Code erzeugen | nur mit Sitzung **und** frischem Code des zweiten Faktors (der verbraucht wird); falsch: 403 `totp.wrong`; fünf falsche sperren das Konto für das Koppeln (und, weil derselbe Zähler, auch für die Anmeldung) | `DevicesApi` (`/pairing`) | `…A_code_needs_a_fresh_code_of_the_second_factor_…`, `…Wrong_codes_of_the_second_factor_lock_…`, `…A_person_without_a_second_factor_here_…` |
| Anonym | jeder Weg unter `/api/web/v1` (auch die neuen) ohne Sitzung: 401 | Wächter der Basis | `AdminApiTests.Only_an_administrator_gets_in_at_any_path` (liest die Endpunkte des Servers) |
| Protokoll | Verstoß gegen die Schreibregeln nennt Person **und Gerätename**, nie `app-token`; Kopplung, Code, Entfernen als Zeilen; nie Token oder Code | `SecurityLog`, `V1Door.PairAsync`, `DevicesApi` | `…The_security_log_names_the_device_…` |
| Alter Vertrag | `/api/v1` unverändert für den alten Token; Vertragstests unverändert grün | – | `ApiContractTests`, `DoorsTests`, `OwnerTests` |

**Bekannte Lücken und Grenzen von 3a (ohne Beschönigung):**
- **Die Zeit der Antworten ist nicht gemessen.** Für nie gewesen, abgelaufen und benutzt macht der Server dieselben Abfragen (Liste der gültigen Codes, Suche der Person mit einer Kennung, die es nicht gibt), aber ein Zeittest fehlt. Ein Code einer **gesperrten** Person braucht eine Abfrage mehr als ein unbekannter (nur wer einen gültigen Code erraten hat, kann es sehen).
- **Ablehnung über http, zu großer Körper und Bremse** antworten schneller als die übrigen, weil sie die Datenbank nicht berühren; sie hängen nicht vom Code ab. Die Bremse antwortet 429 statt 401: das verrät nur, dass die Adresse gesperrt ist.
- **Der https-Test hängt an `ProxyTrust`.** Solange in der Installation keine Proxys eingetragen sind, glaubt der Server jedes private Netz (so ist die Basis): Ein Gerät im selben Netz, das den Port **direkt** erreicht, kann `X-Forwarded-Proto: https` setzen und die Prüfung umgehen. Das schützt nur die Vertraulichkeit des Codes auf dem Weg, nicht vor dem Erraten (die Bremse gilt unabhängig). Abhilfe: die Proxys eintragen und den Port nur dem Proxy zugänglich machen (steht in der Liste „Für das Einspielen“). Eine Verbindung ohne Adresse (Unix-Socket) gilt für die Ausnahme `localhost` als Loopback, aber nur, wenn der Kopf `Host` `localhost` heißt.
- **Der Code ist ein Geheimnis mit 50 Bit und nur ein ungesalzener SHA-256 in der Datenbank:** Wer die Datenbank in den zehn Minuten liest, kann ihn offline durchprobieren (Größenordnung von Stunden auf einer starken Grafikkarte, also **nach** Ablauf des Codes); danach ist er wertlos. Der Gerätetoken hat 256 Bit und ist unproblematisch.
- **Keine Obergrenze für die Zahl der Geräte je Person** (jede Kopplung braucht aber einen frischen zweiten Faktor, also höchstens ein Gerät je 30 Sekunden). Entfernte Zeilen bleiben für das Protokoll in der Tabelle.
- **Hub-Personen** ohne eigenen zweiten Faktor können in 3a **nicht koppeln** (409 mit einem Satz); wie sie es dürfen (zum Beispiel eine frische Anmeldung am Hub), ist eine offene Entscheidung. Die einzige Person, die heute hier ist, meldet sich mit Passwort und zweitem Faktor an.
- **Ein Fehlversuch beim zweiten Faktor auf der Kopplungsseite zählt mit für die Anmeldung** (derselbe Zähler der Basis): wer sich fünfmal vertippt, wartet 30 Sekunden mehr. Gewollt, aber eine Folge, die die Person spüren kann.
- **Nicht geprüft:** die App (die Kopplung ist nur mit `curl` und in Tests gegangen), das **Scannen** des QR-Codes (nur das Bild im Browser gesehen, nicht mit einer Kamera gelesen), https und Proxy im echten Netz, hell im schmalen Fenster, Bedienung mit Bildschirmleser, die Seite im Safari. Das Feld `pairing` in `/api/v1/status` fehlt noch (kommt mit der App).
- Gerätenamen und Adressen sind Text der Person beziehungsweise des Netzes: Steuerzeichen werden entfernt, höchstens 60 Zeichen, die Seite setzt sie als Text (nie als HTML). Gespeichert wird die letzte Adresse des Geräts (Datenschutz: sie steht nur in der Liste der Person, gekürzt).

## Meilenstein 3, Scheibe 3c: Freigaben im Web (25.11.2026, lokal committet, nichts gepusht)

Entwurf: ARCHITECTURE 10.2 (kursiv, wo der Bau abweicht). **Ein Heft, nur lesen, nur im Web:** der Abgleich ändert sich nicht; das iPad der Empfängerin bekommt freigegebene Hefte erst mit Stufe 2.

**Was gebaut ist:**
- **Migration `0010_shares.sql`:** `shares(id, notebook_id, owner_id, recipient_id, mode, created_at, revoked_at)`; `mode` hat eine `check`-Bedingung auf `read` (die Tabelle kennt keinen anderen Modus); höchstens eine gültige Freigabe je Heft und Person (teilweise eindeutiger Index); Entziehen setzt `revoked_at`, Neuteilen macht eine neue Zeile.
- **`ShareStore`** (`Backend/Library/ShareStore.cs`): fragt bei **jeder** Anfrage die Tabelle, nichts wird behalten. `FindAsync` (Freigabe für Person und Heft; das Heft muss noch der Besitzerin gehören), `FindForPageAsync` (die Seite muss in einem freigegebenen Heft liegen **und** der Besitzerin gehören), `AssetInNotebookAsync` (Datei kommt als Cover, als Scan oder PDF einer Seite oder als Bild, Sticker oder Aufnahme auf einer Seite des Hefts vor; Zeilen im Papierkorb und gelöschte zählen nicht; alle Zeilen müssen der Besitzerin gehören).
- **Wege** (`Api/SharesApi.cs`, hinter der Sitzung): Empfängerin: `GET /shared`, `/shared/notebooks/{id}`, `…/pages`, `/shared/pages/{id}`, `/shared/assets/{id}`; Besitzerin: `GET`/`POST /notebooks/{id}/shares`, `DELETE /shares/{id}`; alle: `GET /people` (nur Name und id der anderen aktiven Personen, die sich anmelden können; Entscheidung des Orchestrators). Die Seite wird über den **Leser der Besitzerin** gelesen, also so wie sie sie selbst sieht, aber nur durch die Freigabe dieses einen Hefts. Das Bild einer Seite lädt Dateien vom Pfad `/shared/assets/…` (Erlaubnisliste in `SvgAllowList.cs` und `pages/util.js`: `(shared/)?assets`).
- **Teilen:** nur die Besitzerin; ein Heft, das es nicht gibt, nicht ihr gehört oder im Papierkorb liegt, ist 404 (auch für `{recipientId}` = sie selbst, wenn das Heft nicht ihr gehört: das Heft wird zuerst geprüft); an sich selbst: 400 `share.self`; Person unbekannt, gesperrt oder noch nicht eingerichtet: 404 `share.person`; zweimal an dieselbe Person: dieselbe Freigabe (200).
- **Protokollzeilen** `share.created` und `share.revoked`: Person, **Name des Gegenübers und Heft-id**, kein Titel, kein Inhalt.
- **Seite:** „Teilen“ am eigenen Heft (Dialog: wer hat es, „Entziehen“, Person wählen; er schließt sich beim Seitenwechsel), „Mit mir geteilt“ in der Bibliothek (Kacheln mit „von Name“), geteiltes Heft und geteilte Seite unter `#/shared/notebook/…` und `#/shared/page/…` mit dem Hinweis „Du kannst es nur lesen“ (kein „Teilen“, kein Weg zum Schreiben). Im Browser angesehen (als `viewer1`, mit einem von Hand angelegten Heft einer zweiten Person und einer Freigabe): breit hell und 390 px dunkel, Teilen, Entziehen, Lesen von Heft und Seite mit Strich; keine Konsolenfehler.

**Nicht gebaut in 3c (bewusst):** Freigabe von Ordnern; Suche in geteilten Heften (die Suche liest nur Eigenes); „Freigabe verlassen“ durch die Empfängerin; eine Übersicht aller Freigaben der Besitzerin und eine Meldung an die Empfängerin; das Löschen von Freigaben beim Löschen einer Person (3b).

**Tests:** `SharesTests` (17; drei Personen: Ada besitzt, Bea bekommt, Cleo ist Dritte). **Gesamtlauf nach 3c: 421 Tests, 415 grün; rot sind dieselben fünf Backup-Tests (`pg_dump` 17 gegen Server 18).** Ein Test von 3a (`DeviceTests`) schlug im ersten Gesamtlauf einmal fehl, weil ein Zeitschritt des zweiten Faktors mitten im Test wechselte; die Hilfsfunktion nimmt jetzt immer den ältesten noch gültigen, noch nicht benutzten Schritt (danach 37 von 37 grün in `DeviceTests` und `SharesTests`). Geprüft, dass sie beißen: ohne die Bedingung „Datei gehört der Besitzerin“ und ohne die Prüfung auf `revoked_at` schlagen drei Tests fehl.

### Für die Sicherheitsprüfung (3c; zusammen mit 3a)

| Tür oder Regel | Was gilt | Wo der Code steht | Wo der Test steht |
|---|---|---|---|
| Lesen durch eine Freigabe | `shared/**` öffnet nur Hefte, die mit der Person **jetzt** geteilt sind; jedes andere Heft derselben Besitzerin, ihre Seiten, Dateien, Ordner und fremde ids: 404; eine Dritte: überall 404; die Besitzerin selbst über die Wege der Empfängerin: 404 | `SharesApi`, `ShareStore.FindAsync`/`FindForPageAsync` | `SharesTests.Bea_reads_the_shared_notebook_…`, `…A_share_opens_one_notebook_and_not_another_…` |
| Dateien | nur wenn sie im geteilten Heft vorkommen **und** der Besitzerin gehören; weder lose Dateien noch Dateien nur eines anderen Hefts noch die Datei einer Dritten, die das Heft nennt | `ShareStore.AssetInNotebookAsync`, `AssetService.OpenAsync(id, besitzerin)` | `…A_share_opens_one_notebook_…`, `…A_file_that_the_shared_notebook_names_but_that_belongs_to_somebody_else_…` |
| Fremde Zeilen unter dem Heft | eine Zeile einer Dritten, die unter die id eines (damals noch nicht vorhandenen) Hefts geschrieben wurde, wird weder gelistet noch geöffnet, ihre Dateien werden nicht gegeben (alle Abfragen verlangen `owner_id` der Besitzerin) | `ShareStore`, `LibraryReader` mit Besitzerin | `…A_stray_row_of_somebody_else_under_the_shared_notebook_…` |
| Entzug | wirkt bei der nächsten Anfrage auf allen Wegen; Empfängerin und Dritte können nichts entziehen (404 wie für eine unbekannte id); Neuteilen geht | `ShareStore.RevokeAsync` (nur mit `owner_id`) | `…Taking_a_share_back_works_with_the_next_request_…` |
| Papierkorb und Löschen | ein Heft im Papierkorb oder gelöscht ist für die Empfängerin weg und nicht in ihrer Liste; aus dem Papierkorb geholt, ist die Freigabe wieder da (siehe Grenzen); eine Seite im Papierkorb samt einer Datei, die nur sie brauchte, ebenso weg | `LibraryReader` (Besitzerin), `ShareStore` | `…A_notebook_in_the_trash_or_deleted_…`, `…A_page_in_the_trash_…` |
| Wer teilen darf | nur die Besitzerin, nur ein Heft (nicht Ordner, Seite, Strich), nicht im Papierkorb, nicht mit sich selbst, nur mit einer aktiven Person; eine Nicht-Besitzerin bekommt immer 404, auch beim Teilen mit sich selbst | `ShareStore.CreateAsync` | `…Only_the_owner_shares_…`, `…The_owner_sees_who_has_the_notebook_…` |
| Weitergeben | eine Empfängerin kann das Heft weder weiterteilen noch die Freigaben ansehen | wie oben | `…With_three_people_each_reads_what_was_shared_with_them_…` |
| Abgleich unverändert | der Pull der Empfängerin liefert nichts Geteiltes; ihr Push kann das geteilte Heft nicht ändern; es gibt unter `shared/**` nur `GET`; die Tabelle kennt nur den Modus `read` | `SyncService` (unverändert), `0010_shares.sql` | `…The_sync_of_the_recipient_…`, `…A_share_can_only_read_…` |
| Tür | App-Token und ohne Sitzung: 401 für alle neuen Wege; jeder Weg unter `/api/web/v1` (auch die neuen) ohne Sitzung 401 | Wächter der Basis | `…The_app_token_and_a_device_token_open_nothing_of_the_shares_…`, `AdminApiTests.Only_an_administrator_gets_in_at_any_path` |
| Namensliste | nur `id` und `name`, nur aktive Personen, ohne die Fragende; nur mit Sitzung | `ShareStore.PeopleAsync` | `…The_names_list_gives_name_and_id_…` |
| Protokoll | `share.created`, `share.revoked`: Person, Gegenüber, Heft-id; nie ein Titel oder Inhalt | `SharesApi` | `…Sharing_and_taking_back_are_written_…` |
| SVG-Erlaubnisliste | `href` an `image` nur auf `/api/web/v1/assets/<guid>` oder `/api/web/v1/shared/assets/<guid>`; sonst fällt es weg | `SvgAllowList.cs`, `pages/util.js` | `…The_page_lets_the_shared_path_…`, `SvgAllowListTests` |

**Bekannte Lücken und Grenzen von 3c (ohne Beschönigung):**
- **Eine Freigabe lebt mit dem Heft:** Legt die Besitzerin das Heft in den Papierkorb und holt es zurück, haben die Empfängerinnen wieder Zugriff (die Zeile `shares` bleibt gültig). Das ist Absicht (Entziehen ist ein eigener Schritt). **Damit sie es weiß** (Entscheidung des Orchestrators): Die Seite des eigenen Hefts sagt immer „Dieses Heft ist geteilt mit …“ und den Satz, dass sie nach dem Zurückholen wieder dabei sind. Der Papierkorb und das Zurückholen gibt es nur auf dem iPad (die Web-App hat keinen Papierkorb, die iPad-App ändert sich nicht), also steht der Satz dort, wo sie das Heft im Web öffnet, **nicht im Augenblick des Zurückholens**.
- **Die Freigaben einer gesperrten Besitzerin sind zu** (Entscheidung des Orchestrators, umgesetzt: eine Bedingung `users.state = 'active'` in denselben drei Abfragen; Test `SharesTests.The_shares_of_a_blocked_owner_are_closed_…`): alle Wege unter `shared/**` für ihre Hefte antworten 404, die Freigaben bleiben als Zeilen bestehen und gelten nach dem Entsperren wieder.
- **Der Inhalt eines geteilten Hefts ist alles, was die Besitzerin sieht:** Überschriften, gedruckter Text, **Lösung und Ergänzungen** der Seiten, Zeichnungen, Dateien. Es gibt keine Auswahl; der Teilen-Dialog sagt es in einem Satz.
- **Die Namensliste zeigt jeder Person alle anderen Personen der Installation** (Entscheidung des Orchestrators, eine Familie). Bei einem größeren Kreis wäre es eine Einstellung.
- **Schreibt eine Dritte eine Zeile unter eine noch nicht vorhandene Eltern-id** (bekannte Lücke aus 1b), bleibt die Zeile bei ihr; die Freigabe zeigt sie nicht (getestet). Weiterhin ungeklärt ist, was die Besitzerin in ihrer App dann sieht.
- **Dateien werden über den JSON-Text der Zeilen gefunden** (`assetID`, ohne Rücksicht auf Groß- und Kleinschreibung). Eine Zeile mit anderer Schreibweise des Schlüssels würde nicht gefunden (die Datei bliebe dann unzugänglich, nie zu offen).
- **Nicht geprüft:** das Lesen als zweite angemeldete Person im Browser (ich habe als `viewer1` gelesen, mit Zeilen und Freigabe, die ich in der Wegwerfdatenbank von Hand angelegt habe; die Wege selbst sind mit Sitzungen von drei Personen getestet), Safari, Bedienung mit Bildschirmleser, hell im schmalen Fenster, große Hefte (die Seitenliste und die Dateisuche lesen je Anfrage die Zeilen des Hefts).
- **Der zeitabhängige Test ist ersetzt:** `SecurityFindingsTests.The_claim_finishes_although_…` gab die Anfrage nach 250 ms auf und war unter Last rot. Jetzt wartet er, bis die Zuweisung auf dem Server läuft (`pg_stat_activity`), und bricht dann ab; die Uhr spielt keine Rolle mehr. Geprüft, dass er beißt: mit dem Abbruchzeichen der Anfrage statt dem des Programmendes schlägt er fehl.

## Meilenstein 3, Scheibe 3d: Geräte in der Verwaltung, der gemeinsame Token (25.11.2026, lokal committet, nichts gepusht)

Entwurf: ARCHITECTURE 10.1 („Ablösen des alten Tokens“, „Verwaltung der Geräte“).

**Was gebaut ist:**
- **Migration `0011_shared_token.sql`:** `shared_token_seen(address, first_seen, last_seen)`: Wer den gemeinsamen Token benutzt, wird nur als **Adresse** (wie der Server sie sieht, IPv6 auf das Netz gekürzt) mit erstem und letztem Mal festgehalten, höchstens einmal je Minute je Adresse, Zeilen älter als 30 Tage werden beim Schreiben gelöscht. Kein Token, kein Pfad, kein Inhalt.
- **`SharedToken`** (`Admin/SharedToken.cs`): der Schalter steht in der Datenbank (`app_instance_settings`, Schlüssel `shared_token_off`), nicht in der Umgebung, und wird bei **jeder** Anfrage mit dem gemeinsamen Token gelesen. Ist er aus, antwortet die Tür auch dem **richtigen** Token mit 401, **ohne** es als falsches Token zu zählen (es ist kein Angriff; die Bremse sperrt die Adresse nicht) und mit einer Protokollzeile `api.token_switched_off` je Adresse und Stunde, damit ein vergessenes Gerät auffällt. Geräte mit eigenem Token und `/api/v1/status` sind nicht betroffen.
- **`FreshSecondFactor`:** der frische, verbrauchte Code des zweiten Faktors, jetzt ein Teil, den die Kopplung (3a) und der Schalter gemeinsam benutzen (gleiche Zählung der Fehlversuche wie bei der Anmeldung).
- **Wege** (Rolle `admin`, `Api/DevicesAdminApi.cs`): `GET /admin/devices` (je Person **nur die Anzahl**, ohne Namen, Adressen, Token), `POST /admin/people/{id}/devices/revoke` (alle Geräte einer Person; Protokollzeile `device.revoked_by_admin` mit Anzahl und Name der Person), `GET /admin/shared-token` (`configured`, `enabled`, Zahl der Adressen der letzten 30 Tage, letzte Benutzung), `POST /admin/shared-token/off` und `/on` (frischer Code des zweiten Faktors des Administrators; Protokollzeilen `sharedtoken.off` mit der Zahl der Adressen und `sharedtoken.on`). **Einschalten geht nur, solange die Umgebung den Token noch trägt** (sonst 409 `sharedtoken.not_configured`, und der Code wird nicht verbraucht).
- **Seite:** Verwaltung › Reiter „Geräte“ (`devicesPage`): Karte „Gemeinsamer Token der App“ mit Zustand (an, aus, nicht gesetzt), **Zahl der Adressen der letzten 30 Tage vor dem Abschalten**, Hinweis „Eine Adresse ist nicht genau ein Gerät“, Dialog mit dem Satz, was danach geschieht, und dem Code; Karte „Geräte der Personen“ mit der Anzahl und „Alle widerrufen“ (mit Rückfrage). Im Browser gezeigt und bedient (hell, breit): Abschalten mit echtem Code, danach 401 mit dem richtigen Token und 200 für `/status`, Wiedereinschalten, Protokollzeilen.

**Abweichung vom Entwurf, mit Grund:** 10.1 sagt „der Administrator sieht je Person nur die **Anzahl** und kann **ein Gerät** widerrufen“. Ohne Namen lässt sich ein einzelnes Gerät nicht wählen, und der Orchestrator wollte „nur die Anzahl“; gebaut ist deshalb „**alle Geräte einer Person widerrufen**“ (ein verlorenes iPad: die Person koppelt die übrigen neu). Ein Widerruf einzelner Geräte durch den Administrator gibt es nicht; die Person selbst kann einzelne entfernen (3a).

**Tests:** `DevicesAdminTests` (7). **Gesamtlauf nach 3d: 429 Tests, 424 grün; rot sind dieselben fünf Backup-Tests (`pg_dump` 17 gegen Server 18).** Der Testhost kann den gemeinsamen Token aus der Umgebung weglassen (`AdminHost.SharedToken`); `TestSecondFactor` liefert Codes des zweiten Faktors unabhängig vom Zeitschritt. Geprüft, dass sie beißen: ohne die Prüfung des Schalters in der Tür und ohne den frischen Code schlagen drei Tests fehl.

### Für die Sicherheitsprüfung (3d)

| Tür oder Regel | Was gilt | Wo der Code steht | Wo der Test steht |
|---|---|---|---|
| Rolle | alle neuen Wege unter `/admin`: ohne Sitzung 401, als `member` 403 | `DevicesAdminApi` (Gruppe `RequireRole("admin")`) | `DevicesAdminTests.*` (je ein Fall), `AdminApiTests.Only_an_administrator_gets_in_at_any_path` (liest die Endpunkte des Servers) |
| Anzahl, sonst nichts | die Antwort hat je Person genau `id`, `name`, `state`, `devices`; kein Gerätename, kein Token, keine Adresse | `DeviceStore.CountsAsync` | `…The_administrator_sees_how_many_devices_…` |
| Widerruf | alle Geräte der Person bekommen sofort 401, andere Personen nicht; unbekannte Person 404; Protokollzeile mit Anzahl und Name | `DeviceStore.RevokeAllAsync` | `…Revoking_all_devices_of_a_person_…` |
| Abschalten und Einschalten | nur mit frischem, verbrauchtem Code des zweiten Faktors des Administrators; falscher Code 403 `totp.wrong` ändert nichts; derselbe Code ein zweites Mal 403; Einschalten nur wenn die Umgebung den Token trägt (sonst 409, Code unverbraucht) | `FreshSecondFactor`, `DevicesAdminApi` | `…Switching_the_shared_token_off_needs_a_fresh_code_…`, `…Switching_it_on_again_…`, `…A_token_that_the_environment_does_not_carry_…` |
| Wirkung in der Tür | der richtige Token wird bei Abschalten auf allen Wegen mit 401 abgewiesen (`status` bleibt offen), Geräte laufen weiter, falsche Token zählen weiter für die Bremse | `V1Door.CheckAsync` | `…Switching_the_shared_token_off_needs_a_fresh_code_…`, `…A_right_token_that_is_switched_off_is_not_counted_…` |
| Nicht als Angriff gezählt | der abgeschaltete richtige Token sperrt die Adresse nicht und füllt das Protokoll nicht mit `api.token_refused`; eine Zeile `api.token_switched_off` je Adresse und Stunde, ohne Token | `SharedToken.Refused` | `…A_right_token_that_is_switched_off_is_not_counted_…` |
| Zählung der Adressen | nur Adresse und zwei Zeiten; nur der gemeinsame Token schreibt (nicht Geräte, nicht falsche Token); höchstens eine Zeile je Minute; über 30 Tage alte zählen nicht und werden gelöscht | `SharedToken.SeenAsync`, `StateAsync` | `…The_addresses_that_use_the_shared_token_are_counted_…` |
| Protokoll | `sharedtoken.off` (Zahl der Adressen), `sharedtoken.on`, `device.revoked_by_admin`; nie ein Token | `DevicesAdminApi` | `…Switching_it_on_again_…`, `…Revoking_all_devices_…` |

**Bekannte Lücken und Grenzen von 3d (ohne Beschönigung):**
- **„Adressen“ sind keine Geräte.** Der gemeinsame Token hat kein Gerät dahinter; gezählt wird, von wo er kommt (ein Gerät kann im Mobilnetz und im WLAN auftreten, mehrere Geräte hinter demselben Router teilen eine Adresse). Die Zahl ist also eine Näherung und steht auch so auf der Seite. Hinter einem Proxy, dem der Server nicht traut (`ProxyTrust`), wäre es die Adresse des Proxys: dann zählt die Seite „1 Adresse“, ganz gleich wie viele Geräte; die Einspiel-Liste verlangt deshalb, die Proxys einzutragen.
- **Abschalten sperrt ein Gerät aus, das noch nicht gekoppelt ist,** und die App kann sich **noch nicht koppeln** (die Swift-Änderung ist nicht gebaut). Wer jetzt abschaltet, sperrt das iPad aus; die Seite sagt, was der Schalter bewirkt und wie viele Adressen ihn benutzt haben, aber nicht „die App kann noch nicht koppeln“. Das ist vor dem Abschalten zu klären.
- **Der Schalter wird bei jeder Anfrage mit dem gemeinsamen Token gelesen** (eine Abfrage mehr, keine Zwischenspeicherung): ein Abschalten wirkt sofort, kostet aber eine Abfrage je Anfrage der App.
- **Der Widerruf aller Geräte einer Person ist nicht rückgängig zu machen** (die Zeilen bleiben, `revoked_at` wird nie gelöscht); die Person koppelt neu.
- **Nicht geprüft:** Safari, schmales Fenster, dunkel und Bildschirmleser für die neue Seite; der Reiter „Geräte“ in einer Installation mit vielen Personen (keine Seitenteilung der Personenliste: sie ist kurz).

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
- Eigentümer, 25.11.2026 (über den Orchestrator): **Einspielen von Meilenstein 1 und 2: Ja** (mit dem Tag `m2-geprueft`). **Fall B1:** Die Hefte auf dem iPad gehören einer anderen Person der Familie; diese schließt die Einrichtung ab
  und wird die erste Person mit der Rolle Administrator, der Eigentümer lädt sich danach ein. „Person hinter dem alten Token ändern“ und die Übergabe von Heften werden **nicht** gebaut. **Stundenplan:** der derzeit beste Weg
  (Recherche beim Orchestrator); Erlaubnis klärt der Eigentümer; kein Aufruf gegen den echten Dienst, bevor er sie bestätigt hat und die betroffene Person sich selbst angemeldet hat, dann so wenige wie möglich.
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
| `SYNC_TOKEN` (`Sync__Token`) | gemeinsamer Token der iPad-App | 3, wenn die App Gerätetoken je Person schickt; **abschaltbar in der Verwaltung seit 3d** (Reiter Geräte, mit frischem zweitem Faktor), danach kann er aus der Umgebung verschwinden; einschalten geht nur, solange die Umgebung ihn trägt |
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
| – | Razor-Fangnetz | `AuthorizeFolder("/")` | **entfallen mit den Razor-Seiten** (die Wege prüft jetzt der Wächter der Basis; Tests: `DoorsTests`, `AdminApiTests`, `WebApiTests`) |
| – | Sicherungen ohne `secret.key` | Mit Passphrase geht der Schlüssel verschlüsselt (`….key.enc`) zu jeder Kopie, wird zusammen mit seiner Sicherung gelöscht und liegt nie im Klartext neben einem Dump; ohne Passphrase kommt er nicht hinein und die Sicherungsseite warnt deutlich | `BackupKeyTests` (4; mit einem Ersatz für `pg_dump`, laufen deshalb auch hier) |

**Nicht gefolgt / anders gelöst:** Bei Punkt 8 zählt die Bremse **falsche Versuche** mit dem `RateLimit` der Basis (es kann nur zählen, nicht nachsehen) und führt die Sperre selbst. Bei Punkt 5 wird die Migration **unserer** Folge über die Verbindung ohne Zeitgrenze
gefahren; die Skripte der Basis laufen über die normale Verbindung (kleine neue Tabellen).

**Offene Frage des Orchestrators an den Eigentümer** (nichts gebaut): Die Zuweisung gibt alle heutigen Hefte der Person, die die Einrichtung abschließt; der Besitzer ändert sich später nicht mehr.

## Sicherheitsprüfung 2: Behebungen von Meilenstein 1 und Meilenstein 2 (Befunde vom 25.11.2026, **behoben**)

Ergebnis der Prüfung: kein kritischer oder hoher Befund, kein Weg, auf dem ein Mitglied oder jemand ohne Zugang an Hefte, Seiten, Striche, Scans, Noten, Karten oder die Verwaltung kommt; die Behebungen aus der ersten Prüfung halten.
Alle Punkte sind nach dem Startzeichen des Orchestrators behoben, je Punkt ein Commit und, wo möglich, ein Test. Stand: 384 Tests, 379 grün (die 5 roten sind die Backup-Tests, die `pg_dump` 18 brauchen).

| Nr. | Befund | Behebung | Commit | Test |
|---|---|---|---|---|
| 1 | Falsche Token spülen das Sicherheitsprotokoll leer | höchstens eine Zeile je Adresse und Stunde (`TokenBrake`), Sperren und Zeilen werden stündlich vergessen | `ad4d6c1` | `SecurityFindingsTests.The_brake_writes_at_most_one_line_an_hour_…` |
| 2 | Die Sperre wies auch das richtige Token ab | **erst das Token prüfen, ein richtiges geht immer durch**; gezählt werden nur falsche; die Sperre ist nur noch eine Bremse für falsche | `ad4d6c1` | `…A_right_token_gets_through_from_an_address_that_sent_many_wrong_ones_…`, `…A_lock_hits_the_one_address_and_no_other` |
| 3 | Das Einsetzen des SVG war eine Verbotsliste | **Erlaubnisliste** auf dem Server (`SvgAllowList`: Elemente, Attribute, Muster je Wert, `href` nur an `image` auf `/api/web/v1/assets/<guid>`, vier CSS-Eigenschaften) und dieselbe Liste in der Seite (`pages/util.js`, neu aufgebaut aus neuen Knoten) | `bc2e3f0` | `SvgAllowListTests` (alles, was der Renderer schreibt, bleibt für alle 11 Papiere; jedes genannte Gefährliche fällt weg; die Listen der Seite sind die des Servers) |
| 4 | Zeilen während der ersten Zuweisung blieben ohne Besitzer | Ein Push der Person hinter dem App-Token übernimmt besitzerlose Zeilen seines Stapels (und der genannten Eltern), unter der Sperre des Push; kein anderer darf das | `ef96e63` | `SecurityFindingsTests.A_push_of_a_row_that_nobody_owns_yet_…` |
| 5 | Die Schlüsseldatei lag beim Sichern kurz im Klartext in `/tmp` | direkt aus der Schlüsseldatei verschlüsseln; die verschlüsselte Zwischendatei liegt im temporären Ordner und wird nach dem Senden gelöscht | `3692a0c` | `BackupKeyTests.The_key_is_encrypted_straight_from_its_file_…` (kein Klartext in `/tmp` und im Schlüsselordner während des Sendens) |
| 6 | Ein Inhalt, der kein Objekt ist, machte alle Listen kaputt | beim Prüfen abgelehnt (400), beim Lesen übersprungen | `6174ca4` | `WebApiTests.A_payload_that_is_not_an_object_is_refused_…` |
| 7 | Die Zuweisung hing an der Anfrage | läuft mit dem Token des Programmendes | `ef96e63` | `SecurityFindingsTests.The_claim_finishes_although_the_request_…` |
| 8 | Abmelden über den Hub ließ Inhalt im Vor-und-zurück-Speicher | Inhalt vor dem Weggehen leeren, Adresse auf die Seite ohne Suchbegriff, bei `pageshow` mit `persisted` neu laden | `e706d7f` | `WebPagesTests.Signing_out_empties_the_page_…` (als Text; im Browser nicht nachgestellt: unbestätigt wie der Befund) |
| 9 | Der Codex-Prozess erbte die ganze Umgebung | Umgebung leeren, nur `PATH`, `HOME`, `CODEX_HOME` | `dc4ba33` | `CodexSafetyTests.The_environment_of_codex_is_…`, `…The_running_program_really_does_not_see_…` |
| 10 | Codex-Anmeldeprozesse blieben stehen; die KI-Seite fragte alle 5 s | nach 20 s ohne Code, bei zwei gleichzeitigen Anmeldungen und nach einer Zeitüberschreitung wird beendet; die Seite fragt alle 15 s und nur, solange sie vorne ist | `dc4ba33` | `CodexSafetyTests.A_login_that_shows_no_code_in_time_is_ended`, `…Two_logins_at_once_…`, `…A_command_that_runs_past_its_time_…` |
| 11 | Der ungefilterte `LibraryReader` stand im DI | nur `Libraries.Of(person)` im DI, der Konstruktor von `LibraryReader` ist intern | `70eeedb` | `WebApiTests.There_is_no_reader_of_everybodys_notes_to_ask_for` |
| 11 | Der Test für die Admin-Rolle benutzte eine handgeschriebene Wegliste | nimmt die Endpunkte des Servers selbst (und prüft zusätzlich, dass jeder Weg der Web-Schnittstelle ohne Sitzung 401 gibt) | `70eeedb` | `AdminApiTests.Only_an_administrator_gets_in_at_any_path` |
| 11 | Seitenweises Lesen: unbekannter Cursor von vorn, gelöschte Zeile wiederholt Zeilen | ein unbekannter oder veralteter Cursor ist ein Fehler (`cursor.stale`), die Seite fragt einmal von vorn und entdoppelt | `70eeedb` | `WebApiTests.Lists_come_page_by_page_…` |
| 11 | STATE.md und ARCHITECTURE.md nannten gelöschte Tests und Überholtes | berichtigt | `4d07e35` | – |
| 11 | Bilder zeigten den Menüpunkt „Stundenplan“ | neu aufgenommen (`docs/screenshots/web-*.png`: 12 breit, 4 Telefon) | siehe Verlauf | – |

**Offen aus dieser Prüfung (nicht behoben, benannt):**
- Ob die Sandbox von Codex das Lesen der Schlüsseldatei, der Sicherungen und der Dateien verhindert, ist **unbelegt** (Befund 9); die Umgebung ist jetzt leer, der Prozess läuft aber weiter als derselbe Benutzer. **Vor dem Zugang der Mitglieder zur KI zu klären**, am besten mit einem eigenen Benutzer oder einer Prüfung der Sandbox.
- Der Wächter `owner_id is not distinct from excluded.owner_id` im `ON CONFLICT` ist weiter nicht einzeln getestet.
- Die Bilder sind nur hell (der Headless-Browser ignoriert hier die Einstellung für dunkel).

## Für das Einspielen (Meilenstein 1 und 2 auf die laufende Installation)

Stand 25.11.2026, nach beiden Sicherheitsprüfungen. **Nichts davon ist an der laufenden Installation ausgeführt.** Einspielen ist Sache des Orchestrators, mit dem Ja des Eigentümers. Adresse, Ordner und Host der Installation stehen in `docs/LOCAL.md`
(nicht im Repository); hier stehen `<ordner>` (der Ordner mit `docker-compose.yml` und `.env`) und `<adresse>` (die öffentliche Adresse). Die Dienste der Compose-Datei heißen `junotes` und `postgres`, Datenbank und Benutzer `junotes`.

**Ohne Änderung der Compose-Datei und der Volumes:** Der Schlüssel der Basis liegt im Volume unter `/data/base` (das Image setzt `BASE_DATA`), die Datenbankverbindung bleibt `ConnectionStrings__JuNotes`, die Werte `Admin__Password`/`Web__Password`/`UNTIS_*` werden nicht mehr gelesen (harmlos).

### Schritt 0: zwei Antworten des Eigentümers (**beantwortet am 25.11.2026: Fall B1 und Ja**, siehe SCOPE.md 9 und 10)
1. **Wem gehören die Hefte auf dem iPad?** (Fall A: der Person, die die Einrichtung abschließt: weiter. Fall B1: einer anderen Person: **diese** schließt die Einrichtung ab und wird erste Administratorin, der Eigentümer lädt sich danach ein. Fall B2: der Eigentümer schließt ab und gibt später ab: braucht die zurückgestellte Übergabe, **dann nicht einspielen**.) Die Zuweisung der alten Hefte gibt **alle** an die Person, die die Einrichtung abschließt, und ändert sich später nicht.
2. **Sein Ja** zum Einspielen (Sicherung davor, Wartezeit der App, Rückweg bekannt).

### Schritt 1: vorher prüfen
| Prüfung | Befehl oder Ort | Soll |
|---|---|---|
| Stand der Installation | `cd <ordner> && docker compose ps` und `curl -fsS <adresse>/api/v1/status` | beide Dienste laufen, Antwort mit `"api":1` und der alten Version |
| **Länge des App-Tokens** | `awk -F= '/^SYNC_TOKEN=/{print length($2)}' <ordner>/.env` (gibt nur die Länge aus) | **mindestens 32**, sonst startet der Server nicht. Ist es kürzer: neues Token (`openssl rand -base64 32`), in `.env` und **gleichzeitig** in der App eintragen |
| Besitzer des Volumes | `docker compose exec junotes ls -ld /data` | gehört `app` (sonst kann der Schlüssel nicht angelegt werden und der Start bricht ab) |
| Anzahl der Zeilen merken | `docker compose exec -T postgres psql -U junotes -d junotes -Atc "select count(*) from entities; select max(number) from schema_migrations"` | Zahlen aufschreiben (Vergleich nachher); Migration steht auf **7** |
| **Vertrauenswürdige Proxys** | Der Reverse-Proxy schickt `X-Forwarded-For` und `X-Forwarded-Proto` (die Voreinstellung von Caddy). Nach dem Start in der App unter Einstellungen › Installation die Adresse des Proxys eintragen | Im Sicherheitsprotokoll und im Verlauf der Anmeldungen steht die **Adresse des Geräts**, nicht die des Proxys (sonst zählen alle Geräte unter der Adresse des Proxys, und die Protokollzeilen und die Bremse je Adresse sagen nichts; ein richtiges Token wird davon nie abgewiesen) |
| Der Stand, der gebaut wird | `git ls-remote <repository> refs/heads/main` stimmt mit dem getesteten Commit überein; der Build-Kontext der Compose-Datei zeigt auf **dieses** Repository (nach der Umbenennung zu `notes` prüfen, ob die Adresse noch stimmt) | derselbe Commit; Tests auf ihm: 379 von 384 grün, die 5 roten brauchen `pg_dump` 18 (in der CI und im Image vorhanden) |
| Platz | `df -h` auf dem Host und `docker system df` | genug für Image, Sicherung, Volume-Kopie |

### Schritt 2: Sicherung (vor dem ersten Start des neuen Images)
Zusätzlich zur automatischen Sicherung, die der Server vor der ersten Migration selbst macht (schlägt sie fehl, startet er nicht):
1. Datenbank von Hand: `cd <ordner> && docker compose exec -T postgres pg_dump -U junotes -Fc junotes > ~/vor-der-basis-$(date +%F).dump` (die Datei ist **nicht** verschlüsselt; auf dem Host im Ordner des Benutzers lassen, nicht kopieren).
2. Dateien (Scans): `docker volume ls | grep junotes` zeigt den Namen des Volumes mit `/data`; dann `docker run --rm -v <volumename>:/data -v "$PWD":/out alpine tar czf /out/vor-der-basis-daten.tgz -C /data .`
3. `.env` und `docker-compose.yml` kopieren (`cp .env .env.vor-der-basis`, `cp docker-compose.yml docker-compose.yml.vor-der-basis`).
4. **Das alte Image behalten:** `docker image ls` zeigt das Image der Installation; `docker tag <image-id> junotes-vor-der-basis` (ohne das ist der Rückweg ein neuer Bau des alten Commits).

### Schritt 3: einspielen
```
cd <ordner>
docker compose build --pull
docker compose up -d
docker compose logs -f junotes
```
**Erfolg im Protokoll:** `Notes … (base 0.3.4)`, `Database ready, N migration(s) applied`, dann `FIRST SETUP: there is no account yet … setup code: XXXX-XXXX-XXXX` (der Code steht **nur** dort). Fehlermeldungen: `Sync__Token has N characters` (zu kurz, siehe Schritt 1), `The data folder … cannot be written` (Besitzer des Volumes), `Backup … failed` (die Sicherung vor der Migration schlug fehl; es wurde **nichts** geändert; Ursache beheben).

### Schritt 4: Erfolg erkennen (Der Eigentümer hat noch nichts getan)
| Was | Befehl | Soll |
|---|---|---|
| Gesundheit | `curl -fsS <adresse>/api/health` | `ok`, neue Version, Version der Basis |
| Tür der App offen | `curl -fsS <adresse>/api/v1/status` | `"api":1` |
| Alter Token geht | `read -rs T; curl -s -o /dev/null -w '%{http_code}\n' -H "Authorization: Bearer $T" "<adresse>/api/v1/sync/pull?limit=1"` (das Token erscheint weder im Verlauf noch auf dem Bildschirm) | **200** |
| Nichts verloren | dieselbe Abfrage wie in Schritt 1 | **dieselbe** Zeilenzahl; `schema_migrations` zeigt **8**; `select count(*) filter (where owner_id is null) from entities` = Zeilenzahl (noch niemandem zugewiesen) |
| Neue Tabellen | `\dt` in `psql` | `users`, `sessions`, `base_migrations`, `assets`, `app_instance_settings` sind da |
| iPad | Die App öffnen | Statuszeile „synchronisiert“ (**nur am Gerät prüfbar**; hier nie ausprobiert) |

### Schritt 5: der Eigentümer, einmal (oder die Person aus Fall B1)
1. `<adresse>` im Browser öffnen. Den **Einrichtungscode** aus dem Protokoll eingeben (`docker compose logs junotes | grep "setup code"`), Name und Passwort wählen (mindestens 12 Zeichen), den zweiten Faktor einrichten (Authenticator-App), die **Wiederherstellungscodes sichern** (nicht auf diesem Gerät allein). Das erste Konto ist der Administrator.
2. **Prüfen:** `select count(*) filter (where owner_id is null) from entities` ist jetzt **0** (alles gehört dem Konto), und der Verlauf unter Einstellungen zeigt die Anmeldung. In der Verwaltung › Sicherungen steht, ob der Schlüssel in einer Sicherung liegt (ohne `Backup__Passphrase` und zweiten Ort **nicht**: die Datei `/data/base/secret.key` getrennt aufheben).
3. Einstellungen › Installation: **vertrauenswürdige Proxys** setzen (Schritt 1).
4. Verwaltung › KI-Verbindung: der Status von Codex bleibt (die Anmeldung liegt im Volume); sonst neu anmelden.
5. Weitere Personen einladen (Einstellungen › Personen).
6. Später, mit dem Umzug der Compose-Datei: die Zeilen `ADMIN_PASSWORD`, `WEB_PASSWORD`, `UNTIS_*` aus `.env` und Compose entfernen.

### Was passiert, solange das nicht geschehen ist
Die iPad-App gleicht **weiter ab**, mit demselben Token und denselben Antworten (Vertragstests; Altstand-Probe). Die Zeilen, die dabei neu entstehen, gehören niemandem und gehen beim ersten Konto mit an den Administrator. **Nicht erreichbar:** die Web-App und die Verwaltung (sie brauchen ein Konto; die Startseite zeigt die Einrichtung), also auch die Codex-Anmeldung im Browser. **Wer die Adresse und den Einrichtungscode aus dem Protokoll hat, kann das erste Konto anlegen: die Einrichtung nicht liegen lassen**, möglichst im selben Zug einspielen und einrichten.
**Der App-Token steht danach für eine festgeschriebene Person** (`token_owner` in `app_instance_settings`): Wird sie gesperrt oder gelöscht, antwortet `/api/v1` mit 401 und es wird nie jemand anderes gewählt. Scheitert die Zuweisung der alten Hefte (steht im Protokoll, die Übersicht der Verwaltung zeigt es), antwortet `/api/v1` mit **503**, bis die Ursache behoben und der Server **neu gestartet** ist.

### Rückweg
**Weg 1 (der sichere, mit den Dateien von Schritt 2):**
1. `cd <ordner> && docker compose down` (**nicht** `-v`: das Volume bleibt).
2. In `docker-compose.yml` das Image des Dienstes `junotes` vorübergehend auf `junotes-vor-der-basis` stellen (die Zeile `build:` für den Dienst auskommentieren, `image: junotes-vor-der-basis` einfügen), oder den alten Commit bauen.
3. Datenbank zurück: `docker compose up -d postgres`, dann
   `docker compose exec -T postgres psql -U junotes -d postgres -c "drop database junotes with (force)" -c "create database junotes owner junotes"` und
   `docker compose exec -T postgres pg_restore -U junotes -d junotes --no-owner < ~/vor-der-basis-<datum>.dump`.
4. Dateien nur, wenn sie beschädigt sind: `docker run --rm -v <volumename>:/data -v "$PWD":/in alpine sh -c 'cd /data && tar xzf /in/vor-der-basis-daten.tgz'`.
5. `docker compose up -d`. **Erfolg:** `/api/v1/status` antwortet, der alte Token geht (Schritt 4), `/Admin` fragt wieder nach dem alten Passwort aus `.env`, das iPad gleicht ab.
Dabei gehen Konten, Sitzungen und der zweite Faktor verloren **und** alles, was seit der Sicherung geschrieben wurde (das iPad schickt es beim nächsten Abgleich erneut, soweit es bei ihm noch im Postausgang steht).

**Weg 2 (nur das Schema zurück, die Zeilen bleiben, auch die neuen; an einer Kopie mit Testdaten geprüft, nicht an echten Daten):** das alte Image starten (Schritt 2 des Weges 1) und
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
Dabei gehen die Konten, Sitzungen und der zweite Faktor der Basis verloren (nicht die Hefte). Danach wie in Weg 1, Punkte 5 ff.

### Was bewusst nicht Teil des Einspielens ist
Kein neues Compose (der Umzug auf die Compose-Datei der Basis ohne Variablen plant der Orchestrator getrennt), keine Änderung der Volumes, kein Abschalten des gemeinsamen Tokens (das gibt es erst mit Meilenstein 3), keine Schule (Meilenstein 4), keine KI-Zugang für Mitglieder (zuerst die Sandbox von Codex klären, siehe „Sicherheitsprüfung 2“).

## Für die Sicherheitsprüfung (1a und 1b)

**Jede neue Tür:**

| Tür | Wer kommt hinein | Wo der Code steht | Wo der Test steht |
|---|---|---|---|
| `/api/v1/*` (Prefix, ohne Rücksicht auf Groß- und Kleinschreibung) | nur mit `Authorization: Bearer <Sync__Token>`; kein Cookie, kein `X-Base`, kein Origin; nur `/status` offen | `Api/V1Door.cs` (`CheckAsync`), Deklaration in `Program.cs` (`Doors`) | `ApiContractTests.Every_other_route_answers_401_…` (14 Wege × 3 Köpfe), `DoorsTests.A_session_opens_nothing_under_api_v1`, `…The_instance_token_opens_nothing_outside_api_v1`, `…The_token_door_needs_no_header…`, `…The_door_is_the_same_door_in_any_spelling_of_its_path` |
| `/api/**` der Basis (Anmeldung, Einrichtung, Personen, Einstellungen, Konto) | Sitzung der Basis, `X-Base: 1` und Origin bei Änderungen | `base/` (unverändert, Tests der Basis) | `DoorsTests.The_base_answers_health_about_…` |
| `/api/web/v1/**` (Bibliothek, Hefte, Seiten, Dateien, Noten, Karten, Suche) | Sitzung der Basis (Wächter der Basis), nur eigene Zeilen und Dateien; der App-Token öffnet nichts davon | `Api/WebApi.cs`, `LibraryReader.Of(person)`, `AssetService.OpenAsync(id, person)` | `WebApiTests.*`, `OwnerTests.The_notebooks_of_the_web_…`, `DoorsTests.The_instance_token_opens_nothing_outside_api_v1` |
| `/api/web/v1/admin/**` | Sitzung der Basis und Rolle `admin` (`RequireRole`, geprüft im Wächter der Basis) | `Api/AdminApi.cs` | `AdminApiTests.Only_an_administrator_gets_in_at_any_path` (ohne Sitzung 401, als `member` 403) |
| `/Notes`, `/Notes/{id}`, `/Admin`, `/Admin/Codex|Profiles|Backups` | nur noch Weiterleitungen auf die neuen Adressen, ohne Inhalt | `Program.cs` | `DoorsTests.What_the_web_api_gives_is_never_kept_…` |
| `/health/live`, `/health/ready`, `/api/health`, `/api/about`, Dateien in `wwwroot/` | offen | `Program.cs`, `base/` | `/api/health` und `/api/about`: `DoorsTests.The_base_answers_health_about_…`; **`/health/live` und `/health/ready` haben seit dem Wegfall der Razor-Tests keinen benannten Test mehr** (kommt mit dem nächsten Bauen) |

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
- Der App-Token ist weiter **ein gemeinsamer Schlüssel** für das Konto des ersten Administrators. Wer ihn hat, liest und schreibt dessen Hefte. Das war vorher nicht anders; Token je Gerät und Person sind seit 3a gebaut (Kopplung), der gemeinsame Token gilt aber weiter, bis die App sich koppeln kann und der Administrator ihn abschaltet (3d).
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

- **Sechs Backup-Tests** (`BackupServiceTests`, `BackupMirrorIntegrationTests`; der frühere `AdminTests.A_manual_backup_…` ist mit den Razor-Seiten entfallen, `AdminApiTests.A_backup_is_made_by_hand_…` läuft mit einem Ersatz für `pg_dump`): Auf dieser Maschine liegt `pg_dump` in Version 17, der Testserver ist 18
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
| Abgleich mit mehreren Personen | auf dem Server gebaut und mit Zwei-Personen-Tests geprüft (Gerätetoken, 3a); die App kann sich noch nicht koppeln, also nie mit einem Gerät probiert |
