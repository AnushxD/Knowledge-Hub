# CLAUDE.md — AI Documentation & Knowledge Hub

## What this is
An internal Documentation & Knowledge Hub: upload/organize/search team docs, with an AI assistant that answers questions grounded in indexed content (docs + source code repos via MCP later) and always cites sources. Never fabricate — if the answer isn't in retrieved context, say so.

This is a real org requirement being built as a personal learning project (also learning Claude Code, git workflow, CI/CD).

## Stack
- Frontend: Angular (standalone components, signals) + Angular Material/PrimeNG + Tailwind
- Backend: ASP.NET Core Web API
- DB: PostgreSQL + pgvector extension (one DB for relational data AND embeddings)
- File storage: Azure Blob Storage — Azurite emulator locally (`UseDevelopmentStorage=true`), real Azure in prod
- Background jobs: Hangfire
- Auth: ASP.NET Core Identity locally → Azure AD/Entra ID (OIDC) in prod
- CI/CD: GitHub Actions
- Local dev: dotnet run + ng serve + Docker Compose (Postgres, Azurite) — NOT IIS day-to-day
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
1. Core doc management (upload, folders, metadata, preview) — NO AI yet
2. Ingestion pipeline (extract → chunk → embed) + hybrid search (keyword + vector)
3. AI chat assistant with citations, RAG over uploaded docs only
4. MCP `IKnowledgeSource` abstraction + stub implementation
5. Real auth + roles + security hardening
6. Deployment pipeline (Docker, GitHub Actions, IIS deploy)
7. Real MCP integration + revisit scale (vector store, search)

## Conventions / commands
- `docker compose up -d` — start Postgres + Azurite before `dotnet run`
- `dotnet run` (server/src/DocHub.Api) — Kestrel, not IIS, for daily dev
- `ng serve` (client) — Angular dev server
- Commit small, one thing at a time; a milestone commit when a roadmap phase is functionally done
- Trunk-based: commit to `main` directly, or one short-lived `feature/*` branch at a time

## Never
- Never put a real secret in any committed `appsettings.*.json`
- Never let a Controller contain business logic, or a Service talk to EF Core/Blob/LLM clients directly instead of through Data Access / Integrations interfaces
- Never skip a roadmap phase (e.g. don't build the MCP integration before core doc management + basic RAG work)
