using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using BenchmarkDotNet.Attributes;
using Graphiti.Core.Embedding;
using Graphiti.Core.LlmClients;
using Graphiti.Core.Telemetry;
using Microsoft.Extensions.AI;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// Latency-injecting fake chat-provider measurements for LLM cache and rate-limited concurrency.
/// These scenarios drive the production Microsoft.Extensions.AI adapter without real API keys.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("ProviderThroughput", "LLM")]
public class LlmProviderThroughputBenchmarks : IDisposable
{
    private const int RequestCount = 16;
    private static readonly IReadOnlyList<Message> CachedMessages =
    [
        new("system", "Return the provided JSON object."),
        new("user", "same prompt")
    ];

    private readonly IReadOnlyList<Message>[] _distinctMessages = new IReadOnlyList<Message>[RequestCount];
    private LatencyChatClient _cachedChat = null!;
    private LatencyChatClient _rateLimitedChat = null!;
    private MemoryLlmResponseCache _cache = null!;
    private MicrosoftExtensionsAIChatClient _cachedLlm = null!;
    private MicrosoftExtensionsAIChatClient _rateLimitedLlm = null!;
    private ConcurrencyLimiter _rateLimiter = null!;
    private GraphitiMetricCollector _metrics = null!;

    [Params(25)]
    public int ProviderLatencyMs { get; set; }

    [Params(4)]
    public int ProviderConcurrencyLimit { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _cachedChat = new LatencyChatClient(TimeSpan.FromMilliseconds(ProviderLatencyMs));
        _rateLimitedChat = new LatencyChatClient(TimeSpan.FromMilliseconds(ProviderLatencyMs));
        _cache = new MemoryLlmResponseCache();
        _rateLimiter = CreateLimiter(ProviderConcurrencyLimit);
        _metrics = new GraphitiMetricCollector();
        _cachedLlm = new MicrosoftExtensionsAIChatClient(_cachedChat, cache: _cache);
        _rateLimitedLlm = new MicrosoftExtensionsAIChatClient(
            _rateLimitedChat,
            cache: null,
            rateLimiter: _rateLimiter);

        for (var i = 0; i < _distinctMessages.Length; i++)
        {
            _distinctMessages[i] =
            [
                new("system", "Return the provided JSON object."),
                new("user", $"distinct prompt {i:D2}")
            ];
        }
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        _cachedLlm?.Dispose();
        _rateLimitedLlm?.Dispose();
        _cachedChat?.Dispose();
        _rateLimitedChat?.Dispose();
        _cache?.Dispose();
        _rateLimiter?.Dispose();
        _metrics?.Dispose();
        GC.SuppressFinalize(this);
    }

    [IterationSetup(Target = nameof(LlmCacheHits_SamePrompt))]
    public void WarmLlmCache()
    {
        _cachedLlm.GenerateResponseAsync(
                CachedMessages,
                responseModel: typeof(CacheProbeResponse),
                promptName: "benchmark.cache_probe")
            .GetAwaiter()
            .GetResult();
        _cachedChat.ResetCounters();
        _metrics.Reset();
    }

    [IterationSetup(Target = nameof(LlmRateLimitedMisses_DistinctPrompts))]
    public void ResetRateLimitedLlmCounters()
    {
        _rateLimitedChat.ResetCounters();
        _metrics.Reset();
    }

    [Benchmark]
    public async Task<int> LlmCacheHits_SamePrompt()
    {
        var propertyCount = 0;
        for (var i = 0; i < RequestCount; i++)
        {
            var response = await _cachedLlm
                .GenerateResponseAsync(
                    CachedMessages,
                    responseModel: typeof(CacheProbeResponse),
                    promptName: "benchmark.cache_probe")
                .ConfigureAwait(false);
            propertyCount += response.Count;
        }

        var snapshot = _metrics.GetSnapshot();
        if (_cachedChat.ProviderCallCount != 0
            || snapshot.CacheHits != RequestCount
            || snapshot.CacheMisses != 0)
        {
            throw new InvalidOperationException(
                $"Expected {RequestCount} cache hits and zero live calls; observed hits={snapshot.CacheHits}, " +
                $"misses={snapshot.CacheMisses}, calls={_cachedChat.ProviderCallCount}.");
        }

        return propertyCount + (int)snapshot.CacheHits;
    }

