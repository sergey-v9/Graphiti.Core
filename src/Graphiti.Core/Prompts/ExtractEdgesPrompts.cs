using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graphiti.Core.Prompts;

/// <summary>
/// Prompt builders ported from Python <c>graphiti_core/prompts/extract_edges.py</c> with context
/// shaping from <c>utils/maintenance/edge_operations.py::extract_edges</c>. The instruction text is
/// transcribed near-verbatim per the prompt parity contract in <c>.agents/notes/decisions.md</c>;
/// golden tests pin the rendered output. Do not reword the prose without a parity reason.
/// </summary>
internal static class ExtractEdgesPrompts
{
    /// <summary>Rendered context strings interpolated into the edge extraction prompt.</summary>
    internal readonly record struct EdgeExtractionContext(
        string EpisodeContent,
        string NodesJson,
        string PreviousEpisodesJson,
        string ReferenceTime,
        string EdgeTypesJson,
        string CustomExtractionInstructions)
    {
        /// <summary>Empty when no edge types are configured; the FACT_TYPES section is omitted.</summary>
        public bool HasEdgeTypes => EdgeTypesJson.Length > 0;
    }

    internal static EdgeExtractionContext BuildContext(
        EpisodicNode episode,
        IReadOnlyList<EpisodicNode> previousEpisodes,
        IReadOnlyList<EntityNode> nodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap,
        string? customExtractionInstructions)
    {
        return new EdgeExtractionContext(
            episode.Content,
            PromptJson.Serialize(BuildNodesContext(nodes)),
            PromptJson.Serialize(ExtractNodesPrompts.BuildPreviousEpisodesContext(previousEpisodes)),
            GraphitiHelpers.EnsureUtc(episode.ValidAt).ToString("O"),
            edgeTypes is null || edgeTypes.Count == 0
                ? string.Empty
                : PromptJson.Serialize(BuildEdgeTypesContext(edgeTypes, edgeTypeMap)),
            customExtractionInstructions ?? string.Empty);
    }

