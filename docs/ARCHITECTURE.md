# Architektur des Umzugs auf die Basis (Entwurf)

Stand: 24.11.2026. **Entwurf für den Orchestrator.** Er beschreibt, wie `notes` (heute JuNotes) auf die gemeinsame Basis umzieht. Nichts davon ist
gebaut. Was ich aus dem Code der Basis nicht selbst gelesen habe, ist als **ungeprüft** markiert. Meilensteine und Entscheidungen stehen in
[SCOPE.md](SCOPE.md).

## 1. Was vom heutigen Server bleibt

| Teil | Ort | Warum es bleibt |
|---|---|---|
| Datenmodell `entities` (`kind`, `id`, `parent_id`, `payload`, `deleted`, `seq`) | `database/migrations/0001…0007` | Das ist der Vertrag mit der iPad-App. Ändert sich nur durch **additive** Spalten. |
| Abgleich Push/Pull/Info | `src/JuNotes.Backend/Sync` | Läuft auf Strichebene und ist getestet gegen eine echte Datenbank. |
| Dateien (Scans, Bilder) | `Assets` | Die App lädt per `PUT`/`GET /api/v1/assets/{id}`. |
| Sicherungen mit `pg_dump`, verschlüsselt, Rotation, vor jeder Migration | `Backup` | Gerade die Sicherung vor jeder Migration schützt den Umzug. |
| Arbeiter (Sicherung nachts, Schule) | `JuNotes.Workers`, `SchoolWorker`, `BackupWorker` | Unabhängig von Anmeldung und Oberfläche. |
| Lernhilfen (Quiz, Test, Karten, Prüfen, Noten, Cover) | `Ai/LearningService` | Bleibt, wird aber hinter eine Schnittstelle gestellt (siehe 6). |
| Export nach Paperless | `/api/v1/export` | Unverändert. |

## 2. Was die Basis ersetzt

| Heute | Danach |
|---|---|
| Zwei Passwörter aus der Umgebung (`ADMIN_PASSWORD`, `WEB_PASSWORD`), Sperre im Speicher, Cookie `junotes-admin` ohne dauerhaften Schlüsselring (Sitzungen enden beim Neustart) | Konten, Sitzungen in der Datenbank (nur als Hash), Rollen, Einladungen, Geräte, TOTP und Wiederherstellungscodes aus der Basis; Anmeldung über den Hub (OIDC) oder eigenes Konto |
| Razor-Seiten unter `src/JuNotes.Frontend/Pages` (`Notes/`, `Admin/`, `Login`) | Seiten der Basis: JavaScript ohne Build (`shell()`, Personenmenü, Anmeldeseiten), sie sprechen JSON mit `/api/web/v1`. **Die Razor-Seiten fallen am Ende von Meilenstein 2 weg.** |
| Einstellungen in `AiSettingsStore`, `AiProfileStore` und Umgebungsvariablen | Einstellungen der Basis (Web-Modul) für das, was die Verwaltung ändert; Umgebungsvariablen nur für Geheimnisse |
| Sicherheitsköpfe, Gesundheitsroute, Einrichtungsassistent | `Web`-Modul der Basis (`/api/health`, `/api/about`); die heutigen `/health/live` und `/health/ready` bleiben, bis nichts mehr darauf zeigt |

**Bleibt zunächst daneben stehen:** der gemeinsame `Sync__Token`. Er prüft nur den Zugriff auf `/api/v1`, nicht die Person (siehe 3).

## 3. Daten ohne Verlust mitnehmen

Grundsatz: **Nur hinzufügen, nie umbauen.** Die App kennt die Form der Daten und wird nicht zugleich geändert.

1. **Migrationen** laufen wie heute nummeriert (`0008…`) in einer Transaktion, mit Sicherung davor (`DatabaseUpgrade`). Die der Basis stehen in einer Tabelle für sich (`base_migrations`, `MigrationTable`), unsere bleibt `schema_migrations`; die Basis läuft zuerst. Eine Tabelle der Baseline (`instance_settings`) trägt den Namen einer Tabelle der Basis und wird einmal in `app_instance_settings` umbenannt. Der Umzug darf keine Zeile
   verändern, nur Spalten und Tabellen anlegen.
2. **Besitzer:** Eine SQL-Migration kann ihn nicht zuweisen, weil das Konto des Eigentümers erst im Einrichtungsassistenten der Basis entsteht. Darum:
   `0008` legt `entities.owner_id uuid` (und gleiche Spalten an den Dateitabellen) **leer** an. **Leer heißt: gehört dem ersten Administrator.** Beim ersten Zugriff
   nach dem Anlegen des ersten Kontos (Start, Anmeldung einer Person, Zugriff mit dem App-Token; die Basis hat keinen Haken im Augenblick der Erstellung) werden alle leeren Zeilen in **einer** Anweisung diesem Konto
   zugewiesen, ebenso die Dateien (`OwnerDirectory`); der alte App-Token steht ab da für diese Person. Bis dahin
   verhält sich alles wie heute. Neue Zeilen vor dem ersten Konto bleiben leer und werden mitgenommen. Die Spalte wird erst `not null`, wenn der Abgleich
   gegen alte Stände geprüft ist.
