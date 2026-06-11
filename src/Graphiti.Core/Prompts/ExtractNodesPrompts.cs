using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graphiti.Core.Prompts;

/// <summary>
/// Prompt builders ported from Python <c>graphiti_core/prompts/extract_nodes.py</c> with context
/// shaping from <c>utils/maintenance/node_operations.py::extract_nodes</c>. The instruction text is
/// transcribed near-verbatim per the prompt parity contract in <c>.agents/notes/decisions.md</c>;
/// golden tests pin the rendered output. Do not reword the prose without a parity reason.
/// </summary>
internal static class ExtractNodesPrompts
{
    internal const string DefaultEntityTypeDescription =
        "A specific, identifiable entity that does not fit any of the other listed types. " +
        "Must still be a concrete, meaningful thing - specific enough to be uniquely identifiable. " +
        "GOOD: a named entity not covered by the other types. " +
        "BAD: \"luck\", \"ideas\", \"tomorrow\", \"things\", \"them\", \"everybody\", " +
        "\"a sense of wonder\", \"great times\". " +
        "When in doubt, do not extract the entity.";

    /// <summary>Rendered context strings interpolated into the node extraction prompts.</summary>
    internal readonly record struct NodeExtractionContext(
        string EpisodeContent,
        string EntityTypesJson,
        string PreviousEpisodesJson,
        string SourceDescription,
        string CustomExtractionInstructions);

    internal readonly record struct NodeAttributeExtractionContext(
        string PreviousEpisodesJson,
        string EpisodeContentJson,
        string NodeJson);

    internal static NodeExtractionContext BuildContext(
        EpisodicNode episode,
        IReadOnlyList<EpisodicNode> previousEpisodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        string? customExtractionInstructions)
    {
        return new NodeExtractionContext(
            episode.Content,
            PromptJson.Serialize(BuildEntityTypesContext(entityTypes)),
            PromptJson.Serialize(BuildPreviousEpisodesContext(previousEpisodes)),
            episode.SourceDescription,
            customExtractionInstructions ?? string.Empty);
    }

    /// <summary>
    /// Dispatches on the episode source the way Python <c>node_operations.py</c> selects the
    /// prompt version: message, json, or text (the fallback for every other source).
    /// </summary>
    internal static Message[] Build(EpisodeType source, in NodeExtractionContext context) =>
        source switch
        {
            EpisodeType.Message => BuildExtractMessage(context),
            EpisodeType.Json => BuildExtractJson(context),
            _ => BuildExtractText(context)
        };

