# SESSION.md — current state

Session state only. Architecture, conventions, design decisions and workflow
live in `CLAUDE.md` and are not repeated here.

**Last updated:** 2026-09-01 · **Branch:** `main`, clean and pushed ·
**Tests:** 248 green (17 Api · 50 Integrations · 21 DataAccess · 160 Services) ·
**CI:** pushed, not yet checked

---

## Where the project stands

**v1 is released.** Tagged `v1.0.0`, with the branch `release-1` pinned at the
same commit (`007b108`). That is the upload-based hub: everything through
phase 7, the MCP repository client, and the fabrication fixes.

**v2 is built and verified locally.** The requirement changed: the documents
are already in a GitLab repository, so the hub mirrors that repository instead
of accepting uploads. The library is now a read-only reflection of one GitLab
project — the same files, in the same folder structure.

What that meant in practice:

- **Documents are repository files.** A document exists because a file does.
  Its identity is its repository path, enforced unique in the database. Upload,
  add-version, move, delete and every folder mutation are gone from the API,
  the gateway and the UI.
- **Folders are the repository's directories**, reconciled per sync — created
  including intermediate levels, removed when they leave, and never editable
  here.
- **File bytes are fetched from GitLab on demand.** Nothing is copied. Blob
  storage stays wired and health-checked (that was the decision taken) but no
  document path touches it, and `document_versions` is gone — GitLab owns file
  history.
- **Sync diffs on git blob ids**, so an unchanged repository costs one tree
  listing and nothing else. Verified: a second sync straight after a first
  reports 0 added, 0 updated, 0 removed.
- **Triggered by an admin "Sync now" and a GitLab push webhook**, both queued
  through Hangfire. No scheduled poll — that was the choice made.
- **A new database, `documenthub_v2`, on a fresh squashed migration chain.**
  V1's is untouched and `documenthub-schema.sql` is frozen beside the new
  `documenthub-v2-schema.sql`, which is now what the IIS artefact and the
  Docker image ship.

**Verified end to end** against a real GitLab instance (`gitlab.com`, the
`gitlab-org/gitlab-foss` project's `doc/development` directory, read
anonymously):

- 636 documents across 101 folders from a tree of 761 files, listed in ~19s
- 125 files of unreadable types counted as skipped, not mirrored as failures
- the folder tree on screen matches the repository's nesting exactly
- a document preview renders Markdown streamed live from GitLab, with its
  path, revision and last-synced time in the sidebar
- a second sync: 0 added, 0 updated, 0 removed
- `/healthz` healthy on all checks, including the new `repository` one

**Two bugs were found by doing this, both fixed:**

1. **The library emptied itself in silence.** Folder reconciliation ran before
   the document diff, so a directory leaving took its documents by cascade —
   nothing counted, nothing named in the activity trail. Departures are now
   settled first. Caught by `ActivityTrailTests`.
2. **A queued sync starved behind its own backlog.** The first sync queues one
   ingestion job per document; on the single Hangfire queue with two workers,
   the next sync sat unstarted for as long as the backlog took, while the
   screen showed the previous run's numbers as if nothing had been asked for.
   Syncs now have their own queue, drained first. Caught by pressing the button
   twice against 636 documents.

A third, smaller one: `ExecuteDelete` left deleted documents in the change
tracker, so the next `SaveChanges` in the same sync failed on a concurrency
error. They are detached now.

**Since then: an MCP server is searched with all of its tools, not one.** A
question now goes concurrently to every tool a server exposes that is read-only
and takes search text, merged by rank, with search-shaped tools asked first and
winning ties. This reverses a recorded decision — "only a `search`-style tool
can ground an answer" — because it cost answers a server plainly had:
`get_architecture` beside a thin `search_code` contributed nothing. The trade is
recorded in `CLAUDE.md` and on `McpRepositoryKnowledgeSource`; the guard against
its obvious failure mode is `RepositoryToolPlan`, which never calls a tool that
changes something. One tool failing is a degradation named on the reply; all of
them failing is the source failing. Covered by 8 new tests against real MCP
servers hosted in-process, including one whose tools would delete things.

