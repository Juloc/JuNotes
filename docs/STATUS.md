# Stand der Entwicklung

Stand: 20.11.2026. Der [Plan](PLAN.md) beschreibt, was JuNotes werden soll. Diese Datei sagt, was davon gebaut ist, **wie es geprüft wurde**
und was nicht.

## Wie geprüft wurde

| Teil | Prüfung |
|---|---|
| Server (.NET 11, PostgreSQL) | automatische Tests gegen eine echte Datenbank, dazu der echte Host mit echten HTTP-Aufrufen (`WebApplicationFactory`) |
| `JuNotesCore` (Swift 6.4) | automatische Tests unter Linux, die SQLite-Teile zusätzlich auf macOS in der CI |
| iPad-App (SwiftUI, PencilKit, VisionKit) | **nur übersetzt** (Xcode 27 in GitHub Actions). Sie wurde **nie auf einem iPad gestartet.** |
| Fremddienste (Codex, Google Drive, Paperless) | gegen Attrappen getestet, **nicht gegen die echten Dienste** |

Das ist der wichtigste Satz dieser Datei: Die Logik ist gründlich getestet, aber ob die App sich auf dem iPad gut anfühlt, wissen wir erst, wenn
sie darauf läuft.

## Gebaut

**Phase 0, Fundament:** Repo, Server nach dem Jularr-Muster, Swift-Paket `JuNotesCore`, XcodeGen-Projekt, CI für Server, Core und eine
unsignierte `.ipa`, Docker-Image und `compose.yaml`.

**Phase 1, Schreiben und Ordnen:**
- Fächer, Hefte (Seiten oder Whiteboard), Seiten mit Papier (liniert, kariert, gepunktet, Cornell, leer), Auto-Seiten beim Weiterschreiben, Papierkorb mit 30 Tagen
- PencilKit-Fläche mit dem System-Werkzeugkasten (damit Squeeze, Doppeltipp und Hover des Pencil Pro), jeder Strich wird sofort lokal gespeichert
- lokale SQLite-Datenbank, Sync mit dem Server auf Strichebene (Striche werden nur hinzugefügt oder gelöscht), Statuszeile mit gespeichert / synchronisiert / Backup
- Scannen mit der Dokumentenkamera, „Wohin mit dem Scan?“, Scans als Dateien (auf dem iPad und auf dem Server)
- Texterkennung auf dem Gerät (Handschrift und Druck), eine Suche über alles mit hervorgehobenem Treffer
- Server-Backups mit `pg_dump` (nachts, vor jeder Migration, von Hand), Rotation, Scans im Archiv

**Phase 2, Schule:**
- Stundenplan als iCal-Link, Schul-News als RSS/Atom, beides vom Server geholt und auf dem iPad zwischengespeichert
- „Heute“ mit laufender und nächster Stunde, Ausfällen, Arbeiten; „Neue Seite für jetzt“; Fach-Zuordnung („Mathematik“ ist „Mathe“)
- Noten mit gewichtetem Schnitt und „Was brauche ich für Note X?“

**Phase 3, KI und Lernen:**
- Codex-Aufruf auf dem Server mit strengen JSON-Schemas, Tageslimit, Profile (Anweisung, Schwierigkeit, Ton) im Admin-Bereich
- Abfragen (mit Selbsteinschätzung oder Prüfung durch die KI), Karteikarten (SM-2), Ergänzen und Prüfen (übernommene Vorschläge stehen in einem Kasten auf der Seite), Probetest als Heft mit Lösungen zum Aufdecken, Abschreib-Modus mit Vergleich
- Lernplan pro Arbeit (lernen, üben, wiederholen, Probetest, letzter Blick), nie mehr als die gewählte Zeit pro Tag

