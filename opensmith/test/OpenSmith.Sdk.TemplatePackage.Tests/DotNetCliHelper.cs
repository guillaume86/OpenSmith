using System.Diagnostics;

namespace OpenSmith.Sdk.TemplatePackage.Tests;

public record CliResult(int ExitCode, string StdOut, string StdErr)
{
    public void EnsureSuccess()
    {
        if (ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet exited with code {ExitCode}.\nSTDOUT:\n{StdOut}\nSTDERR:\n{StdErr}");
    }
}

public static class DotNetCliHelper
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    public static async Task<CliResult> RunAsync(
        string args,
        string workingDirectory,
        IDictionary<string, string>? envVars = null,
        TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";

        if (envVars is not null)
        {
            foreach (var (key, value) in envVars)
                psi.Environment[key] = value;
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet process");

        using var cts = new CancellationTokenSource(timeout ?? DefaultTimeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"dotnet {args} timed out after {(timeout ?? DefaultTimeout).TotalSeconds}s");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new CliResult(process.ExitCode, stdout, stderr);
    }

    public static async Task<CliResult> PackAsync(
        string projectDir,
        string outputDir,
        string version,
        IDictionary<string, string>? envVars = null)
    {
        var result = await RunAsync(
            $"pack -o \"{outputDir}\" /p:Version={version} --configuration Release",
            projectDir,
            envVars);
        return result;
    }

    public static async Task<CliResult> BuildAsync(
        string projectDir,
        IDictionary<string, string>? envVars = null)
    {
        return await RunAsync("build", projectDir, envVars);
    }
}
