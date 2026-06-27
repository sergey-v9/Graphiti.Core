# Upstream Python sync — repeatable procedure

How to chase the upstream Python `graphiti_core` and fold its improvements into the C# port. Run this
whenever we want to pull newer upstream work. The last execution (2026-06-14, anchor `34f56e6` →
`origin/main` `0ed90b7`) is the worked example; its dispositions live in `parity.md`
("upstream sync") and `decisions.md` ("Deliberate divergences from the … upstream sync").

## Standing policy (decided 2026-06-14 with Sergey)

- **We track `origin/main` HEAD, not release tags.** Move straight to the latest commit on
  `getzep/graphiti` `main`; do not wait for a tagged version.
- **We mirror `graphiti_core/` only** — the library. `server/`, `mcp_server/`, CI, docs, examples,
  and dependency bumps are out of scope (we don't port them). The C# parity contract is against the
  Python *library* source.
- **The C# primary provider is LadybugDB** (a maintained Kuzu-lineage engine). The C# port mirrors
  Python's **KUZU** driver behavior. InMemory is the deterministic reference/test backend. Neo4j was
  removed 2026-06-17 and is no longer a C# provider; FalkorDB / Neptune are enum/wire-compat only
  (no real C# driver). Python upstream has deprecated Kuzu and made FalkorDB primary; **we deliberately
  diverge** — we keep LadybugDB primary and do not echo the Kuzu deprecation.
- **Other-provider changes get evaluated for adaptation to our primary provider** (see step 4): if a
  fix to a Python provider we do not invest in (FalkorDB/Neo4j/Neptune) reveals a *real latent issue*
  for LadybugDB AND adapting it aligns with Python's own Kuzu behavior + the authors' intent, adapt
  it. Do **not** blindly copy provider-protocol-specifics (RediSearch tokenization, Redis NUL
  handling) that Python itself does not apply to Kuzu.
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
| **Provider-specific** | `driver/falkordb/*`, `driver/neo4j/*`, `driver/neptune/*` | Do not port or improve these provider paths for their own sake — there is no C# driver for any of them (Neo4j was removed 2026-06-17, like FalkorDB/Neptune). Run the step-4 adaptation check against LadybugDB. A change to a *shared* helper (e.g. `helpers.py`) may still be in scope. |
| **Kuzu driver** | `driver/kuzu_driver.py` | Mirror genuine behavior changes into the LadybugDB driver. **Reject** the upstream Kuzu *deprecation* — we use the maintained LadybugDB lineage. |
| **Version / docs / tests** | `pyproject.toml`, `__init__` version, docstrings, `_test.py` | Skip (record the version bump only). |

## Step 3 — Incorporate library changes into C#

For each in-scope change: make the C# edit, mirror it in the test, and verify against the Python
source file (not the diff summary). Watch the recurring trap: **agents author both code and golden
tests, so transcription/logic drift passes CI** — verify against Python and prefer full-string
goldens.

## Step 4 — Other-provider adaptation check (do this explicitly)

For every change to a provider we do not invest in (FalkorDB/Neo4j/Neptune), ask: *does this reveal a
latent issue in LadybugDB / our M.E.AI layer?* Decision rule:

1. What is the root cause? If it is **protocol/engine-specific to that provider** (RediSearch treats
   `_`/`-` as operators; Redis can't carry NUL bytes; FalkorDB vector syntax), and **Python does not
   apply it to its Kuzu path**, then adapting it to LadybugDB would *diverge from Python's Kuzu
   behavior* → do **not** adapt; record N/A with the reason.
2. If it fixes a **real bug that also exists for LadybugDB** (provable: a failing LadybugDB runtime
   test, an engine limitation we can demonstrate), and the fix aligns with the authors' intent →
   adapt it to the LadybugDB driver, with a test, and record it in `parity.md`.
3. When unsure, prefer N/A + a one-line note over speculative provider-specific code.

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

## Step 9 — Advance the local `graphiti_core` pointer (gated on steps 3/5 passing)

Only after the suite is green and all library changes are incorporated, advance the local mirror so
future diffs are clean. The parent repo `W:\code\graphiti` is a **fork** (it carries our own commits:
the `csharp` submodule + root-docs retarget), so it cannot fast-forward to `origin/main`. Advance the
mirrored library surgically rather than merging unrelated upstream changes:

```
# from W:\code\graphiti, working tree clean except intended changes
git checkout origin/main -- graphiti_core            # advance the library we mirror
# (the net graphiti_core tree is now identical to origin/main HEAD)
git commit -m "sync graphiti_core to upstream origin/main @ <sha>"
```

Do **not** stage the `csharp` submodule pointer or the `csharp-wt-*` worktree dirs in this commit.
Record the synced `<sha>` in `parity.md`'s anchor (step 7). Next time, that sha is the new ANCHOR for
step 1.

> Note: this advances only `graphiti_core/` (and version metadata if changed). The fork's
> `server/`, `mcp_server/`, etc. intentionally stay where they are — we don't mirror them. If we ever
> want the whole fork current, that's a separate `merge origin/main` decision with its own conflict
> review (README is the likely conflict).
