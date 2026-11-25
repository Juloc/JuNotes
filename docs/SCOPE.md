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
   **Präzisiert am 24.11.2026 (Eigentümer, auf Frage „nur Kalender-Link oder eigene Anmeldung“): „Eigene Anmeldung“ für den Stundenplan.** Das ändert die Regel „nie ein Passwort
   speichern“ für genau diesen Fall (unten). Das **Schul-Intranet** ist **noch nicht entschieden**: Der Eigentümer sieht es sich erst an, was es offiziell anbietet.

### Entscheidungen des Eigentümers vom 25.11.2026 (über den Orchestrator, Eintrag nach seiner Mitteilung)

9. **Einspielen von Meilenstein 1 und 2 auf die laufende Installation: Ja.** Der Orchestrator macht es mit dem Stand, der als lokaler Tag `m2-geprueft` markiert ist (Liste „Für das Einspielen“ in STATE.md).
10. **Die Hefte auf dem iPad gehören seiner Frau: Fall B1.** Sie schließt die Einrichtung ab und wird die erste Administratorin; der Eigentümer lädt sich danach selbst ein. Folge: Die alten Hefte und der
    gemeinsame App-Token gehören ihr. **„Person hinter dem alten Token ändern“ und die Übergabe von Heften (ARCHITECTURE 10.4) werden nicht gebaut.**
11. **Stundenplan:** Der Eigentümer will den derzeit besten Weg der Anbindung (der Orchestrator lässt recherchieren: offizielle Wege, bekannte Bibliotheken, Anmeldung ohne gespeichertes Passwort) und
    kümmert sich selbst darum, dass es erlaubt ist („nur privat für meine Frau, erstmal ein Test“). **Es bleibt dabei:** kein Aufruf gegen den echten Dienst, bevor er die Erlaubnis bestätigt hat **und**
    seine Frau sich selbst angemeldet hat; dann so wenige Aufrufe wie möglich; die Regeln unten gelten unverändert (Tests nur gegen Attrappen).

### Regeln für alles Schulische

1. **Die Person meldet sich selbst an,** in ihrer eigenen Einstellungsseite, mit ihren eigenen Händen. Niemand sonst tippt ihr Passwort, auch keine Sitzung und kein Test. Es gibt keinen gemeinsamen Schulzugang
   und nichts Schulisches in der Verwaltung; ein Administrator sieht nur „verbunden / nicht verbunden“, nie Zugangsdaten oder Inhalte.
2. **So wenig Geheimnis wie möglich.** Gibt es nach einer einmaligen Anmeldung ein dauerhaftes App-Geheimnis oder einen Token, mit dem der Server ohne Passwort weiterarbeiten kann, dann liegt das Passwort nur für diesen
   einen Austausch im Speicher und wird nie gespeichert. Nur wenn es nachweislich nicht anders geht, liegt es je Person verschlüsselt (`SecretBox` der Basis, Schlüssel außerhalb der Datenbank). In beiden Fällen: nie
   in der Umgebung, nie im Protokoll, nie in einer Antwort der API, nie im Klartext in einer Sicherung; „Trennen“ löscht es sofort und endgültig; das Löschen der Person löscht es mit.
3. **Nur das Eigene der Person:** ihr Stundenplan (Zeiten, Fächer, Räume, Klassenbezeichnungen). Keine Schülerlisten, keine Noten, keine Nachrichten mit Schülernamen; keine Schülerdaten verlassen das System der Schule.
   Abgerufen wird sparsam (feste Abstände, kein Dauerabruf) und nur über die dokumentierte Schnittstelle des Dienstes.
4. **Das Schul-Intranet** ist ein eigener Fall: Hat es keine dokumentierte Schnittstelle, wäre „eigene Anmeldung“ dort das Auslesen von Seiten hinter der Anmeldung. Das bleibt ausgeschlossen, solange die Schule es nicht
   erlaubt hat. **Offener Punkt, ohne Entwurf und ohne Code:** Der Eigentümer sieht es sich erst an und entscheidet dann. Geplant wird nur, was es offiziell anbietet (Feed, Export, Kalender-Abo); der vorhandene Import-Code
   für Neuigkeiten bleibt unangetastet und inaktiv.
5. Schulname und Domain stehen nie in einem Repository (nur in der nicht eingecheckten `LOCAL.md`). Tests laufen gegen eine Attrappe, nie gegen den echten Dienst und nie mit echten Zugangsdaten.

Folge für den Code von heute: Der WebUntis-Client (`src/JuNotes.Backend/School/UntisClient.cs`) mit einem Zugang für alle und dem Passwort im Klartext in der Umgebung (`School__Untis__*`) verschwindet **in dieser Form**.
Bis Meilenstein 4 bleibt er unangetastet und inaktiv; dort wird er auf „je Person, wie in Punkt 2“ umgebaut, und `School__Untis__*` entfällt.

