using Graphiti.Core;

namespace Graphiti.Core.Tests.Search;

public class SearchUtilitiesTests
{
    [Fact]
    public void ReciprocalRankFusion_UsesPythonRankConstantAndScoreFormula()
    {
        var ranked = SearchUtilities.ReciprocalRankFusion(
            new IReadOnlyList<string>[]
            {
                new[] { "a", "b" },
                new[] { "b", "c" }
            },
            item => item,
            limit: 10);

        Assert.Equal(new[] { "b", "a", "c" }, ranked.Select(item => item.Item));
        Assert.Equal(1.5f, ranked[0].Score, precision: 6);
        Assert.Equal(1f, ranked[1].Score, precision: 6);
        Assert.Equal(0.5f, ranked[2].Score, precision: 6);
    }

    [Fact]
    public void ReciprocalRankFusion_FiltersByMinimumScore()
    {
        var ranked = SearchUtilities.ReciprocalRankFusion(
            new IReadOnlyList<string>[] { new[] { "a", "b", "c" } },
            item => item,
            limit: 10,
            minScore: 0.5f);

        Assert.Equal(new[] { "a", "b" }, ranked.Select(item => item.Item));
    }

    [Fact]
    public void ReciprocalRankFusion_KeepsFirstSeenOrderForTies()
    {
        var ranked = SearchUtilities.ReciprocalRankFusion(
            new IReadOnlyList<string>[]
            {
                new[] { "first" },
                new[] { "second" }
            },
            item => item,
            limit: 10);

        Assert.Equal(new[] { "first", "second" }, ranked.Select(item => item.Item));
        Assert.All(ranked, item => Assert.Equal(1f, item.Score));
    }

    [Fact]
    public void ReciprocalRankFusion_BoundedTopKMatchesStableFullSortOracleForLargeInputs()
    {
        const int limit = 25;
        var rankedLists = Enumerable.Range(0, 12)
            .Select(listIndex => (IReadOnlyList<string>)Enumerable.Range(0, 250)
                .Select(rank => $"item-{((rank * 37) + (listIndex * 13)) % 800}")
                .ToArray())
            .ToArray();

        var expected = ReciprocalRankFusionFullSortOracle(rankedLists, limit, minScore: 0.05f);
        var actual = SearchUtilities.ReciprocalRankFusion(
            rankedLists,
            item => item,
            limit,
            minScore: 0.05f);

        Assert.Equal(limit, actual.Count);
        Assert.Equal(expected.Select(item => item.Item), actual.Select(item => item.Item));
        Assert.Equal(expected.Select(item => item.Score), actual.Select(item => item.Score));
    }

