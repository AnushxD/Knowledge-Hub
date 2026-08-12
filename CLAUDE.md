# CLAUDE.md — Document Hub

Stable project knowledge: goals, stack, architecture, decisions, conventions and
workflow. This file changes rarely.

**Session state lives in `SESSION.md`** — what is done, what is in progress, what
is blocked, what is next. That file is regenerated every session; this one is
not. Do not duplicate between them: if a fact will still be true in six months,
it belongs here; if it describes the state of the work right now, it belongs
there.

---

## What this is

An internal Documentation & Knowledge Hub: **mirrors a GitLab repository's
documentation** — the same files, the same folder structure — and searches it,
with an AI assistant that answers **only** from indexed content and cites every
claim. Never fabricate — if the answer is not in the retrieved context, say so.

**The repository is the system of record.** A document exists because a file
exists in it; the folder tree is its directories; nothing is uploaded, moved,
renamed or deleted here. The only thing that changes the library is a commit,
picked up by a sync.

A real org requirement, built as a personal learning project (also learning
Claude Code, git workflow and CI/CD). Deployed on an org Windows machine under
IIS; the development machine is a Mac.

**Product goals, in priority order**

1. An answer is trustworthy or it is not given. Citations resolve to the exact
   passage, and "I don't know" is a designed outcome rather than a failure.
2. Nothing leaves the machine. Local models by default; a hosted provider is a
   registration change, never a rewrite.
3. Honest UI. A source that is off by design must not look like one that is
   broken, and an empty state must say what would fill it.

---

## Stack

| Layer | Choice | Notes |
|---|---|---|
| Frontend | Angular 22 — standalone, signals, zoneless — + Tailwind v4 | **No third-party UI kit** |
| Backend | ASP.NET Core (.NET 10) | |
| Database | PostgreSQL 17 + pgvector | One database for relational data *and* embeddings. V2 uses `documenthub_v2`; v1's is frozen |
| Source of documents | GitLab v4 REST API | One project, one branch, optionally one sub-path |
| File storage | Azure Blob Storage | Wired and health-checked, but **no document uses it** — files stream from GitLab on demand |
| Background jobs | Hangfire, Postgres-backed, in-process | Dashboard at `/jobs`, Admin only |
| AI models | Ollama — `nomic-embed-text` (768-dim), `qwen2.5:7b` | Free, key-less, local |
| Auth | ASP.NET Core Identity, session cookie | Optional Google sign-in; Entra ID later |
| API docs | Swagger UI over `Microsoft.AspNetCore.OpenApi` | `/swagger`, dev only *and* Admin only |
| CI/CD | GitHub Actions | |

**PrimeNG and PrimeIcons were evaluated and rejected** — as of v22 both moved to
the commercial PrimeUI licence, which needs a per-developer key and shows a nag
banner without one. Every control is hand-built on our own design tokens.

Design tokens live in `client/src/styles.css` (`--dh-*`) and are the single
source of truth for both themes. Add a colour, radius or shadow there, never
inline in a component.

Icons are Lucide (ISC), inlined as CSS masks by `client/tools/gen-icons.mjs`
into the generated `client/src/styles/icons.css`. To add one, extend the map in
that script and re-run `node tools/gen-icons.mjs`. **Never edit `icons.css` by
hand** — CI asserts it is current.

---

## Architecture

Follow this exactly; it matches the org's existing convention.

```
Controllers   → endpoints only, accept/return ViewModels, NO business logic
Service layer → business logic lives HERE, converts ViewModel → DTO before
                calling Data Access. RAG orchestration (search, chat,
                ingestion, activity) and repository mirroring also live here.
Data Access   → DTOs ↔ EF Core ↔ PostgreSQL
Integrations  → sibling to Data Access (not inside it) — external systems only,
                always called from Services through an interface:
                ILlmProvider, IEmbeddingProvider, IKnowledgeSource,
                IRepositoryKnowledgeSourceFactory, ISourceRepositoryClient,
                IFileStorage
```

