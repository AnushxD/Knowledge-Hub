# Project Overview

Internal **Documentation & Knowledge Hub** (DocHub): upload/organise/search team docs, plus an AI assistant that answers strictly from indexed content and cites every claim. Personal learning project built to a real org requirement.

**Stack:** Angular 22 (standalone, signals, zoneless) + Tailwind v4 · ASP.NET Core (.NET 10) · PostgreSQL 17 + pgvector · Azurite (blob) · Hangfire · Ollama (local models) · Docker Compose.

**Architecture (strict, org convention):** `Controllers → Services → Data Access (EF Core) ` with `Integrations` a *sibling* of Data Access for external systems, always behind an interface.

**Modules:** `DocHub.Api`, `DocHub.Services`, `DocHub.DataAccess`, `DocHub.Integrations`, `client/` (Angular).

**Branch:** `main`, synced with `origin/main`. Trunk-based; commit + push each finished chunk.

> Stable architecture, conventions and grounding rules live in `CLAUDE.md`. This file is session state only.

---

# Current Objective

Phases 1–4 are **complete and pushed**. Next objective is **Phase 5: real auth + roles + security hardening** — Identity locally → Entra ID, replacing `SeededCurrentUser`, securing `/jobs` and `/swagger`, rate-limiting chat.

---

# Current Progress

## Completed
- **Phase 1** — folders, upload, metadata, versioning, preview, blob storage.
- **Phase 2** — ingestion pipeline (extract → chunk → embed → index), hybrid search, search screen.
- **Phase 3** — RAG assistant: streaming grounded answers, verified citations, refusals, session history.
- **Phase 4** — `IKnowledgeSource` abstraction, composite retrieval with per-source failure
  isolation, null repository stub, `GET /api/sources`, real `/sources` screen.
- Swagger UI at `/swagger` (dev only).
- 108 tests green: 8 Integrations, 14 DataAccess, 86 Services.

## In Progress
- Nothing. Working tree clean, all work pushed.

