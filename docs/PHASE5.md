# Phase 5: Aufbau wie in GoodNotes, Noten nach Schulsystem

Stand: 20.11.2026. Entstanden aus einem Gespräch nach dem ersten Blick auf die App. Das hier sind die **Entscheidungen**, noch kein Code.
Alles Schulspezifische (Schulname, Adressen) steht nicht hier, sondern in `LOCAL.md`.

## Ausgangslage

Sie hat in GoodNotes schon alles geordnet: Ordner für Praxis („Kindi“), Schule und Sonstiges, in der Schule ein Ordner je Fach, dazu
PDFs aus dem Unterricht, die sie direkt importiert hat. JuNotes soll **genau so** aufgebaut sein können. Die Ausbildung ist die
praxisintegrierte Erzieherausbildung (Fachschule für Sozialpädagogik, BW).

## Entscheidungen

### 1. Aufbau: frei verschachtelbar

- **Ordner und Hefte beliebig ineinander**, wie in GoodNotes. Es gibt keine feste Ebene „Fach“ mehr.
- Ein **Fach ist ein Ordner mit Fach-Eigenschaften** (Symbol, Farbe, Notenskala, Gewichtung, Hauptfach/Nebenfach). Ordner ohne diese
  Eigenschaften sind einfach Ordner (Kindi, Sonstiges, Halbjahr, Thema).
- **Start leer.** Keine Vorbelegung mit Fächern; sie legt alles selbst an.
- **Cover frei gestaltbar** für Ordner und Hefte.

### 2. Import aus GoodNotes

- GoodNotes kann einen Ordner **als ZIP mit PDFs exportieren, die Ordnerstruktur bleibt erhalten.** Das ist der Weg.
- JuNotes importiert so ein ZIP: Ordner werden Ordner, PDFs werden Hefte, Seiten bleiben beschreibbar.
- Das **eigene Format** `.goodnotes` ist nicht dokumentiert und wird nicht gelesen. Die Handschrift ist im PDF-Export fest eingebrannt, also
  nicht mehr einzeln bearbeitbar, aber als Bild lesbar und durchsuchbar (Texterkennung).

### 3. Noten

- **Skala pro Fach wählbar:** 1,0 bis 6,0 in Zehnteln (feinere Stufen einstellbar), ganze Noten mit Tendenz oder Punkte 0 bis 15.
  Das ersetzt die bisherige Eingabe, die nur 1, 1,3, 1,7 kannte.
- **Gewichtung pro Fach:** mündlich zu schriftlich (zum Beispiel 30 zu 70), und **pro Note überschreibbar** (eine Klassenarbeit dreifach).
  Neue Fächer starten bei 50 zu 50; das ist noch zu bestätigen.
- **Notenarten:** Klassenarbeit/Klausur, mündlich/Mitarbeit, Test/Kurztest/Vokabeltest, Referat/Präsentation/Praxisnote.
- **Hauptfach/Nebenfach (maßgebend)** pro Fach angebbar; die Gewichtung lässt sich überschreiben. Angezeigt werden Gesamtschnitt sowie
  die Schnitte der Hauptfächer und der übrigen.
- **Schuljahr und Halbjahr:** getrennt, **wenn man will** (abschaltbar).

### 4. Cover und Symbole

- Vorlagen mit Farbe, Symbol und Titel; eigenes Foto oder Bild.
- **KI-Cover über die Codex-Anbindung.** Codex kann Bilder erzeugen (eingebautes `image_gen`, im ChatGPT-Konto, ohne eigenen API-Schlüssel;
  laut Quellen schreibt es die Datei in `CODEX_HOME`). Das ist **nicht geprüft** und muss beim ersten echten Lauf ausprobiert werden.
- **Fallback:** Cover als Vektorgrafik (SVG), falls die Bilderzeugung im Aufruf nicht klappt.

### 5. Erkannter Text (OCR)

- **Beides:** ein Knopf „Text anzeigen“ zeigt den erkannten Text der Seite zum Kopieren und Teilen, und die Schrift auf der Seite lässt
  sich antippen und markieren (wie Live-Text).
- Der Text liegt heute nur auf dem iPad. Wenn die Ansicht auch im Web gebraucht wird, muss er mit synchronisiert werden.

### 6. Schreibfunktionen (aus GoodNotes)

Handschrift in Text umwandeln, Formen glätten, Laserpointer, Vergrößerungsfenster.

### 7. Ordnung

Seiten-Raster (verschieben, kopieren, in andere Hefte ziehen), Lesezeichen und Gliederung, Sperre pro Heft (Face ID oder Code),
Favoriten und Tags.

### 8. Einfügen

Bilder und Fotos, Textfelder, Sticker und Washi-Tape, Audioaufnahme zur Schrift.

### 9. Vorlagen (Papier)

Linien und Karos in mehreren Abständen (5, 7, 10 mm), Punkte, Planer und Kalender, Checklisten, **eigene PDF als Vorlage**.

### 10. Praxis („Kindi“)

