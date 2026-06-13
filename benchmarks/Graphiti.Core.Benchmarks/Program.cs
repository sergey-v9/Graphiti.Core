using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// Entry point for the Graphiti.Core performance harness. Uses <see cref="BenchmarkSwitcher"/> so a
/// subset can be selected with <c>--filter</c>; a short job keeps runs directional rather than
/// publication-grade.
/// </summary>
internal static class Program
{
    private static void Main(string[] args)
    {
        // Short, low-iteration job: directional evidence in seconds-per-case, not minutes.
        var config = DefaultConfig.Instance
            .AddJob(Job.ShortRun
                .WithWarmupCount(3)
                .WithIterationCount(5)
                .WithLaunchCount(1));

        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args, config);
    }
}
