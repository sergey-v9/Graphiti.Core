using Graphiti.Core;

namespace Graphiti.Core.Tests.Maintenance;

public class EntityNodeDeduplicationTests
{
    [Fact]
    public void Resolve_FuzzyMatchesExistingNodesWithStableAliases()
    {
        var existing = new EntityNode
        {
            Uuid = "existing-openai",
            Name = "OpenAI",
            GroupId = "group",
            Labels = new List<string> { "Entity" }
        };
        var extracted = new EntityNode
        {
            Uuid = "extracted-open-ai",
            Name = "Open AI",
            GroupId = "group",
            Labels = new List<string> { "Entity", "Organization" }
        };

        var resolution = EntityNodeDeduplicator.Resolve(
            new[] { extracted },
            new[] { existing },
            MergeLabels);

        var resolved = Assert.Single(resolution.Nodes);
        Assert.Same(existing, resolved);
        Assert.Same(existing, resolution.NodesByExtractedName["Open AI"]);
        Assert.Contains("Organization", existing.Labels);
    }

    [Fact]
    public void Resolve_AmbiguousExactMatchesDoNotChooseArbitraryExistingNode()
    {
        var first = new EntityNode { Uuid = "first", Name = "Acme Corp", GroupId = "group" };
        var second = new EntityNode { Uuid = "second", Name = " acme   corp ", GroupId = "group" };
        var extracted = new EntityNode { Uuid = "extracted", Name = "ACME corp", GroupId = "group" };

        var resolution = EntityNodeDeduplicator.Resolve(
            new[] { extracted },
            new[] { first, second },
            MergeLabels);

        var resolved = Assert.Single(resolution.Nodes);
        Assert.Same(extracted, resolved);
        Assert.Same(extracted, resolution.NodesByExtractedName["ACME corp"]);
    }

    [Fact]
    public void Resolve_LowEntropyShortNameDoesNotUseFuzzyProfile()
    {
        var existing = new EntityNode { Uuid = "existing-ai", Name = "A I", GroupId = "group" };
        var extracted = new EntityNode { Uuid = "extracted-ai", Name = "AI", GroupId = "group" };

        var resolution = EntityNodeDeduplicator.Resolve(
            new[] { extracted },
            new[] { existing },
            MergeLabels);

        var resolved = Assert.Single(resolution.Nodes);
        Assert.Same(extracted, resolved);
        Assert.Same(extracted, resolution.NodesByExtractedName["AI"]);
    }

    [Fact]
    public void Resolve_ShortMultiTokenHighEntropyNameCanUseFuzzyProfile()
    {
        var existing = new EntityNode { Uuid = "existing-abc", Name = "ABC", GroupId = "group" };
        var extracted = new EntityNode { Uuid = "extracted-a-b-c", Name = "A B C", GroupId = "group" };

        var resolution = EntityNodeDeduplicator.Resolve(
            new[] { extracted },
            new[] { existing },
            MergeLabels);

        var resolved = Assert.Single(resolution.Nodes);
        Assert.Same(existing, resolved);
        Assert.Same(existing, resolution.NodesByExtractedName["A B C"]);
    }

    [Fact]
    public void Resolve_ChoosesMoreSpecificCanonicalForExactDuplicateExtractedNodes()
    {
        var generic = new EntityNode
        {
            Uuid = "generic-acme",
            Name = "Acme Corp",
            GroupId = "group",
            Labels = new List<string> { "Entity" }
        };
        var specific = new EntityNode
        {
            Uuid = "specific-acme",
            Name = " acme   corp ",
            GroupId = "group",
            Labels = new List<string> { "Entity", "Organization" }
        };

        var resolution = EntityNodeDeduplicator.Resolve(
            new[] { generic, specific },
            Array.Empty<EntityNode>(),
            MergeLabels);

        var resolved = Assert.Single(resolution.Nodes);
        Assert.Same(specific, resolved);
        Assert.Same(specific, resolution.NodesByExtractedName["Acme Corp"]);
        Assert.Same(specific, resolution.NodesByExtractedName[" acme   corp "]);
    }

    private static EntityNode MergeLabels(EntityNode existing, EntityNode extracted)
    {
        existing.Labels = existing.Labels
            .Concat(extracted.Labels)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return existing;
    }
}
