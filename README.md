# Document Hub — AI Documentation & Knowledge Hub

An internal Documentation & Knowledge Hub: upload, organise and search team
documentation, with an AI assistant that answers questions grounded strictly
in indexed content and always cites its sources.

> **Status:** **v1 — complete.** Phases 1–6 are done. Documents upload, index and become
> searchable by hybrid keyword + semantic search, and an AI assistant answers
> questions from them — citing the exact passage behind every claim, and
> saying "I don't know" when the answer isn't there. The assistant now retrieves
> through an `IKnowledgeSource` abstraction, so a repository source over MCP
> joins document search without the assistant changing; that MCP client now
> exists and is switched on with one configuration key, falling back to a stub
> that contributes nothing. Everything sits behind a sign-in with
> Admin / Editor / Viewer roles, and Google sign-in can be switched on for
> company addresses. It ships as container images or as a single-site IIS
> artefact, both from one CI pipeline, with an activity trail recording who did
> what. See [Current state](#current-state).

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

> This section sets up a **development machine** — the Mac or PC you write code
> on, running the API and client from the command line. To set up the **org
> Windows machine the app is hosted on**, go to
> [Hosting on the org Windows machine (IIS)](#hosting-on-the-org-windows-machine-iis)
> instead; it is self-contained and does not need any of the steps below.

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
emulator) and Ollama (the local AI models) all run in Docker. Nothing else
needs installing locally.

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

### 6. Pull the models

Two local models do the AI work — one turns text into vectors for search, the
other writes the assistant's answers. Neither is bundled with the Ollama
image, so pull them once:

```bash
docker compose exec ollama ollama pull nomic-embed-text
```
```bash
docker compose exec ollama ollama pull llama3.2:3b
```

About 2.3 GB in total, with no API key or account needed. Everything stays on
your machine — neither your documents nor your questions are sent anywhere.

On CPU-only Docker expect the assistant to produce roughly 5–15 tokens a
second, so an answer streams in over several seconds. That is the cost of
running locally for free; see [Configuration](#configuration) for what to
change if you'd rather use a hosted model.

### 7. Set the administrator password

The database arrives with one account, `dev@dochub.local`, and no password —
a password hash is salted per call, so it cannot live in a migration, and a
constant one would be a credential in source control. Set it explicitly:

```bash
dotnet run --project server/src/DocHub.Api -- seed-admin
```

That applies `Authentication:SeedAdminPassword` from
`appsettings.Development.json` (`documenthub-local-dev-admin` by default) and exits.
Re-running it resets the password, which is also how to recover a forgotten
local one. There is no self-registration — sign in as the administrator and
create everyone else under **People**.

### 8. Confirm setup is complete

Start the API (see below) and open http://localhost:5080/healthz — it stays
anonymous so an orchestrator can reach it. It should report
`"status": "Healthy"`. If a step above was missed, the status is `Degraded` and
the response names the exact command to run.

Then open the client and sign in as `dev@dochub.local`. Every other endpoint,
plus `/swagger` and `/jobs`, now requires a session.

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
| Swagger UI — browse and call every endpoint | http://localhost:5080/swagger — development only |
| Background jobs dashboard | http://localhost:5080/jobs — development only |
| OpenAPI document | http://localhost:5080/openapi/v1.json |
| Postgres | `localhost:5432` — db `documenthub`, user `documenthub`, password `documenthub_local_dev` |
| Azurite blob | `localhost:10000` |
| Ollama | `localhost:11434` |

`GET /healthz` should return `"status": "Healthy"` with the `postgres`,
`blob-storage`, `embeddings` and `assistant-model` checks green. A `Degraded` status means a setup
step is missing — the response says which, and the command that fixes it. See
[Troubleshooting](#troubleshooting).

Browsing to the API root redirects to **Swagger UI**, where every endpoint can
be expanded and called with *Try it out* — including file uploads, which get a
real file picker. It reads the same `/openapi/v1.json` document the API already
serves, so it can never drift from the actual routes.

Swagger UI and the jobs dashboard are both registered in development only: they
are unauthenticated, and both invite real requests against real data. They gain
proper authorisation when auth arrives in phase 5.

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

A chunk never spans two sections, which is what lets a search result or a
citation say "Page 4" rather than pointing vaguely at a file.

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

## How the assistant answers

The assistant answers **only** from indexed documents. Asking it a question:

1. **Retrieves** passages using the same hybrid search the search screen uses —
   deliberately, so what it reasons over is what you could have found yourself
2. **Refuses immediately if nothing was retrieved.** The model is never called
   with no sources; that is precisely the situation that produces confident,
   fabricated answers
3. **Builds a grounded prompt** — the numbered passages plus rules that permit
   nothing outside them
4. **Streams** the answer back token by token
5. **Verifies every citation** against the passages actually supplied, then
   persists the turn

Step 5 is what makes the citations trustworthy. A model asked to cite will
occasionally produce a plausible `[7]` when it was given four sources; those
markers are stripped rather than rendered as links to nothing. If an answer
ends up citing nothing at all, the UI says so instead of letting it pass as
grounded.

**"I don't know" is a designed outcome, not a failure.** When the documents
don't cover a question the assistant says so plainly, and that turn is saved
like any other — a recorded refusal is what makes the grounding auditable.

Everything is persisted: questions, answers, and the sources each answer
cited, with the document title and heading copied onto the citation. Renaming
or deleting a document later cannot rewrite what a historical answer claimed
to be based on.

Citations link to `/docs/:id?chunk=N`, which opens the document with that exact
passage highlighted.

For the full technical walkthrough — every stage from the HTTP request to the
persisted citation, with flow charts, the exact prompt, the rank-fusion maths and
a failure-mode table — see [chat-pipeline.md](chat-pipeline.md).

---

## Running tests

```bash
dotnet test server/DocHub.slnx
```

Both test projects run against the **real containers**, not fakes or in-memory
providers. Docker must be running.

- **Data access** — materialised-path queries, `text[]` columns and the GIN
  index are exactly what an in-memory provider would fail to catch. Uses a
  separate `documenthub_test` database, created and dropped per run.
- **Integrations** — exercises the actual Azure SDK against Azurite, including
  the 404 behaviour the code depends on. Uses a throwaway blob container per
  run.
- **Services** — the whole stack minus HTTP: real services over real
  repositories over real blob storage, including the ingestion pipeline and
  hybrid search. Mocking those seams would only prove the mocks behave; the
  bugs worth catching live between the layers, such as a deleted folder failing
  to free its documents' files, or the two search branches colliding on a
  shared DbContext.

  The two AI models are the only things faked: embeddings use the
  deterministic `hashing` provider, and the assistant's model is scripted per
  test. Tests must not depend on a model being pulled, and what is under test
  is the orchestrator's judgement — what it retrieves, whether it calls the
  model at all, what it does with a fabricated citation — none of which
  depends on a model being good. Chunking, extraction and citation
  verification need no infrastructure at all and run as plain unit tests.

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
│   ├── src/app/features/         # dashboard, browse, document detail, search, chat, settings
│   ├── src/app/shared/           # reusable components, pipes, directives
│   └── tools/gen-icons.mjs       # regenerates the Lucide icon CSS
├── server/
│   ├── src/DocHub.Api/           # controllers, DI composition, health checks
│   ├── src/DocHub.Services/      # business logic: documents, ingestion, search, chat
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
| `FileStorage:ServiceVersion` | Storage REST API version, e.g. `2025-11-05`. Empty uses the SDK default. Only needed against an Azurite older than the SDK — see [Azurite rejects the API version](#azurite-rejects-the-api-version) |
| `Embeddings:Provider` | `ollama` (default) or `hashing` — see below |
| `Embeddings:BaseUrl` | Ollama endpoint (default `http://localhost:11434`) |
| `Embeddings:Model` | Embedding model (default `nomic-embed-text`) |
| `Embeddings:Dimensions` | Vector width — **must match the migrated column**, see below |
| `Ingestion:TargetTokens` | Chunk size target (default 800) |
| `Ingestion:OverlapTokens` | Tokens repeated between chunks (default 120) |
| `Llm:Provider` | `ollama` — the model that writes answers |
| `Llm:Model` | Answer model (default `llama3.2:3b`) |
| `Llm:Temperature` | Low by default (0.1); sampling variety is how a model starts inventing |
| `Llm:ContextTokens` | Context window offered to the model (default 8192). Ollama's own default is 2048 and it discards the overflow silently — too low and the assistant answers without seeing the passages it was told to cite |
| `Chat:PassageCount` | Passages retrieved per question (default 6) |
| `Chat:HistoryTurns` | Prior turns replayed for follow-ups (default 4) |
| `KnowledgeSources:RepositoryProvider` | `none` (default) registers the stub that contributes nothing; `mcp` searches a real MCP server |
| `KnowledgeSources:RepositoryEndpoint` | The MCP server's address; unused while the provider is `none`. An override saved on `/sources` wins over this |
| `KnowledgeSources:RepositoryToolName` | Which tool to search with. Empty discovers the first tool with `search` in its name — a guess, so name it once known |
| `KnowledgeSources:RepositoryMaxResults` | Passages to ask the tool for (default 8) |
| `Authentication:SessionHours` | Session lifetime, sliding (default 8) |
| `Authentication:KeyPath` | Directory for the Data Protection keys that encrypt the session cookie. **Set it on IIS**, or every application pool recycle signs everyone out. Leave unset in containers |
| `Knowledge:SourceTimeoutSeconds` | How long one knowledge source may take before the answer goes ahead without it (default 10) |
| `Authentication:SeedAdminPassword` | Applied by `seed-admin`; a real deployment puts this in user-secrets |
| `Authentication:Google:Enabled` | Turns Google sign-in on. Off by default |
| `Authentication:Google:ClientId` | From the Google Cloud console |
| `Authentication:Google:ClientSecret` | **Secret** — `dotnet user-secrets`, never an appsettings file |
| `Authentication:Google:AllowedDomains` | Email domains allowed in. **Empty admits nobody**, never everybody |
| `Authentication:Google:AutoProvision` | Create a Viewer for a verified allowed-domain sign-in (default true) |
| `RateLimits:ChatRequests` / `ChatWindowSeconds` | Questions per user per window (default 10 / 60) |
| `Cors:AllowedOrigins` | Origins allowed to call the API in development |

**Using a bigger or hosted model.** `Llm:Model` takes any model Ollama can
serve — `llama3.1:8b` or `qwen2.5:7b` follow the citation format noticeably
better than the 3B default, at the cost of speed and disk. Pull it, change the
setting, restart; nothing is re-indexed, because generation and retrieval are
separate concerns.

Switching to a hosted API (Claude, OpenAI) is one more `ILlmProvider`
implementation and one more branch in `AddIntegrations` — the RAG orchestrator
never learns which model answered. That is why `ILlmProvider` exists rather
than the orchestrator calling Ollama directly.

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
docker compose exec ollama ollama pull nomic-embed-text && docker compose exec ollama ollama pull llama3.2:3b
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

## Deployment

Three things exist here: container images, a CI workflow, and an IIS artefact.
They share one binary — nothing branches on how it was deployed.

### Continuous integration

`.github/workflows/ci.yml` runs on every push and pull request to `main`:

- **Server** — builds and tests against the repository's *own* `docker-compose.yml`
  Postgres and Azurite. The tests deliberately use real infrastructure, and
  reusing the compose file means CI cannot drift from a developer's machine.
- **Client** — typechecks, builds, and asserts the generated `icons.css` is
  current, so an edited icon map that was never regenerated fails here rather
  than as a missing glyph later.
- **Images** — builds both Dockerfiles once the code is known good. They are
  *built, not pushed*: which registry to publish to needs credentials this
  repository does not have yet.

`DOCHUB_TEST_DB` is deliberately left unset in CI. Both test projects read that
one variable but default to different databases, and each drops and recreates
its own — pointing them at a single database would have them delete the schema
out from under each other.

### Running the built stack locally

```bash
docker compose -f docker-compose.yml -f docker-compose.app.yml up -d --build
```

The client is then on http://localhost:4300 and the API on http://localhost:8080.
Provisioning is still explicit — after the first start:

```bash
docker compose -f docker-compose.yml -f docker-compose.app.yml run --rm api dotnet DocHub.Api.dll init-storage
```
```bash
docker compose -f docker-compose.yml -f docker-compose.app.yml run --rm api dotnet DocHub.Api.dll seed-admin
```

Migrations still come from the repository with `dotnet ef database update`.

In this arrangement nginx serves the client and proxies `/api` to the API, so
the two are same-origin and the session cookie needs no CORS or `SameSite=None`.

### Hosting on the org Windows machine (IIS)

The complete first-time setup for the machine the app will actually run on.
Follow it top to bottom; every step is configuration, not a code change.

**What this machine needs — and what it does not.** The artefact you deploy is
already compiled: .NET assemblies plus the built Angular bundle as plain JS and
CSS. Nothing is built on the server.

| Needed | Not needed |
|---|---|
| .NET 10 **Hosting Bundle** (runtime + the IIS module) | .NET **SDK** — only if you choose to run migrations here, and step 4 gives an alternative that avoids it |
| **Docker Engine**, managed through Portainer, for Postgres and Azurite | **Node.js / npm** — the Angular app arrives pre-built |
| Ollama, for the two local models | **Angular CLI** — same reason |
| | **Git** — you paste one file into Portainer, you do not clone the repository |
| | **A `docker` CLI** — Portainer deploys and manages the stack for you |

**What runs where.** Only the application goes in IIS. The infrastructure runs
as containers, from the same `docker-compose.yml` a developer uses:

| Piece | Where | Why |
|---|---|---|
| API + client | **IIS**, one site | The API serves the client from `wwwroot`, so they are same-origin and the session cookie needs no CORS |
| PostgreSQL + pgvector | **Portainer stack**, on this machine | pgvector has no supported Windows installer; the `pgvector/pgvector:pg17` image is the reliable route |
| Blob storage | **Portainer stack** (Azurite), or real Azure | Azurite is an emulator — see step 2 |
| Ollama | **Native Windows install**, or the same stack | Native gets the GPU if this box has one; containerised is fine on CPU and keeps everything in one place |

These instructions assume Docker runs on **this same machine**, so the
containers publish their ports to `localhost` and every connection string below
says so. If you ever move the containers to another host, the only change is
swapping `localhost` for that host's address in step 3 — and Azurite needs its
long-form connection string, because `UseDevelopmentStorage=true` resolves to
`127.0.0.1` and cannot express a remote host.

#### 1. Install the prerequisites

In PowerShell **as Administrator**:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-StaticContent, IIS-DefaultDocument, IIS-HttpErrors, IIS-HttpLogging, IIS-RequestFiltering, IIS-Security -All
```

On Windows Server, use Server Manager → *Add Roles and Features* → **Web Server (IIS)**.

Then install, in this order:

1. The **.NET 10 Hosting Bundle** — not the SDK, not the plain runtime — from
   <https://dotnet.microsoft.com/download/dotnet/10.0>.
2. **Ollama for Windows** from <https://ollama.com/download/windows> — unless
   you would rather run it as part of the stack, see step 2.

Nothing else installs here. The containers are deployed through Portainer, so
this machine needs no Docker CLI and no Docker Desktop licence. If the org
already runs a PostgreSQL you can use, point `Database__ConnectionString` at it
in step 3 and skip the Postgres container entirely — provided the `vector`
extension can be enabled on it.

> **Order matters.** The Hosting Bundle registers `AspNetCoreModuleV2` with IIS.
> Installed *before* IIS, that registration is missing and every request returns
> **500.19** — re-run the installer with `/repair` if that happens.

```powershell
iisreset
C:\Windows\System32\inetsrv\appcmd.exe list modules
```

The second command should list `AspNetCoreModuleV2`.

#### 2. Deploy the infrastructure as a Portainer stack

In Portainer: **Stacks → Add stack**, name it `documenthub`, and paste the contents of
the repository's `docker-compose.yml` into the web editor. Then **Deploy the
stack**.

Two adjustments before deploying:

- **Remove the `ollama` service** if you installed Ollama natively in step 1,
  which is the recommended route: a native install picks up an NVIDIA GPU if the
  machine has one, whereas reaching a GPU from a container on Windows needs the
  WSL 2 backend and the NVIDIA container toolkit. Keep the service instead if
  this box is CPU-only and you would rather manage everything in one stack.
- **Change the Postgres password** from the compose default, and use the same
  value in step 3.

The stack publishes Postgres on `5432` and Azurite on `10000`, reachable from
this machine as `localhost`. Nothing needs opening in the firewall — the API and
the containers are on the same host.

Wait for the containers to report *healthy* in Portainer before continuing; the
compose file defines health checks, so Portainer shows the real state rather
than just "running".

Then pull the two models, once, a few GB in total:

- **Ollama on Windows** — from PowerShell here:

  ```powershell
  ollama pull nomic-embed-text
  ```
  ```powershell
  ollama pull llama3.2:3b
  ```

- **Ollama in the stack** — in Portainer open the `ollama` container, choose
  **Console → Connect** (`/bin/sh`), and run the same two `ollama pull` commands
  there.

> **Azurite is an emulator.** Fine for a pilot, and its data survives restarts in
> a Docker volume, but it is not a supported production store. If the org has
> Azure, create a Storage Account and use its connection string in step 3
> instead — `IFileStorage` already speaks the real Blob API, so nothing else
> changes.

#### 3. Create the application pool and its configuration

Configuration reaches the app as **environment variables**, where a `:` in a
config key becomes `__`. Scoping them to the pool is tighter than machine-wide,
where any process on the box could read them.

```powershell
$appcmd = "C:\Windows\System32\inetsrv\appcmd.exe"
& $appcmd add apppool /name:DocumentHub /managedRuntimeVersion:""
```

`managedRuntimeVersion:""` means **No Managed Code** — the .NET runtime lives in
the published app, not in IIS.

```powershell
function Set-PoolEnv($name, $value) {
  & $appcmd set config -section:system.applicationHost/applicationPools `
    "/+[name='DocumentHub'].environmentVariables.[name='$name',value='$value']" /commit:apphost
}

```powershell
Set-PoolEnv "ASPNETCORE_ENVIRONMENT"        "Production"
Set-PoolEnv "Database__ConnectionString"    "Host=localhost;Port=5432;Database=documenthub;Username=documenthub;Password=<the password you set in step 2>"
Set-PoolEnv "FileStorage__ConnectionString" "UseDevelopmentStorage=true"
Set-PoolEnv "FileStorage__ContainerName"    "documents"
# Only when uploads fail with "InvalidHeaderValue" — see the troubleshooting
# entry. Harmless to leave unset against real Azure.
Set-PoolEnv "FileStorage__ServiceVersion"   "2025-11-05"
Set-PoolEnv "Embeddings__BaseUrl"           "http://localhost:11434"
Set-PoolEnv "Llm__BaseUrl"                  "http://localhost:11434"
Set-PoolEnv "Authentication__KeyPath"       "C:\inetpub\documenthub-keys"
```

`Authentication__KeyPath` is where the keys that encrypt the session cookie are
kept. Without it they live in the application pool's user profile, and **every
recycle or deploy signs everybody out** — which looks like an intermittent bug
rather than a setting. Create the folder now; step 5 grants access to it.

`UseDevelopmentStorage=true` is the Azure SDK's shorthand for Azurite on
`127.0.0.1` — correct here, and the reason moving the containers elsewhere later
would need the long-form connection string instead.

Optional settings worth knowing:

| Variable | Notes |
|---|---|
| `Authentication__SessionHours` | Session lifetime, default 8 |
| `RateLimits__ChatRequests` | Questions per user per window, default 10 |
| `Llm__Model` | `llama3.1:8b` or `qwen2.5:7b` follow the citation format better than the 3B default, at the cost of speed |
| `Llm__ContextTokens` | Lower it on a constrained box — but lower `Chat__PassageCount` to match, rather than letting the prompt overflow |
| `KnowledgeSources__RepositoryProvider` | Leave at `none` — an administrator can set the MCP address from **Knowledge sources** in the UI instead, which overrides this |

For Google sign-in:

```powershell
Set-PoolEnv "Authentication__Google__Enabled"          "true"
Set-PoolEnv "Authentication__Google__ClientId"         "….apps.googleusercontent.com"
Set-PoolEnv "Authentication__Google__ClientSecret"     "…"
Set-PoolEnv "Authentication__Google__AllowedDomains__0" "your-company.com"
```

The redirect URI registered in the Google Cloud console must be
`https://<your-host>/signin-google` exactly. An **empty** allow-list admits
nobody by design — the app refuses to start rather than letting every Google
account in the world sign in.

#### 4. Create the database schema

Provisioning is never automatic; the app will not migrate a database on startup.

With the .NET SDK on the machine:

```powershell
dotnet ef database update --project server\src\DocHub.DataAccess --startup-project server\src\DocHub.Api
```

Without it — preferable on a server — generate an idempotent script on a
development machine:

```bash
dotnet ef migrations script --idempotent --project server/src/DocHub.DataAccess --startup-project server/src/DocHub.Api --output documenthub-schema.sql
```

The script is safe to re-run and applies only the migrations that are missing.
Portainer gives you no `docker compose exec`, so apply it one of these ways:

- **With `psql` on this machine**, against the published port — the cleanest
  route if you have the client installed:

  ```powershell
  psql -h localhost -p 5432 -U documenthub -d documenthub -f documenthub-schema.sql
  ```
- **Through Portainer**, with no client to install — open the `postgres`
  container, **Console → Connect** (`/bin/sh`), run `psql -U documenthub -d documenthub`,
  and paste the script in. Fine once; awkward for a long script.

#### 5. Deploy the site

Get the artefact from the **Publish (IIS artefact)** workflow in GitHub Actions
and download `documenthub-iis-*`. It is the published API with the built Angular app
already inside `wwwroot` — compiled output only, which is why this machine needs
no build tooling.

If you would rather build it yourself, do so **on a development machine** — the
one place Node and the .NET SDK are needed — and copy the result across:

```bash
cd client && npm ci && npm run build && cd ..
dotnet publish server/src/DocHub.Api/DocHub.Api.csproj -c Release -r win-x64 --self-contained false -o publish
mkdir -p publish/wwwroot && cp -r client/dist/client/browser/. publish/wwwroot/
```

Extract to `C:\inetpub\documenthub`, then:

```powershell
& $appcmd add site /name:DocumentHub /physicalPath:"C:\inetpub\documenthub" /bindings:"http/*:8080:"
& $appcmd set app "DocumentHub/" /applicationPool:DocumentHub
```

**Load the user profile — do not skip this:**

```powershell
& $appcmd set config -section:system.applicationHost/applicationPools `
  "/[name='DocumentHub'].processModel.loadUserProfile:true" /commit:apphost
```

ASP.NET Core encrypts the session cookie with Data Protection keys. With no user
profile loaded those keys are not persisted, so **every application pool recycle
signs everybody out** — and it presents as an intermittent bug rather than a
configuration problem.

Permissions — read and execute on the folder, write only to `logs\`:

```powershell
icacls "C:\inetpub\documenthub" /grant "IIS AppPool\DocumentHub:(OI)(CI)RX"
icacls "C:\inetpub\documenthub\logs" /grant "IIS AppPool\DocumentHub:(OI)(CI)M"

# The Data Protection keys from step 3 — the pool has to be able to write here.
New-Item -ItemType Directory -Force -Path "C:\inetpub\documenthub-keys" | Out-Null
icacls "C:\inetpub\documenthub-keys" /grant "IIS AppPool\DocumentHub:(OI)(CI)M"
```

The key folder sits **outside** the site directory on purpose: replacing the
published folder on the next deploy must not take the keys with it.

#### 6. Provision storage and the administrator

These are one-shot commands against the same binary IIS runs. The pool's
environment variables do **not** reach a command prompt, so set what they need
in the shell first:

```powershell
cd C:\inetpub\documenthub
# Pool variables do not reach a command prompt, so repeat the two these need.
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Database__ConnectionString = "Host=localhost;Port=5432;Database=documenthub;Username=documenthub;Password=<the password you set in step 2>"
$env:FileStorage__ConnectionString = "UseDevelopmentStorage=true"

.\DocHub.Api.exe init-storage
```

Then set the administrator password — type it in rather than storing it:

```powershell
$env:Authentication__SeedAdminPassword = "<a real password, 7+ characters>"
.\DocHub.Api.exe seed-admin
```

It prints `Password set for dev@dochub.local (Admin).` That account is the only
way in; re-running the command resets the password if it is ever forgotten.
Close the shell afterwards.

#### 7. Start and verify

```powershell
& $appcmd start site /site.name:DocumentHub
```

In order:

1. `http://localhost:8080/healthz` → `"status": "Healthy"`, with `postgres`,
   `blob-storage`, `embeddings` and `assistant-model` all healthy. Anything
   `Degraded` names the command that fixes it.
2. `http://localhost:8080/` → the sign-in screen.
3. Sign in as `dev@dochub.local`.
4. Upload a Markdown file and watch it reach **Indexed**.
5. Ask the assistant about it — the answer should stream in word by word. All at
   once means response buffering; see below.

Open it to the network:

```powershell
New-NetFirewallRule -DisplayName "Document Hub" -Direction Inbound -LocalPort 8080 -Protocol TCP -Action Allow
```

For HTTPS, add a binding with the org certificate. The session cookie is
`SecurePolicy = SameAsRequest`, so it works over plain HTTP inside the network
and becomes `Secure` automatically once the site is served over HTTPS — no
configuration change either way.

#### When something goes wrong

| Symptom | Cause |
|---|---|
| **500.19** on every request | Hosting Bundle installed before IIS. Re-run its installer with `/repair`, then `iisreset` |
| **500.30** on startup | The app threw while starting, nearly always configuration. Set `stdoutLogEnabled="true"` in `web.config`, reproduce, read `logs\stdout_*.log`, then turn it back off |
| **Everyone signed out** after a recycle or deploy | `Authentication__KeyPath` is unset, or the pool cannot write to it. Check step 3 and the `icacls` grant in step 5 |
| Answer **arrives in one lump** instead of streaming | Response buffering. `web.config` sets `responseBufferLimit` to `0`; check it survived the deploy, and that nothing in front of IIS buffers too |
| File of 25–28 MB rejected with a bare **404.13** | IIS checked its own limit first. `web.config` sets `maxAllowedContentLength` to match the app's 25 MB |
| `/healthz` reports **postgres** or **blob-storage** degraded | Check the stack is running and healthy in Portainer, and that the password in `Database__ConnectionString` matches the one the stack was deployed with |
| Health check says the **assistant model** is missing | Ollama runs in the signed-in user's session by default. Confirm `http://localhost:11434` answers from the server itself, or set it to run as a service |
| **Ingestion stalls** when nobody uses the app | Hangfire runs in-process. Set the pool's *Idle Time-out* to `0` and *Start Mode* to `AlwaysRunning` |

#### Upgrading later

1. Download the new artefact.
2. Apply new migrations (step 4) — **before** swapping files, not after.
3. Stop the site, replace the folder contents, start the site.
4. `init-storage` only if storage configuration changed. `seed-admin` is not
   needed again.

#### Why one site rather than two

The API serves the built client from `wwwroot`, so there is no Application
Request Routing to install and keep configured, and no cross-origin session
cookie to get right. In the container image `wwwroot` is empty and nginx does
that job instead — the same binary covers both.

Two settings in `web.config` are not defaults and the app is wrong without them:
`responseBufferLimit="0"`, or the assistant's server-sent events are buffered;
and `maxAllowedContentLength`, matched to the 25 MB the service enforces.

### Secrets

Nothing secret is committed, and the pipeline does not carry any yet.

| Where | How configuration arrives |
|---|---|
| Local development | `appsettings.Development.json` (no real values) and `dotnet user-secrets` |
| Containers | Environment variables — `Database__ConnectionString`, `Authentication__Google__ClientSecret`, and so on |
| IIS | Environment variables on the site or app pool, or `web.config` `environmentVariables` for non-secret values |
| Azure, later | Key Vault |

`appsettings.Development.json` is excluded from both the publish output and the
Docker build context. It is safe in the repository because it only points at
containers on one machine, but it also carries the local `seed-admin` password,
and no deployable artefact should contain a credential.

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
| Answer generation (`ILlmProvider`) | Done |
| RAG orchestrator with citation verification | Done |
| Assistant screen: streaming answers, sources, session history | Done |
| `IKnowledgeSource` abstraction + composite retrieval, `/sources` screen | Done |
| Sign-in, roles, admin account management, Google sign-in, rate limiting | Done |
| Container images, GitHub Actions CI, IIS artefact and single-site hosting | Done |
| Activity trail behind the dashboard feed | Done |
| Document previews rendered as themselves (Markdown, source, PDF, images) | Done |
| Citations that resolve outside the hub, not just to documents | Done |
| Real MCP repository client behind `RepositoryProvider: mcp` | Done |

**v1 is complete.** Upload a Markdown, PDF or Word file and it is
extracted, chunked, embedded and searchable within seconds. Ask a question and
the assistant answers from those documents, links every claim to the exact
passage behind it, and declines when the answer isn't there.

Retrieval runs through `IKnowledgeSource`: every configured source is searched
concurrently and merged by rank, a source that fails is left out of that answer
and named in the reply rather than failing the whole question, and `/sources`
shows which are contributing. The repository source is a real MCP client: set
`KnowledgeSources:RepositoryProvider` to `mcp` and give it an address, and its
passages join document search and are cited alongside documents. Left at `none`
it is a stub that contributes nothing, which is what keeps the fan-out exercised
on a machine with no MCP server.

A citation carries a `kind`. A `document` citation resolves into the hub at
`/docs/:id?chunk=n`; an `external` one links out to wherever the source said the
passage lives, or renders without a link when the source could not say. That is
why a repository passage can be cited at all — it has no document id to point at.

The MCP tool is expected to take `query` and `maxResults`, and to return either
structured content or a text block shaped
`{ "results": [ { "path", "lines", "text", "url", "score" } ] }`. Anything else
is treated as one passage of prose per text block. Whatever the shape, `text`
must be the source **verbatim**: the assistant cites what it is handed, so a
server returning summaries would have it quoting text that exists nowhere. That
cannot be detected from this side, so it is a contract the server has to meet.

A document's Preview tab shows the file as itself: Markdown rendered, source
files with a line-number gutter, PDFs in the browser's own viewer, images inline.
An **Extracted** toggle switches to the chunked text the assistant actually
retrieves, and a citation link (`?chunk=N`) opens straight to it with the passage
highlighted — the rendered view has no chunks to point at. File types with no
faithful in-browser rendering, such as Word and PowerPoint, say so and offer the
download rather than showing an approximation.

Rendered Markdown is bound through Angular's HTML sanitizer, never
`bypassSecurityTrustHtml`: documents are uploaded by contributors, so their
content is untrusted input. For the same reason `?inline=true` is honoured only
for PDFs and raster images — an uploaded SVG or HTML file displayed on our own
origin could script against a reader's session.

Access is a session cookie issued by ASP.NET Core Identity. Every endpoint
requires one unless it opts out, content changes need Editor or Admin, and
`/swagger` and `/jobs` need Admin. Google sign-in is off until configured; when
on, the allowed-domain check runs on the server against the address Google
verified, and an empty allow-list admits nobody.

Not yet built, by design: Entra ID single sign-on; pushing images to a registry,
and any automated deploy step —
CI builds the images and the IIS artefact, but a human still puts them
somewhere. OCR for scanned PDFs and client-side unit tests are also deferred. OCR for scanned documents is also deferred, so
image-only PDFs are reported as failed rather than silently indexed as empty.

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
| `GET` | `/api/documents/{id}/content` | The current file. Downloads by default; `?inline=true` serves it for display, honoured only for PDFs and raster images |
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
| `POST` | `/api/chat` | Ask a question; streams the grounded answer as server-sent events |
| `GET` | `/api/chat/sessions` | Conversation history |
| `GET` | `/api/chat/sessions/{id}` | One conversation with citations |
| `DELETE` | `/api/chat/sessions/{id}` | Delete a conversation |
| `GET` | `/api/sources` | Knowledge sources the assistant may ground answers in, and each one's state |
| `GET`/`PUT`/`DELETE` | `/api/sources/repository` | The repository source's address (Admin only). DELETE drops the override so configuration applies again |
| `POST` | `/api/sources/repository/test` | Check an address answers before saving it (Admin only) |
| `POST` | `/api/auth/login` | Sign in with an email and password; sets the session cookie |
| `POST` | `/api/auth/logout` | End the session |
| `GET` | `/api/auth/me` | The signed-in user, or 401 |
| `GET` | `/api/auth/google/start` | Begin Google sign-in (only when it is enabled) |
| `GET` | `/api/users` | Accounts (Admin only) |
| `POST` | `/api/users` | Create an account (Admin only) |

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

What is built and what is deliberately left out is in
[Current state](#current-state).

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
- `the 'llama3.2:3b' model is not installed` → run step 6

### Azurite rejects the API version

Uploads fail and the log shows:

```
Azure.RequestFailedException: The API version 2026-06-06 is not supported by Azurite.
Status: 400   ErrorCode: InvalidHeaderValue
```

The `Azure.Storage.Blobs` SDK asks for the newest storage REST API version it
knows, and Azurite implements one specific version and rejects anything newer.
The SDK ships ahead of the emulator, so this happens **even on the latest
Azurite** — as of Azurite 3.36 the newest accepted version is `2025-11-05`,
while the SDK asks for `2026-06-06`. Upgrading Azurite does not fix it.

Three ways out, in order of preference:

1. **Pin the version the client asks for.** No code or package change:

   ```
   FileStorage__ServiceVersion=2025-11-05
   ```

   Set it as an app-pool environment variable on IIS. Leave it unset against
   real Azure, which always supports the newest version.

2. **Start Azurite with `--skipApiVersionCheck`**, which is what
   `docker-compose.yml` does locally. Best when the emulator's launch arguments
   are yours to change; useless when it is installed as a service or bundled
   with Visual Studio.

3. Downgrading the `Azure.Storage.Blobs` package also works, but it is the
   worst of the three: it gives up the SDK's fixes for everyone, including
   deployments talking to real Azure, to satisfy one emulator.

To find the newest version your Azurite accepts, try values from newest down —
`2026-02-06`, `2025-11-05`, `2025-07-05` — until `dotnet run -- init-storage`
stops failing.

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

**The assistant says it has no information, but the document is right there** —
it only sees what retrieval returned. Search the same wording first: if the
search screen doesn't surface the passage either, the problem is retrieval, not
the assistant. Narrowing the question to the words the document actually uses
usually fixes it.

**Answers arrive but cite nothing** — the model ignored the citation format.
Smaller models do this more often; `llama3.1:8b` or `qwen2.5:7b` follow it
noticeably better (see [Configuration](#configuration)). The UI flags these
answers rather than passing them off as grounded.

**The assistant is very slow** — a local model on CPU-only Docker produces
roughly 5–15 tokens a second. Answers stream so you can read as they arrive.
A smaller model, or a hosted provider, is the fix if that isn't fast enough.

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
create and drop the `documenthub_test` database themselves.

**Client build fails after pulling** — dependencies changed; run
`npm --prefix client install` again.

**The client loads but shows no data, and the console logs failed `/api` calls**
— the API isn't running. Start it in a second terminal; the dev server only
proxies `/api` through, it does not host it.
