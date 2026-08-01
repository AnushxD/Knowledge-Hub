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
| `Llm:KeepAlive` | How long Ollama keeps the answer model in memory (default `30m`, `-1` for always). Ollama's own default is 5 minutes, so the first question after a quiet spell pays a full reload first |
| `Embeddings:KeepAlive` | The same for the embedding model, which every question needs before retrieval can run |
| `Chat:PassageCount` | Passages retrieved per question (default 6) |
| `Chat:HistoryTurns` | Prior turns replayed for follow-ups (default 4) |
| `KnowledgeSources:RepositoryProvider` | `mcp` (default) searches the servers added on the **Knowledge sources** screen; `none` searches none of them, whatever has been added. Which servers exist is not configuration — see below |
| `KnowledgeSources:RepositoryMaxResults` | Passages to ask each tool for (default 8) |
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

**Adding repository servers.** They are not configuration — they are rows an
administrator adds on the **Knowledge sources** screen, because which code a
team searches changes as the team's code moves, and that should not need a text
editor on the box and an app-pool recycle. Each server becomes its own knowledge
source: searched concurrently, under its own deadline, named individually on an
answer it could not contribute to, and searched from the very next question
without a restart.

Each one carries:

| Field | Meaning |
|---|---|
| Name | Lower-case letters, digits and hyphens. It goes in the API route and is recorded on every citation the server produces, so it cannot be changed afterwards |
| Display name | What appears on screen and in "… could not be searched". This is what tells two servers apart, so it is worth choosing well |
| Address | Absolute `http://` or `https://`. **Test address** connects over MCP and reports what is there — see below |
| Search tool | Empty discovers the first tool with `search` in its name — a guess worth replacing once the server's tool list is known |
| Search this source | Off takes it out of circulation without losing it, which is what an outage calls for |

`KnowledgeSources:RepositoryProvider` stays in configuration and is the
deployment's own switch over every server at once: at `none` none of them is
searched no matter how many have been added, and the rows are left untouched for
when it goes back to `mcp`. That split is deliberate — an administrator decides
*where* to look, the deployment decides *whether* to.

It defaults to `mcp`, which sounds like the unsafe default and is not: a fresh
install has no servers, so nothing is searched either way. Defaulting to `none`
as well would only mean a server added in the UI silently does nothing until
somebody edited this file — the exact round trip the screen exists to remove.
Reach for `none` when every server is down or a network is being rebuilt.

Removing a server does not rewrite history. Answers that cited it keep their
citations, because those denormalise the source's name for the same reason they
denormalise a document's title.

**Test address** does a real MCP handshake rather than an HTTP ping, because the
mistakes that matter are not "the host is down" — they are "that is the wrong
one of our two servers" and "the tool is not called what you assumed". It
reports the server's whole tool list, which repositories it says it indexes, and
which tool searching would pick, with a button to fill that in. Three outcomes,
drawn differently:

- **Connected** — the handshake worked. If nothing has `search` in its name it
  still says so, because that source would fail on every question.
- **Something answered, but not MCP** — the address and network path are right
  and it is still unusable. Usually the service's home page rather than its MCP
  endpoint.
- **Could not connect** — nothing is listening. A different problem entirely,
  which is why it is worth telling apart from the one above.

Only `search`-style tools are usable. A server exposing `get_answer` or
`get_architecture` returns its own prose, and the assistant cites what it is
handed — so grounding an answer in a summary would have it quoting text that
exists in no file. Analysis tools like `get_blast_radius` are the same: real
output, but nothing a citation can point at.

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

First-time setup for the machine the app runs on. The site itself is created
through **IIS Manager**, so there are no `appcmd` scripts here — the only command
line you need is the one that creates the database.

**What this machine needs — and what it does not.** The artefact you deploy is
already compiled: .NET assemblies plus the built Angular bundle as plain JS and
CSS. Nothing is built on the server.

| Needed | Not needed |
|---|---|
| .NET 10 **Hosting Bundle** (runtime + the IIS module) | .NET **SDK** — the schema comes from a script shipped in the artefact |
| **Docker**, managed through Portainer, for PostgreSQL | **Node.js / npm** — the Angular app arrives pre-built |
| `psql`, to run that script once | **Angular CLI** — same reason |
| Ollama, for the two local models | **Git** — you paste one file into Portainer, you do not clone the repository |

**What runs where.** Only the application goes in IIS:

