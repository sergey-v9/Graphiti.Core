# Plan 13 - Upstream Python sync through 2026-08-18

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Reconcile the 11 `graphiti_core/` commits between Python anchor
`b59d4ba01118a91708fd6a6892200016168eeb5d` and upstream
`10374d6044f91b9ecae3586828abb1ecbf022c4f`, adapting portable behavior to LadybugDB and InMemory.

**Architecture:** Port observable Graphiti behavior, not FalkorDB/RediSearch implementation details.
First probe the real LadybugDB runtime for semantically equivalent full-text and group-isolation defects.
Then land the two confirmed C# gaps as separate test-first slices: group-scoped community rebuilds and
the RRF-bounded edge cross-encoder shortlist. Finish by recording every upstream disposition and
recreating the parent repository's single hookup commit on current upstream `main`.

**Tech Stack:** C# 14, .NET 10, xUnit v3, Microsoft.Testing.Platform/VSTest host, LadybugDB,
`Graphiti.Core` InMemory driver.

**Spec:** `.agents/notes/upstream-sync-procedure.md` and parent `FORK_MAINTENANCE.md`.

## Global Constraints

- Scope is upstream `graphiti_core/` only.
- Preserve public API, prompt text, wire values, schema/cache identity, and defaults unless an upstream
  behavior fix specifically requires otherwise.
- Do not add FalkorDB, Neo4j, or Neptune providers.
- Treat FalkorDB changes as capability signals: adapt them only when the same observable defect exists
  in LadybugDB or InMemory; record engine-only protocol changes as N/A with runtime evidence.
- Use TDD for every production behavior change.
- Run LadybugDB tests centrally and never concurrently from worktrees.
- Each implementation slice ends with zero-warning build, focused tests, full suite, format verification,
  notes update, and a logical C# commit.
- Do not publish, stamp a version, or push the parent repository.

---

### Task 1: Probe FalkorDB fixes against LadybugDB semantics

**Files:**
- Modify: `tests/Graphiti.Core.Tests/Drivers/Ladybug/LadybugPackageRuntimeTests.cs`
- Modify only on reproduced failure: `src/Graphiti.Core/Drivers/Ladybug/LadybugFulltextQuery.cs`
- Modify: `.agents/notes/parity.md`

**Interfaces:**
- Consumes: `ISearchGraphDriver.SearchEntityNodesFulltextAsync` and generic group-filtered reads/searches.
- Produces: runtime evidence classifying `e6c3af9`, `7db4031`, `4674e1e`, `3bb2d0b`, and `abc0017`.

- [x] **Step 1: Add a real LadybugDB FTS probe**

  Add a runtime test that stores a code-identifier entity and verifies a backtick-wrapped query does not
  fail and remains discoverable. In the same initialized runtime, verify stopword-only and
  punctuation-only queries complete without an engine syntax error.

- [x] **Step 2: Run the probe against unchanged production code**

  Run:
  `dotnet test tests/Graphiti.Core.Tests/Graphiti.Core.Tests.csproj --no-restore --filter FullyQualifiedName~<new-test-name> --verbosity minimal`

  Expected: either PASS (Ladybug is semantically aligned; no production edit) or a reproducible Ladybug
  parser/matching failure that identifies the exact input requiring adaptation.

- [x] **Step 3: If and only if the probe fails, implement the minimum Ladybug adaptation**

  Keep `LadybugFulltextQuery.Build` engine-specific. Sanitize only characters proven to break or prevent
  matching in LadybugDB, and short-circuit only inputs proven to produce an invalid empty query. Do not
  import FalkorDB stopword lists or RediSearch query syntax.

- [x] **Step 4: Prove group isolation on the real provider**

  Add or extend a Ladybug runtime test with two groups. Assert one-group search/read returns only that
  group and multi-group search/read returns both. No production change is expected because Ladybug uses
  `group_id` predicates rather than one Falkor graph per group.

- [x] **Step 5: Verify and commit the provider-evidence slice**

  Run the focused Ladybug tests, `dotnet build -c Release`, full `dotnet test -c Release --no-build`, and
  `dotnet format --verify-no-changes --no-restore`. Record the dispositions in `parity.md`; commit the
  tests and any evidence-required Ladybug fix as one provider slice.

### Task 2: Preserve unrelated communities during scoped rebuilds

**Files:**
- Modify: `tests/Graphiti.Core.Tests/GraphitiCommunityTests.cs`
- Modify: `src/Graphiti.Core/Internal/Services/CommunityService.cs`
- Modify: `.agents/notes/parity.md`

**Interfaces:**
- Consumes: `Graphiti.BuildCommunitiesAsync(IReadOnlyList<string>? groupIds, ...)`.
- Produces: selected-group cleanup while retaining `null`/empty-list full-rebuild behavior.

- [x] **Step 1: Reverse the stale regression expectation**

  Change `BuildCommunities_RebuildRemovesCommunitiesAcrossAllGroups` into a test that first builds two
  groups, rebuilds only `group-a`, and asserts the existing `group-b` community and membership edges
  remain. Add an empty-group-list assertion proving the established full-rebuild semantics remain.

- [x] **Step 2: Run the focused test and verify RED**

  Run the class/method filter for `GraphitiCommunityTests`; expected failure is that `group-b` has been
  deleted by the current unscoped cleanup.

