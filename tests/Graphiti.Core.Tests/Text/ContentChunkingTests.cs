using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.Core;
using Microsoft.Extensions.Options;

namespace Graphiti.Core.Tests.Text;

[Collection(global::Graphiti.Core.Tests.ContentChunkingTestCollection.Name)]
public class ContentChunkingTests
{
    [Fact]
    public void HeuristicTokenCounter_UsesFourCharactersPerToken()
    {
        var counter = new HeuristicTokenCounter();

        Assert.Equal(0, counter.CountTokens(string.Empty));
        Assert.Equal(0, counter.CountTokens("a"));
        Assert.Equal(0, counter.CountTokens("abc"));
        Assert.Equal(1, counter.CountTokens("abcd"));
        Assert.Equal(1, counter.CountTokens("abcde"));
        Assert.Equal(2, counter.CountTokens("abcdefgh"));
        Assert.Equal(999, counter.CountTokens(new string('a', 3999)));
        Assert.Equal(1000, counter.CountTokens(new string('a', 4000)));
        Assert.Equal(100, counter.CountTokens(new string('a', 400)));
        Assert.Equal(4, ContentChunking.CharsPerToken);
    }

    [Fact]
    public void TiktokenTokenCounter_CreateDefaultFallsBackForUnknownModel()
    {
        var cachedBefore = TiktokenTokenCounter.CachedTokenizerCount;
        var counter = TiktokenTokenCounter.CreateDefault("not-a-real-model-" + Guid.NewGuid());

        Assert.IsAssignableFrom<ITokenCounter>(counter);
        Assert.Equal(1, counter.CountTokens("abcd"));
        Assert.Equal(cachedBefore, TiktokenTokenCounter.CachedTokenizerCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TiktokenTokenCounter_CreateDefaultRejectsBlankModel(string model)
    {
        Assert.Throws<ArgumentException>(() => TiktokenTokenCounter.CreateDefault(model));
    }

    [Fact]
    public void TiktokenTokenCounter_TokenBoundaryMethodsAcceptSpanInputs()
    {
        if (TiktokenTokenCounter.CreateDefault() is not TiktokenTokenCounter counter)
        {
            return;
        }

        const string text = "one two three four five six";

        Assert.True(counter.TryGetIndexByTokenCount(text.AsSpan(), 2, out var end));
        Assert.InRange(end, 1, text.Length);
        Assert.True(counter.TryGetIndexByTokenCountFromEnd(text.AsSpan(), 2, out var start));
        Assert.InRange(start, 0, text.Length - 1);
    }

    [Fact]
    public void ChunkTextContent_UsesConfiguredTokenCounterForBudgeting()
    {
        var original = ContentChunking.TokenCounter;
        try
        {
            ContentChunking.TokenCounter = new WordTokenCounter();

            var chunks = ContentChunking.ChunkTextContent(
                "Alpha beta gamma. Delta epsilon zeta.",
                chunkSizeTokens: 4,
                overlapTokens: 1);

            Assert.True(chunks.Count > 1);
            Assert.All(chunks, chunk => Assert.True(ContentChunking.EstimateTokens(chunk) <= 4));
        }
        finally
        {
            ContentChunking.TokenCounter = original;
        }
    }

    [Fact]
    public void ChunkTextContent_UsesTokenBoundaryProviderForOversizedSentences()
    {
        var original = ContentChunking.TokenCounter;
        try
        {
            ContentChunking.TokenCounter = new CharacterTokenCounter();

            var chunks = ContentChunking.ChunkTextContent(
                "abcdefghij",
                chunkSizeTokens: 4,
                overlapTokens: 1);

            Assert.Equal(new[] { "abcd", "defg", "ghij" }, chunks);
            Assert.All(chunks, chunk => Assert.True(ContentChunking.EstimateTokens(chunk) <= 4));
        }
        finally
        {
            ContentChunking.TokenCounter = original;
        }
    }

    [Fact]
    public void DefaultContentChunker_UsesInjectedTokenBoundaryProviderForSplitting()
    {
        var original = ContentChunking.TokenCounter;
        try
        {
            ContentChunking.TokenCounter = new WordTokenCounter();
            var chunker = new DefaultContentChunker(
                new CharacterTokenCounter(),
                Options.Create(new ContentChunkingOptions
                {
                    ChunkTokenSize = 4,
                    ChunkOverlapTokens = 1
                }));

            var chunks = chunker.ChunkTextContent("abcdefghij");

            Assert.Equal(new[] { "abcd", "defg", "ghij" }, chunks);
            Assert.All(chunks, chunk => Assert.True(chunker.EstimateTokens(chunk) <= 4));
        }
        finally
        {
            ContentChunking.TokenCounter = original;
        }
    }

    [Fact]
    public void ChunkJsonContent_ReturnsSmallArrayAsSingleValidChunk()
    {
        const string content = "[{\"name\":\"Alice\"},{\"name\":\"Bob\"}]";

        var chunks = ContentChunking.ChunkJsonContent(content, chunkSizeTokens: 1000);

        Assert.Single(chunks);
        Assert.Equal(2, JsonDocument.Parse(chunks[0]).RootElement.GetArrayLength());
    }

    [Fact]
    public void ChunkJsonContent_SplitsArrayAtElementBoundaries()
    {
        var data = Enumerable.Range(0, 20)
            .Select(index => new Dictionary<string, object> { ["id"] = index, ["data"] = new string('x', 100) })
            .ToArray();
        var content = JsonSerializer.Serialize(data);

        var chunks = ContentChunking.ChunkJsonContent(content, chunkSizeTokens: 100, overlapTokens: 20);

        Assert.All(chunks, chunk =>
        {
            var parsed = JsonDocument.Parse(chunk).RootElement;
            Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
            Assert.All(parsed.EnumerateArray(), item => Assert.True(item.TryGetProperty("id", out _)));
        });
    }

    [Fact]
    public void ChunkJsonContent_PreservesAllArrayElements()
    {
        var content = JsonSerializer.Serialize(Enumerable.Range(0, 10).Select(index => new { id = index }));

        var chunks = ContentChunking.ChunkJsonContent(content, chunkSizeTokens: 50, overlapTokens: 10);
        var seenIds = chunks
            .SelectMany(chunk => JsonDocument.Parse(chunk).RootElement.EnumerateArray())
            .Select(item => item.GetProperty("id").GetInt32())
            .ToHashSet();

        Assert.Equal(Enumerable.Range(0, 10).ToHashSet(), seenIds);
    }

    [Fact]
    public void ChunkJsonContent_HonorsZeroOverlapWithoutRepeatingArrayElements()
    {
        var original = ContentChunking.TokenCounter;
        try
        {
            ContentChunking.TokenCounter = new WordTokenCounter();
            var content = JsonSerializer.Serialize(
                Enumerable.Range(0, 8).Select(index => new { id = index }));

            var chunks = ContentChunking.ChunkJsonContent(
                content,
                chunkSizeTokens: 5,
                overlapTokens: 0);
            var seenIds = chunks
                .SelectMany(chunk => JsonDocument.Parse(chunk).RootElement.EnumerateArray())
                .Select(item => item.GetProperty("id").GetInt32())
                .ToList();

            Assert.Equal(Enumerable.Range(0, 8), seenIds);
            Assert.Equal(seenIds.Count, seenIds.Distinct().Count());
        }
        finally
        {
            ContentChunking.TokenCounter = original;
        }
    }

    [Fact]
    public void ChunkJsonContent_SerializesArrayChunksWithEscapedValues()
    {
        var original = ContentChunking.TokenCounter;
        try
        {
            ContentChunking.TokenCounter = new WordTokenCounter();
            const string content = """
                [
                  {"text": "keep [ brackets ] and { braces }"},
                  "quote: \"hi\", slash: \\",
                  null
                ]
                """;

            var chunks = ContentChunking.ChunkJsonContent(
                content,
                chunkSizeTokens: 4,
                overlapTokens: 0);
            var merged = chunks
                .SelectMany(chunk => JsonNode.Parse(chunk)!.AsArray())
                .Select(node => node?.DeepClone())
                .ToList();

            Assert.True(chunks.Count > 1);
            Assert.Equal("keep [ brackets ] and { braces }", merged[0]?["text"]?.GetValue<string>());
            Assert.Equal("quote: \"hi\", slash: \\", merged[1]?.GetValue<string>());
            Assert.Null(merged[2]);
        }
        finally
        {
            ContentChunking.TokenCounter = original;
        }
    }

    [Fact]
    public void ChunkJsonContent_SplitsObjectAtKeyBoundaries()
    {
        var data = Enumerable.Range(0, 20).ToDictionary(index => $"key_{index}", _ => new string('x', 100));
        var content = JsonSerializer.Serialize(data);

        var chunks = ContentChunking.ChunkJsonContent(content, chunkSizeTokens: 100, overlapTokens: 20);

        Assert.All(chunks, chunk =>
        {
            var parsed = JsonDocument.Parse(chunk).RootElement;
            Assert.Equal(JsonValueKind.Object, parsed.ValueKind);
            Assert.All(parsed.EnumerateObject(), property => Assert.StartsWith("key_", property.Name));
        });
    }

    [Fact]
    public void ChunkJsonContent_PreservesAllObjectKeys()
    {
        var data = Enumerable.Range(0, 10).ToDictionary(index => $"key_{index}", index => $"value_{index}");
        var content = JsonSerializer.Serialize(data);

        var chunks = ContentChunking.ChunkJsonContent(content, chunkSizeTokens: 50, overlapTokens: 10);
        var seenKeys = chunks
            .SelectMany(chunk => JsonDocument.Parse(chunk).RootElement.EnumerateObject())
            .Select(property => property.Name)
            .ToHashSet();

        Assert.Equal(data.Keys.ToHashSet(), seenKeys);
    }

    [Fact]
    public void ChunkJsonContent_SerializesObjectChunksWithEscapedKeysAndNestedValues()
    {
        var original = ContentChunking.TokenCounter;
        try
        {
            ContentChunking.TokenCounter = new WordTokenCounter();
            const string content = """
                {
                  "quote\"key": {"text": "keep { braces } and \\ slash"},
                  "line\nkey": null,
                  "array[key]": ["value"]
                }
                """;

            var chunks = ContentChunking.ChunkJsonContent(
                content,
                chunkSizeTokens: 4,
                overlapTokens: 0);
            var merged = new JsonObject();
            foreach (var chunk in chunks)
            {
                var parsed = JsonNode.Parse(chunk)!.AsObject();
                foreach (var property in parsed)
                {
                    merged[property.Key] = property.Value?.DeepClone();
                }
            }

            Assert.True(chunks.Count > 1);
            Assert.Equal(
                "keep { braces } and \\ slash",
                merged["quote\"key"]?["text"]?.GetValue<string>());
            Assert.True(merged.ContainsKey("line\nkey"));
            Assert.Null(merged["line\nkey"]);
            Assert.Equal("value", merged["array[key]"]?[0]?.GetValue<string>());
        }
        finally
        {
            ContentChunking.TokenCounter = original;
        }
    }

    [Fact]
    public void ChunkJsonContent_HandlesEmptyAndScalarJson()
    {
        Assert.Equal(new[] { "[]" }, ContentChunking.ChunkJsonContent("[]", chunkSizeTokens: 100));
        Assert.Equal(new[] { "{}" }, ContentChunking.ChunkJsonContent("{}", chunkSizeTokens: 100));
        Assert.Equal(new[] { "123" }, ContentChunking.ChunkJsonContent("123", chunkSizeTokens: 100));
    }

    [Fact]
    public void ChunkJsonContent_InvalidJsonFallsBackToTextChunking()
    {
        const string invalidJson = "not valid json {";

        var chunks = ContentChunking.ChunkJsonContent(invalidJson, chunkSizeTokens: 1000);

        Assert.Contains(invalidJson, chunks[0]);
    }

    [Fact]
    public void ChunkTextContent_SplitsAtNaturalBoundariesAndPreservesWords()
    {
        const string text = "Alpha beta gamma delta epsilon zeta eta theta.";

        var chunks = ContentChunking.ChunkTextContent(text, chunkSizeTokens: 10, overlapTokens: 2);
        var foundWords = chunks
            .SelectMany(chunk => chunk.Replace(".", string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet();

        Assert.True(text.Replace(".", string.Empty).Split(' ').ToHashSet().IsSubsetOf(foundWords));
    }

    [Fact]
    public void ChunkTextContent_SplitsLargeParagraphBySentences()
    {
        var text = string.Join(" ", Enumerable.Range(0, 20).Select(index => $"This is sentence number {index}."));

        var chunks = ContentChunking.ChunkTextContent(text, chunkSizeTokens: 50, overlapTokens: 10);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.False(chunk.EndsWith(' ')));
    }

    [Fact]
    public void DefaultContentChunker_HonorsConfiguredZeroOverlap()
    {
        var chunker = new DefaultContentChunker(
            new WordTokenCounter(),
            Options.Create(new ContentChunkingOptions
            {
                ChunkTokenSize = 4,
                ChunkOverlapTokens = 0
            }));

        var chunks = chunker.ChunkTextContent("Alpha beta.\n\nGamma delta.\n\nEpsilon zeta.");

        Assert.Equal(new[] { "Alpha beta.", "Gamma delta.", "Epsilon zeta." }, chunks);
    }

    [Fact]
    public void ChunkTextContent_ParagraphSplitterSkipsWhitespaceOnlySeparators()
    {
        var original = ContentChunking.TokenCounter;
        try
        {
            ContentChunking.TokenCounter = new WordTokenCounter();

            var chunks = ContentChunking.ChunkTextContent(
                "  Alpha beta.  \r\n \t \n  Gamma delta.  \n\n   Epsilon zeta.   ",
                chunkSizeTokens: 2,
                overlapTokens: 0);

            Assert.Equal(new[] { "Alpha beta.", "Gamma delta.", "Epsilon zeta." }, chunks);
        }
        finally
        {
            ContentChunking.TokenCounter = original;
        }
    }

    [Fact]
    public void ChunkMessageContent_PreservesSpeakerMessageFormat()
    {
        var content = string.Join("\n", Enumerable.Range(0, 10).Select(index => $"Speaker{index}: This is message number {index}."));

        var chunks = ContentChunking.ChunkMessageContent(content, chunkSizeTokens: 50, overlapTokens: 10);

        Assert.All(chunks, chunk =>
        {
            var lines = chunk.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.All(lines, line => Assert.Contains(":", line));
        });
    }

    [Fact]
    public void ChunkMessageContent_ChunksJsonMessageArray()
    {
        var messages = Enumerable.Range(0, 10).Select(index => new { role = "user", content = $"Message {index}" });
        var content = JsonSerializer.Serialize(messages);

        var chunks = ContentChunking.ChunkMessageContent(content, chunkSizeTokens: 50, overlapTokens: 10);

        Assert.All(chunks, chunk =>
        {
            var parsed = JsonDocument.Parse(chunk).RootElement;
            Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
            Assert.All(parsed.EnumerateArray(), message => Assert.True(message.TryGetProperty("role", out _)));
        });
    }

    [Fact]
    public void ChunkMessageContent_LineFallbackUsesTokenAccurateOverlap()
    {
        var original = ContentChunking.TokenCounter;
        try
        {
            ContentChunking.TokenCounter = new CharacterTokenCounter();

            var chunks = ContentChunking.ChunkMessageContent(
                "abcdefghij",
                chunkSizeTokens: 4,
                overlapTokens: 1);

            Assert.Equal(new[] { "abcd", "defg", "ghij" }, chunks);
            Assert.All(chunks, chunk => Assert.True(ContentChunking.EstimateTokens(chunk) <= 4));
        }
        finally
        {
            ContentChunking.TokenCounter = original;
        }
    }

    [Fact]
    public void ChunkMessageContent_LineFallbackPreservesEmptyAndTrailingLines()
    {
        var original = ContentChunking.TokenCounter;
        try
        {
            ContentChunking.TokenCounter = new CharacterTokenCounter();

            var chunks = ContentChunking.ChunkMessageContent(
                "aa\n\nbb\n",
                chunkSizeTokens: 4,
                overlapTokens: 0);

            Assert.Equal(new[] { "aa\n", "bb\n" }, chunks);
            Assert.All(chunks, chunk => Assert.True(ContentChunking.EstimateTokens(chunk) <= 4));
        }
        finally
        {
            ContentChunking.TokenCounter = original;
        }
    }

    [Fact]
    public void ShouldChunk_OnlyChunksLargeDenseContent()
    {
        Assert.False(ContentChunking.ShouldChunk(string.Empty, EpisodeType.Text));
        Assert.False(ContentChunking.ShouldChunk("[{\"name\":\"Entity\"}]", EpisodeType.Json, minTokens: 1000, densityThreshold: 0.001));

        var denseJson = JsonSerializer.Serialize(
            Enumerable.Range(0, 200).Select(index => new { name = $"Entity{index}", desc = new string('x', 20) }));
        Assert.True(ContentChunking.ShouldChunk(denseJson, EpisodeType.Json, minTokens: 500, densityThreshold: 0.01));

        var prose = string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog. ", 50));
        Assert.False(ContentChunking.ShouldChunk(prose, EpisodeType.Text, minTokens: 100, densityThreshold: 0.05));
    }

    [Fact]
    public void DensityHelpers_MatchJsonAndTextHeuristics()
    {
        var denseObject = JsonSerializer.Serialize(Enumerable.Range(0, 50).ToDictionary(index => $"key_{index}", index => $"value_{index}"));
        Assert.True(ContentChunking.JsonLikelyDense(denseObject, ContentChunking.EstimateTokens(denseObject), densityThreshold: 0.01));

        const string nested = "{\"a\":1,\"b\":{\"c\":2,\"d\":3},\"e\":[{\"f\":4},{\"g\":5}]}";
        Assert.Equal(7, ContentChunking.CountJsonKeys(nested, maxDepth: 2));

        var entityRichText = string.Concat(Enumerable.Repeat(
            "Alice met Bob at Acme Corp. Then Carol and David joined them. ",
            10));
        Assert.True(ContentChunking.TextLikelyDense(entityRichText, ContentChunking.EstimateTokens(entityRichText), densityThreshold: 0.01));

        var sentenceStarters = string.Concat(Enumerable.Repeat("This is a sentence. Another one follows. Yet another here. ", 50));
        Assert.False(ContentChunking.TextLikelyDense(sentenceStarters, ContentChunking.EstimateTokens(sentenceStarters), densityThreshold: 0.05));
    }

    [Fact]
    public void TextLikelyDense_HandlesLongSparseTextWithoutFalsePositives()
    {
        var sentenceStarters = string.Concat(
            Enumerable.Repeat("This is a sentence. Another one follows. Yet another here. ", 1000));

        Assert.False(ContentChunking.TextLikelyDense(sentenceStarters, tokens: 10_000, densityThreshold: 0.01));
    }

    [Fact]
    public void TextLikelyDense_HandlesLongEntityRichText()
    {
        var entityRichText = string.Concat(
            Enumerable.Repeat("met Alice with Bob at Acme Corp near Paris ", 1000));

        Assert.True(ContentChunking.TextLikelyDense(entityRichText, tokens: 10_000, densityThreshold: 0.01));
    }

    [Fact]
    public void TextLikelyDense_IgnoresFirstWordsSentenceStartersAndAllCaps()
    {
        const string text = "Alice. Bob! Carol? NASA USA. ALPHA BETA!";

        Assert.False(ContentChunking.TextLikelyDense(text, tokens: 10, densityThreshold: 0.001));
    }

    [Fact]
    public void TextLikelyDense_PreservesPythonWhitespaceSplitting()
    {
        Assert.True(ContentChunking.TextLikelyDense("  Alice Bob", tokens: 100, densityThreshold: 0.01));
        Assert.True(ContentChunking.TextLikelyDense("x\tAlice\nBob", tokens: 100, densityThreshold: 0.01));
    }

    [Fact]
    public void TextLikelyDense_UsesRawPreviousWordForSentenceBoundary()
    {
        Assert.False(ContentChunking.TextLikelyDense("end. Alice", tokens: 100, densityThreshold: 0.001));
        Assert.True(ContentChunking.TextLikelyDense("end.\" Alice", tokens: 100, densityThreshold: 0.001));
    }

    [Fact]
    public void TextLikelyDense_UsesStrictThresholdComparison()
    {
        Assert.False(ContentChunking.TextLikelyDense("lead Alice", tokens: 1000, densityThreshold: 0.002));
        Assert.True(ContentChunking.TextLikelyDense("lead Alice", tokens: 999, densityThreshold: 0.002));
    }

    [Fact]
    public void TextLikelyDense_TrimsPythonPunctuationForEntityWords()
    {
        const string text = "lead Alice met Carol near (Delta) and [Eve] while NASA stayed quiet.";

        Assert.True(ContentChunking.TextLikelyDense(text, tokens: 100, densityThreshold: 0.05));
    }

    [Fact]
    public void GenerateCoveringChunks_ReturnsSingleEmptyChunkForEmptyInput()
    {
        var result = ContentChunking.GenerateCoveringChunks(Array.Empty<string>(), k: 3);

        var chunk = Assert.Single(result);
        Assert.Empty(chunk.Items);
        Assert.Empty(chunk.Indices);
    }

    [Fact]
    public void GenerateCoveringChunks_ReturnsSingleChunkWhenItemsFit()
    {
        var items = new[] { "A", "B", "C" };

        var result = ContentChunking.GenerateCoveringChunks(items, k: 5);

        var chunk = Assert.Single(result);
        Assert.Equal(items, chunk.Items);
        Assert.Equal(new[] { 0, 1, 2 }, chunk.Indices);
    }

    [Fact]
    public void GenerateCoveringChunks_ReturnsSingleChunkWhenItemCountEqualsChunkSize()
    {
        var items = new[] { "A", "B", "C", "D" };

        var result = ContentChunking.GenerateCoveringChunks(items, k: 4);

        var chunk = Assert.Single(result);
        Assert.Equal(items, chunk.Items);
        Assert.Equal(new[] { 0, 1, 2, 3 }, chunk.Indices);
    }

    [Fact]
    public void GenerateCoveringChunks_CoversAllPairsForKTwo()
    {
        var result = ContentChunking.GenerateCoveringChunks(new[] { "A", "B", "C", "D" }, k: 2);

        var coveredPairs = CoveredPairs(result);
        var expectedPairs = ExpectedPairs(4);

        Assert.Equal(expectedPairs, coveredPairs);
    }

    [Fact]
    public void GenerateCoveringChunks_CoversAllPairsForLargerInputs()
    {
        var items = Enumerable.Range(0, 30).ToArray();

        var result = ContentChunking.GenerateCoveringChunks(items, k: 5);
        var coveredPairs = CoveredPairs(result);

        Assert.All(result, chunk => Assert.True(chunk.Indices.Count <= 5));
        Assert.Equal(ExpectedPairs(30), coveredPairs);
    }

    [Fact]
    public void GenerateCoveringChunks_IsDeterministicForLargeCombinationSpaces()
    {
        var items = Enumerable.Range(0, 50).ToArray();

        var first = ContentChunking.GenerateCoveringChunks(items, k: 5);
        var second = ContentChunking.GenerateCoveringChunks(items, k: 5);

        Assert.Equal(ChunkIndexKeys(first), ChunkIndexKeys(second));
    }

    [Theory]
    [InlineData(20, 10, 5)]
    [InlineData(30, 15, 5)]
    [InlineData(50, 5, 0)]
    [InlineData(100, 4, 0)]
    public void GenerateCoveringChunks_CoversPythonSampledEdgeCases(
        int n,
        int k,
        int minimumChunkCount)
    {
        var items = Enumerable.Range(0, n).ToArray();

        var result = ContentChunking.GenerateCoveringChunks(items, k);

        Assert.All(result, chunk => Assert.True(chunk.Indices.Count <= k));
        Assert.Equal(ExpectedPairs(n), CoveredPairs(result));
        if (minimumChunkCount > 0)
        {
            Assert.True(result.Count >= minimumChunkCount);
        }
    }

    [Fact]
    public void GenerateCoveringChunks_MapsIndicesToOriginalItems()
    {
        var items = new[] { "Alice", "Bob", "Carol", "Dave", "Eve" };

        var result = ContentChunking.GenerateCoveringChunks(items, k: 3);

        Assert.All(result, chunk =>
        {
            for (var i = 0; i < chunk.Indices.Count; i++)
            {
                Assert.Equal(items[chunk.Indices[i]], chunk.Items[i]);
            }
        });
    }

    [Fact]
    public void GenerateCoveringChunks_WorksWithCustomItemTypes()
    {
        var items = new[] { new Entity("A"), new Entity("B"), new Entity("C"), new Entity("D") };

        var result = ContentChunking.GenerateCoveringChunks(items, k: 2);

        Assert.All(result, chunk =>
        {
            Assert.Equal(2, chunk.Items.Count);
            Assert.All(chunk.Items, item => Assert.IsType<Entity>(item));
        });
        Assert.Equal(ExpectedPairs(items.Length), CoveredPairs(result));
    }

    private sealed record Entity(string Name);

    private static HashSet<string> CoveredPairs<T>(IReadOnlyList<CoveringChunk<T>> chunks)
    {
        var coveredPairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            for (var i = 0; i < chunk.Indices.Count; i++)
            {
                for (var j = i + 1; j < chunk.Indices.Count; j++)
                {
                    coveredPairs.Add(PairKey(chunk.Indices[i], chunk.Indices[j]));
                }
            }
        }

        return coveredPairs;
    }

    private static HashSet<string> ExpectedPairs(int n)
    {
        var expectedPairs = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                expectedPairs.Add(PairKey(i, j));
            }
        }

        return expectedPairs;
    }

    private static List<string> ChunkIndexKeys<T>(IReadOnlyList<CoveringChunk<T>> chunks) =>
        chunks.Select(chunk => string.Join(",", chunk.Indices)).ToList();

    private static string PairKey(int first, int second) =>
        first < second ? $"{first}:{second}" : $"{second}:{first}";

    private sealed class WordTokenCounter : ITokenCounter
    {
        public int CountTokens(string? text) =>
            string.IsNullOrWhiteSpace(text)
                ? 0
                : text.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private sealed class CharacterTokenCounter : ITokenCounter, ITokenBoundaryProvider
    {
        public int CountTokens(string? text) => text?.Length ?? 0;

        public bool TryGetIndexByTokenCount(ReadOnlySpan<char> text, int maxTokens, out int index)
        {
            index = maxTokens <= 0 ? 0 : Math.Min(text.Length, maxTokens);
            return true;
        }

        public bool TryGetIndexByTokenCountFromEnd(ReadOnlySpan<char> text, int maxTokens, out int index)
        {
            index = maxTokens <= 0 ? text.Length : Math.Max(0, text.Length - maxTokens);
            return true;
        }
    }
}
