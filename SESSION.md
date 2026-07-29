# SESSION.md — current state

Session state only. Architecture, conventions, design decisions and workflow
live in `CLAUDE.md` and are not repeated here.

**Last updated:** 2026-07-30 · **Branch:** `main`, clean and pushed · **Tests:** 143 green
(8 Integrations · 17 Api · 14 DataAccess · 104 Services)

---

## Where the project stands

**v1 is complete.** Roadmap phases 1–6 are done, tested and pushed:

1. ✅ Core document management — folders, upload, metadata, versioning, preview
2. ✅ Ingestion + hybrid search — extract → chunk → embed → index, RRF over keyword + vector
3. ✅ Grounded assistant — streaming SSE, verified citations, refusals, session history
4. ✅ `IKnowledgeSource` abstraction — composite fan-out, per-source deadlines, `/sources` screen
5. ✅ Auth — Identity cookie, Admin/Editor/Viewer, admin user management, optional Google, rate limiting
6. ✅ Deployment pipeline — container images, GitHub Actions CI, single-site IIS artefact

Plus, after phase 6: the repository source address is editable from the UI, an
activity trail backs the dashboard feed, and roadmap phase language has been
removed from every screen.

**Phase 7 — the real MCP client — is the only roadmap item left, and it is NOT done.**
See "Next steps" for exactly what remains.

---

## In progress

Nothing. Working tree clean, everything pushed.

---

## Blockers

- **Phase 7 needs an MCP server to point at.** Everything else it depends on
  exists. The org's server is reachable only from inside the org network, which
  is the shape the stub was built for: the Mac keeps `RepositoryProvider: "none"`,
  the IIS box gets the real one, and config decides.
- **Nothing blocks anything else.**

---

## Known issues

**Behaviour**
- Local generation is slow — roughly 5–15 tok/s on CPU. A GPU is the single
  biggest difference to how the assistant feels.
- Citation reliability after the `num_ctx` fix has not been re-judged. That fix
  meant the 3B model could see all six passages for the first time; whether it
  is still the weak link is an open question. `llama3.1:8b` and `qwen2.5:7b`
  follow the format better if it is.
- `Cited in answers` on the document detail screen is always 0 — never wired to
  chat citations.

**Unproven, not unbuilt**
- **IIS has never run this.** The single-site arrangement was verified by
  publishing and running that output under Kestrel, which exercises static
  serving and the anonymous SPA fallback but **not** `AspNetCoreModuleV2` or
  `web.config` — where the SSE buffering and upload-limit settings live.
- **Google sign-in has never talked to Google.** The domain allow-list has 17
  unit tests; the OAuth round trip has had none.
- CI is green on every push, but no step deploys anything: images are built and
  pushed nowhere, and a human installs the IIS artefact.

**Deferred, no phase assigned**
- Entra ID single sign-on — one more branch in `AddDocHubAuthentication`.
- Anthropic/Claude `ILlmProvider` — one class plus a branch in `AddIntegrations`.
  `LlmOptions.Provider` validates `ollama` only. The SDK dependency was declined.
- OCR for scanned PDFs; client-side unit tests.
- CSRF rests on `SameSite=Lax` plus a JSON content type; antiforgery tokens were
  not added. Worth revisiting if a form-encoded endpoint appears.
- CI warns that `actions/checkout@v4` and friends target the deprecated Node 20;
  fixed by bumping those actions when newer majors land.

**Environment**
- **Git identity was never configured**, so every commit up to 2026-07-30 is
  attributed to `Anush S <anushs@Anushs-MacBook-Air.local>` — a synthesised
  hostname address GitHub cannot link to the account. Set `user.name` and
  `user.email` to fix it going forward; existing commits would need a history
  rewrite and a force-push, which has not been done.
- Dev database carries leftovers from verification: a `vera@dochub.local` Viewer
  (password `viewer-local-dev-pw`), an "Activity Demo" folder, and ~5 test
  documents (`vpn-guide.md`, `runbook.pdf`, `expense-policy.docx`, duplicates).
  All harmless; delete if unwanted.

---

## Next steps

### 1. Deploy to the org Windows machine

