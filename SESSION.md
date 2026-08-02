# SESSION.md — current state

Session state only. Architecture, conventions, design decisions and workflow
live in `CLAUDE.md` and are not repeated here.

**Last updated:** 2026-08-02 · **Branch:** `main`, clean and pushed · **Tests:** 205 green
(17 Api · 42 Integrations · 17 DataAccess · 129 Services) · **CI:** green

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

**Since phase 7**, several smaller pieces landed:

- Both Ollama calls send `keep_alive` (default 30m), and every answer logs
  Ollama's own load / prompt-eval / generation split — the only way to tell
  reading the prompt from writing the answer on a deployed box.
- A folder can be deleted from the tree, behind a confirm dialog naming what
  goes with it.
- Naming a new folder happens in a real dialog, not `window.prompt`.
- Ingestion status updates on screen: while anything is `pending` or `indexing`
  the library re-reads every 2.5s, and stops when the tab is hidden.
- `Cited in answers` on the document detail screen is a real count now — a jsonb
  containment query over stored citations, behind a `jsonb_path_ops` GIN index.
- An over-long heading no longer fails a whole document: section refs and
  failure reasons are clamped to their columns before they reach Postgres.
- **Repository servers are added from the UI**, not from `appsettings`. The
  `repository_source_settings` table *is* the list; `IKnowledgeSourceCatalog`
  resolves sources per request, so a server added on **Knowledge sources** is
  searched by the very next question with no restart. Name, display name,
  address, tool and on/off are all editable; a server can be removed. There is
  no per-server description: the display name tells two servers apart, and the
  status line under it already names the address and the tool. `KnowledgeSources:Repositories` is gone — the only repository
  setting left in configuration is `RepositoryProvider`, the deployment's
  own on/off.
- **An answer that cites nothing is now refused.** A source returning "no
  matches" as prose counted as a retrieved passage, so the empty-retrieval
  guard did not fire, and `llama3.2:3b` answered a Python question from its own
  training with no citations — which was then shown. Found by asking it to
  reverse a number in Python.
- **Search tools are called with the arguments they declare.** We hard-coded
  `query`/`maxResults`; a server taking `q` ignored both and searched for the
  empty string. Read off the tool's input schema now.
- **Users can change their own password** from Settings, with the current one
  required and 5 attempts per 5 minutes. Verified end to end against a
  throwaway database: wrong current and too-short new are both refused with the
  real reason, the session survives the change, the old password stops working,
  and the sixth attempt is a 429.
- **The sources screen renders immediately.** It took ~3s with two remote MCP
  servers configured, because every status is a handshake with a server
  somewhere else (measured: 2.2–3.1s). Split into `/api/sources` (2–35ms, draws
  the page) and `/api/sources/status` (unchanged, fills the badges).
- **Two more fabrication holes closed**, both found by asking off-topic
  questions. Retrieval had no relevance floor, so "how to make orange juice"
  retrieved the nearest chunks however far (measured 0.54+ against 0.31 for a
  real match) — there is now a configurable cosine floor for the assistant
  only. And citation checking was purely syntactic, so a model could invent an
  answer and hang resolving markers off it; a citation must now share wording
  with the sentence citing it, beyond what the question itself supplied.
- **"Nothing matched" is no longer a passage.** The live-score server replies
  `Search results for '…': {"result": []}` — prose wrapping JSON, so it never
  parsed, so the whole blob became a citable passage. That single junk passage
  is what defeated the empty-retrieval guard in the first place. The envelope is
  now recognised (including `result`, and embedded in a sentence) and an empty
  one yields nothing; entries in an unfamiliar shape still pass through, so real
  results are never dropped.
- **Test address speaks MCP**, not HTTP: it reports the server's tool list,
  which repositories it says it indexes, and which tool searching would pick,
  with a button to fill that field in. It tells "answered but not MCP" apart
  from "nothing listening", and warns when a reachable server exposes nothing
  searchable. Only `search`-style tools are usable — `get_answer` and the
  analysis tools return no passage a citation could point at.

**It is deployed.** As of 2026-08-01 the site runs on the org Windows machine
under IIS: people sign in and the assistant streams — the half of phase 6 that
had only ever been simulated. Two of its settings still have not been exercised
there; see "Unproven, not unbuilt".

---

## In progress

Nothing. Working tree clean, everything pushed.

---

## Blockers