    internal static Message[] BuildExtractMessage(in NodeExtractionContext context)
    {
        var userPrompt = $$"""

            NEVER extract any of the following:
            - Pronouns (you, me, I, he, she, they, we, us, it, them, him, her, this, that, those)
            - Abstract concepts or feelings (joy, balance, growth, resilience, happiness, passion, motivation)
            - Generic common nouns or bare object words (day, life, people, work, stuff, things, food, time,
              way, tickets, supplies, clothes, keys, gear)
            - Generic media/content nouns unless uniquely identified in the node name itself (photo, pic, picture,
              image, video, post, story)
            - Generic event/activity nouns unless uniquely identified in the node name itself (event, game, meeting,
              class, workshop, competition)
            - Broad institutional nouns unless explicitly named or uniquely qualified (government, school, company,
              team, office)
            - Ambiguous bare nouns whose meaning depends on sentence context rather than the node name itself
            - Sentence fragments or clauses ("what you really care about", "results of that effort")
            - Adjectives or descriptive phrases ("amazing", "something different", "new hair color")
            - Duplicate references to the same real-world entity. Extract each entity at most once per message,
              even if it appears multiple times or both as a speaker label and in the body text.
            - Bare relational or kinship terms (dad, mom, mother, father, sister, brother, husband, wife,
              spouse, son, daughter, uncle, aunt, cousin, grandma, grandpa, friend, boss, teacher, neighbor,
              roommate) and bare animal/pet words (dog, cat, pet, puppy, kitten). These are too generic on
              their own. Instead, qualify them with the possessor: extract "Nisha's dad" not "dad",
              "Jordan's dog" not "dog".
            - Bare generic objects that cannot be meaningfully qualified with a possessor, brand, or
              distinguishing detail (e.g., NEVER extract "supplies" from "I picked up some supplies")

            Your task is to extract **entity nodes** that are **explicitly** mentioned in the CURRENT MESSAGE.
            Pronoun references such as he/she/they or this/that/those should be disambiguated to the names of the
            reference entities. Only extract distinct entities from the CURRENT MESSAGE.

            <ENTITY TYPES>
            {{context.EntityTypesJson}}
            </ENTITY TYPES>

            <PREVIOUS MESSAGES>
            {{context.PreviousEpisodesJson}}
            </PREVIOUS MESSAGES>

            <CURRENT MESSAGE>
            {{context.EpisodeContent}}
            </CURRENT MESSAGE>

            1. **Speaker Extraction**: Always extract the speaker (the part before the colon `:` in each dialogue line) as the first entity node.
               - If the speaker is mentioned again in the message, treat both mentions as a **single entity**.

            2. **Entity Identification**:
               - Extract named entities and specific, concrete things that are **explicitly** mentioned in the CURRENT MESSAGE.
               - Only extract entities that are specific enough to be uniquely identifiable. Ask: "Could this have its own Wikipedia article or database entry, OR is it specific enough to distinguish from other items of the same category within this conversation?"
               - For objects, possessions, and physical items, extract when they are specific enough
                 to distinguish from other items of the same category. SHOULD be extracted:
                 - Brand-named items ("Gamecube", "Ford Mustang", "Moen faucet")
                 - Qualified items ("wool coat", "red and purple lighting", "cracked windshield",
                   "dog leash")
                 - Items with a concrete distinguishing descriptor (color, material, size, model,
                   owner, specific use)
                 Should NOT be extracted:
                 - Bare head nouns alone ("car", "coat", "game", "lighting", "windshield")
               - When a speaker or named person refers to a relative, pet, or associate using a bare term
                 (e.g., "my dad", "his cat"), extract the entity qualified with the possessor's name
                 (e.g., "Nisha's dad", "Jordan's cat"). Do NOT extract the bare term alone.
               - **Exclude** entities mentioned only in the PREVIOUS MESSAGES (they are for context only).

            3. **Entity Classification**:
               - Use the descriptions in ENTITY TYPES to classify each extracted entity.
               - Assign the appropriate `entity_type_id` for each one.

            4. **Exclusions**:
               - Do NOT extract entities representing relationships or actions.
               - Do NOT extract dates, times, or other temporal information - these will be handled separately.
               - When in doubt, do NOT extract.

            5. **Specificity**:
               - Always use the **most specific form** mentioned in the message. If the message says "road cycling",
                 extract "road cycling" not "cycling". If it says "wool coat", extract "wool coat" not "coat".
               - When context makes an object's type clear, include that context in the name. For example, if the
                 message mentions forgetting a leash while discussing a dog walk, extract "dog leash" not "leash".
               - If a phrase would not be distinguishable when read alone later, do NOT extract it.

            6. **Formatting**:
               - Be **explicit and unambiguous** in naming entities (e.g., use full names when available).

            <EXAMPLE>
            Message: "Jordan: We just moved to Denver last month. My spouse started a new role at Lockheed Martin and I enrolled in a ceramics workshop at the Belmont Arts Center."
            Good extractions: "Jordan" (speaker), "Denver" (Location), "Lockheed Martin" (Organization), "Belmont Arts Center" (Location), "ceramics" (Topic)
            Do NOT extract: "spouse" (generic reference - extract only if named), "new role" (not an entity), "last month" (temporal), "we" (pronoun)
            </EXAMPLE>

            <EXAMPLE>
            Message: "Nisha: My dad is visiting next week. He loves walking his dogs in Riverside Park."
            Good extractions: "Nisha" (speaker), "Nisha's dad" (Person), "Riverside Park" (Location)
            Do NOT extract: "dad" (bare relational term - qualify as "Nisha's dad"), "dogs" (bare animal word - no specific identity), "next week" (temporal)
            </EXAMPLE>

            <EXAMPLE>
            Message: "Mary: I forgot Trigger's leash so I couldn't take him on a dog walk. After that I went road cycling in my new wool coat."
            Good extractions: "Mary" (speaker), "Trigger" (animal name), "dog leash" (Object), "road cycling" (Topic), "wool coat" (Object)
            Do NOT extract: "leash" (too generic - use "dog leash"), "cycling" (too generic - use "road cycling"), "coat" (too generic - use "wool coat"), "dog walk" (activity, not an entity)
            </EXAMPLE>

            <EXAMPLE>
            Message: "Nate: My gaming room has red and purple lighting and I mostly play on a Gamecube. Last week the windshield on my Mustang got cracked."
            Good extractions: "Nate" (speaker), "gaming room" (Object), "red and purple lighting" (Object), "Gamecube" (Object), "Mustang" (Object), "cracked windshield" (Object)
            Do NOT extract: "lighting" (bare head noun - use "red and purple lighting"), "windshield" (bare head noun - use "cracked windshield"), "week" (temporal)
            </EXAMPLE>

            <EXAMPLE>
            Message: "Alex: I shared a pic from the game after the event."
            Good extractions: "Alex" (speaker)
            Do NOT extract: "pic" (generic media noun), "game" (generic event noun), "event" (generic event noun)
            </EXAMPLE>

            <EXAMPLE>
            Message: "Jordan: We won by a tight score. Scoring that last basket felt incredible."
            Good extractions: "Jordan" (speaker)
            Do NOT extract: "basket" (ambiguous bare noun that depends on sentence context)
            </EXAMPLE>

            {{context.CustomExtractionInstructions}}

            """;

        return new[]
        {
            new Message(
                "system",
                "You are an entity extraction specialist for conversational messages. " +
                "NEVER extract abstract concepts, feelings, or generic words."),
            new Message("user", userPrompt)
        };
    }