    [Benchmark]
    public async Task<int> LlmRateLimitedMisses_DistinctPrompts()
    {
        var propertyCount = 0;
        var tasks = new Task<JsonObject>[_distinctMessages.Length];
        for (var i = 0; i < _distinctMessages.Length; i++)
        {
            tasks[i] = _rateLimitedLlm.GenerateResponseAsync(
                _distinctMessages[i],
                responseModel: typeof(CacheProbeResponse),
                promptName: "benchmark.cache_probe");
        }

        var responses = await Task.WhenAll(tasks).ConfigureAwait(false);
        for (var i = 0; i < responses.Length; i++)
        {
            propertyCount += responses[i].Count;
        }

        var snapshot = _metrics.GetSnapshot();
        if (snapshot.InputTokens != RequestCount * 11L
            || snapshot.OutputTokens != RequestCount * 7L)
        {
            throw new InvalidOperationException(
                $"Expected token metrics input={RequestCount * 11}, output={RequestCount * 7}; " +
                $"observed input={snapshot.InputTokens}, output={snapshot.OutputTokens}.");
        }

        return propertyCount + _rateLimitedChat.MaxConcurrentCalls;
    }

    private static ConcurrencyLimiter CreateLimiter(int permitLimit) =>
        new(new ConcurrencyLimiterOptions
        {
            PermitLimit = permitLimit,
            QueueLimit = 1_000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

    private sealed class LatencyChatClient : IChatClient
    {
        private readonly TimeSpan _latency;
        private int _activeCalls;
        private int _providerCallCount;
        private int _maxConcurrentCalls;

        public LatencyChatClient(TimeSpan latency) => _latency = latency;

        public int ProviderCallCount => Volatile.Read(ref _providerCallCount);

        public int MaxConcurrentCalls => Volatile.Read(ref _maxConcurrentCalls);

        public void ResetCounters()
        {
            Volatile.Write(ref _activeCalls, 0);
            Volatile.Write(ref _providerCallCount, 0);
            Volatile.Write(ref _maxConcurrentCalls, 0);
        }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            var active = Interlocked.Increment(ref _activeCalls);
            Interlocked.Increment(ref _providerCallCount);
            UpdateMaxConcurrent(active);
            try
            {
                await Task.Delay(_latency, cancellationToken).ConfigureAwait(false);
                return new ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, "{\"ok\":true}"))
                {
                    Usage = new UsageDetails
                    {
                        InputTokenCount = 11,
                        OutputTokenCount = 7
                    }
                };
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            await Task.CompletedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private void UpdateMaxConcurrent(int active)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maxConcurrentCalls);
                if (active <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrentCalls, active, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class CacheProbeResponse
    {
        public bool Ok { get; set; }
    }

    private sealed class GraphitiMetricCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private long _cacheHits;
        private long _cacheMisses;
        private long _inputTokens;
        private long _outputTokens;

        public GraphitiMetricCollector()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == GraphitiTelemetry.MeterName
                    && instrument.Name is "graphiti.llm.cache.lookups" or "graphiti.llm.tokens")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(RecordMeasurement);
            _listener.Start();
        }

        public void Reset()
        {
            Volatile.Write(ref _cacheHits, 0);
            Volatile.Write(ref _cacheMisses, 0);
            Volatile.Write(ref _inputTokens, 0);
            Volatile.Write(ref _outputTokens, 0);
        }

        public MetricSnapshot GetSnapshot() =>
            new(
                Volatile.Read(ref _cacheHits),
                Volatile.Read(ref _cacheMisses),
                Volatile.Read(ref _inputTokens),
                Volatile.Read(ref _outputTokens));

        public void Dispose() => _listener.Dispose();

        private void RecordMeasurement(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            _ = state;
            if (instrument.Name == "graphiti.llm.cache.lookups")
            {
                if (HasTag(tags, "graphiti.cache.outcome", "hit"))
                {
                    Interlocked.Add(ref _cacheHits, measurement);
                }
                else if (HasTag(tags, "graphiti.cache.outcome", "miss"))
                {
                    Interlocked.Add(ref _cacheMisses, measurement);
                }
            }
            else if (instrument.Name == "graphiti.llm.tokens")
            {
                if (HasTag(tags, "graphiti.token.type", "input"))
                {
                    Interlocked.Add(ref _inputTokens, measurement);
                }
                else if (HasTag(tags, "graphiti.token.type", "output"))
                {
                    Interlocked.Add(ref _outputTokens, measurement);
                }
            }
        }