- **The MCP client has never spoken to the org's servers.** It is tested against
  a real MCP server hosted in-process, so the protocol path is exercised. The
  org runs two — `mcp-cs` and `mcp-impl`, each exposing the same 13 tools — and
  the hub now searches both as separate sources. What is still unsettled:
  - ~~**The tool's input schema.**~~ Settled generically: arguments are now
    read off the tool's own schema, so `search_codebase` is sent whatever it
    says it takes. **Test address** reports the tool list, so confirming the
    name is one click.
  - **Whether it returns verbatim source.** `ReadResults` already accepts three
    output shapes, so the shape is the forgiving half. The text being the file
    verbatim rather than a summary is the contract that matters, and it cannot
    be detected from our side. **`get_answer` is the wrong tool for exactly this
    reason** — a synthesized answer would have the assistant quoting text that
    exists in no file.
  - **No authentication.** HTTP with no credentials. The SDK transport exposes
    `AdditionalHeaders` and `OAuth`; neither is wired. A server behind a bearer
    token will not connect.
  - **HTTP transport only.** A stdio server would need a different transport.
  `RepositoryProvider` now defaults to `"mcp"`, so adding a server in the UI
  is enough on any machine; the Mac simply has none added.
- **Nothing blocks anything else.**

---

## Known issues

**Behaviour**
- Local generation is slow — roughly 5–15 tok/s on CPU. A GPU is the single
  biggest difference to how the assistant feels.
- **`llama3.2:3b` is the weak link and the decision is taken** — see Next steps
  item 1. It writes uncited sentences beside cited ones and invents reference
  lists, which no amount of post-hoc verification can repair. Four guards now
  sit between it and the screen; the fifth fix is a better model.

**Unproven, not unbuilt**
- **IIS runs it, with two settings still untested.** The site was deployed to
  the org Windows machine on 2026-08-01. Signing in works and the assistant
  streams word by word, so `AspNetCoreModuleV2`, the single-site arrangement,
  the SPA fallback and `responseBufferLimit="0"` are all proven there. Not yet
  exercised, each failing quietly rather than loudly:
  - **The 25 MB upload limit** (`web.config`) — upload something over ~30 MB and
    check the refusal comes from the API, not from IIS.
  - **Data Protection key persistence** (`Authentication__KeyPath`) — recycle the
    app pool and see whether the session survives.
- **Google sign-in has never talked to Google.** The domain allow-list has 17
  unit tests; the OAuth round trip has had none.
- No CI step deploys anything: images are built and pushed nowhere, and a human
  installs the IIS artefact.
- **The container build was red from 2026-07-30 to 2026-08-02** — thirteen runs
  — and nobody noticed, because `dotnet build`/`dotnet test` pass locally and
  the publish workflow kept succeeding. Only the Docker build was affected.
  Worth remembering: local green is not CI green, and this repository has a job
  that only runs on the server.

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

### 1. Move the answer model to `qwen2.5:7b` — decided

The assistant still fabricates, and four grounding guards have not stopped it
because the problem is upstream of them: `llama3.2:3b` does not follow the
instruction it is given. Asked "arsenal" it produced one cited sentence, three
uncited ones from its own training, and an invented **References** block naming
Wikipedia and BBC Sport — sources that exist nowhere in this system.

```bash
docker compose exec ollama ollama pull qwen2.5:7b
```

Then set `Llm:Model` to `qwen2.5:7b` and restart the API. Nothing is re-indexed:
generation and retrieval are separate, and the embedding model is untouched.

- **Size** 4.7 GB, against 2.0 GB for `llama3.2:3b` (Ollama registry, Q4_K_M).
  Keep both while comparing; `ollama rm llama3.2:3b` afterwards.
- **Speed** roughly 2–2.5× slower on CPU, so a 10s answer becomes 20–25s.
  `Llm:KeepAlive` is already 30m, so the larger load is paid once.
- **Why this one** slightly smaller and faster than `llama3.1:8b`, which is the
  fallback if it still misbehaves.
- **How to judge it** re-ask the two questions that exposed the fabrications:
  "how to make orange juice" should refuse outright, and "arsenal" should either
  answer only from the live-score passage or refuse. An answer with uncited
  sentences or an invented reference list means the model is still the problem.
- **The real fix for speed** is Ollama natively rather than in Docker: Docker on
  macOS cannot reach Metal, so it is CPU-only whatever the model. See
  "Answers take tens of seconds" in `README.md`.

