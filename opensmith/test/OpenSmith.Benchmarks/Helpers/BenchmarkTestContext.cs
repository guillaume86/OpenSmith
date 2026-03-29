namespace OpenSmith.Benchmarks.Helpers;

internal static class BenchmarkTestContext
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string GetTemplatePath(params string[] parts) =>
        Path.Combine([RepoRoot, "plinqo", "src", "OpenSmith.Plinqo", "Templates", .. parts]);

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "OpenSmith.All.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException(
            "Could not find repo root (looked for OpenSmith.All.slnx)");
    }
}