        private static bool HasTag(
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            string name,
            string value)
        {
            for (var i = 0; i < tags.Length; i++)
            {
                var tag = tags[i];
                if (string.Equals(tag.Key, name, StringComparison.Ordinal)
                    && string.Equals(tag.Value as string, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private readonly record struct MetricSnapshot(
        long CacheHits,
        long CacheMisses,
        long InputTokens,
        long OutputTokens);
}

/// <summary>
/// Latency-injecting fake embedding-provider measurements for batch sizing and chunk concurrency.
/// This drives the production Microsoft.Extensions.AI embedding adapter and provider rate limiter.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("ProviderThroughput", "Embedding")]
public class EmbeddingProviderThroughputBenchmarks : IDisposable
{
    private const int VectorDimension = 256;
    private const int InputCount = 96;
    private readonly string[] _embeddingInputs = new string[InputCount];
    private LatencyEmbeddingGenerator _embeddingGenerator = null!;
    private MicrosoftExtensionsAIEmbedderClient _embedder = null!;
    private ConcurrencyLimiter _rateLimiter = null!;

    [Params(25)]
    public int ProviderLatencyMs { get; set; }

    [Params(8, 32, 128)]
    public int EmbeddingBatchSize { get; set; }

    [Params(4)]
    public int ProviderConcurrencyLimit { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        for (var i = 0; i < _embeddingInputs.Length; i++)
        {
            _embeddingInputs[i] = $"embedding input {i:D3}";
        }

        _embeddingGenerator = new LatencyEmbeddingGenerator(
            VectorDimension,
            TimeSpan.FromMilliseconds(ProviderLatencyMs));
        _rateLimiter = CreateLimiter(ProviderConcurrencyLimit);
        _embedder = new MicrosoftExtensionsAIEmbedderClient(
            _embeddingGenerator,
            VectorDimension,
            modelId: "latency-fake",
            rateLimiter: _rateLimiter,
            batchSize: EmbeddingBatchSize,
            batchConcurrency: ProviderConcurrencyLimit);
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        _embeddingGenerator?.Dispose();
        _rateLimiter?.Dispose();
        GC.SuppressFinalize(this);
    }

    [IterationSetup]
    public void ResetCounters() => _embeddingGenerator.ResetCounters();

    [Benchmark]
    public async Task<int> EmbeddingBatch_LatencyBound()
    {
        var embeddings = await _embedder.CreateBatchAsync(_embeddingInputs).ConfigureAwait(false);
        return embeddings.Count + _embeddingGenerator.MaxConcurrentCalls;
    }

    private static ConcurrencyLimiter CreateLimiter(int permitLimit) =>
        new(new ConcurrencyLimiterOptions
        {
            PermitLimit = permitLimit,
            QueueLimit = 1_000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

    private sealed class LatencyEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly TimeSpan _latency;
        private readonly float[] _vector;
        private int _activeCalls;
        private int _maxConcurrentCalls;

        public LatencyEmbeddingGenerator(int dimension, TimeSpan latency)
        {
            _latency = latency;
            _vector = BenchmarkData.CreateUnitVector(dimension, seed: 131_000);
        }

        public int MaxConcurrentCalls => Volatile.Read(ref _maxConcurrentCalls);

        public void ResetCounters()
        {
            Volatile.Write(ref _activeCalls, 0);
            Volatile.Write(ref _maxConcurrentCalls, 0);
        }

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = options;
            var inputCount = values.Count();
            var active = Interlocked.Increment(ref _activeCalls);
            UpdateMaxConcurrent(active);
            try
            {
                await Task.Delay(_latency, cancellationToken).ConfigureAwait(false);
                var embeddings = new Embedding<float>[inputCount];
                for (var i = 0; i < embeddings.Length; i++)
                {
                    embeddings[i] = new Embedding<float>(_vector);
                }

                return new GeneratedEmbeddings<Embedding<float>>(embeddings);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private void UpdateMaxConcurrent(int active)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maxConcurrentCalls);
                if (active <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrentCalls, active, current) == current)
                {
                    return;
                }
            }
        }
    }
}
