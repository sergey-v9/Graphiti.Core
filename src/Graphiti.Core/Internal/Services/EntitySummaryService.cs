using System.Text;
using Graphiti.Core.Text;
using Microsoft.Extensions.Logging;

namespace Graphiti.Core.Internal.Services;

internal sealed class EntitySummaryService(
    ILlmClient llmClient,
    ILogger logger,
    Func<int> getMaxDegreeOfParallelism)
{
    private const int MaxNodesPerSummaryFlight = 30;

    public async Task ExtractEntitySummariesAsync(
        List<EntityNode> nodes,
        EpisodicNode episode,
        IReadOnlyList<EpisodicNode> previousEpisodes,
        IReadOnlyList<EntityEdge> edges,
        CancellationToken cancellationToken)
    {
        await ExtractEntitySummariesAsync(
            nodes,
            episode,
            previousEpisodes,
            edges,
            shouldSummarizeNode: null,
            skipFactAppending: false,
            entityTypes: null,
            includeTypeDescriptions: false,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task ExtractEntitySummariesAsync(
        List<EntityNode> nodes,
        EpisodicNode? episode,
        IReadOnlyList<EpisodicNode>? previousEpisodes,
        IReadOnlyList<EntityEdge> edges,
        Func<EntityNode, CancellationToken, ValueTask<bool>>? shouldSummarizeNode,
        bool skipFactAppending,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        bool includeTypeDescriptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        using var activity = GraphitiTelemetry.StartActivity("Extraction.EntitySummaries");
        activity?.SetTag("graphiti.group_id", episode?.GroupId ?? nodes.FirstOrDefault()?.GroupId);
        activity?.SetTag("graphiti.input.nodes", nodes.Count);
        activity?.SetTag("graphiti.input.edges", edges.Count);
        activity?.SetTag("graphiti.previous_episodes.count", previousEpisodes?.Count ?? 0);
        activity?.SetTag("graphiti.summary.skip_fact_appending", skipFactAppending);

        try
        {
            if (nodes.Count == 0)
            {
                activity?.SetTag("graphiti.extraction.skipped", true);
                activity?.SetTag("graphiti.extraction.targets", 0);
                activity?.SetTag("graphiti.summary.direct_appends", 0);
                GraphitiTelemetry.SetOk(activity);
                return;
            }

            var edgesByNode = EntityTypeResolver.BuildEdgesByNode(edges);
            var nodesNeedingLlm = new List<EntityNode>();
            var directAppends = 0;
            var filteredNodes = 0;
            for (var i = 0; i < nodes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = nodes[i];
                if (shouldSummarizeNode is not null
                    && !await shouldSummarizeNode(node, cancellationToken).ConfigureAwait(false))
                {
                    filteredNodes++;
                    continue;
                }

                if (skipFactAppending)
                {
                    if (episode is not null || !string.IsNullOrEmpty(node.Summary))
                    {
                        nodesNeedingLlm.Add(node);
                    }
                    else
                    {
                        filteredNodes++;
                    }

                    continue;
                }

                edgesByNode.TryGetValue(node.Uuid, out var nodeEdges);
                var summaryWithEdges = BuildSummaryWithEdgeFacts(node.Summary, nodeEdges);
                if (!string.IsNullOrEmpty(summaryWithEdges)
                    && summaryWithEdges.Length <= TextUtilities.MaxSummaryChars * 2)
                {
                    node.Summary = summaryWithEdges;
                    directAppends++;
                    continue;
                }

                if (string.IsNullOrEmpty(summaryWithEdges) && episode is null)
                {
                    filteredNodes++;
                    continue;
                }

                nodesNeedingLlm.Add(node);
            }

            activity?.SetTag("graphiti.extraction.targets", nodesNeedingLlm.Count);
            activity?.SetTag("graphiti.extraction.skipped", nodesNeedingLlm.Count == 0 && directAppends == 0);
            activity?.SetTag("graphiti.summary.direct_appends", directAppends);
            activity?.SetTag("graphiti.summary.filtered_nodes", filteredNodes);

            if (nodesNeedingLlm.Count == 0)
            {
                GraphitiTelemetry.SetOk(activity);
                return;
            }

            var flights = BuildFlights(nodesNeedingLlm);
            activity?.SetTag("graphiti.summary.flights", flights.Count);
            var entityTypeDescriptions = includeTypeDescriptions
                ? BuildEntityTypeDescriptions(entityTypes)
                : null;
            var episodeContent = episode?.Content ?? string.Empty;
            await ThrottledWork.ForEachAsync(
                flights,
                (flight, token) => ProcessSummaryFlightAsync(
                    flight,
                    episodeContent,
                    previousEpisodes ?? Array.Empty<EpisodicNode>(),
                    entityTypeDescriptions,
                    skipFactAppending,
                    token),
                getMaxDegreeOfParallelism(),
                cancellationToken).ConfigureAwait(false);

            GraphitiTelemetry.SetOk(activity);
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private async Task ProcessSummaryFlightAsync(
        List<EntityNode> nodes,
        string episodeContent,
        IReadOnlyList<EpisodicNode> previousEpisodes,
        Dictionary<string, string>? entityTypeDescriptions,
        bool useEpisodePrompt,
        CancellationToken cancellationToken)
    {
        var context = ExtractNodesPrompts.BuildExtractSummariesContext(
            nodes,
            episodeContent,
            previousEpisodes,
            entityTypeDescriptions);
        var promptName = useEpisodePrompt
            ? "extract_nodes.extract_entity_summaries_from_episodes"
            : "extract_nodes.extract_summaries_batch";
        var messages = useEpisodePrompt
            ? ExtractNodesPrompts.BuildExtractEntitySummariesFromEpisodes(context)
            : ExtractNodesPrompts.BuildExtractSummariesBatch(context);
        var response = await llmClient.GenerateTypedResponseAsync<Graphiti.SummarizedEntitiesResponse>(
            messages,
            modelSize: ModelSize.Small,
            groupId: nodes.Count == 0 ? null : nodes[0].GroupId,
            promptName: promptName,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        ApplySummaries(nodes, response);
    }

    private void ApplySummaries(
        List<EntityNode> nodes,
        Graphiti.SummarizedEntitiesResponse response)
    {
        var nameToNodes = new Dictionary<string, List<EntityNode>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (!nameToNodes.TryGetValue(node.Name, out var matchingNodes))
            {
                matchingNodes = new List<EntityNode>();
                nameToNodes[node.Name] = matchingNodes;
            }

            matchingNodes.Add(node);
        }

        foreach (var summary in response.Summaries)
        {
            if (string.IsNullOrWhiteSpace(summary.Name))
            {
                continue;
            }

            if (!nameToNodes.TryGetValue(summary.Name, out var matchingNodes))
            {
                // Python logs only the first 30 chars of the name (node_operations.py:1001-1004,
                // '%.30s') so a runaway hallucinated name does not flood the log.
                var loggedName = summary.Name.Length > 30 ? summary.Name[..30] : summary.Name;
                GraphitiLog.UnknownEntitySummaryReturned(logger, loggedName);
                continue;
            }

            var truncated = TextUtilities.TruncateAtSentence(
                summary.Summary,
                TextUtilities.MaxSummaryChars) ?? string.Empty;
            for (var i = 0; i < matchingNodes.Count; i++)
            {
                matchingNodes[i].Summary = truncated;
            }
        }
    }

    private static string BuildSummaryWithEdgeFacts(
        string summary,
        List<EntityEdge>? edges)
    {
        if (edges is null || edges.Count == 0)
        {
            return summary;
        }

        var builder = new StringBuilder(summary ?? string.Empty);
        for (var i = 0; i < edges.Count; i++)
        {
            var fact = edges[i].Fact;
            if (string.IsNullOrEmpty(fact))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(fact);
        }

        return builder.ToString().Trim();
    }

    private static List<List<EntityNode>> BuildFlights(List<EntityNode> nodes)
    {
        var flights = new List<List<EntityNode>>((nodes.Count + MaxNodesPerSummaryFlight - 1) / MaxNodesPerSummaryFlight);
        for (var start = 0; start < nodes.Count; start += MaxNodesPerSummaryFlight)
        {
            var count = Math.Min(MaxNodesPerSummaryFlight, nodes.Count - start);
            var flight = new List<EntityNode>(count);
            for (var index = 0; index < count; index++)
            {
                flight.Add(nodes[start + index]);
            }

            flights.Add(flight);
        }

        return flights;
    }

    private static Dictionary<string, string>? BuildEntityTypeDescriptions(
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        if (entityTypes is null || entityTypes.Count == 0)
        {
            return null;
        }

        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in entityTypes)
        {
            var description = TruncateTypeDescription(pair.Value.Description);
            if (!string.IsNullOrWhiteSpace(description))
            {
                descriptions[pair.Value.Name] = description;
            }
        }

        return descriptions.Count == 0 ? null : descriptions;
    }

    private static string TruncateTypeDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var paragraphLines = new List<string>();
        using var reader = new StringReader(description);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (paragraphLines.Count > 0)
                {
                    break;
                }

                continue;
            }

            paragraphLines.Add(line.Trim());
        }

        var text = string.Join(' ', paragraphLines);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sentences = new List<string>(3);
        var remaining = text;
        for (var i = 0; i < 3 && remaining.Length > 0; i++)
        {
            var boundary = FindSentenceEnd(remaining);
            if (boundary < 0)
            {
                sentences.Add(remaining);
                break;
            }

            sentences.Add(remaining[..(boundary + 1)]);
            remaining = remaining[(boundary + 1)..].TrimStart();
        }

        return string.Join(' ', sentences).Trim();
    }

    private static int FindSentenceEnd(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch is not ('.' or '!' or '?'))
            {
                continue;
            }

            if (i + 1 >= text.Length)
            {
                return i;
            }

            if (text[i + 1] == ' ' && i + 2 < text.Length && char.IsUpper(text[i + 2]))
            {
                return i;
            }
        }

        return -1;
    }
}
