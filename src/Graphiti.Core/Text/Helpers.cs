using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace Graphiti.Core.Text;

/// <summary>
/// Shared, side-effect-free utility helpers used across Graphiti Core: timestamp/UUID generation,
/// database date parsing, group-id and node-label validation, Lucene query sanitization, L2 vector
/// normalization, and bounded-concurrency task gathering.
/// </summary>
public static partial class GraphitiHelpers
{
    internal const int DefaultSemaphoreLimit = 20;

    /// <summary>Target token size for a content chunk.</summary>
    public const int ChunkTokenSize = ContentChunking.DefaultChunkTokenSize;

    /// <summary>Token overlap between adjacent chunks.</summary>
    public const int ChunkOverlapTokens = 200;

    /// <summary>Minimum token size for a content chunk.</summary>
    public const int ChunkMinTokens = 1_000;

    /// <summary>Density threshold used when deciding chunk boundaries.</summary>
    public const double ChunkDensityThreshold = ContentChunking.DefaultChunkDensityThreshold;

    private static readonly FrozenSet<string> EntityNodeProtectedAttributeNames = new[]
    {
        "uuid",
        "name",
        "group_id",
        "labels",
        "created_at",
        "name_embedding",
        "summary",
        "attributes"
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Deterministic default timestamp (Unix epoch) used for missing/uninitialized values.</summary>
    public static DateTime DefaultTimestamp { get; } = DateTime.UnixEpoch;

    /// <summary>Generates a new time-ordered (version 7) UUID string.</summary>
    public static string NewUuid() => Guid.CreateVersion7().ToString();

    /// <summary>Returns the value as UTC, treating <see cref="DateTimeKind.Unspecified"/> as UTC.</summary>
    public static DateTime EnsureUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        if (value.Kind == DateTimeKind.Unspecified)
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        return value.ToUniversalTime();
    }

    /// <summary>
    /// Parses a date value returned from a graph backend (a <see cref="DateTime"/>,
    /// <see cref="DateTimeOffset"/>, or ISO-8601 string) into UTC, returning
    /// <c>null</c> for null input.
    /// </summary>
    public static DateTime? ParseDbDate(object? input)
    {
        if (TryParseDbDate(input, out var parsed))
        {
            return parsed;
        }

        var text = input as string ?? input?.ToString();
        throw new FormatException($"Could not parse database date value '{text}'.");
    }

    internal static bool TryParseDbDate(object? input, out DateTime? parsed)
    {
        if (input is null)
        {
            parsed = null;
            return true;
        }

        return input switch
        {
            DateTime dateTime => TrySetParsedDate(EnsureUtc(dateTime), out parsed),
            DateTimeOffset offset => TrySetParsedDate(offset.UtcDateTime, out parsed),
            string text => TryParseDbDateString(text, out parsed),
            _ => TryParseObjectDate(input, out parsed)
        };
    }

    private static bool TrySetParsedDate(DateTime value, out DateTime? parsed)
    {
        parsed = value;
        return true;
    }

    private static bool TryParseObjectDate(object input, out DateTime? parsed)
    {
        var text = input.ToString();
        return TryParseDbDateString(text, out parsed);
    }

