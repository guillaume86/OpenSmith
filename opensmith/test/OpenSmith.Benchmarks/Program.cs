using BenchmarkDotNet.Running;
using OpenSmith.Benchmarks.Benchmarks;

if (args.Length > 0 && args[0] == "profile-schema")
{
    SchemaProfiler.RunDetailed(args.Length > 1 ? args[1] : null);
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
