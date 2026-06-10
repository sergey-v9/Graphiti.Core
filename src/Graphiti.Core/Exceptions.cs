namespace Graphiti.Core;

/// <summary>
/// Base exception type for all Graphiti Core errors. Mirrors Python's <c>GraphitiError</c>.
/// </summary>
public class GraphitiException : Exception
{
    /// <summary>Initializes a new instance with the supplied error message.</summary>
    /// <param name="message">A human-readable description of the failure.</param>
    public GraphitiException(string message) : base(message)
    {
    }
}

/// <summary>Thrown when a single edge cannot be found by its UUID.</summary>
public sealed class EdgeNotFoundException : GraphitiException
{
    /// <summary>Initializes the exception for the missing edge.</summary>
    /// <param name="uuid">UUID of the edge that could not be located.</param>
    public EdgeNotFoundException(string uuid) : base($"edge {uuid} not found")
    {
    }
}

/// <summary>Thrown when none of the requested edges could be found.</summary>
public sealed class EdgesNotFoundException : GraphitiException
{
    /// <summary>Initializes the exception for the missing edges.</summary>
    /// <param name="uuids">UUIDs that were requested but not found.</param>
    public EdgesNotFoundException(IEnumerable<string> uuids)
        : base($"None of the edges for [{string.Join(", ", uuids)}] were found.")
    {
    }
}

/// <summary>Thrown when no edges exist for the requested group ids.</summary>
public sealed class GroupsEdgesNotFoundException : GraphitiException
{
    /// <summary>Initializes the exception for the requested group ids.</summary>
    /// <param name="groupIds">Graph partition identifiers that yielded no edges.</param>
    public GroupsEdgesNotFoundException(IEnumerable<string> groupIds)
        : base($"no edges found for group ids [{string.Join(", ", groupIds)}]")
    {
    }
}

/// <summary>Thrown when no nodes exist for the requested group ids.</summary>
public sealed class GroupsNodesNotFoundException : GraphitiException
{
    /// <summary>Initializes the exception for the requested group ids.</summary>
    /// <param name="groupIds">Graph partition identifiers that yielded no nodes.</param>
    public GroupsNodesNotFoundException(IEnumerable<string> groupIds)
        : base($"no nodes found for group ids [{string.Join(", ", groupIds)}]")
    {
    }
}

/// <summary>Thrown when a single node cannot be found by its UUID.</summary>
public sealed class NodeNotFoundException : GraphitiException
{
    /// <summary>Initializes the exception for the missing node.</summary>
    /// <param name="uuid">UUID of the node that could not be located.</param>
    public NodeNotFoundException(string uuid) : base($"node {uuid} not found")
    {
    }
}

/// <summary>Thrown when a search reranker fails or is misconfigured.</summary>
public sealed class SearchRerankerException : GraphitiException
{
    /// <summary>Initializes the exception with reranker-specific detail.</summary>
    /// <param name="message">Description of the reranker failure.</param>
    public SearchRerankerException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when a custom entity type declares an attribute whose name collides with a
/// protected, framework-reserved attribute name.
/// </summary>
public sealed class EntityTypeValidationException : GraphitiException
{
    /// <summary>Initializes the exception describing the offending attribute.</summary>
    /// <param name="entityType">The entity type that declared the invalid attribute.</param>
    /// <param name="attribute">The protected attribute name that may not be reused.</param>
    public EntityTypeValidationException(string entityType, string attribute)
        : base($"{attribute} cannot be used as an attribute for {entityType} as it is a protected attribute name.")
    {
    }
}

/// <summary>Thrown when a <c>group_id</c> contains characters outside the allowed set.</summary>
public sealed class GroupIdValidationException : GraphitiException
{
    /// <summary>Initializes the exception for the invalid group id.</summary>
    /// <param name="groupId">The group id that failed validation.</param>
    public GroupIdValidationException(string groupId)
        : base($"group_id \"{groupId}\" must contain only alphanumeric characters, dashes, or underscores")
    {
    }
}

/// <summary>Thrown when one or more node labels contain invalid characters.</summary>
public sealed class NodeLabelValidationException : GraphitiException
{
    /// <summary>Initializes the exception for the invalid node labels.</summary>
    /// <param name="nodeLabels">The node labels that failed validation.</param>
    public NodeLabelValidationException(IEnumerable<string> nodeLabels)
        : base("node_labels must start with a letter or underscore and contain only alphanumeric characters or underscores: " +
               string.Join(", ", nodeLabels.Select(label => $"\"{label}\"")))
    {
    }
}