Do not fold Integrations into Data Access. External API calls have different
failure and testing needs than DB calls, but the same "always behind an
interface" relationship to Services.

**Project reference direction is one-way and decides a lot:** Services
references Integrations and DataAccess; Integrations references nothing. So a
contract an external client must implement cannot be defined in Services — it
goes in Integrations, and Services supplies whichever implementations need
database or policy access.

That rule produced the knowledge-source layout:

- `IKnowledgeSource`, `KnowledgeQuery/Result/SearchResult`,
  `IRepositoryKnowledgeSourceFactory`, `RepositorySourceDescriptor`
  → **Integrations** (the contracts an MCP client implements or consumes)
- `NullRepositoryKnowledgeSource` and `McpRepositoryKnowledgeSource` → **Integrations** (external systems)
- `DocumentKnowledgeSource` → **Services** (wraps `ISearchService`; nothing external)
- `CompositeKnowledgeSource`, `KnowledgeSourceCatalog`, `RepositorySourceAdmin`
  → **Services** (fan-out, merge, and deciding which servers exist are policy,
  i.e. business logic). The catalog is why a server added in the UI is searched
  immediately: it reads the table per request and asks Integrations to build a
  client for each row.

`ChatService` depends on `IKnowledgeRetriever` (Services), not on any source, and
gets back `RetrievedPassage` — so citation verification is untouched by adding a
source.

The client talks to the backend only through `KnowledgeGateway` (abstract class),
bound to `HttpKnowledgeGateway`. `MockKnowledgeGateway` implements the same
contract for working on screens without a backend — a one-line provider swap in
`app.config.ts`. No component injects `HttpClient` directly. The one exception is
SSE streaming, which uses `fetch` inside the gateway because `HttpClient` buffers
the whole body.

---

## Folder structure

```
client/
  src/app/core/{data,models,state,theme,utils}
  src/app/features/{dashboard,browse,document-detail,search,chat,sources,auth,users,settings}
  src/app/layout/{shell,nav-rail,top-bar,folder-tree,ai-dock,command-palette}
  src/app/shared/{components,directives,pipes}
  src/styles.css                          # --dh-* design tokens
  Dockerfile · nginx.conf.template
server/
  src/DocHub.Api/{Controllers,Infrastructure}
    Infrastructure/Auth/                  # Identity wiring, policies, seeder, admin gates
    web.config                            # IIS: no response buffering, 25 MB limit
  src/DocHub.Services/{Documents,Folders,Ingestion,Search,Chat,Knowledge,Repository,Activity,ViewModels}
  src/DocHub.DataAccess/{Entities,Dtos,Repositories,Migrations}
  src/DocHub.Integrations/{Storage,SourceControl,Embeddings,Llm,Knowledge,HealthChecks}
  tests/{DocHub.Api.Tests,DocHub.Services.Tests,DocHub.DataAccess.Tests,DocHub.Integrations.Tests}
  Dockerfile
.github/workflows/{ci,publish}.yml
docker-compose.yml                        # postgres+pgvector, azurite, ollama
docker/initdb/                            # creates documenthub_v2 on a fresh volume
documenthub-v2-schema.sql                 # idempotent V2 setup; ships in the publish output
documenthub-schema.sql                    # V1's, frozen at release 1, never regenerated
docker-compose.app.yml                    # the built stack on that infrastructure
CLAUDE.md · SESSION.md · README.md · architecture-blueprint.md · chat-pipeline.md
```

**Key files worth knowing before changing anything nearby**

