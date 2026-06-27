using System.Text.Json.Nodes;
using Graphiti.Core.Internal.Helpers;

namespace Graphiti.Core.Tests.Internal;

public class EdgeMergeHelpersTests
{
    [Fact]
    public void ReadIntArray_CoercesNumericStringsAndSkipsInvalidValues()
    {
        var response = new JsonObject
        {
            ["duplicate_facts"] = new JsonArray
            {
                1,
                "2",
                "-3",
                "not-number",
                new JsonObject(),
                null
            }
        };

        var values = EdgeMergeHelpers.ReadIntArray(response, "duplicate_facts");

        Assert.Equal(new[] { 1, 2, -3 }, values);
        Assert.Empty(EdgeMergeHelpers.ReadIntArray(response, "missing"));
        Assert.Empty(EdgeMergeHelpers.ReadIntArray(new JsonObject { ["duplicate_facts"] = "1" }, "duplicate_facts"));
    }

    [Fact]
    public void MergeEdgeOverrides_ReplacesSourceEdgeWithSnapshotOverride()
    {
        var stale = new EntityEdge
        {
            Uuid = "same",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Fact = "stale copy"
        };
        var unrelated = new EntityEdge
        {
            Uuid = "unrelated",
            SourceNodeUuid = "source",
            TargetNodeUuid = "other",
            Fact = "unrelated"
        };
        var snapshot = new EntityEdge
        {
            Uuid = "same",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Fact = "snapshot copy",
            InvalidAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var appended = new EntityEdge
        {
            Uuid = "appended",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Fact = "snapshot only"
        };

        var merged = EdgeMergeHelpers.MergeEdgeOverrides(
            new[] { stale, unrelated },
            new[] { snapshot, appended },
            edge => edge.SourceNodeUuid == "source" && edge.TargetNodeUuid == "target");

        Assert.Equal(3, merged.Count);
        Assert.Same(snapshot, merged[0]);
        Assert.Same(unrelated, merged[1]);
        Assert.Same(appended, merged[2]);
    }

    [Fact]
    public void MergeEdgeOverrides_AcceptsDictionaryValueCollectionOverrides()
    {
        var stale = new EntityEdge
        {
            Uuid = "same",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Fact = "stale copy"
        };
        var unrelated = new EntityEdge
        {
            Uuid = "unrelated",
            SourceNodeUuid = "source",
            TargetNodeUuid = "other",
            Fact = "unrelated"
        };
        var snapshot = new EntityEdge
        {
            Uuid = "same",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Fact = "snapshot copy"
        };
        var appended = new EntityEdge
        {
            Uuid = "appended",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Fact = "snapshot only"
        };
        var overrides = new Dictionary<string, EntityEdge>(StringComparer.Ordinal)
        {
            [snapshot.Uuid] = snapshot,
            [appended.Uuid] = appended
        };

        var merged = EdgeMergeHelpers.MergeEdgeOverrides(
            new[] { stale, unrelated },
            overrides.Values,
            edge => edge.SourceNodeUuid == "source" && edge.TargetNodeUuid == "target");

        Assert.Equal(3, merged.Count);
        Assert.Same(snapshot, merged[0]);
        Assert.Same(unrelated, merged[1]);
        Assert.Same(appended, merged[2]);
    }

    [Fact]
    public void MergeEdgeOverrides_WithoutOverridesCopiesSourceOrderAndDuplicates()
    {
        var first = new EntityEdge { Uuid = "first", Fact = "first" };
        var duplicate = new EntityEdge { Uuid = "first", Fact = "duplicate" };
        var second = new EntityEdge { Uuid = "second", Fact = "second" };

        var merged = EdgeMergeHelpers.MergeEdgeOverrides(
            new[] { first, duplicate, second },
            overrides: null,
            _ => true);

        Assert.Collection(
            merged,
            edge => Assert.Same(first, edge),
            edge => Assert.Same(duplicate, edge),
            edge => Assert.Same(second, edge));
    }

    [Fact]
    public void ResolveEdgeContradictions_UsesFreshExpiryTimePerInvalidatedCandidate()
    {
        var expiryTimes = new[]
        {
            new DateTime(2026, 4, 1, 12, 0, 1, DateTimeKind.Utc),
            new DateTime(2026, 4, 1, 12, 0, 2, DateTimeKind.Utc)
        };
        var nextTimeIndex = -1;
        DateTime NextTime()
        {
            var index = Interlocked.Increment(ref nextTimeIndex);
            return expiryTimes[Math.Min(index, expiryTimes.Length - 1)];
        }

        var resolved = new EntityEdge
        {
            ValidAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var firstCandidate = new EntityEdge
        {
            Uuid = "first",
            ValidAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var secondCandidate = new EntityEdge
        {
            Uuid = "second",
            ValidAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var invalidated = EdgeMergeHelpers.ResolveEdgeContradictions(
            resolved,
            new[] { firstCandidate, secondCandidate },
            NextTime);

        Assert.Equal(new[] { firstCandidate, secondCandidate }, invalidated);
        Assert.Equal(resolved.ValidAt, firstCandidate.InvalidAt);
        Assert.Equal(resolved.ValidAt, secondCandidate.InvalidAt);
        Assert.Equal(expiryTimes[0], firstCandidate.ExpiredAt);
        Assert.Equal(expiryTimes[1], secondCandidate.ExpiredAt);
    }
}