    [Fact]
    public void CalculateCosineSimilarity_RejectsMismatchedNonEmptyDimensions()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            SearchUtilities.CalculateCosineSimilarity(
                new List<float> { 1f, 1f, 100f },
                new[] { 1f, 1f }));

        Assert.Contains("dimension 3", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dimension 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CalculateCosineSimilarity_ReturnsZeroForMissingOrZeroVectors()
    {
        Assert.Equal(0, SearchUtilities.CalculateCosineSimilarity(null, new[] { 1f }));
        Assert.Equal(0, SearchUtilities.CalculateCosineSimilarity(Array.Empty<float>(), new[] { 1f }));
        Assert.Equal(0, SearchUtilities.CalculateCosineSimilarity(new[] { 0f, 0f }, new[] { 1f, 1f }));
    }

    [Fact]
    public void CalculateCosineSimilarity_ReturnsZeroForNonFiniteVectors()
    {
        Assert.Equal(0, SearchUtilities.CalculateCosineSimilarity(new[] { float.NaN, 1f }, new[] { 1f, 1f }));
        Assert.Equal(0, SearchUtilities.CalculateCosineSimilarity(new[] { float.PositiveInfinity, 1f }, new[] { 1f, 1f }));

        var scorer = SearchUtilities.CreateCosineSimilarityScorer(new[] { 1f, 1f });
        Assert.Equal(0, scorer.Score(new[] { 1f, float.NegativeInfinity }));
    }

    [Fact]
    public void CosineSimilarityScorer_MatchesSingleShotScoring()
    {
        var query = new List<float> { 2f, 0f, 100f };
        var scorer = SearchUtilities.CreateCosineSimilarityScorer(query);

        Assert.Equal(
            SearchUtilities.CalculateCosineSimilarity(query, new[] { 4f, 0f, 200f }),
            scorer.Score(new[] { 4f, 0f, 200f }),
            precision: 6);
        Assert.Throws<ArgumentException>(() => scorer.Score(new[] { 1f, 1f }));
        Assert.Equal(0, scorer.Score(null));
        Assert.Equal(0, scorer.Score(Array.Empty<float>()));
        Assert.Equal(0, SearchUtilities.CreateCosineSimilarityScorer(new[] { 0f, 0f }).Score(new[] { 1f, 1f }));
    }

    [Fact]
    public void CosineSimilarityScorer_UsesQuerySnapshotAndCachedNorm()
    {
        var query = new List<float> { 3f, 4f };
        var scorer = SearchUtilities.CreateCosineSimilarityScorer(query);

        query[0] = 0;
        query[1] = 1;

        Assert.Equal(1f, scorer.Score(new[] { 6f, 8f }), precision: 6);
        Assert.Equal(0.8f, scorer.Score(new[] { 0f, 10f }), precision: 6);
        Assert.Equal(0, scorer.Score(new[] { float.NaN, 1f }));
    }

    [Fact]
    public void TopByScore_MatchesStableDescendingSortWithLimitAndMinScore()
    {
        var candidates = Enumerable.Range(0, 120)
            .Select(index => new ScoredCandidate($"low-{index}", 0.1f))
            .ToList();
        candidates.Insert(10, new ScoredCandidate("first-tie", 1f));
        candidates.Insert(40, new ScoredCandidate("second-tie", 1f));
        candidates.Insert(70, new ScoredCandidate("third", 0.9f));
        candidates.Insert(90, new ScoredCandidate("filtered", 0.4f));

        var ranked = SearchUtilities.TopByScore(
            candidates,
            candidate => candidate.Score,
            limit: 3,
            minScore: 0.5f);

        Assert.Equal(new[] { "first-tie", "second-tie", "third" }, ranked.Select(item => item.Item.Name));
        Assert.Equal(new[] { 1f, 1f, 0.9f }, ranked.Select(item => item.Score));
    }

    [Fact]
    public void TopByScore_CanApplyStrictMinimumScoreForVectorSearchParity()
    {
        var ranked = SearchUtilities.TopByScore(
            new[]
            {
                new ScoredCandidate("above", 0.7f),
                new ScoredCandidate("boundary", 0.6f),
                new ScoredCandidate("below", 0.5f)
            },
            candidate => candidate.Score,
            limit: 10,
            minScore: 0.6f,
            includeMinScore: false);

        Assert.Equal(new[] { "above" }, ranked.Select(item => item.Item.Name));
    }

    [Fact]
    public void TextScorer_MatchesTextScoreAndReusesFrozenQueryTerms()
    {
        var scorer = SearchUtilities.CreateTextScorer("Alpha alpha beta");
        var text = "alpha beta beta gamma";

        Assert.Equal(SearchUtilities.TextScore("Alpha alpha beta", text), scorer.Score(text));
        Assert.Equal(5f / 6f, scorer.Score(text), precision: 6);
        Assert.Equal(0, SearchUtilities.CreateTextScorer("   ").Score(text));
        Assert.Equal(0, scorer.Score("   "));
    }

    [Fact]
    public void TextScorer_RepeatedMatchesKeepCurrentFormula()
    {
        var scorer = SearchUtilities.CreateTextScorer("alpha beta");

        var score = scorer.Score("alpha alpha gamma");

        Assert.Equal(3f / 5f, score, precision: 6);
    }

    [Fact]
    public void TextScorer_CollapsesDuplicateUnicodeQueryTermsInvariantly()
    {
        var scorer = SearchUtilities.CreateTextScorer("CAFÉ café ΩMEGA");

        var score = scorer.Score("café ωmega ωmega other");

        Assert.Equal(5f / 6f, score, precision: 6);
    }

    [Fact]
    public void Bm25TextScorer_RanksCorpusTermsWithLengthNormalization()
    {
        var documents = new[]
        {
            new TextCandidate("both-terms-short", "alpha beta"),
            new TextCandidate("alpha-only-long", "alpha alpha alpha alpha alpha"),
            new TextCandidate("no-match", "gamma delta")
        };

        var ranked = Bm25TextScorer.Rank(
            documents,
            document => document.Text,
            "alpha beta",
            limit: 10);

        Assert.Equal(new[] { "both-terms-short", "alpha-only-long" }, ranked.Select(item => item.Item.Name));
        Assert.True(ranked[0].Score > ranked[1].Score);
        Assert.All(ranked, item => Assert.True(item.Score > 0));
    }

    [Fact]
    public void Bm25TextScorer_CountsRepeatedQueryTermsOnceAndDocumentTermsByFrequency()
    {
        var documents = new[]
        {
            new TextCandidate("repeated", "alpha alpha alpha"),
            new TextCandidate("single", "alpha")
        };

        var ranked = Bm25TextScorer.Rank(
            documents,
            document => document.Text,
            "alpha",
            limit: 10);
        var repeatedQueryRanked = Bm25TextScorer.Rank(
            documents,
            document => document.Text,
            "alpha alpha alpha",
            limit: 10);

        Assert.Equal(new[] { "repeated", "single" }, ranked.Select(item => item.Item.Name));
        Assert.True(ranked[0].Score > ranked[1].Score);
        Assert.Equal(ranked.Select(item => item.Item.Name), repeatedQueryRanked.Select(item => item.Item.Name));
        Assert.Equal(ranked.Select(item => item.Score), repeatedQueryRanked.Select(item => item.Score));
    }

    [Fact]
    public void Bm25TextScorer_CountsNonQueryTokensForLengthNormalization()
    {
        var documents = new[]
        {
            new TextCandidate("short", "alpha"),
            new TextCandidate("long", "alpha gamma gamma gamma")
        };

        var ranked = Bm25TextScorer.Rank(
            documents,
            document => document.Text,
            "alpha",
            limit: 10);

        Assert.Equal(new[] { "short", "long" }, ranked.Select(item => item.Item.Name));
        Assert.True(ranked[0].Score > ranked[1].Score);
    }

    [Fact]
    public void Bm25TextScorer_HandlesEmptyInputsAndStableTies()
    {
        var tied = new[]
        {
            new TextCandidate("first", "alpha"),
            new TextCandidate("second", "alpha")
        };

        Assert.Empty(Bm25TextScorer.Rank(tied, document => document.Text, "   ", limit: 10));
        Assert.Empty(Bm25TextScorer.Rank(tied, document => document.Text, "alpha", limit: 0));

        var ranked = Bm25TextScorer.Rank(
            tied,
            document => document.Text,
            "alpha",
            limit: 10);

        Assert.Equal(new[] { "first", "second" }, ranked.Select(item => item.Item.Name));
        Assert.Equal(ranked[0].Score, ranked[1].Score);
    }

    [Fact]
    public void Tokenize_EnumeratesUnicodeTermsInvariantly()
    {
        var tokens = SearchUtilities.Tokenize("Café NAÏVE_2 ΩMEGA-42").ToArray();

        Assert.Equal(new[] { "café", "naïve_2", "ωmega", "42" }, tokens);
    }

    [Fact]
    public void DeduplicateByUuid_UsesGraphUuidForNodesAndEdges()
    {
        var nodes = SearchUtilities.DeduplicateByUuid(
            new[]
            {
                new EntityNode { Uuid = "node", Name = "first" },
                new EntityNode { Uuid = "node", Name = "second" },
                new EntityNode { Uuid = "other", Name = "third" }
            });
        var edges = SearchUtilities.DeduplicateByUuid(
            new[]
            {
                new EntityEdge { Uuid = "edge", Fact = "first" },
                new EntityEdge { Uuid = "edge", Fact = "second" },
                new EntityEdge { Uuid = "other", Fact = "third" }
            });

        Assert.Equal(new[] { "first", "third" }, nodes.Select(node => node.Name));
        Assert.Equal(new[] { "first", "third" }, edges.Select(edge => edge.Fact));
    }

    [Fact]
    public void DeduplicateByUuid_UsesEqualityForNonGraphItemsInsteadOfHashCodes()
    {
        var items = new[]
        {
            new HashCollidingItem("first"),
            new HashCollidingItem("second"),
            new HashCollidingItem("first")
        };

        var deduplicated = SearchUtilities.DeduplicateByUuid(items);

        Assert.Equal(new[] { "first", "second" }, deduplicated.Select(item => item.Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FulltextQuery_SkipsBlankLuceneQueriesAsIntentionalHardening(string query)
    {
        var fulltextQuery = SearchUtilities.FulltextQuery(
            query,
            new[] { "tenant" },
            new InMemoryGraphDriver());

        Assert.Equal(string.Empty, fulltextQuery);
    }

    [Fact]
    public void FulltextQuery_PreservesEscapedSpecialCharacterQueries()
    {
        var fulltextQuery = SearchUtilities.FulltextQuery(
            "+ - && || ! ( ) { } [ ] ^ \" ~ * ? : \\ /",
            new[] { "tenant" },
            new InMemoryGraphDriver());

        Assert.Equal(
            "(group_id:\"tenant\") AND (\\+ \\- \\&\\& \\|\\| \\! \\( \\) \\{ \\} \\[ \\] \\^ \\\" \\~ \\* \\? \\: \\\\ \\/)",
            fulltextQuery);
    }

    [Fact]
    public void FulltextQuery_LuceneCountsLiteralSpaceSplitsLikePython()
    {
        var overLimitByEmptyParts = $"alpha{new string(' ', SearchUtilities.MaxQueryLength - 1)}beta";

        var fulltextQuery = SearchUtilities.FulltextQuery(
            overLimitByEmptyParts,
            null,
            new InMemoryGraphDriver());

        Assert.Equal(string.Empty, fulltextQuery);
    }

    [Fact]
    public void FulltextQuery_LucenePreservesQueriesBelowLiteralSpaceLimit()
    {
        var belowLimitByEmptyParts = $"alpha{new string(' ', SearchUtilities.MaxQueryLength - 2)}beta";

        var fulltextQuery = SearchUtilities.FulltextQuery(
            belowLimitByEmptyParts,
            null,
            new InMemoryGraphDriver());

        Assert.Equal($"({belowLimitByEmptyParts})", fulltextQuery);
    }

    [Fact]
    public void FulltextQuery_UsesFalkorRedisSearchSyntax()
    {
        var fulltextQuery = SearchUtilities.FulltextQuery(
            "Alice and Bob: roadmap!",
            new[] { "tenant-a", "tenant-b" },
            GraphProvider.FalkorDb);

        Assert.Equal(
            "(@group_id:\"tenant-a\"|\"tenant-b\") (Alice | Bob | roadmap)",
            fulltextQuery);
    }

    [Fact]
    public void FulltextQuery_FalkorDropsStopwordOnlyQueriesToEmptyClause()
    {
        var fulltextQuery = SearchUtilities.FulltextQuery(
            "and the or",
            groupIds: null,
            provider: GraphProvider.FalkorDb);

        Assert.Equal(" ()", fulltextQuery);
    }

    [Fact]
    public void FulltextQuery_FalkorCountsRedisSearchOperatorsForPythonLimitParity()
    {
        var longQuery = string.Join(" ", Enumerable.Range(0, 65).Select(index => $"term{index}"));

        var fulltextQuery = SearchUtilities.FulltextQuery(
            longQuery,
            groupIds: null,
            provider: GraphProvider.FalkorDb);

        Assert.Equal(string.Empty, fulltextQuery);
    }

    [Fact]
    public void FulltextQuery_ParenthesizesSingleGroupFilter()
    {
        var fulltextQuery = SearchUtilities.FulltextQuery(
            "alpha",
            new[] { "tenant-a" },
            new InMemoryGraphDriver());

        Assert.Equal("(group_id:\"tenant-a\") AND (alpha)", fulltextQuery);
    }

    [Fact]
    public void FulltextQuery_ParenthesizesMultipleGroupFilter()
    {
        var fulltextQuery = SearchUtilities.FulltextQuery(
            "alpha",
            new[] { "tenant-a", "tenant-b" },
            new InMemoryGraphDriver());

        Assert.Equal("(group_id:\"tenant-a\" OR group_id:\"tenant-b\") AND (alpha)", fulltextQuery);
    }

    [Fact]
    public void FulltextQuery_StillValidatesGroupIds()
    {
        Assert.Throws<GroupIdValidationException>(() =>
            SearchUtilities.FulltextQuery(
                "alpha",
                new[] { "tenant:a" },
                new InMemoryGraphDriver()));
    }

    [Fact]
    public void MaximalMarginalRelevanceWithScores_MatchesPythonScoringShape()
    {
        var candidates = new[]
        {
            new VectorCandidate("aligned", new[] { 1f, 0f }),
            new VectorCandidate("mixed", new[] { 0.6f, 0.8f }),
            new VectorCandidate("orthogonal", new[] { 0f, 1f })
        };

        var ranked = SearchUtilities.MaximalMarginalRelevanceWithScores(
            candidates,
            new[] { 1f, 0f },
            candidate => candidate.Vector,
            limit: 10,
            lambda: 0.5f);

        Assert.Equal(new[] { "aligned", "mixed", "orthogonal" }, ranked.Select(item => item.Item.Name));
        Assert.Equal(0.2f, ranked[0].Score, precision: 6);
        Assert.Equal(-0.1f, ranked[1].Score, precision: 6);
        Assert.Equal(-0.4f, ranked[2].Score, precision: 6);
    }

    [Fact]
    public void MaximalMarginalRelevanceWithScores_FiltersByMinimumScoreAndKeepsStableTies()
    {
        var candidates = new[]
        {
            new VectorCandidate("first", new[] { 1f, 0f }),
            new VectorCandidate("second", new[] { 1f, 0f }),
            new VectorCandidate("filtered", new[] { -1f, 0f })
        };

        var ranked = SearchUtilities.MaximalMarginalRelevanceWithScores(
            candidates,
            new[] { 1f, 0f },
            candidate => candidate.Vector,
            limit: 10,
            lambda: 0.5f,
            minScore: 0);

        Assert.Equal(new[] { "first", "second" }, ranked.Select(item => item.Item.Name));
        Assert.All(ranked, item => Assert.Equal(0, item.Score, precision: 6));
    }

    [Fact]
    public void MaximalMarginalRelevanceWithScores_RejectsMismatchedNonEmptyDimensions()
    {
        var candidates = new[]
        {
            new VectorCandidate("valid", new[] { 1f, 0f }),
            new VectorCandidate("mismatch", new[] { 1f, 0f, 0f })
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            SearchUtilities.MaximalMarginalRelevanceWithScores(
                candidates,
                new[] { 1f, 0f },
                candidate => candidate.Vector,
                limit: 10));

        Assert.Contains("dimension 3", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dimension 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MaximalMarginalRelevanceWithScores_BoundedTopKMatchesStableFullSortOracleForLargeInputs()
    {
        const int limit = 17;
        var candidates = Enumerable.Range(0, 180)
            .Select(index => new VectorCandidate(
                $"item-{index}",
                new[]
                {
                    ((index % 11) + 1) / 11f,
                    (((index * 7) % 13) + 1) / 13f,
                    (((index * 17) % 19) + 1) / 19f
                }))
            .ToArray();
        var queryVector = new[] { 1f, 0.25f, 0.5f };

        var expected = MaximalMarginalRelevanceFullSortOracle(
            candidates,
            queryVector,
            limit,
            lambda: 0.35f,
            minScore: -2.0f);
        var actual = SearchUtilities.MaximalMarginalRelevanceWithScores(
            candidates,
            queryVector,
            candidate => candidate.Vector,
            limit,
            lambda: 0.35f,
            minScore: -2.0f);

        Assert.Equal(limit, actual.Count);
        Assert.Equal(expected.Select(item => item.Item.Name), actual.Select(item => item.Item.Name));
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Score, actual[i].Score, precision: 5);
        }
    }

    private sealed record VectorCandidate(string Name, IReadOnlyList<float> Vector);

    private sealed record TextCandidate(string Name, string Text);

    private sealed record ScoredCandidate(string Name, float Score);

    private sealed record HashCollidingItem(string Value)
    {
        public override int GetHashCode() => 1;
    }

    private static List<(string Item, float Score)> ReciprocalRankFusionFullSortOracle(
        IEnumerable<IReadOnlyList<string>> rankedLists,
        int limit,
        float minScore)
    {
        var scores = new Dictionary<string, (string Item, float Score, int Index)>(StringComparer.Ordinal);
        var nextIndex = 0;
        foreach (var rankedList in rankedLists)
        {
            for (var i = 0; i < rankedList.Count; i++)
            {
                var item = rankedList[i];
                var score = (float)(1.0 / (i + 1));
                if (scores.TryGetValue(item, out var existing))
                {
                    scores[item] = (existing.Item, existing.Score + score, existing.Index);
                }
                else
                {
                    scores[item] = (item, score, nextIndex++);
                }
            }
        }

        return scores.Values
            .Where(item => item.Score >= minScore)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .Take(limit)
            .Select(item => (item.Item, item.Score))
            .ToList();
    }

    private static List<(VectorCandidate Item, float Score)> MaximalMarginalRelevanceFullSortOracle(
        IReadOnlyList<VectorCandidate> candidates,
        IReadOnlyList<float> queryVector,
        int limit,
        float lambda,
        float minScore)
    {
        var records = candidates
            .Select((candidate, index) => (
                Item: candidate,
                Vector: GraphitiHelpers.NormalizeL2(candidate.Vector),
                Index: index))
            .ToArray();
        var maxSimilarities = new float[records.Length];
        for (var i = 0; i < records.Length; i++)
        {
            for (var j = 0; j < i; j++)
            {
                var similarity = DotSharedPrefix(records[i].Vector, records[j].Vector);
                if (similarity > maxSimilarities[i])
                {
                    maxSimilarities[i] = similarity;
                }

                if (similarity > maxSimilarities[j])
                {
                    maxSimilarities[j] = similarity;
                }
            }
        }

        return records
            .Select(record =>
            {
                var relevance = DotSharedPrefix(queryVector, record.Vector);
                var score = lambda * relevance + (lambda - 1) * maxSimilarities[record.Index];
                return (record.Item, Score: score, record.Index);
            })
            .Where(item => item.Score >= minScore)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .Take(limit)
            .Select(item => (item.Item, item.Score))
            .ToList();
    }

    private static float DotSharedPrefix(float[] left, float[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        var sum = 0f;
        for (var i = 0; i < length; i++)
        {
            sum += left[i] * right[i];
        }

        return sum;
    }

    private static float DotSharedPrefix(IReadOnlyList<float> left, float[] right)
    {
        var length = Math.Min(left.Count, right.Length);
        var sum = 0f;
        for (var i = 0; i < length; i++)
        {
            sum += left[i] * right[i];
        }

        return sum;
    }
}
