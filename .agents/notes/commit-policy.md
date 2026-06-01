# Commit Policy

The C# port lives in the local `csharp/` submodule. Its local origin is
`../graphiti-csharp.git`, and the parent `graphiti` repo records the submodule at `csharp`.

## Normal C# Iterations

- Before starting an iteration, define a small objective with a clear stopping point.
- At the end of the iteration, update relevant notes, run appropriate verification, and commit the
  completed work inside the `csharp` submodule with a descriptive message.
- If the same verification loop repeats across iterations, write a small helper script for it instead
  of manually running command after command. Prefer a simple PowerShell script with fail-fast behavior,
  clear step output, and parameters for targeted tests when useful.
- Own the loop you create: run the helper script, update it when the repeated process changes, and
  commit it when it is generally useful to future C# work. Keep throwaway one-off scripts out of the
  durable tree unless they become part of the normal workflow.
- Do not let broad objectives accumulate a large uncommitted tree. If an objective is still open after
  a coherent slice lands, commit the slice and continue in the next iteration.
- Include important verification results or known gaps in the commit body or handoff note.
- If a slice cannot be safely committed because of conflicts, unrelated edits, failed verification, or
  unclear ownership, stop and record the blocker instead of continuing indefinitely.

## Commit Message Quality

- Keep each commit to one logical change. If the subject is hard to summarize, or the body needs to
  explain several unrelated bullets, split the work or make a larger coherent slice instead of
  stacking miscellaneous edits together.
- Follow the common Git convention: short imperative subject, no trailing period. Think `preserve
  in-memory BFS ordering`, not `preserved ...`, `preserves ...`, or `preserving ...`.
- Make the subject describe what applying the commit does to the codebase, not what you happened to do
  while working. It should read naturally after "this commit will ...".
- Keep subjects concise enough to scan in `git log` (roughly 50 characters when practical). If the
  reason needs more room, add a blank line and a body rather than stretching the subject.
- Use the body for why the change exists, what behavior changed, verification, and any important
  tradeoffs or side effects. Do not narrate the diff line by line; the diff already shows how.
- If the body would only repeat the subject, omit it. If a workaround, backend limitation, rejected
  alternative, or non-obvious scope boundary matters, put that context in the body.
- Summarize important external context instead of relying only on an issue/link. Future readers should
  understand the commit from the repository history itself.
- Wrap body prose for terminal readability, roughly 72 columns when practical. Exact wrapping matters
  less than keeping the message easy to read in Git tools.
- Do not introduce Conventional Commit prefixes (`feat:`, `fix:`, `refactor:`) as a new local rule
  unless the surrounding commit history has adopted them. A short area word can be useful, but clarity
  beats ceremony here.
- Avoid robotic runs of nearly identical subjects such as repeated `modernize ...` commits. If several
  slices have the same vague verb, either combine smaller slices or choose subjects that name the
  specific behavior, invariant, provider path, or test outcome changed by each commit.
- Prefer concrete subjects such as `reduce chunk splitter buffering`, `prove Ladybug saga summaries`,
  or `preserve in-memory BFS ordering` over generic labels like `modernize helpers`.
- Let commit frequency follow meaningful review boundaries, not a timer. A commit should make a slice
  easier to understand; if it only adds another near-duplicate subject to the log, widen or rename the
  slice.

## Parent Repo Policy

Leave the parent `graphiti` repository alone for ordinary C# slices. Do not create parent commits
whose only purpose is to bump the `csharp` submodule revision.

The parent repo's submodule pointer should move only at an intentional integration or release point,
or when the user explicitly asks for it.

## Historical Context

The one-time C# port recovery has already been split into local submodule commits. Treat this file as
the steady-state policy for future C# work, not as a recovery checklist.