3. **Der Primärschlüssel `(kind, id)` bleibt.** Die ids sind zufällige UUIDs der Geräte; Kollisionen zwischen Personen sind praktisch ausgeschlossen. Wer sie
   absichtlich provoziert, darf nichts überschreiben. **Schreibregeln (die zentrale Sicherheitsregel des Umzugs):**
   - Eine vorhandene Zeile darf nur ihrer Besitzerin überschrieben oder gelöscht werden.
   - Das `parent_id`-Ziel einer geschriebenen Zeile muss derselben Person gehören oder für sie zum **Bearbeiten** freigegeben sein. Sonst könnte man Zeilen
     unter ein fremdes Heft hängen.
   - Zugriff über die Elternkette gilt nur für Zeilen, die dem Besitzer des Hefts gehören.
   - Dateien (`PUT`/`GET /api/v1/assets/{id}`) prüfen den Besitz genauso.
   - **Jede dieser Regeln bekommt einen Test mit zwei Personen.**
   - **Wie der Server eine Verletzung beantwortet (entschieden, Orchestrator 24.11.2026):** Eine Zeile, die gegen die Schreibregeln verstößt, wird **ausgelassen**.
     Der Rest des Stapels wird angenommen, `accepted` nennt nur die angenommenen Zeilen, und es gibt **keinen Fehlerstatus** für den Stapel. Grund: In
     Meilenstein 1 gehört alles dem Eigentümer, ein ehrliches Gerät trifft die Regel nie; ein Fehler würde nur ein ehrliches Gerät mit einem Programmfehler dauerhaft
     blockieren (siehe unten, was die App bei Fehlern tut).
     - Die Antwort verrät nichts über die fremde Zeile, außer dass **diese eine** nicht angenommen wurde.
     - Ein Eintrag im Sicherheitsprotokoll nennt Person, Gerät und Art der Regel, **keine Inhalte**, und ist gedrosselt, damit ein Gerät das Protokoll nicht fluten kann.
     - Ein **verformter** Stapel (Prüfung der Form, `SyncValidationException`, heute Status 400) bleibt ein Fehler; das ist keine Besitzfrage.
     - **Tests:** Ein Stapel mit einer verbotenen und zwei erlaubten Zeilen speichert genau die zwei; die fremde Zeile ist danach Byte für Byte unverändert;
       dasselbe für ein fremdes `parent_id`-Ziel. Die Golden-Tests aus Meilenstein 0 halten das heutige Verhalten der Push-Antwort fest (Form von `accepted`
       und `seq`, Statuscodes), damit die neue Regel in Meilenstein 1 als bewusste, **additive** Änderung sichtbar wird.

   **Was die App mit Zeilen macht, die nicht in `accepted` stehen** (gelesen in `SyncEngine.swift`, nichts ausgeführt): Sie **wertet die Antwort gar nicht aus**
   (`_ = try await push(pending)`), sondern löscht nach jedem Push ohne Fehler **alle** gerade geschickten Zeilen aus dem Postausgang (`markPushed`).
   Eine ausgelassene Zeile wird also **nicht** wieder geschickt, sie ist für den Server verloren. Auf dem Gerät bleibt die lokale Fassung stehen; der Stand dort und auf
   dem Server laufen auseinander, bis der Server diese Zeile von sich aus wieder ausliefert. Das ist umgekehrt zum Fehlerfall (dort Dauerblockade). **Für
   Meilenstein 1 hinnehmbar** (ein ehrliches Gerät schickt nie eine verbotene Zeile). **Vor Stufe 2 der Freigaben muss es gelöst sein** (siehe 3a, Vorbedingung).

4. **`seq` bleibt global.** Der Pull einer Person liefert nur ihre Zeilen (und freigegebene) mit `seq` über ihrem Merker. Lücken in der Folge gibt es **heute
   schon**: jede geschickte Zeile verbraucht eine Nummer, auch wenn nichts gespeichert wird, und eine Änderung verbraucht zwei (festgehalten in `ApiContractTests`).
   **Gezielt nachgelesen (24.11.2026, `grep` über `JuNotesCore/Sources` und `App/Sources`):** Die App rechnet nirgends mit `seq`. Sie speichert genau eine Zahl, den
   `cursor` aus der Antwort (`meta`-Tabelle, `SQLiteStore.apply`), und schickt sie beim nächsten `pull` als `since` zurück (`SyncEngine`). `SyncRecord.seq` wird gelesen
   und nie benutzt, `PushResponse.seq` ebenso nicht; das `seq` des Postausgangs ist eine eigene lokale Zählung. Eine Besitzer-Filterung des Pulls (leere Antwort mit
   `cursor` = `since`) ist für die App also unbedenklich, solange der Server den Merker nie zurücksetzt oder senkt.
5. **Der bestehende App-Token** wird zum „Gerät des Eigentümers“: Der Server ordnet ihn bei jeder Anfrage der Person Eigentümer zu. Neue Token je Gerät
   und Person kommen mit Meilenstein 3 aus der Basis (Geräte-Modul). Der alte Token wird erst abgeschaltet, wenn die App ihn nicht mehr schickt, und das
   schaltet der Eigentümer von Hand.
6. **Freigaben:** eigene Tabelle `shares(entity_id, recipient_id, mode, granted_seq, revoked_seq)`; Lesen prüft Besitz oder Freigabe. Ein freigegebenes Heft
   bringt alle Seiten und Striche darunter mit (Zugriff über die `parent_id`-Kette, nur für Zeilen des Besitzers). **Offener Entwurfspunkt für Meilenstein 3:**
   siehe Abschnitt 3a.
7. **Beweis:** Der Test `A_database_from_an_older_version_answers_the_app_the_same_after_every_migration` (Meilenstein 0, vorhanden) legt einen Stand nach Migration 5 mit
   Zeilen jeder Art an, lässt den Host die übrigen Migrationen laufen und prüft, dass `pull`, `push`, Dateien und die übrigen Wege danach dasselbe antworten. Jede
   spätere Migration (Besitzer, Freigaben) läuft durch ihn und darf ihn nicht ändern.

### 3a. Freigaben und `seq` (offener Entwurfspunkt, Meilenstein 3)

**Problem.** Wird ein Heft später freigegeben, haben seine Zeilen ein `seq` unter dem Merker der Empfängerin; ihr Pull sähe sie nie. Beim Entziehen muss sie
erfahren, dass die Zeilen verschwinden. Die iPad-App kennt als Merker nur eine Zahl („größer als“), das ist der eingefrorene Vertrag.

**Vorschlag, in zwei Stufen:**

