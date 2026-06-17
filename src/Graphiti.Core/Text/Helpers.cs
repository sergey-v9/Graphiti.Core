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
    /// <see cref="DateTimeOffset"/>, or Python-compatible ISO string) into UTC, returning
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

        if (!TryParsePythonIsoDateTime(text.AsSpan(), out var dateTime))
        {
            return false;
        }

        parsed = dateTime;
        return true;
    }

    private static bool TryParsePythonIsoDateTime(ReadOnlySpan<char> text, out DateTime parsed)
    {
        parsed = default;
        if (!TryParseIsoDate(text, out var date, out var consumed))
        {
            return false;
        }

        if (consumed == text.Length)
        {
            parsed = DateTime.SpecifyKind(date, DateTimeKind.Utc);
            return true;
        }

        if (IsAsciiDigit(text[consumed]))
        {
            return false;
        }

        consumed++;
        if (consumed == text.Length)
        {
            return false;
        }

        if (!TryParseIsoTime(text[consumed..], out var hour, out var minute, out var second, out var microsecond, out var offsetTicks, out var timeConsumed)
            || consumed + timeConsumed != text.Length)
        {
            return false;
        }

        if (!TryCreateDateTime(date.Year, date.Month, date.Day, hour, minute, second, microsecond, out var localTime))
        {
            return false;
        }

        var ticks = localTime.Ticks;
        if (offsetTicks.HasValue)
        {
            ticks -= offsetTicks.GetValueOrDefault();
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            {
                return false;
            }
        }

        parsed = new DateTime(ticks, DateTimeKind.Utc);
        return true;
    }

    private static bool TryParseIsoDate(ReadOnlySpan<char> text, out DateTime date, out int consumed)
    {
        date = default;
        consumed = 0;
        if (text.Length < 7 || !TryReadNDigits(text, 0, 4, out var year))
        {
            return false;
        }

        if (text.Length >= 8 && text[4] == 'W')
        {
            return TryParseIsoWeekDate(year, text, basic: true, out date, out consumed);
        }

        if (text.Length >= 8 && IsAsciiDigit(text[4]))
        {
            if (!TryReadNDigits(text, 4, 2, out var basicMonth)
                || !TryReadNDigits(text, 6, 2, out var basicDay)
                || !TryCreateDate(year, basicMonth, basicDay, out date))
            {
                return false;
            }

            consumed = 8;
            return true;
        }

        if (text.Length >= 8 && text[4] == '-' && text[5] == 'W')
        {
            return TryParseIsoWeekDate(year, text, basic: false, out date, out consumed);
        }

        if (text.Length >= 10
            && text[4] == '-'
            && text[7] == '-'
            && TryReadNDigits(text, 5, 2, out var month)
            && TryReadNDigits(text, 8, 2, out var day)
            && TryCreateDate(year, month, day, out date))
        {
            consumed = 10;
            return true;
        }

        return false;
    }

    private static bool TryParseIsoWeekDate(
        int year,
        ReadOnlySpan<char> text,
        bool basic,
        out DateTime date,
        out int consumed)
    {
        date = default;
        consumed = 0;
        var weekStart = basic ? 5 : 6;
        if (!TryReadNDigits(text, weekStart, 2, out var week))
        {
            return false;
        }

        var day = 1;
        consumed = weekStart + 2;
        if (basic)
        {
            if (text.Length > consumed && IsAsciiDigit(text[consumed]))
            {
                day = text[consumed] - '0';
                consumed++;
            }
        }
        else if (text.Length > consumed && text[consumed] == '-')
        {
            if (!TryReadNDigits(text, consumed + 1, 1, out day))
            {
                return false;
            }

            consumed += 2;
        }

        if (day is < 1 or > 7)
        {
            return false;
        }

        try
        {
            date = ISOWeek.ToDateTime(year, week, day == 7 ? DayOfWeek.Sunday : (DayOfWeek)day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            date = default;
            return false;
        }
    }

    private static bool TryParseIsoTime(
        ReadOnlySpan<char> text,
        out int hour,
        out int minute,
        out int second,
        out int microsecond,
        out long? offsetTicks,
        out int consumed)
    {
        hour = 0;
        minute = 0;
        second = 0;
        microsecond = 0;
        offsetTicks = null;
        consumed = 0;
        if (!TryReadNDigits(text, 0, 2, out hour) || hour > 23)
        {
            return false;
        }

        consumed = 2;
        if (text.Length > consumed && text[consumed] == ':')
        {
            consumed++;
            if (!TryReadNDigits(text, consumed, 2, out minute) || minute > 59)
            {
                return false;
            }

            consumed += 2;
            if (text.Length > consumed && text[consumed] == ':')
            {
                consumed++;
                if (!TryReadNDigits(text, consumed, 2, out second) || second > 59)
                {
                    return false;
                }

                consumed += 2;
            }
        }
        else if (text.Length >= consumed + 2 && IsAsciiDigit(text[consumed]))
        {
            if (!TryReadNDigits(text, consumed, 2, out minute) || minute > 59)
            {
                return false;
            }

            consumed += 2;
            if (text.Length >= consumed + 2 && IsAsciiDigit(text[consumed]))
            {
                if (!TryReadNDigits(text, consumed, 2, out second) || second > 59)
                {
                    return false;
                }

                consumed += 2;
            }
        }

        if (!TryParseIsoFraction(text, ref consumed, out microsecond))
        {
            return false;
        }

        if (consumed == text.Length)
        {
            return true;
        }

        if (text[consumed] == 'Z')
        {
            consumed++;
            offsetTicks = 0;
            return consumed == text.Length;
        }

        if (text[consumed] is '+' or '-')
        {
            var sign = text[consumed] == '-' ? -1 : 1;
            consumed++;
            if (!TryParseIsoOffset(text[consumed..], sign, out var parsedOffsetTicks, out var offsetConsumed))
            {
                return false;
            }

            consumed += offsetConsumed;
            offsetTicks = parsedOffsetTicks;
            return consumed == text.Length;
        }

        return false;
    }

    private static bool TryParseIsoOffset(
        ReadOnlySpan<char> text,
        int sign,
        out long offsetTicks,
        out int consumed)
    {
        offsetTicks = 0;
        consumed = 0;
        if (!TryReadNDigits(text, 0, 2, out var hour))
        {
            return false;
        }

        consumed = 2;
        var minute = 0;
        var second = 0;
        var microsecond = 0;
        if (text.Length > consumed && text[consumed] == ':')
        {
            consumed++;
            if (!TryReadNDigits(text, consumed, 2, out minute) || minute > 59)
            {
                return false;
            }

            consumed += 2;
            if (text.Length > consumed && text[consumed] == ':')
            {
                consumed++;
                if (!TryReadNDigits(text, consumed, 2, out second) || second > 59)
                {
                    return false;
                }

                consumed += 2;
            }
        }
        else if (text.Length >= consumed + 2 && IsAsciiDigit(text[consumed]))
        {
            if (!TryReadNDigits(text, consumed, 2, out minute) || minute > 59)
            {
                return false;
            }

            consumed += 2;
            if (text.Length >= consumed + 2 && IsAsciiDigit(text[consumed]))
            {
                if (!TryReadNDigits(text, consumed, 2, out second) || second > 59)
                {
                    return false;
                }

                consumed += 2;
            }
        }

        if (!TryParseIsoFraction(text, ref consumed, out microsecond) || consumed != text.Length)
        {
            return false;
        }

        var absoluteTicks =
            (hour * TimeSpan.TicksPerHour)
            + (minute * TimeSpan.TicksPerMinute)
            + (second * TimeSpan.TicksPerSecond)
            + (microsecond * 10L);
        if (absoluteTicks >= TimeSpan.TicksPerDay)
        {
            return false;
        }

        offsetTicks = sign * absoluteTicks;
        return true;
    }

    private static bool TryParseIsoFraction(ReadOnlySpan<char> text, ref int index, out int microsecond)
    {
        microsecond = 0;
        if (index == text.Length || text[index] is not ('.' or ','))
        {
            return true;
        }

        index++;
        if (index == text.Length || !IsAsciiDigit(text[index]))
        {
            return false;
        }

        var digits = 0;
        while (index < text.Length && IsAsciiDigit(text[index]))
        {
            if (digits < 6)
            {
                microsecond = (microsecond * 10) + (text[index] - '0');
            }

            digits++;
            index++;
        }

        while (digits < 6)
        {
            microsecond *= 10;
            digits++;
        }

        return true;
    }

    private static bool TryCreateDateTime(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        int microsecond,
        out DateTime dateTime)
    {
        try
        {
            dateTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified)
                .AddTicks(microsecond * 10L);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            dateTime = default;
            return false;
        }
    }

    private static bool TryCreateDate(int year, int month, int day, out DateTime date)
    {
        try
        {
            date = new DateTime(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            date = default;
            return false;
        }
    }

    private static bool TryReadNDigits(ReadOnlySpan<char> text, int start, int count, out int value)
    {
        value = 0;
        if (start < 0 || count <= 0 || text.Length < start + count)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var character = text[start + i];
            if (!IsAsciiDigit(character))
            {
                value = 0;
                return false;
            }

            value = (value * 10) + (character - '0');
        }

        return true;
    }

    private static bool IsAsciiDigit(char character) => character is >= '0' and <= '9';

    /// <summary>Returns the provider-specific default group id (FalkorDB needs a non-empty value).</summary>
    public static string GetDefaultGroupId(GraphProvider provider)
    {
        // FalkorDB's default group id is '_' (a clean, validator-safe value). Mirrors
        // graphiti_core/helpers.py get_default_group_id after upstream #1549 (ff7e29c): the old
        // '\_' failed validate_group_id (backslashes are rejected) and broke the FalkorDB
        // quickstart out of the box. '_' passes ValidateGroupId below, removing that latent
        // self-inconsistency here too.
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
                $"Invalid excluded entity types: [{FormatCommaSeparated(invalidTypes)}]. Available types: [{FormatCommaSeparated(sortedAvailableTypes)}]",
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

    private static string FormatCommaSeparated(List<string> values)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        var length = (values.Count - 1) * 2;
        for (var i = 0; i < values.Count; i++)
        {
            length += values[i].Length;
        }

        var builder = new StringBuilder(length);
        builder.Append(values[0]);
        for (var i = 1; i < values.Count; i++)
        {
            builder.Append(", ");
            builder.Append(values[i]);
        }

        return builder.ToString();
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
        var vector = SnapshotEmbedding(embedding);
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

    private static float[] SnapshotEmbedding(IEnumerable<float> embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);

        if (embedding is ICollection<float> collection)
        {
            if (collection.Count == 0)
            {
                return Array.Empty<float>();
            }

            var snapshot = new float[collection.Count];
            collection.CopyTo(snapshot, 0);
            return snapshot;
        }

        if (embedding is IReadOnlyList<float> list)
        {
            return CopyReadOnlyList(list);
        }

        var values = new List<float>();
        foreach (var value in embedding)
        {
            values.Add(value);
        }

        return CopyList(values);
    }

    private static float[] CopyReadOnlyList(IReadOnlyList<float> values)
    {
        if (values.Count == 0)
        {
            return Array.Empty<float>();
        }

        var snapshot = new float[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            snapshot[i] = values[i];
        }

        return snapshot;
    }

    private static float[] CopyList(List<float> values)
    {
        if (values.Count == 0)
        {
            return Array.Empty<float>();
        }

        var snapshot = new float[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            snapshot[i] = values[i];
        }

        return snapshot;
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

        return CopyOperationList(list);
    }

    private static Func<CancellationToken, Task<T>>[] CopyOperationList<T>(
        List<Func<CancellationToken, Task<T>>> operations)
    {
        if (operations.Count == 0)
        {
            return Array.Empty<Func<CancellationToken, Task<T>>>();
        }

        var snapshot = new Func<CancellationToken, Task<T>>[operations.Count];
        for (var i = 0; i < operations.Count; i++)
        {
            snapshot[i] = operations[i];
        }

        return snapshot;
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