**Not verified through the UI.** The sources screen wording and the "would be
asked" tool chips were built and typecheck, but the screen is admin-only and
signing in was not done in that session, so nobody has looked at them rendered.

**Then: the assistant refused every question, and it was the mirror.** Diagnosed
against the local database rather than guessed at:

- 636 documents mirrored, **21 Indexed**, 2 stranded in `Indexing`, 613
  `Pending`. Only Indexed documents are retrievable, so 97% of the library
  could not answer anything.
- `hangfire.job` empty; `stats:succeeded` 23, all on 2026-08-09. The first
  sync (11:18:37–11:18:51) created all 636 and queued them; 23 ran before the
  process stopped, and the rest never did.
- The sync three minutes later (11:21:41) reported **0 added, 0 updated, 0
  removed, 125 skipped** — correct by the blob-id rule and useless, because
  every stranded document was "unchanged". There was no route back.
- Retrieval itself was healthy throughout, which is why it took looking:
  questions inside the indexed 21 embed to 0.24–0.27 cosine, well inside the
  0.5 floor. The pipeline was fine; the corpus was 3% of itself.

Fixed by re-queueing unchanged files whose document is `Pending` or `Indexing`,
counted and reported as `FilesRequeued` (new column, migration
`RequeuedFileCount`). `Failed` is left alone on purpose.

**What is still not explained:** the ~613 *enqueued* Hangfire rows are gone
rather than sitting unprocessed. Expiry does not remove enqueued jobs, so
something else took them — a recreated volume or schema is the likely
candidate, three days having passed. The re-queue makes it recoverable either
way, which is the property worth having, but it is not the same as knowing.

**An MCP-only answer already worked; it had never been tested.** "The documents
have nothing but the server does" is the case a hub in front of a repository
hits constantly, and every existing test had documents present alongside the
external source. Written now, it passed first run: the answer is shown, cited
`external`, attributed to the server's name, linking out. What was wrong was
the wording next door — a refusal with several sources searched still said
"Nothing in the indexed documents matched… only documents that finished
ingestion are searchable", which sends somebody to index a file when their
repository server had already been asked and had nothing either.

**Two UI items done since.** The "Include subfolders" toggle in Settings was a
decorative span pinned to "on" — removed rather than wired up, on the reasoning
that anyone who may see a folder may see its subfolders, so the control could
only ever hide a reader's own documents from them. Browsing stays recursive with
the reason recorded where the value is set. And the sync counts, which had been
computed and persisted and shown nowhere, now appear on the library header:
"Last synced 9m ago · 2 updated, 1 removed, 3 requeued for indexing, 122 not
indexable", with zero counts omitted and all-zero reading "no changes".

**The local database is still in the bad state** as of this writing: the fix is
committed but the API on port 5080 is running the old build. Restart it and
press **Sync now** to drain the backlog.

---

## Since then: the repository is chosen in the UI

The `GitLab` section is no longer the only way to say which repository the hub
mirrors. `repository_settings` is one row overlaying it, edited by an
administrator under **Settings → Source repository** — instance, project,
branch, sub-path, access token and webhook secret — and in force on the next
call, with no restart.

This reverses a recorded decision. `GitLabOptions` argued the mirror was "a
single deployment-level setting" unlike the MCP servers; repointing it in
practice meant a text editor on the box and an app-pool recycle, which is a
ticket for a five-second decision. Configuration stays the default and the
fallback: a field left blank falls back to it, so a box provisioned by
environment variables is untouched by this.

What it cost, and what was decided:

- **Startup validation had to relax.** It now checks that a value which *is*
  there is well formed, not that one exists — refusing to boot without a
  repository would put the screen that sets one out of reach. No repository is
  a first-run state: `/healthz` degraded, sync refused with a message, and a
  library that says "No repository is configured yet" with the action beside
  it rather than an empty shelf.
