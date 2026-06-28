# Upstream Python sync — repeatable procedure

How to chase the upstream Python `graphiti_core` and fold its improvements into the C# port. Run this
whenever we want to pull newer upstream work. The last execution (2026-06-14, anchor `34f56e6` →
`origin/main` `0ed90b7`) is the worked example; its dispositions live in `parity.md`
("upstream sync") and `decisions.md` ("Deliberate divergences from the … upstream sync").

> **Parent-repo context.** This C# port is a git submodule of a local-only checkout of upstream
> `getzep/graphiti`. The parent keeps `main` as a pristine mirror of `origin/main` and carries our
> submodule hookup as a single commit on a local `csharp-port` branch (never pushed). This sync
> procedure is the **inner loop** of refreshing that branch onto a newer upstream: see the parent repo's
> `FORK_MAINTENANCE.md` for the outer wrapper (fetch → spot a `graphiti_core` delta → run this → move the
> branch forward). The current sync anchor is recorded in `parity.md`.

## Standing policy (decided 2026-06-14 with Sergey)

- **We track `origin/main` HEAD, not release tags.** Move straight to the latest commit on
  `getzep/graphiti` `main`; do not wait for a tagged version.
- **We mirror `graphiti_core/` only** — the library. `server/`, `mcp_server/`, CI, docs, examples,
  and dependency bumps are out of scope (we don't port them). The C# parity contract is against the
  Python *library* source.
- **The C# primary provider is LadybugDB** (a maintained Kuzu-lineage engine). The C# port mirrors the
  Python *library's* behavior — historically realized through its Kuzu driver path; as Kuzu is deprecated
  upstream we mirror library *features* regardless of which provider now delivers them (see the two
  bullets below). InMemory is the deterministic reference/test backend. Neo4j was removed 2026-06-17 and
  is no longer a C# provider; FalkorDB / Neptune are enum/wire-compat only (no real C# driver). Python
  upstream has deprecated Kuzu and made FalkorDB primary; **we deliberately diverge** — we keep LadybugDB
  primary and do not echo the Kuzu deprecation.
- **We adopt upstream FEATURES by meaning, even when they arrive on the FalkorDB driver.** Upstream has
  deprecated Kuzu and made FalkorDB its primary backend, so new library *capabilities* will increasingly
  land FalkorDB-flavored and may have **no Kuzu path to mirror**. We still want the capability: realize
  the same *observable behavior* on our LadybugDB driver (and the InMemory reference), implementing it
  however the LadybugDB engine supports — possibly differently or more exotically because the engines
  differ. Equivalence is judged by **behavior / wire shape, not code shape**. The gate is "is this a
  library capability we want?", **not** "does Python apply it to Kuzu?" (which stops being a useful test
  as Kuzu fades upstream). Record it in `parity.md`, and in `decisions.md` if the mechanism diverges.
- **But do NOT port engine-protocol quirks.** Provider plumbing that exists only because of that engine's
  wire protocol — RediSearch token escaping, Redis NUL-byte stripping, FalkorDB-specific query syntax —
  carries no feature meaning for LadybugDB; skip it and record N/A with the reason. The narrow exception
  is a fix for a bug that *also provably* affects LadybugDB. The test is **feature vs. mechanism**: a
  capability the library now offers (adopt the meaning) vs. a workaround for one engine's protocol (skip).
  See step 4 and `decisions.md` → "Adopting upstream features that arrive on the FalkorDB driver".
- **Pointer move is gated on incorporation.** Only advance the local `graphiti_core` checkout after
  every library change in the delta is incorporated, dispositioned, and the C# suite is green.
- **CI — keep as-is, do not expand.** Plan 06 retired the old core-only lane; the remaining full
  verifier lane stays. Do not add or expand CI without a new ask. See "Resolved scope decisions" at
  the top of `roadmap.md`.

## Step 1 — Establish the exact delta

The anchor is the Python commit recorded at the top of `parity.md` ("Python baseline"). From the
parent repo `W:\code\graphiti`:

```
git fetch origin --no-tags
git rev-parse origin/main                                   # the new target HEAD
git log --oneline <ANCHOR>..origin/main -- graphiti_core    # library commits in the delta
git diff --stat <ANCHOR>..origin/main -- graphiti_core      # AUTHORITATIVE completeness check
```

The same check is executable from `W:\code\graphiti\csharp`:

```
.\eng\Check-PythonUpstreamDelta.ps1 -Fetch
```

Use `-FailOnDelta` when a no-library-delta check should fail automation.

For the G5 recurring reminder, schedule the non-blocking wrapper instead of adding a CI lane:

```
.\eng\Invoke-UpstreamDeltaReminder.ps1
```

The wrapper fetches by default, runs the same library-delta check, prints a warning when
`graphiti_core` has upstream changes, and exits `0` for both no-delta and delta cases so the reminder
does not block unrelated work. Unexpected git or script failures still fail the task. Sergey can wire it
as a Windows Scheduled Task, cron entry, or manual dispatch; the command above is the only repository
setup required.

The `git diff --stat … -- graphiti_core` is the source of truth for "what library code changed":
it is the NET file set regardless of how many commits touched it. If every file/hunk in that net
diff is explained by the commits you review, coverage is complete. (Most commits in the full
`<ANCHOR>..origin/main` range touch only `mcp_server/`, `server/`, CI, or deps and never appear in
the `-- graphiti_core` filter — ignore them.)

## Step 2 — Classify each library commit

| Category | What it touches | How to handle |
|---|---|---|
| **Prompts / pipeline / search** | `prompts/`, `nodes.py`, `edges.py`, `search/`, `utils/` ingestion | **Highest risk.** Port the behavior + instruction text faithfully (see Prompt Parity Contract in `decisions.md`). Prefer full-string golden tests. Verify against the Python source, never a summary. |
| **LLM-client SDK mechanics** | `llm_client/openai*`, `azure_openai*`, reasoning/temperature/response_format | Usually **N/A**: the C# port uses the `Microsoft.Extensions.AI` adapter boundary, so OpenAI-SDK request construction is the *consumer's* chat client, not Graphiti. Extract only the portable *semantic* intent (e.g. "empty response is retryable", "strip markdown fences"). |
| **Provider-specific** | `driver/falkordb/*`, `driver/neo4j/*`, `driver/neptune/*` | There is no C# driver for any of them, but the commit can still carry a **feature** (increasingly FalkorDB, now that Kuzu is deprecated). Two cases (step 4): (a) a **capability/behavior** the library now offers → realize the same *semantic* behavior on LadybugDB + InMemory, even if implemented differently; (b) **engine-protocol plumbing** (RediSearch escaping, Redis NUL handling, FalkorDB syntax) → skip, record N/A. A change to a *shared* helper (e.g. `helpers.py`) is in scope. |
| **Kuzu driver** | `driver/kuzu_driver.py` | Mirror genuine behavior changes into the LadybugDB driver. **Reject** the upstream Kuzu *deprecation* — we use the maintained LadybugDB lineage. |
| **Version / docs / tests** | `pyproject.toml`, `__init__` version, docstrings, `_test.py` | Skip (record the version bump only). |

## Step 3 — Incorporate library changes into C#

For each in-scope change: make the C# edit, mirror it in the test, and verify against the Python
source file (not the diff summary). Watch the recurring trap: **agents author both code and golden
tests, so transcription/logic drift passes CI** — verify against Python and prefer full-string
goldens.

## Step 4 — Other-provider adaptation check (do this explicitly)

For every change to a provider we do not invest in (FalkorDB/Neo4j/Neptune), ask **two** questions, in
order:

