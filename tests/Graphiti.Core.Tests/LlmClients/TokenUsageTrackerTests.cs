using System.Collections.Frozen;
using Graphiti.Core;

namespace Graphiti.Core.Tests.LlmClients;

public class TokenUsageTrackerTests
{
    [Fact]
    public void AddUsage_IsSafeForConcurrentAsyncClients()
    {
        var tracker = new TokenUsageTracker();

        Parallel.For(
            0,
            10_000,
            _ => tracker.AddUsage("extract_nodes", inputTokens: 1, outputTokens: 2));

        var usage = tracker.Usage["extract_nodes"];
        Assert.Equal(10_000, usage.CallCount);
        Assert.Equal(10_000, usage.InputTokens);
        Assert.Equal(20_000, usage.OutputTokens);
        Assert.Equal(1, usage.AvgInputTokens);
        Assert.Equal(2, usage.AvgOutputTokens);

        var total = tracker.GetTotalUsage();
        Assert.Equal(10_000, total.InputTokens);
        Assert.Equal(20_000, total.OutputTokens);
    }

    [Fact]
    public void AddUsage_UsesInt64TotalsWithoutOverflowingAtIntMaxValue()
    {
        var tracker = new TokenUsageTracker();

        tracker.AddUsage("extract_nodes", int.MaxValue, int.MaxValue);
        tracker.AddUsage("extract_nodes", 1, 2);

        var usage = tracker.Usage["extract_nodes"];
        Assert.Equal(2, usage.CallCount);
        Assert.Equal((long)int.MaxValue + 1, usage.InputTokens);
        Assert.Equal((long)int.MaxValue + 2, usage.OutputTokens);
        Assert.Equal(((long)int.MaxValue + 1) / 2.0, usage.AvgInputTokens);
        Assert.Equal(((long)int.MaxValue + 2) / 2.0, usage.AvgOutputTokens);

        var total = tracker.GetTotalUsage();
        Assert.Equal((long)int.MaxValue + 1, total.InputTokens);
        Assert.Equal(((long)int.MaxValue * 2) + 3, total.TotalTokens);
    }

    [Fact]
    public void AddUsage_NormalizesBlankPromptNamesAndResetClearsSnapshot()
    {
        var tracker = new TokenUsageTracker();

        tracker.AddUsage("", inputTokens: 3, outputTokens: 4);
        tracker.AddUsage("   ", inputTokens: 5, outputTokens: 6);
        tracker.Reset();

        Assert.Empty(tracker.Usage);
        Assert.Equal(0, tracker.GetTotalUsage().TotalTokens);
    }

    [Fact]
    public void Usage_ReturnsImmutableSnapshot()
    {
        var tracker = new TokenUsageTracker();
        tracker.AddUsage("extract_nodes", inputTokens: 1, outputTokens: 2);

        var snapshot = tracker.Usage;

        Assert.IsAssignableFrom<FrozenDictionary<string, PromptTokenUsage>>(snapshot);
        Assert.Equal(1, snapshot["extract_nodes"].CallCount);
        Assert.Equal(1, snapshot["extract_nodes"].InputTokens);
    }

    [Fact]
    public void UsageSnapshot_DoesNotChangeAfterReset()
    {
        var tracker = new TokenUsageTracker();
        tracker.AddUsage("extract_nodes", inputTokens: 1, outputTokens: 2);

        var snapshot = tracker.Usage;
        tracker.Reset();

        Assert.Equal(1, snapshot["extract_nodes"].InputTokens);
        Assert.Equal(1, snapshot["extract_nodes"].CallCount);
        Assert.Empty(tracker.Usage);
    }

    [Fact]
    public void UsageSnapshotValueMutation_DoesNotAffectTracker()
    {
        var tracker = new TokenUsageTracker();
        tracker.AddUsage("extract_nodes", inputTokens: 1, outputTokens: 2);

        var snapshot = tracker.Usage;
        snapshot["extract_nodes"].InputTokens = 999;
        snapshot["extract_nodes"].OutputTokens = 999;

        var current = tracker.Usage["extract_nodes"];
        Assert.Equal(1, current.CallCount);
        Assert.Equal(1, current.InputTokens);
        Assert.Equal(2, current.OutputTokens);
    }

    [Fact]
    public void PromptTokenUsage_AveragesReturnZeroWhenNoCallsRecorded()
    {
        var usage = new PromptTokenUsage
        {
            PromptName = "extract_nodes",
            InputTokens = 10,
            OutputTokens = 20
        };

        Assert.Equal(0, usage.CallCount);
        Assert.Equal(0, usage.AvgInputTokens);
        Assert.Equal(0, usage.AvgOutputTokens);
    }
}