| Path | Why it matters |
|---|---|
| `chat-pipeline.md` | End-to-end walkthrough of answering one question, with flow charts. Read before changing anything in the chat path |
| `Services/Chat/GroundedPrompt.cs` | Prompt construction, citation verification, refusal detection. Pure functions |
| `Services/Chat/ChatService.cs` | RAG orchestrator: retrieve → refuse-or-generate → verify → persist |
| `Services/Search/SearchService.cs` | Hybrid search + RRF; `RankAsync` shared by search and retrieval |
| `Services/Knowledge/CompositeKnowledgeSource.cs` | Fan-out, per-source deadlines, failure isolation, rank fusion, dedupe |
| `Integrations/Knowledge/McpRepositoryKnowledgeSource.cs` | The MCP client: fan-out across a server's tools, per-tool isolation, and the three response shapes it accepts. The tool contract it expects is documented on the class |
| `Integrations/Knowledge/RepositoryToolPlan.cs` | Which of a server's tools a question is put to, and what disqualifies one. Shared by the searcher and the probe |
| `Services/Repository/RepositoryMirrorService.cs` | The sync: list tree → diff on blob ids → add/repoint/remove → reconcile folders. Read the ordering comment before touching it |
| `Integrations/SourceControl/GitLabRepositoryClient.cs` | The GitLab v4 client: paginated tree, raw file streaming, head commit |
| `Services/Ingestion/IngestionService.cs` | fetch → extract → chunk → embed → index; permanent vs transient failure split |
| `DataAccess/DocHubDbContext.cs` | Schema; `EmbeddingDimensions=768`, tsvector, HNSW, jsonb citations |
| `DataAccess/Repositories/ChunkRepository.cs` | Both search branches |
| `Api/Program.cs` | DI composition, auth pipeline, static files, Hangfire, health checks |
| `Api/Infrastructure/Auth/AuthenticationRegistration.cs` | Identity, cookie, policies, Google, claims factory, key persistence |
| `client/src/app/core/state/auth-store.ts` | Signed-in principal; drives presentation only |
| `server/tests/DocHub.Services.Tests/StackFixture.cs` | Real stack, hashing embeddings, scripted LLM |

---

## Configuration

- `appsettings.json` — shape and defaults only, safe to commit, no real values
- `appsettings.Development.json` — local container connection strings, safe to
  commit, **excluded from publish output and the Docker build context** because
  it carries the local seed-admin password
- Real secrets → `dotnet user-secrets` locally, environment variables or Key
  Vault in prod. Never in any committed appsettings file
- Bind via strongly-typed Options classes, one per integration, with
  `.Validate(...).ValidateOnStart()`

Settings that are load-bearing and easy to get wrong:

| Key | Why it matters |
|---|---|
| `Llm:ContextTokens` | Ollama defaults `num_ctx` to 2048 and **discards the overflow silently**. The grounded prompt is 5,000+ tokens, so too low means the model cites passages it never saw |
| `Authentication:KeyPath` | Data Protection keys. Unset on IIS, every app-pool recycle signs everyone out |
| `Knowledge:SourceTimeoutSeconds` | Per-source deadline; without one a hung source stalls every question |
| `Authentication:Google:AllowedDomains` | The access gate. **Empty admits nobody**, never everybody |
| `Embeddings:Dimensions` | Must match the migrated column. Changing it needs a migration *and* a full re-index |
| `GitLab:BaseUrl` / `ProjectPath` | Required. Every document comes from here, so an unset one is an empty product, not a degraded one — it fails at boot |
| `GitLab:WebhookSecret` | The webhook endpoint is anonymous of necessity. **Empty refuses every delivery**, never accepts them all |
| `GitLab:SubPath` | Narrows the mirror. A repository root is mostly code, and mirroring it fills the tree with files no extractor can read |

---

## Design decisions

Recorded so they are not re-litigated. Each is a trade already reasoned through.

**Storage and search**
- pgvector in the same Postgres as relational data — one connection string, one
  backup, embedding and metadata written in one transaction.
- Embedding dimension fixed at 768 (`DocHubDbContext.EmbeddingDimensions`),
  validated at startup rather than failing per row.
- The two search branches stay separate in the repository
  (`SearchKeywordAsync`, `SearchVectorAsync`); fusion is a Service-layer
  decision, not SQL.
- Reciprocal rank fusion, k=60. `ts_rank` and cosine similarity are on unrelated
  scales, so only rank position is honestly comparable.
- Search and the assistant share `SearchService.RankAsync`, so the assistant can
  only cite what the search screen would have shown.
