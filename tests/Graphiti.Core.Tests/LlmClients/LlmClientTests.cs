using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.Core;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Graphiti.Core.Tests.LlmClients;

public class LlmClientTests
{
    public static TheoryData<Action<LlmConfig>> InvalidDirectConfig =>
        new()
        {
            config => config.Model = "",
            config => config.Model = "   ",
            config => config.SmallModel = "",
            config => config.MaxTokens = 0,
            config => config.MaxTokens = -1,
            config => config.Temperature = -0.1,
            config => config.Temperature = double.NaN,
            config => config.Temperature = double.PositiveInfinity
        };

    [Theory]
    [MemberData(nameof(InvalidDirectConfig))]
    public void Constructor_RejectsInvalidConfig(Action<LlmConfig> configure)
    {
        var config = new LlmConfig();
        configure(config);

        Assert.ThrowsAny<ArgumentException>(() => new CapturingLlmClient(config));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GenerateResponse_RejectsInvalidMaxTokensBeforeCore(int maxTokens)
    {
        var client = new CapturingLlmClient();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.GenerateResponseAsync(
                new[] { new Message("system", "sys"), new Message("user", "hello") },
                maxTokens: maxTokens));

        Assert.Equal(0, client.GenerateCalls);
    }

    [Fact]
    public async Task GenerateResponse_AddsLanguageInstructionAndCleansInput()
    {
        var client = new CapturingLlmClient();

        await client.GenerateResponseAsync(
            new[] { new Message("system", "sys\u200b"), new Message("user", "hello\u0001\n\t\ud800x\udc00😀") });

        Assert.Contains("same language", client.LastMessages[0].Content);
        Assert.DoesNotContain(client.LastMessages[0].Content, ch => ch == '\u200b');
        Assert.Contains('\n', client.LastMessages[1].Content);
        Assert.Contains('\t', client.LastMessages[1].Content);
        Assert.Contains("😀", client.LastMessages[1].Content, StringComparison.Ordinal);
        Assert.DoesNotContain(client.LastMessages[1].Content, ch => ch is '\u0001' or '\ud800' or '\udc00');
    }

    [Fact]
    public async Task GenerateResponse_AddsAttributePreambleOnce()
    {
        var client = new CapturingLlmClient();

        await client.GenerateResponseAsync(
            new[] { new Message("system", "sys"), new Message("user", "hello") },
            attributeExtraction: true);
        await client.GenerateResponseAsync(client.LastMessages, attributeExtraction: true);

        Assert.Single(FindAll(client.LastMessages[0].Content, "<<graphiti.attr_extraction.preamble.v1>>"));
    }

    [Fact]
    public void PrepareMessages_EmbedsStaticResponseSchemaInLastPrompt()
    {
        var prepared = CapturingLlmClient.PrepareTestMessages(
            new[] { new Message("system", "sys"), new Message("user", "extract") },
            typeof(CacheResponseA),
            responseSchema: null,
            groupId: null,
            attributeExtraction: false);

        var prompt = prepared[^1].Content;

        Assert.Contains("Respond with a JSON object in the following format:", prompt, StringComparison.Ordinal);
        Assert.Contains("\"properties\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"ok\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"boolean\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("requested CacheResponseA response model", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareMessages_EmbedsRuntimeResponseSchemaInLastPrompt()
    {
        var prepared = CapturingLlmClient.PrepareTestMessages(
            new[] { new Message("system", "sys"), new Message("user", "extract") },
            responseModel: null,
            responseSchema: BooleanOkSchema("RuntimeOk"),
            groupId: null,
            attributeExtraction: false);

        var prompt = prepared[^1].Content;

        Assert.Contains("Respond with a JSON object in the following format:", prompt, StringComparison.Ordinal);
        Assert.Contains("\"additionalProperties\":false", prompt, StringComparison.Ordinal);
        Assert.Contains("\"ok\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"boolean\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("requested RuntimeOk response model", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateResponse_UsesCacheWhenEnabled()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "graphiti-llm-cache-" + Guid.NewGuid());
        var client = new CapturingLlmClient(cache: true, cacheDirectory: tempDir);

        var first = await client.GenerateResponseAsync(new[] { new Message("system", "sys"), new Message("user", "hello") });
        var second = await client.GenerateResponseAsync(new[] { new Message("system", "sys"), new Message("user", "hello") });

        Assert.Equal(first.ToJsonString(), second.ToJsonString());
        Assert.Equal(1, client.GenerateCalls);
        Assert.True(File.Exists(Path.Combine(tempDir, "cache.db")));
    }

    [Fact]
    public async Task GenerateResponse_PersistsCacheAcrossClientInstances()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "graphiti-llm-cache-" + Guid.NewGuid());
        var messages = new[] { new Message("system", "sys"), new Message("user", "hello") };
        var firstClient = new CapturingLlmClient(cache: true, cacheDirectory: tempDir);

        var first = await firstClient.GenerateResponseAsync(messages);
        var secondClient = new CapturingLlmClient(cache: true, cacheDirectory: tempDir);
        var second = await secondClient.GenerateResponseAsync(messages);

        Assert.Equal(first.ToJsonString(), second.ToJsonString());
        Assert.Equal(1, firstClient.GenerateCalls);
        Assert.Equal(0, secondClient.GenerateCalls);
    }

    [Fact]
    public async Task GenerateResponse_CacheCoalescesConcurrentMisses()
    {
        var client = new CapturingLlmClient(
            cache: true,
            delay: TimeSpan.FromMilliseconds(50));
        var messages = new[] { new Message("system", "sys"), new Message("user", "hello") };

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => client.GenerateResponseAsync(messages)));

