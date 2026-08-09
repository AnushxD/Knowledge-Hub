# AI-Powered Documentation & Knowledge Hub — Technical Blueprint

> **This is v1's blueprint, kept as the record of the original design.** It is
> not maintained against the current code. V2 replaced uploads with a read-only
> mirror of a GitLab repository: documents are files in that repository,
> folders are its directories, bytes are streamed from GitLab on demand, and
> "No forced structure" below is now the repository's structure rather than the
> user's. What still holds is everything about grounding, retrieval and the
> layering — the assistant, the search, and the interfaces around them are
> unchanged. For what the system does today, read `CLAUDE.md`, `README.md` and
> `chat-pipeline.md`.

## 1. Overview & Goals

A centralized, enterprise-grade Documentation & Knowledge Hub that lets a team upload, organize, and search internal documentation, and lets an AI assistant answer natural-language questions grounded in that documentation *and* the team's source code repositories (via an existing MCP server, added later).

Non-negotiable product principles baked into this architecture:
- **No fabrication.** The AI only answers from retrieved, indexed content and always cites sources; if nothing relevant is found, it says so.
- **No forced structure.** Users define their own folder hierarchy.
- **Local-first development.** Everything works standalone against uploaded documents; the MCP repository source is a pluggable add-on, not a hard dependency.
- **Grow-in-place.** Every "vector database," "search," and "repository source" decision is made behind an interface so the underlying implementation can change without touching calling code.

---

## 2. High-Level System Architecture

```
                         ┌─────────────────────────┐
                         │   Angular SPA (client)   │
                         │  Dashboard / Folder Tree │
                         │  Search / AI Chat Panel  │
                         └────────────┬────────────┘
                                      │ HTTPS (REST + SSE/WebSocket for streaming chat)
                         ┌────────────▼────────────┐
                         │   ASP.NET Core Web API   │
                         │  (Clean Architecture)    │
                         └──┬──────┬──────┬─────┬──┘
                            │      │      │     │
              ┌─────────────┘      │      │     └──────────────┐
              ▼                   ▼      ▼                     ▼
     ┌────────────────┐  ┌───────────────┐ ┌───────────────┐ ┌──────────────────┐
     │ Document Service│  │ Search/RAG    │ │ Knowledge      │ │ Auth / Identity  │
     │ (CRUD, folders, │  │ Orchestrator  │ │ Source         │ │ Service          │
     │ metadata)       │  │               │ │ Aggregator     │ │                  │
     └───────┬─────────┘  └───────┬───────┘ └───────┬────────┘ └──────────────────┘
             │                    │                 │
             │           ┌────────┴────────┐   ┌────┴─────────────────┐
             │           ▼                 ▼   ▼                      ▼
             │   ┌───────────────┐ ┌───────────────┐        ┌──────────────────┐
             │   │ Vector Store  │ │ LLM Provider   │        │ MCP Client        │
             │   │ (pgvector)    │ │ (Claude API)   │        │ (stub locally,    │
             │   └───────────────┘ └───────────────┘        │ real in prod)     │
             │                                                └──────────────────┘
             ▼
   ┌───────────────────┐        ┌────────────────────┐
   │ Relational DB      │        │ Blob/File Storage  │
   │ (PostgreSQL)       │        │ (MinIO → Azure Blob│
   │ metadata, folders, │        │  / S3 in prod)     │
   │ chunks, chat log   │        └────────────────────┘
   └───────────────────┘

   ┌──────────────────────────────────┐
   │ Background Worker (Hangfire)      │
   │ Ingestion pipeline: extract →     │
   │ chunk → embed → index             │
   └──────────────────────────────────┘
```

---

## 3. Technology Stack & Justification

