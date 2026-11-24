# Umfang: JuNotes wird `notes`

Stand: 24.11.2026. **Entwurf zur Durchsicht durch den Orchestrator.** Es wird nichts gebaut, bevor er den Plan gesehen hat. Dies ist ein Plan, kein Code und
kein Seitenentwurf: Das Aussehen kommt aus dem Baukasten der Basis (dort ändert es sich gerade, 0.3.x), hier steht nur die Struktur.
Technische Einzelheiten stehen in [ARCHITECTURE.md](ARCHITECTURE.md), der heutige Stand in [STATE.md](STATE.md).

## Das Ziel in einem Satz

`notes` ist ein Notizbuch für mehrere Menschen, das im Browser und auf iPad und iPhone dieselben Hefte zeigt, jede Person nur die eigenen sieht, ohne
dass die heutige iPad-App dabei kaputtgeht, und dessen KI über den gemeinsamen Zugang des Hubs läuft.

## Entscheidungen des Eigentümers (24.11.2026)

1. **Name und Umzug.** JuNotes heißt künftig `notes`. Das Umbenennen des Repositorys auf GitHub und des lokalen Ordners macht der Orchestrator, wenn in
   dem Ordner keine Sitzung läuft. Server und Web ziehen schrittweise auf die gemeinsame Basis um; die Swift-Logik der App bleibt.
2. **Ziele:** Web, iOS und eine Web-Verwaltung. Ein Windows-Programm kommt später.
3. **iOS ist eine App für iPad und iPhone.** Auf dem iPad mit Pencil, auf dem iPhone zum Lesen, Tippen und Scannen. Verteilung wie heute. Nur als Notiz für
   später: eine eigene Installationsquelle mit Selbst-Update.
4. **Im Browser geht alles außer den Feinheiten des Pencils:** Hefte, Seiten, Tippen, Scans, Suche, Lernhilfen und KI. Gezeichnet wird mit Maus, Finger
   oder Stift, ohne Druck, Neigung oder Radiergeste.
5. **Menschen:** der Eigentümer und die Familie, mehrere Personen mit eigenen Heften. Anmeldung über den Hub (JulOS) oder mit einem eigenen Konto. Jede
   Person sieht nur das Eigene; einzelne Hefte lassen sich teilen.
6. **Offline:** auf iOS ja (lokal arbeiten, später abgleichen), im Web nur online.
7. **KI: beides, sauber.** Mit Hub-Anbindung läuft alles über den KI-Zugang des Hubs, die eigene Codex-Brücke ist dann aus (nicht sichtbar an, keine
   doppelte Anmeldung). Ohne Hub bleibt die eigene Brücke. Der Weg über den Hub soll „perfekt“ sein und keine Token verschwenden: Grenzen pro Person und
   Tag, keine doppelten Anfragen, keine Anfragen im Hintergrund ohne Anlass, Abbruch beim Verlassen der Seite, sichtbare Nutzung.
8. **Verwaltung im Web:** Personen und Rollen (aus der Basis), KI-Profile und Grenzen, Sicherungen und Geräte mit Abgleichstand. **Die Schule gehört nicht
   in die Verwaltung.** Jede Person verbindet Stundenplan und Schul-Intranet mit ihrem eigenen Zugang, nur für sich.

### Regeln für alles Schulische

- Nur offizielle Wege für den Kontoinhaber: Kalender-Abo, Feed, Export.
- Sie meldet sich selbst an. **Nie ein Passwort speichern oder eintippen.**
- Nichts hinter der Anmeldung abgreifen, solange die Schule das nicht erlaubt hat.
- Keine Schülerdaten aus dem System der Schule.
- Schulname und Domain stehen nie in einem Repository (nur in der nicht eingecheckten `LOCAL.md`).

Folge für den Code von heute: Der WebUntis-Client (`src/JuNotes.Backend/School/UntisClient.cs`) legt ihr Passwort in der `.env` des Servers ab und
widerspricht diesen Regeln. Er ist ausgeliefert, aber ohne Zugangsdaten inaktiv. **Empfehlung: entfernen** (Meilenstein 0). Das ist eine Entscheidung des
Eigentümers, nicht meine.

## Meilensteine

Jeder endet in etwas, das er aufmachen kann. Reihenfolge und Zuschnitt gegenüber dem Vorschlag sind unten begründet.

### 0. Aufräumen und Vorbedingungen *(neu)*

- WebUntis-Client und Passwort-Einstellungen entfernen (Entscheidung offen, siehe oben).
- Prüfen, ob der Code der Basis (.NET 10) in dieses Projekt (.NET 11) hineinpasst; sonst entscheidet der Orchestrator über die Zielversion.
- Den Abgleichvertrag mit der iPad-App als Test einfrieren (Golden-Tests auf `/api/v1`), damit jeder spätere Schritt beweisbar nichts bricht.
- **Aufmachen kann er:** eine grüne Testliste, die den Vertrag der App beschreibt. Am laufenden System ändert sich nichts.

### 1. Anmeldung und Personen aus der Basis, neben dem App-Token

- Konten, Sitzungen, Rollen, Einladungen aus der Basis; Anmeldung über den Hub oder eigenes Konto (Argon2id, TOTP).
- **Zusätzlich:** jede Zeile in `entities` bekommt einen Besitzer (Migration). Alle heutigen Daten gehören dem Eigentümer. Das gehört in diesen Meilenstein
  und nicht in den späteren, weil jede spätere Zeile sonst nachträglich umgeschrieben werden müsste.
