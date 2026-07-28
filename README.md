# DocHub — AI Documentation & Knowledge Hub

An internal Documentation & Knowledge Hub: upload, organise and search team
documentation, with an AI assistant (from phase 3) that answers questions
grounded strictly in indexed content and always cites its sources.

> **Status:** phases 1 and 2 are **complete** — upload, folders, metadata and
> versioning work end to end, and uploaded documents are automatically
> extracted, chunked, embedded and made searchable by hybrid keyword +
> semantic search. Phase 3 (the AI assistant) is next. See
> [Current state](#current-state).

---

## Prerequisites

Install these before anything else.

| Tool | Version | Check with | Notes |
|---|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | **10.0** or later | `dotnet --version` | Built against 10.0.301 |
| [Node.js](https://nodejs.org) | **20 LTS** or later | `node --version` | Developed on 26.5.0 |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | any current | `docker --version` | Must be **running** — Postgres, Azurite and Ollama live here |
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

Postgres (with the pgvector extension), Azurite (the Azure Blob Storage
emulator) and Ollama (the local embedding model) all run in Docker. Nothing
else needs installing locally.

```bash
docker compose up -d --wait
```

Confirm all three are healthy:

```bash
docker compose ps
```

The first start pulls the Ollama image, which is large — give it a few
minutes.

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

### 6. Pull the embedding model

Ingestion turns document text into vectors with a local model. It is not
bundled with the Ollama image, so pull it once:

```bash
docker compose exec ollama ollama pull nomic-embed-text
```

That is a ~275 MB download and needs no API key or account. Everything stays
on your machine — document text is never sent anywhere.

### 7. Confirm setup is complete

Start the API (see below) and open http://localhost:5080/healthz. It should
report `"status": "Healthy"`. If a step above was missed, the status is
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
| Background jobs dashboard | http://localhost:5080/jobs — development only |
| OpenAPI document | http://localhost:5080/openapi/v1.json |
| Postgres | `localhost:5432` — db `dochub`, user `dochub`, password `dochub_local_dev` |
| Azurite blob | `localhost:10000` |
| Ollama | `localhost:11434` |

`GET /healthz` should return `"status": "Healthy"` with the `postgres`,
`blob-storage` and `embeddings` checks green. A `Degraded` status means a setup
step is missing — the response says which, and the command that fixes it. See
[Troubleshooting](#troubleshooting).

The jobs dashboard is unauthenticated and shows job arguments, so it is
registered in development only. It gains real authorisation when auth arrives
in phase 5.

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

## How ingestion and search work

Uploading a document returns immediately and queues a background job — a large
PDF takes far longer to process than an upload should. The job:

1. **Extracts** text according to file type
2. **Chunks** it at roughly 800 tokens with 15% overlap, splitting on the
   structure the document already has (headings, pages, slides) and only
   falling back to sentences and then raw length when a block will not fit
3. **Embeds** each chunk with the local model
4. **Stores** chunks, embeddings and section references in Postgres
5. **Marks** the document `Indexed`, or `Failed` with a reason you can read

A chunk never spans two sections, which is what lets a search result and
(from phase 3) a citation say "Page 4" rather than pointing vaguely at a file.

Watch a document move through the pipeline on the
[jobs dashboard](http://localhost:5080/jobs), or by its status in the library.

**Supported file types** — `GET /api/documents/supported-types` returns the
live list:

| Type | Handling |
|---|---|
| Markdown | Split on headings, which become the citation labels |
| Text, CSV, JSON, YAML, XML, SQL, config, logs | Read directly |
| PDF | Text layer via PdfPig, one section per page |
| Word, PowerPoint, Excel | OpenXML — headings, slide numbers and sheet names become labels |

Images and scanned PDFs have no text layer and are marked `Failed` with a clear
reason; OCR is a later phase. Legacy binary `.doc`/`.ppt`/`.xls` are not
OpenXML and are not supported.

**Search** runs two branches and merges them with reciprocal rank fusion:

- **Keyword** — Postgres full-text search over a generated `tsvector` column.
  Finds exact things a language model has no reason to consider similar to
  anything: error codes, product names, identifiers.
- **Semantic** — pgvector cosine similarity over the embeddings. Finds the
  right passage when the question uses none of the document's words.

The two produce scores on unrelated scales, so they are fused on rank position
rather than raw score. A passage both branches find outranks one only a single
branch found, and every result says which branch matched it. If the embedding
provider is down, search degrades to keyword-only and says so rather than
quietly returning half the answer.

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
  repositories over real blob storage, including the ingestion pipeline and
  hybrid search. Mocking those seams would only prove the mocks behave; the
  bugs worth catching live between the layers, such as a deleted folder failing
  to free its documents' files, or the two search branches colliding on a
  shared DbContext.

  The embedding model is the one thing faked here, using the deterministic
  `hashing` provider — tests must not depend on a model being pulled, and what
  is under test is the pipeline's wiring rather than the quality of the
  vectors. Chunking and extraction need no infrastructure at all and run as
  plain unit tests.

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
│   ├── src/app/features/         # dashboard, browse, document detail, search, settings
│   ├── src/app/shared/           # reusable components, pipes, directives
│   └── tools/gen-icons.mjs       # regenerates the Lucide icon CSS
├── server/
│   ├── src/DocHub.Api/           # controllers, DI composition, health checks
│   ├── src/DocHub.Services/      # business logic: documents, ingestion, search
│   ├── src/DocHub.DataAccess/    # EF Core, entities, repositories, migrations
│   ├── src/DocHub.Integrations/  # external systems: blob storage, embeddings, LLM, MCP
│   └── tests/                    # integration tests
├── docker-compose.yml            # Postgres + pgvector, Azurite, Ollama
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
| `Database:ConnectionString` | Postgres connection — also backs the Hangfire job store |
| `FileStorage:ConnectionString` | `UseDevelopmentStorage=true` locally; a real Azure connection string in production |
| `FileStorage:ContainerName` | Blob container for document files (default `documents`) |
| `Embeddings:Provider` | `ollama` (default) or `hashing` — see below |
| `Embeddings:BaseUrl` | Ollama endpoint (default `http://localhost:11434`) |
| `Embeddings:Model` | Embedding model (default `nomic-embed-text`) |
| `Embeddings:Dimensions` | Vector width — **must match the migrated column**, see below |
| `Ingestion:TargetTokens` | Chunk size target (default 800) |
| `Ingestion:OverlapTokens` | Tokens repeated between chunks (default 120) |
| `Cors:AllowedOrigins` | Origins allowed to call the API in development |

**Running without a model.** Setting `Embeddings:Provider` to `hashing` uses
deterministic in-process vectors instead — no download, no network. The
pipeline and search both work, but similarity becomes pure lexical overlap: it
matches "invoice" to "invoice" and has no idea that relates to "billing". It
exists so tests are hermetic, not as a substitute for a real model.

**Changing embedding provider or model.** The chunk table is migrated for a
fixed vector width (768, matching `nomic-embed-text`). A model of a different
width needs a new migration *and* a full re-index — vectors from two models are
not comparable, so mixing them silently corrupts ranking. The API validates the
configured width at startup rather than failing partway through an ingestion
run. Swapping to a hosted provider (Voyage, OpenAI) means implementing
`IEmbeddingProvider` and changing one registration; nothing in ingestion or
search changes.

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
```bash
docker compose exec ollama ollama pull nomic-embed-text
```

**Re-index a document** after it fails, or after changing the chunking
settings:

```bash
curl -X POST http://localhost:5080/api/documents/THE-DOCUMENT-ID/reindex
```

The library screen offers the same action on a failed document.

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
| Local infrastructure + solution skeleton | Done |
| Data access: entities, migrations, repositories | Done |
| Blob storage (`IFileStorage`) | Done |
| Services + API endpoints | Done |
| Angular client wired to the real API | Done |
| Text extraction (Markdown, text, PDF, Office) | Done |
| Chunking + embeddings (`IEmbeddingProvider`) | Done |
| Background ingestion on Hangfire | Done |
| Hybrid keyword + semantic search, with a search screen | Done |

**Phases 1 and 2 are complete.** Upload a Markdown, PDF or Word file and it is
extracted, chunked, embedded and searchable within seconds — by exact term or
by a question in your own words.

Not yet built, by design (later phases): the AI chat assistant with citations;
MCP repository sources; authentication and roles; the deployment pipeline. OCR
for scanned documents is also deferred, so image-only PDFs are reported as
failed rather than silently indexed as empty.

The client talks to the API through one seam, `KnowledgeGateway`. Two
implementations exist:

- `HttpKnowledgeGateway` — the real API (the default)
- `MockKnowledgeGateway` — in-memory sample data, useful for working on screens
  without running the backend

Swap them in `client/src/app/app.config.ts`. In development the Angular dev
server proxies `/api` to `http://localhost:5080` (`client/proxy.conf.json`), so
requests are same-origin and CORS never applies.

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
| `GET` | `/api/documents/supported-types` | File extensions ingestion can index |
| `POST` | `/api/documents/{id}/reindex` | Requeue a document for ingestion |
| `GET` | `/api/search?query=…` | Hybrid keyword + semantic search over indexed chunks |

Errors come back as RFC 7807 problem details — 400 for a rejected business
rule (with a message meant for the user), 404 for a missing entity.

Try it once the API is running:

```bash
curl -X POST http://localhost:5080/api/folders -H 'Content-Type: application/json' -d '{"parentId":null,"name":"Engineering"}'
```

Search, once something is indexed:

```bash
curl -G http://localhost:5080/api/search --data-urlencode 'query=how do I connect remotely'
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
- `the 'nomic-embed-text' model is not installed` → run step 6

**Documents never leave Pending** — the background worker is not processing
them. Check the [jobs dashboard](http://localhost:5080/jobs) for failed jobs
and the API log for the reason.

**Documents go straight to Failed** — open the document; the failure reason is
shown on it. The usual causes are a scanned PDF with no text layer, an
unsupported file type, or the embedding model not being pulled (step 6). Fix
the cause and re-index from the library or via
`POST /api/documents/{id}/reindex`.

**Search returns "Semantic matching is unavailable"** — Ollama is not reachable
or the model is missing. Results shown are keyword-only. Run `docker compose up
-d` and step 6, then search again. Nothing needs re-indexing.

**Search finds nothing** — only documents that reached `Indexed` are
searchable, by design: a half-processed document must not be citable. Check the
library for anything still pending or failed.

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

**The client loads but shows no data, and the console logs failed `/api` calls**
— the API isn't running. Start it in a second terminal; the dev server only
proxies `/api` through, it does not host it.