- **Stufe 1 (Meilenstein 3): Freigaben gelten nur im Web.** Der Browser liest direkt aus der Datenbank, hat keinen Merker und braucht kein `seq`. Das Teilen
  ist dann sofort richtig und entzieht sich sofort. Die iPad-App der Empfängerin bekommt freigegebene Hefte noch **nicht**. Das hält den Vertrag der App
  unberührt und liefert, was Meilenstein 3 zum Aufmachen braucht.
- **Stufe 2 (später, eigene Entscheidung): Freigaben auch im Abgleich.** Zwei Wege zur Wahl:
  - *Teilbaum neu stempeln:* Beim Freigeben bekommt jede Zeile des Teilbaums ein frisches `seq` (aus `change_seq`, in Stapeln in einer Transaktion). Alle
    Pulls sehen sie dann neu; Einfügen ist für die App ein Überschreiben mit gleichem Inhalt, also harmlos. Kosten: die Geräte der Besitzerin laden den
    Teilbaum noch einmal, und ein Heft mit vielen Strichen sind viele Zeilen. Entziehen: der Pull der Empfängerin liefert für die Wurzel der Freigabe eine
    **synthetische Löschung** (`deleted = true`, `seq` = `revoked_seq`); die App muss die Kinder dann selbst entfernen (**ungeprüft:** ob sie beim Löschen
    eines Hefts die Kinder mitnimmt; vor Stufe 2 im App-Code lesen).
  - *Eigener Merker je Freigabe:* Der Pull liefert zusätzlich `grants` mit dem Teilbaum seit dem Freigabezeitpunkt. Sauberer, aber **ändert die Antwort** des
    Abgleichs (nur additiv, die alte App ignoriert das Feld) und braucht Client-Arbeit.
  Ich würde mit „neu stempeln“ beginnen, solange Hefte klein sind, und den eigenen Merker nur einführen, wenn es in der Praxis zu viel Verkehr gibt.

**Ergebnis aus dem App-Code (gelesen am 24.11.2026, nichts ausgeführt):**

1. **Löschen nimmt die Kinder nicht mit.** `SQLiteStore.apply` und `upsert` setzen bei einer gelöschten Zeile nur diese eine Zeile auf `deleted = 1`. Die App
   löscht ein Heft nie mit einem Schlag: `AppModel.deleteFolder` verlangt einen leeren Ordner, und Seiten gehen einzeln in den Papierkorb. Eine synthetische
   Löschung der Wurzel bei Entzug **verbirgt** das Heft in der Ansicht (die Abfragen filtern nach Elternzeile und `deleted = 0`), lässt aber Seiten, Striche
   und Dateien **auf dem Gerät der Empfängerin liegen**. Entzug heißt also nur: nicht mehr sichtbar, nicht: von dort gelöscht. Für ein echtes Entfernen müsste
   der Server auch für jede Kindzeile eine Löschung schicken (viele Zeilen) oder die App lernt, Kinder mitzunehmen (App-Änderung).
2. **Die Suche auf dem Gerät** (`page_text`, eine lokale Volltexttabelle) wird beim Löschen einer Zeile nicht aufgeräumt, soweit ich gelesen habe. Text aus
   einem entzogenen Heft könnte in den Treffern bleiben. **Ungeprüft**, vor Stufe 2 gezielt nachlesen.
3. **Die App kennt keinen Besitzer.** Sie behandelt jede Zeile als ihre eigene und lässt sie bearbeiten. Schreibt die Empfängerin auf ein nur lesend
   freigegebenes Heft, lehnt der Server das ab, und das **blockiert wegen des Stapelverhaltens (oben) ihren ganzen Abgleich**. Stufe 2 braucht deshalb
   mindestens: Server lässt solche Zeilen still aus (siehe oben), **und** die App erfährt, dass ein Heft nur lesbar ist (neues additives Feld an der
   Wurzelzeile), sonst schreibt die Empfängerin ins Leere. Das ist eine App-Änderung und gehört nicht zu Meilenstein 3.

**Folge (mit dem Orchestrator abgestimmt):** Stufe 1 (nur Web) bleibt richtig und ist alles, was Meilenstein 3 liefert. **Vorbedingung von Stufe 2** ist eine kleine
App-Änderung mit eigenem Entwurf: Kinder beim Löschen mitnehmen, Lese-Marker an der Wurzelzeile, Aufräumen der lokalen Suche prüfen, und die App darf
ausgelassene Zeilen nicht mehr still verlieren.

## 4. .NET 11 hier, .NET 10 in der Basis

- Die Basis wird **als Quelltext in die App kopiert** (`sync-base.sh`), nicht als Paket. Der kopierte Code wird dann mit dem Zielframework der App
  übersetzt. Code für net10 übersetzt unter net11 in aller Regel; die Gegenrichtung (net11-Pakete in net10) geht nicht.
- **Entschieden (Orchestrator, 24.11.2026): `notes` bleibt auf net11, wenn die Probe in Meilenstein 0 grün ist.** Ist sie es nicht, gehen die Fehler an den
  Orchestrator, **bevor** an der Kopie der Basis etwas geändert wird: Änderungen gehören in die Basis, nicht in die Kopie.
- **Ungeprüft:** ob der Basiscode unter `net11.0` und unseren Analysatoren (Warnungen als Fehler, zum Beispiel CA1720, CA1861, CA2026) ohne Änderung durchgeht.
- Die Datenbank bleibt PostgreSQL 18 mit Npgsql 10; das passt zu beiden.

### 4a. Wie die Basis in dieses Projekt kommt

Die Basis ist öffentlich (MIT, 0.3.1); lokal liegt sie unter `~/projects/base`, der Vertrag zwischen App und Hub steht in `docs/CONTRACT.md` dort. Vorgehen
(**in Meilenstein 0, noch nichts davon ausgeführt**):

