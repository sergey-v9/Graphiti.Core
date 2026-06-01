using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Graphiti.Core.Text;

/// <summary>
/// The static content-chunking algorithms shared by <see cref="DefaultContentChunker"/>. Includes
/// token estimation, density heuristics that decide when chunking is worthwhile, format-specific
/// chunkers (JSON/text/message), and the covering-chunk combinatorial generator. The active token
/// counter can be overridden ambiently via <see cref="TokenCounter"/>.
/// </summary>
public static partial class ContentChunking
{
    /// <summary>Assumed average characters per token for heuristic estimates.</summary>
    public const int CharsPerToken = 4;

    /// <summary>Default target chunk size in tokens.</summary>
    public const int DefaultChunkTokenSize = 3000;

    /// <summary>Default overlap, in tokens, carried between consecutive chunks.</summary>
    public const int DefaultChunkOverlapTokens = 200;

    /// <summary>Default minimum token count below which content is not chunked.</summary>
    public const int DefaultChunkMinTokens = 1000;

    /// <summary>Default density threshold for deciding whether to chunk.</summary>
    public const double DefaultChunkDensityThreshold = 0.15;

    /// <summary>Upper bound on combinations evaluated before switching to the greedy covering strategy.</summary>
    public const int MaxCombinationsToEvaluate = 1000;
    private const int DeterministicGreedyCandidateLimit = 128;
    private const int DeterministicGreedyVertexSeedLimit = 32;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly AsyncLocal<ITokenCounter?> ScopedTokenCounter = new();
    private static ITokenCounter _tokenCounter = TiktokenTokenCounter.CreateDefault();

    /// <summary>
    /// The token counter used by the parameterless overloads. A scoped (async-local) override takes
    /// precedence over the process-wide default when set.
    /// </summary>
    public static ITokenCounter TokenCounter
    {
        get => ScopedTokenCounter.Value ?? _tokenCounter;
        set => _tokenCounter = value ?? throw new ArgumentNullException(nameof(value));
    }

