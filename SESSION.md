# SESSION.md — current state

Session state only. Architecture, conventions, design decisions and workflow
live in `CLAUDE.md` and are not repeated here.

**Last updated:** 2026-08-12 · **Branch:** `main`, clean and pushed ·
**Tests:** 224 green (17 Api · 50 Integrations · 21 DataAccess · 136 Services) ·
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

**The local database is still in the bad state** as of this writing: the fix is
committed but the API on port 5080 is running the old build. Restart it and
press **Sync now** to drain the backlog.

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
  gitlab.com anonymously. The token path (`GitLab:Token` in user-secrets,
  `PRIVATE-TOKEN`) is written and unit-covered but has never had a real token
  through it.
- **The webhook has never received a real delivery.** The decision logic is
  tested directly — secret match, wrong branch, non-push event — but no GitLab
  instance has actually called the endpoint.
- **No scheduled sync.** Deliberate: manual plus webhook was the choice. A
  deployment GitLab cannot reach relies on the button.

---

## Known limits, stated rather than hidden

- **One repository per deployment.** Which project a hub contains defines the
  whole installation, so this is configuration, not a table.
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
