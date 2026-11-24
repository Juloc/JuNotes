# JuNotes

Notizen und Lernen für das iPad, mit Web-App und eigenem Server. Handschrift mit dem Apple Pencil Pro, Hefte, Scans,
Suche und Lernhilfen, gebaut so, dass man immer sieht, was gerade passiert.

Der Plan steht in [docs/PLAN.md](docs/PLAN.md), was davon gebaut und wie es geprüft ist in [docs/STATUS.md](docs/STATUS.md). Alle Phasen sind umgesetzt; die iPad-App wurde bisher nur übersetzt, noch nicht auf einem iPad benutzt.

## Aufbau

| Ordner | Inhalt |
|---|---|
| `src/` | Server: .NET 11, ein Host (`JuNotes.Frontend`) mit `Backend`, `Workers` und `Integrations` als Bibliotheken, PostgreSQL |
| `database/` | eingefrorene Baseline `Schema.sql` und nummerierte Migrationen in `migrations/` |
| `clients/ios/JuNotesCore` | Swift-Paket mit der Logik der App, läuft und testet auch unter Linux |
| `clients/ios/App` | dünne SwiftUI-Hülle für das iPad (XcodeGen, `project.yml`) |
| `tests/` | Server-Tests |

## Bauen und testen

Server (.NET 11 SDK, Postgres 18 für die Integrationstests):

```bash
dotnet build JuNotes.slnx
docker run -d --name junotes-test-pg -p 127.0.0.1:15434:5432 -e POSTGRES_USER=junotes_test \
  -e POSTGRES_PASSWORD=isolated-test-password -e POSTGRES_DB=junotes_test_admin postgres:18
export JUNOTES_TEST_DB='Host=127.0.0.1;Port=15434;Username=junotes_test;Password=isolated-test-password;Database=junotes_test_admin'
dotnet test tests/JuNotes.Backend.Tests
```

Ohne `JUNOTES_TEST_DB` werden die Datenbanktests übersprungen. Tests laufen nie gegen eine echte Datenbank.
Die Backup-Tests brauchen `pg_dump` und `pg_restore` in Version 18 (mindestens so neu wie der Server). Liegen ältere auf dem PATH,
zeigen `JUNOTES_TEST_PGDUMP` und `JUNOTES_TEST_PGRESTORE` auf die Programme der Version 18.

Betrieb mit Docker (Server und Postgres): `.env.example` nach `.env` kopieren, `POSTGRES_PASSWORD` und `SYNC_TOKEN` setzen, dann
`docker compose up -d`. Der Server hört auf `127.0.0.1:8098`; Backups liegen im Volume unter `/data/backups` (nachts um 3 Uhr,
die neuesten 7 bleiben, dazu eines vor jeder Migration).

Schule (alles optional): `School__TimetableIcsUrl` ist der iCal-Link des Stundenplans (in WebUntis unter „Stundenplan abonnieren“,
`webcal://` geht auch), `School__NewsFeedUrl` ein RSS- oder Atom-Feed der Schul-Website. Der Server holt beides alle 30 Minuten
und stellt es unter `/api/v1/school/calendar` und `/api/v1/school/news` bereit, ebenfalls mit dem Token.

KI (Phase 3): Das Codex-CLI muss im Image oder auf dem Server stehen (`Codex__Executable`, Standard `codex`); die Anmeldung liegt unter
`Codex__Home` (im Docker-Image `/data/codex`) und wird im Admin-Bereich per Geräteanmeldung gemacht. `Ai__DailyLimit` (Standard 60)
begrenzt die Anfragen pro Tag. Die Profile (Prompts, Schwierigkeit, Ton) stellt der Admin ein; die App sieht nur ihre Namen.

Weitere Orte für Backups (Phase 4, alles optional): `Backup__CopyTo` kopiert jedes Backup in einen zweiten Ordner, zum Beispiel eine
NAS-Freigabe, die in den Container gemountet ist. `Backup__Drive__ClientId`, `__ClientSecret`, `__RefreshToken` und `__FolderId` schicken es
zu Google Drive (den Refresh-Token holt man einmal mit dem Google OAuth Playground, Bereich `https://www.googleapis.com/auth/drive.file`).
Mit `Backup__Passphrase` wird jede Kopie verschlüsselt (AES-256-GCM), bevor sie den Server verlässt. Wieder öffnen:

```bash
Backup__Passphrase='…' dotnet JuNotes.Frontend.dll decrypt-backup junotes-….dump.enc junotes-….dump
```

Paperless (optional): `Paperless__BaseUrl` und `Paperless__Token` (ein API-Token eines Paperless-Benutzers, der Dokumente und Tags anlegen darf).
Die App schickt dann fertige Hefte als PDF dorthin, mit dem Fach als Tag. Das ist ein Archiv, kein Backup.

Anmeldung im Browser: Beim ersten Start steht ein **Einrichtungscode** im Log des Servers (`docker compose logs junotes`). Wer die Adresse des Servers öffnet und ihn eingibt,
legt das erste Konto an (Name, Passwort, zweiter Faktor mit einer Authenticator-App); es verwaltet die Installation. Weitere Personen lädt der Administrator in den
Einstellungen ein. Die Konten, die Sitzungen und der Schlüssel des Servers (`secret.key`, im Ordner `BASE_DATA`, im Image `/data/base`) liegen im Volume `/data` bzw. in der
Datenbank. Die Passwörter `Admin__Password` und `Web__Password` gibt es nicht mehr. Die iPad-App meldet sich weiter mit `Sync__Token` an, bis sie ein Gerätetoken je Person bekommt.

Server ohne Docker starten (legt die Datenbank bei Bedarf selbst an):

```bash
export ConnectionStrings__JuNotes='Host=…;Database=junotes;Username=…;Password=…'
dotnet run --project src/JuNotes.Frontend
```

Die Sync-API (`/api/v1/sync/push` und `/pull`) verlangt ein Token, das du setzt: `Sync__Token=<langer zufälliger Wert>`.
Ohne Token antwortet sie mit 503, mit falschem Token mit 401.

iPad-Logik (Swift 6.4):

```bash
cd clients/ios/JuNotesCore && swift test
```

iPad-App: Der Build braucht macOS und läuft in GitHub Actions (`.github/workflows/ios.yml`). Das Ergebnis ist eine
unsignierte `JuNotes.ipa`, die man mit iloader oder Impactor und einer Apple-ID aufs iPad bringt.

## Regeln

- Schema-Änderungen sind neue Dateien `database/migrations/NNNN_name.sql`, die Baseline bleibt eingefroren.
- Keine persönlichen Daten und keine Secrets im Repo.
