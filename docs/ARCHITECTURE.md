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
| Razor-Seiten unter `src/JuNotes.Frontend/Pages` (`Notes/`, `Admin/`, `Login`) | Seiten im Baukasten (`ui/`); gleicher Inhalt zuerst, dann neue Funktionen |
| Einstellungen in `AiSettingsStore`, `AiProfileStore` und Umgebungsvariablen | Einstellungen der Basis (Web-Modul) für das, was die Verwaltung ändert; Umgebungsvariablen nur für Geheimnisse |
| Sicherheitsköpfe, Gesundheitsroute, Einrichtungsassistent | `Web`-Modul der Basis (`/api/health`, `/api/about`); die heutigen `/health/live` und `/health/ready` bleiben, bis nichts mehr darauf zeigt |

**Bleibt zunächst daneben stehen:** der gemeinsame `Sync__Token`. Er prüft nur den Zugriff auf `/api/v1`, nicht die Person (siehe 3).

## 3. Daten ohne Verlust mitnehmen

Grundsatz: **Nur hinzufügen, nie umbauen.** Die App kennt die Form der Daten und wird nicht zugleich geändert.

1. **Migrationen** laufen wie heute nummeriert (`0008…`) in einer Transaktion, mit Sicherung davor (`DatabaseUpgrade`). Der Umzug darf keine Zeile
   verändern, nur Spalten und Tabellen anlegen.
2. **Besitzer:** `0008` fügt `entities.owner_id uuid` und gleiche Spalten an den Dateitabellen hinzu. Alle vorhandenen Zeilen bekommen den Eigentümer. Das
   Konto des Eigentümers existiert dann schon (Meilenstein 1, Reihenfolge: erst Konto, dann Migration; sonst läuft die Besitzer-Zeile ins Leere).
   Die Spalte wird erst `not null`, wenn der Abgleich gegen alte Stände geprüft ist.
3. **Der Primärschlüssel `(kind, id)` bleibt.** Die ids sind zufällige UUIDs der Geräte; Kollisionen zwischen Personen sind praktisch ausgeschlossen. Wer sie
   absichtlich provoziert (eine Person schickt die id einer fremden), darf nichts überschreiben: der Server prüft beim Schreiben, dass eine vorhandene Zeile
   derselben Person gehört. **Das ist die zentrale Sicherheitsregel des Umzugs und bekommt eigene Tests.**
4. **`seq` bleibt global.** Der Pull einer Person liefert nur ihre Zeilen (und freigegebene) mit `seq` über ihrem Merker. Lücken in der Folge sind für den
   Merker der App harmlos, weil sie nur „größer als“ benutzt (**ungeprüft: App-Code noch einmal gegen Lücken lesen**).
5. **Der bestehende App-Token** wird zum „Gerät des Eigentümers“: Der Server ordnet ihn bei jeder Anfrage der Person Eigentümer zu. Neue Token je Gerät
   und Person kommen mit Meilenstein 3 aus der Basis (Geräte-Modul). Der alte Token wird erst abgeschaltet, wenn die App ihn nicht mehr schickt, und das
   schaltet der Eigentümer von Hand.
6. **Freigaben:** eigene Tabelle `shares(entity_id, recipient_id, mode)`; Pull und Lesen prüfen Besitz oder Freigabe. Ein freigegebenes Heft bringt alle
   Seiten und Striche darunter mit (Zugriff über die `parent_id`-Kette).
7. **Beweis:** vor Meilenstein 1 ein Test, der einen Stand der Datenbank (Seite mit Strichen, Note, Karte, Objekt, Scan) durch alle neuen Migrationen
   schickt und danach über `/api/v1` mit dem alten Token exakt dieselben Antworten bekommt wie vorher.

## 4. .NET 11 hier, .NET 10 in der Basis

- Die Basis wird **als Quelltext in die App kopiert** (`sync-base.sh`), nicht als Paket. Der kopierte Code wird dann mit dem Zielframework der App
  übersetzt. Code für net10 übersetzt unter net11 in aller Regel; die Gegenrichtung (net11-Pakete in net10) geht nicht.
- **Ungeprüft:** ob der Basiscode unter `net11.0` und unseren Analysatoren (Warnungen als Fehler, zum Beispiel CA1720, CA1861, CA2026) ohne Änderung durchgeht.
  Das ist die erste Probe in Meilenstein 0 und kostet einen Nachmittag.