    private static bool TryParseDbDateString(string? text, out DateTime? parsed)
    {
        parsed = null;
        if (text is null)
        {
            return false;
        }

        if (text.Length == 0 || char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]))
        {
            return false;
        }

        if (!IsoDateParser.TryParseIsoDateTime(text.AsSpan(), out var dateTime))
        {
            return false;
        }

        parsed = dateTime;
        return true;
    }

    /// <summary>Returns the provider-specific default group id (FalkorDB needs a non-empty value).</summary>
    public static string GetDefaultGroupId(GraphProvider provider)
    {
        // FalkorDB's default group id is '_': a clean, validator-safe value. A backslash-prefixed
        // value would fail ValidateGroupId below (backslashes are rejected), so '_' keeps the
        // default group id self-consistent with the validator and usable out of the box.
        return provider == GraphProvider.FalkorDb ? "_" : string.Empty;
    }

    /// <summary>
    /// Validates a group id (alphanumerics, dashes, underscores). Null/empty is allowed (no-op);
    /// invalid characters throw <see cref="GroupIdValidationException"/>.
    /// </summary>
    public static void ValidateGroupId(string? groupId)
    {
        if (string.IsNullOrEmpty(groupId))
        {
            return;
        }

        if (!GroupIdRegex().IsMatch(groupId))
        {
            throw new GroupIdValidationException(groupId);
        }
    }

    /// <summary>Validates each group id in the sequence; see <see cref="ValidateGroupId"/>.</summary>
    public static void ValidateGroupIds(IEnumerable<string>? groupIds)
    {
        if (groupIds is null)
        {
            return;
        }

        foreach (var groupId in groupIds)
        {
            ValidateGroupId(groupId);
        }
    }

    /// <summary>
    /// Validates node labels (must start with a letter or underscore, then alphanumerics/underscores).
    /// Throws <see cref="NodeLabelValidationException"/> when any label is invalid.
    /// </summary>
    public static void ValidateNodeLabels(IEnumerable<string>? nodeLabels)
    {
        if (nodeLabels is null)
        {
            return;
        }

        List<string>? invalid = null;
        foreach (var label in nodeLabels)
        {
            if (string.IsNullOrWhiteSpace(label) || !NodeLabelRegex().IsMatch(label))
            {
                invalid ??= new List<string>();
                invalid.Add(label);
            }
        }

        if (invalid is not null)
        {
            throw new NodeLabelValidationException(invalid);
        }
    }

    /// <summary>
    /// Ensures every excluded entity type name refers to a known type key (the built-in <c>Entity</c>
    /// or a declared custom type key), throwing <see cref="ArgumentException"/> otherwise.
    /// </summary>
    public static void ValidateExcludedEntityTypes(
        IEnumerable<string>? excludedEntityTypes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        if (excludedEntityTypes is null)
        {
            return;
        }

        var availableTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Entity"
        };
        if (entityTypes is not null)
        {
            foreach (var pair in entityTypes)
            {
                availableTypes.Add(pair.Key);
            }
        }

        HashSet<string>? seenInvalidTypes = null;
        List<string>? invalidTypes = null;
        foreach (var type in excludedEntityTypes)
        {
            if (availableTypes.Contains(type))
            {
                continue;
            }

            seenInvalidTypes ??= new HashSet<string>(StringComparer.Ordinal);
            if (seenInvalidTypes.Add(type))
            {
                invalidTypes ??= new List<string>();
                invalidTypes.Add(type);
            }
        }

        if (invalidTypes is not null)
        {
            invalidTypes.Sort(StringComparer.Ordinal);
            var sortedAvailableTypes = new List<string>(availableTypes.Count);
            foreach (var type in availableTypes)
            {
                sortedAvailableTypes.Add(type);
            }

            sortedAvailableTypes.Sort(StringComparer.Ordinal);
            throw new ArgumentException(
                $"Invalid excluded entity types: {FormatStringList(invalidTypes)}. Available types: {FormatStringList(sortedAvailableTypes)}",
                nameof(excludedEntityTypes));
        }
    }

    /// <summary>
    /// Ensures no custom entity type declares an attribute whose name collides with a protected,
    /// framework-reserved name, throwing <see cref="EntityTypeValidationException"/> on conflict.
    /// </summary>
    public static void ValidateEntityTypes(IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        if (entityTypes is null)
        {
            return;
        }

        foreach (var pair in entityTypes)
        {
            foreach (var attributeName in pair.Value.Attributes.Keys)
            {
                if (EntityNodeProtectedAttributeNames.Contains(attributeName))
                {
                    throw new EntityTypeValidationException(pair.Key, attributeName);
                }
            }
        }
    }

    private static string FormatStringList(List<string> values)
    {
        if (values.Count == 0)
        {
            return "[]";
        }

        var builder = new StringBuilder();
        builder.Append('[');
        AppendStringLiteral(builder, values[0]);
        for (var i = 1; i < values.Count; i++)
        {
            builder.Append(", ");
            AppendStringLiteral(builder, values[i]);
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static void AppendStringLiteral(StringBuilder builder, string value)
    {
        var quote = value.Contains('\'')
            && !value.Contains('"')
            ? '"'
            : '\'';
        builder.Append(quote);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\\' || ch == quote)
            {
                builder.Append('\\');
                builder.Append(ch);
                continue;
            }

            switch (ch)
            {
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        builder.Append(quote);
    }

    /// <summary>
    /// Escapes Lucene query special characters so a raw query can be passed to a full-text index,
    /// including the uppercase letters used by Lucene boolean operators (O, R, N, T, A, D).
    /// </summary>
    public static string LuceneSanitize(string query) => LuceneQueryEscaper.Sanitize(query);

    /// <summary>Returns an L2-normalized copy of the vector; a zero norm is left unchanged.</summary>
    public static float[] NormalizeL2(IEnumerable<float> embedding) =>
        EmbeddingNormalization.NormalizeL2(embedding);

    /// <summary>
    /// Runs the operations concurrently and returns their results in input order. When
    /// <paramref name="maxConcurrency"/> is null or zero, the default cap of
    /// 20 is used; otherwise concurrency is capped at the supplied positive value.
    /// </summary>
    internal static async Task<IReadOnlyList<T>> SemaphoreGatherAsync<T>(
        IEnumerable<Func<CancellationToken, Task<T>>> operations,
        int? maxConcurrency = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        cancellationToken.ThrowIfCancellationRequested();

        if (maxConcurrency < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        }

        var resolvedMaxConcurrency = maxConcurrency is null or 0
            ? DefaultSemaphoreLimit
            : maxConcurrency.Value;
        var opList = SnapshotOperations(operations);
        if (opList.Length == 0)
        {
            return Array.Empty<T>();
        }

        var results = new T[opList.Length];
        await Parallel.ForAsync(
            0,
            opList.Length,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = resolvedMaxConcurrency,
                CancellationToken = cancellationToken
            },
            async (index, token) =>
            {
                results[index] = await opList[index](token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return results;
    }

    private static Func<CancellationToken, Task<T>>[] SnapshotOperations<T>(
        IEnumerable<Func<CancellationToken, Task<T>>> operations)
    {
        if (operations is ICollection<Func<CancellationToken, Task<T>>> collection)
        {
            if (collection.Count == 0)
            {
                return Array.Empty<Func<CancellationToken, Task<T>>>();
            }

            var snapshot = new Func<CancellationToken, Task<T>>[collection.Count];
            collection.CopyTo(snapshot, 0);
            return snapshot;
        }

        return [.. operations];
    }

    /// <summary>Normalizes text into a comparison key: trimmed, lowercased, whitespace collapsed.</summary>
    public static string NormalizeEntityKey(string text)
    {
        var source = (text ?? string.Empty).AsSpan().Trim();
        if (source.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(source.Length);
        var pendingSpace = false;
        foreach (var character in source)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex GroupIdRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NodeLabelRegex();
}
