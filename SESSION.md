# SESSION.md — current state

Session state only. Architecture, conventions, design decisions and workflow
live in `CLAUDE.md` and are not repeated here.

**Last updated:** 2026-08-01 · **Branch:** `main`, clean and pushed · **Tests:** 156 green
(17 Api · 17 Integrations · 14 DataAccess · 108 Services)

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

**Phase 7 — the real MCP client — is built.** `McpRepositoryKnowledgeSource`
speaks MCP over the official SDK, `RepositoryProvider: "mcp"` switches it on, and
citations can now resolve outside the hub. What is *not* done is running it
against the org's actual server — see "Blockers".

**Since phase 7**, four smaller pieces landed:

- Both Ollama calls send `keep_alive` (default 30m), and every answer logs
  Ollama's own load / prompt-eval / generation split — the only way to tell
  reading the prompt from writing the answer on a deployed box.
- A folder can be deleted from the tree, behind a confirm dialog naming what
  goes with it.
- Naming a new folder happens in a real dialog, not `window.prompt`.
- Ingestion status updates on screen: while anything is `pending` or `indexing`
  the library re-reads every 2.5s, and stops when the tab is hidden.

---

## In progress

Nothing. Working tree clean, everything pushed.

---

## Blockers

- **The MCP client has never spoken to the org's server.** It is tested against
  a real MCP server hosted in-process, so the protocol path is exercised, but
  three things can only be settled against theirs:
  - **The tool contract is our convention**, not theirs: `query` / `maxResults`
    returning `{ results: [ { path, lines, text, url, score } ] }`. Ask for the
    tool list and schema — that, not the address, is what unblocks the mapping.
  - **No authentication.** HTTP with no credentials. The SDK transport exposes
    `AdditionalHeaders` and `OAuth`; neither is wired. A server behind a bearer
    token will not connect.
  - **HTTP transport only.** A stdio server would need a different transport.
  The Mac keeps `RepositoryProvider: "none"`; the IIS box gets `"mcp"`.
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

### 2. Point the MCP client at the org's server

The client is written and tested; what remains is configuration and one round of
reality.

1. **Get the tool contract**, not just the address. If the server's search tool
   does not take `query` / `maxResults` and return
   `{ results: [ { path, lines, text, url, score } ] }`, adjust `ReadResults` in
   `McpRepositoryKnowledgeSource` — it already accepts structured content, that
   JSON in a text block, or plain prose.
2. **Confirm it returns verbatim source text**, not summaries. The assistant
   cites what it is handed, so a summarising server would have it quoting text
   that exists nowhere. This cannot be detected from our side.
3. **Set `KnowledgeSources__RepositoryProvider` to `mcp`** on the IIS box and put
   the address in from the **Knowledge sources** screen, or in
   `KnowledgeSources__RepositoryEndpoint`. Name the tool in
   `KnowledgeSources__RepositoryToolName` once known — discovery picks the first
   tool with "search" in its name, which is a guess.
4. **Add authentication if the server needs it.** `HttpClientTransportOptions`
   takes `AdditionalHeaders` and `OAuth`; a bearer token is a small change plus
   somewhere to keep the secret.
5. **Verify from the IIS box**, since the Mac cannot reach the server — a real
   outage is the first genuine test of the failure isolation, which now names the
   missing source on the answer itself.

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
> `main` — do not re-analyse or rebuild them. All 156 tests pass. `CLAUDE.md`
> holds the architecture, design decisions, conventions and workflow and is
> authoritative; `SESSION.md` holds current state. **Phase 7, the real MCP
> client, is built and tested against an in-process MCP server** — what remains
> is pointing it at the org's server, whose tool schema and auth are unknown.
> Follow "Next steps" in `SESSION.md`.
> Respect the grounding, security and layering rules in `CLAUDE.md`. Build and
> test before each commit, and commit + push each finished chunk to `main`
> without asking.
