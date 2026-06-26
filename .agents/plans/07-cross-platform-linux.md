# Plan 07 — Cross-platform (Linux x64) proof for the LadybugDB driver

Created 2026-06-26. This is the work order for roadmap goal **G1 — Cross-platform proof (HIGH)**. It
becomes the current actionable plan now that plan 06 (Ladybug→Core merge), G4 (observability), and the
first wave of G3 perf/allocation slices are complete.

## Status

**DONE 2026-06-26.** Reproduced the linux-x64 FTS failure, classified it as a `ladybug-dotnet`
runtime-asset loader bug, fixed it in the fork, published `0.17.1-dev.2.1.g53e5ab5`, re-pinned
Graphiti, and added a gated Linux FTS/vector extension smoke. The Linux GitHub Actions job is wired
behind repository variable `GRAPHITI_ENABLE_LINUX_LADYBUG_SMOKE=1`, so enabling that lane remains a
repo-operations switch rather than a code change.

## Why this is the next stream

At plan start, the LadybugDB driver was validated only on **win-x64** and the Linux path was
*known-broken*, not merely unvalidated: the fork's earlier Linux package hit an
**FTS-extension ABI / symbol-visibility mismatch**
loading from `~/.lbdb/extension`. The binding has since shipped the `dlopen(RTLD_NOW | RTLD_GLOBAL)`
loader fix and rethrows undefined-symbol failures (see `kuzu-driver-port.md` WS-1 audit), so the
remaining open question is **runtime validation on a real linux-x64 runner**: does `fts` (and ideally
`vector`) actually `INSTALL` / `LOAD` / `CREATE` / `QUERY` round-trip there now? Graphiti's own source
does **not** hard-code a `win-x64` RID — the driver references the cross-platform meta packages — so any
failure is expected to be in the binding/extension packaging or native layer, not Graphiti `src/`.

This goal also most directly backs the standing decision to keep **LadybugDB primary** and to
**self-service the bindings** (`sergey-v9/ladybug-dotnet`). Making the primary provider actually run
cross-platform is the proof of that bet.

## Steps (one slice each — implement → verify → commit → check off)

- [x] **A. Reproduce on linux-x64.** Stand up a linux-x64 environment (Docker container or WSL2) with
  the .NET 10 SDK and the `github_ladybug` GitHub Packages credential (`read:packages`). Restore and run
  the LadybugDB runtime tests (`LadybugRuntimeDriverTests`, especially the file-backed
  build→close→reopen→search proof). Capture the **exact** outcome: do the InMemory paths pass while only
  the Ladybug-extension paths fail? What is the precise error loading from `~/.lbdb/extension` —
  undefined symbol, wrong ELF class/arch, missing `.so`? Record the reproduction verbatim in
  `kuzu-driver-port.md`.
- [x] **B. Diagnose and classify.** Bucket the failure as (i) **Graphiti-side** (a RID / runtime-asset /
  path assumption in `src/`), (ii) a **binding / extension-packaging gap** in `ladybug-dotnet` (missing
  or wrong linux-x64 extension binary, bad rpath/`RUNPATH`, residual symbol visibility), or (iii) a
  **native LadybugDB defect**. State which, with evidence.
- [x] **C. Fix per the self-service-bindings policy.** If (ii)/(iii): patch in `W:\code\ladybug`, push
  the fork's `dev` branch, let the `sergey-v9/ladybug-dotnet` workflow publish a new dev package, re-pin
  Graphiti's `NuGet.config` / version, and re-verify. If (i): fix in Graphiti `src/`. Keep the C API /
  wire behavior unchanged; do not regress win-x64.
- [x] **D. Add a gated Linux smoke.** A `fts` + `vector` `CREATE`/`QUERY` round-trip check (test or
  `eng/` script) that proves the extensions load and search round-trips on linux-x64. Gate it like the
  OpenAI provider tests: it skips cleanly when the Linux runner / feed credential is absent, so the
  win-x64 verifier stays green and unconditional. Wire a GitHub Actions Linux job that runs it, but leave
  enabling the secret/runner to the user (see Status).
- [x] **E. Make README / package metadata tell the truth.** Until **D** is green, README and package
  metadata must say **win-x64 is the only supported RID** — do not advertise cross-platform. Once D
  passes, list linux-x64 and remove the caveat.

## Non-stall fallback (supervisor guard)

Native extension / ABI debugging can become a deep hole. **Time-box A+B.** If the binding fix in **C**
is not reachable within the slice, land a precise *failing repro + defect report* (in
`kuzu-driver-port.md` and the binding feedback file under
`W:\code\ladybug\tools\csharp_api\`) and fall back to the **remaining G3 hot-path profiling** so the
agent stays productive: profile InMemory O(n) full-scan cosine, MMR merge, prompt serialization, and
bulk edge dedupe, landing only measured, parity-safe wins (BenchmarkDotNet before/after, update
baselines). Resume **C** when a fixed binding package is available.

## Verification

`.\eng\Verify-GraphitiCore.ps1` must stay green on win-x64 throughout — the Linux smoke is **additive
and gated**, never a new requirement for the win-x64 lane. Keep exact-cosine the default vector path;
this plan does not touch the deferred HNSW gate (that stays under G3's evidence-driven program).

### Completion Evidence

- WSL2 Ubuntu-24.04 reproduced the original package failure without `LD_PRELOAD`:
  `LOAD EXTENSION FTS` failed on
  `~/.lbdb/extension/0.17.0/linux_amd64/fts/libfts.lbug_extension` with undefined symbol
  `_ZTIN4lbug7catalog12IndexAuxInfoE`.
- `LD_PRELOAD` of the package-copied `runtimes/linux-x64/native/liblbug.so` made the same tests pass,
  proving a binding/native loader visibility gap rather than a Graphiti query bug.
- `ladybug-dotnet` commit `53e5ab5` makes the resolver probe `runtimes/<rid>/native` and load that
  library with `RTLD_NOW | RTLD_GLOBAL`; the fork workflow published `0.17.1-dev.2.1.g53e5ab5`.
- Graphiti's local WSL run restored the fixed package from an isolated cache and passed the three
  previously failing package/runtime tests without `LD_PRELOAD`; the committed Linux smoke adds an
  explicit gated `fts` + `vector` create/query round-trip.