1. **Is there a portable FEATURE/capability here we want?** A library behavior the change adds or enables
   — even if it lands on the FalkorDB driver and has **no Kuzu equivalent** (Kuzu is deprecated upstream,
   so "Python applies it to Kuzu" is no longer the test). If yes, realize the same *observable behavior*
   on the LadybugDB driver (and the InMemory reference), implementing it however the LadybugDB engine
   allows — possibly differently or more exotically than upstream. Add a test, record it in `parity.md`,
   and note any mechanism divergence in `decisions.md`. This is the path that matters as upstream
   evolution becomes FalkorDB-flavored.
2. **Is it an engine-protocol quirk?** If the root cause is **protocol/engine-specific to that provider**
   (RediSearch treats `_`/`-` as operators; Redis can't carry NUL bytes; FalkorDB vector syntax), it
   carries no feature meaning for LadybugDB → do **not** port; record N/A with the reason. Narrow
   exception: a fix for a bug that *also provably* exists for LadybugDB (a failing LadybugDB runtime
   test / a demonstrable engine limitation) → adapt the fix with a test.
3. When unsure which it is: port the *meaning* if a consumer would observe the capability; record N/A if
   it is invisible engine plumbing. See `decisions.md` → "Adopting upstream features that arrive on the
   FalkorDB driver".

## Step 5 — Verify centrally (never concurrent worktree tests)

From `W:\code\graphiti\csharp`:

```
dotnet build -c Release -clp:ErrorsOnly          # must be 0 warnings (TreatWarningsAsErrors)
dotnet test  -c Release --no-build               # full suite, single central run
dotnet format --verify-no-changes --no-restore   # 0 whitespace/style drift
```

**Hard rule:** never run multiple worktree agents' `dotnet test` concurrently — the LadybugDB native
package deadlocks across worktrees (caused a 1.5h hang once). If you fan out to parallel branches,
agents build/format-only; run the consolidated test + any API-snapshot baseline regen centrally,
single-threaded. If the public surface changed, regenerate the API snapshot baseline (review the
`*.received.txt` diff, then promote over `*.approved.txt`).

## Step 6 — Adversarially verify (ultracode)

Before claiming "fully reconciled", run an adversarial pass: one skeptic per upstream commit that
opens the actual C# code and tries to **refute** each disposition, plus a completeness critic that
re-reviews the net `git diff … -- graphiti_core` for anything missed (especially a portable behavior
dismissed as N/A, or a provider fix that should have been adapted). The
`upstream-sync-audit` Workflow script is the reusable harness for this.

## Step 7 — Record the dispositions

- `parity.md`: add a dated "upstream sync" subsection with a per-commit disposition table; **move the
  "Python baseline" anchor** to the new `origin/main` HEAD sha. Move/reopen any prompt/pipeline rows
  the delta touched.
- `decisions.md`: record every deliberate divergence (with the *why*) and every adopted fix.
- `csharp-port-replan` memory: one-paragraph summary + the new anchor sha.

## Step 8 — Commit the C# work

Commit to the `csharp` submodule `main` (logically-separate commits). Message convention ends with
`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

## Step 9 — Move the parent fork branch forward (gated on steps 3/5 passing)

Only after the suite is green and all library changes are incorporated, refresh the parent repo onto the
new upstream HEAD. The parent `W:\code\graphiti` keeps `main` as a **pristine mirror of `origin/main`**
and carries our hookup (the `csharp` submodule + root-doc pointers) as a **single commit on the local
`csharp-port` branch** — so "advancing the mirror" is just moving that branch onto the new HEAD. Follow
the parent repo's **`FORK_MAINTENANCE.md`** (the outer wrapper this procedure is the inner loop of):
fetch → `git checkout main && git reset --hard origin/main` → recreate the single hookup commit on the
new HEAD with the gitlink pointed at the current `csharp` submodule HEAD. **Never push the parent.**

Record the new `origin/main` sha as `parity.md`'s anchor (step 7); next time, that sha is the new ANCHOR
for step 1. We mirror only `graphiti_core/`; the fork's `server/`, `mcp_server/`, CI, and deps are not
ours to carry (resetting `main` to upstream keeps them at upstream automatically).
