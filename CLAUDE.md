# CLAUDE.md — AI Documentation & Knowledge Hub

## What this is
An internal Documentation & Knowledge Hub: upload/organize/search team docs, with an AI assistant that answers questions grounded in indexed content (docs + source code repos via MCP later) and always cites sources. Never fabricate — if the answer isn't in retrieved context, say so.

This is a real org requirement being built as a personal learning project (also learning Claude Code, git workflow, CI/CD).

## Stack
- Frontend: Angular 22 (standalone components, signals, zoneless) + Tailwind v4 — **no third-party UI kit**
  - PrimeNG and PrimeIcons were evaluated and rejected: as of v22 both moved to the
    commercial PrimeUI license, which requires a per-developer key and shows a nag
    banner without one. Every control is hand-built on our own design tokens instead.
  - Design tokens live in `client/src/styles.css` (`--dh-*`) — single source of truth for
    both themes. Add a colour/radius/shadow there, never inline in a component.
  - Icons: Lucide (ISC), inlined as CSS masks by `client/tools/gen-icons.mjs` into the
    generated `client/src/styles/icons.css`. To add an icon, extend the map in that script
    and re-run `node tools/gen-icons.mjs`. Never edit `icons.css` by hand.
  - The UI talks to the backend only through `KnowledgeGateway` (abstract class). Bound to
    `HttpKnowledgeGateway`; `MockKnowledgeGateway` implements the same contract for working
    on screens without the backend — a one-line provider swap in `app.config.ts`. No
    component injects HttpClient directly. Exception: SSE streaming uses `fetch` inside the
    gateway, because HttpClient buffers the whole body.
- Backend: ASP.NET Core Web API
- DB: PostgreSQL + pgvector extension (one DB for relational data AND embeddings)
- File storage: Azure Blob Storage — Azurite emulator locally (`UseDevelopmentStorage=true`), real Azure in prod
- Background jobs: Hangfire (Postgres-backed, in-process; dashboard at /jobs, Admin only)
- AI models: local Ollama in Docker — `nomic-embed-text` (768-dim embeddings) and
  `llama3.2:3b` (answers). Free, key-less, nothing leaves the machine. Both sit behind
  Integrations interfaces so a hosted provider is a registration change.
- API docs: Swagger UI at /swagger (dev only, and Admin only) over the built-in OpenAPI document
- Auth: ASP.NET Core Identity with a session cookie → Azure AD/Entra ID (OIDC) in prod.
  Optional Google sign-in for company addresses, off unless configured.
