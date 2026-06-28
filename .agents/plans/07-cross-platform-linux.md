# Plan 07 — Cross-platform (Linux x64) proof for the LadybugDB driver

**DONE 2026-06-26 (roadmap G1).** Reproduced the linux-x64 failure (`LOAD EXTENSION FTS` undefined symbol
`_ZTIN4lbug7catalog12IndexAuxInfoE` under `~/.lbdb/extension`) in WSL2; root-caused it to a `ladybug-dotnet`
runtime-asset loader gap (not a Graphiti bug); fixed it in the fork (the resolver now probes
`runtimes/<rid>/native` and loads with `RTLD_NOW | RTLD_GLOBAL`), published `0.17.1-dev.2.1.g53e5ab5`,
re-pinned, and added a gated linux-x64 `fts`+`vector` CREATE/QUERY smoke (behind repo var
`GRAPHITI_ENABLE_LINUX_LADYBUG_SMOKE=1`). win-x64 remains the unconditional verifier lane.

Durable record: `roadmap.md` G1, `kuzu-driver-port.md`, git history. (Stub per `doc-hygiene.md`, 2026-06-28.)
