// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Testing;
using DotNetDevMCP.Testing.Mcp.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DotNetDevMCP.Testing.Tests;

/// <summary>
/// The affected-test project fallback (<see cref="AffectedTestFinder.FindAffectedTestProjects"/>) following NuGet
/// package references, not just Roslyn ProjectReferences: a test project that consumes a solution library only
/// through a restored PackageReference (its obj/project.assets.json), with no ProjectReference at all, should still
/// be selected when that library changes. Unlike <see cref="AffectedTestFinderTests"/>, these need real files on
/// disk (a project.assets.json fixture, and for one case a real .csproj with a literal &lt;PackageId&gt;), so each
/// test builds its own temp directory tree and points an AdhocWorkspace's project FilePaths at it.
/// </summary>
public class NuGetPackageEdgeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotnetdevmcp-nuget-edge-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public void Test_project_referencing_a_package_with_no_ProjectReference_is_selected_when_the_library_changes()
    {
        var (solution, libACs) = Build(
            libAProjectXml: DefaultCsproj,
            assetsJson: AssetsJson(("LibA", "package")));

        var affected = AffectedTestFinder.FindAffectedTestProjects(solution, [libACs]);

        Assert.Equal([Path.Combine(_root, "test", "LibA.Tests.csproj")], affected);
    }

    [Fact]
    public void An_unrelated_package_id_in_the_assets_file_does_not_create_an_edge()
    {
        var (solution, libACs) = Build(
            libAProjectXml: DefaultCsproj,
            assetsJson: AssetsJson(("Some.Other.Package", "package")));

        var affected = AffectedTestFinder.FindAffectedTestProjects(solution, [libACs]);

        Assert.Empty(affected);
    }

    [Fact]
    public void A_literal_PackageId_in_the_project_file_is_used_instead_of_the_assembly_name()
    {
        const string customPackageId = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>Custom.Id</PackageId>
              </PropertyGroup>
            </Project>
            """;

        var (solution, libACs) = Build(
            libAProjectXml: customPackageId,
            assetsJson: AssetsJson(("Custom.Id", "package")));

        var affected = AffectedTestFinder.FindAffectedTestProjects(solution, [libACs]);

        Assert.Equal([Path.Combine(_root, "test", "LibA.Tests.csproj")], affected);
    }

    [Fact]
    public void Malformed_assets_json_does_not_throw_and_creates_no_edge()
    {
        var (solution, libACs) = Build(libAProjectXml: DefaultCsproj, assetsJson: "{ this is not valid json");

        var affected = Record.Exception(() => AffectedTestFinder.FindAffectedTestProjects(solution, [libACs]));

        Assert.Null(affected);
        Assert.Empty(AffectedTestFinder.FindAffectedTestProjects(solution, [libACs]));
    }

    [Fact]
    public void CountUnrestoredTestProjects_counts_test_projects_with_no_assets_file()
    {
        var (solution, _) = Build(libAProjectXml: DefaultCsproj, assetsJson: null);

        Assert.Equal(1, AffectedTestFinder.CountUnrestoredTestProjects(solution));
    }

    [Fact]
    public void CountUnrestoredTestProjects_is_zero_once_the_assets_file_exists()
    {
        var (solution, _) = Build(libAProjectXml: DefaultCsproj, assetsJson: AssetsJson(("LibA", "package")));

        Assert.Equal(0, AffectedTestFinder.CountUnrestoredTestProjects(solution));
    }

    private const string DefaultCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    private static string AssetsJson(params (string Id, string Type)[] entries)
    {
        var targets = string.Join(",\n", entries.Select(e => $$"""
                "{{e.Id}}/1.0.0": { "type": "{{e.Type}}" }
            """));
        return $$"""
            {
              "targets": {
                "net10.0": {
                  {{targets}}
                }
              }
            }
            """;
    }

    /// <summary>
    /// LibA (a library with the given project XML) and LibA.Tests (an xUnit project with NO ProjectReference to
    /// LibA - the whole point of these tests - but with the given project.assets.json, or none) on real temp-folder
    /// paths, wired into an AdhocWorkspace the same way <see cref="AffectedTestFinderTests"/> does it.
    /// </summary>
    private (Solution Solution, string LibACsprojPath) Build(string libAProjectXml, string? assetsJson)
    {
        var srcDir = Path.Combine(_root, "src");
        var testDir = Path.Combine(_root, "test");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(testDir);

        var libACsprojPath = Path.Combine(srcDir, "LibA.csproj");
        File.WriteAllText(libACsprojPath, libAProjectXml);

        var testsCsprojPath = Path.Combine(testDir, "LibA.Tests.csproj");
        File.WriteAllText(testsCsprojPath, DefaultCsproj);

        if (assetsJson is not null)
        {
            var objDir = Path.Combine(testDir, "obj");
            Directory.CreateDirectory(objDir);
            File.WriteAllText(Path.Combine(objDir, "project.assets.json"), assetsJson);
        }

        var workspace = new AdhocWorkspace();
        var corlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var runtime = MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"));
        var xunit = MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location);

        var solution = workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Default, Path.Combine(_root, "Test.sln"), []));

        var libAId = ProjectId.CreateNewId();
        var libASourcePath = Path.Combine(srcDir, "A.cs");
        solution = solution
            .AddProject(ProjectInfo.Create(libAId, VersionStamp.Default, "LibA", "LibA", LanguageNames.CSharp,
                filePath: libACsprojPath, metadataReferences: [corlib, runtime]))
            .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(libAId), "A.cs",
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From("namespace LibA; public class A { public int M() => 1; }"), VersionStamp.Default)),
                filePath: libASourcePath));

        var testsId = ProjectId.CreateNewId();
        solution = solution
            // No AddProjectReference here: the edge under test comes only from the package reference below.
            .AddProject(ProjectInfo.Create(testsId, VersionStamp.Default, "LibA.Tests", "LibA.Tests", LanguageNames.CSharp,
                filePath: testsCsprojPath, metadataReferences: [corlib, runtime, xunit]))
            .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(testsId), "T.cs",
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(
                    "using Xunit; namespace LibA.Tests; public class T { [Fact] public void T1() { } }"), VersionStamp.Default)),
                filePath: Path.Combine(testDir, "T.cs")));

        return (solution, libACsprojPath);
    }
}

/// <summary>
/// The pure, unit-testable half of the Central Package Management precision in
/// <see cref="TestingTools.RunAffected"/>: diffing two versions of a Directory.Packages.props file's XML to the set
/// of package ids that were added, removed, or changed version.
/// </summary>
public class CentralPackageManagementDiffTests
{
    [Fact]
    public void Detects_a_version_bump()
    {
        var oldXml = Props(("Foo", "1.0.0"), ("Bar", "2.0.0"));
        var newXml = Props(("Foo", "1.1.0"), ("Bar", "2.0.0"));

        var changed = TestingTools.DiffPackageVersions(oldXml, newXml);

        Assert.Equal(["Foo"], changed);
    }

    [Fact]
    public void Detects_an_added_package()
    {
        var oldXml = Props(("Foo", "1.0.0"));
        var newXml = Props(("Foo", "1.0.0"), ("Bar", "2.0.0"));

        var changed = TestingTools.DiffPackageVersions(oldXml, newXml);

        Assert.Equal(["Bar"], changed);
    }

    [Fact]
    public void Detects_a_removed_package()
    {
        var oldXml = Props(("Foo", "1.0.0"), ("Bar", "2.0.0"));
        var newXml = Props(("Foo", "1.0.0"));

        var changed = TestingTools.DiffPackageVersions(oldXml, newXml);

        Assert.Equal(["Bar"], changed);
    }

    [Fact]
    public void Unchanged_packages_are_not_reported()
    {
        var oldXml = Props(("Foo", "1.0.0"), ("Bar", "2.0.0"));
        var newXml = Props(("Foo", "1.0.0"), ("Bar", "2.0.0"));

        var changed = TestingTools.DiffPackageVersions(oldXml, newXml);

        Assert.Empty(changed!);
    }

    [Fact]
    public void Malformed_xml_returns_null_instead_of_throwing()
    {
        var changed = TestingTools.DiffPackageVersions("<Project><ItemGroup>", Props(("Foo", "1.0.0")));

        Assert.Null(changed);
    }

    private static string Props(params (string Id, string Version)[] entries) =>
        "<Project>\n  <ItemGroup>\n" +
        string.Join("\n", entries.Select(e => $"""    <PackageVersion Include="{e.Id}" Version="{e.Version}" />""")) +
        "\n  </ItemGroup>\n</Project>";
}
