# Plan 12 — Trim-readiness (trim-compatible, NOT NativeAOT)

Created 2026-06-28. Scoped per the host-grounded decision in `decisions.md` → "NativeAOT / trimming
stance". The consumer (Nestor) will likely `PublishTrimmed` and embed this library **in-process**, so
`Graphiti.Core` must be a clean **trimming citizen**: it must not break or silently mis-trim inside a
trimmed host, and must honestly declare its trim-unsafe surfaces.

**In scope:** the TRIM analyzer only — `IL2026` (`RequiresUnreferencedCode`) and the `IL2070/IL2075/…`
reflection-member-access family. **Out of scope:** NativeAOT / `IL3050` (`RequiresDynamicCode`) — the
host can't use AOT, so do **not** enable `IsAotCompatible` or chase IL3050.

## Status

**Current priority (2026-06-28).** Behavioral parity, wire values, and cache/schema identity stay
unchanged. Note: unlike the other recent passes, this one **does** legitimately change the public-API
snapshot — adding `[RequiresUnreferencedCode]` / `[DynamicallyAccessedMembers]` attributes to public
members is part of the trim *contract* and appears in the PublicApiGenerator output. That snapshot change
is **expected and correct** here (attribute additions only); regenerate + review the baseline.

## Steps

- [ ] **A. Turn the trim analyzer on (trim only).** Add `<IsTrimmable>true</IsTrimmable>` to
  `src/Graphiti.Core/Graphiti.Core.csproj` (this enables the trim analyzer **and** stamps the assembly so
  a consuming trimmer trims into it). Do **not** set `IsAotCompatible` / `EnableAotAnalyzer`. Because
  `TreatWarningsAsErrors=true` will turn the IL warnings into errors, first capture a baseline by adding
  the surfaced IL codes to `<WarningsNotAsErrors>` (or build one project to list them), then burn them
  down and remove the exemption so the end state is `IsTrimmable=true` + **0 warnings**.

- [ ] **B. Burn down each trim warning by root cause** (per-site decision framework):
  1. **Genuinely trim-unsafe public API** (reflection over arbitrary/consumer types) → annotate the
     public entry point with `[RequiresUnreferencedCode("…why…")]`. This is the *honest* declaration and
     propagates the warning to the host. Expected sites: the open-attribute `Dictionary<string,object?>`
     serialization (the `DefaultJsonTypeInfoResolver` fallback in `GraphitiJsonSerializer`) and the
     lenient `PropertyInfo`-based response materializer (`LlmClientResponseExtensions.MaterializeLeniently<TResponse>`).
  2. **Controllable reflection** (we know which members are needed) → annotate the generic/parameter with
     `[DynamicallyAccessedMembers(…)]` so the trimmer preserves exactly those members; this keeps the path
     trim-**safe** (no warning propagated). Prefer this over `[RequiresUnreferencedCode]` whenever the
     type is a known DTO whose members we can declare.
  3. **Options/config binding** (`Configure<TOptions>(IConfiguration)` registrations) → switch to the
     source-generated configuration binder (`Microsoft.Extensions.Configuration.Binder` source generator)
     or explicit manual binding to clear those `IL2026` cleanly. If that proves disproportionate,
     annotate the registration helper `[RequiresUnreferencedCode]` instead and note it.
  4. **Known-type serialization** → route it through the existing source-gen `JsonSerializerContext`
     (`GraphitiJsonContext`) so there is no reflection; extend the context with any missing known DTOs.
  5. **Un-annotatable third-party** (e.g. a Polly generic) → a targeted
     `[UnconditionalSuppressMessage("Trimming","ILxxxx", Justification="…")]` with a documented reason,
     used **sparingly**.

- [ ] **C. Document the open-attribute serialization stance** in `decisions.md`: `Dictionary<string,object?>`
  attribute values are reflection-serialized because the value types are arbitrary/consumer-defined
  (mirrors the Python shape); the entry points are `[RequiresUnreferencedCode]`-annotated as the
  documented trim boundary, and the host (which has the same pattern) accepts it. Record it.

- [ ] **D. Regenerate + review the public-API snapshot.** The trim attributes will change
  `tests/Graphiti.Core.Tests/Api/Graphiti.Core.approved.txt`. Regenerate, and **review the diff**: it must
  contain *only* trim-attribute additions on public members — no type/member/signature additions or
  removals. If anything other than attributes changed, something behavioral leaked in — stop and fix.

## Verification

`.\eng\Verify-GraphitiCore.ps1` green: restore, format, **0-warning build with `IsTrimmable=true`**, full
test suite, pack. The only expected baseline change is the API snapshot (trim attributes). Keep
`IsTrimmable=true` committed so the trim analyzer runs in the normal build going forward — that is the
gate (no new CI lane, per the standing CI decision).

## Non-goals

- No NativeAOT / `IsAotCompatible` / IL3050 work.
- No behavior/wire/cache-key change; defaults unchanged.
- Do not flip `IsTrimmable` on the test/benchmark/sample projects — only the shippable `Graphiti.Core`.
