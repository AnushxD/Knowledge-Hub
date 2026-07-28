# DocHub — AI Documentation & Knowledge Hub

An internal Documentation & Knowledge Hub: upload, organise and search team
documentation, with an AI assistant (from phase 3) that answers questions
grounded strictly in indexed content and always cites its sources.

> **Status:** phase 1 (core document management). The Angular client is
> complete; the backend is being built block by block. See
> [Current state](#current-state) for exactly what works today.

---

## Prerequisites

Install these before anything else.

| Tool | Version | Check with | Notes |
|---|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | **10.0** or later | `dotnet --version` | Built against 10.0.301 |
| [Node.js](https://nodejs.org) | **20 LTS** or later | `node --version` | Developed on 26.5.0 |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | any current | `docker --version` | Must be **running** — Postgres and Azurite live here |
| Git | any current | `git --version` | |

Editor: **VS Code** with the [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit)
extension. Open the repository root and VS Code will offer the rest of the
recommended extensions. Visual Studio for Mac is retired — don't look for it.
[JetBrains Rider](https://www.jetbrains.com/rider/) also works well if you
prefer a full IDE.

---

## First-time setup

### 1. Clone and install the EF Core CLI

```bash
git clone https://github.com/AnushxD/Knowledge-Hub.git
cd Knowledge-Hub
```

```bash
dotnet tool install --global dotnet-ef
```

If it's already installed, use `dotnet tool update --global dotnet-ef` instead.
Make sure `~/.dotnet/tools` is on your `PATH` — add this to `~/.zshrc` if
`dotnet ef` isn't found:

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
```

### 2. Start local infrastructure

Postgres (with the pgvector extension) and Azurite (the Azure Blob Storage
emulator) both run in Docker. Nothing else needs installing locally.

```bash
docker compose up -d --wait
```

Confirm both are healthy:

```bash
docker compose ps
```

### 3. Install client dependencies

```bash
npm --prefix client install
```

npm 11 gates package install scripts. If you see an `allow-scripts` warning,
approve the build tooling once:

```bash
npm --prefix client approve-scripts esbuild fsevents lmdb msgpackr-extract @parcel/watcher
```

### 4. Create the database schema

The API **never** creates or migrates anything on startup — provisioning is an
explicit step you run, so nothing is silently created behind your back.

```bash
dotnet ef database update --project server/src/DocHub.DataAccess --startup-project server/src/DocHub.Api
```

### 5. Create the blob storage container

```bash
dotnet run --project server/src/DocHub.Api -- init-storage
```

This creates the private `documents` container in Azurite and exits. It is
idempotent — running it again just reports that the container already exists.

### 6. Confirm setup is complete

Start the API (see below) and open http://localhost:5080/healthz. It should
report `"status": "Healthy"`. If either step above was missed, the status is
`Degraded` and the response names the exact command to run.

---

## Running the app

You need **two terminals** (or use the VS Code launch configs below).

**Terminal 1 — API** (Kestrel, not IIS):

```bash
dotnet run --project server/src/DocHub.Api
```

**Terminal 2 — Angular client:**

```bash
npm --prefix client start
```

| Service | URL |
|---|---|
| Angular client | http://localhost:4200 |
| API | http://localhost:5080 |
| API readiness check | http://localhost:5080/healthz |
| OpenAPI document | http://localhost:5080/openapi/v1.json |
| Postgres | `localhost:5432` — db `dochub`, user `dochub`, password `dochub_local_dev` |
| Azurite blob | `localhost:10000` |

`GET /healthz` should return `"status": "Healthy"` with both the `postgres` and
`blob-storage` checks green. A `Degraded` status means a setup step is missing —
the response says which, and the command that fixes it. See
[Troubleshooting](#troubleshooting).

### Running and debugging from VS Code

Open the **repository root** (not `client/`) and pick a configuration from the
Run and Debug panel:

- **Full stack (API + Client)** — both at once
- **API (.NET)** — starts Docker, builds, launches with the debugger attached
- **Client (Angular in Chrome)** — runs `ng serve`, then attaches Chrome
- **Attach to running .NET process** — for an API already running in a terminal

Breakpoints work in both C# and TypeScript. The API config runs
`docker compose up -d --wait` first, so it can't race the containers.

---

## Running tests

```bash
dotnet test server/DocHub.slnx
```

Both test projects run against the **real containers**, not fakes or in-memory
providers. Docker must be running.

- **Data access** — materialised-path queries, `text[]` columns and the GIN
  index are exactly what an in-memory provider would fail to catch. Uses a
  separate `dochub_test` database, created and dropped per run.
- **Integrations** — exercises the actual Azure SDK against Azurite, including
  the 404 behaviour the code depends on. Uses a throwaway blob container per
  run.
- **Services** — the whole stack minus HTTP: real services over real
  repositories over real blob storage. Mocking those seams would only prove the
  mocks behave; the bugs worth catching live between the layers, such as a
  deleted folder failing to free its documents' files.

None of them touch your development data. Override the targets with the
`DOCHUB_TEST_DB` and `DOCHUB_TEST_BLOBS` environment variables if you need to.

Client tests (`npm --prefix client test`) exist as a harness but there are no
specs yet.

---

## Project layout

```
Knowledge-Hub/
├── client/                       # Angular 22 SPA
│   ├── src/app/core/             # models, gateway, state, theme
│   ├── src/app/layout/           # shell, nav rail, folder tree, command palette
│   ├── src/app/features/         # dashboard, browse, document detail, settings
│   ├── src/app/shared/           # reusable components, pipes, directives
│   └── tools/gen-icons.mjs       # regenerates the Lucide icon CSS
├── server/
│   ├── src/DocHub.Api/           # controllers, DI composition, health checks
│   ├── src/DocHub.Services/      # business logic (in progress)
│   ├── src/DocHub.DataAccess/    # EF Core, entities, repositories, migrations
│   ├── src/DocHub.Integrations/  # external systems: blob storage, LLM, MCP
│   └── tests/                    # integration tests
├── docker-compose.yml            # Postgres + pgvector, Azurite
├── CLAUDE.md                     # architecture rules and conventions — read this
└── architecture-blueprint.md     # the full technical design
```

Architecture rules (layering, what may reference what, config and secrets
handling) live in [CLAUDE.md](CLAUDE.md). Read it before adding code.

---

## Configuration

| File | Purpose | Safe to commit |
|---|---|---|
| `appsettings.json` | Shape and defaults only, no values | Yes |
| `appsettings.Development.json` | Local container connection strings | Yes — no real credentials |
| `dotnet user-secrets` | Real secrets (LLM API keys, etc.) | Never committed |
| Environment variables / Key Vault | Production configuration | Never committed |

Key settings, one strongly-typed Options class per external dependency:

| Key | Purpose |
|---|---|
| `Database:ConnectionString` | Postgres connection |
| `FileStorage:ConnectionString` | `UseDevelopmentStorage=true` locally; a real Azure connection string in production |
| `FileStorage:ContainerName` | Blob container for document files (default `documents`, created at startup) |
| `Cors:AllowedOrigins` | Origins allowed to call the API in development |

All are validated at startup, so a missing or empty value fails the boot rather
than the first request. Note that validation is all the app does at startup —
it never creates a database, applies a migration, or creates a container on
its own. Those are the explicit setup steps above.

Never put a real secret in any `appsettings.*.json`. To add one locally:

```bash
dotnet user-secrets set "SomeProvider:ApiKey" "value" --project server/src/DocHub.Api
```

---

## Common tasks

**Add a migration** after changing entities:

```bash
dotnet ef migrations add YourMigrationName --project server/src/DocHub.DataAccess --startup-project server/src/DocHub.Api
```

**Reset everything** — wipes the database *and* all stored files, then re-runs
setup:

```bash
docker compose down -v && docker compose up -d --wait
```
```bash
dotnet ef database update --project server/src/DocHub.DataAccess --startup-project server/src/DocHub.Api
```
```bash
dotnet run --project server/src/DocHub.Api -- init-storage
```

**Roll a migration back** to a known one (use `0` to undo everything):

```bash
dotnet ef database update PreviousMigrationName --project server/src/DocHub.DataAccess --startup-project server/src/DocHub.Api
```

**Add an icon to the client** — extend the map in `client/tools/gen-icons.mjs`,
then regenerate (never edit `icons.css` by hand):

```bash
node client/tools/gen-icons.mjs
```

**Inspect stored files** — Azurite blobs are browsable with the
[Azure Storage Explorer](https://azure.microsoft.com/products/storage/storage-explorer)
(connect to "Local storage emulator"), or from the CLI if you have it:

```bash
az storage blob list --container-name documents --connection-string "UseDevelopmentStorage=true" --output table
```

**Stop everything:**

```bash
docker compose down
```

---

## Current state

| Area | Status |
|---|---|
| Angular client (all phase 1 screens) | Done — runs on mock data |
| Local infrastructure + solution skeleton | Done |
| Data access: entities, migrations, repositories | Done |
| Blob storage (`IFileStorage`) | Done |
| Services + API endpoints | Done |
| Client wired to the real API | Not started — still `MockKnowledgeGateway` |

The API is fully working — you can create folders and upload documents with
`curl` or from the OpenAPI document today. The **client** still runs on
in-memory mock data, so uploads made in the browser do not persist across a
refresh. Swapping it over is a one-line provider change in
`client/src/app/app.config.ts`, and it is the last piece of phase 1.

### API endpoints

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/folders` | Whole folder tree with recursive document counts |
| `POST` | `/api/folders` | Create a folder |
| `PUT` | `/api/folders/{id}` | Rename a folder |
| `DELETE` | `/api/folders/{id}` | Delete a folder, its subtree and its files |
| `GET` | `/api/documents` | List/filter documents (folder, text, tag, status, owner, sort, paging) |
| `GET` | `/api/documents/{id}` | Document with breadcrumb and version history |
| `GET` | `/api/documents/{id}/content` | Download the current file |
| `POST` | `/api/documents?folderId=…` | Upload a document (multipart `file`) |
| `POST` | `/api/documents/{id}/versions` | Upload a replacement as a new version |
| `PATCH` | `/api/documents/{id}` | Update title, description, tags, starred |
| `POST` | `/api/documents/{id}/move` | Move to another folder |
| `DELETE` | `/api/documents/{id}` | Delete a document and all its files |
| `GET` | `/api/documents/stats` | Library counts for the dashboard |
| `GET` | `/api/documents/tags` | Every tag in use |

Errors come back as RFC 7807 problem details — 400 for a rejected business
rule (with a message meant for the user), 404 for a missing entity.

Try it once the API is running:

```bash
curl -X POST http://localhost:5080/api/folders -H 'Content-Type: application/json' -d '{"parentId":null,"name":"Engineering"}'
```

Roadmap phases (search, AI assistant, MCP, auth, deployment) are listed in
[CLAUDE.md](CLAUDE.md#roadmap--build-in-this-order-dont-jump-ahead).

---

## Troubleshooting

**`dotnet ef` not found** — install the tool and put `~/.dotnet/tools` on your
`PATH`, see [First-time setup](#1-clone-and-install-the-ef-core-cli).

**API exits at startup with a connection error** — Docker isn't running, or the
containers aren't up. Run `docker compose ps` and check both are healthy.
Config is validated at boot on purpose, so a bad connection string fails
immediately rather than on the first request.

**`/healthz` reports `Degraded`** — a setup step hasn't been run. The response
says which one:

- `no migrations have been applied` → run step 4
- `container does not exist` → run step 5

**A request fails with "container does not exist"** — same cause. The app
deliberately does not create storage at runtime, so run step 5.

**`relation "documents" does not exist`** — the database schema is missing; run
step 4. This also happens after `docker compose down -v`, which deletes the
volumes.

**`/healthz` reports `blob-storage` unhealthy** — Azurite trails the Azure SDK's
service version and rejects newer ones. `docker-compose.yml` passes
`--skipApiVersionCheck` to handle this; if you changed that line, put it back.

**Port already in use** — the API uses 5080 and the client 4200. Find the
offender with `lsof -ti:5080` and stop it, or change the port in
`server/src/DocHub.Api/Properties/launchSettings.json` (and `Cors:AllowedOrigins`
in `appsettings.Development.json` if you move the client).

**Tests fail to connect** — they need Docker running, same as the app. They
create and drop the `dochub_test` database themselves.

**Client build fails after pulling** — dependencies changed; run
`npm --prefix client install` again.