### 2. Finish proving the IIS host

It is deployed, people can sign in, and answers stream. What is left is the two
checks under **Unproven, not unbuilt** above — a large upload, and a session
across an app-pool recycle. Neither needs a code change if it passes; each has a
one-line fix in `web.config` or the environment if it does not.

The **Hosting on the org Windows machine (IIS)** section of `README.md` is the
runbook, including where those settings live.

### 3. Point the MCP client at the org's two servers

Adding them is now a UI action, and the client adapts to whatever they declare.

1. **Add both on Knowledge sources.** No configuration step —
   `RepositoryProvider` defaults to `mcp`. Set the search tool to
   `search_codebase` on each: it is the one tool of the 13 with "search" in its
   name, so discovery would find it anyway, but naming it stops a future
   `search_docs` quietly taking over. **Test address** reports the live tool
   list before you save.
2. ~~Check the argument names.~~ Handled: arguments are read from the tool's own
   input schema, so a tool taking `q` is sent `q`.
3. **Confirm `search_codebase` returns verbatim source text**, not summaries.
   The assistant cites what it is handed, so a summarising server would have it
   quoting text that exists nowhere. This cannot be detected from our side.
   Do **not** point it at `get_answer` for the same reason.
4. **Add authentication if the servers need it.** `HttpClientTransportOptions`
   takes `AdditionalHeaders` and `OAuth`; a bearer token is a small change plus
   somewhere to keep the secret.
5. **Verify from the IIS box**, since the Mac cannot reach them — a real outage
   is the first genuine test of the failure isolation, which names the missing
   source on the answer itself, and now names *which* of the two it was.

### 4. Undecided, raised and not acted on

- **The search screen does not search MCP sources.** Measured: "arsenal" gives
  the assistant a passage and the Search screen zero results. Search is
  documents-only because its results link to `/docs/:id?chunk=n` and an MCP
  passage has no document — but the Sources screen presents those servers as
  sources, so the split surprises people.
- **A sentence-level grounding guard**: refuse when most sentences carry no
  verified marker. Deferred deliberately in favour of changing the model first —
  a fourth guard on a model that ignores its instructions is the wrong layer.
- **Every question pays an MCP handshake per server** (~2–3s each, measured, in
  parallel). Fixing it means caching connections, which reverses the deliberate
  "a client per operation" decision.
- **Reset a forgotten local password.** `sanush@carestack.com` was requested but
  is not in the local database, which holds `dev@dochub.local`,
  `admin@documenthub.local`, `vera@dochub.local` and `anush@test.com`.
- Revisit vector-store scale — HNSW parameters and whether pgvector still fits.
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

Empty retrieval refusal · fabricated citation markers · an answer that cites
nothing verifiable · a citation whose passage says nothing about the sentence ·
a source echoing the query back as if it were agreement · a retrieved passage
that is merely the nearest of many bad ones · a server's "nothing matched" read
as content · vector-branch outage
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

> Read `CLAUDE.md` then `SESSION.md` at the repo root before doing anything
> else. This is Document Hub — an ASP.NET Core 10 + Angular 22 documentation
> hub with local-Ollama RAG, deployed on the org's Windows machine under IIS.
> `CLAUDE.md` is authoritative for architecture, design decisions, conventions
> and workflow; `SESSION.md` is current state. **v1 and phase 7 are complete —
> do not re-analyse or rebuild them.** 205 tests pass, `main` is clean and
> pushed, and CI is green.
>
> **Start with "Next steps" item 1: move the answer model to `qwen2.5:7b`.**
> That decision is already made — pull it, set `Llm:Model`, restart, then judge
> it by re-asking "how to make orange juice" (must refuse) and "arsenal" (must
> answer only from the retrieved passage, or refuse). The reason is in that
> section: `llama3.2:3b` writes uncited sentences and invents reference lists,
> and four grounding guards have not fixed what is a model problem.
>
> Respect the grounding, security and layering rules in `CLAUDE.md` — in
> particular that an answer is refused rather than shown when it cannot be tied
> to retrieved passages. Verify with `dotnet build`, `dotnet test` and a client
> typecheck before each commit, commit and push each finished chunk to `main`
> without asking, and **check CI after pushing** — the container-image job runs
> only on the server and was red for thirteen runs without anyone noticing.
> Docker must be up: `docker compose up -d --wait`.
