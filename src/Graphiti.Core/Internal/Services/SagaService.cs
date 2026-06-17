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
            var response = await llmClient.GenerateTypedResponseAsync<Graphiti.SagaSummaryResponse>(
                SummarizeSagasPrompts.BuildSummarizeSaga(saga, episodesData),
                promptName: "summarize_sagas.summarize_saga",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            saga.Summary = HardTruncateSummary(response.Summary);

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
            return ProjectExistingSagaForNameAssociation(existing);
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

    private static SagaNode ProjectExistingSagaForNameAssociation(SagaNode existing) =>
        new()
        {
            // Name-based association intentionally uses the minimal existing-saga shape.
            Uuid = existing.Uuid,
            Name = existing.Name,
            GroupId = existing.GroupId,
            CreatedAt = existing.CreatedAt
        };

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

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
