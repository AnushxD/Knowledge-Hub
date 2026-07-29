# Project Overview

Internal **Documentation & Knowledge Hub** (DocHub): upload/organise/search team docs, plus an AI assistant that answers strictly from indexed content and cites every claim. Personal learning project built to a real org requirement.

**Stack:** Angular 22 (standalone, signals, zoneless) + Tailwind v4 · ASP.NET Core (.NET 10) · PostgreSQL 17 + pgvector · Azurite (blob) · Hangfire · Ollama (local models) · Docker Compose.

**Architecture (strict, org convention):** `Controllers → Services → Data Access (EF Core) ` with `Integrations` a *sibling* of Data Access for external systems, always behind an interface.

**Modules:** `DocHub.Api`, `DocHub.Services`, `DocHub.DataAccess`, `DocHub.Integrations`, `client/` (Angular).

**Branch:** `main`, synced with `origin/main`. Trunk-based; commit + push each finished chunk.

> Stable architecture, conventions and grounding rules live in `CLAUDE.md`. This file is session state only.

---

# Current Objective

**v1 is complete and pushed.** Phases 1–6 are done, and the activity trail closed the last
gap that was visible in the UI.

Phase 7 — the real MCP client — is the only roadmap item left, and it is blocked on having
an MCP server to point at rather than on anything in this repository. Everything it needs
is in place: `IKnowledgeSource` in Integrations, a composite that fans out under per-source
deadlines and isolates failures, and an inactive stub whose address an admin can already set
from `/sources`.

---

# Current Progress

## Completed
- **Phase 1** — folders, upload, metadata, versioning, preview, blob storage.
- **Phase 2** — ingestion pipeline (extract → chunk → embed → index), hybrid search, search screen.
- **Phase 3** — RAG assistant: streaming grounded answers, verified citations, refusals, session history.
- **Phase 4** — `IKnowledgeSource` abstraction, composite retrieval with per-source failure
  isolation, null repository stub, `GET /api/sources`, real `/sources` screen.
- **Phase 5** — Identity cookie auth, Admin/Editor/Viewer roles, admin account management,
  optional Google sign-in with server-side domain allow-listing, chat rate limiting,
  `/jobs` and `/swagger` behind the Admin role, client sign-in flow.
- **Phase 6** — API and client container images, `docker-compose.app.yml` for the built
  stack, GitHub Actions CI (server + client + images) and a publish workflow producing a
  single-site IIS artefact.
- **Post-phase-6 tidying** — the repository source address is UI-editable; roadmap phase
  language removed from every screen; the assistant dock and the document page's "Ask about
  this doc" now reach the real assistant instead of being disabled placeholders; Upload and
  Notifications removed from the top bar; the IIS runbook targets Portainer with Docker on
  the same machine.
- **Activity trail** — recorded on upload, edit, move, delete, folder changes and ingestion
  outcomes; `GET /api/activity` behind the dashboard feed that had always read
  "no activity recorded".
- Swagger UI at `/swagger` (dev only, Admin only).
- 143 tests green: 8 Integrations, 17 Api, 14 DataAccess, 104 Services.

## In Progress
- Nothing. Working tree clean, all work pushed.

## Remaining (roadmap)
7. Real MCP integration + revisit vector-store scale — **next**
Also outstanding: Entra ID single sign-on (the Identity seam is in place); pushing images to
a registry and an actual automated deploy step (CI builds, a human still deploys).

---

# Architecture Decisions

