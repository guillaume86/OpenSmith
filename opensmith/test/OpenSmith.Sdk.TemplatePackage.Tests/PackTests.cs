namespace OpenSmith.Sdk.TemplatePackage.Tests;

[Collection("TemplatePackage")]
public class PackTests(TemplatePackageFixture fixture)
{
    [Fact]
    public async Task Pack_IncludesTemplateFiles()
    {
        var testDir = fixture.CreateTestDir("pack-templates");
        var projectDir = fixture.CreateTemplateProject(testDir, "TestPkg.Templates", new()
        {
            ["Hello.cst"] = "<%= \"Hello\" %>",
            ["Sub/Nested.cst"] = "<%= \"Nested\" %>",
        });

        var nupkg = await fixture.PackTemplateProjectAsync(projectDir);
        var entries = TemplatePackageFixture.GetNupkgEntries(nupkg);

        Assert.Contains("templates/Hello.cst", entries);
        Assert.Contains("templates/Sub/Nested.cst", entries);
    }

    [Fact]
    public async Task Pack_IncludesGenericConsumerTargets_WhenNoCustomTargets()
    {
        var testDir = fixture.CreateTestDir("pack-generic-targets");
        var projectDir = fixture.CreateTemplateProject(testDir, "TestPkg.Generic", new()
        {
            ["Main.cst"] = "template content",
        });

        var nupkg = await fixture.PackTemplateProjectAsync(projectDir);
        var entries = TemplatePackageFixture.GetNupkgEntries(nupkg);

        // Should contain a .targets file named after the package ID
        Assert.Contains("build/TestPkg.Generic.targets", entries);

        // Content should be the generic consumer targets (contains _OpenSmithCopyTemplates)
        var content = TemplatePackageFixture.ReadNupkgEntry(nupkg, "build/TestPkg.Generic.targets");
        Assert.Contains("_OpenSmithCopyTemplates", content);
        Assert.Contains("_OpenSmithCopySampleCsp", content);
    }

    [Fact]
    public async Task Pack_IncludesCustomTargets_WhenPresent()
    {
        var customTargets = """
            <Project>
              <Target Name="MyCustomTarget" BeforeTargets="BeforeBuild">
                <Message Text="Custom target running!" />
              </Target>
            </Project>
            """;

        var testDir = fixture.CreateTestDir("pack-custom-targets");
        var projectDir = fixture.CreateTemplateProject(testDir, "TestPkg.Custom", new()
        {
            ["Main.cst"] = "template content",
        }, customTargetsContent: customTargets);

        var nupkg = await fixture.PackTemplateProjectAsync(projectDir);
        var entries = TemplatePackageFixture.GetNupkgEntries(nupkg);

        Assert.Contains("build/TestPkg.Custom.targets", entries);

        var content = TemplatePackageFixture.ReadNupkgEntry(nupkg, "build/TestPkg.Custom.targets");
        Assert.Contains("MyCustomTarget", content);
        Assert.DoesNotContain("_OpenSmithCopyTemplates", content);
    }

    [Fact]
    public async Task Pack_IncludesCustomProps_WhenPresent()
    {
        var customProps = """
            <Project>
              <PropertyGroup>
                <MyCustomProp>true</MyCustomProp>
              </PropertyGroup>
            </Project>
            """;

        var testDir = fixture.CreateTestDir("pack-custom-props");
        var projectDir = fixture.CreateTemplateProject(testDir, "TestPkg.Props", new()
        {
            ["Main.cst"] = "template content",
        }, customPropsContent: customProps);

        var nupkg = await fixture.PackTemplateProjectAsync(projectDir);
        var entries = TemplatePackageFixture.GetNupkgEntries(nupkg);

        Assert.Contains("build/TestPkg.Props.props", entries);

        var content = TemplatePackageFixture.ReadNupkgEntry(nupkg, "build/TestPkg.Props.props");
        Assert.Contains("MyCustomProp", content);
    }

    [Fact]
    public async Task Pack_ExcludesTemplatesFromCompilation()
    {
        var testDir = fixture.CreateTestDir("pack-no-compile");
        var projectDir = fixture.CreateTemplateProject(testDir, "TestPkg.NoCmp", new()
        {
            // This would fail C# compilation if included as source
            ["Bad.cst"] = "<%= this is definitely not valid C# code !@#$% %>",
            ["Sub/AlsoBad.cst"] = "<%@ Template Language=\"C#\" %>\n<% foreach (INVALID) { } %>",
        });

        var result = await DotNetCliHelper.PackAsync(
            projectDir, fixture.LocalFeedPath, fixture.SdkVersion, fixture.EnvVars);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Pack_DoesNotBundleNuGetTransitiveDlls()
    {
        var testDir = fixture.CreateTestDir("pack-no-nuget-dlls");

        var projectDir = Path.Combine(testDir, "TestPkg.NuGetDeps");
        Directory.CreateDirectory(projectDir);

        var templatesDir = Path.Combine(projectDir, "Templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "Main.cst"), "template content");

        File.WriteAllText(Path.Combine(projectDir, "TestPkg.NuGetDeps.csproj"),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFramework>netstandard2.0</TargetFramework>
                 <LangVersion>latest</LangVersion>
                 <PackageId>TestPkg.NuGetDeps</PackageId>
                 <Version>{fixture.SdkVersion}</Version>
               </PropertyGroup>
               <ItemGroup>
                 <PackageReference Include="OpenSmith.Sdk.TemplatePackage"
                                   Version="{fixture.SdkVersion}" PrivateAssets="all" />
                 <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
               </ItemGroup>
             </Project>
             """);

        fixture.WriteNuGetConfig(projectDir, "TestPkg.NuGetDeps");

        var nupkg = await fixture.PackTemplateProjectAsync(projectDir);
        var entries = TemplatePackageFixture.GetNupkgEntries(nupkg);

        // NuGet package DLLs should NOT be bundled in lib/
        Assert.DoesNotContain(entries,
            e => e.Contains("Newtonsoft.Json.dll", StringComparison.OrdinalIgnoreCase));

        // But they should appear as nuspec dependencies
        var deps = TemplatePackageFixture.GetNuspecDependencies(nupkg);
        Assert.Contains("Newtonsoft.Json", deps);
    }

    [Fact]
    public async Task Pack_DoesNotBundleNativeRuntimeAssets()
    {
        var testDir = fixture.CreateTestDir("pack-no-runtimes");
        var projectDir = fixture.CreateTemplateProject(testDir, "TestPkg.NoRuntimes", new()
        {
            ["Main.cst"] = "template content",
        });

        var nupkg = await fixture.PackTemplateProjectAsync(projectDir);
        var entries = TemplatePackageFixture.GetNupkgEntries(nupkg);

        // No runtimes/ folder should be present in the package
        Assert.DoesNotContain(entries,
            e => e.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase));
    }
}
