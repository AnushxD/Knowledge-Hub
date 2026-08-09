# SESSION.md — current state

Session state only. Architecture, conventions, design decisions and workflow
live in `CLAUDE.md` and are not repeated here.

**Last updated:** 2026-08-09 · **Branch:** `main`, clean and pushed ·
**Tests:** 214 green (17 Api · 42 Integrations · 21 DataAccess · 134 Services) ·
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

---

## Next, in the order that would make sense

1. Run it against the org's GitLab with a real token and a real `SubPath`.
2. Configure the push webhook and confirm a real delivery lands.
3. Check the CI run and fix whatever the runner does not have.
4. Redeploy the IIS box onto `documenthub_v2` and re-verify sign-in, streaming
   and a first sync there.
