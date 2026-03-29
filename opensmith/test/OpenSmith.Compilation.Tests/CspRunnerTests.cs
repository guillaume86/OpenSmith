using OpenSmith.Compilation;
using OpenSmith.Engine;

namespace OpenSmith.Cli.Tests;

public class CspRunnerTests
{
    [Fact]
    public void LogsWarningForRegisteredReferences()
    {
        // Arrange
        var template = new FakeTemplateWithReferences();
        var logs = new List<string>();

        // Act
        CspRunner.LogPostExecution(template, verbose: false, log: msg => logs.Add(msg));

        // Assert
        Assert.Contains(logs, l => l.Contains("WARNING") && l.Contains("System.Data.Linq"));
        Assert.Contains(logs, l => l.Contains("WARNING") && l.Contains("System.Configuration"));
    }

    [Fact]
    public void LogsRegisteredOutputsInVerboseMode()
    {
        // Arrange
        var template = new FakeTemplateWithOutputs();
        var logs = new List<string>();

        // Act
        CspRunner.LogPostExecution(template, verbose: true, log: msg => logs.Add(msg));

        // Assert
        Assert.Contains(logs, l => l.Contains("Generated/MyDatabase.dbml"));
        Assert.Contains(logs, l => l.Contains("Generated/Enums.xml"));
    }

    [Fact]
    public void DoesNotLogOutputsWhenNotVerbose()
    {
        // Arrange
        var template = new FakeTemplateWithOutputs();
        var logs = new List<string>();

        // Act
        CspRunner.LogPostExecution(template, verbose: false, log: msg => logs.Add(msg));

        // Assert
        Assert.DoesNotContain(logs, l => l.Contains("MyDatabase.dbml"));
    }

    [Fact]
    public void NoLogsWhenNothingRegistered()
    {
        // Arrange
        var template = new EmptyTemplate();
        var logs = new List<string>();

        // Act
        CspRunner.LogPostExecution(template, verbose: true, log: msg => logs.Add(msg));

        // Assert
        Assert.Empty(logs);
    }

    [Fact]
    public void LogsOutputParentFileWhenPresent()
    {
        // Arrange
        var template = new FakeTemplateWithOutputs();
        var logs = new List<string>();

        // Act
        CspRunner.LogPostExecution(template, verbose: true, log: msg => logs.Add(msg));

        // Assert: Enums.xml has parent Generated/MyDatabase.dbml
        Assert.Contains(logs, l => l.Contains("Enums.xml") && l.Contains("MyDatabase.dbml"));
    }

    // --- Test doubles ---

    private class FakeTemplateWithReferences : CodeTemplateBase
    {
        public FakeTemplateWithReferences()
        {
            RegisterReference("System.Data.Linq");
            RegisterReference("System.Configuration");
        }
    }

    private class FakeTemplateWithOutputs : CodeTemplateBase
    {
        public FakeTemplateWithOutputs()
        {
            RegisterOutput(new OutputFile("Generated/MyDatabase.dbml"));
            RegisterOutput(new OutputFile("Generated/Enums.xml", "Generated/MyDatabase.dbml"));
        }
    }

    private class EmptyTemplate : CodeTemplateBase { }
}