    internal static Message[] BuildExtractJson(in NodeExtractionContext context)
    {
        var userPrompt = $$"""

            NEVER extract:
            - Date, time, or timestamp values
            - Abstract concepts or generic field values (e.g., "true", "active", "pending")
            - Numeric IDs or codes that are not meaningful entity names
            - Bare relational or kinship terms (e.g., "spouse", "parent", "pet") - only extract if qualified
              with a possessor name
            - Bare generic objects or common nouns (e.g., "supplies", "tickets", "gear") - only extract if
              qualified with a distinguishing detail
            - Generic media/content nouns unless uniquely identified in the value itself (photo, pic, picture,
              image, video, post, story)
            - Generic event/activity nouns unless uniquely identified in the value itself (event, game, meeting,
              class, workshop, competition)
            - Broad institutional nouns unless explicitly named or uniquely qualified (government, school, company,
              team, office)
            - Ambiguous bare nouns whose meaning depends on surrounding text rather than the extracted value itself

            Extract entities from the JSON and classify each using the ENTITY TYPES above.

            <ENTITY TYPES>
            {{context.EntityTypesJson}}
            </ENTITY TYPES>

            <SOURCE DESCRIPTION>
            {{context.SourceDescription}}
            </SOURCE DESCRIPTION>

            <JSON>
            {{context.EpisodeContent}}
            </JSON>

            Guidelines:
            1. Extract the primary entity the JSON represents (e.g., a "name" or "user" field).
            2. Extract named entities referenced in other properties throughout the JSON structure.
            3. Only extract entities specific enough to be uniquely identifiable.
            4. Be explicit in naming entities - use full names when available.
            5. Use the most specific form present in the data (e.g., "road cycling" not "cycling").
            6. If a value would not be meaningful and distinguishable when read alone later, do NOT extract it.

            {{context.CustomExtractionInstructions}}

            <EXAMPLE>
            JSON: {"user": "Jordan Lee", "company": "Acme Corp", "role": "engineer", "start_date": "2024-01-15", "location": "Denver", "active": true}
            Good extractions: "Jordan Lee" (Person), "Acme Corp" (Organization), "Denver" (Location)
            Do NOT extract: "engineer" (role, not an entity), "2024-01-15" (date), "true" (field value)
            </EXAMPLE>

            <EXAMPLE>
            JSON: {"author": "Alex", "attachment_type": "photo", "event_name": "event", "agency": "government"}
            Good extractions: "Alex" (Person)
            Do NOT extract: "photo" (generic media noun), "event" (generic event noun), "government" (broad institutional noun)
            </EXAMPLE>

            """;

        return new[]
        {
            new Message(
                "system",
                "You are an entity extraction specialist for JSON data. " +
                "NEVER extract abstract concepts, dates, or generic field values."),
            new Message("user", userPrompt)
        };
    }