| Layer | Choice | Why |
|---|---|---|
| Frontend | **Angular** (latest LTS, standalone components + signals) | Requested; strong fit for enterprise SPA with complex state (folder tree, chat panel, previews) |
| UI kit | Angular Material or PrimeNG + Tailwind for custom polish | Gets you Notion/Confluence-grade UI fast without hand-rolling components |
| Backend | **ASP.NET Core Web API**, layered as **Controller → Service → Data Access**, matching the org's existing convention (ViewModels in/out of Controllers, DTOs between Service and Data Access), plus a sibling **Integrations layer** for external providers (see §3a) | Requested; keeps this project consistent with the org's actual codebase conventions rather than introducing an unfamiliar pattern |
| Relational DB | **PostgreSQL** (confirmed) | Free/open-source software with no licensing cost at any scale; doubles as the vector store (see below). Local dev is free (Docker); Azure Database for PostgreSQL in prod runs roughly $12+/month depending on tier — a small, predictable cost for an internal tool |
| Vector store | **pgvector extension on the same PostgreSQL instance** | Avoids a second database to operate in early phases. One connection string, one backup strategy, one place to reason about consistency between metadata and embeddings. Swap to Qdrant/Azure AI Search later purely by re-implementing `IVectorStore` if scale demands it |
| File storage | **Azure Blob Storage** (confirmed — already used in the org) locally via the Azurite emulator, real Azure Blob Storage in prod | Same SDK/client code in both environments (Azurite emulates the Blob Storage API); no code branching between dev and prod storage code, and no need to introduce a separate tool (e.g. MinIO) the org doesn't already use |

### 3a. Layered Architecture (C# backend)

```
Controllers        → Endpoints only. Accept/return ViewModels. No business logic.
      ↓ ViewModel
Service layer       → Business logic lives here. Converts ViewModel → DTO
                       before calling Data Access. Also where RAG orchestration
                       lives (SearchService, IngestionService, ChatOrchestratorService).
      ↓ DTO                              ↓ (via interfaces, same pattern as DAL)
Data Access layer    → DTOs ↔ EF Core ↔ PostgreSQL         Integrations layer
                       (Documents, Folders, Chunks, etc.)   (sibling to DAL, not inside it)
                                                             - ILlmProvider       → Claude API
                                                             - IEmbeddingProvider → embeddings
                                                             - IVectorStore       → pgvector
                                                             - IKnowledgeSource   → MCP client (stub locally)
                                                             - IFileStorage       → Azure Blob
```

The Integrations layer exists because these are calls to *external systems* (LLM API, blob storage, MCP), not the app's own database — different failure modes and testing needs than DAL, but the same relationship to the Service layer: always called through an interface, never a concrete client directly. This is what keeps `IKnowledgeSource`, `IVectorStore`, etc. swappable later without touching Service code, while leaving the existing Controller/Service/DTO convention completely intact.
| Background jobs | **Hangfire** | Runs inside the ASP.NET Core process, has a dashboard out of the box, trivial to set up locally, and graduates to a distributed worker later without a rewrite (swap the persistence store) |
| Search | Hybrid: Postgres full-text (`tsvector`) + pgvector cosine similarity, merged and re-ranked | No extra infrastructure for phase 1–3; add OpenSearch/Elastic later only if full-text relevance becomes a bottleneck |
| LLM | **Claude** (Anthropic API) via a provider-agnostic `ILlmProvider` interface | Keeps you free to swap models/providers later; also matches your own tooling |
| Embeddings | Configurable embedding provider behind `IEmbeddingProvider` (e.g. Voyage AI, OpenAI, or a local model) | Embedding model choice often changes faster than the rest of the stack — isolate it |
| Auth | ASP.NET Core Identity + JWT locally; **Azure AD / Entra ID (OIDC)** in prod if team is on Microsoft 365 | Enterprise teams usually already have SSO — plug into it rather than reinventing login |
| Containerization | Docker + docker-compose (local), Kubernetes or Azure App Service (prod) | Same container images in both environments |
| CI/CD | GitHub Actions | Matches the git workflow you're learning; free-tier friendly |
| Observability | Serilog (structured logs) + OpenTelemetry traces → Application Insights or Grafana/Loki stack | Standard .NET observability path |

---

## 4. Document Ingestion Pipeline

```
Upload → Blob Storage + DB row (status: Pending)
   → Enqueue ingestion job (Hangfire)
      → Extract text (per file type — see table below)
      → Chunk text (semantic chunking, ~500–1000 tokens, overlap ~15%)
      → Generate embeddings (batched calls to IEmbeddingProvider)
      → Write chunks + embeddings + section refs to DB
      → Update document status: Indexed (or Failed, with retry)
```