    internal static Message[] BuildEdge(in EdgeExtractionContext context)
    {
        var edgeTypesSection = context.HasEdgeTypes
            ? $$"""

              <FACT_TYPES>
              {{context.EdgeTypesJson}}
              </FACT_TYPES>

              """
            : string.Empty;

        var userPrompt = $$"""

            <PREVIOUS_MESSAGES>
            {{context.PreviousEpisodesJson}}
            </PREVIOUS_MESSAGES>

            <CURRENT_MESSAGE>
            {{context.EpisodeContent}}
            </CURRENT_MESSAGE>

            <ENTITIES>
            {{context.NodesJson}}
            </ENTITIES>

            <REFERENCE_TIME>
            {{context.ReferenceTime}}  # ISO 8601 (UTC); used to resolve relative time mentions
            </REFERENCE_TIME>
            {{edgeTypesSection}}
            # TASK
            Extract all factual relationships between the given ENTITIES based on the CURRENT MESSAGE.
            Only extract facts that:
            - involve two DISTINCT ENTITIES from the ENTITIES list,
            - are clearly stated or unambiguously implied in the CURRENT MESSAGE,
                and can be represented as edges in a knowledge graph.
            - Facts should include entity names rather than pronouns whenever possible.

            You may use information from the PREVIOUS MESSAGES only to disambiguate references or support continuity.


            {{context.CustomExtractionInstructions}}

            # EXTRACTION RULES

            1. **Entity Name Validation**: `source_entity_name` and `target_entity_name` must use only the `name` values from the ENTITIES list provided above.
               - **CRITICAL**: Using names not in the list will cause the edge to be rejected
            2. Each fact must involve two **distinct** entities - `source_entity_name` and `target_entity_name` NEVER refer to the same entity.
            3. Prefer facts that involve two distinct entities from the ENTITIES list. When a sentence describes a specific, concrete detail about a single entity (a brand name, a specific item, a physical description, a quantity, a location, a named activity), do NOT drop it. Instead, look for a second entity in the ENTITIES list that the detail relates to and form a proper triple (e.g., Entity -> OWNS -> item-entity, Entity -> LIVES_IN -> place-entity, Entity -> HAS_ATTRIBUTE -> detail-entity). Only skip the fact when no second entity in the ENTITIES list can anchor the detail.
               - BAD: "Alice feels happy" (vague single-entity state with no concrete detail - what is Alice happy about?)
               - GOOD: "Alice feels happy about Bob's promotion" -> Alice -> FEELS_HAPPY_ABOUT -> Bob's promotion
               - GOOD: "Nate plays games on a Gamecube" -> Nate -> PLAYS_GAMES_ON -> Gamecube (when "Gamecube" is in ENTITIES)
               - GOOD: "Alice congratulated Bob" (relationship between two entities), "Alice lives in Paris" (relationship between entity and place)
            4. Do not emit semantically redundant facts, even across episodes within the CURRENT_MESSAGE. However, if a later episode adds specific details to a previously stated fact (e.g., adding a brand name, a count, a color, a location, or any concrete attribute), extract the more detailed version as a NEW fact - it is NOT a duplicate. Only treat facts as duplicates when they convey the same specificity.
               - NOT a duplicate: "user plays video games" (Episode 0) vs. "user plays games on a Gamecube" (Episode 1) -> extract the second, more detailed fact.
               - IS a duplicate: "user plays games on a Gamecube" (Episode 0) vs. "user plays Gamecube games" (Episode 1) -> extract once, list both episodes in `episode_indices`.
            5. The `fact` MUST preserve all specific details from the source text: proper nouns, brand names, product names, model numbers, quantities, counts, colors, materials, physical descriptions, specific items, named locations, and named activities. Paraphrase the sentence structure but NEVER generalize:
               - NEVER generalize "Gamecube" to "gaming console", "Ford Mustang" to "car", "wool coat" to "coat", "red and purple lighting" to "lighting", "cracked windshield" to "car damage", or "three screenplays" to "several screenplays".
               - Do not verbatim quote the original text, but every concrete noun, number, and descriptor in the source should survive into the `fact`.
            6. Use `REFERENCE_TIME` to resolve vague or relative temporal expressions (e.g., "last week"). When the CURRENT_MESSAGE contains multiple episodes with per-episode timestamps, prefer the timestamp of the specific episode the fact originates from.
            7. Do **not** hallucinate or infer temporal bounds from unrelated events.

            # RELATION TYPE RULES

            - If FACT_TYPES are provided and the relationship matches one of the types (considering the entity type signature), use that fact_type_name as the `relation_type`.
            - Otherwise, derive a `relation_type` from the relationship predicate in SCREAMING_SNAKE_CASE (e.g., WORKS_AT, LIVES_IN, IS_FRIENDS_WITH).

            # DATETIME RULES

            - Use ISO 8601 with "Z" suffix (UTC) (e.g., 2025-04-30T00:00:00Z).
            - If the fact is ongoing (present tense), set `valid_at` to the timestamp of the episode the fact originates from. If no per-episode timestamp is available, use REFERENCE_TIME.
            - If a change/termination is expressed, set `invalid_at` to the relevant timestamp.
            - Leave both fields `null` if no explicit or resolvable time is stated.
            - If only a date is mentioned (no time), assume 00:00:00.
            - If only a year is mentioned, use January 1st at 00:00:00.

            """;

        return new[]
        {
            new Message(
                "system",
                "You are an expert fact extractor that extracts fact triples from text. " +
                "1. Extracted fact triples should also be extracted with relevant date information. " +
                "2. The CURRENT_MESSAGE may contain multiple episodes, each with its own timestamp. " +
                "Use each episode's timestamp to resolve temporal references within that episode. " +
                "REFERENCE_TIME is a fallback for when no per-episode timestamp is available."),
            new Message("user", userPrompt)
        };
    }

