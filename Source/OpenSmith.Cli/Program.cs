namespace OpenSmith.Cli;

public class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: opensmith <path-to-csp-file> [--verbose] [--no-cache]");
            return 1;
        }

        var cspPath = args[0];
        var verbose = args.Contains("--verbose");
        var useCache = !args.Contains("--no-cache");

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
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (verbose)
                Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
