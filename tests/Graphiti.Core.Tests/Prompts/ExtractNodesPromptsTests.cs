using Graphiti.Core.Models;
using Graphiti.Core.Models.Nodes;
using Graphiti.Core.Prompts;

namespace Graphiti.Core.Tests.Prompts;

/// <summary>
/// Golden tests pinning the rendered node-extraction prompts to the Python source
/// (graphiti_core/prompts/extract_nodes.py). The expected text is transcribed independently from
/// Python; if a test fails after an edit, reconcile against the Python file, not against the
/// builder.
/// </summary>
public class ExtractNodesPromptsTests
{
    private const string DefaultEntityTypeJson =
        """[{"entity_type_id":0,"entity_type_name":"Entity","entity_type_description":"A specific, identifiable entity that does not fit any of the other listed types. Must still be a concrete, meaningful thing - specific enough to be uniquely identifiable. GOOD: a named entity not covered by the other types. BAD: \"luck\", \"ideas\", \"tomorrow\", \"things\", \"them\", \"everybody\", \"a sense of wonder\", \"great times\". When in doubt, do not extract the entity."}]""";

    private static EpisodicNode CreateEpisode(string content, EpisodeType source) => new()
    {
        Name = "episode",
        Content = content,
        Source = source,
        SourceDescription = "a chat transcript",
        GroupId = "group",
        ValidAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
    };

    [Fact]
    public void BuildExtractMessage_RendersPythonParityPrompt()
    {
        var episode = CreateEpisode("Alice: I met Bob at Acme Corp.", EpisodeType.Message);
        var context = ExtractNodesPrompts.BuildContext(
            episode,
            Array.Empty<EpisodicNode>(),
            entityTypes: null,
            customExtractionInstructions: null);

        var messages = ExtractNodesPrompts.BuildExtractMessage(context);

        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(
            "You are an entity extraction specialist for conversational messages. " +
            "NEVER extract abstract concepts, feelings, or generic words.",
            messages[0].Content);
        Assert.Equal("user", messages[1].Role);

        var expected = $$"""

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
            {{DefaultEntityTypeJson}}
            </ENTITY TYPES>

            <PREVIOUS MESSAGES>
            []
            </PREVIOUS MESSAGES>

            <CURRENT MESSAGE>
            Alice: I met Bob at Acme Corp.
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



            """;
        Assert.Equal(expected, messages[1].Content);
    }

    [Fact]
    public void BuildExtractText_RendersPythonParityPrompt()
    {
        var episode = CreateEpisode("Dr. Amara Osei presented at AAN.", EpisodeType.Text);
        var context = ExtractNodesPrompts.BuildContext(
            episode,
            Array.Empty<EpisodicNode>(),
            entityTypes: null,
            customExtractionInstructions: null);

        var messages = ExtractNodesPrompts.BuildExtractText(context);

        Assert.Equal(
            "You are an entity extraction specialist for unstructured text. " +
            "NEVER extract abstract concepts, feelings, or generic words.",
            messages[0].Content);

        var expected = $$"""

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
            {{DefaultEntityTypeJson}}
            </ENTITY TYPES>

            <TEXT>
            Dr. Amara Osei presented at AAN.
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
        Assert.Equal(expected, messages[1].Content);
    }

    [Fact]
    public void BuildExtractJson_RendersPythonParityPrompt()
    {
        var episode = CreateEpisode("""{"user": "Jordan Lee"}""", EpisodeType.Json);
        var context = ExtractNodesPrompts.BuildContext(
            episode,
            Array.Empty<EpisodicNode>(),
            entityTypes: null,
            customExtractionInstructions: null);

        var messages = ExtractNodesPrompts.BuildExtractJson(context);

        Assert.Equal(
            "You are an entity extraction specialist for JSON data. " +
            "NEVER extract abstract concepts, dates, or generic field values.",
            messages[0].Content);

        var expected = $$"""

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
            {{DefaultEntityTypeJson}}
            </ENTITY TYPES>

            <SOURCE DESCRIPTION>
            a chat transcript
            </SOURCE DESCRIPTION>

            <JSON>
            {"user": "Jordan Lee"}
            </JSON>

            Guidelines:
            1. Extract the primary entity the JSON represents (e.g., a "name" or "user" field).
            2. Extract named entities referenced in other properties throughout the JSON structure.
            3. Only extract entities specific enough to be uniquely identifiable.
            4. Be explicit in naming entities - use full names when available.
            5. Use the most specific form present in the data (e.g., "road cycling" not "cycling").
            6. If a value would not be meaningful and distinguishable when read alone later, do NOT extract it.



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
        Assert.Equal(expected, messages[1].Content);
    }

    [Fact]
    public void BuildContext_RendersEntityTypesPreviousEpisodesAndCustomInstructions()
    {
        var episode = CreateEpisode("Alice: hello", EpisodeType.Message);
        var previous = new[]
        {
            new EpisodicNode
            {
                Name = "previous",
                Content = "Alice joined Acme Corp.",
                Source = EpisodeType.Message,
                SourceDescription = "a chat transcript",
                GroupId = "group",
                ValidAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
            }
        };
        var entityTypes = new Dictionary<string, EntityTypeDefinition>
        {
            ["Person"] = new("Person", "A human person.")
        };

        var context = ExtractNodesPrompts.BuildContext(
            episode,
            previous,
            entityTypes,
            customExtractionInstructions: "Focus on people.");

        Assert.Equal(
            """[{"content":"Alice joined Acme Corp.","timestamp":"2026-01-01T12:00:00.0000000Z"}]""",
            context.PreviousEpisodesJson);
        Assert.EndsWith(
            """,{"entity_type_id":1,"entity_type_name":"Person","entity_type_description":"A human person."}]""",
            context.EntityTypesJson);
        Assert.Equal("Focus on people.", context.CustomExtractionInstructions);

        var rendered = ExtractNodesPrompts.BuildExtractMessage(context)[1].Content;
        Assert.Contains(
            "<ENTITY TYPES>\n" + context.EntityTypesJson + "\n</ENTITY TYPES>",
            rendered,
            StringComparison.Ordinal);
        Assert.Contains(
            "<PREVIOUS MESSAGES>\n" + context.PreviousEpisodesJson + "\n</PREVIOUS MESSAGES>",
            rendered,
            StringComparison.Ordinal);
        Assert.EndsWith("</EXAMPLE>\n\nFocus on people.\n", rendered);
    }

    [Fact]
    public void Build_DispatchesOnEpisodeSourceLikePython()
    {
        var context = ExtractNodesPrompts.BuildContext(
            CreateEpisode("content", EpisodeType.Message),
            Array.Empty<EpisodicNode>(),
            entityTypes: null,
            customExtractionInstructions: null);

        Assert.Equal(
            ExtractNodesPrompts.BuildExtractMessage(context)[1].Content,
            ExtractNodesPrompts.Build(EpisodeType.Message, context)[1].Content);
        Assert.Equal(
            ExtractNodesPrompts.BuildExtractJson(context)[1].Content,
            ExtractNodesPrompts.Build(EpisodeType.Json, context)[1].Content);
        Assert.Equal(
            ExtractNodesPrompts.BuildExtractText(context)[1].Content,
            ExtractNodesPrompts.Build(EpisodeType.Text, context)[1].Content);
        Assert.Equal(
            ExtractNodesPrompts.BuildExtractText(context)[1].Content,
            ExtractNodesPrompts.Build(EpisodeType.FactTriple, context)[1].Content);
    }
}