| Piece | Where |
|---|---|
| API + client | **IIS**, one site — the API serves the client from `wwwroot`, so they are same-origin and the session cookie needs no CORS |
| PostgreSQL + pgvector | **Portainer stack** on this machine — pgvector has no supported Windows installer, so the `pgvector/pgvector:pg17` image is the reliable route |
| Blob storage | The **Azurite this machine already runs** — see step 2 |
| Ollama | Native Windows install, or the same stack |

#### 1. Install the prerequisites

Enable IIS through *Server Manager → Add Roles and Features → Web Server (IIS)*,
or *Turn Windows features on or off* on a desktop edition.

Then install, in this order:

1. The **.NET 10 Hosting Bundle** — not the SDK, not the plain runtime — from
   <https://dotnet.microsoft.com/download/dotnet/10.0>.
2. **Ollama for Windows** from <https://ollama.com/download/windows>.

> **Order matters.** The Hosting Bundle registers `AspNetCoreModuleV2` with IIS.
> Installed *before* IIS, that registration is missing and every request returns
> **500.19** — re-run the installer with `/repair` if that happens.

After installing, restart IIS and confirm the module is there: in **IIS Manager**,
select the server node and open **Modules**. `AspNetCoreModuleV2` should be listed.

#### 2. Deploy PostgreSQL, and add a container to Azurite

**PostgreSQL.** In Portainer: **Stacks → Add stack**, name it `documenthub`, and
paste the `postgres` service from the repository's `docker-compose.yml` into the
web editor — just that service and its `volumes:` entry. Then **Deploy the
stack**.

Two adjustments before deploying:

- **Leave out `azurite` and `ollama`.** This machine already runs Azurite, and
  Ollama is installed natively in step 1.
- **Change the Postgres password** from the compose default, and use the same
  value in step 4.

Wait for the container to report *healthy* in Portainer before continuing — the
compose file defines a health check, so Portainer shows the real state rather
than just "running".

**Azurite.** Nothing to install; it is already running. It does need somewhere to
put the files:

> **Create a blob container named `documents`** in the existing Azurite. Azure
> Storage Explorer is the easiest route — connect to the local emulator, then
> *Blob Containers → Create Blob Container*. The name has to match
> `FileStorage__ContainerName` in step 4, which defaults to `documents`.
>
> Uploads fail until this container exists, and `/healthz` reports
> `blob-storage` as degraded with the container named.

**The models.** Pull them once, a few GB in total, from PowerShell here:

```powershell
ollama pull nomic-embed-text
```
```powershell
ollama pull llama3.2:3b
```

#### 3. Create the database

The artefact ships `documenthub-schema.sql` beside the binaries. It creates every
table and index and seeds the administrator account, and it is **idempotent** —
running it again applies only what is missing, so it is also how you apply
changes on a later upgrade.

Run it once against the container from step 2:

```powershell
psql -h localhost -p 5432 -U documenthub -d documenthub -f D:\Knowledge-Hub-main\documenthub-schema.sql
```

Point `-f` at wherever you extracted the artefact. There is no `dotnet ef` step
and no SDK needed — this script *is* the migration. It enables the `vector`
extension itself, so an empty database is all it needs.

> If `psql` is not installed, open the `postgres` container in Portainer,
> **Console → Connect** (`/bin/sh`), and paste the script into
> `psql -U documenthub -d documenthub`. Workable, but the file is long; the
> command above is better.

#### 4. Create the application pool and site in IIS Manager

**Application pool.** *Application Pools → Add Application Pool*:

| Setting | Value |
|---|---|
| Name | `DocumentHub` |
| .NET CLR version | **No Managed Code** — the runtime lives in the published app, not in IIS |
| Managed pipeline mode | Integrated |

Then select the pool → **Advanced Settings**:

| Setting | Value | Why |
|---|---|---|
| Load User Profile | **True** | Data Protection keys are not persisted without it, so every recycle signs everybody out — an intermittent-looking bug that is really a setting |
| Idle Time-out (minutes) | **0** | Hangfire runs in-process; a sleeping pool stops ingesting |
| Start Mode | **AlwaysRunning** | Same reason |

**Configuration.** Settings reach the app as environment variables, where a `:`
in a config key becomes `__`. In IIS Manager, select the **server node** →
**Configuration Editor** → section
`system.applicationHost/applicationPools` → your pool → `environmentVariables`,
and add:

| Name | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Database__ConnectionString` | `Host=localhost;Port=5432;Database=documenthub;Username=documenthub;Password=<the password from step 2>` |
| `FileStorage__ConnectionString` | `UseDevelopmentStorage=true` |
| `FileStorage__ContainerName` | `documents` |
| `Embeddings__BaseUrl` | `http://localhost:11434` |
| `Llm__BaseUrl` | `http://localhost:11434` |
| `Authentication__KeyPath` | `C:\inetpub\documenthub-keys` |