The unproven half of phase 6, and the highest-value thing left. Follow the
**Hosting on the org Windows machine (IIS)** section of `README.md` — it is a
complete runbook covering Portainer, the app pool, environment variables and the
provisioning commands.

Two settings there are load-bearing and easy to miss: `Authentication__KeyPath`
(without it every recycle signs everyone out) and the app pool's
`loadUserProfile`.

### 2. Phase 7 — the real MCP client

What exists: the `IKnowledgeSource` contract, the composite with deadlines and
failure isolation, an inactive stub, and a UI-editable address with a
reachability probe.

What does **not** exist: any code that speaks MCP. `NullRepositoryKnowledgeSource`
returns `KnowledgeSearchResult.Empty`, and `RepositoryProvider: "mcp"` is still
rejected at startup. Setting an address in the UI stores it and changes nothing
at question time.

Remaining work, in order:

1. **Decide the citation target for a non-document source.** `KnowledgeResult` is
   document-shaped because a persisted `Citation` deep-links to
   `/docs/:id?chunk=n`; a file at a commit has no document id. Plan: thread an
   optional `SourceName` + `Url` through `KnowledgeResult` → `RetrievedPassage`
   → `Citation` → `CitationViewModel` → `citation-text.ts`, rendering an external
   link when `Url` is set. Citations are `jsonb`, so **no migration is needed**,
   and historical answers keep working because theirs carry no `Url`.
2. **Implement `McpKnowledgeSource`** in Integrations. It takes its address from
   `IRepositorySourceSettings`, not `IOptions`, so an admin changing it takes
   effect on the next question. The servers are open on the org network, so no
   credential handling is needed.
3. **Allow `"mcp"`** in `KnowledgeSourceOptions` validation and register the
   client in place of the stub.
4. **Add an MCP health check** alongside the embedding and LLM ones, and have
   `CheckStatusAsync` return `Unavailable` with a reason.
5. **Verify from the IIS box**, since the Mac cannot reach the server — a real
   outage is the first genuine test of the failure isolation.

MCP is a standard protocol (JSON-RPC 2.0, `initialize` / `tools/list` /
`tools/call`), so most of this can be written against the spec. What cannot be
guessed is which tool means "search" on that server and what its results look
like.

### 3. Smaller, whenever

- Re-judge answer quality on `llama3.2:3b` now that the context window is right;
  upgrade the model only if it is still weak.
- Revisit vector-store scale — HNSW parameters and whether pgvector still fits.
- Wire the `Cited in answers` counter.
- Pick a registry if images should be pushed.

---

## Open questions

- When to add Entra ID, and whether Google sign-in stays alongside it.
- Whether a per-source toggle ("answer without repositories this time") is worth
  having — the composite currently searches every registered source.
- Whether to build the Anthropic `ILlmProvider` now or when Claude becomes the
  default.

---

## Edge cases already handled — do not re-solve

Empty retrieval refusal · fabricated citation markers · vector-branch outage
degrading to keyword-only · `DbContext` concurrency across search branches · SSE
validation before headers · unresolved markers rendered as plain text · a
knowledge source failing mid-question · a source that never replies · duplicate
passages from two sources · an empty query reaching the composite · account
enumeration via the login form · open redirect on `returnUrl` · a cookie whose
user was deleted mid-session · an admin demoting themselves to nobody · the SSE
`fetch` path missing the session cookie · Data Protection keys lost on restart ·
the row action menu clipped on the last list item.

---

## Context recovery prompt

> Read `CLAUDE.md` then `SESSION.md` at the repo root before doing anything else.
> This is DocHub — an ASP.NET Core + Angular documentation hub with local-Ollama
> RAG. **v1 is complete**: roadmap phases 1–6 are done, tested and pushed to
> `main` — do not re-analyse or rebuild them. All 143 tests pass. `CLAUDE.md`
> holds the architecture, design decisions, conventions and workflow and is
> authoritative; `SESSION.md` holds current state. The only roadmap item left is
> **phase 7, the real MCP client, which is not built** — no code speaks MCP yet,
> despite the address being UI-editable. Follow "Next steps" in `SESSION.md`.
> Respect the grounding, security and layering rules in `CLAUDE.md`. Build and
> test before each commit, and commit + push each finished chunk to `main`
> without asking.
