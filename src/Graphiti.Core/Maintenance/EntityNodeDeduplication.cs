using System.Buffers.Text;
using System.Collections.Frozen;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Graphiti.Core.Maintenance;

/// <summary>
/// Result of resolving extracted entity nodes against existing ones: the canonical node list and a map
/// from each originally extracted name to its resolved node.
/// </summary>
internal sealed record EntityNodeResolution(
    List<EntityNode> Nodes,
    IReadOnlyDictionary<string, EntityNode> NodesByExtractedName,
    IReadOnlyDictionary<string, string> UuidMap,
    IReadOnlyList<UuidMapPair> DuplicatePairs);

internal readonly record struct UuidMapPair(string SourceUuid, string TargetUuid);

/// <summary>
/// Deduplicates extracted entity nodes using name normalization, entropy/length heuristics, and fuzzy
/// (MinHash/Jaccard) matching to collapse near-duplicate entities onto a single canonical node before
/// they are persisted.
/// </summary>
internal static partial class EntityNodeDeduplicator
{
    private const double NameEntropyThreshold = 1.5;
    private const int MinNameLength = 6;
    private const int MinTokenCount = 2;
    private const double FuzzyJaccardThreshold = 0.9;
    private const int MinHashPermutations = 32;
    private const int MinHashBandSize = 4;

    public static EntityNodeResolution Resolve(
        IReadOnlyList<EntityNode> extractedNodes,
        IReadOnlyList<EntityNode> existingNodes,
        Func<EntityNode, EntityNode, EntityNode> merge)
    {
        ArgumentNullException.ThrowIfNull(extractedNodes);
        ArgumentNullException.ThrowIfNull(existingNodes);
        ArgumentNullException.ThrowIfNull(merge);

        var collapsed = CollapseExtractedNodes(extractedNodes);
        var indexes = CandidateIndexes.Create(existingNodes);
        var resolvedByExtractedName = new Dictionary<string, EntityNode>(StringComparer.OrdinalIgnoreCase);
        var resolvedByCanonicalUuid = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        var resolvedByUuid = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        var uuidMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicatePairs = new List<UuidMapPair>();

        foreach (var canonical in collapsed.CanonicalProfiles)
        {
            var resolved = ResolveOne(canonical, indexes, merge);
            resolvedByCanonicalUuid[canonical.Node.Uuid] = resolved;
            resolvedByUuid.TryAdd(resolved.Uuid, resolved);
        }

        foreach (var extracted in extractedNodes)
        {
            var canonical = collapsed.CanonicalByOriginalUuid[extracted.Uuid];
            var resolved = resolvedByCanonicalUuid[canonical.Node.Uuid];
            resolvedByExtractedName[extracted.Name] = resolved;
            uuidMap[extracted.Uuid] = resolved.Uuid;
            if (!string.Equals(extracted.Uuid, resolved.Uuid, StringComparison.Ordinal))
            {
                duplicatePairs.Add(new UuidMapPair(extracted.Uuid, resolved.Uuid));
            }
        }

        return new EntityNodeResolution(
            ToNodeList(resolvedByUuid),
            resolvedByExtractedName,
            uuidMap,
            duplicatePairs);
    }

    private static CollapsedExtractedNodes CollapseExtractedNodes(IReadOnlyList<EntityNode> extractedNodes)
    {
        var canonicalByKey = new Dictionary<string, EntityNameProfile>(StringComparer.Ordinal);
        var orderedKeys = new List<string>();
        var originalProfiles = new List<EntityNameProfile>(extractedNodes.Count);
        foreach (var node in extractedNodes)
        {
            var profile = EntityNameProfile.Create(node);
            originalProfiles.Add(profile);

            var key = profile.NormalizedExact;
            if (!canonicalByKey.TryGetValue(key, out var existing))
            {
                canonicalByKey[key] = profile;
                orderedKeys.Add(key);
                continue;
            }

            if (IsMoreSpecific(profile.Node, existing.Node))
            {
                canonicalByKey[key] = profile;
            }
        }

        var canonicalByOriginalUuid = new Dictionary<string, EntityNameProfile>(StringComparer.Ordinal);
        foreach (var profile in originalProfiles)
        {
            canonicalByOriginalUuid[profile.Node.Uuid] = canonicalByKey[profile.NormalizedExact];
        }

        return new CollapsedExtractedNodes(
            BuildCanonicalProfiles(orderedKeys, canonicalByKey),
            canonicalByOriginalUuid);
    }

    private static bool IsMoreSpecific(EntityNode candidate, EntityNode existing)
    {
        var candidateSpecificLabels = SpecificLabelCount(candidate.Labels);
        var existingSpecificLabels = SpecificLabelCount(existing.Labels);
        return candidateSpecificLabels > existingSpecificLabels
               || (candidateSpecificLabels == existingSpecificLabels
                   && candidate.Name.Trim().Length > existing.Name.Trim().Length);
    }