## Meilensteine

Jeder endet in etwas, das er aufmachen kann. Reihenfolge und Zuschnitt gegenüber dem Vorschlag sind unten begründet.

### 0. Aufräumen und Vorbedingungen *(neu)*

- WebUntis-Client und Passwort-Einstellungen entfernen: **Entscheidung beim Eigentümer**, bis dahin bleibt der Code unangetastet und inaktiv. Der Rest von
  Meilenstein 0 hängt nicht daran.
- Probe: Passt der Code der Basis (.NET 10, öffentlich, 0.3.1) unter .NET 11 hier? **Entschieden: `notes` bleibt auf net11, wenn die Probe grün ist.** Ist sie es
  nicht, gehen die Fehler an den Orchestrator, bevor an der Kopie etwas geändert wird (Änderungen gehören in die Basis).
- Die Basis ins Repository holen (`base/` samt `MANIFEST`, danach `check-app.sh` und `sync-base.sh`), siehe ARCHITECTURE.md 4a.
- Den Abgleichvertrag mit der iPad-App als Test einfrieren (Golden-Tests auf `/api/v1`), damit jeder spätere Schritt beweisbar nichts bricht, **einschließlich der Antwort auf `push`** (Form von `accepted` und `seq`, Statuscodes), damit die neue Auslass-Regel aus Meilenstein 1 als bewusste, additive Änderung sichtbar wird.
- **Aufmachen kann er:** eine grüne Testliste, die den Vertrag der App beschreibt. Am laufenden System ändert sich nichts.

### 1. Anmeldung und Personen aus der Basis, neben dem App-Token *(gebaut, 24.11.2026; Stand und Abweichungen in STATE.md)*

- Konten, Sitzungen, Rollen, Einladungen aus der Basis; Anmeldung über den Hub oder eigenes Konto (Argon2id, TOTP).
- **Zusätzlich:** jede Zeile in `entities` bekommt einen Besitzer. Die Migration legt die Spalte **leer** an (leer heißt: gehört dem ersten Administrator), weil das
  Konto erst im Einrichtungsassistenten entsteht; beim Anlegen des ersten Kontos werden alle leeren Zeilen in einer Anweisung zugewiesen. Das gehört in
  diesen Meilenstein und nicht in den späteren, weil jede spätere Zeile sonst nachträglich umgeschrieben werden müsste.
- Der heutige `Sync__Token` der App gilt weiter und steht ab dem ersten Konto für diese Person. Die App merkt nichts. `/api/v1` bleibt eine eigene Tür mit
  Bearer-Prüfung (ARCHITECTURE.md 5a).
- **Schreibregeln mit Tests mit zwei Personen:** eine Zeile nur ihrer Besitzerin; das Ziel von `parent_id` gehört derselben Person; Dateien genauso.
- Die heutigen Passwörter `ADMIN_PASSWORD` und `WEB_PASSWORD` fallen weg, sobald das eigene Konto da ist.
- **Aufmachen kann er:** im Browser mit eigenem Konto oder über den Hub anmelden; die iPad-App synchronisiert unverändert weiter.

### 2. Die Web-App als Seiten der Basis, mit denselben Funktionen wie heute *(gebaut, 25.11.2026; Stand in STATE.md)*

- Die Web-App ist JavaScript im Baukasten und spricht JSON. Meilenstein 2 baut daher **die lesenden Wege von `/api/web/v1`**: Bibliothek, Heft, Seite, Datei,
  dazu Noten und Karten (nur Listen, ohne Rechnung) und die Suche über Namen von Ordnern und Heften (die Oberfläche sagt in einem Satz, dass der Text der Seiten noch nicht durchsucht wird; das geht erst, wenn der erkannte Text mit synchronisiert wird). **Der Stundenplan gehört nicht zu Meilenstein 2** (Entscheidung des Orchestrators, 25.11.2026): keine Sonderregel „nur der erste Administrator“, er kommt mit Meilenstein 4, wenn die Schuldaten je Person sind.
- Heutige Admin-Seiten (Übersicht, Codex, Profile, Sicherungen) ziehen in den Baukasten um, noch ohne neue Funktionen.
- **Die Razor-Seiten fallen am Ende von Meilenstein 2 weg.**
- **Aufmachen kann er:** dieselbe Web-Ansicht wie heute, nur im neuen Aussehen und hinter der neuen Anmeldung.

### 3. Mehrere Personen und Teilen

- Zweite und dritte Person mit eigenen Heften; jede sieht nur das Eigene. Einzelne Hefte freigeben (lesen; später bearbeiten).
- Die iPad-App meldet sich mit einem **Gerätetoken je Person** an (aus der Basis) statt mit dem gemeinsamen Token. Das ist die einzige App-Änderung in
  diesem Plan, die der Server erzwingt; der alte Token bleibt bis dahin nutzbar.