**Phase 4, Feinschliff:**
- Web-Leseansicht (Hefte, Seiten als SVG, Scans), installierbar; Admin-Bereich im Web
- Backups zusätzlich in einen NAS-Ordner und zu Google Drive, verschlüsselt vor dem Verlassen des Servers
- PDF-Import (so zieht ein Heft aus GoodNotes um), PDF-Export mit Teilen, Senden an Paperless
- Kurzbefehle („Neue Seite für jetzt“, „Blatt scannen“), Notizbücher in Spotlight

**Phase 5, Aufbau wie in GoodNotes** (Entscheidungen in [PHASE5.md](PHASE5.md)):
- Ordner beliebig ineinander, ein Ordner kann zusätzlich Fach sein; Cover frei gestaltbar (Verlauf, Farbe, Foto, Symbol, Emoji, Muster); Verschieben und Ziehen
- Noten pro Fach: Notensystem (1 bis 6 in Zehnteln oder Hundertsteln, mit Tendenz, Punkte), Anteil mündlich zu schriftlich, Gewicht pro Note, Hauptfächer mit eigenem Schnitt, Halbjahre
- Import eines GoodNotes-Exports (ZIP mit PDFs), Ordnerstruktur bleibt; Ansicht des erkannten Textes
- Seiten-Raster (umsortieren, duplizieren, Lesezeichen, in anderes Heft), Favoriten, Stichwörter, Sperre pro Heft mit Face ID, Suche nach Namen und Stichwort
- Papier in weiteren Linienabständen, Checkliste, Wochenplan
- Objekte auf Seiten: Fotos, Textfelder, Sticker, Washi-Tape, Sprachaufnahmen; Laserpointer; erkannte Schrift als Textfeld
- „Enthält Daten von Kindern“: nichts davon geht ohne Ja an die KI, an Paperless oder in die Systemsuche
- Lokale Benachrichtigungen: Unterricht entfällt, neue Schul-News, Arbeit morgen, Lernplan, fällige Karten
- Schuljahre: Schuljahr-Ordner, abgeschlossene Jahre zählen nicht mehr, Noten nach Schuljahr und Halbjahr, nächstes Schuljahr aus dem letzten
- Noten mit Rad in Schritten von 0,1, 0,05 oder 0,01 und einem Feld für eine genaue Zahl
- Web-Ansicht zeigt Fotos, Textfelder, Sticker, Tape und Aufnahmen als Chip, Cover der Hefte und die neuen Papiere
- Cover von der KI entwerfen: Farben, Emoji oder Symbol und Muster nach einer Beschreibung (mit Codex, wie die anderen Lernhilfen)

## Nicht geprüft

- **Die App auf einem echten iPad.** Offen sind: wie sich das Schreiben anfühlt, ob PencilKit-Striche sauber wiedererkannt und synchronisiert
  werden (ein PencilKit-Strich hat keine feste Nummer, die App erkennt ihn an Zeit, Länge und Start- und Endpunkt), wie gut Apple Vision
  deutsche Handschrift liest, Tempo und Speicher bei vielen Strichen, Bedienung mit VoiceOver und Dynamic Type.
- **Die Budgets** aus dem Plan (Heft in 0,2 s, 60 Bilder pro Sekunde, Suche in 0,1 s) sind nirgends gemessen.
- **Codex mit einem echten Konto.** Der Aufruf ist so gebaut wie in Jularr und FullWorth, getestet mit einem Fake-Programm. Ob das echte Modell die
  Schemas einhält, zeigt erst der erste echte Lauf. Die Antworten werden zur Sicherheit noch einmal geprüft.
- **Google Drive und Paperless** gegen die echten Dienste.
- Die **Anmeldung über die Geräteanmeldung** von Codex im Admin-Bereich mit dem echten Programm.

- **Alles aus Phase 5 auf einem echten iPad.** Es wurde in der CI übersetzt und teils im Simulator gesehen (Ordner, Cover, Heute). Nie ausprobiert sind:
  Fotoauswahl, Aufnahme, Face ID, Ziehen und Ablegen, Gesten auf Objekten, Laserpointer, der Import eines echten GoodNotes-ZIPs, Benachrichtigungen
  und die Hintergrundabfrage.
