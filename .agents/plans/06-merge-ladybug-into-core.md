# Plan 06 — Merge LadybugDB driver into Core

**DONE 2026-06-26.** The LadybugDB driver, options, DI helper, factory, and `LadybugDB` /
`LadybugDB.Native` package refs were folded into `Graphiti.Core` under `src/Graphiti.Core/Drivers/Ladybug/`.
The separate driver package, the core-only verifier (`Verify-GraphitiCoreOnly.ps1`), the core-only CI
lane, and the second public-API snapshot were retired; the package smoke now exercises InMemory +
LadybugDB from one package. Accepted consequence: Core restores need the `github_ladybug` feed and can't
publish to nuget.org until LadybugDB is public there — fine for the embeddable-library paradigm.

Durable record: `roadmap.md` "Resolved scope decisions" #4, `kuzu-driver-port.md`, git history.
(Stub per `doc-hygiene.md`, 2026-06-28.)