- **Offener Entwurfspunkt:** Wie kommt ein später freigegebenes Heft in den Abgleich der Empfängerin (ihr Merker liegt schon darüber), und wie erfährt sie vom
  Entziehen? Vorschlag in zwei Stufen: **in diesem Meilenstein Freigaben nur im Web** (kein Merker nötig), die App der Empfängerin später durch Neustempeln des
  Teilbaums. Einzelheiten in ARCHITECTURE.md 3a.
- **Freigaben im Abgleich (Stufe 2) sind nicht Teil dieses Meilensteins.** **Vorbedingung dafür ist eine kleine App-Änderung** mit eigenem Entwurf: beim Löschen
  eines Hefts die Kinder mitnehmen, einen Lese-Marker an der Wurzelzeile einer Freigabe auswerten, die lokale Suche beim Löschen aufräumen (zu prüfen), und
  die App lernt, dass der Server eine Zeile auslassen kann (`accepted` auswerten oder verworfene Zeilen nicht still verlieren). Siehe ARCHITECTURE.md 3a.
- **Aufmachen kann er:** ein zweites Konto anlegen, ein Heft teilen, und es erscheint im Web dort, nicht bei der dritten Person.

### 4. Schule pro Person *(vorgezogen, damit Meilenstein 5 und 6 mit echten Daten laufen)*

- **Stundenplan, je Person, ohne Passwort (Entscheidung vom 25.11.2026, Entwurf in ARCHITECTURE.md 8):** Jede Person verbindet in ihrer Einstellungsseite ihren Stundenplan auf einem von zwei Wegen, in dieser
  Reihenfolge angeboten: **(a) der iCal-Link** ihres Stundenplans, **(b) der Text des QR-Codes aus ihrem Profil** („Zugriff über Untis Mobile“; ein App-Geheimnis, nicht das Passwort). **Es gibt kein Passwortfeld.**
  Beides liegt je Person verschlüsselt, „Trennen“ löscht sofort. Höchstens ein Abruf am Tag und einer auf Knopfdruck; gespeichert werden nur die eigenen Stunden (Zeit, Fach, Raum, Klasse, Zustand). Der heutige
  `UntisClient` mit Passwort wird durch diese zwei Wege ersetzt. Der Administrator sieht nur „verbunden / nicht verbunden“. **Unbestätigt:** Felder des Feeds, ob die Schule das Modul für Schüler hat, ob (b) bei
  Single Sign-on oder zweitem Faktor geht, größter Zeitraum eines Abrufs; ausgefallene Stunden fehlen im Feed (a), (b) liefert sie. Weg (b) ist nicht offiziell beschrieben und eine Grauzone gegenüber den
  Geschäftsbedingungen des Herstellers (Vertragspartner ist die Schule). Kein Aufruf gegen den echten Dienst, bevor der Eigentümer die Erlaubnis bestätigt hat.
- **Was die Schule sonst offiziell anbietet** (Kalender-Abo, Feed, Export) bleibt als Weg für Neuigkeiten und als Rückfall.
- **Schul-Intranet: offener Punkt.** Kein Entwurf für eine Anmeldung dort, kein Code, bis der Eigentümer entschieden hat.
- **Der Stundenplan erscheint im Web erst hier** (und mit der Seite dafür), nicht schon in Meilenstein 2.
- **Aufmachen kann er:** sein eigener Stundenplan in der App und im Web, aus seinem eigenen Schulkonto; „Trennen“ löscht die Verbindung auf der Stelle.

### 5. Web-Verwaltung

- Personen und Rollen (Basis), KI-Profile und Grenzen, Sicherungen, Geräte mit Abgleichstand. Keine Schule.
- **Aufmachen kann er:** eine Verwaltungsseite im Browser, mit der er alles Genannte einstellt.

### 6. KI über den Hub

- Anbindung an den KI-Zugang des Hubs. Der Schlüssel wird in der Verwaltung eingetragen (verschlüsselt in der Datenbank, nie zurückgegeben); der Modus ist Hub,
  wenn Verbindung, Schlüssel und Schalter stimmen, sonst eigene Brücke; kein stiller Rückfall. Dazu Grenzen pro Person und Tag, Abbruch, sichtbare Nutzung.
- **Aufmachen kann er:** ein Quiz oder Cover erzeugen und die Nutzung dazu sehen; mit Hub ohne jede Codex-Anmeldung im Server.
- **Entschieden:** Der Hub zählt pro App, nicht pro Person. `notes` zählt selbst pro Person; das Budget der App im Hub ist die äußere Schranke. Es wird nicht
  damit geplant, dass der Hub die Person mitzählt.

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