1. Mit `scripts/new-app.sh` eine Start-App im Zwischenordner der Sitzung erzeugen und dort lesen, wie `Program.cs` die Basis einhängt.
2. Den Ordner `base/` samt `MANIFEST` in dieses Repository übernehmen. Ab dann gelten `check-app.sh` und `sync-base.sh` der Basis.
3. **Akzentfarbe:** `--accent-hue` ist eine Zahl von 0 bis 359 (OKLCH-Farbton), die einzige Farbe, die die App selbst setzt. **Mein Vorschlag: 75** (warmes
   Bernstein, Papier). Ob andere Apps der Familie schon einen nahen Farbton belegen, habe ich nicht geprüft; die Festlegung treffen Orchestrator und
   Eigentümer.

## 5. Welche Schnittstellen die Web-App braucht

Die Web-App besteht aus Seiten der Basis (JavaScript ohne Build, spricht JSON). Die heutigen Razor-Seiten, die die Datenbank direkt lesen, taugen dafür nicht.
Meilenstein 2 baut deshalb **die lesenden Wege von `/api/web/v1`** und die Seiten im Baukasten; die Razor-Seiten fallen am Ende von Meilenstein 2 weg.

| Bereich | Richtung | Anmeldung |
|---|---|---|
| `/api/v1/*` (heute: `status`, Abgleich `push`/`pull`/`info`, Dateien `PUT`/`GET`, Schule `status`/`calendar`/`news`, KI `profiles`/`{task}`, Export `status`/`paperless`) | iPad-App | Bearer-Token. **Eingefroren.** Nur additive Felder. |
| `/api/web/v1/*` | Browser | Sitzungs-Cookie der Basis, CSRF-Schutz, **nie** der App-Token |

**Meilenstein 2, nur lesen:** `me`; Bibliothek (Ordnerbaum); Heft mit Seiten; Seite (mit Strichen, für die Ansicht); Datei; Noten; Karten; Stundenplan der
Person. Die Namen sind ein Vorschlag, keine Seitenentwürfe. **Später:** Suche, Lernhilfen und KI, Freigaben (3), Zeichnen (8).

**Wichtig für die Suche:** Der erkannte Text (OCR) liegt heute nur auf dem iPad (siehe `docs/PHASE5.md`, Punkt 5). Eine Suche im Web gibt es erst, wenn
der Text mit synchronisiert wird. Das ist eine **additive** Erweiterung des Abgleichs (neues Feld oder neue `kind`), kein Umbau.

### 5a. `/api/v1` ist eine eigene Tür (gebaut in Meilenstein 1)

Der Wächter der Basis verlangt für alles unter `/api` eine Sitzung und für Änderungen `X-Base: 1` und einen Origin. Seit Basis 0.3.2 kann eine App **eigene Türen** erklären (SEC-07,
`BaseAppOptions.Doors`): `/api/v1` ist eine, die Prüfung ist der Instanz-Token (`V1Door.CheckAsync`), nur `/status` ist offen. Unter einer Tür liest die Basis nie das Cookie, setzt keine
Identität und fragt weder nach Kopf noch nach Origin; das Präfix gilt ohne Rücksicht auf Groß- und Kleinschreibung. Der erste Entwurf (eine abgezweigte Pipeline vor der Basis, `MapWhen`) war
nötig, bevor die Basis das konnte, und ist wieder weg. Tests: kein Weg unter `/api/v1` ohne Token (auch nicht in anderer Schreibung); der App-Token gilt nirgends sonst unter `/api`;
ein Sitzungs-Cookie gilt nie unter `/api/v1` (`DoorsTests`, `ApiContractTests`). Bekannte Folgen: der Fehlerkörper bei 401 ist der der Basis, die Sicherheitsköpfe der Basis liegen auch auf
diesen Antworten, und die JSON-Antworten behalten ihre `null`-Werte, weil die Basis sie sonst weglässt (siehe STATE.md, Abweichungen 2 bis 4).

## 6. KI über den Hub und die eigene Brücke

**Heute:** `LearningService` ruft `ICodexRunner.RunAsync(CodexRequest, ct)` (`src/JuNotes.Integrations/Codex/CodexRunner.cs`). Der Aufruf startet
`codex exec` (nur lesen, Schema-Ausgabe, Modell und Aufwand per Aufruf). Zählung in `AiUsageStore`, Profile in `AiProfileStore`.

**Der Hub** (nach seiner Beschreibung, beim Bauen gegen den laufenden Hub zu prüfen, ich habe nur die Beschreibung gelesen): Schnittstelle
`/api/ai/v1` mit `GET /models`, `POST /chat` (auch als SSE-Strom) und `GET /usage`; ein Schlüssel je App als `Bearer`, serverseitig nur als SHA-256
abgelegt; Budgets je App, Tag und Monat; Rate-Limit je Minute; standardmäßig aus (`503 ai_disabled`).

**Vorschlag:** `ICodexRunner` bekommt Geschwister hinter einer gemeinsamen Schnittstelle (`IAiBackend`):

- `CodexBackend`: der bisherige Runner, unverändert.
- `HubBackend`: spricht `/api/ai/v1` mit dem Schlüssel der App. **Der Schlüssel kommt nicht aus der Umgebung:** Die Basis verlangt eine Compose-Datei ohne
  Variablen und ohne `.env`; Geheimnisse liegen verschlüsselt in der Datenbank. Der Hub-Schlüssel wird deshalb in der Verwaltung eingetragen (verschlüsselt
  gespeichert, nie zurückgegeben).

**Wie entschieden wird, welcher aktiv ist** (eine Regel, kein Zufall):

1. Der Modus ist **Hub**, wenn **alle drei** stimmen: die Verbindung zum Hub steht, ein KI-Schlüssel ist hinterlegt und der Schalter in der Verwaltung ist an.
   Sonst ist er **eigene Brücke**.
2. Im Modus Hub wird `CodexBackend` **gar nicht registriert**: kein `codex`-Prozess, keine Codex-Seite in der Verwaltung, keine Anmeldung per Gerätecode,
   kein Hinweis „Codex verbunden“. Die Verwaltung zeigt statt dessen „KI über den Hub“ mit Nutzung.