**Extraction strategy by file type:**

| Type | Approach |
|---|---|
| PDF | Text extraction library (e.g. PdfPig / iText); OCR fallback for scanned pages |
| Word / PPTX / Excel | OpenXML SDK for structured extraction |
| Markdown / text / config / SQL | Direct read, light structural parsing (headers, code blocks) preserved as metadata |
| Images / diagrams | Store as-is for preview; optional OCR or an image-captioning model to make them searchable/citable |
| Workflow diagrams | Treat as images unless in a text-based format (e.g. Mermaid/drawio XML), in which case parse the underlying text too |

Each chunk stores: document ID, chunk text, embedding vector, section/page reference, and order — this is what lets citations point to the *exact section*, not just "the document."

---

## 5. RAG / AI Assistant Workflow

1. User submits a question (chat panel or global search bar).
2. Generate a query embedding.
3. **Parallel retrieval:**
   - Vector similarity search over document chunks (pgvector)
   - Full-text/keyword search (Postgres `tsvector`)
   - Repository search via the Knowledge Source Aggregator (real MCP call in prod, no-op locally)
4. Merge + re-rank results (reciprocal rank fusion or a simple weighted score) into a top-k context set.
5. Build a strict system prompt: *answer only from the provided context; cite every claim to its source; if the context doesn't contain the answer, say so explicitly — never speculate.*
6. Call the LLM, stream the response back to the client.
7. Attach structured citations (document title, section, deep link) to the response before rendering.

This strictness (step 5) is what prevents fabrication — it's a prompting/architecture discipline, not something the model guarantees on its own, so the orchestrator should also do a lightweight post-check that every cited source ID actually appears in the retrieved context before returning the answer.

---

## 6. MCP Integration Strategy (the key abstraction)

Define one interface that both local development and production implement identically from the caller's point of view:

```csharp
public interface IKnowledgeSource
{
    string Name { get; }
    Task<IReadOnlyList<KnowledgeResult>> SearchAsync(string query, CancellationToken ct);
}
```

- **`DocumentKnowledgeSource`** — always active. Wraps the Postgres/pgvector hybrid search over uploaded docs.
- **`RepositoryKnowledgeSource`** — wraps the MCP client. In local dev, registered as a `NullRepositoryKnowledgeSource` that returns an empty result set (or is entirely omitted from the aggregator via config). In production, the real MCP-backed implementation is registered instead — same interface, zero changes to the orchestrator or UI.
- **`CompositeKnowledgeSource`** — implements the same interface, fans out to all registered sources in parallel, merges results.

This is the single most important design decision for your stated constraint ("works locally now, MCP added later with minimal changes") — everything upstream (RAG orchestrator, citation builder, UI) only ever talks to `IKnowledgeSource`, never to Postgres or MCP directly.

---

## 7. Database Schema (high-level)

| Table | Key columns |
|---|---|
| `Users` | Id, Name, Email, Role |
| `Folders` | Id, ParentId (self-referencing), Name, Path, OwnerId |
| `Documents` | Id, FolderId, Title, Description, Tags[], Version, OwnerId, FileType, StoragePath, Status, CreatedAt, UpdatedAt |
| `DocumentVersions` | Id, DocumentId, VersionNumber, StoragePath, ChangedBy, ChangedAt |
| `DocumentChunks` | Id, DocumentId, ChunkText, Embedding (vector), SectionRef, ChunkOrder |
| `AuditLog` | Id, UserId, Action, EntityType, EntityId, Timestamp |
| `ChatSessions` | Id, UserId, CreatedAt |
| `ChatMessages` | Id, SessionId, Role, Content, Citations (JSON), CreatedAt |

---

## 8. Security & Auth

- **AuthN:** ASP.NET Core Identity (local dev) → Azure AD/Entra ID via OIDC (prod), so login "just works" with existing company accounts.
- **AuthZ:** Role-based to start (Admin / Editor / Viewer); folder-level ACLs are a natural phase-2 addition once the base model is proven.
- File upload validation (type/size limits); consider a virus-scan step (e.g. ClamAV container) before a file is marked available.
- Secrets via environment variables locally, Azure Key Vault (or equivalent) in prod — never in source control.
- Rate-limit the AI/chat endpoints separately from CRUD endpoints (LLM calls are the expensive, abusable surface).

