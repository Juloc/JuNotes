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

1. **Migrationen** laufen wie heute nummeriert (`0008…`) in einer Transaktion, mit Sicherung davor (`DatabaseUpgrade`). Der Umzug darf keine Zeile
   verändern, nur Spalten und Tabellen anlegen.
2. **Besitzer:** Eine SQL-Migration kann ihn nicht zuweisen, weil das Konto des Eigentümers erst im Einrichtungsassistenten der Basis entsteht. Darum:
   `0008` legt `entities.owner_id uuid` (und gleiche Spalten an den Dateitabellen) **leer** an. **Leer heißt: gehört dem ersten Administrator.** Beim Anlegen
   des ersten Kontos werden alle leeren Zeilen in **einer** Anweisung diesem Konto zugewiesen; der alte App-Token steht ab da für diese Person. Bis dahin
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
   - **Wie der Server eine Verletzung beantwortet, ist durch die App festgelegt** (gelesen in `SyncEngine.swift`): Die App schickt ihre Änderungen in
     Stapeln und bricht den **ganzen** Abgleich ab, sobald der Server einen Stapel mit einem Fehlerstatus ablehnt (`TransportError.rejected`). Der Stapel
     bleibt im Postausgang und wird beim nächsten Versuch wieder geschickt: **eine einzige abgelehnte Zeile blockiert dieses Gerät dauerhaft** (die
     Statuszeile zeigt „Server nicht erreichbar“). Darum antwortet der Server auf eine Zeile, die gegen die Schreibregeln verstößt, **nicht mit einem Fehler**,
     sondern lässt sie aus, nimmt den Rest des Stapels an, meldet in `accepted` nur die angenommenen und schreibt eine Warnung ins Protokoll. Ob das die
     gewünschte Strenge ist, ist eine Entscheidung für den Orchestrator (Alternative: Fehler, dann bräuchte die App eine Änderung).
4. **`seq` bleibt global.** Der Pull einer Person liefert nur ihre Zeilen (und freigegebene) mit `seq` über ihrem Merker. Lücken in der Folge sind für den
   Merker der App harmlos, weil sie nur „größer als“ benutzt (**ungeprüft: App-Code noch einmal gegen Lücken lesen**).
5. **Der bestehende App-Token** wird zum „Gerät des Eigentümers“: Der Server ordnet ihn bei jeder Anfrage der Person Eigentümer zu. Neue Token je Gerät
   und Person kommen mit Meilenstein 3 aus der Basis (Geräte-Modul). Der alte Token wird erst abgeschaltet, wenn die App ihn nicht mehr schickt, und das
   schaltet der Eigentümer von Hand.
6. **Freigaben:** eigene Tabelle `shares(entity_id, recipient_id, mode, granted_seq, revoked_seq)`; Lesen prüft Besitz oder Freigabe. Ein freigegebenes Heft
   bringt alle Seiten und Striche darunter mit (Zugriff über die `parent_id`-Kette, nur für Zeilen des Besitzers). **Offener Entwurfspunkt für Meilenstein 3:**
   siehe Abschnitt 3a.

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

**Folge:** Stufe 1 (nur Web) bleibt richtig. Stufe 2 ist erst entwerfbar, wenn der Orchestrator entschieden hat, ob eine kleine App-Änderung (Kinder mitnehmen,
lesend markieren) eingeplant wird.
7. **Beweis:** vor Meilenstein 1 ein Test, der einen Stand der Datenbank (Seite mit Strichen, Note, Karte, Objekt, Scan) durch alle neuen Migrationen
   schickt und danach über `/api/v1` mit dem alten Token exakt dieselben Antworten bekommt wie vorher.

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

### 5a. `/api/v1` ist eine eigene Tür

Der Wächter der Basis verlangt für alles unter `/api` eine Sitzung. `/api/v1/*` wird deshalb **einmal an einer Stelle** als öffentlicher Pfad mit eigener
Prüfung (Bearer-Token) angemeldet. Dazu zwei Tests: (1) kein Weg unter `/api/v1` antwortet ohne Token; (2) der App-Token gilt **nie** unter `/api/web/v1`
(und ein Sitzungs-Cookie nie unter `/api/v1`).

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

- Pro Person: ein Link zu einem Kalender-Abo oder Feed (Eintrag in der Datenbank der Person). Kein Passwort, kein Benutzername, nichts hinter einer
  Anmeldung. Der Server liest den Link mit `SchoolImportService` ohne Zugangsdaten.
- Wo die Schule keinen Link anbietet, gibt es nichts. Wir bauen keinen Ersatz.
- `UntisClient` und `School__Untis__*` werden entfernt, **wenn der Eigentümer das entscheidet** (er wird gefragt). Bis dahin bleibt der Code unangetastet und
  inaktiv.

## 9. Reihenfolge der Risiken

1. Besitzer und Abgleich (Abschnitt 3, Punkt 3): ein Fehler lässt eine Person fremde Daten sehen oder überschreiben.
2. Die App-Seite: die App wird auf dem Gerät nie vom Server aus getestet, sondern nur übersetzt; eine Änderung am Vertrag fiele erst auf dem iPad auf.
3. Versionsfrage (Abschnitt 4): billig zu prüfen, teuer zu spät.
