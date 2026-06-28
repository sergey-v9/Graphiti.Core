# Documentation Hygiene

The `.agents/notes/` and `.agents/plans/` files are **living current-state**, not an audit log. They are
loaded into agent context every session, so their size is a recurring cost. They drift toward bloat
because edits **append** and almost never **remove**. This note is the procedure that counteracts that:
summarize and prune on a cadence, trusting git history (and `evolution.md`) as the durable archive so the
working notes stay lean.

## Core principle

Nothing is ever truly lost: **git history holds every past version**, and `evolution.md` holds the
milestone record. So the working notes are free to be aggressively *current* — keep what an agent needs
**now**, and delete (after lifting anything still durable into its proper home) what has become history.
Prefer **replacing** stale text over appending next to it.

## File roles and soft size budgets

A budget is a **trigger**, not a hard limit: exceeding it means "run a compaction pass before adding
more," never "truncate blindly."

| File | Role | Soft cap |
|---|---|---|
| `handoff.md` | current state + how to pick work — **no changelog** | ~300 lines |
| `decisions.md` | durable decisions (decision + short rationale); mark/remove superseded | ~400, prune-not-grow |
| `roadmap.md` | forward plan; active stream in detail, finished phases as one-liners | ~250 lines |
| `parity.md` | Python-parity ground truth — the matrix + legend + anchor, not narrative | matrix only |
| `evolution.md` | milestone history (the archive other files shed into); fold old eras into era-summaries | ~300 lines |
| `kuzu-driver-port.md` | provider current state; collapse dated rechecks to current status | ~200 lines |
| `.agents/plans/NN-*.md` | work orders; active plan in full, **completed plan → short DONE stub** | ~25 lines once DONE |
| `commit-policy.md`, `upstream-sync-procedure.md`, `doc-hygiene.md`, `llm-boundary-risk-map.md` | stable procedure/reference — edit in place | stable |

## When to compact (triggers)

1. **Every edit:** replace stale guidance in place; do not append a duplicate or contradicting paragraph.
2. **On stream/plan completion:** collapse its per-slice detail in `handoff.md` to a one-paragraph
   headline; reduce the completed plan file to a DONE stub; record the milestone in `evolution.md`.
3. **Over budget:** a file past its soft cap → compact it before adding new content.
4. **Hygiene checkpoint:** at the start of each new stream (and whenever the notes feel heavy), scan all
   notes for stale content and compact. Cheap; do it routinely.

## What to drop vs keep

**Drop / summarize away** — after lifting any still-load-bearing fact into its proper home:
- Per-slice / per-commit changelog ("X slice complete (date): …") — git history owns this.
- Dated rechecks that are now just current state ("as of <date>, still true…") — state it once, undated.
- Superseded decisions, resolved TODOs, executed plan steps, "PENDING"/"in progress" notes for finished work.
- A fact already stated in another note — cross-reference it instead of copying.

**Always keep:**
- Active constraints and security/workflow rules; the current state and the work pointer.
- Durable decisions + short rationale (`decisions.md`); the parity matrix (`parity.md`).
- Unresolved questions and open risks.

## The compaction procedure (docs-only; safe)

1. Pick a file; recall its role + budget from the table above.
2. Find candidates (the "drop" list). For each, ask: is any fact still load-bearing? If yes, write **one
   line** of it in its proper home (`decisions.md` durable / `parity.md` mapping / `evolution.md`
   milestone), then delete the verbose original. If no, just delete.
3. Re-read the trimmed file end-to-end — it must still stand alone for a fresh agent.
4. Review the `git diff`: confirm every removed durable fact is summarized elsewhere or safely in git
   history. No code runs; this is docs only.
5. Commit as a dedicated `docs: hygiene` commit, separate from functional work, naming what was compacted.

Keep this note itself lean — it is an example of its own rule.
