using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;
using System.Numerics.Tensors;
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
    /// <summary>Target token size for a content chunk.</summary>
    public const int ChunkTokenSize = ContentChunking.DefaultChunkTokenSize;

    /// <summary>Token overlap between adjacent chunks.</summary>
    public const int ChunkOverlapTokens = 200;

    /// <summary>Minimum token size for a content chunk.</summary>
    public const int ChunkMinTokens = 1_000;

    /// <summary>Density threshold used when deciding chunk boundaries.</summary>
    public const double ChunkDensityThreshold = ContentChunking.DefaultChunkDensityThreshold;

    private static readonly SearchValues<char> LuceneCharactersToEscape =
        SearchValues.Create("+-&|!(){}[]^\"~*?:\\/ORNTAD");
    private static readonly FrozenSet<string> EntityNodeProtectedAttributeNames = new[]
    {
        "uuid",
        "name",
        "group_id",
        "groupid",
        "labels",
        "created_at",
        "createdat",
        "name_embedding",
        "nameembedding",
        "summary",
        "attributes"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Deterministic default timestamp (Unix epoch) used for missing/uninitialized values.</summary>
    public static DateTime DefaultTimestamp { get; } = DateTime.UnixEpoch;

    /// <summary>Current UTC time. Obsolete: prefer an injected <see cref="TimeProvider"/>.</summary>
    [Obsolete("Use TimeProvider.GetUtcNow() in workflows or GraphitiHelpers.DefaultTimestamp for deterministic missing values.")]
    public static DateTime UtcNow() => TimeProvider.System.GetUtcNow().UtcDateTime;

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
    /// <see cref="DateTimeOffset"/>, or string) into UTC, returning <c>null</c> for null/blank input.
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
        if (string.IsNullOrWhiteSpace(text))
        {
            parsed = null;
            return true;
        }

        var normalized = text.Trim();
        if (DateTimeOffset.TryParse(
                normalized,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var offset))
        {
            parsed = offset.UtcDateTime;
            return true;
        }

        parsed = null;
        return false;
    }

    /// <summary>Returns the provider-specific default group id (FalkorDB needs a non-empty value).</summary>
    public static string GetDefaultGroupId(GraphProvider provider) =>
        provider == GraphProvider.FalkorDb ? @"\_" : string.Empty;

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
    /// Ensures every excluded entity type name refers to a known type (the built-in <c>Entity</c> or a
    /// declared custom type), throwing <see cref="ArgumentException"/> otherwise.
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
                availableTypes.Add(pair.Value.Name);
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
            var sortedAvailableTypes = availableTypes.Order(StringComparer.Ordinal);
            throw new ArgumentException(
                $"Invalid excluded entity types: [{string.Join(", ", invalidTypes)}]. Available types: [{string.Join(", ", sortedAvailableTypes)}]",
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

    /// <summary>
    /// Escapes Lucene query special characters so a raw query can be passed to a full-text index.
    /// Mirrors the Python implementation exactly, including escaping the uppercase letters used by
    /// Lucene boolean operators (O, R, N, T, A, D).
    /// </summary>
    public static string LuceneSanitize(string query)
    {
        var source = query ?? string.Empty;
        var firstEscaped = source.AsSpan().IndexOfAny(LuceneCharactersToEscape);
        return firstEscaped < 0
            ? source
            : EscapeLuceneCharacters(source, firstEscaped);
    }

    /// <summary>Returns an L2-normalized copy of the vector; a zero/non-finite norm is left unchanged.</summary>
    public static float[] NormalizeL2(IEnumerable<float> embedding)
    {
        var vector = embedding.ToArray();
        NormalizeL2InPlace(vector);
        return vector;
    }

    internal static void NormalizeL2InPlace(Span<float> vector)
    {
        var norm = TensorPrimitives.Norm(vector);
        if (!float.IsFinite(norm) || norm <= 0)
        {
            return;
        }

        TensorPrimitives.Divide(vector, norm, vector);
    }

    private static string EscapeLuceneCharacters(string source, int firstEscaped)
    {
        var builder = new StringBuilder(source.Length + 8);
        builder.Append(source.AsSpan(0, firstEscaped));

        for (var i = firstEscaped; i < source.Length; i++)
        {
            var current = source[i];
            if (LuceneCharactersToEscape.Contains(current))
            {
                builder.Append('\\');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Runs the operations concurrently and returns their results in input order. When
    /// <paramref name="maxConcurrency"/> is null or non-positive, all run unbounded; otherwise
    /// concurrency is capped at that value.
    /// </summary>
    public static async Task<IReadOnlyList<T>> SemaphoreGatherAsync<T>(
        IEnumerable<Func<CancellationToken, Task<T>>> operations,
        int? maxConcurrency = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        cancellationToken.ThrowIfCancellationRequested();

        var opList = SnapshotOperations(operations);
        if (opList.Length == 0)
        {
            return Array.Empty<T>();
        }

        if (maxConcurrency is null || maxConcurrency <= 0)
        {
            return await RunUnboundedOperationsAsync(opList, cancellationToken).ConfigureAwait(false);
        }

        var results = new T[opList.Length];
        await Parallel.ForAsync(
            0,
            opList.Length,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency.Value,
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

        var list = new List<Func<CancellationToken, Task<T>>>();
        foreach (var operation in operations)
        {
            list.Add(operation);
        }

        return list.Count == 0 ? Array.Empty<Func<CancellationToken, Task<T>>>() : list.ToArray();
    }

    private static async Task<T[]> RunUnboundedOperationsAsync<T>(
        Func<CancellationToken, Task<T>>[] operations,
        CancellationToken cancellationToken)
    {
        var tasks = new Task<T>[operations.Length];
        for (var i = 0; i < operations.Length; i++)
        {
            tasks[i] = operations[i](cancellationToken);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
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

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