3. **Kein stiller Rückfall.** Ist der Hub nicht erreichbar oder aus (`503 ai_disabled`), bekommt die Person eine klare Meldung. Der Server weicht nicht
   heimlich auf die eigene Brücke aus. Grund: zwei Wege würden doppelt zählen und doppelt abrechnen, und niemand sähe, welcher gerade läuft.
4. Den Modus ändert, wer in der Verwaltung den Schalter umlegt oder den Schlüssel einträgt oder entfernt. Die Verwaltung zeigt den Modus immer an. Ein
   Wechsel gilt für neue Anfragen; laufende beendet der Weg, auf dem sie begonnen haben.

**Sparsamkeit (Entscheidung 7), wie es gebaut würde:**

| Anforderung | Umsetzung |
|---|---|
| Grenze pro Person und Tag | **Der Hub zählt pro App, nicht pro Person.** Darum zählt `notes` selbst (`AiUsageStore` bekommt `person_id`) und prüft vor jedem Aufruf. Die Grenze der App im Hub bleibt die äußere Schranke. **Entschieden (Orchestrator):** `notes` zählt selbst pro Person, das Budget der App im Hub bleibt die äußere Schranke. Dass der Hub die Person mitzählt, ist nur eine Idee für JulOS; **es wird nicht damit geplant.** |
| Keine doppelten Anfragen | Zusammenführen gleicher laufender Anfragen (gleiche Person, Aufgabe und Eingabe: eine Anfrage, mehrere Wartende). Ergebnisse, die zu einer Seite gehören (Quiz, Karten), werden wie heute gespeichert und nicht neu erzeugt, außer die Person verlangt es. |
| Keine Hintergrundanfragen ohne Anlass | Ein Arbeiter ruft die KI nie auf. Heute ruft nur `LearningService` auf, und der wird nur von Anfragen einer Person erreicht (**beim Bauen mit einem Test belegen**, der die Arbeiter ohne KI-Aufruf laufen lässt). |
| Abbruch beim Verlassen | `HttpContext.RequestAborted` geht als `CancellationToken` bis in den Hub-Aufruf; bei SSE schließt der Abbruch den Strom. Der Hub zählt dann nur, was bis dahin geflossen ist (**ungeprüft: wie der Hub abgebrochene Ströme zählt**). |
| Sichtbare Nutzung | Je Person in der Web-App (heute, Monat); gesamt in der Verwaltung; die Zahl des Hubs (`GET /usage`) daneben als Gegenprobe. |

## 7. Offline, Geräte und Verwaltung

- iOS arbeitet weiter lokal und gleicht später ab. Dafür bleibt der Abgleich auf Strichebene, ohne Vorbedingung einer Anmeldung im Web.
- Geräte und Abgleichstand: Aus dem Geräte-Modul der Basis plus `lastSeen` und dem letzten bestätigten `seq` je Gerät (neue Tabelle, die der Abgleich
  beim `pull` schreibt). **Ungeprüft:** ob das Geräte-Modul der Basis diese Felder schon hat.
- Verwaltung: Personen und Rollen (Basis), KI-Profile und Grenzen (bleiben aus `AiProfileStore`, nach Person erweitert), Sicherungen (heute
  `Admin/Backups`). Keine Schule.

## 8. Schule pro Person

Entscheidung des Eigentümers (24.11.2026): **eigene Anmeldung für den Stundenplan**, je Person, nur für sich. Regeln in SCOPE.md („Regeln für alles Schulische“). Gebaut wird es in Meilenstein 4.

**Wie das gebaut würde:**
- Eine Seite in den Einstellungen der Person (Seite der Basis), auf der **die Person selbst** Server, Schule und Konto des Stundenplan-Dienstes einträgt und anmeldet („Verbinden“, „Trennen“). Nichts Schulisches in der
  Verwaltung; die Verwaltung zeigt je Person nur „verbunden / nicht verbunden“.
- Der Server ruft den Dienst **sparsam** ab (feste Abstände, kein Dauerabruf; ein Abruf beim Verbinden, danach höchstens einige Male am Tag) und speichert **nur den Stundenplan der Person** (Zeiten, Fächer, Räume,
  Klassenbezeichnungen) in einer Tabelle, die ihr gehört. Keine Schülerlisten, keine Noten, keine Nachrichten.
- Tests laufen gegen eine Attrappe des Dienstes, nie gegen den echten und nie mit echten Zugangsdaten. Niemand außer der Person tippt ihr Passwort.
- Der heutige `UntisClient` (ein Zugang für alle, Passwort in `School__Untis__*`) bleibt bis Meilenstein 4 **unangetastet und inaktiv**; dort wird er umgebaut und `School__Untis__*` entfällt.
  `SchoolImportService` liest weiter Feeds ohne Zugangsdaten (Kalender-Abo, Neuigkeiten), solange es sie gibt.
- **Schul-Intranet:** offener Punkt. Ohne dokumentierte Schnittstelle wäre eine Anmeldung dort das Auslesen von Seiten hinter der Anmeldung und bleibt ausgeschlossen, bis die Schule es erlaubt und der Eigentümer
  entschieden hat. Kein Entwurf, kein Code.