## Remaining (roadmap)
5. Real auth + roles + security hardening — **next**
6. Deployment pipeline (Docker, GitHub Actions, IIS)
7. Real MCP integration + revisit vector-store scale

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
- **The null repository source is registered locally on purpose** — a fan-out exercised against one source until phase 7 is a fan-out first debugged in phase 7.
- **`KnowledgeSourceState` has three values, not a boolean** — `inactive` (off by design) must not render like `unavailable` (should work, doesn't).

---

# Repository Structure

```
client/
  src/app/core/{data,models,state,theme,utils}
  src/app/features/{dashboard,browse,document-detail,search,chat,sources,settings,roadmap}
  src/app/layout/{shell,nav-rail,top-bar,folder-tree,ai-dock,command-palette}
  src/app/shared/{components,directives,pipes}
  src/styles.css                     # --dh-* design tokens (single source of truth)
server/
  src/DocHub.Api/{Controllers,Infrastructure}
  src/DocHub.Services/{Documents,Folders,Ingestion,Search,Chat,Knowledge,ViewModels}
  src/DocHub.DataAccess/{Entities,Dtos,Repositories,Migrations}
  src/DocHub.Integrations/{Storage,Embeddings,Llm,Knowledge,HealthChecks}
  tests/{DocHub.Services.Tests,DocHub.DataAccess.Tests,DocHub.Integrations.Tests}
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
| `server/src/DocHub.Api/Program.cs` | DI composition, Hangfire, Swagger, health checks | Done |
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

1. **Phase 5 — auth**: Identity → Entra ID, roles, replace `SeededCurrentUser`, secure `/jobs` + `/swagger`, rate-limit chat.
2. Phase 6 — Dockerfile, GitHub Actions, IIS deploy.
3. Phase 7 — real MCP client implementing `IKnowledgeSource` (the seam is in place: one class in Integrations plus a branch in `AddIntegrations`), vector-store scale review.
4. Deferred: OCR for scanned PDFs; audit log (`activity()` returns `[]`); client unit tests.

---

# Known Issues

- **Answer quality is model-limited.** `llama3.2:3b` sometimes emits no citations despite the worked example in the prompt. Mitigated by the UI warning on uncited answers. `llama3.1:8b`/`qwen2.5:7b` follow the format better.
- **Local generation is slow** — ~5–15 tok/s on CPU-only Docker.
- **Anthropic/Claude `ILlmProvider` not implemented.** User declined adding the Anthropic C# SDK dependency this session. The interface is the seam; adding it = one class + one branch in `AddIntegrations`. `LlmOptions.Provider` currently validates `ollama` only.
- **`activity()` returns `[]`** — no audit log until phase 5.
- **`/jobs` and `/swagger` are unauthenticated**, dev-only by registration.
- **`Cited in answers` counter on document detail is always 0** — never wired to chat citations.
- Test suites share one Postgres per collection; assertions that could match other tests' documents must scope by `FolderId`.
- ~5 test documents the assistant/search were verified against remain in the user's dev DB (`vpn-guide.md`, `runbook.pdf`, `expense-policy.docx`, duplicates), alongside the user's own uploads.

---

# Current Task Context

No files are mid-edit; the tree is clean and pushed.

**Starting point for phase 5** — auth. `SeededCurrentUser` (registered in
`ServicesServiceCollectionExtensions`) is the seam: `ICurrentUser.Id` is already what every
service uses for ownership, so real auth replaces one registration. `/jobs` and `/swagger`
are dev-only by registration and need real protection once they are not.

**Edge cases already handled — don't re-solve:** empty retrieval refusal; fabricated citation markers; vector-branch outage degrading to keyword-only; DbContext concurrency; SSE validation-before-headers; unresolved markers rendered as plain text; a knowledge source failing mid-question; duplicate passages returned by two sources; an empty query reaching the composite.

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

- Whether to build the Anthropic `ILlmProvider` now or defer to when Claude becomes the default.
- Whether to bump the default chat model to `llama3.1:8b` for citation reliability, trading speed/disk.
- Whether to wire the document-detail "Cited in answers" counter to real chat citations.
- Whether a per-source toggle ("search without repositories for this question") belongs in phase 7 or earlier — the composite currently searches every registered source unconditionally.
- What a repository citation resolves to. `KnowledgeResult` is document-shaped because a persisted `Citation` carries a document id and deep-links to `/docs/:id?chunk=n`; a file-at-a-commit needs a citation target that is not a document id. This is the one part of the phase 4 contract expected to change.

**Resolved in phase 4:** `IKnowledgeSource` lives in Integrations and its implementations
wherever their work belongs (forced by the reference direction); `KnowledgeResult` was
introduced and maps to `RetrievedPassage`, leaving citation verification untouched; an
inactive source renders as its own state, not as a failure.

---

# Next Immediate Steps

1. ASP.NET Core Identity in the API, with the local login flow and a real `ICurrentUser` replacing `SeededCurrentUser`.
2. Roles (reader / contributor / admin) and authorisation on the document, folder and chat endpoints.
3. Secure `/jobs` and `/swagger` behind an admin policy rather than relying on dev-only registration.
4. Rate-limit `POST /api/chat` — generation is the expensive endpoint and is currently unbounded.
5. Client: sign-in screen, auth interceptor on `HttpKnowledgeGateway`, and the settings screen's account section wired to the real principal.
6. Update README + `CLAUDE.md` roadmap markers; commit and push each chunk.

---

# Context Recovery Prompt

> Read `SESSION.md` and `CLAUDE.md` at the repo root before doing anything else. This is DocHub — an ASP.NET Core + Angular documentation hub with local-Ollama RAG. Phases 1–4 (doc management, ingestion + hybrid search, grounded AI assistant with verified citations, and the `IKnowledgeSource` abstraction with composite retrieval) are **complete, tested and pushed to `main`** — do not re-analyse or rebuild them. All 108 tests pass. Start on **Phase 5: real auth + roles + security hardening**, following the "Next Immediate Steps" checklist in `SESSION.md`. Respect the grounding rules and layering constraints in `CLAUDE.md`. Build and test before each commit, and commit + push each finished chunk to `main` without asking.