    private static int SpecificLabelCount(List<string> labels)
    {
        var count = 0;
        for (var i = 0; i < labels.Count; i++)
        {
            if (!string.Equals(labels[i], "Entity", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static EntityNode ResolveOne(
        EntityNameProfile extracted,
        CandidateIndexes indexes,
        Func<EntityNode, EntityNode, EntityNode> merge)
    {
        if (indexes.NormalizedExisting.TryGetValue(extracted.NormalizedExact, out var exactMatches))
        {
            if (exactMatches.Count == 1)
            {
                return merge(exactMatches[0].Node, extracted.Node);
            }

            return extracted.Node;
        }

        if (!extracted.HasHighEntropy)
        {
            return extracted.Node;
        }

        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        var signature = extracted.Signature;
        for (var i = 0; i < LshBandCount(signature); i++)
        {
            var band = LshBandAt(signature, i);
            if (indexes.LshBuckets.TryGetValue(band, out var bucket))
            {
                foreach (var candidateId in bucket)
                {
                    candidateIds.Add(candidateId);
                }
            }
        }

        EntityNode? bestCandidate = null;
        var bestScore = 0.0;
        foreach (var candidateId in candidateIds)
        {
            if (!indexes.ProfilesByUuid.TryGetValue(candidateId, out var candidate))
            {
                continue;
            }

            var score = JaccardSimilarity(extracted.Shingles, candidate.Shingles);
            if (score > bestScore)
            {
                bestScore = score;
                bestCandidate = candidate.Node;
            }
        }

        return bestCandidate is not null && bestScore >= FuzzyJaccardThreshold
            ? merge(bestCandidate, extracted.Node)
            : extracted.Node;
    }

    private static string NormalizeExact(string name) => GraphitiHelpers.NormalizeEntityKey(name);

    private static string NormalizeFuzzy(string name)
    {
        var normalized = NonFuzzyNameCharactersRegex().Replace(NormalizeExact(name), " ").Trim();
        return WhitespaceRegex().Replace(normalized, " ");
    }

    private static bool HasHighEntropy(string normalizedName)
    {
        var tokenCount = CountWhitespaceSeparatedTerms(normalizedName);
        if (normalizedName.Length < MinNameLength && tokenCount < MinTokenCount)
        {
            return false;
        }

        return NameEntropy(normalizedName) >= NameEntropyThreshold;
    }

    private static int CountWhitespaceSeparatedTerms(string normalizedName)
    {
        var count = 0;
        var inTerm = false;
        foreach (var character in normalizedName)
        {
            if (char.IsWhiteSpace(character))
            {
                inTerm = false;
                continue;
            }

            if (!inTerm)
            {
                count++;
                inTerm = true;
            }
        }

        return count;
    }

    private static double NameEntropy(string normalizedName)
    {
        var counts = new Dictionary<char, int>();
        var total = 0;
        foreach (var character in normalizedName)
        {
            if (character == ' ')
            {
                continue;
            }

            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, character, out _);
            count++;
            total++;
        }

        if (total == 0)
        {
            return 0;
        }

        var entropy = 0.0;
        foreach (var count in counts.Values)
        {
            var probability = count / (double)total;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    private static FrozenSet<string> Shingles(string normalizedName)
    {
        var cleaned = normalizedName.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (cleaned.Length < 2)
        {
            return cleaned.Length == 0
                ? FrozenSet<string>.Empty
                : new[] { cleaned }.ToFrozenSet(StringComparer.Ordinal);
        }

        var shingles = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < cleaned.Length - 2; i++)
        {
            shingles.Add(cleaned.Substring(i, 3));
        }

        return shingles.ToFrozenSet(StringComparer.Ordinal);
    }

    private static ulong[] MinHashSignature(FrozenSet<string> shingles)
    {
        if (shingles.Count == 0)
        {
            return Array.Empty<ulong>();
        }

        var signature = new ulong[MinHashPermutations];
        Array.Fill(signature, ulong.MaxValue);
        foreach (var shingle in shingles)
        {
            UpdateSignatureWithShingle(signature, shingle);
        }

        return signature;
    }

    private static int LshBandCount(ulong[] signature) => signature.Length / MinHashBandSize;

    private static BandKey LshBandAt(ulong[] signature, int bandIndex)
    {
        var start = bandIndex * MinHashBandSize;
        return new BandKey(
            bandIndex,
            signature[start],
            signature[start + 1],
            signature[start + 2],
            signature[start + 3]);
    }

    private static double JaccardSimilarity(FrozenSet<string> left, FrozenSet<string> right)
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return 1;
        }

        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var intersection = 0;
        foreach (var item in left)
        {
            if (right.Contains(item))
            {
                intersection++;
            }
        }

        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : intersection / (double)union;
    }

    private static void UpdateSignatureWithShingle(ulong[] signature, string shingle)
    {
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(shingle.Length);
        Span<byte> shingleBuffer = maxByteCount <= 256 ? stackalloc byte[maxByteCount] : new byte[maxByteCount];
        var byteCount = Encoding.UTF8.GetBytes(shingle, shingleBuffer);
        var shingleBytes = shingleBuffer[..byteCount];
        for (var seed = 0; seed < MinHashPermutations; seed++)
        {
            signature[seed] = Math.Min(signature[seed], HashShingle(shingleBytes, seed));
        }
    }

    private static ulong HashShingle(ReadOnlySpan<byte> shingle, int seed)
    {
        var bufferLength = shingle.Length + 12;
        Span<byte> buffer = bufferLength <= 256 ? stackalloc byte[bufferLength] : new byte[bufferLength];
        if (!Utf8Formatter.TryFormat(seed, buffer, out var written))
        {
            throw new InvalidOperationException("Could not format MinHash seed.");
        }

        buffer[written++] = (byte)':';
        shingle.CopyTo(buffer[written..]);
        written += shingle.Length;
        return XxHash64.HashToUInt64(buffer[..written]);
    }

    private sealed class EntityNameProfile
    {
        private FrozenSet<string>? shingles;
        private ulong[]? signature;

        private EntityNameProfile(EntityNode node, string normalizedExact, string normalizedFuzzy)
        {
            Node = node;
            NormalizedExact = normalizedExact;
            NormalizedFuzzy = normalizedFuzzy;
            HasHighEntropy = EntityNodeDeduplicator.HasHighEntropy(normalizedFuzzy);
        }

        public EntityNode Node { get; }

        public string NormalizedExact { get; }

        public string NormalizedFuzzy { get; }

        public bool HasHighEntropy { get; }

        public FrozenSet<string> Shingles =>
            shingles ??= EntityNodeDeduplicator.Shingles(NormalizedFuzzy);

        public ulong[] Signature => signature ??= EntityNodeDeduplicator.MinHashSignature(Shingles);

        public static EntityNameProfile Create(EntityNode node) =>
            new(
                node,
                EntityNodeDeduplicator.NormalizeExact(node.Name),
                EntityNodeDeduplicator.NormalizeFuzzy(node.Name));
    }

    private sealed record CandidateIndexes(
        IReadOnlyDictionary<string, IReadOnlyList<EntityNameProfile>> NormalizedExisting,
        IReadOnlyDictionary<string, EntityNameProfile> ProfilesByUuid,
        IReadOnlyDictionary<BandKey, IReadOnlyList<string>> LshBuckets)
    {
        public static CandidateIndexes Create(IReadOnlyList<EntityNode> existingNodes)
        {
            var normalizedExisting = new Dictionary<string, List<EntityNameProfile>>(StringComparer.Ordinal);
            var profilesByUuid = new Dictionary<string, EntityNameProfile>(StringComparer.Ordinal);
            var lshBuckets = new Dictionary<BandKey, List<string>>();

            foreach (var candidate in existingNodes)
            {
                var profile = EntityNameProfile.Create(candidate);
                if (!normalizedExisting.TryGetValue(profile.NormalizedExact, out var exactMatches))
                {
                    exactMatches = new List<EntityNameProfile>();
                    normalizedExisting[profile.NormalizedExact] = exactMatches;
                }

                exactMatches.Add(profile);
                profilesByUuid[candidate.Uuid] = profile;

                var signature = profile.Signature;
                for (var i = 0; i < LshBandCount(signature); i++)
                {
                    var band = LshBandAt(signature, i);
                    if (!lshBuckets.TryGetValue(band, out var bucket))
                    {
                        bucket = new List<string>();
                        lshBuckets[band] = bucket;
                    }

                    bucket.Add(candidate.Uuid);
                }
            }

            return new CandidateIndexes(
                normalizedExisting.ToFrozenDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<EntityNameProfile>)CopyBucket(pair.Value),
                    StringComparer.Ordinal),
                profilesByUuid.ToFrozenDictionary(StringComparer.Ordinal),
                lshBuckets.ToFrozenDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)CopyBucket(pair.Value)));
        }

        private static T[] CopyBucket<T>(List<T> bucket)
        {
            if (bucket.Count == 0)
            {
                return Array.Empty<T>();
            }

            var snapshot = new T[bucket.Count];
            for (var i = 0; i < bucket.Count; i++)
            {
                snapshot[i] = bucket[i];
            }

            return snapshot;
        }
    }

    private static List<EntityNode> ToNodeList(Dictionary<string, EntityNode> nodesByUuid)
    {
        var nodes = new List<EntityNode>(nodesByUuid.Count);
        foreach (var node in nodesByUuid.Values)
        {
            nodes.Add(node);
        }

        return nodes;
    }

    private static List<EntityNameProfile> BuildCanonicalProfiles(
        List<string> orderedKeys,
        Dictionary<string, EntityNameProfile> canonicalByKey)
    {
        var canonicalProfiles = new List<EntityNameProfile>(orderedKeys.Count);
        for (var i = 0; i < orderedKeys.Count; i++)
        {
            canonicalProfiles.Add(canonicalByKey[orderedKeys[i]]);
        }

        return canonicalProfiles;
    }

    private readonly record struct BandKey(int Index, ulong First, ulong Second, ulong Third, ulong Fourth);

    private sealed record CollapsedExtractedNodes(
        IReadOnlyList<EntityNameProfile> CanonicalProfiles,
        IReadOnlyDictionary<string, EntityNameProfile> CanonicalByOriginalUuid);

    [GeneratedRegex("[^a-z0-9' ]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonFuzzyNameCharactersRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