- The branches run **sequentially**, not concurrently — they share a
  request-scoped `DbContext`. The embedding HTTP call starts first so the only
  real latency overlaps. This fixed a real intermittent bug and is
  regression-tested.

**The mirror**
- **The repository is the system of record, and the hub is read-only.** Upload,
  move, delete and every folder mutation are gone. A control that edited the
  tree here would either be a lie the next sync undoes, or a write into
  somebody's repository; both are worse than not offering it.
- A document's identity is its **repository path**, and the database enforces
  one row per path. Hub-local metadata — title, description, tags, starring —
  therefore survives the file's contents changing but not a rename, which reads
  as a delete and an add. That is the honest consequence of identifying a file
  by where it lives.
- Sync diffs on the **git blob id**, never on the commit. Git is
  content-addressed, so an unchanged file has an unchanged id and a push
  touching one file cannot re-embed six hundred. A commit says nothing about
  whether any particular file changed.
- **A type no extractor can read is counted as skipped, not mirrored.** Under
  uploads a person chose the file and deserved to be told why it was not
  searchable; a repository chose nothing and is mostly source code, so a row
  per `.cs` file that can only ever say "failed" is noise that never clears.
- **A failed sync leaves the mirror exactly as it was** and does not advance
  the recorded commit. It has no way to tell a file that was deleted from one
  it never got to, so emptying the library because GitLab blipped is far worse
  than serving yesterday's tree — and claiming a commit it never finished
  reading would make the next sync skip the remainder.
- **Departures are settled before the folder tree is reconciled.** Reconciling
  first removes the directories a departed file lived in and the cascade takes
  the document with them, leaving nothing to count and nothing to name in the
  trail: the library empties itself in silence. Found by a test.
- One sync at a time, on a **process-wide semaphore**, and refused rather than
  queued when one is running — two syncs reach the same answer twice, and race
  on the unique path. Two API instances would need a database lock instead.
- Syncs run on **their own Hangfire queue, drained ahead of ingestion**. The
  first sync of a real repository queues hundreds of ingestion jobs; on a shared
  queue the next sync lands behind all of them and the screen shows the previous
  run's numbers as if nothing had been asked for. Measured at 636 documents.
- File bytes are **fetched from GitLab on demand**, never copied. What a reader
  downloads is what the branch holds now, not what the last sync happened to
  see. The cost is honest and stated: GitLab down means preview fails, while
  search still works.
- Listing and checking are the same call here, unlike knowledge sources:
  `/api/repository` is one row plus configuration and contacts nothing, so it
  is safe to poll while a sync runs.
- The webhook endpoint is **anonymous of necessity** — GitLab holds no session —
  so the shared secret is the only thing separating GitLab from anyone else who
  can reach the box. Empty refuses every delivery, for the same reason an empty
  Google allow-list admits nobody. A push to another branch is acknowledged and
  ignored: GitLab disables a hook that keeps erroring, and that is not an error.

**Ingestion**
- Text extraction lives in Services, not Integrations: PdfPig and OpenXML are
  in-process libraries, not external systems.
- Chunks never span sections, so a citation's "Page 4" is unambiguous.
- The chunk minimum is per document, not per section — it drops bare-heading
  fragments but keeps a genuinely one-line document.

**The assistant**
- Citations are stored as `jsonb` on the message, denormalising title and
  heading, so a historical answer stays truthful after renames and deletes.
- A citation carries a `kind`: a `document` one resolves to `/docs/:id?chunk=n`,
  an `external` one to a URL, or to no link at all when the source gave none.
  That is what lets a repository passage, which has no document id, be cited.
- `IsRefusal` is stored explicitly, not inferred from empty citations — a
  refusal and a failure to cite render differently.
- Sources that could not be searched are stored on the message too, for the same
  reason as citations: an answer grounded in less than usual has to still say so
  when the conversation is reopened.