Berichte und Praxisaufgaben, Beobachtungen zu einzelnen Kindern, Fotos und Projekte, Vorlagen und Bögen.

**Datenschutz-Regeln dafür** (Daten von Kindern):
- Sperre pro Heft.
- **Keine KI und kein Upload** (auch kein Paperless, kein Drive) ohne ausdrückliches Ja pro Heft.
- Backups verschlüsselt (gibt es schon), Bilder nicht ungefragt in Suche und KI.
- Möglichkeit, Namen zu kürzen.

### 10b. Schuljahre

- Ein Ordner lässt sich als **Schuljahr** markieren (Beginn im August, „2026/27“). Fächer darin gehören zu diesem Jahr; ihre Noten sind damit getrennt.
- Ein **abgeschlossenes** Schuljahr zählt nicht mehr für Stundenplan („Mathematik“ passt nur auf das aktuelle Mathe), „Heute“, Karteikarten und
  Erinnerungen. Noten und Hefte bleiben. In der Seitenleiste erscheint es ausgegraut.
- Gibt es einen Ordner für das aktuelle Schuljahr, zählen nur dessen Fächer (und Fächer, die in keinem Schuljahr liegen).
- Die Noten wählen das Schuljahr (standardmäßig das aktuelle) und darin „Alles / 1. Halbjahr / 2. Halbjahr“.
- „Nächstes Schuljahr anlegen“ am Schuljahr-Ordner kopiert Ordner und Fächer samt Notenskala, Gewichtung und Hauptfach leer in einen neuen Ordner.

### 11. Benachrichtigungen aus der Schule

Gewünscht: Meldungen der Schule und rund ums Lernen als Benachrichtigung auf dem iPad.
- **Ohne Developer-Konto gibt es keine Push-Nachrichten** (APNs). Möglich sind **lokale Benachrichtigungen**: Die App holt Stundenplan und
  Schul-News im Hintergrund und meldet dann selbst, wenn sich etwas ändert.
- Geplant: Unterrichtsausfall und Raumänderung, neue Schul-News, „Morgen ist Klassenarbeit in …“, Lernplan-Aufgaben des Tages und fällige
  Karteikarten, jeweils einzeln abschaltbar.
- Grenze: iOS entscheidet, wann die Hintergrundabfrage läuft (alle paar Stunden), es ist also nicht sekundengenau. Mit dem
  Developer-Konto ginge echter Push vom Server.
- Die Schul-News brauchen einen Feed-Link (`School__NewsFeedUrl`). Ob die Schule einen hat, ist offen; die interne Seite braucht eine Anmeldung.

## Noch offen

- Standardgewichtung neuer Fächer (50 zu 50 oder 30 zu 70).
- Genaue Fachliste der Ausbildung: Die Schule nennt für das einjährige Berufskolleg Religion, Deutsch, Englisch und Handlungsfelder
  (zum Beispiel „Bildung und Entwicklung fördern“). Für die praxisintegrierte Form steht keine Liste im Infoblatt. Sie legt sie selbst an.
- Wie Noten in dieser Ausbildung wirklich gebildet werden (Rundung, maßgebende Fächer): in den öffentlichen Infoblättern nicht zu finden.
  Deshalb ist alles einstellbar statt fest verdrahtet.

## Vorgeschlagene Reihenfolge

1. **Ordnerbaum und Cover:** neues Datenmodell (verschachtelte Ordner, Fach-Eigenschaften), Server-Migration, Oberfläche, Cover-Editor.
2. **Noten-Umbau:** Skala pro Fach, Gewichtung, Notenarten, Hauptfach, Halbjahre.
3. **OCR-Ansicht und Textebene.**
4. **GoodNotes-ZIP-Import.**
5. **Schreibfunktionen** (Text, Formen, Laserpointer, Lupe).
6. **Seiten-Raster, Lesezeichen, Sperre, Favoriten.**
7. **Einfügen und Audio.**
8. **Praxis-Datenschutz.**

### 12. Stundenplan direkt aus WebUntis, KI-Einstellungen

- **WebUntis ohne iCal-Link:** Der Server meldet sich mit ihrem WebUntis-Zugang an (Server, Schulname, Benutzer, Passwort in `.env`), liest den Stundenplan
  und die Ferien und meldet sich wieder ab. Gestrichene Stunden (`cancelled`), Änderungen (`irregular`) und Klassenarbeiten (`ex`) kommen mit. Mit
  einem iCal-Link zusammen hat WebUntis Vorrang. **Gegen das echte WebUntis der Schule ist das noch nicht ausprobiert**: Manche Schulen erlauben
  Schülern diese Schnittstelle nicht (Fehler −8509). Die Meldung im Admin-Bereich sagt dann genau das.
- **Modell, Aufwand und Tageslimit** stellt der Admin unter „KI-Verbindung“ ein, pro Aufgabe auch unter „KI-Profile“. Leer heißt: Standard des Kontos.
  Das Kontingent des ChatGPT-Tarifs selbst kann der Server nicht abfragen, nur die eigene Zählung.