    internal static Message[] BuildExtractText(in NodeExtractionContext context)
    {
        var userPrompt = $$"""

            NEVER extract:
            - Pronouns (you, me, he, she, they, it, them, him, her, we, us, this, that, those)
            - Abstract concepts (joy, balance, growth, resilience, passion, motivation)
            - Generic common nouns or bare object words (day, life, people, work, stuff, things, food, time,
              tickets, supplies, clothes, keys, gear)
            - Generic media/content nouns unless uniquely identified in the node name itself (photo, pic, picture,
              image, video, post, story)
            - Generic event/activity nouns unless uniquely identified in the node name itself (event, game, meeting,
              class, workshop, competition)
            - Broad institutional nouns unless explicitly named or uniquely qualified (government, school, company,
              team, office)
            - Ambiguous bare nouns whose meaning depends on sentence context rather than the node name itself
            - Sentence fragments or clauses as entity names
            - Bare relational or kinship terms (dad, mom, sister, brother, spouse, friend, boss, pet, dog,
              cat) unless qualified with a possessor (e.g., "Nisha's dad" is acceptable, "dad" alone is not)
            - Bare generic objects that cannot be meaningfully qualified with a possessor, brand, or
              distinguishing detail (e.g., NEVER extract "supplies" from "I picked up some supplies")

            Extract entities from the TEXT that are **explicitly mentioned**.
            For each entity, classify it using the ENTITY TYPES above.
            Only extract entities specific enough to be uniquely identifiable - ask: "Could this have its own Wikipedia article or database entry?"

            <ENTITY TYPES>
            {{context.EntityTypesJson}}
            </ENTITY TYPES>

            <TEXT>
            {{context.EpisodeContent}}
            </TEXT>

            Guidelines:
            1. Extract named entities and specific, concrete things.
            2. Do not create nodes for relationships or actions.
            3. Do not create nodes for temporal information like dates, times or years.
            4. Be explicit in node names, using full names and avoiding abbreviations.
            5. Always use the most specific form from the text (e.g., "road cycling" not "cycling",
               "wool coat" not "coat"). Include qualifying context when it's clear from the text.
            6. When the text refers to a person's relative, pet, or associate by a bare term, qualify the
               entity with the possessor's name (e.g., "Dr. Osei's colleague" not "colleague").
            7. If a phrase would not be meaningful and distinguishable when read alone later, do NOT extract it.
            8. When in doubt, do NOT extract.

            {{context.CustomExtractionInstructions}}

            <EXAMPLE>
            Text: "Dr. Amara Osei presented her migraine study results at the AAN conference. The study tracked 340 patients using a new CGRP combination protocol."
            Good extractions: "Dr. Amara Osei" (Person), "AAN" (Organization), "migraine study" (Topic), "CGRP combination protocol" (Object)
            Do NOT extract: "results" (generic noun), "340" (number), "patients" (generic noun), "conference" (generic without a specific name)
            </EXAMPLE>

            <EXAMPLE>
            Text: "Alex shared a pic after the event and said scoring the last basket felt incredible."
            Good extractions: "Alex" (Person)
            Do NOT extract: "pic" (generic media noun), "event" (generic event noun), "basket" (ambiguous bare noun)
            </EXAMPLE>

            """;

        return new[]
        {
            new Message(
                "system",
                "You are an entity extraction specialist for unstructured text. " +
                "NEVER extract abstract concepts, feelings, or generic words."),
            new Message("user", userPrompt)
        };
    }

    internal static NodeAttributeExtractionContext BuildExtractAttributesContext(
        EntityNode node,
        EpisodicNode episode,
        IReadOnlyList<EpisodicNode> previousEpisodes)
    {
        return new NodeAttributeExtractionContext(
            PromptJson.Serialize(BuildPreviousEpisodesContext(previousEpisodes)),
            PromptJson.Serialize(JsonValue.Create(episode.Content)),
            PromptJson.Serialize(BuildAttributeNodeContext(node)));
    }

