# Plan 08 — Release-surface finalization

**DONE 2026-06-27 (non-gated part of G6).** Settled the open public-surface decisions while still
`2.0.0-alpha.1`: `CommunityEdgeNamespace.SaveBulkAsync` was **kept** as a deliberate additive API, and
`EntityAttributeDefinition.MaxLength` + `.Required` were made public — closing the per-field-cap /
required-retain divergence vs Python `apply_capped_attributes`. Did the public-API freeze pass + the
package metadata / RID-claim audit + a green pack and fresh-consumer dry run. Stopped before any
`<Version>` stamp or publish (parked, user-gated).

Durable record: the two surface decisions in `decisions.md`, the matrix in `parity.md`, git history.
(Stub per `doc-hygiene.md`, 2026-06-28.)