**Welche Variante für das Geheimnis möglich ist (nach dem Lesen, Stand 24.11.2026; nichts davon gegen die echte Schule ausprobiert):**
- **Variante A, dauerhaftes App-Geheimnis statt Passwort (Passwort nie gespeichert): technisch möglich, aber nicht offiziell dokumentiert.** Die Gemeinschaftsbibliothek `untis` (Elixir) spricht den „Untis Mobile“-Zugang an und
  meldet mit `user`, einem Einmalpasswort (`otp`) und der Uhrzeit an; sie sagt von sich, sie speichere nie Passwörter, müsse aber das geteilte App-Geheimnis behalten
  ([Dokumentation der Bibliothek](https://untis.hexdocs.pm/json-rpc.html)). Laut Beschreibung der Bibliothek stammt das Geheimnis aus dem Profil im Stundenplan-Dienst oder aus dem QR-Code dort
  (Suchergebnis, die Seite selbst, die ich gelesen habe, sagt es nicht). **Die Person könnte also das Geheimnis einmal selbst aus ihrem Profil nehmen und eintragen, und das Passwort käme gar nicht zu uns.**
- **Variante B, Passwort verschlüsselt je Person:** der Weg des heutigen `UntisClient` (Anmeldung mit Benutzer und Passwort über die JSON-RPC-Schnittstelle).
- **Die Quellenlage:** Eine **offizielle, öffentliche Beschreibung der JSON-RPC-Schnittstelle gibt es nicht**; mehrere Autoren von Bibliotheken schreiben, das Dokument gebe es nur auf Anfrage bei Untis
  ([Beispiel](https://webuntis.noim.me/)). Öffentlich dokumentiert ist die REST-Schnittstelle für Integrationspartner
  ([Untis Help Center](https://help.untis.at/hc/de/articles/4886785534354-API-documentation-for-integration-partners)); die Seite war für mich nicht lesbar (403), ich weiß also nicht, ob und wie eine einzelne Person sie nutzen darf.
- **Meine Einschätzung:** A ist die richtige Wahl, **wenn** die Schule den Zugang „Untis Mobile“ eingeschaltet hat und das Geheimnis der Person zugänglich ist; das ist erst **nach** der Entscheidung des Eigentümers (er weiß dann, dass die Schnittstelle nicht öffentlich beschrieben ist) und dann an einem Konto der Person
  selbst zu prüfen (sie tippt, nicht wir). Bis dahin wird nichts gegen den echten Dienst probiert. Beide Wege nutzen eine Schnittstelle, die Untis nicht öffentlich beschreibt: Das sollte der Eigentümer wissen, und im Zweifel fragt man bei Untis oder der Schule nach, ob die Nutzung
  für das eigene Konto in Ordnung ist. Kann A nicht bestätigt werden, gilt B nur mit der Regel „nachweislich nicht anders“, und der Eigentümer entscheidet.

## 9. Reihenfolge der Risiken

1. Besitzer und Abgleich (Abschnitt 3, Punkt 3): ein Fehler lässt eine Person fremde Daten sehen oder überschreiben.
2. Die App-Seite: die App wird auf dem Gerät nie vom Server aus getestet, sondern nur übersetzt; eine Änderung am Vertrag fiele erst auf dem iPad auf.
3. Versionsfrage (Abschnitt 4): billig zu prüfen, teuer zu spät.

## 10. Meilenstein 3: Personen, Geräte, Freigaben (Entwurf, nichts gebaut)

Stand 25.11.2026, Text für den Orchestrator. Grundlage ist, was gebaut und gelesen ist: die Basis hat **kein** Modul für Gerätetoken (nur Sitzungen im Browser, Einladungen und das Hub-Konto), der App-Token ist der eine
Instanz-Token (festgeschrieben auf den ersten Administrator), `owner_id` hat keinen Fremdschlüssel, und die App löscht beim Löschen einer Zeile deren Kinder nicht mit (3a).

### 10.1 Gerätetoken je Person

**Ziel:** Das iPad schreibt als die Person, der es gehört, und in das Gerät wird nie ein Passwort getippt. Der gemeinsame Token wird abgelöst.

**Kopplung (einmal je Gerät):**
1. Die Person ist im Browser angemeldet (mit zweitem Faktor) und wählt in ihren Einstellungen „Gerät koppeln“. Der Server macht einen **Kopplungscode**: 10 Zeichen aus dem Alphabet der Basis (Base32, 50 Bit), 10 Minuten gültig, **einmal** benutzbar, nur als SHA-256 gespeichert. Angezeigt als Text und als QR-Code (Adresse des Servers und Code; die Basis hat `qr.js`).
2. Auf dem iPad gibt die Person den Code ein oder scannt den QR-Code (die Kamera für Scans ist schon in der App). Die App ruft `POST /api/v1/pair` mit Code und Gerätename. Das ist die **eine** Stelle der Tür `/api/v1`, die ohne Token antwortet (Ausnahme in `V1Door.CheckAsync`, nur dieser Pfad, nur POST); sie hat ihre eigene Bremse (die vom Token-Weg, je Adresse, plus eine Sperre je Code nach drei Fehlversuchen).
3. Der Server antwortet **einmal** mit dem Gerätetoken: 256 zufällige Bit, Präfix `nt1_` (damit ein verlorenes Token in einem Fund erkennbar ist), und speichert nur den SHA-256. Die App legt ihn im Schlüsselbund ab. Der Code ist verbraucht.

**Tabelle** `device_tokens(id uuid pk, person_id uuid not null, name text, hash text unique, created_at, last_seen, last_address, revoked_at)`; Index auf `hash`. Kein Fremdschlüssel (wie bei `owner_id`), aber das Löschen der Person löscht ihre Token (10.3).

**Die Tür:** `CheckAsync` sucht den SHA-256 des gesendeten Tokens zuerst unter den Gerätetoken (nicht widerrufen, Person aktiv), sonst gilt der alte Instanz-Token. Ergebnis wie heute in `Items`: die Person und, neu, das Gerät. Ein Gerätetoken gilt **für seine Person**, auch wenn sie nicht Administrator ist; der alte Token bleibt „die festgeschriebene Person“. `last_seen` wird höchstens einmal je Minute geschrieben. Falsche Token laufen weiter in die Bremse und ins Sicherheitsprotokoll (nie der Token selbst). Das Sicherheitsprotokoll nennt künftig bei Verstößen gegen die Schreibregeln den **Gerätenamen** statt „app-token“.

**Ablösen des alten Tokens, in drei Schritten:**
1. Meilenstein 3 liefert die Kopplung; der alte Token bleibt gültig (Geräte, die noch nicht gekoppelt sind, gleichen weiter ab).
2. Jedes Gerät wird gekoppelt (die App meldet beim ersten Start nach dem Update „Bitte koppeln“, arbeitet aber mit dem alten Token weiter, bis die Person es tut).
3. Der Administrator schaltet „Gemeinsamer Token“ in der Verwaltung **aus** (Einstellung in der Datenbank, nicht in der Umgebung; mit Rückfrage und Protokollzeile). Danach ist `Sync__Token` wirkungslos und kann bei der Umstellung der Compose-Datei verschwinden. Wieder einschalten geht nur, solange die Umgebung ihn noch trägt.

**Verwaltung der Geräte:** Jede Person sieht in ihren Einstellungen ihre Geräte (Name, gekoppelt am, zuletzt gesehen, Adresse gekürzt) und kann sie **entfernen**; ein entferntes Gerät bekommt 401, die App zeigt „Bitte neu koppeln“. Der Administrator sieht je Person nur die **Anzahl** und kann ein Gerät widerrufen (Protokollzeile), nie einen Token lesen.

**App-Änderung (klein, eigener Entwurf, einzige erzwungene in diesem Meilenstein):** Kopplungsbildschirm (Code eintippen oder scannen), Schlüsselbund, Token senden, bei 401 die Kopplung anbieten, die Antwort von `push` auswerten (`accepted`, siehe 3a). Ohne diese Änderung läuft die App unverändert mit dem alten Token. Gebaut wird sie wie bisher in der CI (kein Mac); getestet auf dem Gerät erst, wenn jemand es installiert.

**Verworfen:** Passwort am Gerät eintippen (Regel, und es gäbe keinen zweiten Faktor); den Instanz-Token pro Person ableiten (ein Leck des einen verrät alle). **Offen:** Ob der Hub einen Gerätefluss (Device Authorization Grant) anbietet; dann könnte die Kopplung ihn nutzen. Das habe ich nicht gelesen und baue nicht darauf.

### 10.2 Freigaben, Stufe 1 (nur Web)

Wie in 3a entschieden: Freigaben gelten im Web, nicht im Abgleich; die App der Empfängerin bekommt freigegebene Hefte erst mit Stufe 2.

- **Tabelle** `shares(id, notebook_id, owner_id, recipient_id, mode, created_at, revoked_at)`; `mode` zunächst nur `read`. Geteilt wird **ein Heft** (nicht ein Ordner; ein Ordner kann später dazukommen). Das Teilen gibt es nur der Besitzerin des Hefts.
- **Wege** (alle hinter der Sitzung, alle prüfen die Freigabe bei **jeder** Anfrage, nichts wird zwischengespeichert): `shared` (Liste „mit mir geteilt“), `shared/notebooks/{id}`, `…/pages`, `shared/pages/{id}`, `shared/assets/{id}`. Eine Datei darf die Empfängerin nur lesen, wenn sie in dem freigegebenen Heft **vorkommt** (Seiteninhalt, Objekt oder Cover); sonst könnte eine Freigabe jede Datei der Besitzerin öffnen. Dafür gibt es eine Abfrage „Dateien eines Hefts“ (liest die Nutzlasten der Seiten und Objekte).
- **Wer wählbar ist:** Die Basis gibt die Personenliste nur dem Administrator. Für das Teilen braucht jede Person eine **Namensliste** der aktiven Personen (nur Name und id, kein Konto, keine Rollen): `GET /api/web/v1/people`. In einer Familie ist das vertretbar; bei größerem Kreis wäre es eine Einstellung (Teilen nur mit Eingeladenen). Zu klären mit dem Eigentümer.
- **Oberfläche:** „Teilen“ am Heft (Personen wählen, Freigabe entziehen), „Mit mir geteilt“ als Eintrag in der Bibliothek, ein Hinweis „nur lesen“, Seiten wie die eigenen. Entziehen wirkt sofort (die nächste Anfrage bekommt 404).
- **Protokoll:** Freigabe und Entzug als Zeilen im Sicherheitsprotokoll (Person, Gegenüber, Heft-id).
- **Tests (Zwei- und Drei-Personen):** Empfängerin liest Seiten, Striche und eingebundene Dateien des freigegebenen Hefts; sie liest **kein** anderes Heft derselben Besitzerin, keine nicht eingebundene Datei, und eine Dritte nichts davon; Entzug wirkt bei der nächsten Anfrage; ein gelöschtes oder in den Papierkorb gelegtes Heft ist nicht mehr abrufbar; der Abgleich der Empfängerin ändert sich nicht (der Pull liefert nichts Geteiltes); der App-Token öffnet nichts davon.

### 10.3 Löschen einer Person: nie Hefte stehen lassen

**Regel:** Eine Person mit Heften, Dateien oder Freigaben wird **nicht** gelöscht, bevor ihre Hefte versorgt sind. Der Administrator wählt, in dieser Reihenfolge der Vorsicht: (a) **Übergeben** an eine andere Person (10.4), (b) **Löschen** der Hefte endgültig (zweite Rückfrage mit Zahlen, vorher eine Sicherung von Hand, Protokollzeile). Das Löschen der Person selbst läuft danach über die Verwaltung der Basis.

**Umsetzung ohne die Kopie der Basis zu ändern:** Die Basis löscht über `DELETE /api/people/{id}`. Davor liegt eine **eigene Prüfung** unserer App (eine Middleware, die genau diesen Weg erkennt und mit `409 person.has_notes` und einem Satz antwortet, solange die Person Heften, Dateien oder Gerätetoken hat; die Verwaltung der Basis zeigt die Meldung). Besser wäre ein **Haken in der Basis** (`BaseAppOptions.BeforeDeletePerson`, eine Funktion, die ein Nein mit Text liefern darf); das gebe ich dem Orchestrator für die Basis mit. Gesperrte Personen behalten ihre Hefte (nichts geschieht).
**Beim Löschen einer Person mit versorgten Heften:** ihre Gerätetoken, Freigaben (als Besitzerin und als Empfängerin) und Kopplungscodes werden mit gelöscht (in derselben Prüfung, vor der Löschung der Basis; scheitert eines, wird nichts gelöscht).

### 10.4 Übergabe von Heften an eine andere Person (Verwaltungsfunktion)

**Wozu:** (1) vor dem Einspielen, falls die Hefte auf dem iPad nicht der Person gehören, die die Einrichtung abschließt (offene Frage an den Eigentümer); (2) beim Löschen einer Person (10.3); (3) wenn jemand die Hefte einer anderen Person übernimmt.

**Was übergeben wird:** alles einer Person oder ein Teil (gewählte Ordner und Hefte samt allem darunter: Seiten, Striche, Objekte, Noten, Karten, Dateien). Der Umfang ist eine Wurzelliste; die **Hülle** wird über `parent_id` bestimmt (Ordner, Hefte, Seiten, Striche, Objekte, Noten, Karten).

**Ablauf, in einer Transaktion unter der Sperre des Abgleichs:**
1. **Vorschau** (ohne Änderung): Anzahl der Zeilen je Art, Seiten, Dateien, Hinweise. **Bricht ab** (mit Liste), wenn eine Datei auch von Zeilen **außerhalb** der Übergabe gebraucht wird (ein Bild in zwei Heften): so etwas wird nicht stillschweigend zerrissen; die Lösung (Datei kopieren) ist ein eigener Schritt, falls es vorkommt.
2. **Sicherung von Hand** (nur wenn der Administrator das Ja gibt; ohne Passphrase wird gesagt, dass der Schlüssel nicht in ihr liegt).
3. **Umschreiben:** `owner_id` der Hülle und `assets.owner_id` der Dateien auf die neue Person; jede umgeschriebene Zeile bekommt ein **neues `seq`**, **Eltern vor Kindern** (nach der Tiefe sortiert), damit der Pull der neuen Person sie vollständig und in sinnvoller Reihenfolge liefert (bei ihr sind es neue Zeilen).
4. **Der bisherigen Person die Löschung melden:** Für jede umgeschriebene Zeile entsteht ein Eintrag in `removed_rows(owner_id, kind, id, parent_id, seq)`; der Pull der bisherigen Person liefert sie als **Löschungen** (`deleted: true`), **für jede Zeile einzeln**, nicht nur für die Wurzeln, weil die App beim Löschen die Kinder nicht mitnimmt (3a). Ohne das behielten ihre Geräte die Hefte und schickten Änderungen, die stillschweigend ausgelassen werden. Das ist derselbe Weg wie der „synthetische Löschung“ in 3a Stufe 2; er wird hier zum ersten Mal gebraucht und für Freigaben wiederverwendet.
5. **Protokoll:** Wer, von wem an wen, Zahlen, Zeit (keine Inhalte).

**Rückgängig:** nicht als Knopf; dieselbe Funktion in die Gegenrichtung stellt es wieder her (die Hefte bekommen dann wieder neue `seq`).

**Was dabei auf dem iPad geschieht:** Die bisherige Person verliert die Hefte auf ihren Geräten beim nächsten Abgleich (Löschungen); die neue Person bekommt sie, sobald ihr Gerät gekoppelt ist. **Vorsicht:** Solange der **alte gemeinsame Token** gilt, steht er für die festgeschriebene Person (10.1). Übergibt der Administrator seine Hefte an jemand anderen, bevor ein Gerät gekoppelt ist, verliert das iPad (das noch den alten Token trägt) alles. Darum gehört zur Übergabe **vor der Kopplung** die zweite Verwaltungsfunktion:

**„Person hinter dem alten Token ändern“:** Der Administrator wählt eine andere aktive Person (mit Rückfrage, Protokoll, nur wenn der Eigentümer seinen zweiten Faktor eben bestätigt hat); `token_owner` wird neu geschrieben. Eine Übergabe der Hefte auf diese Person und diese Umstellung bilden **einen** geführten Ablauf („Hefte und iPad an Person Y geben“), damit nichts dazwischen steht.

**Zur offenen Frage vor dem Einspielen (wem gehört das iPad?):**
- **Fall A:** Es gehört der Person, die die Einrichtung abschließt. Nichts weiter nötig; Meilenstein 3 kann nach dem Einspielen kommen.
- **Fall B:** Es gehört einer anderen Person. Dann gibt es zwei Wege: B1, **diese Person schließt die Einrichtung ab** (sie wird die erste Administratorin; der Eigentümer lädt sich selbst danach ein und wird Administrator): das ist **ohne** Neues machbar und mein Vorschlag, wenn es passt. B2, der Eigentümer schließt ab und übergibt später: braucht die geführte Übergabe aus diesem Abschnitt **vor** dem Einspielen.

### 10.5 Scheiben und Reihenfolge

3a Gerätetoken und Kopplung (Server mit Tests; der Entwurf der App-Änderung getrennt) · 3b Löschprüfung und Übergabe (ohne die Umstellung des Tokens zuerst als Vorschau) und die Umstellung · 3c Freigaben im Web · 3d Verwaltung der Geräte, Abschalten des gemeinsamen Tokens. Jede Scheibe lauffähig, mit Tests mit mehreren Personen, STATE.md am Ende.
**Abhängigkeiten/Risiken:** Die Löschungen für die bisherige Person (10.4, Punkt 4) sind der heikelste Teil: ein Fehler lässt Hefte auf einem Gerät stehen oder löscht sie dort zu früh. Der Test dafür braucht einen Abgleich in zwei Richtungen mit der echten Swift-Logik; die kann hier nur als Vertrag nachgebildet werden, **nicht auf einem iPad** geprüft werden. Das ist vor dem ersten echten Gebrauch ein offener Punkt.
