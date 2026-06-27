using BenchmarkDotNet.Attributes;
using Graphiti.Core.LlmClients;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// LLM request-preparation text cleanup. The clean-input path runs for every prepared prompt message;
/// dirty inputs still need the full rune-preserving cleanup path.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("LLM")]
public class LlmClientBenchmarks
{
    private string _cleanAscii = null!;
    private string _cleanUnicode = null!;
    private string _dirtyControls = null!;

    [GlobalSetup]
    public void Setup()
    {
        _cleanAscii = BenchmarkData.CreateDocument(approximateWords: 250, seed: 81);
        _cleanUnicode = string.Concat(
            BenchmarkData.CreateDocument(approximateWords: 180, seed: 82),
            " Привет Graphiti 日本語 résumé 😀 ",
            BenchmarkData.CreateDocument(approximateWords: 40, seed: 83));
        _dirtyControls =
            "\u200B\u200C" +
            BenchmarkData.CreateDocument(approximateWords: 160, seed: 84) +
            "\0\b\u001Fkeep\n\r\t\ud800x\udc00😀\u2060";
    }

    [Benchmark]
    public string CleanInput_CleanAscii() => LlmClientAccessor.Clean(_cleanAscii);

    [Benchmark]
    public string CleanInput_CleanUnicode() => LlmClientAccessor.Clean(_cleanUnicode);

    [Benchmark]
    public string CleanInput_DirtyControls() => LlmClientAccessor.Clean(_dirtyControls);

    private abstract class LlmClientAccessor : LlmClient
    {
        protected LlmClientAccessor()
            : base(config: null, cache: (ILlmResponseCache?)null)
        {
        }

        public static string Clean(string input) => CleanInput(input);
    }
}
