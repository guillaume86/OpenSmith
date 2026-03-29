namespace OpenSmith.Plinqo.Tests;

internal static class TestContext
{
    public static string PlinqoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "OpenSmith.Plinqo.slnx")))
                dir = Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException("Could not find plinqo root");
        }
    }

    /// <summary>
    /// For backward compat with IntegrationTests that reference RepoRoot.
    /// </summary>
    public static string RepoRoot => PlinqoRoot;
}
