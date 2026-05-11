using System.Reflection;
using OpenSmith.Compilation;

namespace OpenSmith.Cli;

public class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            var version = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown";
            Console.WriteLine(version);
            return 0;
        }

        var verbose = args.Contains("--verbose");
        var useCache = !args.Contains("--no-cache");
        var clearCache = args.Contains("--clear-cache");

        var knownFlags = new[] { "--verbose", "--no-cache", "--clear-cache", "--version" };
        var cspPath = args.FirstOrDefault(a => !knownFlags.Contains(a));

        if (clearCache)
        {
            Console.WriteLine("Clearing compilation cache...");
            TemplateCompilationCache.ClearCache();
            Console.WriteLine("Cache cleared.");
            if (cspPath == null)
                return 0;
        }

        if (cspPath == null)
        {
            Console.Error.WriteLine("Usage: opensmith <path-to-csp-file> [--verbose] [--no-cache] [--clear-cache] [--version]");
            return 1;
        }

        if (!File.Exists(cspPath))
        {
            Console.Error.WriteLine($"File not found: {cspPath}");
            return 1;
        }

        try
        {
            var runner = new CspRunner(verbose, useCache);
            runner.Run(cspPath);
            return 0;
        }
        catch (TemplateCompilationException ex)
        {
            Console.Error.WriteLine("Template compilation failed:");
            foreach (var error in ex.Errors)
                Console.Error.WriteLine($"  {error}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex}");
            return 1;
        }
    }
}