- **pgvector in the same Postgres** as relational data — one connection string, one backup, embeddings and metadata written in one transaction.
- **Embedding dimension fixed at 768** (`DocHubDbContext.EmbeddingDimensions`) matching `nomic-embed-text`. Changing model ⇒ new migration + full re-index; validated at startup rather than failing per-row.
- **Two search branches kept separate in the repository** (`SearchKeywordAsync`, `SearchVectorAsync`); fusion is a *Service-layer* decision, not SQL.
- **Reciprocal rank fusion (k=60)** — `ts_rank` and cosine similarity are on unrelated scales, so only rank position is honestly comparable.
- **Search and assistant share `SearchService.RankAsync`** — the assistant can only cite what the search screen would show. `SearchAsync` returns snippets; `RetrieveAsync` returns full chunk text.
- **Branches run sequentially, not concurrently** — they share a request-scoped `DbContext`. The embedding HTTP call is started first so the only real latency overlaps. (Fixed a real intermittent bug; regression-tested.)
- **Text extraction lives in Services, not Integrations** — CLAUDE.md scopes Integrations to *external systems*; PdfPig/OpenXML are in-process libraries.
- **Chunks never span sections** — makes a citation's "Page 4"/heading unambiguous.
- **Chunk minimum applied per document, not per section** — drops bare-heading fragments but keeps a genuinely one-line document.
- **Ollama chosen for both embeddings and generation** — free, key-less, data stays local. Both behind `IEmbeddingProvider` / `ILlmProvider`.
- **Hashing embedding provider** for hermetic tests; **scripted LLM provider** for assistant tests.
- **Citations stored as `jsonb`** on the message, denormalising title+heading — a historical answer stays truthful after renames/deletes.
- **`IsRefusal` stored explicitly**, not inferred from empty citations — refusal and "failed to cite" render differently.
- **SSE for chat**, with the first event pulled *before* headers are written so validation/404 still return problem details.
- **`fetch` (not HttpClient) for SSE in the client gateway** — HttpClient buffers the whole body.
- **Hangfire enqueues via `IIngestionQueue`** defined in Services, implemented in Api — keeps the job runner a composition detail.
- **Swagger UI is the UI package only**; document generation stays with `Microsoft.AspNetCore.OpenApi` to avoid two competing generators.
- **`IKnowledgeSource` lives in Integrations, its implementations wherever their work belongs** — Services references Integrations and never the reverse, so a contract an MCP client must implement cannot be defined in Services. `DocumentKnowledgeSource` and `CompositeKnowledgeSource` are therefore Services types implementing an Integrations interface.
- **`ChatService` depends on `IKnowledgeRetriever` (Services), not on `IKnowledgeSource`** — the composite returns `RetrievedPassage`, so `GroundedPrompt` and citation verification were untouched by phase 4.
- **`KnowledgeResult` was introduced rather than reusing `RetrievedPassage`** — forced, since Integrations cannot see a Services type. It maps to `RetrievedPassage` at the composite boundary.
- **Sources fan out concurrently; at most one may touch the DbContext.** The document source is that one. A second DB-backed source must run sequentially with it, exactly as the keyword and vector branches do.
- **Sources merge by rank (RRF, k=60), never by score** — each source scores in its own units.
- **A failing source degrades one answer and is named in the reply**; only if every source fails does the normal refusal path take over.
- **The repository address is editable in the UI, with configuration as the baseline.** An override row wins if present; otherwise `KnowledgeSources:*` applies. Adding a server is an admin action, not an app-pool variable and a recycle — but a deployment that must always have a source can still declare one. Clearing the override differs from saving an empty address: the first restores configuration, the second switches the source off.
- **`IRepositorySourceSettings` follows the phase 4 pattern** — contract in Integrations (where the MCP client that consumes it will live), implementation in Services (the only layer that can see both the stored row and the options).
- **Pointing the server at an arbitrary host is admin-gated SSRF by design.** Only absolute http/https is accepted, so the box cannot be turned into a file reader; reaching internal hosts is the deliberate, role-gated capability.
- **The "test address" probe confirms the network path only** — it cannot establish that the thing answering speaks MCP, and says so in its own wording.
- **The null repository source is registered locally on purpose** — a fan-out exercised against one source until phase 7 is a fan-out first debugged in phase 7.
- **`KnowledgeSourceState` has three values, not a boolean** — `inactive` (off by design) must not render like `unavailable` (should work, doesn't).
- **The `users` table *is* the Identity store** (`User : IdentityUser<Guid>`) — one row per person, so every `owner_id` FK survived the change. A parallel identity table would have let credentials and owners drift apart.
- **Role is a column, not Identity's role tables** — one role per person, projected into a claim by `DocHubClaimsPrincipalFactory`. Entra ID maps a directory group onto the same value later.
- **Authorisation defaults to closed** via `FallbackPolicy`; only health checks and the sign-in endpoints opt out. A new endpoint is protected before anyone thinks about it.
- **Identity used directly from the Api layer, not wrapped in a Service** — a Service over `SignInManager` would only forward. The boundary that matters is `ICurrentUser`, which stays framework-free.
- **`ICurrentUser` gained `Role`/`IsAuthenticated`** — endpoint attributes cover "may this role call this"; a rule about a *particular row* is business logic and needs the role in Services.
- **`/jobs` and `/swagger` gated on the Admin role, not the environment** — dev-only registration kept them off production but left every developer machine serving an open jobs dashboard.
- **Password hash never in a migration** — it is salted per call, so it could not be a constant, and a constant credential would be one in source control. `dotnet run -- seed-admin` sets it.
- **Google decides access on the verified email, server-side** — the `hd` hint is a request parameter the browser controls. Unverified addresses are refused; an empty allow-list admits nobody.
- **Accounts are disabled, never deleted** — they own documents, folders and conversations.
- **Security stamp revalidated every 5 minutes** — otherwise a revoked role lingered until the cookie expired, up to `SessionHours`.
- **Chat rate limit partitions by user id, not IP** — an office NAT is one address, and a shared limit punishes the wrong people.
- **One binary serves two deployment shapes.** Containers: nginx serves the client and proxies `/api`. IIS: the API serves the client from `wwwroot`, one site. Chosen at startup by looking for `wwwroot/index.html`, so nothing branches on an environment name.
- **IIS is a single site rather than two plus ARR** — Application Request Routing is another component to install and keep configured on the org box, and a second origin means a cross-origin session cookie for no benefit.
- **The SPA fallback is `AllowAnonymous`** — it is the login screen, and the fallback policy would otherwise 401 the page whose purpose is to obtain a session.
- **Response buffering must be off in front of the assistant** — `proxy_buffering off` (nginx) and `responseBufferLimit="0"` (web.config). Neither fails loudly; SSE just stops streaming.
- **CI reuses the repo's own compose file** for Postgres and Azurite, so test infrastructure cannot drift from local dev. Azurite needs `--skipApiVersionCheck`, which a GitHub service container cannot express — it has no way to override the image command.
- **`DOCHUB_TEST_DB` is never set in CI** — both suites read that one variable, default to different databases, and each drops and recreates its own.
- **`appsettings.Development.json` excluded from publish output and the Docker context** — it carries the local seed-admin password, and no artefact should contain a credential. The publish workflow asserts it.
- **Images are built, not pushed** — the registry is a deployment decision needing credentials the repo does not have.

---

# Repository Structure

```
client/
  src/app/core/{data,models,state,theme,utils}
  src/app/features/{dashboard,browse,document-detail,search,chat,sources,auth,users,settings,roadmap}
  src/app/layout/{shell,nav-rail,top-bar,folder-tree,ai-dock,command-palette}
  src/app/shared/{components,directives,pipes}
  src/styles.css                     # --dh-* design tokens (single source of truth)
server/
  src/DocHub.Api/{Controllers,Infrastructure}
  src/DocHub.Services/{Documents,Folders,Ingestion,Search,Chat,Knowledge,ViewModels}
  src/DocHub.DataAccess/{Entities,Dtos,Repositories,Migrations}
  src/DocHub.Integrations/{Storage,Embeddings,Llm,Knowledge,HealthChecks}
  src/DocHub.Api/Infrastructure/Auth/    # Identity wiring, policies, seeder, admin gates
  src/DocHub.Api/web.config              # IIS: no response buffering, 25 MB limit
  server/Dockerfile · client/Dockerfile · client/nginx.conf.template
.github/workflows/{ci,publish}.yml
docker-compose.app.yml                 # the built stack on the dev infrastructure
  tests/{DocHub.Api.Tests,DocHub.Services.Tests,DocHub.DataAccess.Tests,DocHub.Integrations.Tests}
docker-compose.yml                   # postgres+pgvector, azurite, ollama
CLAUDE.md · SESSION.md · README.md · architecture-blueprint.md
```

---

# Important Files

| Path | Purpose | Status |
|---|---|---|
| `server/src/DocHub.Services/Chat/GroundedPrompt.cs` | Prompt construction + citation verification/stripping/refusal detection. Pure functions. | Done, mutation-tested |
| `server/src/DocHub.Services/Chat/ChatService.cs` | RAG orchestrator: retrieve → refuse-or-generate → verify → persist | Done |
| `server/src/DocHub.Services/Knowledge/CompositeKnowledgeSource.cs` | Fan-out, per-source failure isolation, rank fusion, dedupe; `DescribeSourcesAsync` | Done |
| `server/src/DocHub.Services/Knowledge/DocumentKnowledgeSource.cs` | Documents as one source; wraps `ISearchService`, adds no ranking | Done |
| `server/src/DocHub.Integrations/Knowledge/IKnowledgeSource.cs` | The contract a real MCP client implements in phase 7 | Done |
| `server/src/DocHub.Services/Search/SearchService.cs` | Hybrid search + RRF; `RankAsync` shared by search and retrieval | Done |
| `server/src/DocHub.Services/Ingestion/IngestionService.cs` | extract → chunk → embed → index; permanent vs transient failure split | Done |
| `server/src/DocHub.Services/Ingestion/TextChunker.cs` | Structure-aware chunking, overlap, per-document minimum | Done |
| `server/src/DocHub.Services/Ingestion/Extraction/*` | `ITextExtractor` + PlainText / PdfPig / OpenXML | Done |
| `server/src/DocHub.Integrations/Llm/OllamaLlmProvider.cs` | NDJSON streaming chat | Done |
| `server/src/DocHub.Integrations/Embeddings/*` | Ollama + hashing providers | Done |
| `server/src/DocHub.DataAccess/DocHubDbContext.cs` | Schema; `EmbeddingDimensions=768`, tsvector, HNSW, jsonb citations | Done |
| `server/src/DocHub.DataAccess/Repositories/ChunkRepository.cs` | Both search branches; `WebSearchToTsQuery` must stay **inline** in the expression tree | Done |
| `server/src/DocHub.Api/Program.cs` | DI composition, auth pipeline, Hangfire, Swagger, health checks | Done |
| `server/src/DocHub.Api/Infrastructure/Auth/AuthenticationRegistration.cs` | Identity, cookie, policies, Google provider, claims factory | Done |
| `server/src/DocHub.Api/Controllers/AuthController.cs` | login/logout/me/options + the Google callback where access is decided | Done |
| `server/src/DocHub.Api/Controllers/UsersController.cs` | Admin account management | Done |
| `client/src/app/core/state/auth-store.ts` | Signed-in principal; `canContribute`/`isAdmin` drive presentation only | Done |
| `server/src/DocHub.Api/Controllers/ChatController.cs` | SSE endpoint + session CRUD | Done |
| `client/src/app/features/chat/chat.ts` / `.html` | Assistant screen | Done |
| `client/src/app/features/chat/citation-text.ts` | Renders `[n]` markers as passage links | Done |
| `client/src/app/core/data/http-knowledge-gateway.ts` | REST + SSE via fetch | Done |
| `server/tests/DocHub.Services.Tests/StackFixture.cs` | Real stack, hashing embeddings, `ScriptedLlmProvider`, `RecordingIngestionQueue` | Done |

---

# Features Implemented

- Folders (nested, materialised path), documents, metadata, tags, starring, versioning, download.
- Background ingestion on Hangfire; reindex endpoint; failure reasons surfaced per document.
- Extraction: Markdown/text/config/SQL, PDF (PdfPig), Word/PowerPoint/Excel (OpenXML).
- Chunking ~800 tokens, 15% overlap, section-scoped, with citation-ready section refs.
- Embeddings via Ollama; hashing fallback for tests.
- Hybrid search (Postgres FTS + pgvector HNSW) with RRF, filters, snippets, diagnostics.
- Search screen: linkable `?q=`, filters, highlighted snippets, branch badges.
- RAG assistant: streaming SSE, sources-before-tokens, citation verification, refusals, session history, stop, delete.
- Citations deep-link to `/docs/:id?chunk=N` and highlight the passage.
- Knowledge sources: concurrent fan-out, rank fusion across sources, per-source failure isolation, `GET /api/sources`, and a `/sources` screen distinguishing inactive from unavailable.
- Health checks: postgres, blob-storage, embeddings, assistant-model — each names the fixing command.
- Swagger UI + OpenAPI; root redirects to `/swagger`.

---

# Pending Features (priority order)

Nothing blocks v1. What follows it:

1. **Deploy to the org Windows box** — the one part of phase 6 never exercised for real. `web.config` and `AspNetCoreModuleV2` have not run.
2. **Phase 7 — real MCP client**, when there is a server to point at. One class in Integrations plus a branch in `AddIntegrations`, and the citation-target decision.
3. **Entra ID single sign-on** — one more branch in `AddDocHubAuthentication`.
4. **Verify Google sign-in end to end** against real credentials.
5. Deferred: OCR for scanned PDFs; client unit tests; the `Cited in answers` counter; CSRF tokens; pushing images to a registry.

---

# Known Issues

- **Uncited answers were mostly a truncation bug, not the model.** Ollama defaults `num_ctx` to 2048 and drops the overflow silently; the grounded prompt is 5,000+ tokens, so the model was being asked to cite passages it had never been shown. Measured on a 4,202-token prompt: at 2048 Ollama read 1,026 tokens, at 8192 it read all 4,202. `Llm:ContextTokens` now sets it explicitly. Whatever citation weakness remains after this is genuinely the model, and `llama3.1:8b`/`qwen2.5:7b` follow the format better than the 3B default.
- **Local generation is slow** — ~5–15 tok/s on CPU-only Docker.
- **Anthropic/Claude `ILlmProvider` not implemented.** User declined adding the Anthropic C# SDK dependency this session. The interface is the seam; adding it = one class + one branch in `AddIntegrations`. `LlmOptions.Provider` currently validates `ollama` only.
- ~~`activity()` returns `[]`~~ **Fixed.** An activity trail now records who did what, and the dashboard feed reads from it. Deletions keep the name of what was deleted, since the row they described is gone.
- **CI runs green on every push to `main`** — server, client and image jobs all pass. (An earlier note here claimed it had never run; that was wrong, it had been triggering on each push all along.) The runner warns that `actions/checkout@v4` and friends target the deprecated Node 20 and are being forced onto Node 24 — informational, and fixed by bumping those actions when newer majors land.
- **Images are built but pushed nowhere**, and no step deploys anything. CI produces artefacts; a human still installs them.
- **IIS itself is untested** — dev is a Mac. The single-site arrangement was verified by publishing and running the same output under Kestrel, which exercises the static-file serving and the anonymous SPA fallback but not `AspNetCoreModuleV2` or `web.config`.
- **No Entra ID yet.** Phase 5 shipped local Identity plus optional Google. The OIDC provider is the same registration shape — one more branch in `AddDocHubAuthentication`.
- **Google sign-in is untested against real Google.** The domain allow-list is unit-tested and the provider is registered correctly, but no end-to-end run has happened without credentials. Configure `Authentication:Google:*` and try it before relying on it.
- ~~No per-source timeout~~ **Fixed.** Each source now runs under its own deadline
  (`Knowledge:SourceTimeoutSeconds`, default 10), linked to the caller's token so a client
  that goes away still cancels everything. A source that times out is left out and named,
  exactly like one that throws. Regression-tested, including that caller cancellation is
  still told apart from the deadline.
- ~~Data Protection keys are not persisted~~ **Fixed.** `Authentication:KeyPath` persists
  them; unset keeps the framework default, which is right for containers. Verified by
  restarting the API twice: with the path set the same cookie still authenticates, without
  it the same cookie is rejected.
- **CSRF rests on SameSite=Lax plus a JSON content type.** Antiforgery tokens were not added; worth revisiting if a form-encoded endpoint ever appears.
- **`Cited in answers` counter on document detail is always 0** — never wired to chat citations.
- Test suites share one Postgres per collection; assertions that could match other tests' documents must scope by `FolderId`.
- ~5 test documents the assistant/search were verified against remain in the user's dev DB (`vpn-guide.md`, `runbook.pdf`, `expense-policy.docx`, duplicates), alongside the user's own uploads.
- **Git identity was never configured**, so every commit up to 2026-07-30 is attributed to
  `Anush S <anushs@Anushs-MacBook-Air.local>` — a synthesised hostname address that GitHub
  cannot link to the account. Fixed going forward by setting `user.name`/`user.email`;
  existing commits would need a history rewrite and a force-push, which has not been done.
- A `vera@dochub.local` Viewer account (password `viewer-local-dev-pw`) was created in the dev DB while verifying role separation. Harmless, and useful for re-testing; disable or delete it if unwanted.

---

# Current Task Context

No files are mid-edit; the tree is clean and pushed.

**Starting point for phase 7** — the real MCP client. The seam is already in place:
`IKnowledgeSource` in Integrations, with `NullRepositoryKnowledgeSource` as the stub to
replace and `KnowledgeSourceOptions.RepositoryProvider` currently validating `none` only.
The composite, its failure isolation and the `/sources` screen all work against more than
one source already.

The one contract question phase 7 has to answer: `KnowledgeResult` is document-shaped
because a persisted `Citation` carries a document id and deep-links to `/docs/:id?chunk=n`.
A file at a commit needs a citation target that is not a document id.

**Edge cases already handled — don't re-solve:** empty retrieval refusal; fabricated citation markers; vector-branch outage degrading to keyword-only; DbContext concurrency; SSE validation-before-headers; unresolved markers rendered as plain text; a knowledge source failing mid-question; duplicate passages returned by two sources; an empty query reaching the composite; account enumeration via the login form; open redirect on `returnUrl`; a cookie whose user was deleted mid-session; an admin demoting themselves to nobody; the SSE `fetch` path missing the session cookie.

---

# Coding Standards

- **Comments explain *why*, never *what*.** Every non-obvious decision carries a short rationale comment. Match surrounding density.
- XML doc comments on public interfaces/records; `<param>` for non-obvious parameters (never inline in a positional record's parameter list — it won't compile).
- Repositories/implementations are `internal`; only interfaces escape a layer. `InternalsVisibleTo` for test projects.
- One `IOptions<T>` class per integration, `.Validate(...).ValidateOnStart()`.
- Enums persisted as **text** (`HasConversion<string>`) so dumps stay readable and reordering can't remap rows.
- `Guid.CreateVersion7()` for new ids.
- Tests run against **real** Postgres/Azurite — no in-memory providers. Only the AI models are faked.
- Test names are full sentences (`A_fabricated_citation_is_dropped_from_the_answer`).
- Angular: `ChangeDetectionStrategy.OnPush`, signals, `@if`/`@for`, `protected` members for template use. Colours/radii only via `--dh-*` tokens in `styles.css`.
- Templates: no whitespace around interpolation when it would render mid-word (see `citation-text.ts`).
- Commits: short subject + a few terse bullets, no rationale essays, **no attribution trailers**. Push after each chunk.
- README updated in the same commit as any setup/port/status change.

---

# Important Constraints

- **Never** let a Controller hold business logic, or a Service touch EF Core/Blob/LLM clients except through interfaces.
- **Never** skip a roadmap phase.
- **Never** commit real secrets; LLM keys go to `dotnet user-secrets`.
- **Never** add Co-Authored-By / "Generated with Claude Code" to commits or PRs.
- Provisioning stays explicit — no create-on-startup (Hangfire's own schema excepted).
- `WebSearchToTsQuery` must appear **inline inside the LINQ expression**; hoisting it into a local throws at runtime.
- EF test fixtures must call `.UseNpgsql(conn, o => o.UseVector())` or the model fails validation.
- Changing the embedding model requires a migration **and** a full re-index.
- Commit and push each finished chunk without asking; verify (build + tests) before pushing.
- Port 5080 may already hold a user-run API — verify on a spare port (5099) rather than killing their process. If the client proxy is repointed for verification, revert it before committing.

---

# Frequently Used Commands

```bash
docker compose up -d --wait
docker compose exec ollama ollama pull nomic-embed-text
docker compose exec ollama ollama pull llama3.2:3b
dotnet ef database update --project server/src/DocHub.DataAccess --startup-project server/src/DocHub.Api
dotnet ef migrations add <Name> --project server/src/DocHub.DataAccess --startup-project server/src/DocHub.Api
dotnet run --project server/src/DocHub.Api -- init-storage
dotnet run --project server/src/DocHub.Api
dotnet build
dotnet test server/DocHub.slnx
npm --prefix client install
npm --prefix client start
npx ng build --configuration development     # from client/
npx tsc --noEmit -p tsconfig.app.json        # from client/
```

Ports: client 4200 · API 5080 (`/swagger`, `/jobs`, `/healthz`) · Postgres 5432 · Azurite 10000 · Ollama 11434.

---

# Questions Still Open

- When to add Entra ID, and whether Google sign-in stays alongside it or is replaced by it.
- Whether the audit log (`activity()`) belongs in phase 6 or later — authentication now makes "who did what" recordable for the first time.
- Whether to build the Anthropic `ILlmProvider` now or defer to when Claude becomes the default.
- Whether to bump the default chat model to `llama3.1:8b` or `qwen2.5:7b`, now that the context truncation that was blamed on the model is fixed — worth re-judging answer quality on the 3B first.
- Whether to wire the document-detail "Cited in answers" counter to real chat citations.
- Whether a per-source toggle ("search without repositories for this question") belongs in phase 7 or earlier — the composite currently searches every registered source unconditionally.
- What a repository citation resolves to. `KnowledgeResult` is document-shaped because a persisted `Citation` carries a document id and deep-links to `/docs/:id?chunk=n`; a file-at-a-commit needs a citation target that is not a document id. This is the one part of the phase 4 contract expected to change.

**Resolved in phase 4:** `IKnowledgeSource` lives in Integrations and its implementations
wherever their work belongs (forced by the reference direction); `KnowledgeResult` was
introduced and maps to `RetrievedPassage`, leaving citation verification untouched; an
inactive source renders as its own state, not as a failure.

---

# Next Immediate Steps

The org's MCP server is reachable **only from inside the org network**. That is the shape the
phase 4 stub was built for: the Mac dev machine keeps `RepositoryProvider: "none"` and the null
source, the IIS box sets `"mcp"`, and nothing branches on an environment name — config decides.

1. ~~Add a per-source timeout~~ — **done**, ahead of phase 7. The fan-out is now safe to point
   at a server that may not answer.
2. **Decide the citation target for a non-document source.** `KnowledgeResult` is
   document-shaped because a persisted `Citation` deep-links to `/docs/:id?chunk=n`; a file at a
   commit has no document id. Plan: add optional `SourceName` + `Url` through
   `KnowledgeResult` → `RetrievedPassage` → `Citation` → `CitationViewModel` → `citation-text.ts`,
   rendering an external link when `Url` is set and the existing deep link otherwise. Citations
   are stored as **jsonb**, so this needs no migration, and historical answers keep working
   because their citations simply carry no `Url`.
3. **Implement `McpKnowledgeSource` in Integrations** against `IKnowledgeSource`. It takes its
   address from `IRepositorySourceSettings` (already built and UI-editable), not from
   `IOptions`, so an administrator changing the address takes effect on the next question. The
   servers are open on the org network, so no credential handling is needed. Replace
   `NullRepositoryKnowledgeSource` at the registration in `AddIntegrations`.
4. **Add an MCP health check** alongside the embedding and LLM ones, and make `CheckStatusAsync`
   return `Unavailable` with the reason so `/sources` stays honest rather than always Active.
5. **Verify against the real server from the IIS box**, since the Mac cannot reach it: a genuine
   outage is the first real test of the composite's failure isolation.
6. Revisit vector-store scale — HNSW parameters and whether pgvector still fits the corpus.
7. Update README + `CLAUDE.md` roadmap markers; commit and push each chunk.

Grounding rules are unchanged and still bind: MCP results must be verbatim passages, not
summaries, or the assistant would be citing text it was never given.

---

# Context Recovery Prompt

> Read `SESSION.md` and `CLAUDE.md` at the repo root before doing anything else. This is DocHub — an ASP.NET Core + Angular documentation hub with local-Ollama RAG. Phases 1–6 (doc management, ingestion + hybrid search, grounded AI assistant with verified citations, the `IKnowledgeSource` abstraction with composite retrieval, Identity cookie auth with roles and optional Google sign-in, and the Docker/CI/IIS deployment pipeline) are **complete, tested and pushed to `main`** — do not re-analyse or rebuild them. All 128 tests pass. Start on **Phase 7: the real MCP client**, following the "Next Immediate Steps" checklist in `SESSION.md`. Respect the grounding rules, security rules, deployment rules and layering constraints in `CLAUDE.md`. Build and test before each commit, and commit + push each finished chunk to `main` without asking.
