# Notes (JuNotes): working rules

Built on the shared base of the app family. base version: **0.3.4** (the copy in `base/`). This repository is public.

1. **Scope first.** Read [docs/SCOPE.md](docs/SCOPE.md) (what this project becomes and what stays out), then [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) (the draft), and keep [docs/STATE.md](docs/STATE.md) current: it is the hand-over page (what is done, the next step, how to start, test and publish, what is not verified). Build only the milestone the orchestrator has started.
2. **The rulebook is [base/RULES.md](base/RULES.md).** Every rule there applies here; cite the numbers (`AUTH-03`) in comments and reviews. A deviation is written down with its reason in `docs/STATE.md` ("Known deviations"); today: `OPS-01`/`OPS-02` (the Compose file with variables, until the installation moves).
3. **`base/` is a copy and is never edited here** (CODE-07). A change goes into the base repository and comes back with its `scripts/sync-base.sh`; `scripts/check-app.sh` shows drift. After a sync the version above changes with it.
4. **`/api/v1` is the frozen contract with the iPad app.** Only additive fields; the contract tests (`tests/JuNotes.Backend.Tests/ApiContractTests.cs`, `DoorsTests.cs`, `OwnerTests.cs`) stay green, and a deliberate change to an answer changes its test in the same commit. The iPad app (`clients/ios`) is not changed by server work. The door is the instance token only; a session never opens it and the token never opens anything else.
5. **Nothing private, nothing school-specific by name in the repository** (it is public): no school name, no address or domain of a school or of the owner's server, no names of people, no credentials. Those stand only in `docs/LOCAL.md`, which is not checked in. **Never type a real password**; test accounts are throwaway values on localhost, and nobody but the person types a school password (see the rules for school data in `docs/SCOPE.md`).
6. **Whose is a row.** Every row and file has an owner; write rules, the pull filter and the readers keep one person away from another's notes (`docs/STATE.md`, "For the security review"). A new way to read or write notes names its owner and gets a test with two people.
7. **Test sparingly and verify by running.** Run only the tests that cover the change (`dotnet test --filter`), one build at a time, `nice -n 10`; the SDK 11 lives in `~/.dotnet`. Look at a change in the running app (wide and 390 px, light and dark) and say what was not checked.
8. **Push, release and installing on the server are the orchestrator's,** with the owner's yes. Commit locally; do not push.
9. **No new packages, no build step in the browser, no new secrets in the environment** (CODE-02, CODE-06). Versions stand in `Directory.Packages.props`.
10. **Docs change with behaviour.** Commits are small with a clear subject.

## Map

| Where | What |
|---|---|
| `src/JuNotes.Frontend/Program.cs` | Start: `AddBase`, migrations (base first, then ours), `UseBase`, `MapBase`, the old addresses (redirects), `MapV1`, `MapWebApi`, `MapAdminApi`. |
| `src/JuNotes.Frontend/Api/V1Door.cs` | The contract with the iPad app (`/api/v1`): the check and the endpoints. |
| `src/JuNotes.Frontend/Api/` | `V1Door.cs` (the contract with the iPad app, `/api/v1`), `WebApi.cs` (reading paths of the web app), `AdminApi.cs` (administration, role admin). |
| `src/JuNotes.Frontend/Admin/` | The owner directory (who the app token stands for, the claim), the token brake, the security log, the Codex sign-in session. |
| `src/JuNotes.Frontend/wwwroot/` | The web app in the toolkit of the base: `app.js`, `pages/` (library, study, administration), `i18n/`, `app.css` (one colour variable), the service worker and PWA files. |
| `src/JuNotes.Backend/` | Sync, assets, library reader, backups, school import, AI helpers, migrations runner. |
| `database/` | `Schema.sql` (baseline) and `migrations/NNNN_name.sql` (numbered, never edited once released). |
| `base/` | The copy of the base. |
| `clients/ios/` | The iPad app and its Swift package. |
| `docs/` | `SCOPE.md`, `ARCHITECTURE.md`, `STATE.md`, older plans (`PLAN.md`, `PHASE5.md`, `STATUS.md`). |
