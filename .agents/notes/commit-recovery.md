# Commit Recovery And Iteration Discipline

The C# port has accumulated a long-running uncommitted working tree. Treat commit recovery as an
active task before continuing broad implementation work.

## Current Local Submodule Setup

`csharp/` is intended to be worked as a local Git submodule. Its local origin is
`../graphiti-csharp.git`, and the parent repo records the submodule at `csharp`.

The submodule starts from an empty initialization commit so the current port files can still be split
into meaningful commits inside `csharp/`. Do the recovery commits in the C# submodule first, then
update the parent repo's submodule pointer once the reconstructed commit sequence exists.

Inspection on 2026-06-01:

- The parent repo has `.gitmodules` and a `csharp` gitlink staged, pointing at submodule commit
  `facce1220b119baa97b8d967f5e52dd755cea93f`.
- The parent repo also has an unstaged `.gitignore` change for `csharp/.vs`.
- The nested `csharp` repo is on `main` at `facce12` with origin `W:\code\graphiti-csharp.git`.
  That commit is the empty initialization commit.
- The actual C# port files, tests, notes, and local skills are currently untracked inside the
  nested `csharp` repo. They are source/recovery input, not generated output to clean from the
  parent repo.

## Recover The Current Work Into Meaningful Commits

Do not make one giant "port C#" commit for the current tree. The original step-by-step history may be
gone, so reconstruct a logical sequence from the current file boundaries, tests, and behavior.

Suggested approach:

- Start with `git status --short` and grouped diffs/stats for `csharp/`. Identify unrelated changes,
  generated output, secrets, or local-only files before staging anything.
- Split the current C# work into several reviewable commits that read like stages of the port. Good
  commit boundaries are project scaffolding/docs, core models/configuration/serialization, LLM and
  embedding infrastructure, driver foundations, search/ranking behavior, ingestion/maintenance/text
  helpers, LadybugDB/Kuzu foundation, and test coverage.
- Use parallel read-only agents if helpful to classify large areas of the tree, but keep final staging
  and commit decisions in one coordinating session so commits do not overlap or race.
- Stage deliberately by path or hunk and inspect each staged diff before committing. Preserve unrelated
  user or agent edits, and ask before including anything ambiguous.
- Prefer each commit to build a coherent state. If full verification after every reconstructed commit
  is too expensive, at least run targeted tests or explain the verification gap in the commit body.
- Commit messages should describe the intent and behavior, not only the file movement. Avoid vague
  messages such as "update C# port" or "misc fixes".

The goal is a sequence reviewers can understand even if it is not the exact historical sequence.

## Commit Future Iterations

After the recovery commits, format subsequent work as completed iterations:

- Before starting an iteration, define a small objective with a clear stopping point.
- At the end of the iteration, update relevant notes, run appropriate verification, and commit the
  completed work with a descriptive message.
- Do not let another broad, endless objective accumulate a large uncommitted tree. If the objective is
  still open after a coherent slice lands, commit the slice and continue in the next iteration.
- Include important verification results or known gaps in the commit body or handoff note.
- If a slice cannot be safely committed because of conflicts, unrelated edits, failed verification, or
  unclear ownership, stop and record the blocker instead of continuing indefinitely.

This note is specifically about the long-running C# port work. It does not change the technical port
direction in `decisions.md`, `handoff.md`, `roadmap.md`, or `kuzu-driver-port.md`.