- Der heutige `Sync__Token` der App gilt weiter und steht für „Gerät des Eigentümers“. Die App merkt nichts.
- Die heutigen Passwörter `ADMIN_PASSWORD` und `WEB_PASSWORD` fallen weg, sobald das eigene Konto da ist.
- **Aufmachen kann er:** im Browser mit eigenem Konto oder über den Hub anmelden; die iPad-App synchronisiert unverändert weiter.

### 2. Die Web-App im neuen Baukasten, mit denselben Funktionen wie heute

- Lesen: Bibliothek, Heft, Seite, Scans, Cover. Dazu Noten, Karten, Stundenplan, Suche, wie es heute möglich ist.
- Heutige Admin-Seiten (Übersicht, Codex, Profile, Sicherungen) ziehen in den Baukasten um, noch ohne neue Funktionen.
- **Aufmachen kann er:** dieselbe Web-Ansicht wie heute, nur im neuen Aussehen und hinter der neuen Anmeldung.

### 3. Mehrere Personen und Teilen

- Zweite und dritte Person mit eigenen Heften; jede sieht nur das Eigene. Einzelne Hefte freigeben (lesen; später bearbeiten).
- Die iPad-App meldet sich mit einem **Gerätetoken je Person** an (aus der Basis) statt mit dem gemeinsamen Token. Das ist die einzige App-Änderung in
  diesem Plan, die der Server erzwingt; der alte Token bleibt bis dahin nutzbar.
- **Aufmachen kann er:** ein zweites Konto anlegen, ein Heft teilen, und es erscheint dort, nicht bei der dritten Person.

### 4. Schule pro Person *(vorgezogen, damit Meilenstein 5 und 6 mit echten Daten laufen)*

- Jede Person hinterlegt für sich einen Kalender-Abo-Link oder Feed. Kein Passwort, kein Abgreifen. Was die Schule nicht anbietet, gibt es nicht.
- **Aufmachen kann er:** sein eigener Stundenplan in der App und im Web, aus seinem eigenen Link.

### 5. Web-Verwaltung

- Personen und Rollen (Basis), KI-Profile und Grenzen, Sicherungen, Geräte mit Abgleichstand. Keine Schule.
- **Aufmachen kann er:** eine Verwaltungsseite im Browser, mit der er alles Genannte einstellt.

### 6. KI über den Hub

- Anbindung an den KI-Zugang des Hubs, der Schalter zwischen Hub und eigener Brücke, Grenzen pro Person und Tag, Abbruch, sichtbare Nutzung.
- **Aufmachen kann er:** ein Quiz oder Cover erzeugen und die Nutzung dazu sehen; mit Hub ohne jede Codex-Anmeldung im Server.
- **Hängt am Hub:** Der Hub zählt heute pro App und Tag, nicht pro Person. Die Grenze pro Person muss `notes` selbst führen (siehe ARCHITECTURE.md).

### 7. iPhone-Layout

- Dieselbe App, kompakte Darstellung: Hefte lesen, Text tippen, Scannen. Kein Pencil nötig.
- **Aufmachen kann er:** die App auf dem iPhone. **Geprüft wird nur, was gebaut und übersetzt ist; ob es sich gut anfühlt, zeigt erst das Gerät.**

### 8. Zeichnen im Browser

- Zeichenfläche für Maus, Finger und Stift ohne Druck und Neigung; Striche gehen durch denselben Abgleich wie die der iPad-App.
- **Aufmachen kann er:** im Browser auf ein Heft schreiben und es auf dem iPad sehen.

### Änderungen gegenüber dem Vorschlag, mit Gründen

| Änderung | Grund |
|---|---|
| Neuer Schritt 0 | Ohne einen eingefrorenen Vertrag lässt sich „nichts bricht“ nicht belegen. Die Versionsfrage (.NET 10 gegen 11) entscheidet über die Form von Schritt 1. |
| Besitzerspalte schon in 1 | Spätere Nachrüstung hieße, alle Zeilen und alle Abfragen noch einmal anzufassen. |
| Schule pro Person (4) als eigener Schritt | Die Entscheidung 8 verlangt sie je Person; vor dem Verwalten und der KI, weil beide Daten brauchen, die einer Person gehören. |
| Verwaltung nach „mehrere Personen“ | Personen und Rollen kann sie erst zeigen, wenn es mehrere gibt. |
| KI nach Verwaltung | Grenzen pro Person brauchen Personen und eine Seite, auf der man sie einstellt. |
| Gerätetoken je Person in 3 | Der gemeinsame Token kann nicht sagen, wer schreibt. |

## Was bewusst draußen bleibt

- **Kein Neubau der iPad-Logik** (Datenhaltung, Abgleich, PencilKit). Sie bleibt, wie sie ist.
- **Keine Seitenentwürfe in diesem Plan.** Aussehen kommt aus der Basis.
- **Keine Bearbeitung durch mehrere gleichzeitig am selben Strich** (kein Echtzeit-Zusammenarbeiten). Teilen heißt zunächst lesen.
- **Kein Offline im Web.**
- **Keine Schule in der Verwaltung**, kein gemeinsamer Schulzugang, kein Auslesen hinter der Schul-Anmeldung.
- **Windows-Programm** und **eigene Installationsquelle mit Selbst-Update** später.
- **Push-Nachrichten** (brauchen ein Developer-Konto); es bleiben lokale Benachrichtigungen.
- **Kein Umzug der Daten in ein anderes Format.** Der Abgleich auf Strichebene bleibt.