        Assert.All(responses, response => Assert.Equal(responses[0].ToJsonString(), response.ToJsonString()));
        Assert.Equal(1, client.GenerateCalls);
    }

    [Fact]
    public async Task GenerateTypedResponse_SharesRawResponseModelCacheKey()
    {
        var client = new CapturingLlmClient(cache: true);
        var messages = new[] { new Message("system", "sys"), new Message("user", "extract") };

        await client.GenerateResponseAsync(
            messages,
            responseModel: typeof(CacheResponseA),
            promptName: "typed");
        var typed = await client.GenerateTypedResponseAsync<CacheResponseA>(
            messages,
            promptName: "typed");

        Assert.True(typed.Ok);
        Assert.Equal(1, client.GenerateCalls);
    }

    [Fact]
    public async Task GenerateTypedResponse_CoercesStringFieldsWhenClientDoesNotValidate()
    {
        var client = new DirectJsonLlmClient(new JsonObject
        {
            ["valid_at"] = "2026-01-01T00:00:00Z",
            ["invalid_at"] = 42
        });

        var typed = await client.GenerateTypedResponseAsync<Graphiti.EdgeTimestampResponse>(
            new[] { new Message("user", "extract timestamps") },
            promptName: "extract_edges.extract_timestamps");

        Assert.Equal("2026-01-01T00:00:00Z", typed.ValidAt);
        Assert.Equal("42", typed.InvalidAt);
        Assert.Equal(typeof(Graphiti.EdgeTimestampResponse), client.ResponseModel);
    }

    [Fact]
    public async Task MemoryLlmResponseCache_GetOrCreateReturnsClonedResponses()
    {
        var cache = new MemoryLlmResponseCache();
        var created = await cache.GetOrCreateAsync(
            "key",
            _ => Task.FromResult(new JsonObject { ["value"] = 1 }));
        created["value"] = 2;

        var cached = await cache.GetOrCreateAsync(
            "key",
            _ => Task.FromResult(new JsonObject { ["value"] = 3 }));

        Assert.Equal(1, cached["value"]?.GetValue<int>());
    }

    [Fact]
    public async Task MemoryLlmResponseCache_CancelledWaitDoesNotCancelSharedFill()
    {
        using var cache = new MemoryLlmResponseCache();

        await CacheCancelledWaitDoesNotCancelSharedFill(cache);
    }

    [Fact]
    public async Task MemoryLlmResponseCache_DisposePreventsReuse()
    {
        var cache = new MemoryLlmResponseCache();
        await cache.SetAsync("key", new JsonObject { ["value"] = 1 });

        cache.Dispose();
        cache.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            cache.GetAsync("key"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            cache.SetAsync("key", new JsonObject { ["value"] = 2 }));
    }

    [Fact]
    public void MemoryLlmResponseCache_DisposeDoesNotDisposeInjectedMemoryCache()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryLlmResponseCache(inner);

        cache.Dispose();

        inner.Set("key", "value");
        Assert.Equal("value", inner.Get<string>("key"));
    }

    [Fact]
    public async Task SqliteLlmResponseCache_CancelledWaitDoesNotCancelSharedFill()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "graphiti-llm-cache-" + Guid.NewGuid());
        using var cache = new SqliteLlmResponseCache(tempDir);

        await CacheCancelledWaitDoesNotCancelSharedFill(cache);
    }

    [Fact]
    public async Task SqliteLlmResponseCache_CoalescesConcurrentMisses()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "graphiti-llm-cache-" + Guid.NewGuid());
        var cache = new SqliteLlmResponseCache(tempDir);
        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;

            async Task<JsonObject> Factory(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref calls);
                Assert.False(cancellationToken.CanBeCanceled);
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return new JsonObject { ["value"] = 1 };
            }

            var waits = Enumerable.Range(0, 16)
                .Select(_ => cache.GetOrCreateAsync("shared-key", Factory))
                .ToArray();

            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult();
            var responses = await Task.WhenAll(waits);

            Assert.Equal(1, calls);
            Assert.All(responses, response => Assert.Equal(1, response["value"]?.GetValue<int>()));
        }
        finally
        {
            cache.Dispose();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SqliteLlmResponseCache_AllowsConcurrentDistinctKeyWrites()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "graphiti-llm-cache-" + Guid.NewGuid());
        var cache = new SqliteLlmResponseCache(tempDir);
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 32)
                .Select(index => cache.SetAsync(
                    $"key-{index}",
                    new JsonObject { ["value"] = index })));

            var values = await Task.WhenAll(Enumerable.Range(0, 32)
                .Select(index => cache.GetAsync($"key-{index}")));

            Assert.Equal(
                Enumerable.Range(0, 32).ToArray(),
                values.Select(value => value!["value"]!.GetValue<int>()).ToArray());
        }
        finally
        {
            cache.Dispose();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HybridCacheLlmResponseCache_CancelledWaitDoesNotCancelSharedFill()
    {
        using var provider = BuildHybridCacheProvider();
        var cache = new HybridCacheLlmResponseCache(provider.GetRequiredService<HybridCache>());

        await CacheCancelledWaitDoesNotCancelSharedFill(cache);
    }

    [Fact]
    public async Task HybridCacheLlmResponseCache_CoalescesConcurrentMisses()
    {
        using var provider = BuildHybridCacheProvider();
        var cache = new HybridCacheLlmResponseCache(provider.GetRequiredService<HybridCache>());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<JsonObject> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Assert.False(cancellationToken.CanBeCanceled);
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return new JsonObject { ["value"] = 1 };
        }

        var waits = Enumerable.Range(0, 16)
            .Select(_ => cache.GetOrCreateAsync("shared-key", Factory))
            .ToArray();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        var responses = await Task.WhenAll(waits);

        Assert.Equal(1, calls);
        Assert.All(responses, response => Assert.Equal(1, response["value"]?.GetValue<int>()));
    }

    [Fact]
    public async Task AddGraphitiCore_UsesHybridCacheWithCancellationIsolatedFill()
    {
        var services = new ServiceCollection();
        services.AddGraphitiCore();
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ILlmResponseCache>();

        Assert.IsType<HybridCacheLlmResponseCache>(cache);
        await CacheCancelledWaitDoesNotCancelSharedFill(cache);
    }

    [Fact]
    public async Task AddGraphitiCore_BindsHybridCacheOptionsFromConfiguration()
    {
        var configuration = new ConfigurationManager
        {
            ["Cache:LlmResponseExpiration"] = "00:00:00.050",
            ["Cache:LlmResponseLocalCacheExpiration"] = "00:00:00.050",
            ["Cache:LlmResponseTags:0"] = "graphiti",
            ["Cache:LlmResponseTags:1"] = "llm-test"
        };
        var services = new ServiceCollection();
        services.AddGraphitiCore(configuration);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GraphitiCacheOptions>>().Value;

        Assert.Equal(TimeSpan.FromMilliseconds(50), options.LlmResponseExpiration);
        Assert.Equal(TimeSpan.FromMilliseconds(50), options.LlmResponseLocalCacheExpiration);
        Assert.Equal(new[] { "graphiti", "llm-test" }, options.LlmResponseTags);
    }

    [Fact]
    public async Task AddGraphitiCore_AppliesConfiguredHybridCacheExpiration()
    {
        var configuration = new ConfigurationManager
        {
            ["Cache:LlmResponseExpiration"] = "00:00:00.050",
            ["Cache:LlmResponseLocalCacheExpiration"] = "00:00:00.050"
        };
        var services = new ServiceCollection();
        services.AddGraphitiCore(configuration);

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ILlmResponseCache>();
        var calls = 0;

        async Task<JsonObject> Factory(CancellationToken cancellationToken)
        {
            await Task.Yield();
            return new JsonObject { ["value"] = Interlocked.Increment(ref calls) };
        }

        var first = await cache.GetOrCreateAsync("expiring-key", Factory);
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        var second = await cache.GetOrCreateAsync("expiring-key", Factory);

        Assert.Equal(1, first["value"]?.GetValue<int>());
        Assert.Equal(2, second["value"]?.GetValue<int>());
    }

    [Fact]
    public async Task SqliteLlmResponseCache_DisposeAllowsCacheDirectoryCleanup()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "graphiti-llm-cache-" + Guid.NewGuid());
        var cache = new SqliteLlmResponseCache(tempDir);
        await cache.SetAsync("key", new JsonObject { ["value"] = 1 });

        cache.Dispose();

        Directory.Delete(tempDir, recursive: true);
        Assert.False(Directory.Exists(tempDir));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            cache.GetAsync("key"));
    }

    [Fact]
    public async Task Dispose_DisposesOwnedSqliteCache()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "graphiti-llm-cache-" + Guid.NewGuid());
        var client = new CapturingLlmClient(cache: true, cacheDirectory: tempDir);
        var messages = new[] { new Message("system", "sys"), new Message("user", "hello") };
        await client.GenerateResponseAsync(messages);

        client.Dispose();

        Directory.Delete(tempDir, recursive: true);
        Assert.False(Directory.Exists(tempDir));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GenerateResponseAsync(messages));
    }

    [Fact]
    public void Dispose_DoesNotDisposeInjectedCache()
    {
        var cache = new TrackingDisposableCache();
        var client = new CapturingLlmClient(cache);

        client.Dispose();
        client.Dispose();

        Assert.False(cache.Disposed);
        cache.Dispose();
        Assert.True(cache.Disposed);
    }

    [Fact]
    public async Task GenerateResponse_CacheKeyIncludesGenerationSettings()
    {
        var client = new CapturingLlmClient(
            new LlmConfig { SmallModel = "gpt-small" },
            cache: true);
        var messages = new[] { new Message("system", "sys"), new Message("user", "hello") };

        await client.GenerateResponseAsync(messages, maxTokens: 100, promptName: "a");
        await client.GenerateResponseAsync(messages, maxTokens: 200, promptName: "a");
        await client.GenerateResponseAsync(messages, maxTokens: 200, promptName: "b");
        await client.GenerateResponseAsync(messages, maxTokens: 200, modelSize: ModelSize.Small, promptName: "b");
        await client.GenerateResponseAsync(messages, responseModel: typeof(CacheResponseA), maxTokens: 200, promptName: "b");
        await client.GenerateResponseAsync(messages, responseModel: typeof(CacheResponseB), maxTokens: 200, promptName: "b");
        await client.GenerateResponseAsync(messages, maxTokens: 100, promptName: "a");

        Assert.Equal(6, client.GenerateCalls);
    }

    [Fact]
    public void GenerateResponse_CacheKeyPayloadUsesSourceGeneratedMetadata()
    {
        var typeInfo = GraphitiJsonSerializerContext.Default.GetTypeInfo(typeof(LlmCacheKeyPayload));
        var payload = new LlmCacheKeyPayload(
            "gpt-main",
            null,
            "gpt-main",
            ModelSize.Medium.ToString(),
            0.5,
            100,
            null,
            null,
            "extract",
            new[] { new Message("user", "hello") });

        var json = JsonSerializer.Serialize(payload, GraphitiJsonSerializer.Options);

        Assert.NotNull(typeInfo);
        Assert.Equal(typeof(LlmCacheKeyPayload), typeInfo.Type);
        Assert.Contains("\"small_model\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"resolved_model\":\"gpt-main\"", json, StringComparison.Ordinal);
        Assert.Contains("\"prompt_name\":\"extract\"", json, StringComparison.Ordinal);
        Assert.Contains("\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredResponseValidator_SchemaFingerprintIsStableAndSchemaSensitive()
    {
        var first = StructuredResponseValidator.GetSchemaFingerprint(typeof(CacheResponseA));
        var second = StructuredResponseValidator.GetSchemaFingerprint(typeof(CacheResponseA));
        var different = StructuredResponseValidator.GetSchemaFingerprint(typeof(CacheResponseWithName));

        Assert.Matches("^[0-9a-f]{64}$", first);
        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
        Assert.Null(StructuredResponseValidator.GetSchemaFingerprint(null));
    }

    [Fact]
    public void StructuredResponseValidator_RuntimeSchemaFingerprintIsStableAndSchemaSensitive()
    {
        var first = BooleanOkSchema("RuntimeOk");
        var second = BooleanOkSchema("RuntimeOk");
        var different = new StructuredResponseSchema(
            "RuntimeName",
            new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray(JsonValue.Create("name")),
                ["properties"] = new JsonObject
                {
                    ["name"] = new JsonObject { ["type"] = "string" }
                }
            });

        Assert.Matches("^[0-9a-f]{64}$", first.Fingerprint);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.NotEqual(first.Fingerprint, different.Fingerprint);
    }

    [Fact]
    public void StructuredResponseValidator_RuntimeSchemaValidatesNestedResponses()
    {
        var schemaObject = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray(JsonValue.Create("items")),
            ["properties"] = new JsonObject
            {
                ["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["required"] = new JsonArray(JsonValue.Create("ok")),
                        ["properties"] = new JsonObject
                        {
                            ["ok"] = new JsonObject { ["type"] = "boolean" }
                        }
                    }
                }
            }
        };
        var schema = new StructuredResponseSchema("NestedRuntime", schemaObject);
        schemaObject["properties"] = new JsonObject();

        StructuredResponseValidator.Validate(
            new JsonObject
            {
                ["items"] = new JsonArray(
                    new JsonObject { ["ok"] = true })
            },
            schema);

        var exception = Assert.Throws<JsonException>(() =>
            StructuredResponseValidator.Validate(
                new JsonObject
                {
                    ["items"] = new JsonArray(
                        new JsonObject { ["ok"] = "yes" })
                },
                schema));
        Assert.Contains("NestedRuntime", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateResponse_CacheKeyIncludesStructuredResponseSchemaFingerprint()
    {
        var client = new CapturingLlmClient(
            new LlmConfig
            {
                Model = "gpt-main",
                SmallModel = "gpt-small",
                Temperature = 0.25
            });
        var messages = new[] { new Message("system", "sys"), new Message("user", "hello") };
        var prepared = CapturingLlmClient.PrepareTestMessages(
            messages,
            typeof(CacheResponseA),
            responseSchema: null,
            groupId: "group",
            attributeExtraction: true);

        var cacheKey = client.GetTestCacheKey(
            prepared,
            typeof(CacheResponseA),
            responseSchema: null,
            maxTokens: 200,
            modelSize: ModelSize.Small,
            promptName: "extract");

        var expectedPayload = JsonSerializer.Serialize(
            new
            {
                Model = "gpt-main",
                SmallModel = "gpt-small",
                ResolvedModel = "gpt-small",
                ModelSize = ModelSize.Small.ToString(),
                Temperature = 0.25,
                MaxTokens = 200,
                ResponseModel = typeof(CacheResponseA).AssemblyQualifiedName,
                ResponseSchemaFingerprint = StructuredResponseValidator.GetSchemaFingerprint(typeof(CacheResponseA)),
                PromptName = "extract",
                Messages = prepared
            },
            GraphitiJsonSerializer.Options);
        var expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(expectedPayload)));

        Assert.Equal(expected, cacheKey);
    }

    [Fact]
    public void GenerateResponse_CacheKeyIncludesRuntimeResponseSchemaFingerprint()
    {
        var schema = BooleanOkSchema("RuntimeOk");
        var client = new CapturingLlmClient(
            new LlmConfig
            {
                Model = "gpt-main",
                SmallModel = "gpt-small",
                Temperature = 0.25
            });
        var messages = new[] { new Message("system", "sys"), new Message("user", "hello") };
        var prepared = CapturingLlmClient.PrepareTestMessages(
            messages,
            responseModel: null,
            responseSchema: schema,
            groupId: "group",
            attributeExtraction: true);

        var cacheKey = client.GetTestCacheKey(
            prepared,
            responseModel: null,
            responseSchema: schema,
            maxTokens: 200,
            modelSize: ModelSize.Small,
            promptName: "extract");

        var expectedPayload = JsonSerializer.Serialize(
            new
            {
                Model = "gpt-main",
                SmallModel = "gpt-small",
                ResolvedModel = "gpt-small",
                ModelSize = ModelSize.Small.ToString(),
                Temperature = 0.25,
                MaxTokens = 200,
                ResponseModel = schema.Name,
                ResponseSchemaFingerprint = schema.Fingerprint,
                PromptName = "extract",
                Messages = prepared
            },
            GraphitiJsonSerializer.Options);
        var expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(expectedPayload)));

        Assert.Equal(expected, cacheKey);
    }

    [Fact]
    public async Task GenerateResponse_ValidatesResponseModelForBaseClients()
    {
        var client = new CapturingLlmClient();

        var response = await client.GenerateResponseAsync(
            new[] { new Message("system", "sys"), new Message("user", "hello") },
            responseModel: typeof(CacheResponseA));

        Assert.True(response["ok"]?.GetValue<bool>());
    }

    [Fact]
    public async Task GenerateResponse_ValidatesRuntimeResponseSchemaForBaseClients()
    {
        var client = new CapturingLlmClient();

        var response = await client.GenerateResponseAsync(
            new[] { new Message("system", "sys"), new Message("user", "hello") },
            responseSchema: BooleanOkSchema("RuntimeOk"));

        Assert.True(response["ok"]?.GetValue<bool>());
    }

    [Fact]
    public async Task GenerateResponse_RejectsInvalidResponseModelForBaseClients()
    {
        var client = new InvalidStructuredLlmClient();

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            client.GenerateResponseAsync(
                new[] { new Message("system", "sys"), new Message("user", "hello") },
                responseModel: typeof(CacheResponseA)));
    }

    [Fact]
    public async Task GenerateResponse_RejectsInvalidRuntimeResponseSchemaForBaseClients()
    {
        var client = new InvalidStructuredLlmClient();

        await Assert.ThrowsAsync<JsonException>(() =>
            client.GenerateResponseAsync(
                new[] { new Message("system", "sys"), new Message("user", "hello") },
                responseSchema: BooleanOkSchema("RuntimeOk")));
    }

    private static IEnumerable<int> FindAll(string text, string value)
    {
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            yield return index;
            index += value.Length;
        }
    }

    private static StructuredResponseSchema BooleanOkSchema(string name) =>
        new(
            name,
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray(JsonValue.Create("ok")),
                ["properties"] = new JsonObject
                {
                    ["ok"] = new JsonObject { ["type"] = "boolean" }
                }
            });

    private static async Task CacheCancelledWaitDoesNotCancelSharedFill(ILlmResponseCache cache)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<JsonObject> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Assert.False(cancellationToken.CanBeCanceled);
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return new JsonObject { ["value"] = 1 };
        }

        using var cancellation = new CancellationTokenSource();
        var firstWait = cache.GetOrCreateAsync("shared-key", Factory, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstWait);

        var secondWait = cache.GetOrCreateAsync("shared-key", Factory);
        release.SetResult();
        var second = await secondWait;
        var cached = await cache.GetOrCreateAsync(
            "shared-key",
            _ => Task.FromResult(new JsonObject { ["value"] = 2 }));

        Assert.Equal(1, calls);
        Assert.Equal(1, second["value"]?.GetValue<int>());
        Assert.Equal(1, cached["value"]?.GetValue<int>());
    }

    private static ServiceProvider BuildHybridCacheProvider()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }

    private sealed class CapturingLlmClient : LlmClient
    {
        public CapturingLlmClient(
            LlmConfig? config = null,
            bool cache = false,
            string? cacheDirectory = null,
            TimeSpan? delay = null)
            : base(
                config,
                cache,
                cacheDirectory ?? Path.Combine(Path.GetTempPath(), "graphiti-llm-cache-" + Guid.NewGuid()))
        {
            _delay = delay;
        }

        public CapturingLlmClient(ILlmResponseCache responseCache, TimeSpan? delay = null)
            : base(config: null, cache: responseCache)
        {
            _delay = delay;
        }

        private readonly TimeSpan? _delay;
        private int _generateCalls;
        public IReadOnlyList<Message> LastMessages { get; private set; } = Array.Empty<Message>();
        public int GenerateCalls => Volatile.Read(ref _generateCalls);

        public string GetTestCacheKey(
            IReadOnlyList<Message> messages,
            Type? responseModel,
            StructuredResponseSchema? responseSchema,
            int maxTokens,
            ModelSize modelSize,
            string? promptName) =>
            GetCacheKey(messages, responseModel, responseSchema, maxTokens, modelSize, promptName);

        public static IReadOnlyList<Message> PrepareTestMessages(
            IReadOnlyList<Message> messages,
            Type? responseModel,
            StructuredResponseSchema? responseSchema,
            string? groupId,
            bool attributeExtraction) =>
            PrepareMessages(messages, responseModel, responseSchema, groupId, attributeExtraction);

        protected override Task<JsonObject> GenerateResponseCoreAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel,
            StructuredResponseSchema? responseSchema,
            int maxTokens,
            ModelSize modelSize,
            string? promptName,
            CancellationToken cancellationToken)
        {
            var generateCalls = Interlocked.Increment(ref _generateCalls);
            LastMessages = messages;
            return GenerateAsync(generateCalls, responseModel, responseSchema, cancellationToken);
        }

        private async Task<JsonObject> GenerateAsync(
            int generateCalls,
            Type? responseModel,
            StructuredResponseSchema? responseSchema,
            CancellationToken cancellationToken)
        {
            if (_delay is not null)
            {
                await Task.Delay(_delay.Value, cancellationToken).ConfigureAwait(false);
            }

            return responseModel is null && responseSchema is null
                ? new JsonObject { ["call"] = generateCalls }
                : new JsonObject { ["ok"] = true };
        }
    }

    private sealed class InvalidStructuredLlmClient : LlmClient
    {
        public InvalidStructuredLlmClient()
            : base(config: null, cache: false)
        {
        }

        protected override Task<JsonObject> GenerateResponseCoreAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel,
            StructuredResponseSchema? responseSchema,
            int maxTokens,
            ModelSize modelSize,
            string? promptName,
            CancellationToken cancellationToken) =>
            Task.FromResult(new JsonObject { ["ok"] = "yes" });
    }

    private sealed class DirectJsonLlmClient(JsonObject response) : ILlmClient
    {
        public TokenUsageTracker TokenTracker { get; } = new();

        public Type? ResponseModel { get; private set; }

        public Task<JsonObject> GenerateResponseAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel = null,
            StructuredResponseSchema? responseSchema = null,
            int? maxTokens = null,
            ModelSize modelSize = ModelSize.Medium,
            string? groupId = null,
            string? promptName = null,
            bool attributeExtraction = false,
            CancellationToken cancellationToken = default)
        {
            ResponseModel = responseModel;
            return Task.FromResult((JsonObject)response.DeepClone());
        }
    }

    private sealed class TrackingDisposableCache : ILlmResponseCache, IDisposable
    {
        public bool Disposed { get; private set; }

        public Task<JsonObject?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            return Task.FromResult<JsonObject?>(null);
        }

        public Task SetAsync(string key, JsonObject value, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class CacheResponseA
    {
        public bool Ok { get; set; }
    }

    private sealed class CacheResponseB
    {
        public bool Ok { get; set; }
    }

    private sealed class CacheResponseWithName
    {
        public string Name { get; set; } = string.Empty;
    }
}
