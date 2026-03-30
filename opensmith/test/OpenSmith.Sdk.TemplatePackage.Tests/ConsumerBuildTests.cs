using System.Text.Json;

namespace OpenSmith.Sdk.TemplatePackage.Tests;

[Collection("TemplatePackage")]
public class ConsumerBuildTests(TemplatePackageFixture fixture)
{
    private async Task<(string ConsumerDir, string PackageId)> PackAndCreateConsumerAsync(
        string testName,
        Dictionary<string, string> templates)
    {
        var packageId = $"TestPkg.{testName.Replace("-", "")}";
        var testDir = fixture.CreateTestDir(testName);

        // Create and pack the template project
        var templateProjectDir = fixture.CreateTemplateProject(
            testDir, packageId, templates);
        await fixture.PackTemplateProjectAsync(templateProjectDir);

        // Create a consumer project that references the packed template
        var consumerDir = fixture.CreateConsumerProject(
            testDir, "ConsumerApp", packageId, fixture.SdkVersion, fixture.LocalFeedPath);

        // Build the consumer (triggers template copy via MSBuild targets)
        var result = await DotNetCliHelper.BuildAsync(consumerDir, fixture.EnvVars);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Consumer build failed.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");

        return (consumerDir, packageId);
    }

    [Fact]
    public async Task Build_CopiesTemplatesToProjectDirectory()
    {
        var (consumerDir, pkgId) = await PackAndCreateConsumerAsync("consumer-copy", new()
        {
            ["Greeting.cst"] = "<%= \"Hello World\" %>",
            ["Sub/Detail.cst"] = "<%= \"Detail\" %>",
        });

        Assert.True(File.Exists(
            Path.Combine(consumerDir, "Templates", pkgId, "Greeting.cst")));
        Assert.True(File.Exists(
            Path.Combine(consumerDir, "Templates", pkgId, "Sub", "Detail.cst")));

        // Verify content was copied correctly
        var content = File.ReadAllText(
            Path.Combine(consumerDir, "Templates", pkgId, "Greeting.cst"));
        Assert.Equal("<%= \"Hello World\" %>", content);
    }

    [Fact]
    public async Task Build_WritesOpenSmithManifest()
    {
        var (consumerDir, pkgId) = await PackAndCreateConsumerAsync("consumer-manifest", new()
        {
            ["Main.cst"] = "template content",
        });

        var manifestPath = Path.Combine(
            consumerDir, "Templates", pkgId, ".opensmith.json");
        Assert.True(File.Exists(manifestPath));

        var json = File.ReadAllText(manifestPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(pkgId, root.GetProperty("package").GetString());
        Assert.Equal(fixture.SdkVersion, root.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Build_CopiesSampleCsp_WhenPresent()
    {
        var (consumerDir, pkgId) = await PackAndCreateConsumerAsync("consumer-samplecsp", new()
        {
            ["Main.cst"] = "template content",
            ["Sample-Generator.csp"] = """
                <?xml version="1.0" encoding="utf-8"?>
                <codeSmith>
                  <propertySet name="Generate">
                    <property name="Template">Main.cst</property>
                  </propertySet>
                </codeSmith>
                """,
        });

        var sampleCspPath = Path.Combine(consumerDir, "Sample-Generator.csp");
        Assert.True(File.Exists(sampleCspPath));

        var content = File.ReadAllText(sampleCspPath);
        Assert.Contains("propertySet", content);

        // Sample CSP should NOT also appear in the templates folder
        Assert.False(File.Exists(
            Path.Combine(consumerDir, "Templates", pkgId, "Sample-Generator.csp")));
    }

    [Fact]
    public async Task Build_SkipsCopyWhenTemplatesAlreadyExist()
    {
        var testDir = fixture.CreateTestDir("consumer-idempotent");
        var packageId = "TestPkg.Idempotent";

        // Create and pack the template project
        var templateProjectDir = fixture.CreateTemplateProject(testDir, packageId, new()
        {
            ["Main.cst"] = "original content",
        });
        await fixture.PackTemplateProjectAsync(templateProjectDir);

        // Create and build consumer
        var consumerDir = fixture.CreateConsumerProject(
            testDir, "ConsumerApp", packageId, fixture.SdkVersion, fixture.LocalFeedPath);

        var result = await DotNetCliHelper.BuildAsync(consumerDir, fixture.EnvVars);
        result.EnsureSuccess();

        // Modify the copied template
        var templatePath = Path.Combine(
            consumerDir, "Templates", packageId, "Main.cst");
        Assert.True(File.Exists(templatePath));
        File.WriteAllText(templatePath, "modified by user");

        // Build again -- should NOT overwrite because the template dir already exists
        var result2 = await DotNetCliHelper.BuildAsync(consumerDir, fixture.EnvVars);
        result2.EnsureSuccess();

        var content = File.ReadAllText(templatePath);
        Assert.Equal("modified by user", content);
    }
}