---

## 9. Background Processing & Scalability

- Hangfire handles ingestion jobs from day one — dashboard included, no extra infra.
- If ingestion volume grows: swap Hangfire's storage to a distributed backend or graduate to a dedicated worker service behind a message queue (RabbitMQ / Azure Service Bus) — the job *logic* doesn't change, only where it runs.
- API and worker scale independently (stateless API instances behind a load balancer; horizontally-scaled worker pool).
- pgvector is fine well into the low-millions of chunks; if you outgrow it, the `IVectorStore` abstraction (analogous to `IKnowledgeSource`) is where you'd swap in Qdrant/Azure AI Search without touching the RAG orchestrator.

---

## 10. Monitoring & Logging

- **Serilog** for structured logs everywhere (console in dev, sink to Application Insights / Loki in prod).
- **OpenTelemetry** tracing spanning: API request → retrieval → LLM call, so you can see exactly where latency or failures happen in a RAG request.
- Health-check endpoints (`/healthz`) for DB, blob storage, and LLM provider connectivity.
- Track and log: ingestion failures, LLM error rates, average retrieval relevance (if you add user feedback/thumbs later).

---

## 11. Deployment Architecture

**Local development** (docker-compose):
- Angular dev server (or containerized build)
- ASP.NET Core API container
- PostgreSQL + pgvector container
- **Azurite** container (local Azure Blob Storage emulator — same SDK as prod)
- Hangfire dashboard (bundled in API container)

**Production:**
- Containers built in CI, pushed to a registry
- Deployed to Azure App Service / AKS (or equivalent) — API, worker, and frontend as separate deployable units
- Managed PostgreSQL (Azure Database for PostgreSQL, with pgvector enabled)
- Real Azure Blob Storage (same `IFileStorage` implementation as local, just pointed at real Azure credentials instead of Azurite)
- CI/CD via GitHub Actions: lint → test → build → containerize → deploy, gated by passing tests

---

## 12. Implementation Roadmap

| Phase | Scope |
|---|---|
| **1. Core document management** | Upload, folder CRUD, metadata, preview — no AI yet. Prove out the storage/DB layer. |
| **2. Ingestion + search** | Text extraction, chunking, embeddings, hybrid search (no chat UI yet — just a search results page). |
| **3. AI chat assistant** | RAG over uploaded docs only, with citations. This is the core value proposition — get it right before adding repo search. |
| **4. MCP abstraction** | Build `IKnowledgeSource`/`CompositeKnowledgeSource`, ship with a stub repository source; hybrid ranking across sources. |
| **5. Auth & security hardening** | Real auth (Identity → Azure AD), roles, rate limiting, secrets management. |
| **6. Deployment pipeline** | Dockerize everything, GitHub Actions CI/CD, deploy to a real environment, add monitoring. |
| **7. Real MCP integration + scale** | ✅ MCP client built on the official SDK, behind `RepositoryProvider: mcp`; citations resolve outside the hub. Remaining: point it at the org server, and revisit vector store choice if volume demands it. |

---

## 13. Design Patterns & Practices Used Throughout

- **Controller → Service → Data Access**, matching the org's existing convention: Controllers stay thin (ViewModels only, no logic), Services hold business logic and convert ViewModel → DTO, Data Access handles EF Core/PostgreSQL persistence.
- **Integrations layer as a DAL sibling** — `ILlmProvider`, `IEmbeddingProvider`, `IVectorStore`, `IKnowledgeSource`, `IFileStorage` are called from the Service layer through interfaces, exactly like DAL repositories are, keeping every "might change later" dependency swappable without touching Service code.
- **Repository pattern** over EF Core for the relational data, keeping persistence details out of the Service layer.

---

*This document is meant to seed your Claude Code `CLAUDE.md` once you're set up — copy the tech stack, folder conventions, and phase-1 scope into it so Claude Code starts with real project context instead of guessing.*