- **The two secrets are editable and encrypted at rest** with Data Protection,
  write-only: blank keeps, empty clears, nothing is ever sent back. Leaving the
  token in configuration would have made the screen half a feature — pointing
  the hub at a *private* project is most of the point. The cost is that they
  are only as durable as `Authentication:KeyPath`, and unreadable ciphertext is
  reported as "set it again" rather than as "not set".
- **Settings resolve per call** through `IRepositorySettingsReader` — the
  contract in Integrations, the database-backed overlay in Services, one cached
  snapshot per process refreshed on save and every 30 seconds. A second API
  instance picks a change up on that timer.
- **Test connection speaks to GitLab before saving**, and reports whether the
  sub-path holds any files. A wrong project is an obvious 404; a wrong sub-path
  mirrors nothing and reads as a broken hub, so that is the one worth catching
  while it is still being typed.
- **Saving does not sync.** Changing the project replaces the whole library at
  the next sync, and the form says so before the button is pressed.

**The `GitLab` identity keys are out of both appsettings files.** Nothing
committed says which repository to mirror — `appsettings.json` keeps only
`TimeoutSeconds` and `MaxFileBytes`, which are about this box rather than about
the repository, and `appsettings.Development.json` has no `GitLab` section at
all. A fresh clone therefore starts pointed at nothing, which is exactly the
first-run state the feature was built for; the README's setup order was changed
to match, with choosing the repository now step 9, after there is an
administrator to sign in as. Confirmed by booting against a clean database with
no section present: everything healthy but `repository`, which says it needs
choosing.

**Verified end to end, in a browser, against a throwaway database.** An API
booted with `GitLab:BaseUrl` and `ProjectPath` empty — which the old build
refused to do — reported the repository check as degraded and the library said
so. Pointing it at `gitlab-org/gitlab-foss`, branch `master`, sub-path
`.gitlab/issue_templates` purely through the settings screen, then pressing
**Sync now**, mirrored 96 documents (1 not indexable) which then indexed. The
probe was exercised both ways: a good sub-path reads "Read 'gitlab-org/
gitlab-foss' on branch 'master' anonymously", and `doc/no-such-folder` reads
"holds no files on that branch. The hub would mirror nothing."

11 new Service tests cover the overlay, the three secret states, an unreadable
token after a lost key ring, a rotated webhook secret being the one a delivery
is checked against, and a hub pointed nowhere writing no failed sync.

---

## Since then: a follow-up question found nothing

Reported from the UI: "how to get the Activity Analytics?" was answered and
cited; "can you specify the paths?" — the very next turn — refused. The document
had the paths all along.

Diagnosed against the local database rather than guessed at:

- The cited chunk (`tokens/fine_grained_access_tokens_rest.md`, section
  *Activity Analytics*) holds all three endpoints verbatim. Turn 1 had them and
  wrote "the paths provided" instead, because prompt rule 6 said "Answer in
  prose, briefly" and nothing asked for specifics.
- Turn 2 was searched on its own five words. `ChatService` passed `Query =
  question`; the conversation was replayed to the model and never to retrieval.
- Keyword branch: **0 hits** — `websearch_to_tsquery` ANDs its terms and no
  chunk holds both *specify* and *path*.
- Vector branch: nearest were *Preference* 0.379, *Following* 0.384,
  *Suggestion* 0.387 — inside the 0.5 floor, so the model was handed those.
- The passage that answered the question: **rank 285 of 476, distance 0.5199**,
  outside the floor. The refusal was correct on what was supplied.

Fixed in three parts:

1. `ConversationQuery` — a question under three substantial words is searched
   together with the last two user questions, on the **vector branch only**
   (`SearchRequest.SemanticQuery`, carried through `KnowledgeQuery.SemanticText`
   so MCP sources get the anchored form too). Measured: the same passage goes
   from rank 285 to **rank 1 at 0.2354**.
