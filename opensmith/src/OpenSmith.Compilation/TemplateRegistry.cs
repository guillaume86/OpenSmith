using OpenSmith.Engine;

namespace OpenSmith.Compilation;

/// <summary>
/// Resolves the full dependency graph of templates by following Register directives recursively.
/// </summary>
public class TemplateRegistry
{
    public Dictionary<string, TemplateEntry> Entries { get; } = new();

    /// <summary>
    /// Resolves all templates reachable from the given root template file.
    /// Returns the registry with the root entry keyed by its sanitized class name,
    /// and all registered sub-templates keyed by their Register Name.
    /// </summary>
    public string Resolve(string templatePath)
    {
        var absolutePath = Path.GetFullPath(templatePath);
        var content = File.ReadAllText(absolutePath);
        var parsed = CstParser.Parse(content);
        var rootClassName = TemplateCodeGenerator.SanitizeClassName(absolutePath);

        Entries[rootClassName] = new TemplateEntry
        {
            ClassName = rootClassName,
            AbsolutePath = absolutePath,
            Parsed = parsed,
        };

        ResolveRegisters(parsed, Path.GetDirectoryName(absolutePath)!);

        return rootClassName;
    }

    private void ResolveRegisters(ParsedTemplate template, string baseDir)
    {
        foreach (var reg in template.Registers)
        {
            if (Entries.ContainsKey(reg.Name))
                continue;

            var normalizedTemplate = reg.Template.Replace('\\', Path.DirectorySeparatorChar);
            var templatePath = Path.GetFullPath(Path.Combine(baseDir, normalizedTemplate));
            if (!File.Exists(templatePath))
                continue;

            var content = File.ReadAllText(templatePath);
            var parsed = CstParser.Parse(content);

            Entries[reg.Name] = new TemplateEntry
            {
                ClassName = reg.Name,
                AbsolutePath = templatePath,
                Parsed = parsed,
            };

            ResolveRegisters(parsed, Path.GetDirectoryName(templatePath)!);
        }
    }
}

public class TemplateEntry
{
    public string ClassName { get; set; } = "";
    public string AbsolutePath { get; set; } = "";
    public ParsedTemplate Parsed { get; set; } = null!;
}