    internal static Message[] BuildExtractTimestamps(string fact, DateTime referenceTime)
    {
        var userPrompt = $$"""
            Given a FACT and its REFERENCE TIME, determine when the fact became true
            (valid_at) and when it stopped being true (invalid_at).

            Rules:
            - Resolve relative expressions ("last week", "2 years ago", "yesterday") using REFERENCE TIME.
            - If the fact is ongoing (present tense), set valid_at to REFERENCE TIME.
            - If a change or end is expressed, set invalid_at to the relevant time.
            - Leave both null if no time is stated or resolvable.
            - If only a date is mentioned (no time), assume 00:00:00.
            - Use ISO 8601 with Z suffix (e.g., 2025-04-30T00:00:00Z).
            - Do NOT hallucinate or infer dates from unrelated events.

            <FACT>
            {{fact}}
            </FACT>

            <REFERENCE TIME>
            {{GraphitiHelpers.EnsureUtc(referenceTime).ToString("O")}}
            </REFERENCE TIME>

            """;

        return new[]
        {
            new Message(
                "system",
                "You extract temporal bounds from facts. NEVER hallucinate dates."),
            new Message("user", userPrompt)
        };
    }

    internal static Message[] BuildExtractTimestampsBatch(IReadOnlyList<Graphiti.ExtractedEdge> edges)
    {
        var facts = new JsonArray();
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            facts.Add(new JsonObject
            {
                ["fact"] = edge.Fact,
                ["reference_time"] = FormatReferenceTime(edge.ReferenceTime)
            });
        }

        var userPrompt = $$"""
            Given a list of FACTS with their REFERENCE TIMES, determine when each fact
            became true (valid_at) and when it stopped being true (invalid_at).

            Rules:
            - Resolve relative expressions ("last week", "2 years ago", "yesterday") using each fact's REFERENCE TIME.
            - If the fact is ongoing (present tense), set valid_at to its REFERENCE TIME.
            - If a change or end is expressed, set invalid_at to the relevant time.
            - Leave both null if no time is stated or resolvable.
            - If only a date is mentioned (no time), assume 00:00:00.
            - Use ISO 8601 with Z suffix (e.g., 2025-04-30T00:00:00Z).
            - Do NOT hallucinate or infer dates from unrelated events.

            Return one timestamps entry per fact, in the same order.

            <FACTS>
            {{PromptJson.Serialize(facts)}}
            </FACTS>

            """;