2. Prompt rule 6 now asks for the specifics — paths, endpoints, commands and
   numbers copied out exactly, listed one per line with a marker each — and
   "answer in prose, briefly" became rule 7, conditional on there being nothing
   concrete to copy.
3. A quoted path must appear in the passage cited for it. Found while verifying
   part 2: the model quoted `POST /projects/:id/cluster_agents/:agent_id/tokens`
   correctly and attributed it to the neighbouring *Cluster Agent* section,
   which shares "cluster" and "agent" and does not contain the path. Word
   overlap is weakest exactly where a copied path is strongest — a list line is
   a short sentence — and rule 6 makes list answers the norm. A marker whose
   path lives in a *different* supplied passage is re-pointed there rather than
   dropped, because refusing would throw away a true answer; surplus markers
   landing on one passage are dropped so one source cannot render as two.

**Verified against the real model**, on a copy of the local library so the
working database was untouched. The reported conversation now answers both
turns from the *Activity Analytics* chunk, listing all three endpoints. A
self-contained change of subject mid-conversation is not anchored and answers
correctly; a thin one ("and vulnerabilities?") is anchored and still answers on
the new subject. 8 new tests.

---

## What is not done

- **Not deployed to the org Windows machine.** v1 is what is running there. The
  IIS section of the README is updated for v2 — new database, new script, the
  `GitLab__*` environment variables — but nobody has run it.
- **CI has not been checked** since the v2 push. Both workflows were updated to
  publish `documenthub-v2-schema.sql`; the server tests need `documenthub_v2`
  to exist on the runner, which the compose init script handles on a fresh
  volume — worth confirming on the first run.
- **Not tried against the org's own GitLab.** Everything so far is against
  gitlab.com anonymously. The token path — in user-secrets or saved in the UI,
  sent as `PRIVATE-TOKEN` — is written and covered by tests but has never had a
  real token through it, so the encrypted-at-rest path has never protected a
  credential that GitLab actually accepted.
- **The webhook has never received a real delivery.** The decision logic is
  tested directly — secret match, wrong branch, non-push event — but no GitLab
  instance has actually called the endpoint.
- **No scheduled sync.** Deliberate: manual plus webhook was the choice. A
  deployment GitLab cannot reach relies on the button.

---

## Known limits, stated rather than hidden

- **One repository per deployment.** Still one, but now chosen in the UI rather
  than only in configuration. Which project a hub contains still defines the
  whole installation — changing it replaces the library at the next sync.
- **A saved change reaches a second API instance within 30 seconds**, not
  instantly: the snapshot is per process and only the instance that saved it
  refreshes immediately.
- **A rename loses hub-local metadata.** A document is identified by its path,
  so a rename is a delete and an add. Description, tags and starring go with it.
- **One sync at a time per process**, on a static semaphore. Sufficient for a
  single box; two API instances against one database would need a database lock.
- **Size is unknown until a file is first fetched.** The tree listing carries
  no size and asking per file would be a round trip per file on every sync, so
  ingestion fills it in and a pending document reports 0 B.
- **Whether a tool changes something is a guess when the server does not say.**
  `readOnlyHint` is optional and widely omitted, so a name check stands behind
  it. It is blunt and biased towards excluding — a read tool called
  `sync_index` will be skipped, and can be named explicitly to force it.
- **A server with many tools costs more per question.** They run concurrently
  under the one source deadline, so the wall clock is the slowest tool rather
  than the sum, but a slow server is now slow on every tool it has.

---

## Next, in the order that would make sense

1. Run it against the org's GitLab with a real token and a real `SubPath`.
2. Configure the push webhook and confirm a real delivery lands.
3. Check the CI run and fix whatever the runner does not have.
4. Redeploy the IIS box onto `documenthub_v2` and re-verify sign-in, streaming
   and a first sync there.