    internal static Message[] BuildExtractAttributes(in NodeAttributeExtractionContext context)
    {
        var userPrompt = $$"""
            Given the MESSAGES and the following ENTITY, update its attributes.

            HARD RULES - violating any of these is a failure:

            1. Each attribute value MUST be one of:
               (a) a clean value copied or directly normalized from text in MESSAGES,
               (b) the existing value already on the ENTITY (preserved unchanged), or
               (c) null / omitted, when neither (a) nor (b) applies.

            2. NEVER write reasoning, justification, or commentary into any field. Specifically:
               - NEVER include parenthetical explanations like "(implied by ...)", "(Context: ...)",
                 "(not explicitly stated ...)", "(based on ...)".
               - NEVER include first-person or deliberative phrases like "I should...", "However...",
                 "Sticking to...", "Since no...", "the instruction is to...", "must be kept...",
                 "if no value is present...".
               - NEVER list alternatives or candidates inside one field ("X, or Y, or maybe Z").
               - NEVER explain why a value is null. If unknown, set the field to null and stop.

            3. Each attribute schema description (e.g. an "Industry sector" field whose description
               reads "Industry classification, single word where possible") tells you the FORMAT a
               real value should take. The description text is NEVER itself a value. NEVER copy
               schema description text into the field.

            4. The literal strings "null", "N/A", "Not specified", "unknown", "none", "not provided",
               or any sentence describing absence are NOT valid values. If no value is supported by
               MESSAGES, set the field to null (or omit it) - do not write a sentence.

            5. Each attribute value must be a short, well-formed instance of the type the field
               describes (a phone number, an industry name, a URL, a postal address). If you cannot
               produce a clean value of that type from MESSAGES, the field is null.

            6. NEVER infer attribute values from the entity's name, from related entities, from
               generic world knowledge, or from prior summaries. Only verbatim or directly normalized
               text from MESSAGES qualifies as a new value.

            7. If MESSAGES contain no information about an attribute, leave the existing entity
               value unchanged. If the entity has no existing value, the field is null.

            EXAMPLES

            ENTITY: {"name": "Sam Rivera", "phones": "415-555-0142"}
            MESSAGES contain no phone information for Sam.
            GOOD -> "phones": "415-555-0142"   (preserved existing value)
            BAD  -> "phones": "415-555-0142 (implied by original entity, but no new information in
                    messages, retaining original value as per instruction...)"

            ENTITY: {"name": "Northwind", "industry": null}
            MESSAGES mention Northwind only as the platform some content was posted to.
            GOOD -> "industry": null   (no explicit industry classification was stated)
            BAD  -> "industry": "Content platform, SaaS (implied by usage context, though not stated
                    explicitly as industry classification...)"

            ENTITY: {"name": "Priya"}
            MESSAGES contain no phone for Priya, but discuss a project she contributed to.
            GOOD -> "phones": null
            BAD  -> "phones": "Worked with Lin and Marco on the Q3 launch..."   (off-topic content dump)

            <MESSAGES>
            {{context.PreviousEpisodesJson}}
            {{context.EpisodeContentJson}}
            </MESSAGES>

            <ENTITY>
            {{context.NodeJson}}
            </ENTITY>
            """;

        return new[]
        {
            new Message(
                "system",
                "You are an entity attribute extraction specialist. " +
                "You ONLY emit attribute values that are explicitly stated in MESSAGES or " +
                "already present on the ENTITY. You output strictly the JSON specified by the " +
                "response schema - no reasoning, no explanation, no commentary in any field."),
            new Message("user", userPrompt)
        };
    }

    internal static JsonArray BuildEntityTypesContext(
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        var context = new JsonArray
        {
            new JsonObject
            {
                ["entity_type_id"] = 0,
                ["entity_type_name"] = "Entity",
                ["entity_type_description"] = DefaultEntityTypeDescription
            }
        };
        if (entityTypes is null)
        {
            return context;
        }

        var index = 1;
        foreach (var pair in entityTypes)
        {
            context.Add(new JsonObject
            {
                ["entity_type_id"] = index++,
                ["entity_type_name"] = pair.Value.Name,
                ["entity_type_description"] = pair.Value.Description
            });
        }

        return context;
    }

    internal static JsonArray BuildPreviousEpisodesContext(IReadOnlyList<EpisodicNode> previousEpisodes)
    {
        var context = new JsonArray();
        for (var i = 0; i < previousEpisodes.Count; i++)
        {
            var episode = previousEpisodes[i];
            context.Add(new JsonObject
            {
                ["content"] = episode.Content,
                ["timestamp"] = GraphitiHelpers.EnsureUtc(episode.ValidAt).ToString("O")
            });
        }

        return context;
    }

    private static JsonObject BuildAttributeNodeContext(EntityNode node)
    {
        return new JsonObject
        {
            ["name"] = node.Name,
            ["entity_types"] = ExtractionContextBuilder.BuildStringArray(node.Labels),
            ["attributes"] = JsonSerializer.SerializeToNode(node.Attributes, GraphitiJsonSerializer.Options)
        };
    }
}