    internal static IDisposable UseTokenCounter(ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);
        var previous = ScopedTokenCounter.Value;
        ScopedTokenCounter.Value = tokenCounter;
        return new TokenCounterScope(previous);
    }

    /// <summary>Estimates the number of tokens in <paramref name="text"/> using <see cref="TokenCounter"/>.</summary>
    public static int EstimateTokens(string? text) => EstimateTokens(text, TokenCounter);

    internal static int EstimateTokens(string? text, ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);
        return tokenCounter.CountTokens(text);
    }

    /// <summary>
    /// Returns <c>true</c> if content exceeds the minimum token count and is dense enough to benefit
    /// from chunking; the density test differs for JSON versus free text.
    /// </summary>
    public static bool ShouldChunk(
        string? content,
        EpisodeType episodeType,
        int? minTokens = null,
        double? densityThreshold = null) =>
        ShouldChunk(content, episodeType, minTokens, densityThreshold, TokenCounter);

    internal static bool ShouldChunk(
        string? content,
        EpisodeType episodeType,
        int? minTokens,
        double? densityThreshold,
        ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);
        var source = content ?? string.Empty;
        var tokens = EstimateTokens(source, tokenCounter);
        var minimumTokens = minTokens ?? DefaultChunkMinTokens;

        if (tokens < minimumTokens)
        {
            return false;
        }

        return episodeType == EpisodeType.Json
            ? JsonLikelyDense(source, tokens, densityThreshold)
            : TextLikelyDense(source, tokens, densityThreshold);
    }

    /// <summary>Heuristically determines whether JSON content is "dense" (many elements/keys per token).</summary>
    public static bool JsonLikelyDense(string? content, int tokens, double? densityThreshold = null)
    {
        var threshold = densityThreshold ?? DefaultChunkDensityThreshold;

        try
        {
            using var document = JsonDocument.Parse(content ?? string.Empty);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                var elementCount = root.GetArrayLength();
                var density = tokens > 0 ? (elementCount / (double)tokens) * 1000 : 0;
                return density > threshold * 1000;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                var keyCount = CountJsonKeys(root);
                var density = tokens > 0 ? (keyCount / (double)tokens) * 1000 : 0;
                return density > threshold * 1000;
            }

            return false;
        }
        catch (JsonException)
        {
            return TextLikelyDense(content, tokens, densityThreshold);
        }
    }

    /// <summary>Counts object keys in a JSON string up to <paramref name="maxDepth"/> levels deep.</summary>
    public static int CountJsonKeys(string json, int maxDepth = 2)
    {
        using var document = JsonDocument.Parse(json);
        return CountJsonKeys(document.RootElement, maxDepth);
    }

    /// <summary>Counts object keys in a parsed JSON element up to <paramref name="maxDepth"/> levels deep.</summary>
    public static int CountJsonKeys(JsonElement data, int maxDepth = 2, int currentDepth = 0)
    {
        if (data.ValueKind != JsonValueKind.Object || currentDepth >= maxDepth)
        {
            return 0;
        }

        var count = 0;
        foreach (var property in data.EnumerateObject())
        {
            count++;
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                count += CountJsonKeys(property.Value, maxDepth, currentDepth + 1);
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        count += CountJsonKeys(item, maxDepth, currentDepth + 1);
                    }
                }
            }
        }

        return count;
    }

    /// <summary>Heuristically determines whether free text is "dense" based on capitalized-word frequency.</summary>
    public static bool TextLikelyDense(string? content, int tokens, double? densityThreshold = null)
    {
        if (tokens == 0)
        {
            return false;
        }

        var source = (content ?? string.Empty).AsSpan();
        var index = 0;
        var wordIndex = 0;
        var previousWordEndsSentence = false;
        var capitalizedCount = 0;

        while (TryReadNextWhitespaceWord(source, ref index, out var word))
        {
            if (wordIndex > 0 && !previousWordEndsSentence)
            {
                var cleaned = TrimDensityWord(word);
                if (cleaned.Length > 0 && char.IsUpper(cleaned[0]) && !IsUpperLikePython(cleaned))
                {
                    capitalizedCount++;
                }
            }

            previousWordEndsSentence = PreviousWordEndsSentence(word);
            wordIndex++;
        }

        if (wordIndex == 0)
        {
            return false;
        }

        var density = (capitalizedCount / (double)tokens) * 1000;
        var threshold = densityThreshold ?? DefaultChunkDensityThreshold;
        return density > threshold * 500;
    }

    /// <summary>
    /// Chunks JSON content, splitting arrays/objects element-wise so each chunk stays within the token
    /// budget; falls back to text chunking if the content is not valid JSON.
    /// </summary>
    public static IReadOnlyList<string> ChunkJsonContent(
        string content,
        int? chunkSizeTokens = null,
        int? overlapTokens = null) =>
        ChunkJsonContent(content, chunkSizeTokens, overlapTokens, TokenCounter);

    internal static IReadOnlyList<string> ChunkJsonContent(
        string content,
        int? chunkSizeTokens,
        int? overlapTokens,
        ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);
        var chunkTokenBudget = ResolveTokenCount(chunkSizeTokens, DefaultChunkTokenSize);
        var overlapTokenBudget = ResolveOverlapTokenCount(overlapTokens);

        JsonNode? data;
        try
        {
            data = JsonNode.Parse(content);
        }
        catch (JsonException)
        {
            return ChunkTextContent(content, chunkSizeTokens, overlapTokens, tokenCounter);
        }

        return data switch
        {
            JsonArray array => ChunkJsonArray(MaterializeJsonArrayElements(array), chunkTokenBudget, overlapTokenBudget, tokenCounter),
            JsonObject jsonObject => ChunkJsonObject(jsonObject, chunkTokenBudget, overlapTokenBudget, tokenCounter),
            _ => new[] { content }
        };
    }

    /// <summary>
    /// Chunks free text along paragraph then sentence boundaries, applying token overlap between chunks.
    /// </summary>
    public static IReadOnlyList<string> ChunkTextContent(
        string content,
        int? chunkSizeTokens = null,
        int? overlapTokens = null) =>
        ChunkTextContent(content, chunkSizeTokens, overlapTokens, TokenCounter);

    internal static IReadOnlyList<string> ChunkTextContent(
        string content,
        int? chunkSizeTokens,
        int? overlapTokens,
        ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);
        var chunkTokenBudget = ResolveTokenCount(chunkSizeTokens, DefaultChunkTokenSize);
        var overlapTokenBudget = ResolveOverlapTokenCount(overlapTokens);

        if (FitsTokenBudget(content, chunkTokenBudget, tokenCounter))
        {
            return new[] { content };
        }

        var paragraphs = MaterializeParagraphs(content);
        if (paragraphs.Count <= 1)
        {
            return NormalizeTextChunks(
                ChunkBySentences(content.Trim(), chunkTokenBudget, overlapTokenBudget, tokenCounter),
                content,
                chunkTokenBudget,
                overlapTokenBudget,
                tokenCounter);
        }

        var chunks = new List<string>();
        var currentChunk = new List<string>();
        var currentSize = 0;

        foreach (var paragraph in paragraphs)
        {
            var paragraphSize = MeasureBudgetSize(paragraph, tokenCounter);
            if (paragraphSize > chunkTokenBudget)
            {
                if (currentChunk.Count > 0)
                {
                    AddNonDuplicateChunk(chunks, JoinChunkParts(currentChunk, "\n\n"));
                    currentChunk.Clear();
                    currentSize = 0;
                }

                foreach (var chunk in ChunkBySentences(paragraph, chunkTokenBudget, overlapTokenBudget, tokenCounter))
                {
                    AddNonDuplicateChunk(chunks, chunk);
                }
            }
            else
            {
                if (currentChunk.Count > 0 && currentSize + paragraphSize + 1 > chunkTokenBudget)
                {
                    var joined = JoinChunkParts(currentChunk, "\n\n");
                    AddNonDuplicateChunk(chunks, joined);

                    var overlapText = GetOverlapText(joined, overlapTokenBudget, tokenCounter);
                    if (overlapText.Length > 0)
                    {
                        currentChunk = new List<string> { overlapText };
                        currentSize = MeasureBudgetSize(overlapText, tokenCounter);
                    }
                    else
                    {
                        currentChunk.Clear();
                        currentSize = 0;
                    }
                }

                currentChunk.Add(paragraph);
                currentSize += paragraphSize + 1;
            }
        }

        if (currentChunk.Count > 0)
        {
            AddNonDuplicateChunk(chunks, JoinChunkParts(currentChunk, "\n\n"));
        }

        return NormalizeTextChunks(chunks, content, chunkTokenBudget, overlapTokenBudget, tokenCounter);
    }

    /// <summary>
    /// Chunks chat/message content. JSON arrays are chunked element-wise, speaker-prefixed transcripts
    /// are split on speaker turns, and everything else falls back to line-based chunking.
    /// </summary>
    public static IReadOnlyList<string> ChunkMessageContent(
        string content,
        int? chunkSizeTokens = null,
        int? overlapTokens = null) =>
        ChunkMessageContent(content, chunkSizeTokens, overlapTokens, TokenCounter);

    internal static IReadOnlyList<string> ChunkMessageContent(
        string content,
        int? chunkSizeTokens,
        int? overlapTokens,
        ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);
        var chunkTokenBudget = ResolveTokenCount(chunkSizeTokens, DefaultChunkTokenSize);
        var overlapTokenBudget = ResolveOverlapTokenCount(overlapTokens);

        if (FitsTokenBudget(content, chunkTokenBudget, tokenCounter))
        {
            return new[] { content };
        }

        try
        {
            var node = JsonNode.Parse(content);
            if (node is JsonArray array)
            {
                return ChunkJsonArray(MaterializeJsonArrayElements(array), chunkTokenBudget, overlapTokenBudget, tokenCounter);
            }
        }
        catch (JsonException)
        {
        }

        if (SpeakerSearchRegex().IsMatch(content))
        {
            return ChunkSpeakerMessages(content, chunkTokenBudget, overlapTokenBudget, tokenCounter);
        }

        return ChunkByLines(content, chunkTokenBudget, overlapTokenBudget, tokenCounter);
    }

    /// <summary>
    /// Produces chunks of <paramref name="k"/> items such that every pair of items co-occurs in at least
    /// one chunk. Uses exhaustive combinations for small inputs and a deterministic greedy strategy once
    /// the combination count would exceed <see cref="MaxCombinationsToEvaluate"/>.
    /// </summary>
    public static IReadOnlyList<CoveringChunk<T>> GenerateCoveringChunks<T>(IReadOnlyList<T> items, int k)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (k <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k), k, "Chunk size must be positive.");
        }

        var n = items.Count;
        if (n <= k)
        {
            return new[] { ToCoveringChunkFromRange(items, n) };
        }

        if (k == 1)
        {
            var pairChunks = new List<CoveringChunk<T>>();
            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    pairChunks.Add(ToCoveringChunk(items, i, j));
                }
            }

            return pairChunks;
        }

        var uncoveredPairs = new HashSet<(int First, int Second)>();
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                uncoveredPairs.Add((i, j));
            }
        }

        var chunks = new List<CoveringChunk<T>>();
        var useGreedyCandidates = CombinationCountExceeds(n, k, MaxCombinationsToEvaluate);

        while (uncoveredPairs.Count > 0)
        {
            int[]? bestChunkIndices = null;
            var bestCoveredCount = 0;

            if (useGreedyCandidates)
            {
                foreach (var chunkIndices in GenerateDeterministicGreedyCandidates(n, k, uncoveredPairs))
                {
                    var coveredCount = CountCoveredPairs(chunkIndices, uncoveredPairs);
                    if (IsBetterCoveringCandidate(
                            chunkIndices,
                            coveredCount,
                            bestChunkIndices,
                            bestCoveredCount))
                    {
                        bestCoveredCount = coveredCount;
                        bestChunkIndices = chunkIndices;
                    }
                }
            }
            else
            {
                foreach (var chunkIndices in GenerateCombinations(n, k))
                {
                    var coveredCount = CountCoveredPairs(chunkIndices, uncoveredPairs);
                    if (IsBetterCoveringCandidate(
                            chunkIndices,
                            coveredCount,
                            bestChunkIndices,
                            bestCoveredCount))
                    {
                        bestCoveredCount = coveredCount;
                        bestChunkIndices = chunkIndices;
                    }
                }
            }

            if (bestChunkIndices is null || bestCoveredCount == 0)
            {
                break;
            }

            RemoveCoveredPairs(bestChunkIndices, uncoveredPairs);
            chunks.Add(ToCoveringChunk(items, bestChunkIndices));
        }

        var remainingPairs = CopyPairs(uncoveredPairs);
        remainingPairs.Sort(ComparePairsByFirstThenSecond);
        foreach (var pair in remainingPairs)
        {
            chunks.Add(ToCoveringChunk(items, pair.First, pair.Second));
        }

        return chunks;
    }

    private static List<string> MaterializeJsonArrayElements(JsonArray array)
    {
        var elements = new List<string>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            elements.Add(array[i]?.ToJsonString(JsonOptions) ?? "null");
        }

        return elements;
    }

    private static List<string> ChunkJsonArray(
        List<string> elements,
        int chunkTokenBudget,
        int overlapTokenBudget,
        ITokenCounter tokenCounter)
    {
        if (elements.Count == 0)
        {
            return new List<string> { "[]" };
        }

        var chunks = new List<string>();
        var currentElements = new List<string>();
        var currentSize = MeasureBudgetSize("[]", tokenCounter);

        foreach (var elementJson in elements)
        {
            var elementSize = MeasureBudgetSize(elementJson, tokenCounter) + 1;
            if (currentElements.Count > 0 && currentSize + elementSize > chunkTokenBudget)
            {
                chunks.Add(SerializeArray(currentElements));
                currentElements = GetOverlapElements(currentElements, overlapTokenBudget, tokenCounter);
                currentSize = currentElements.Count > 0
                    ? MeasureBudgetSize(SerializeArray(currentElements), tokenCounter)
                    : MeasureBudgetSize("[]", tokenCounter);
            }

            currentElements.Add(elementJson);
            currentSize += elementSize;
        }

        if (currentElements.Count > 0)
        {
            chunks.Add(SerializeArray(currentElements));
        }

        return chunks.Count > 0 ? chunks : new List<string> { "[]" };
    }

    private static List<string> GetOverlapElements(
        List<string> elements,
        int overlapTokenBudget,
        ITokenCounter tokenCounter)
    {
        if (elements.Count == 0)
        {
            return new List<string>();
        }

        var overlapElements = new List<string>();
        var currentSize = MeasureBudgetSize("[]", tokenCounter);

        for (var i = elements.Count - 1; i >= 0; i--)
        {
            var elementSize = MeasureBudgetSize(elements[i], tokenCounter) + 1;
            if (currentSize + elementSize > overlapTokenBudget)
            {
                break;
            }

            overlapElements.Insert(0, elements[i]);
            currentSize += elementSize;
        }

        return overlapElements;
    }

    private static IReadOnlyList<string> ChunkJsonObject(
        JsonObject data,
        int chunkTokenBudget,
        int overlapTokenBudget,
        ITokenCounter tokenCounter)
    {
        var entries = MaterializeJsonObjectEntries(data);
        if (entries.Count == 0)
        {
            return new[] { "{}" };
        }

        var chunks = new List<string>();
        var currentEntries = new List<KeyValuePair<string, string>>();
        var currentSize = MeasureBudgetSize("{}", tokenCounter);

        foreach (var entry in entries)
        {
            var entrySize = MeasureBudgetSize(SerializeObjectEntry(entry), tokenCounter);
            if (currentEntries.Count > 0 && currentSize + entrySize > chunkTokenBudget)
            {
                chunks.Add(SerializeObject(currentEntries));
                currentEntries = GetOverlapEntries(currentEntries, overlapTokenBudget, tokenCounter);
                currentSize = currentEntries.Count > 0
                    ? MeasureBudgetSize(SerializeObject(currentEntries), tokenCounter)
                    : MeasureBudgetSize("{}", tokenCounter);
            }

            currentEntries.Add(entry);
            currentSize += entrySize;
        }

        if (currentEntries.Count > 0)
        {
            chunks.Add(SerializeObject(currentEntries));
        }

        return chunks.Count > 0 ? chunks : new[] { "{}" };
    }

    private static List<KeyValuePair<string, string>> GetOverlapEntries(
        List<KeyValuePair<string, string>> entries,
        int overlapTokenBudget,
        ITokenCounter tokenCounter)
    {
        if (entries.Count == 0)
        {
            return new List<KeyValuePair<string, string>>();
        }

        var overlapEntries = new List<KeyValuePair<string, string>>();
        var currentSize = MeasureBudgetSize("{}", tokenCounter);

        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var entrySize = MeasureBudgetSize(SerializeObjectEntry(entries[i]), tokenCounter);
            if (currentSize + entrySize > overlapTokenBudget)
            {
                break;
            }

            overlapEntries.Insert(0, entries[i]);
            currentSize += entrySize;
        }

        return overlapEntries;
    }

    private static List<string> ChunkBySentences(
        string text,
        int chunkTokenBudget,
        int overlapTokenBudget,
        ITokenCounter tokenCounter)
    {
        var sentences = SentenceSplitRegex().Split(text);
        var chunks = new List<string>();
        var currentChunk = new List<string>();
        var currentSize = 0;

        foreach (var rawSentence in sentences)
        {
            var sentence = rawSentence.Trim();
            if (sentence.Length == 0)
            {
                continue;
            }

            var sentenceSize = MeasureBudgetSize(sentence, tokenCounter);
            if (sentenceSize > chunkTokenBudget)
            {
                if (currentChunk.Count > 0)
                {
                    chunks.Add(JoinChunkParts(currentChunk, " "));
                    currentChunk.Clear();
                    currentSize = 0;
                }

                chunks.AddRange(ChunkBySize(sentence, chunkTokenBudget, overlapTokenBudget, tokenCounter));
                continue;
            }

            if (currentChunk.Count > 0 && currentSize + sentenceSize + 1 > chunkTokenBudget)
            {
                var joined = JoinChunkParts(currentChunk, " ");
                chunks.Add(joined);

                var overlapText = GetOverlapText(joined, overlapTokenBudget, tokenCounter);
                if (overlapText.Length > 0)
                {
                    currentChunk = new List<string> { overlapText };
                    currentSize = MeasureBudgetSize(overlapText, tokenCounter);
                }
                else
                {
                    currentChunk.Clear();
                    currentSize = 0;
                }
            }

            currentChunk.Add(sentence);
            currentSize += sentenceSize + 1;
        }

        if (currentChunk.Count > 0)
        {
            chunks.Add(JoinChunkParts(currentChunk, " "));
        }

        return chunks;
    }

    private static string JoinChunkParts(List<string> parts, string separator)
    {
        if (parts.Count == 0)
        {
            return string.Empty;
        }

        var length = separator.Length * (parts.Count - 1);
        for (var i = 0; i < parts.Count; i++)
        {
            length += parts[i].Length;
        }

        return string.Create(length, (Parts: parts, Separator: separator), static (destination, state) =>
        {
            var offset = 0;
            for (var i = 0; i < state.Parts.Count; i++)
            {
                var part = state.Parts[i].AsSpan();
                part.CopyTo(destination.Slice(offset));
                offset += part.Length;

                if (i == state.Parts.Count - 1)
                {
                    continue;
                }

                var separator = state.Separator.AsSpan();
                separator.CopyTo(destination.Slice(offset));
                offset += separator.Length;
            }
        });
    }

    private static IReadOnlyList<string> NormalizeTextChunks(
        List<string> chunks,
        string fallbackContent,
        int chunkTokenBudget,
        int overlapTokenBudget,
        ITokenCounter tokenCounter)
    {
        IReadOnlyList<string> sourceChunks = chunks.Count > 0 ? chunks : new[] { fallbackContent };
        var normalized = new List<string>();
        foreach (var chunk in sourceChunks)
        {
            if (FitsTokenBudget(chunk, chunkTokenBudget, tokenCounter))
            {
                AddNonDuplicateChunk(normalized, chunk);
                continue;
            }

            foreach (var splitChunk in ChunkBySentences(chunk, chunkTokenBudget, overlapTokenBudget, tokenCounter))
            {
                AddNonDuplicateChunk(normalized, splitChunk);
            }
        }

        return normalized.Count > 0 ? normalized : new[] { fallbackContent };
    }

    private static void AddNonDuplicateChunk(List<string> chunks, string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        if (chunks.LastOrDefault() == chunk)
        {
            return;
        }

        chunks.Add(chunk);
    }

    private static List<string> ChunkBySize(
        string text,
        int chunkTokenBudget,
        int overlapTokenBudget,
        ITokenCounter tokenCounter)
    {
        var chunks = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            var end = GetTokenBoundaryEnd(text, start, chunkTokenBudget, tokenCounter);
            if (end < text.Length)
            {
                var spaceIndex = text.LastIndexOf(' ', end - 1, end - start);
                if (spaceIndex > start)
                {
                    end = spaceIndex;
                }
            }

            chunks.Add(text[start..end].Trim());
            if (end >= text.Length)
            {
                break;
            }

            var nextStart = start + GetOverlapStart(text[start..end], overlapTokenBudget, tokenCounter);
            if (nextStart <= start)
            {
                nextStart = start + 1;
            }

            start = Math.Min(nextStart, end);
        }

        return chunks;
    }

    private static string GetOverlapText(
        string text,
        int overlapTokenBudget,
        ITokenCounter tokenCounter)
    {
        var overlapStart = GetOverlapStart(text, overlapTokenBudget, tokenCounter);
        return overlapStart < text.Length ? text[overlapStart..] : string.Empty;
    }

    private static int GetTokenBoundaryEnd(
        string text,
        int start,
        int chunkTokenBudget,
        ITokenCounter tokenCounter)
    {
        if (tokenCounter is ITokenBoundaryProvider boundaryProvider
            && boundaryProvider.TryGetIndexByTokenCount(text[start..], chunkTokenBudget, out var tokenEnd)
            && tokenEnd > 0)
        {
            return start + tokenEnd;
        }

        var fallbackEnd = Math.Min(start + TokensToChars(chunkTokenBudget), text.Length);
        return fallbackEnd > start ? fallbackEnd : Math.Min(start + 1, text.Length);
    }

    private static int GetOverlapStart(
        string text,
        int overlapTokenBudget,
        ITokenCounter tokenCounter)
    {
        if (overlapTokenBudget <= 0 || text.Length == 0)
        {
            return text.Length;
        }

        if (TryGetTokenBoundaryStart(text, overlapTokenBudget, tokenCounter, out var tokenOverlapStart))
        {
            return tokenOverlapStart;
        }

        var overlapChars = TokensToChars(overlapTokenBudget);
        if (text.Length <= overlapChars)
        {
            return 0;
        }

        var overlapStart = text.Length - overlapChars;
        var spaceIndex = text.IndexOf(' ', overlapStart);
        return spaceIndex != -1 ? spaceIndex + 1 : overlapStart;
    }

    private static bool TryGetTokenBoundaryStart(
        string text,
        int overlapTokenBudget,
        ITokenCounter tokenCounter,
        out int index)
    {
        if (tokenCounter is ITokenBoundaryProvider boundaryProvider
            && boundaryProvider.TryGetIndexByTokenCountFromEnd(text, overlapTokenBudget, out index))
        {
            return true;
        }

        index = text.Length;
        return false;
    }

    private static IReadOnlyList<string> ChunkSpeakerMessages(
        string content,
        int chunkTokenBudget,
        int overlapTokenBudget,
        ITokenCounter tokenCounter)
    {
        var messages = MaterializeSpeakerMessages(content);

        if (messages.Count == 0)
        {
            return new[] { content };
        }

        var chunks = new List<string>();
        var currentMessages = new List<string>();
        var currentSize = 0;

        foreach (var message in messages)
        {
            var messageSize = MeasureBudgetSize(message, tokenCounter);
            if (messageSize > chunkTokenBudget)
            {
                if (currentMessages.Count > 0)
                {
                    chunks.Add(JoinLines(currentMessages));
                    currentMessages.Clear();
                    currentSize = 0;
                }

                chunks.Add(message);
                continue;
            }

            if (currentMessages.Count > 0 && currentSize + messageSize + 1 > chunkTokenBudget)
            {
                chunks.Add(JoinLines(currentMessages));
                currentMessages = GetOverlapMessages(currentMessages, overlapTokenBudget, tokenCounter);
                currentSize = MeasureJoinedLinesSize(currentMessages, tokenCounter);
            }

            currentMessages.Add(message);
            currentSize += messageSize + 1;
        }

        if (currentMessages.Count > 0)
        {
            chunks.Add(JoinLines(currentMessages));
        }

        return chunks.Count > 0 ? chunks : new[] { content };
    }

    private static List<string> GetOverlapMessages(
        List<string> messages,
        int overlapTokenBudget,
        ITokenCounter tokenCounter)
    {
        if (messages.Count == 0)
        {
            return new List<string>();
        }

        var overlap = new List<string>();
        var currentSize = 0;

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var messageSize = MeasureBudgetSize(messages[i], tokenCounter) + 1;
            if (currentSize + messageSize > overlapTokenBudget)
            {
                break;
            }

            overlap.Insert(0, messages[i]);
            currentSize += messageSize;
        }

        return overlap;
    }

    private static IReadOnlyList<string> ChunkByLines(
        string content,
        int chunkTokenBudget,
        int overlapTokenBudget,
        ITokenCounter tokenCounter)
    {
        var lines = MaterializeLines(content);
        var chunks = new List<string>();
        var currentLines = new List<string>();
        var currentSize = 0;

        foreach (var line in lines)
        {
            var lineSize = MeasureBudgetSize(line, tokenCounter) + 1;
            if (lineSize > chunkTokenBudget)
            {
                if (currentLines.Count > 0)
                {
                    chunks.Add(JoinLines(currentLines));
                    currentLines.Clear();
                    currentSize = 0;
                }

                chunks.AddRange(ChunkBySize(line, chunkTokenBudget, overlapTokenBudget, tokenCounter));
                continue;
            }

            if (currentLines.Count > 0 && currentSize + lineSize > chunkTokenBudget)
            {
                var joined = JoinLines(currentLines);
                chunks.Add(joined);

                var overlap = GetOverlapText(joined, overlapTokenBudget, tokenCounter);
                if (overlap.Length > 0)
                {
                    currentLines = MaterializeLines(overlap);
                    currentSize = MeasureBudgetSize(overlap, tokenCounter);
                }
                else
                {
                    currentLines.Clear();
                    currentSize = 0;
                }
            }

            currentLines.Add(line);
            currentSize += lineSize;
        }

        if (currentLines.Count > 0)
        {
            chunks.Add(JoinLines(currentLines));
        }

        return chunks.Count > 0 ? chunks : new[] { content };
    }

    private static List<string> MaterializeParagraphs(string content)
    {
        var paragraphs = new List<string>();
        var start = 0;
        var index = 0;
        while (index < content.Length)
        {
            if (content[index] != '\n')
            {
                index++;
                continue;
            }

            var separatorEnd = index + 1;
            var hasClosingNewline = false;
            while (separatorEnd < content.Length && char.IsWhiteSpace(content[separatorEnd]))
            {
                if (content[separatorEnd] == '\n')
                {
                    hasClosingNewline = true;
                }

                separatorEnd++;
            }

            if (!hasClosingNewline)
            {
                index++;
                continue;
            }

            AddTrimmedSegment(content.AsSpan(start, index - start), paragraphs);
            start = separatorEnd;
            index = separatorEnd;
        }

        AddTrimmedSegment(content.AsSpan(start), paragraphs);
        return paragraphs;
    }

    private static List<string> MaterializeSpeakerMessages(string content)
    {
        var split = SpeakerSplitRegex().Split(content);
        var messages = new List<string>(split.Length);
        for (var i = 0; i < split.Length; i++)
        {
            AddTrimmedSegment(split[i].AsSpan(), messages);
        }

        return messages;
    }

    private static List<string> MaterializeLines(string content)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != '\n')
            {
                continue;
            }

            lines.Add(content[start..i]);
            start = i + 1;
        }

        lines.Add(content[start..]);
        return lines;
    }

    private static void AddTrimmedSegment(ReadOnlySpan<char> segment, List<string> destination)
    {
        var trimmed = segment.Trim();
        if (trimmed.Length > 0)
        {
            destination.Add(trimmed.ToString());
        }
    }

    private static string JoinLines(List<string> lines)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        if (lines.Count == 1)
        {
            return lines[0];
        }

        var capacity = lines.Count - 1;
        for (var i = 0; i < lines.Count; i++)
        {
            capacity += lines[i].Length;
        }

        var builder = new StringBuilder(capacity);
        builder.Append(lines[0]);
        for (var i = 1; i < lines.Count; i++)
        {
            builder.Append('\n');
            builder.Append(lines[i]);
        }

        return builder.ToString();
    }

    private static int MeasureJoinedLinesSize(
        List<string> lines,
        ITokenCounter tokenCounter)
    {
        if (lines.Count == 0)
        {
            return 0;
        }

        var size = lines.Count - 1;
        for (var i = 0; i < lines.Count; i++)
        {
            size += MeasureBudgetSize(lines[i], tokenCounter);
        }

        return size;
    }

    private static bool TryReadNextWhitespaceWord(
        ReadOnlySpan<char> source,
        ref int index,
        out ReadOnlySpan<char> word)
    {
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        if (index >= source.Length)
        {
            word = default;
            return false;
        }

        var start = index;
        while (index < source.Length && !char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        word = source[start..index];
        return true;
    }

    private static ReadOnlySpan<char> TrimDensityWord(ReadOnlySpan<char> word)
    {
        var start = 0;
        var end = word.Length;

        while (start < end && IsDensityWordTrimChar(word[start]))
        {
            start++;
        }

        while (end > start && IsDensityWordTrimChar(word[end - 1]))
        {
            end--;
        }

        return word[start..end];
    }

    private static bool IsDensityWordTrimChar(char character) =>
        character is ' ' or '.' or ',' or '!' or '?' or ';' or ':' or '\''
            or '"' or '(' or ')' or '[' or ']' or '{' or '}';

    private static bool PreviousWordEndsSentence(ReadOnlySpan<char> word) =>
        word.Length > 0 && word[^1] is '.' or '!' or '?';

    private static bool IsUpperLikePython(ReadOnlySpan<char> text)
    {
        var hasLetter = false;
        foreach (var character in text)
        {
            if (!char.IsLetter(character))
            {
                continue;
            }

            hasLetter = true;
            if (!char.IsUpper(character))
            {
                return false;
            }
        }

        return hasLetter;
    }

    private static int ResolveTokenCount(int? tokenCount, int defaultValue) =>
        tokenCount is null or <= 0 ? defaultValue : tokenCount.Value;

    private static int ResolveOverlapTokenCount(int? overlapTokens) =>
        overlapTokens is null or < 0 ? DefaultChunkOverlapTokens : overlapTokens.Value;

    private static int TokensToChars(int tokens) => tokens * CharsPerToken;

    private static bool FitsTokenBudget(
        string text,
        int tokenBudget,
        ITokenCounter tokenCounter) =>
        MeasureBudgetSize(text, tokenCounter) <= tokenBudget;

    private static int MeasureBudgetSize(string? text, ITokenCounter tokenCounter)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return Math.Max(1, EstimateTokens(text, tokenCounter));
    }

    private static string SerializeArray(IEnumerable<string> elements)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var element in elements)
            {
                writer.WriteRawValue(element);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string SerializeObject(IEnumerable<KeyValuePair<string, string>> entries)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var entry in entries)
            {
                writer.WritePropertyName(entry.Key);
                writer.WriteRawValue(entry.Value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string SerializeObjectEntry(KeyValuePair<string, string> entry)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName(entry.Key);
            writer.WriteRawValue(entry.Value);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static List<KeyValuePair<string, string>> MaterializeJsonObjectEntries(JsonObject data)
    {
        var entries = new List<KeyValuePair<string, string>>(data.Count);
        foreach (var property in data)
        {
            entries.Add(new KeyValuePair<string, string>(
                property.Key,
                property.Value?.ToJsonString(JsonOptions) ?? "null"));
        }

        return entries;
    }

    private static bool CombinationCountExceeds(int n, int k, int limit)
    {
        if (k < 0 || k > n)
        {
            return false;
        }

        k = Math.Min(k, n - k);
        double result = 1;
        for (var i = 1; i <= k; i++)
        {
            result = result * (n - k + i) / i;
            if (result > limit)
            {
                return true;
            }
        }

        return result > limit;
    }

    private static IEnumerable<int[]> GenerateDeterministicGreedyCandidates(
        int n,
        int k,
        HashSet<(int First, int Second)> uncoveredPairs)
    {
        var uncoveredDegrees = CountUncoveredDegrees(n, uncoveredPairs);
        var seenCandidates = new HashSet<string>(StringComparer.Ordinal);
        var emitted = 0;

        var vertices = BuildGreedySeedVertices(n, uncoveredDegrees);
        for (var vertexIndex = 0;
             vertexIndex < vertices.Count && vertexIndex < DeterministicGreedyVertexSeedLimit;
             vertexIndex++)
        {
            var vertex = vertices[vertexIndex];
            if (TryCreateDeterministicGreedyCandidate(
                    n,
                    k,
                    uncoveredPairs,
                    uncoveredDegrees,
                    vertex,
                    secondSeed: null,
                    seenCandidates,
                    out var candidate))
            {
                yield return candidate;
                emitted++;
                if (emitted >= DeterministicGreedyCandidateLimit)
                {
                    yield break;
                }
            }
        }

        var seedPairs = BuildGreedySeedPairs(uncoveredPairs, uncoveredDegrees);
        for (var pairIndex = 0;
             pairIndex < seedPairs.Count && pairIndex < MaxCombinationsToEvaluate;
             pairIndex++)
        {
            var pair = seedPairs[pairIndex];
            if (TryCreateDeterministicGreedyCandidate(
                    n,
                    k,
                    uncoveredPairs,
                    uncoveredDegrees,
                    pair.First,
                    pair.Second,
                    seenCandidates,
                    out var candidate))
            {
                yield return candidate;
                emitted++;
                if (emitted >= DeterministicGreedyCandidateLimit)
                {
                    yield break;
                }
            }
        }

        if (emitted == 0 && TryGetFirstUncoveredPair(uncoveredPairs, out var firstPair))
        {
            if (TryCreateDeterministicGreedyCandidate(
                    n,
                    k,
                    uncoveredPairs,
                    uncoveredDegrees,
                    firstPair.First,
                    firstPair.Second,
                    seenCandidates,
                    out var candidate))
            {
                yield return candidate;
            }
        }
    }

    private static bool TryCreateDeterministicGreedyCandidate(
        int n,
        int k,
        HashSet<(int First, int Second)> uncoveredPairs,
        int[] uncoveredDegrees,
        int firstSeed,
        int? secondSeed,
        HashSet<string> seenCandidates,
        out int[] candidate)
    {
        candidate = BuildDeterministicGreedyCandidate(
            n,
            k,
            uncoveredPairs,
            uncoveredDegrees,
            firstSeed,
            secondSeed);

        var key = string.Join(",", candidate);
        return seenCandidates.Add(key);
    }

    private static int[] BuildDeterministicGreedyCandidate(
        int n,
        int k,
        HashSet<(int First, int Second)> uncoveredPairs,
        int[] uncoveredDegrees,
        int firstSeed,
        int? secondSeed)
    {
        var selected = new List<int>(Math.Min(k, n));
        var selectedLookup = new bool[n];

        AddSeed(firstSeed);
        if (secondSeed is not null)
        {
            AddSeed(secondSeed.Value);
        }

        while (selected.Count < k && selected.Count < n)
        {
            var bestIndex = -1;
            var bestGain = -1;
            var bestDegree = -1;

            for (var candidate = 0; candidate < n; candidate++)
            {
                if (selectedLookup[candidate])
                {
                    continue;
                }

                var gain = CountIncidentUncoveredPairs(candidate, selected, uncoveredPairs);
                var degree = uncoveredDegrees[candidate];
                if (gain > bestGain
                    || (gain == bestGain
                        && (degree > bestDegree
                            || (degree == bestDegree
                                && (bestIndex < 0 || candidate < bestIndex)))))
                {
                    bestIndex = candidate;
                    bestGain = gain;
                    bestDegree = degree;
                }
            }

            if (bestIndex < 0)
            {
                break;
            }

            selected.Add(bestIndex);
            selectedLookup[bestIndex] = true;
        }

        selected.Sort();
        return CopyIntList(selected);

        void AddSeed(int index)
        {
            if (index < 0 || index >= n || selectedLookup[index] || selected.Count >= k)
            {
                return;
            }

            selected.Add(index);
            selectedLookup[index] = true;
        }
    }

    private static int[] CountUncoveredDegrees(
        int n,
        HashSet<(int First, int Second)> uncoveredPairs)
    {
        var degrees = new int[n];
        foreach (var (first, second) in uncoveredPairs)
        {
            degrees[first]++;
            degrees[second]++;
        }

        return degrees;
    }

    private static int CountIncidentUncoveredPairs(
        int candidate,
        List<int> selected,
        HashSet<(int First, int Second)> uncoveredPairs)
    {
        var count = 0;
        foreach (var selectedIndex in selected)
        {
            if (uncoveredPairs.Contains(NormalizePair(candidate, selectedIndex)))
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryGetFirstUncoveredPair(
        HashSet<(int First, int Second)> uncoveredPairs,
        out (int First, int Second) pair)
    {
        var hasPair = false;
        var best = (First: int.MaxValue, Second: int.MaxValue);
        foreach (var candidate in uncoveredPairs)
        {
            if (!hasPair
                || candidate.First < best.First
                || (candidate.First == best.First && candidate.Second < best.Second))
            {
                hasPair = true;
                best = candidate;
            }
        }

        pair = best;
        return hasPair;
    }

    private static IEnumerable<int[]> GenerateCombinations(int n, int k)
    {
        var combination = new int[k];
        for (var index = 0; index < k; index++)
        {
            combination[index] = index;
        }

        while (true)
        {
            yield return CopyIntArray(combination);

            var i = k - 1;
            while (i >= 0 && combination[i] == i + n - k)
            {
                i--;
            }

            if (i < 0)
            {
                yield break;
            }

            combination[i]++;
            for (var j = i + 1; j < k; j++)
            {
                combination[j] = combination[j - 1] + 1;
            }
        }
    }

    private static bool IsBetterCoveringCandidate(
        int[] candidate,
        int coveredCount,
        int[]? bestCandidate,
        int bestCoveredCount)
    {
        if (coveredCount > bestCoveredCount)
        {
            return true;
        }

        return coveredCount > 0
            && coveredCount == bestCoveredCount
            && bestCandidate is not null
            && CompareLexicographically(candidate, bestCandidate) < 0;
    }

    private static int CompareLexicographically(int[] left, int[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        for (var i = 0; i < length; i++)
        {
            var comparison = left[i].CompareTo(right[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static int CountCoveredPairs(int[] chunkIndices, HashSet<(int First, int Second)> uncoveredPairs)
    {
        var coveredCount = 0;
        for (var i = 0; i < chunkIndices.Length; i++)
        {
            for (var j = i + 1; j < chunkIndices.Length; j++)
            {
                var pair = NormalizePair(chunkIndices[i], chunkIndices[j]);
                if (uncoveredPairs.Contains(pair))
                {
                    coveredCount++;
                }
            }
        }

        return coveredCount;
    }

    private static void RemoveCoveredPairs(int[] chunkIndices, HashSet<(int First, int Second)> uncoveredPairs)
    {
        for (var i = 0; i < chunkIndices.Length; i++)
        {
            for (var j = i + 1; j < chunkIndices.Length; j++)
            {
                uncoveredPairs.Remove(NormalizePair(chunkIndices[i], chunkIndices[j]));
            }
        }
    }

    private static (int First, int Second) NormalizePair(int first, int second) =>
        first < second ? (first, second) : (second, first);

    private static CoveringChunk<T> ToCoveringChunkFromRange<T>(IReadOnlyList<T> items, int count)
    {
        var chunkItems = new List<T>(count);
        var indices = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            chunkItems.Add(items[i]);
            indices.Add(i);
        }

        return new CoveringChunk<T>(chunkItems, indices);
    }

    private static CoveringChunk<T> ToCoveringChunk<T>(IReadOnlyList<T> items, int first, int second) =>
        new(new List<T>(2) { items[first], items[second] }, new List<int>(2) { first, second });

    private static CoveringChunk<T> ToCoveringChunk<T>(IReadOnlyList<T> items, int[] indices)
    {
        var chunkItems = new List<T>(indices.Length);
        var chunkIndices = new List<int>(indices.Length);
        for (var i = 0; i < indices.Length; i++)
        {
            var index = indices[i];
            chunkItems.Add(items[index]);
            chunkIndices.Add(index);
        }

        return new CoveringChunk<T>(chunkItems, chunkIndices);
    }

    private static List<int> BuildGreedySeedVertices(int n, int[] uncoveredDegrees)
    {
        var vertices = new List<int>(Math.Min(n, DeterministicGreedyVertexSeedLimit));
        for (var index = 0; index < n; index++)
        {
            if (uncoveredDegrees[index] > 0)
            {
                vertices.Add(index);
            }
        }

        vertices.Sort((left, right) =>
        {
            var degreeComparison = uncoveredDegrees[right].CompareTo(uncoveredDegrees[left]);
            return degreeComparison != 0 ? degreeComparison : left.CompareTo(right);
        });

        return vertices;
    }

    private static List<(int First, int Second)> BuildGreedySeedPairs(
        HashSet<(int First, int Second)> uncoveredPairs,
        int[] uncoveredDegrees)
    {
        var pairs = CopyPairs(uncoveredPairs);
        pairs.Sort((left, right) =>
        {
            var leftDegreeTotal = uncoveredDegrees[left.First] + uncoveredDegrees[left.Second];
            var rightDegreeTotal = uncoveredDegrees[right.First] + uncoveredDegrees[right.Second];
            var totalComparison = rightDegreeTotal.CompareTo(leftDegreeTotal);
            if (totalComparison != 0)
            {
                return totalComparison;
            }

            var leftMinimumDegree = Math.Min(uncoveredDegrees[left.First], uncoveredDegrees[left.Second]);
            var rightMinimumDegree = Math.Min(uncoveredDegrees[right.First], uncoveredDegrees[right.Second]);
            var minimumComparison = rightMinimumDegree.CompareTo(leftMinimumDegree);
            if (minimumComparison != 0)
            {
                return minimumComparison;
            }

            return ComparePairsByFirstThenSecond(left, right);
        });

        return pairs;
    }

    private static List<(int First, int Second)> CopyPairs(HashSet<(int First, int Second)> pairs)
    {
        var copy = new List<(int First, int Second)>(pairs.Count);
        foreach (var pair in pairs)
        {
            copy.Add(pair);
        }

        return copy;
    }

    private static int ComparePairsByFirstThenSecond(
        (int First, int Second) left,
        (int First, int Second) right)
    {
        var firstComparison = left.First.CompareTo(right.First);
        return firstComparison != 0 ? firstComparison : left.Second.CompareTo(right.Second);
    }

    private static int[] CopyIntList(List<int> values)
    {
        var copy = new int[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            copy[i] = values[i];
        }

        return copy;
    }

    private static int[] CopyIntArray(int[] values)
    {
        var copy = new int[values.Length];
        Array.Copy(values, copy, values.Length);
        return copy;
    }

    private sealed class TokenCounterScope : IDisposable
    {
        private readonly ITokenCounter? _previous;
        private bool _disposed;

        public TokenCounterScope(ITokenCounter? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            ScopedTokenCounter.Value = _previous;
            _disposed = true;
        }
    }

    [GeneratedRegex("(?<=[.!?])\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex(
        "^([A-Za-z_][A-Za-z0-9_\\s]*):(.+?)(?=^[A-Za-z_][A-Za-z0-9_\\s]*:|$)",
        RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex SpeakerSearchRegex();

    [GeneratedRegex(
        "(?=^[A-Za-z_][A-Za-z0-9_\\s]*:)",
        RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex SpeakerSplitRegex();
}
