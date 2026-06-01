namespace Graphiti.Core.Internal.Services;

internal sealed class SagaService(
    Func<IGraphDriver> driverAccessor,
    ILlmClient llmClient,
    TimeProvider timeProvider)
{
    public async Task<SagaNode> SummarizeSagaAsync(string sagaId, CancellationToken cancellationToken = default)
    {
        using var activity = GraphitiTelemetry.StartActivity("SummarizeSaga");
        activity?.SetTag("graphiti.saga.uuid", sagaId);

        try
        {
            var driver = driverAccessor();
            var saga = await SagaNode.GetByUuidAsync(driver, sagaId, cancellationToken).ConfigureAwait(false);
            var episodesData = await driver.GetSagaEpisodeContentsAsync(
                sagaId,
                saga.LastSummarizedAt,
                limit: 200,
                cancellationToken).ConfigureAwait(false);

            if (episodesData.Count == 0)
            {
                activity?.SetTag("graphiti.episodes.count", 0);
                GraphitiTelemetry.SetOk(activity);
                return saga;
            }

            var maxValidAt = MaxValidAt(episodesData);
            var messages = BuildSagaSummaryMessages(saga, episodesData);

            try
            {
                var response = await llmClient.GenerateTypedResponseAsync<Graphiti.SagaSummaryResponse>(
                    messages,
                    promptName: "summarize_sagas.summarize_saga",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                saga.Summary = HardTruncateSummary(
                    string.IsNullOrWhiteSpace(response.Summary)
                        ? BuildFallbackSummary(episodesData)
                        : response.Summary);
            }
            catch (NotImplementedException)
            {
                saga.Summary = HardTruncateSummary(BuildFallbackSummary(episodesData));
            }

            saga.LastSummarizedAt = UtcNow();
            if (maxValidAt is not null
                && (saga.LastSummarizedEpisodeValidAt is null
                    || maxValidAt > saga.LastSummarizedEpisodeValidAt))
            {
                saga.LastSummarizedEpisodeValidAt = maxValidAt;
            }

            await saga.SaveAsync(driver, cancellationToken).ConfigureAwait(false);
            activity?.SetTag("graphiti.episodes.count", episodesData.Count);
            activity?.SetTag("graphiti.summary.length", saga.Summary.Length);
            GraphitiTelemetry.SetOk(activity);
            return saga;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    public async Task AssociateAsync(
        object? saga,
        string groupId,
        DateTime now,
        EpisodicNode episode,
        string? sagaPreviousEpisodeUuid,
        CancellationToken cancellationToken)
    {
        if (saga is null)
        {
            return;
        }

        var driver = driverAccessor();
        var sagaNode = await ResolveSagaAsync(
            driver,
            saga,
            groupId,
            episode.ValidAt,
            cancellationToken).ConfigureAwait(false);

        var previousEpisodeUuid = sagaPreviousEpisodeUuid
                                  ?? await driver.GetSagaPreviousEpisodeUuidAsync(
                                      sagaNode.Uuid,
                                      episode.Uuid,
                                      cancellationToken).ConfigureAwait(false);
        if (previousEpisodeUuid is not null)
        {
            await new NextEpisodeEdge
            {
                SourceNodeUuid = previousEpisodeUuid,
                TargetNodeUuid = episode.Uuid,
                GroupId = groupId,
                CreatedAt = now
            }.SaveAsync(driver, cancellationToken).ConfigureAwait(false);
        }

        await new HasEpisodeEdge
        {
            SourceNodeUuid = sagaNode.Uuid,
            TargetNodeUuid = episode.Uuid,
            GroupId = groupId,
            CreatedAt = now
        }.SaveAsync(driver, cancellationToken).ConfigureAwait(false);

        sagaNode.FirstEpisodeUuid ??= episode.Uuid;
        sagaNode.LastEpisodeUuid = episode.Uuid;
        await sagaNode.SaveAsync(driver, cancellationToken).ConfigureAwait(false);
    }

    public async Task AssociateBulkAsync(
        object? saga,
        string groupId,
        DateTime now,
        IReadOnlyList<EpisodicNode> orderedEpisodes,
        CancellationToken cancellationToken)
    {
        if (saga is null || orderedEpisodes.Count == 0)
        {
            return;
        }

        var driver = driverAccessor();
        var sagaNode = await ResolveSagaAsync(
            driver,
            saga,
            groupId,
            orderedEpisodes[0].ValidAt,
            cancellationToken).ConfigureAwait(false);
        var previousEpisodeUuid = await driver.GetSagaPreviousEpisodeUuidAsync(
            sagaNode.Uuid,
            orderedEpisodes[0].Uuid,
            cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < orderedEpisodes.Count; i++)
        {
            var episode = orderedEpisodes[i];
            if (previousEpisodeUuid is not null)
            {
                await new NextEpisodeEdge
                {
                    SourceNodeUuid = previousEpisodeUuid,
                    TargetNodeUuid = episode.Uuid,
                    GroupId = groupId,
                    CreatedAt = now
                }.SaveAsync(driver, cancellationToken).ConfigureAwait(false);
            }

            await new HasEpisodeEdge
            {
                SourceNodeUuid = sagaNode.Uuid,
                TargetNodeUuid = episode.Uuid,
                GroupId = groupId,
                CreatedAt = now
            }.SaveAsync(driver, cancellationToken).ConfigureAwait(false);

            sagaNode.FirstEpisodeUuid ??= episode.Uuid;
            sagaNode.LastEpisodeUuid = episode.Uuid;
            previousEpisodeUuid = episode.Uuid;
        }

        await sagaNode.SaveAsync(driver, cancellationToken).ConfigureAwait(false);
    }

    private static Message[] BuildSagaSummaryMessages(
        SagaNode saga,
        IReadOnlyList<SagaEpisodeContent> episodes)
    {
        var episodesText = JoinEpisodeContents(episodes, "\n---\n", "(no messages)");
        var existingSummarySection = string.IsNullOrWhiteSpace(saga.Summary)
            ? string.Empty
            : $"""

<EXISTING_KNOWLEDGE>
{saga.Summary}
</EXISTING_KNOWLEDGE>
The EXISTING_KNOWLEDGE contains previously extracted facts. Merge any new facts from MESSAGES into it. When newer messages contradict older facts, prefer the newer fact. If MESSAGES add no new durable facts, return the existing knowledge unchanged.
""";

        return new[]
        {
            new Message(
                "system",
                $"You extract durable knowledge from message threads. Output a factual knowledge brief - facts, decisions, preferences, plans, entities, and relationships - that stands alone without reference to the original messages. Stay under {TextUtilities.MaxSummaryChars} characters."),
            new Message(
                "user",
                $"""
NEVER use meta-language verbs: "mentioned", "discussed", "noted", "stated", "described", "referenced", "indicated", "reported", "talked about", "brought up" - these describe conversational dynamics, not knowledge. State facts directly instead.
NEVER refer to the messages, conversation, thread, or participants' communicative acts. The output must read as if no conversation happened - only the facts matter.
NEVER begin with "This conversation", "The thread", "In this thread", or "The discussion".
NEVER infer preferences or habits from a single passing mention. When a person explicitly states a preference ("I prefer X", "I love X", "I always do X"), capture it as a stated preference attributed to that person.

Your task: extract all durable knowledge from the MESSAGES below and produce a factual knowledge brief for the topic "{saga.Name}".

Capture explicitly stated:
- Facts and concrete details (names, dates, numbers, locations)
- Decisions and their outcomes
- Preferences and requirements (when a person explicitly claims them)
- Plans, next steps, and commitments
- Relationships between entities (who works where, who owns what)
- State changes (what was X, now is Y)

Write 2-6 dense sentences. Use third person. Preserve all names, dates, counts, and temporal qualifiers. Lead with the most important fact or decision.
{existingSummarySection}
<MESSAGES>
{episodesText}
</MESSAGES>
""")
        };
    }

    private static async Task<SagaNode> GetOrCreateAsync(
        IGraphDriver driver,
        string sagaName,
        string groupId,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        var existing = await driver.FindSagaByNameAsync(sagaName, groupId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var saga = new SagaNode
        {
            Name = sagaName,
            GroupId = groupId,
            CreatedAt = createdAt
        };
        await saga.SaveAsync(driver, cancellationToken).ConfigureAwait(false);
        return saga;
    }

    private static async Task<SagaNode> ResolveSagaAsync(
        IGraphDriver driver,
        object saga,
        string groupId,
        DateTime createdAt,
        CancellationToken cancellationToken) =>
        saga switch
        {
            SagaNode providedSaga => providedSaga,
            string sagaName => await GetOrCreateAsync(
                driver,
                sagaName,
                groupId,
                createdAt,
                cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException("saga must be a string or SagaNode", nameof(saga))
        };

    private static string HardTruncateSummary(string? summary) =>
        string.IsNullOrEmpty(summary)
            ? string.Empty
            : summary.Length > TextUtilities.MaxSummaryChars
                ? summary[..TextUtilities.MaxSummaryChars]
                : summary;

    private static string BuildFallbackSummary(IReadOnlyList<SagaEpisodeContent> episodesData) =>
        JoinEpisodeContents(episodesData, "\n");

    private static DateTime? MaxValidAt(IReadOnlyList<SagaEpisodeContent> episodes)
    {
        DateTime? maxValidAt = null;
        for (var i = 0; i < episodes.Count; i++)
        {
            var validAt = episodes[i].ValidAt;
            if (validAt is not null && (maxValidAt is null || validAt > maxValidAt))
            {
                maxValidAt = validAt;
            }
        }

        return maxValidAt;
    }

    private static string JoinEpisodeContents(
        IReadOnlyList<SagaEpisodeContent> episodes,
        string separator,
        string emptyValue = "")
    {
        if (episodes.Count == 0)
        {
            return emptyValue;
        }

        var length = separator.Length * (episodes.Count - 1);
        for (var i = 0; i < episodes.Count; i++)
        {
            length += episodes[i].Content.Length;
        }

        return string.Create(length, (Episodes: episodes, Separator: separator), static (destination, state) =>
        {
            var offset = 0;
            for (var i = 0; i < state.Episodes.Count; i++)
            {
                var content = state.Episodes[i].Content.AsSpan();
                content.CopyTo(destination.Slice(offset));
                offset += content.Length;

                if (i == state.Episodes.Count - 1)
                {
                    continue;
                }

                var separator = state.Separator.AsSpan();
                separator.CopyTo(destination.Slice(offset));
                offset += separator.Length;
            }
        });
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

}
