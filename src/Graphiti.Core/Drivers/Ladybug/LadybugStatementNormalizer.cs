using System.Collections;
using System.Globalization;
using System.Text;

namespace Graphiti.Core.Drivers.Ladybug;

/// <summary>
/// Rewrites statement values the current LadybugDB package cannot bind directly while preserving
/// scalar parameters for prepared execution.
/// </summary>
internal static class LadybugStatementNormalizer
{
    internal static LadybugStatement NormalizeForPackageExecution(LadybugStatement statement)
    {
        if (statement.Parameters.Count == 0)
        {
            return statement;
        }

        Dictionary<string, string>? literals = null;
        var parameters = new Dictionary<string, object?>(statement.Parameters.Count, StringComparer.Ordinal);
        foreach (var (name, value) in statement.Parameters)
        {
            if (CanBindDirectly(value))
            {
                parameters[name] = value;
                continue;
            }

            literals ??= new Dictionary<string, string>(StringComparer.Ordinal);
            literals[name] = ToCypherLiteral(value);
        }

        return literals is null
            ? new LadybugStatement(statement.Query, parameters)
            : new LadybugStatement(RewriteParameterReferences(statement.Query, literals), parameters);
    }

    private static bool CanBindDirectly(object? value)
    {
        if (value is null)
        {
            return false;
        }

        return value is string || value is not IEnumerable;
    }

    private static string RewriteParameterReferences(
        string query,
        Dictionary<string, string> literals)
    {
        var builder = new StringBuilder(query.Length);
        var inString = false;
        for (var i = 0; i < query.Length; i++)
        {
            var current = query[i];
            if (current == '\'')
            {
                builder.Append(current);
                if (inString && i + 1 < query.Length && query[i + 1] == '\'')
                {
                    builder.Append(query[++i]);
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (!inString
                && current == '$'
                && i + 1 < query.Length
                && IsParameterStart(query[i + 1]))
            {
                var nameStart = i + 1;
                var nameEnd = nameStart + 1;
                while (nameEnd < query.Length && IsParameterPart(query[nameEnd]))
                {
                    nameEnd++;
                }

                var name = query[nameStart..nameEnd];
                if (literals.TryGetValue(name, out var literal))
                {
                    builder.Append(literal);
                    i = nameEnd - 1;
                    continue;
                }
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static string ToCypherLiteral(object? value)
    {
        if (value is null)
        {
            return "NULL";
        }

        if (value is string text)
        {
            return StringLiteral(text);
        }

        if (value is IEnumerable enumerable)
        {
            return ListLiteral(enumerable);
        }

        return ScalarLiteral(value);
    }

    private static string ListLiteral(IEnumerable values)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        var first = true;
        foreach (var value in values)
        {
            if (first)
            {
                first = false;
            }
            else
            {
                builder.Append(", ");
            }

            builder.Append(ToCypherLiteral(value));
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string ScalarLiteral(object value) =>
        value switch
        {
            bool typed => typed ? "true" : "false",
            sbyte typed => typed.ToString(CultureInfo.InvariantCulture),
            byte typed => typed.ToString(CultureInfo.InvariantCulture),
            short typed => typed.ToString(CultureInfo.InvariantCulture),
            ushort typed => typed.ToString(CultureInfo.InvariantCulture),
            int typed => typed.ToString(CultureInfo.InvariantCulture),
            uint typed => typed.ToString(CultureInfo.InvariantCulture),
            long typed => typed.ToString(CultureInfo.InvariantCulture),
            ulong typed => typed.ToString(CultureInfo.InvariantCulture),
            float typed => typed.ToString("R", CultureInfo.InvariantCulture),
            double typed => typed.ToString("R", CultureInfo.InvariantCulture),
            decimal typed => typed.ToString(CultureInfo.InvariantCulture),
            DateTime typed => StringLiteral(GraphitiHelpers.EnsureUtc(typed).ToString("O", CultureInfo.InvariantCulture)),
            DateTimeOffset typed => StringLiteral(typed.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
            _ => StringLiteral(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
        };

    private static string StringLiteral(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('\'');
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (current == '\'')
            {
                builder.Append("''");
            }
            else
            {
                builder.Append(current);
            }
        }

        builder.Append('\'');
        return builder.ToString();
    }

    private static bool IsParameterStart(char value) =>
        value == '_' || char.IsAsciiLetter(value);

    private static bool IsParameterPart(char value) =>
        value == '_' || char.IsAsciiLetterOrDigit(value);
}