- **Codex-Bilder als Cover** (echte erzeugte Bilder wie in der Codex-Desktop-App) sind nicht gebaut, weil die Bilderzeugung über Codex ungeprüft ist.
  Gebaut ist stattdessen, dass die KI ein Cover aus Farben, Emoji oder Symbol und Muster entwirft. Auch das lief nur gegen ein Fake-Programm, nicht mit echtem Codex.

## Nicht gebaut

| Was | Warum |
|---|---|
| WebUntis über die inoffizielle JSON-RPC-Schnittstelle | Der iCal-Link ist robuster und braucht kein Passwort. Ohne echten Zugang wäre der Zugriff auf die Schnittstelle geraten. |
| Anbindung von `intern.mps-lb.de` | unbekannt, ob es einen Feed gibt oder eine Anmeldung nötig ist. Ein Feed geht über `School__NewsFeedUrl`. |
| Noten aus einer digitalen Quelle | die Noten werden von Hand eingetragen |
| iCloud-Backup, Widgets | brauchen den kostenpflichtigen Apple-Developer-Account (iCloud-Rechte, App-Gruppen) |
| Suche nach Inhalt in der Web-Ansicht | Der erkannte Text liegt nur auf dem iPad. Dort funktioniert die Suche. |
| Suche nach Bedeutung mit KI | nicht gebaut |
| Schreiben im Web | die Web-Ansicht ist nur zum Lesen |
| Prototyp PaperKit gegen PencilKit | Es wird PencilKit benutzt. PaperKit wurde nicht ausprobiert. |
| Whiteboard mit Minikarte und Zoom | Das Whiteboard ist eine große Fläche zum Scrollen. |
| Vergrößerungsfenster, Handschrift in Text per Auswahl | Die Auswahl des Lassos ist für Apps nicht zugänglich. Stattdessen gibt es „erkannte Schrift als Textfeld“. Formen glätten kann PencilKit selbst (Linie halten), das ist nicht geprüft. |
| Sprachaufnahmen abspielen und Cover-Symbole in der Web-Ansicht | Aufnahmen erscheinen dort nur als Beschriftung, Symbole des iPads als Stern. |
| Echte Push-Nachrichten | brauchen das kostenpflichtige Developer-Konto; es gibt nur lokale Benachrichtigungen |
| Barrel Roll als eigene Funktion, eigene Hover-Vorschau | Es gilt, was der System-Werkzeugkasten kann. |
| Mehrere Benutzer | Es gibt einen Schlüssel für den Server und ein Passwort pro Bereich. |
| Lizenz | noch festzulegen. Ohne Lizenz gilt bei einem öffentlichen Repo „alle Rechte vorbehalten“. |

## Was vor dem ersten Einsatz zu tun ist

1. **Server einrichten:** `compose.yaml`, `.env` mit `POSTGRES_PASSWORD` und `SYNC_TOKEN`, dazu `Admin__Password`, `Web__Password`, optional
   `School__TimetableIcsUrl`, `Backup__CopyTo` und `Backup__Passphrase`. Hinter einen Reverse Proxy mit HTTPS legen.
2. **Codex im Image haben** und im Admin-Bereich unter „KI-Verbindung“ anmelden.
3. **App aufs iPad:** die erste Installation mit iloader oder Impactor und einer Apple-ID per USB (läuft 7 Tage). Danach SideStore einrichten und
   diese Quelle eintragen: `https://github.com/Juloc/JuNotes/releases/download/latest/source.json`. Jede grüne CI baut dort eine neue Version
   (0.1.<Laufnummer>), die SideStore als Update anbietet und alle 7 Tage neu signiert. Das ist **nicht ausprobiert**.
   In der App unter Einstellungen Adresse und Schlüssel eintragen.
4. **Eine Woche benutzen**, aufschreiben, was nicht stimmt, und erst dann die nächsten Schritte planen.