- [x] **Step 3: Scope community cleanup**

  Pass the original `groupIds` into `RemoveCommunitiesAsync`. For a non-empty list, load and delete only
  `CommunityNode`s in those groups. For `null` or an empty list, retain discovery and deletion of every
  existing community. Use existing typed node deletion so membership edges follow current driver
  cascade behavior without changing `IGraphDriver`.

- [x] **Step 4: Verify GREEN and commit**

  Run focused community tests, zero-warning Release build, full suite, and format verification. Update
  `parity.md` for `784782c`/the applicable `d9a2db9` hunk and commit the slice.

### Task 3: Use an RRF-bounded edge cross-encoder shortlist

**Files:**
- Modify: `tests/Graphiti.Core.Tests/Search/SearchEngineDriverBackedTests.cs`
- Modify: `src/Graphiti.Core/Search/SearchEngine.cs`
- Modify: `.agents/notes/parity.md`

**Interfaces:**
- Consumes: the three ranked edge retrieval lists and `EdgeReranker.CrossEncoder`.
- Produces: an RRF-fused shortlist capped at `2 * limit`, followed by existing cross-encoder ranking and
  final `limit` truncation.

- [x] **Step 1: Replace obsolete shortlist tests**

  Use BM25-heavy and cosine/BFS fixtures with `limit = 2`. Record the passages given to the cross
  encoder, score the non-BM25 candidate highest, and assert that it reaches the four-candidate shortlist
  and ranks first. Keep duplicate-fact behavior covered.

- [x] **Step 2: Run the focused tests and verify RED**

  Expected failure: current first-seen merge truncates to two BM25 candidates before cross-encoding.

- [x] **Step 3: Implement the upstream shortlist semantics**

  In the edge cross-encoder branch only, call `SearchResultComposer.FuseRanks` over text, vector, and BFS
  lists with `2 * limit` and `rerankerMinScore`. Keep MMR on first-seen order and keep all other rerankers
  unchanged. Preserve final cross-encoder score filtering and result limit.

- [x] **Step 4: Verify GREEN and commit**

  Run focused search tests, zero-warning Release build, full suite, and format verification. Update
  `parity.md` for `d40da88` and commit the slice.

### Task 4: Lock already-aligned edge read and traversal behavior

**Files:**
- Modify: `tests/Graphiti.Core.Tests/Drivers/Ladybug/LadybugPackageRuntimeTests.cs`
- Modify: `tests/Graphiti.Core.Tests/Drivers/InMemorySearchGraphDriverTests.cs`
- Modify: `.agents/notes/parity.md`

**Interfaces:**
- Consumes: incident-edge reads and BFS edge search.
- Produces: regression proof for stored source/target direction and `ReferenceTime` hydration.

- [ ] **Step 1: Strengthen existing runtime assertions**

  Assert that a target-side incident read returns the original source/target order and reference time.
  Assert that an InMemory BFS hit preserves the stored endpoints, fact, and reference time. These should
  pass without production edits; investigate any failure before changing code.

- [ ] **Step 2: Verify and commit the proof slice**

  Run the focused Ladybug and InMemory tests, build, full suite, and format verification. Record
  `a96309b`, `2453209`, `b0ec6d9`, and the remaining `d9a2db9` hunk as already aligned, then commit.

### Task 5: Complete the upstream reconciliation and parent rebase

**Files:**
- Modify: `.agents/notes/parity.md`
- Modify: `.agents/notes/handoff.md`
- Modify: `.agents/plans/13-upstream-python-sync-2026-08-18.md`
- Parent hookup files per `W:/code/graphiti/FORK_MAINTENANCE.md`

**Interfaces:**
- Consumes: all verified slice commits and the 11-commit disposition inventory.
- Produces: parity anchor `10374d6044f91b9ecae3586828abb1ecbf022c4f` and a local parent
  `csharp-port` branch consisting of current upstream `main` plus one hookup commit.

- [ ] **Step 1: Run adversarial completeness review**

  Re-read every upstream commit and the net `graphiti_core/` diff. Confirm each change is adopted,
  already aligned, or N/A with evidence; verify no prompt/pipeline/public API changes were missed.

- [ ] **Step 2: Run the authoritative C# gate**

  Run `./eng/Verify-GraphitiCore.ps1`, `dotnet format --verify-no-changes --no-restore`, and
  `git diff --check`. Require zero warnings, a green full suite, successful pack, and package-consumer
  smoke.

- [ ] **Step 3: Update and compact current-state documentation**

  Advance `parity.md` to the target SHA with one concise per-commit table. Replace the stale latest-audit
  paragraph in `handoff.md`; do not append a changelog. Collapse this completed plan to a short DONE
  stub and commit the reconciliation documentation.

- [ ] **Step 4: Recreate the parent hookup on current upstream main**

  Follow `FORK_MAINTENANCE.md`: update local parent `main` to `origin/main`, recreate local
  `csharp-port` as one hookup commit with the `csharp` gitlink at the verified C# HEAD, and reconcile root
  pointer files against current upstream text. Verify `main == origin/main`, `csharp-port` is exactly one
  commit ahead, and the submodule gitlink resolves to the intended C# commit. Never push the parent.
