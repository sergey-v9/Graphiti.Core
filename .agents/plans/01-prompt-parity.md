# Plan 01 — Prompt parity

**Objective:** every live Python prompt's instruction text exists in C#, rendered by a builder in
`src/Graphiti.Core/Prompts/`, pinned by golden-text tests, and wired into the calling service.

**Why this is the top priority:** prompt prose is the part of Graphiti that makes a real LLM
produce a good graph. Until it is ported, the library is a well-tested shell. No other work
(performance, docs, providers) raises product quality more per hour than this.

**Non-goals for this plan:** no allocation tuning, no refactors beyond what wiring requires, no
new providers, no porting of Python prompts that have no live call site (see `parity.md` for the
unused list).

## The pattern (already landed — copy it)

`Prompts/ExtractNodesPrompts.cs` + `Prompts/ExtractEdgesPrompts.cs` and their tests
(`tests/Graphiti.Core.Tests/Prompts/`) are the model, landed 2026-06-11:

1. One static class per Python prompt module, one `Build*` method per prompt function, returning
   `Message[]` (system + user), taking explicit typed parameters instead of Python's context dict.
2. Instruction text is transcribed from the Python source near-verbatim. Allowed divergences
   (documented in `decisions.md` "Prompt parity contract"): C# interpolates collections as compact
   JSON via `PromptJson.Serialize` where Python uses `to_prompt_json`/repr; timestamps render
   ISO-8601; em-dashes may become hyphens. Nothing else may be reworded — resist the urge to
   "improve" the prose; upstream tuned it against real extraction failures.
3. A golden test per builder asserts the **full rendered text** (not substrings) for a small
   representative context, transcribed against the Python rendering. If you change a prompt, the
   golden test must change in the same commit, and only with a parity reason.
4. The calling service replaces its inline one-liner + raw-JSON-context message pair with the
   builder call. Keep `promptName` strings identical (cache-key identity). Delete the
   now-unused context-builder code paths in the same slice if nothing else uses them.

## Work items (one commit-sized slice each; update `parity.md` row in the same commit)

- [x] 1. `extract_nodes.extract_message` / `extract_text` / `extract_json` → ExtractNodesPrompts
      (landed 2026-06-11).
- [x] 2. `extract_edges.edge` → ExtractEdgesPrompts (landed 2026-06-11).
- [x] 3. `extract_edges.extract_timestamps` → ExtractEdgesPrompts.BuildExtractTimestamps; wire
      EdgeResolutionService.cs:374. Python: prompts/extract_edges.py:242-271. Landed 2026-06-11.
- [x] 4. `dedupe_nodes.nodes` → new DedupeNodesPrompts; wire NodeResolutionService.cs:102. Python:
      prompts/dedupe_nodes.py:117-179. Include the worked EXAMPLE block (NYC/Java/Marco cases) —
      it carries most of the prompt's precision. Landed 2026-06-11.
- [x] 5. `dedupe_edges.resolve_edge` → new DedupeEdgesPrompts; wire EdgeResolutionService.cs:246.
      Python: prompts/dedupe_edges.py:43-100. Landed 2026-06-11.
- [x] 6. `extract_nodes.extract_attributes` → ExtractNodesPrompts.BuildExtractAttributes; wire
      AttributeExtractionService.cs:122. Python: prompts/extract_nodes.py:383-464. The HARD RULES
      anti-hallucination block is the point of this prompt (upstream commit 7514b44); port it
      exactly. Landed 2026-06-11.
- [x] 7. `extract_edges.extract_attributes` → ExtractEdgesPrompts.BuildExtractAttributes; wire
      AttributeExtractionService.cs:45. Python: prompts/extract_edges.py:181-239. Same HARD RULES
      remark. Landed 2026-06-11.
- [x] 8. `summarize_nodes.summarize_pair` + `summary_description` → new SummarizeNodesPrompts;
      wire CommunityService.cs:255/285. Python: prompts/summarize_nodes.py:54-135. Note: Python
      sends the two source summaries in the prompt and lets the LLM synthesize;
      C# currently pre-builds a deterministic text and asks the LLM to compress it. Align with
      Python; keep the deterministic builder only as the documented no-LLM fallback. Landed
      2026-06-11.
- [ ] 9. `summarize_sagas.summarize_saga`: already faithful inline in SagaService.cs:175. Move it
      into Prompts/SummarizeSagasPrompts for uniformity + golden test. Pure mechanical move.
- [ ] 10. Sweep: grep `src/` for `new Message("system"` outside `Prompts/` — there should be none
      left. Update `parity.md` prompt table; mark Phase 1 done in `roadmap.md` if all rows OK.

Items 3–9 are independent of each other — any session can pick any unchecked item, but do exactly
one item per slice, verify, commit, check it off here.

## Verification per slice

`.\eng\Verify-GraphitiCore.ps1 -FocusedFilter "FullyQualifiedName~Graphiti.Core.Tests.Prompts"`
then the full suite (the script runs it). Existing workflow tests key off `promptName` and fake
LLM clients, so prompt-content changes should not break them; if one asserts user-prompt
substrings, update it to assert against the new rendered text, never by weakening to "any string".

## Done when

All items checked, `parity.md` prompt table has no STUB/MISSING rows for live prompts, and
`roadmap.md` Phase 1 is marked complete.