        return new[]
        {
            new Message(
                "system",
                "You extract temporal bounds from facts. NEVER hallucinate dates."),
            new Message("user", userPrompt)
        };
    }

    internal static Message[] BuildExtractAttributes(
        string fact,
        DateTime referenceTime,
        IReadOnlyDictionary<string, object?> existingAttributes)
    {
        var userPrompt = $$"""
            Given the following FACT, its REFERENCE TIME, and any EXISTING ATTRIBUTES, update the attributes.

            HARD RULES - violating any of these is a failure:

            1. Each attribute value MUST be one of:
               (a) a clean value copied or directly normalized from the FACT,
               (b) the existing value already in EXISTING ATTRIBUTES (preserved unchanged), or
               (c) null / omitted, when neither (a) nor (b) applies.

            2. NEVER write reasoning, justification, or commentary into any field. Specifically:
               - NEVER include parenthetical explanations like "(implied by ...)", "(Context: ...)",
                 "(not explicitly stated ...)", "(based on ...)".
               - NEVER include first-person or deliberative phrases like "I should...", "However...",
                 "Sticking to...", "Since no...", "the instruction is to...", "must be kept...".
               - NEVER list alternatives or candidates inside one field ("X, or Y, or maybe Z").
               - NEVER explain why a value is null. If unknown, set the field to null and stop.

            3. Each attribute schema description tells you the FORMAT a real value should take. The
               description text is NEVER itself a value. NEVER copy schema description text into the field.

            4. The literal strings "null", "N/A", "Not specified", "unknown", "none", "not provided",
               or any sentence describing absence are NOT valid values. If no value is supported by
               the FACT, set the field to null (or omit it) - do not write a sentence.

            5. Each attribute value must be a short, well-formed instance of the type the field
               describes. If you cannot produce a clean value of that type from the FACT, the field is null.

            6. Use REFERENCE TIME to resolve any relative temporal expressions in the fact.

            7. Preserve existing attribute values unless the FACT explicitly provides a new value.

            <FACT>
            {{fact}}
            </FACT>

            <REFERENCE TIME>
            {{GraphitiHelpers.EnsureUtc(referenceTime).ToString("O")}}
            </REFERENCE TIME>

            <EXISTING ATTRIBUTES>
            {{PromptJson.Serialize(JsonSerializer.SerializeToNode(existingAttributes, GraphitiJsonSerializer.Options))}}
            </EXISTING ATTRIBUTES>

            """;

        return new[]
        {
            new Message(
                "system",
                "You are a fact attribute extraction specialist. " +
                "You ONLY emit attribute values that are explicitly stated in the FACT or " +
                "already present in EXISTING ATTRIBUTES. You output strictly the JSON specified " +
                "by the response schema - no reasoning, no explanation, no commentary in any field."),
            new Message("user", userPrompt)
        };
    }

    internal static JsonArray BuildNodesContext(IReadOnlyList<EntityNode> nodes)
    {
        var context = new JsonArray();
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            context.Add(new JsonObject
            {
                ["name"] = node.Name,
                ["entity_types"] = ExtractionContextBuilder.BuildStringArray(node.Labels)
            });
        }

        return context;
    }

    /// <summary>
    /// Mirrors Python's <c>edge_types_context</c> entries: <c>fact_type_name</c>,
    /// <c>fact_type_signatures</c> as two-element arrays, and <c>fact_type_description</c>.
    /// Signatures default to <c>["Entity", "Entity"]</c> when the edge type has no mapping. Edge
    /// types and signatures are emitted in a deterministic sorted order (Python preserves caller
    /// insertion order, which <c>IReadOnlyDictionary</c> does not guarantee).
    /// </summary>
    internal static JsonArray BuildEdgeTypesContext(
        IReadOnlyDictionary<string, EntityTypeDefinition> edgeTypes,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap)
    {
        var sortedEdgeTypes = new List<KeyValuePair<string, EntityTypeDefinition>>(edgeTypes.Count);
        foreach (var pair in edgeTypes)
        {
            sortedEdgeTypes.Add(pair);
        }

        sortedEdgeTypes.Sort(static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Key, right.Key));

        var context = new JsonArray();
        for (var i = 0; i < sortedEdgeTypes.Count; i++)
        {
            var pair = sortedEdgeTypes[i];
            context.Add(new JsonObject
            {
                ["fact_type_name"] = pair.Value.Name,
                ["fact_type_signatures"] = BuildSignaturesContext(pair.Key, edgeTypeMap),
                ["fact_type_description"] = pair.Value.Description
            });
        }

        return context;
    }

    private static JsonArray BuildSignaturesContext(
        string edgeTypeName,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap)
    {
        var signatures = new List<(string Source, string Target)>();
        if (edgeTypeMap is not null)
        {
            foreach (var pair in edgeTypeMap)
            {
                if (ContainsEdgeTypeName(pair.Value, edgeTypeName))
                {
                    signatures.Add((pair.Key.SourceType, pair.Key.TargetType));
                }
            }
        }

        if (signatures.Count == 0)
        {
            signatures.Add(("Entity", "Entity"));
        }

        signatures.Sort(static (left, right) =>
        {
            var sourceComparison = StringComparer.OrdinalIgnoreCase.Compare(left.Source, right.Source);
            return sourceComparison != 0
                ? sourceComparison
                : StringComparer.OrdinalIgnoreCase.Compare(left.Target, right.Target);
        });

        var context = new JsonArray();
        for (var i = 0; i < signatures.Count; i++)
        {
            context.Add(new JsonArray(
                JsonValue.Create(signatures[i].Source),
                JsonValue.Create(signatures[i].Target)));
        }

        return context;
    }

    private static bool ContainsEdgeTypeName(IReadOnlyList<string> edgeTypeNames, string edgeTypeName)
    {
        for (var i = 0; i < edgeTypeNames.Count; i++)
        {
            if (string.Equals(edgeTypeNames[i], edgeTypeName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatReferenceTime(DateTime? referenceTime) =>
        referenceTime is null ? "unknown" : GraphitiHelpers.EnsureUtc(referenceTime.Value).ToString("O");
}