`UseDevelopmentStorage=true` is the Azure SDK's shorthand for Azurite on
`127.0.0.1`. If Azurite ever moves to another host, this becomes the long-form
connection string — the shorthand cannot express a remote address.

`Authentication__KeyPath` points **outside** the site folder on purpose, so
replacing the published folder on the next deploy does not take the session keys
with it. Create `C:\inetpub\documenthub-keys` now, and in its *Properties →
Security* give `IIS AppPool\DocumentHub` **Modify**.

Optional settings worth knowing:

| Variable | Notes |
|---|---|
| `FileStorage__ServiceVersion` | Only when uploads fail with `InvalidHeaderValue` — see [Azurite rejects the API version](#azurite-rejects-the-api-version) |
| `Authentication__SessionHours` | Session lifetime, default 8 |
| `RateLimits__ChatRequests` | Questions per user per window, default 10 |
| `Llm__Model` | `llama3.1:8b` or `qwen2.5:7b` follow the citation format better than the 3B default, at the cost of speed |
| `Llm__ContextTokens` | Lower it on a constrained box — but lower `Chat__PassageCount` to match, rather than letting the prompt overflow |
| `KnowledgeSources__RepositoryProvider` | Defaults to `mcp`; set `none` to take every repository server out of circulation at once. The servers themselves are added from **Knowledge sources** in the UI, not here |

For Google sign-in, add `Authentication__Google__Enabled` (`true`),
`__ClientId`, `__ClientSecret` and `__AllowedDomains__0`. The redirect URI
registered in the Google Cloud console must be `https://<your-host>/signin-google`
exactly. An **empty** allow-list admits nobody by design.

**The site.** Get the artefact from the **Publish (IIS artefact)** workflow in
GitHub Actions and download `documenthub-iis-*` — the published API with the
built Angular app already inside `wwwroot`. Extract it to `C:\inetpub\documenthub`.

In IIS Manager: *Sites → Add Website*:

| Setting | Value |
|---|---|
| Site name | `DocumentHub` |
| Application pool | `DocumentHub` (use *Select…* — it does not default to it) |
| Physical path | `C:\inetpub\documenthub` |
| Binding | `http`, port `8080` |

Then on the site folder, *Properties → Security*, give
`IIS AppPool\DocumentHub` **Read & execute**, and **Modify** on the `logs`
subfolder.

#### 5. Verify

Browse to the site from IIS Manager, then check in order:

1. `http://localhost:8080/healthz` → `"status": "Healthy"`, with `postgres`,
   `blob-storage`, `embeddings` and `assistant-model` all healthy. Anything
   `Degraded` names the command that fixes it.
2. `http://localhost:8080/` → the sign-in screen.
3. Sign in as **`admin@documenthub.local`**. The initial password is the one set
   by `documenthub-schema.sql` — it is written in a comment at the bottom of that
   file. **Change it immediately** from the People screen; it is a known,
   committed credential and is meant to be rotated on first use.

   The People screen also lists `dev@dochub.local`, seeded by an early migration
   with no password. Nobody can sign in as it, but disable it here so the account
   list says only what it should.
4. Upload a Markdown file and watch it reach **Indexed**.
5. Ask the assistant about it — the answer should stream in word by word. All at
   once means response buffering; see below.

To open it to the network, add an inbound rule for TCP 8080 in *Windows Defender
Firewall with Advanced Security*.

For HTTPS, add an `https` binding with the org certificate. The session cookie is
`SecurePolicy = SameAsRequest`, so it works over plain HTTP inside the network
and becomes `Secure` automatically once the site is served over HTTPS — no
configuration change either way.

#### When something goes wrong

| Symptom | Cause |
|---|---|
| **500.19** on every request | Hosting Bundle installed before IIS. Re-run its installer with `/repair`, then restart IIS |
| **500.30** on startup | The app threw while starting, nearly always configuration. Set `stdoutLogEnabled="true"` in `web.config`, reproduce, read `logs\stdout_*.log`, then turn it back off |
| **Everyone signed out** after a recycle or deploy | `Authentication__KeyPath` is unset, or the pool cannot write to it. Check step 4 and the folder permission |
| Answer **arrives in one lump** instead of streaming | Response buffering. `web.config` sets `responseBufferLimit` to `0`; check it survived the deploy, and that nothing in front of IIS buffers too |
| File of 25–28 MB rejected with a bare **404.13** | IIS checked its own limit first. `web.config` sets `maxAllowedContentLength` to match the app's 25 MB |
| `/healthz` reports **blob-storage** degraded | The `documents` container does not exist in Azurite — see step 2 |
| `/healthz` reports **postgres** degraded | Check the container is healthy in Portainer, and that the password in `Database__ConnectionString` matches the one the stack was deployed with |
| Health check says the **assistant model** is missing | Ollama runs in the signed-in user's session by default. Confirm `http://localhost:11434` answers from the server itself, or set it to run as a service |
| **Ingestion stalls** when nobody uses the app | The pool went idle. Check *Idle Time-out* is `0` and *Start Mode* is `AlwaysRunning` in step 4 |

#### Upgrading later

1. Download the new artefact.
2. Run its `documenthub-schema.sql` (step 3) — **before** swapping files, not
   after. It only applies what is missing.
3. Stop the site in IIS Manager, replace the folder contents, start it again.

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
| Adding, editing and removing MCP servers from the UI | Done |
| Folder deletion, and naming a folder in a dialog | Done |
| Ingestion status that updates on screen while a document is processed | Done |
| Models kept loaded, and a per-answer latency breakdown in the log | Done |
| `Cited in answers`, counted from the citations answers actually carry | Done |
| Running on the org Windows machine under IIS | Deployed; sign-in and streaming verified there |

**v1 is complete.** Upload a Markdown, PDF or Word file and it is
extracted, chunked, embedded and searchable within seconds. Ask a question and
the assistant answers from those documents, links every claim to the exact
passage behind it, and declines when the answer isn't there.

Retrieval runs through `IKnowledgeSource`: every configured source is searched
concurrently and merged by rank, a source that fails is left out of that answer
and named in the reply rather than failing the whole question, and `/sources`
shows which are contributing. Repository sources are real MCP clients, added on
the **Knowledge sources** screen rather than in a config file: give one an
address and a tool, and its passages join document search and are cited
alongside documents from the next question onwards. Each is a source in its own
right — its own address, its own deadline, its own line on an answer it could
not contribute to — so a team whose code is split over several indexes searches
all of them at once. With none added, a single stub contributes nothing, which
is what keeps the fan-out exercised on a machine with no MCP server.

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
and any automated deploy step — CI builds the images and the IIS artefact, but a
human still puts them somewhere. Client-side unit tests are deferred, as is OCR,
so image-only PDFs are reported as failed rather than silently indexed as empty.

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
| `GET`/`POST` | `/api/sources/repositories` | The MCP repository servers, and adding one (Admin only) |
| `GET`/`PUT`/`DELETE` | `/api/sources/repositories/{name}` | One server. PUT changes everything but its name; DELETE removes it (Admin only) |
| `POST` | `/api/sources/repositories/test` | Check an address answers before adding it (Admin only) |
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

### Answers take tens of seconds

Almost always the model, not retrieval. Retrieval is well under a second; the
rest is Ollama reading the prompt and writing the answer. The log line after
every answer says which:

```
Answered with ollama/llama3.2:3b: load 3030ms, prompt 862 tokens in 5056ms (170 tok/s), generated 85 tokens in 4604ms
```

Read it in this order:

- **`load` is non-zero** — the model was evicted and reloaded before answering.
  Ollama's default is to unload after 5 minutes idle, so the first question
  after a quiet spell pays it. `Llm:KeepAlive` and `Embeddings:KeepAlive`
  default to `30m` to avoid this; raise them, or `-1` to keep models loaded.
- **`prompt … tok/s` is low (roughly 100–200)** — inference is on the CPU. This
  is the big one, and it is hardware, not configuration:

  | Where Ollama runs | GPU |
  |---|---|
  | **Docker on macOS** | **Never** — Docker cannot reach Metal, so it is always CPU |
  | Docker on Linux/Windows | Only with the NVIDIA container toolkit |
  | Native install | Yes — Metal on a Mac, CUDA on an NVIDIA box |

  On a Mac, moving Ollama out of `docker-compose.yml` and installing it natively
  is by far the largest single win available. Point `Llm:BaseUrl` and
  `Embeddings:BaseUrl` at `http://localhost:11434` either way.
- **`prompt` token count is high** — prompt reading is linear in tokens, so
  fewer or smaller passages cost proportionally less time. `Chat:PassageCount`
  (default 6) is the lever, and it is also the main *quality* lever: too few and
  the answer is missing context that exists. Measured on a CPU-only M5, dropping
  6 → 4 cut prompt reading from about 15.7s to 11.0s.
- **`generated … tokens` dominates** — the answer itself is long. `Llm:Model` is
  the lever; a smaller model is faster but follows the citation format worse.

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
