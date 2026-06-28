using System.Globalization;

namespace Graphiti.Core.Text;

/// <summary>
/// A culture-invariant ISO-8601 date/time parser that returns UTC. It accepts extended and basic
/// calendar dates, ISO week dates, optional time-of-day with fractional seconds, and <c>Z</c> or
/// numeric UTC offsets, using non-throwing parse paths so malformed input is reported as a failed
/// result rather than an exception.
/// </summary>
internal static class IsoDateParser
{
    /// <summary>
    /// Attempts to parse <paramref name="text"/> as an ISO-8601 date or date-time, producing a UTC
    /// <see cref="DateTime"/>. Returns <c>false</c> for any input the parser does not fully consume.
    /// </summary>
    public static bool TryParseIsoDateTime(ReadOnlySpan<char> text, out DateTime parsed)
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
}
