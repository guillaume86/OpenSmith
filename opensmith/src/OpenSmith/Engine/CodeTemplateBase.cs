using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

namespace OpenSmith.Engine;

/// <summary>
/// Base class for CodeSmith-compatible templates.
/// Provides the core API surface: Create, CopyPropertiesTo, RenderToFile, Progress, Response, etc.
/// </summary>
public abstract class CodeTemplateBase
{
    public ProgressInfo Progress { get; } = new();
    public ResponseWriter Response { get; } = new();
    public DebugWriter Debug { get; } = new();
    public CodeTemplateInfoData CodeTemplateInfo { get; } = new();

    private readonly List<string> _references = new();
    private readonly List<OutputFile> _outputs = new();

    private static int _filesWrittenCount;
    public static int FilesWrittenCount => _filesWrittenCount;
    public static void ResetCounters() => _filesWrittenCount = 0;

    public T Create<T>() where T : CodeTemplateBase, new()
    {
        var template = new T();
        template.CodeTemplateInfo.DirectoryName = CodeTemplateInfo.DirectoryName;
        return template;
    }

    public void CopyPropertiesTo<T>(T target) where T : CodeTemplateBase
    {
        var sourceType = GetType();
        var targetType = target.GetType();

        foreach (var sourceProp in sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!sourceProp.CanRead) continue;
            if (typeof(CodeTemplateBase).GetProperty(sourceProp.Name) != null) continue;

            var targetProp = targetType.GetProperty(sourceProp.Name, BindingFlags.Public | BindingFlags.Instance);
            if (targetProp == null || !targetProp.CanWrite) continue;
            if (!targetProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType)) continue;

            targetProp.SetValue(target, sourceProp.GetValue(this));
        }
    }

    public void RegisterReference(string path)
    {
        _references.Add(path);
    }

    public void RegisterOutput(OutputFile output)
    {
        _outputs.Add(output);
    }

    public IReadOnlyList<string> References => _references;
    public IReadOnlyList<OutputFile> Outputs => _outputs;

    /// <summary>
    /// Called before output is written, allowing subclasses to react.
    /// </summary>
    public virtual void OnPreOutputWritten(string path) { }

    /// <summary>
    /// Renders the template to a string. Subclasses override to produce output.
    /// </summary>
    public virtual string RenderToString() => string.Empty;

    /// <summary>
    /// Renders the template output to a file, overwriting if specified.
    /// </summary>
    public void RenderToFile(string fileName, bool overwrite)
    {
        if (!overwrite && File.Exists(fileName))
            return;

        var dir = Path.GetDirectoryName(fileName);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fileName, RenderToString());
        Interlocked.Increment(ref _filesWrittenCount);
    }

    /// <summary>
    /// Renders the template output to a file with a parent file dependency.
    /// Generated files use this overload; a trailing newline is ensured.
    /// </summary>
    public void RenderToFile(string fileName, string parentFileName, bool overwrite)
    {
        if (!overwrite && File.Exists(fileName))
            return;

        var dir = Path.GetDirectoryName(fileName);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var content = RenderToString();
        if (!content.EndsWith("\n"))
            content += "\r\n";
        File.WriteAllText(fileName, content);
        Interlocked.Increment(ref _filesWrittenCount);
    }

    /// <summary>
    /// Renders the template output to a file using a merge strategy.
    /// </summary>
    public void RenderToFile(string fileName, InsertClassMergeStrategy mergeStrategy)
    {
        var newContent = RenderToString();

        if (!File.Exists(fileName))
        {
            var dir = Path.GetDirectoryName(fileName);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fileName, newContent);
            Interlocked.Increment(ref _filesWrittenCount);
            return;
        }

        var existingContent = File.ReadAllText(fileName);
        var merged = mergeStrategy.Merge(existingContent, newContent);
        File.WriteAllText(fileName, merged);
        Interlocked.Increment(ref _filesWrittenCount);
    }
}

public class ProgressInfo
{
    public int MaximumValue { get; set; }
    public int Step { get; set; } = 1;
    public int Value { get; private set; }

    public void PerformStep()
    {
        Value += Step;
    }
}

public class ResponseWriter
{
    private System.Text.StringBuilder _output;
    public List<string> Lines { get; } = new();

    /// <summary>
    /// Connects this ResponseWriter to a StringBuilder so that Write/WriteLine
    /// output is appended to the template's render output (like CodeSmith's Response).
    /// </summary>
    public void SetOutput(System.Text.StringBuilder sb) => _output = sb;

    public void Write(string text)
    {
        Lines.Add(text);
        _output?.Append(text);
    }

    public void WriteLine(string text)
    {
        Lines.Add(text);
        _output?.AppendLine(text);
    }

    public void WriteLine(string format, params object[] args)
    {
        var formatted = string.Format(format, args);
        Lines.Add(formatted);
        _output?.AppendLine(formatted);
    }
}

public class DebugWriter
{
    public List<string> Lines { get; } = new();

    public void Write(string text) => Lines.Add(text);
    public void WriteLine(string text) => Lines.Add(text);
}

public class CodeTemplateInfoData
{
    public string DirectoryName { get; set; }
}

public class OutputFile
{
    public OutputFile() { }
    public OutputFile(string fileName) { FileName = fileName; }
    public OutputFile(string fileName, string parentFileName) { FileName = fileName; ParentFileName = parentFileName; }

    public string FileName { get; set; }
    public string Content { get; set; }
    public string ParentFileName { get; set; }
    public Dictionary<string, string> Metadata { get; } = new();
}