- **Entscheidung für den Orchestrator:** (a) `notes` bleibt auf net11 und die kopierten Dateien werden dort geprüft; (b) `notes` geht zurück auf net10.
  Ich empfehle (a), solange die Probe grün ist. Ein Rückschritt würde Sprachfunktionen kosten, die der Code benutzt (C# 15), und den Docker-Unterbau ändern.
- Die Datenbank bleibt PostgreSQL 18 mit Npgsql 10; das passt zu beiden.

## 5. Welche Schnittstellen die Web-App braucht

Heute liest die Web-Ansicht die Datenbank direkt in Razor-Seiten (`Notes/Index`, `Book`, `Asset`). Für Meilenstein 2 reicht das **ohne** neue
Schnittstelle. Eine eigene JSON-Schnittstelle für den Browser ist erst für Suche, Lernhilfen, KI und Zeichnen nötig (Meilenstein 6 bis 8). Sie ist von
`/api/v1` getrennt:

| Bereich | Richtung | Anmeldung |
|---|---|---|
| `/api/v1/*` (heute: `status`, Abgleich `push`/`pull`/`info`, Dateien `PUT`/`GET`, Schule `status`/`calendar`/`news`, KI `profiles`/`{task}`, Export `status`/`paperless`) | iPad-App | Bearer-Token. **Eingefroren.** Nur additive Felder. |
| `/api/web/v1/*` | Browser | Sitzungs-Cookie der Basis, CSRF-Schutz, **nie** der App-Token |

Vorgesehen für `/api/web/v1` (Namen sind Vorschlag, keine Seitenentwürfe): `me`; Bibliothek (Ordnerbaum); Heft mit Seiten; Seite mit Strichen (zum
Zeichnen im Browser); Datei; Suche; Noten; Karten; Stundenplan der Person; KI-Aufgaben und Nutzung der Person; Freigaben.

**Wichtig für die Suche:** Der erkannte Text (OCR) liegt heute nur auf dem iPad (siehe `docs/PHASE5.md`, Punkt 5). Eine Suche im Web gibt es erst, wenn
der Text mit synchronisiert wird. Das ist eine **additive** Erweiterung des Abgleichs (neues Feld oder neue `kind`), kein Umbau.

## 6. KI über den Hub und die eigene Brücke

**Heute:** `LearningService` ruft `ICodexRunner.RunAsync(CodexRequest, ct)` (`src/JuNotes.Integrations/Codex/CodexRunner.cs`). Der Aufruf startet
`codex exec` (nur lesen, Schema-Ausgabe, Modell und Aufwand per Aufruf). Zählung in `AiUsageStore`, Profile in `AiProfileStore`.

**Der Hub** (nach seiner Beschreibung, beim Bauen gegen den laufenden Hub zu prüfen, ich habe nur die Beschreibung gelesen): Schnittstelle
`/api/ai/v1` mit `GET /models`, `POST /chat` (auch als SSE-Strom) und `GET /usage`; ein Schlüssel je App als `Bearer`, serverseitig nur als SHA-256
abgelegt; Budgets je App, Tag und Monat; Rate-Limit je Minute; standardmäßig aus (`503 ai_disabled`).

**Vorschlag:** `ICodexRunner` bekommt Geschwister hinter einer gemeinsamen Schnittstelle (`IAiBackend`):

- `CodexBackend`: der bisherige Runner, unverändert.
- `HubBackend`: spricht `/api/ai/v1` mit dem Schlüssel der App (Umgebung `Ai__Hub__Key`, Adresse `Ai__Hub__BaseUrl`).

**Wie entschieden wird, welcher aktiv ist** (eine Regel, kein Zufall):

1. Sind `Ai__Hub__BaseUrl` **und** `Ai__Hub__Key` gesetzt, ist der Modus **Hub**. Sonst ist er **eigene Brücke**.
2. Im Modus Hub wird `CodexBackend` **gar nicht registriert**: kein `codex`-Prozess, keine Codex-Seite in der Verwaltung, keine Anmeldung per Gerätecode,
   kein Hinweis „Codex verbunden“. Die Verwaltung zeigt statt dessen „KI über den Hub“ mit Nutzung.
3. **Kein stiller Rückfall.** Ist der Hub nicht erreichbar oder aus (`503 ai_disabled`), bekommt die Person eine klare Meldung. Der Server weicht nicht
   heimlich auf die eigene Brücke aus. Grund: zwei Wege würden doppelt zählen und doppelt abrechnen, und niemand sähe, welcher gerade läuft.
4. Den Modus ändert nur, wer die Umgebung ändert. Das ist beabsichtigt; die Verwaltung zeigt ihn nur an.

**Sparsamkeit (Entscheidung 7), wie es gebaut würde:**

| Anforderung | Umsetzung |
|---|---|
| Grenze pro Person und Tag | **Der Hub zählt pro App, nicht pro Person.** Darum zählt `notes` selbst (`AiUsageStore` bekommt `person_id`) und prüft vor jedem Aufruf. Die Grenze der App im Hub bleibt die äußere Schranke. **Offene Frage an den Orchestrator:** kann der Hub die Person als Zusatzangabe annehmen und mitzählen? Dann entfiele die doppelte Zählung. |
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
- `UntisClient` und `School__Untis__*` werden entfernt.

## 9. Reihenfolge der Risiken

1. Besitzer und Abgleich (Abschnitt 3, Punkt 3): ein Fehler lässt eine Person fremde Daten sehen oder überschreiben.
2. Die App-Seite: die App wird auf dem Gerät nie vom Server aus getestet, sondern nur übersetzt; eine Änderung am Vertrag fiele erst auf dem iPad auf.
3. Versionsfrage (Abschnitt 4): billig zu prüfen, teuer zu spät.