- CI/CD: GitHub Actions
- Local dev: dotnet run + ng serve + Docker Compose (Postgres, Azurite, Ollama) — NOT IIS day-to-day
- Deploy path: IIS on an org Windows machine first (dev is on a Mac — IIS itself doesn't run on Mac, so IIS testing happens on the Windows box), then Azure App Service later

## Architecture — follow this exactly, it matches our org's existing convention
```
Controllers   → endpoints only, accept/return ViewModels, NO business logic
Service layer → business logic lives HERE, converts ViewModel → DTO before
                calling Data Access. RAG orchestration (search, chat, ingestion)
                also lives here.
Data Access   → DTOs ↔ EF Core ↔ PostgreSQL
Integrations  → sibling to Data Access (not inside it) — external systems only,
                always called from Service layer through an interface:
                ILlmProvider, IEmbeddingProvider, IVectorStore,
                IKnowledgeSource (MCP client — stub locally, real impl in prod),
                IFileStorage (Azure Blob / Azurite)
```
Do not fold Integrations into Data Access — external API calls (LLM, blob, MCP) have different failure/testing needs than DB calls, but the same "always behind an interface" relationship to Services.

### Where knowledge sources live (phase 4)
`IKnowledgeSource` and its data types (`KnowledgeQuery`, `KnowledgeResult`,
`KnowledgeSearchResult`, `KnowledgeSourceStatus`) live in **Integrations**. The project
reference direction decides it: Services references Integrations and never the reverse, so
a contract an MCP client must implement cannot be defined in Services.

Implementations live wherever their work belongs, not all in one project:
- `NullRepositoryKnowledgeSource` (and the real MCP client later) → **Integrations**, an
  external system.
- `DocumentKnowledgeSource` → **Services**, because it wraps `ISearchService`. Nothing
  external is called; it is an adapter onto our own search.
- `CompositeKnowledgeSource` → **Services**. Fanning out, isolating failures and merging
  by rank are policy decisions, which is the definition of business logic here.

`ChatService` depends on `IKnowledgeRetriever` (Services), implemented by the composite. It
returns the existing `RetrievedPassage`, so citation verification and the grounding rules
below are untouched by the addition of a source.

## Config
- `appsettings.json` — shape/defaults only, safe to commit, no real values
- `appsettings.Development.json` — local Postgres conn string + `UseDevelopmentStorage=true` for Azurite — safe to commit, no real secrets
- Real secrets (e.g. LLM API key) → `dotnet user-secrets`, never in any appsettings file
- Prod secrets → env vars / Azure Key Vault, never committed
- Bind config via strongly-typed Options classes (`IOptions<T>`), one per Integration

## Folder layout
```
doc-knowledge-hub/
├── client/              # Angular
├── server/
│   ├── src/
│   │   ├── DocHub.Api/
│   │   ├── DocHub.Services/
│   │   ├── DocHub.DataAccess/
│   │   └── DocHub.Integrations/
│   └── tests/
├── docker-compose.yml
├── .gitignore
└── CLAUDE.md
```

## Roadmap — build in this order, don't jump ahead
1. ✅ Core doc management (upload, folders, metadata, preview) — NO AI yet
2. ✅ Ingestion pipeline (extract → chunk → embed) + hybrid search (keyword + vector)
3. ✅ AI chat assistant with citations, RAG over uploaded docs only
4. ✅ MCP `IKnowledgeSource` abstraction + stub implementation
5. ✅ Real auth + roles + security hardening
6. ✅ Deployment pipeline (Docker, GitHub Actions, IIS deploy)
7. Real MCP integration + revisit scale (vector store, search)  ← NEXT

## Grounding rules (phases 2–4, non-negotiable)
- Only `Indexed` documents are retrievable. A half-processed or failed document must never
  be searchable or citable.
- The LLM is never called with zero retrieved passages — that is exactly what produces
  confident fabrication. Refuse instead.
- Every citation marker the model emits is verified against the passages actually supplied;
  unresolvable markers are stripped, never rendered as links.
- "I don't know" is a designed outcome, persisted with `IsRefusal`, rendered as information
  rather than as an error.
- Citations denormalise document title + heading onto the message, so renaming or deleting
  a document cannot rewrite a historical answer.
- Search and the assistant share one ranking implementation (`SearchService.RankAsync`), so
  the assistant can only cite what the search screen would have shown.
- A knowledge source that fails is left out of that one answer and named in the reply — never
  silently dropped, and never allowed to fail the whole question. If every source fails there
  is nothing to ground on, and the normal refusal path handles it.
- Every source answers under its own deadline, linked to the caller's token. Failure
  isolation covers a source that throws; a source that never replies would otherwise stall
  the fan-out, because it waits for all of them. A timeout is reported like any other
  degradation, and is distinguished from the caller cancelling.
- Sources are merged by rank, never by score. Each source scores in its own units, so adding
  them together would be arbitrary — the same reason the keyword and vector branches fuse
  by rank.

## Security rules (phase 5, non-negotiable)
- The `users` table **is** the Identity user store — one row per person, so every
  `owner_id` foreign key keeps pointing at the same key. Never add a parallel identity table.
- Authorisation defaults to closed: a fallback policy requires a session, so a new endpoint
  is protected before anyone remembers to think about it. Opting out is an explicit
  `[AllowAnonymous]`, and only health checks and the sign-in endpoints have one.
- Role lives in one column and is projected into a claim at sign-in. One role per person;
  Entra ID maps a directory group onto the same value later.
- Endpoint attributes handle "may this role call this at all". A rule about a *particular
  row* is business logic and belongs in a Service, which is why `ICurrentUser` exposes Role.
- Google sign-in decides access on the server, from the email Google verified — never from
  the `hd` request hint, which the browser controls. An unverified address is refused, and
  an empty allow-list admits **nobody**.
- A password hash never appears in a migration or an appsettings file. `seed-admin` sets the
  local one; everywhere else it is user-secrets or Key Vault.
- Accounts are disabled, never deleted — they own documents, folders and conversations.
- Client-side guards and hidden buttons are courtesy. The API is what enforces access, and
  every rule must hold with the client bypassed entirely.
- Data Protection keys must outlive the process wherever sessions are expected to
  (`Authentication:KeyPath`). Unset, they live in the app pool profile and every recycle
  signs everyone out — a configuration problem that presents as an intermittent bug.

## Deployment rules (phase 6)
- **One binary, two shapes.** Containers: nginx serves the client and proxies `/api`. IIS:
  the API serves the client from `wwwroot` and it is a single site. The API decides at
  startup by looking for `wwwroot/index.html` — nothing branches on an environment name.
- **The SPA fallback is `AllowAnonymous`.** It *is* the login screen; the fallback
  authorisation policy would otherwise 401 the page whose purpose is to obtain a session.
- **Response buffering must be off in front of the assistant** — `proxy_buffering off` in
  nginx, `responseBufferLimit="0"` in `web.config`. Neither fails loudly: the answer simply
  stops streaming and arrives in one lump.
- **CI reuses the repo's own `docker-compose.yml`** for Postgres and Azurite, so the
  infrastructure tests run against cannot drift from local dev.
- **Never set `DOCHUB_TEST_DB` globally in CI.** Both test projects read that one variable
  but default to different databases and each recreates its own.
- **No credential ever reaches an artefact.** `appsettings.Development.json` is excluded
  from publish output *and* from the Docker build context; the publish workflow asserts it.
- Provisioning stays operator-run in every environment — the pipeline never migrates a
  database or creates a container as a side effect of deploying.

## Provisioning is explicit
The API never creates databases, containers or schema at startup. Setup is operator-run:
`dotnet ef database update`, `dotnet run -- init-storage`, `dotnet run -- seed-admin`,
`ollama pull`. The one exception
is Hangfire's own `hangfire` schema, which the library versions with itself.

## Conventions / commands
- `docker compose up -d` — start Postgres + Azurite + Ollama before `dotnet run`
- `dotnet run` (server/src/DocHub.Api) — Kestrel, not IIS, for daily dev
- `ng serve` (client) — Angular dev server
- Commit small, one thing at a time; a milestone commit when a roadmap phase is functionally done
- Trunk-based: commit to `main` directly, or one short-lived `feature/*` branch at a time

## Never
- Never put a real secret in any committed `appsettings.*.json`
- Never let a Controller contain business logic, or a Service talk to EF Core/Blob/LLM clients directly instead of through Data Access / Integrations interfaces
- Never skip a roadmap phase (e.g. don't build the MCP integration before core doc management + basic RAG work)
- Never add Co-Authored-By or "Generated with Claude Code" trailers to commits or PRs
- Never let the assistant answer from anything but retrieved passages
- Never trust a client-supplied domain, `hd` hint or `returnUrl` — verify domains against
  the address the provider verified, and restrict redirects to local paths
- Never add an authentication bypass for convenience, dev-only or otherwise
