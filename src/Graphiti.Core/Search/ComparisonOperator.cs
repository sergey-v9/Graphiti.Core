namespace Graphiti.Core.Search;

/// <summary>Comparison operators usable in search filters (date and property predicates).</summary>
public enum ComparisonOperator
{
    /// <summary>Equality (<c>=</c>).</summary>
    Equals,

    /// <summary>Inequality (<c>&lt;&gt;</c>).</summary>
    NotEquals,

    /// <summary>Strictly greater than (<c>&gt;</c>).</summary>
    GreaterThan,

    /// <summary>Strictly less than (<c>&lt;</c>).</summary>
    LessThan,

    /// <summary>Greater than or equal (<c>&gt;=</c>).</summary>
    GreaterThanEqual,

    /// <summary>Less than or equal (<c>&lt;=</c>).</summary>
    LessThanEqual,

    /// <summary>Value is null (<c>IS NULL</c>).</summary>
    IsNull,

    /// <summary>Value is not null (<c>IS NOT NULL</c>).</summary>
    IsNotNull
}

/// <summary>Conversions for <see cref="ComparisonOperator"/> to query operator strings.</summary>
public static class ComparisonOperatorExtensions
{
    /// <summary>Returns the query operator string for the comparison operator.</summary>
    public static string ToWireValue(this ComparisonOperator source) =>
        source switch
        {
            ComparisonOperator.Equals => "=",
            ComparisonOperator.NotEquals => "<>",
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.GreaterThanEqual => ">=",
            ComparisonOperator.LessThanEqual => "<=",
            ComparisonOperator.IsNull => "IS NULL",
            ComparisonOperator.IsNotNull => "IS NOT NULL",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
}