- **Sources that were searched and matched nothing are stored and named as
  well**, separately from the failures. A source that contributes no passage is
  otherwise invisible on the answer, and that silence reads as "never searched" —
  it was read that way in practice. The two stay apart because they are opposite
  claims: one source worked and had no answer, the other could not be asked. Only
  the second is a fault, and rendering them alike would report a healthy source
  as broken.
- SSE for chat, with the first event pulled *before* headers are written so
  validation and 404s still return problem details.
- Ollama for both embeddings and generation; a hashing embedding provider and a
  scripted LLM provider make tests hermetic.

**Knowledge sources**
- Sources merge by **rank, never by score** — each scores in its own units.
- Sources fan out concurrently, each under its own deadline. Failure isolation
  covers a source that throws; the deadline covers one that never replies.
- A failing source degrades one answer and is named *on that answer* — carried
  on `ChatEvent.Completed` and persisted, not logged and forgotten. Only if every
  source fails does the refusal path take over.
- At most one source may touch the request-scoped `DbContext` — the document
  source. A second database-backed source must run sequentially with it.
- `KnowledgeSourceState` has three values, not a boolean: `inactive` (off by
  design) must not render like `unavailable` (should work, doesn't).
- **Listing sources and checking them are separate calls.** A state costs an
  MCP handshake per remote server — seconds — while the list is a table read.
  `/api/sources` draws the screen, `/api/sources/status` fills the badges in,
  and until one arrives a row says "Checking…" rather than borrowing one of the
  three real states. Statuses are deliberately not cached: the screen exists to
  say whether a source works *now*.
- **Repository servers are data, not configuration.** `repository_source_settings`
  *is* the list of them, added and removed in the UI, one knowledge source each.
  Which code a team searches changes as the team's code moves; that is
  operational, and should not need a text editor on the box and a recycle.
- Sources are therefore resolved **per request** by `IKnowledgeSourceCatalog`,
  not injected as a fixed `IEnumerable<IKnowledgeSource>` — a server added in
  the UI is searched by the very next question. Integrations exposes
  `IRepositoryKnowledgeSourceFactory`; the container cannot know the set.
- `KnowledgeSources:RepositoryProvider` stays in configuration and is the
  deployment's own switch: `none` searches no server however many exist, and
  leaves the rows alone. An administrator decides *where* to look, the
  deployment decides *whether* to. It defaults to `mcp`, because the servers are
  already off by default — an empty table — and defaulting the switch off too
  would mean a server added in the UI does nothing until a file is edited.
- A server's `Name` is immutable: it keys the route and is recorded on every
  citation it produces, so renaming would orphan attribution on answers already
  given. Deleting one leaves those citations intact, for the same reason a
  deleted document's citations survive.
- Switching a server off is not deleting it — an outage should not cost an
  address and its settings.
- Pointing the server at an arbitrary host is **admin-gated SSRF by design**.
  Only absolute http/https is accepted, so the box cannot become a file reader.
- **Every tool a question can be put to is put to it**, concurrently, over one
  connection — not just the one whose name says "search". A server's tools are
  windows on the same repository, and the one that sorts first is not reliably
  the one that knows the answer: a thin `search_code` beside a
  `get_architecture` that answers exactly what was asked used to produce a
  refusal. Reversed deliberately, and the cost is real and stated on
  `McpRepositoryKnowledgeSource`: a search tool returns text out of a file, so a
  citation to it resolves to something a reader can check, while a citation to
  synthesised prose resolves only to "this server said this". Both are still
  verbatim and still verified, which is what makes the trade acceptable —
  search-shaped tools are asked first and win ties, and pinning a source to one
  tool is still there for a server where the distinction has to be absolute.
- **A tool that changes something is never called, and neither is one with
  nowhere to put the question.** Asking every tool means calling tools nobody
  vetted with the user's words in them, and `delete_branch(query: "how do we
  restart the worker")` is not a search that finds nothing — it is a write into
  somebody's repository, which is the one thing the hub must never do. The
  server's `readOnlyHint` decides when it declares one; a blunt name check
  stands behind it, because most servers declare nothing. Biased towards
  excluding: a read tool wrongly skipped costs some passages and can still be
  named explicitly, and a write tool wrongly called cannot be undone. A tool
  with no string parameter is left out for a different reason — it answers the
  same thing however it is asked, which is noise on every answer.
- `RepositoryToolPlan` holds that one rule, shared by the searcher and the
  probe, so the probe cannot promise a tool the searcher would not use.
- **One tool failing degrades the answer; all of them failing fails the
  source.** The same split the composite applies across sources, applied within
  one — the tools that answered are used, and the ones that did not are named on
  the reply rather than logged. Tools merge by rank, never by score, for the
  same reason sources do.
- The address probe speaks MCP and reports the tool list, because the mistakes
  worth catching are "wrong server" and "the tools here cannot be searched", and
  an HTTP ping sees neither. It reports which tools would actually be asked, not
  just which exist. It falls back to a plain request only to tell "answered but
  not MCP" apart from "nothing listening" — different problems, different fixes.

**Identity**
- The `users` table **is** the Identity store (`User : IdentityUser<Guid>`), so
  every `owner_id` foreign key survived the change. Never add a parallel table.
- Role is a column, not Identity's role tables — one role per person, projected
  into a claim. Entra ID maps a directory group onto the same value later.
- Identity is used directly from the Api layer, not wrapped in a Service: a
  Service over `SignInManager` would only forward. The boundary that matters is
  `ICurrentUser`, which stays framework-free.
- Accounts are disabled, never deleted — they own documents and conversations.
- Changing your own password requires the current one, even though the caller is
  already authenticated. That is what keeps a stolen cookie from becoming a
  permanent takeover — and it is rate limited, because otherwise the rule is
  only as strong as how fast the password can be guessed. Identity's lockout
  does not apply to a signed-in caller. The endpoint changes whoever is asking:
  no id crosses the wire.
- The password hash never appears in a migration; `seed-admin` sets it.

**Activity**
- The trail is append-only, and the target name is denormalised — a file
  leaving the repository must not blank out the record of it having left.
- **The actor is nullable, and usually null.** Most of the feed is now the
  repository changing under a webhook with nobody signed in; attributing that
  to the seeded administrator would put a name against work that account did
  not do, and inventing a system user would put a row in the user table that
  cannot sign in. An absent actor renders as the repository.
- A sync writes **one entry for the whole run**, not one per file. A push
  touching two hundred files would otherwise be the entire feed.
- Recording never fails the operation it describes.
- Starring is not recorded: a bookmark is not an edit.

**Deployment**
- **V2 has its own database and its own migration chain.** V1's is left exactly
  as release 1 left it. The two share no history, so neither script may be run
  against the other's database — `documenthub-schema.sql` is frozen and
  `documenthub-v2-schema.sql` is the one that ships.
- **One binary, two shapes.** Containers: nginx serves the client and proxies
  `/api`. IIS: the API serves the client from `wwwroot`, one site. Chosen at
  startup by looking for `wwwroot/index.html` — nothing branches on an
  environment name.
- IIS is a single site rather than two plus ARR: another component to install,
  and a second origin means a cross-origin session cookie for no benefit.
- Hangfire enqueues via `IIngestionQueue`, defined in Services and implemented
  in Api, keeping the job runner a composition detail.
- Images are built, not pushed — the registry is a deployment decision needing
  credentials the repo does not have.

---

## Grounding rules (non-negotiable)

- Only `Indexed` documents are retrievable. A half-processed or failed document
  must never be searchable or citable.
- **A file that leaves the repository takes its chunks with it.** Otherwise
  content the team deleted stays answerable, which is the worst kind of stale:
  confidently cited and no longer true.
- **A revision that changes drops the document back to Pending.** The stored
  chunks describe text the repository no longer has, and leaving them
  searchable would let the assistant quote a passage that has been edited away.
- The LLM is never called with zero retrieved passages — that is exactly what
  produces confident fabrication. Refuse instead.
- Every citation marker the model emits is verified against the passages
  actually supplied; unresolvable markers are stripped, never rendered as links.
- **A source saying "nothing matched" contributes nothing.** A recognised
  results envelope is honoured even when empty — `results`, `result`, `matches`,
  `hits` or `items`, and the object is lifted out of any sentence wrapping it.
  Treating a negative answer as a passage is what let an unrelated server ground
  an answer. A *non-empty* envelope whose entries carry no `text` is the other
  case and passes through as prose: dropping it would discard real results.
- **Retrieval for the assistant has a relevance floor** (`Knowledge:MaxPassageDistance`,
  cosine, default 0.5). A vector index always has a nearest neighbour, so
  without one every question retrieves something and the model is handed
  whatever was least unlike it. The search screen has no floor: a person can
  see a weak result for what it is, and the assistant cannot.
- **A citation is checked against the sentence citing it**, not just against the
  list of supplied passages. Sharing at least two substantial words, and not
  only words the question already supplied — a source that echoes the query
  back would otherwise look like agreement with anything. Deliberately weak: it
  catches decoration, not subtle mismatch.
- **An answer that cites nothing verifiable is refused, not shown.** Non-empty
  retrieval is a weaker guarantee than it looks — a source answering "no
  matches" in prose still returns a passage, and one irrelevant passage is
  enough for a model to answer from its own training instead. Zero resolvable
  citations is the only evidence left that this happened, so it is acted on.
  It costs the occasional real answer whose markers the model forgot; that is
  the trade the first product goal already chose.
- `ChatEvent.Completed` carries the persisted text, because verification
  happens after the tokens have streamed. What is on screen must be replaced by
  what was stored, or a refused answer stays visible until reload.
- "I don't know" is a designed outcome, persisted with `IsRefusal`, rendered as
  information rather than as an error.
- Citations denormalise document title and heading onto the message, so renaming
  or deleting a document cannot rewrite a historical answer.
- Search and the assistant share one ranking implementation.
- A knowledge source that fails is left out of that one answer and named in the
  reply — never silently dropped, never allowed to fail the whole question.
- A knowledge source that was searched and matched nothing is named on the reply
  too, as information rather than as a fault. "Searched and found nothing" and
  "never searched" must not look the same to the reader.
- Every source answers under its own deadline, linked to the caller's token, and
  a timeout is reported like any other degradation.
- Any source must return **verbatim passages, not summaries**, or the assistant
  would be citing text it was never given.

## Security rules (non-negotiable)

- Authorisation defaults to closed: a fallback policy requires a session, so a
  new endpoint is protected before anyone remembers to think about it. Opting
  out is an explicit `[AllowAnonymous]`, and only health checks and the sign-in
  endpoints have one.
- Endpoint attributes handle "may this role call this at all". A rule about a
  *particular row* is business logic and belongs in a Service — which is why
  `ICurrentUser` exposes `Role`.
- Google sign-in decides access on the server, from the email Google verified —
  never from the `hd` request hint, which the browser controls. An unverified
  address is refused, and an empty allow-list admits nobody.
- Client-side guards and hidden buttons are courtesy. The API enforces access,
  and every rule must hold with the client bypassed entirely.
- Data Protection keys must outlive the process wherever sessions are expected
  to (`Authentication:KeyPath`).
- Response buffering must be off in front of the assistant — `proxy_buffering
  off` in nginx, `responseBufferLimit="0"` in `web.config`. Neither fails
  loudly: the answer simply stops streaming.

## Provisioning is explicit

The API never creates databases, containers or schema at startup. Setup is
operator-run: `dotnet ef database update`, `dotnet run -- init-storage`,
`dotnet run -- seed-admin`, `ollama pull`. The one exception is Hangfire's own
`hangfire` schema, which the library versions with itself. The deployment
pipeline never migrates a database as a side effect of deploying.

---

## Coding conventions

- **Comments explain *why*, never *what*.** Every non-obvious decision carries a
  short rationale. Match the surrounding density.
- XML doc comments on public interfaces and records; `<param>` for non-obvious
  parameters — never inline in a positional record's parameter list, it will not
  compile.
- Repositories and implementations are `internal`; only interfaces escape a
  layer. `InternalsVisibleTo` for test projects.
- One `IOptions<T>` per integration, validated on start.
- Enums persisted as **text** (`HasConversion<string>`) so dumps stay readable
  and reordering cannot remap rows.
- `Guid.CreateVersion7()` for new ids.
- Tests run against **real** Postgres and Azurite — no in-memory providers. Only
  the AI models are faked.
- Test names are full sentences:
  `A_fabricated_citation_is_dropped_from_the_answer`.
- Angular: `ChangeDetectionStrategy.OnPush`, signals, `@if`/`@for`, `protected`
  members for template use. Colours and radii only via `--dh-*` tokens.
- Templates: no whitespace around interpolation where it would render mid-word.

**Traps that have already cost time**

- `WebSearchToTsQuery` must appear **inline inside the LINQ expression**;
  hoisting it into a local throws at runtime.
- EF test fixtures must call `.UseNpgsql(conn, o => o.UseVector())`, or model
  validation fails.
- Both test projects read `DOCHUB_TEST_DB` but default to *different* databases
  and each recreates its own — never set it globally.
- Port 5080 may already hold a user-run API. Verify on a spare port (5099)
  rather than killing that process. If the client proxy is repointed for
  verification, revert it before committing.

---

## Development workflow

```bash
docker compose up -d --wait                  # postgres, azurite, ollama
docker compose exec ollama ollama pull nomic-embed-text
docker compose exec ollama ollama pull qwen2.5:7b

# V2 has its own database. On a volume that predates it, create it once:
docker compose exec postgres createdb -U documenthub documenthub_v2

dotnet ef database update --project server/src/DocHub.DataAccess --startup-project server/src/DocHub.Api
dotnet run --project server/src/DocHub.Api -- init-storage
dotnet run --project server/src/DocHub.Api -- seed-admin
dotnet run --project server/src/DocHub.Api   # Kestrel, not IIS, for daily dev

npm --prefix client install
npm --prefix client start

dotnet build server/DocHub.slnx
dotnet test server/DocHub.slnx
npx ng build --configuration development     # from client/
npx tsc --noEmit -p tsconfig.app.json        # from client/
dotnet ef migrations add <Name> --project server/src/DocHub.DataAccess --startup-project server/src/DocHub.Api
```

Ports: client 4200 · API 5080 (`/swagger`, `/jobs`, `/healthz`) · Postgres 5432
· Azurite 10000 · Ollama 11434.

The library is empty until the repository is mirrored — `POST /api/repository/sync`,
or **Sync now** on the library screen as an admin.

- Trunk-based: commit to `main` directly, or one short-lived `feature/*` branch.
- Commit small, one thing at a time. **Verify (build + tests) before pushing**,
  and push each finished chunk without asking.
- Commit messages: short subject plus a few terse bullets. No rationale essays.
- README is the setup guide — update it in the same commit as any change to
  setup, ports, configuration or status.
- CI runs on every push and pull request to `main`: server tests against the
  repo's own compose infrastructure, client typecheck and build, both images.

---

## Never

- Never put a real secret in any committed `appsettings.*.json`
- Never let a Controller contain business logic, or a Service talk to EF Core,
  Blob, GitLab or LLM clients except through Data Access / Integrations
  interfaces
- Never write to the mirrored repository. The token needs `read_repository` and
  nothing more, so a bug here cannot alter the team's documentation
- Never run `documenthub-schema.sql` and `documenthub-v2-schema.sql` against
  the same database, or point `dotnet ef database update` at the v1 one
- Never let the assistant answer from anything but retrieved passages
- Never trust a client-supplied domain, `hd` hint or `returnUrl` — verify
  domains against the address the provider verified, and restrict redirects to
  local paths
- Never add an authentication bypass for convenience, dev-only or otherwise
- Never create databases, containers or schema at startup
- Never add Co-Authored-By or "Generated with Claude Code" trailers to commits
  or PRs
