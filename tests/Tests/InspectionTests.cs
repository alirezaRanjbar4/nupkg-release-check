using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NupkgReleaseCheck;

namespace Tests;

public sealed class InspectionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "nupkg-check-" + Guid.NewGuid().ToString("N"));
    private const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Manifest = "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata><id>Demo.Tool</id><version>1.2.3</version><repository type=\"git\" commit=\"" + Commit + "\"/><readme>README.md</readme></metadata></package>";

    public InspectionTests() => Directory.CreateDirectory(directory);

    private string Package(string? manifest = Manifest, string readme = "# Demo\n", params (string Name, byte[] Bytes)[] extra)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        if (manifest is not null) Add("Demo.nuspec", Encoding.UTF8.GetBytes(manifest));
        Add("README.md", Encoding.UTF8.GetBytes(readme));
        foreach (var entry in extra) Add(entry.Name, entry.Bytes);
        return path;

        void Add(string name, byte[] bytes)
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }

    [Fact]
    public void ValidPackageMatchesExpectationsAndPreservesBytes()
    {
        var path = Package(readme: "[Guide](docs/guide.md)\n", extra: [("docs/guide.md", Encoding.UTF8.GetBytes("[Back](../README.md)\n"))]);
        var before = File.ReadAllBytes(path);
        var report = Inspector.Inspect(path, "demo.tool", "1.2.3", Commit.ToUpperInvariant());
        Assert.Empty(report.Diagnostics);
        Assert.Equal(2, report.MarkdownFilesChecked);
        Assert.Equal(2, report.LocalLinksChecked);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(before)).ToLowerInvariant(), report.PackageSha256);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFileSystemEntries(directory));
    }

    [Theory]
    [InlineData("Other.Tool", "1.2.3")]
    [InlineData("Demo.Tool", "1.2.3.0")]
    public void ExpectedIdentityIsEnforced(string id, string version) =>
        Assert.Contains(Inspector.Inspect(Package(), id, version).Diagnostics, row => row.Code == "NRC003");

    [Fact]
    public void WrongAndMissingCommitFailAnExplicitExpectation()
    {
        Assert.Contains(Inspector.Inspect(Package(), expectedCommit: new string('b', 40)).Diagnostics, row => row.Code == "NRC004" && row.Severity == "error");
        var path = Package(Manifest.Replace(Commit, ""));
        Assert.Contains(Inspector.Inspect(path, expectedCommit: Commit).Diagnostics, row => row.Code == "NRC004" && row.Severity == "error");
        Assert.Contains(Inspector.Inspect(path).Diagnostics, row => row.Code == "NRC004" && row.Severity == "warning");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void CommitExpectationMustBeAFullGitHash(string commit) =>
        Assert.Throws<InputException>(() => Inspector.Inspect(Package(), expectedCommit: commit));

    [Theory]
    [InlineData("not XML")]
    [InlineData("<!DOCTYPE package [<!ENTITY x SYSTEM 'file:///never-read'>]><package><metadata><id>&x;</id><version>1</version></metadata></package>")]
    [InlineData("<package><metadata><id>Demo</id></metadata></package>")]
    [InlineData("<package><metadata><id>A</id><id>B</id><version>1</version></metadata></package>")]
    public void MalformedOrAmbiguousManifestIsRejected(string manifest) =>
        Assert.Contains(Inspector.Inspect(Package(manifest)).Diagnostics, row => row.Code == "NRC002");

    [Fact]
    public void ExactlyOneRootManifestIsRequired()
    {
        Assert.Contains(Inspector.Inspect(Package(null)).Diagnostics, row => row.Code == "NRC002");
        Assert.Contains(Inspector.Inspect(Package(extra: [("Other.nuspec", Encoding.UTF8.GetBytes(Manifest))])).Diagnostics, row => row.Code == "NRC002");
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/rooted")]
    [InlineData("a\\b")]
    [InlineData("C:/drive")]
    [InlineData("a//b")]
    [InlineData("README.md")]
    [InlineData("readme.md")]
    public void UnsafeAndCollidingArchivePathsFail(string name) =>
        Assert.Contains(Inspector.Inspect(Package(extra: [(name, [])])).Diagnostics, row => row.Code == "NRC001");

    [Fact]
    public void DeclaredReadmeMustExist()
    {
        Assert.Contains(Inspector.Inspect(Package(Manifest.Replace("<readme>README.md</readme>", "<readme>missing.md</readme>"))).Diagnostics, row => row.Code == "NRC005" && row.Severity == "error");
        Assert.Contains(Inspector.Inspect(Package(Manifest.Replace("<readme>README.md</readme>", ""))).Diagnostics, row => row.Code == "NRC005" && row.Severity == "warning");
    }

    [Fact]
    public void CommonMarkLinksImagesAndReferencesHaveSourceLines()
    {
        var report = Inspector.Inspect(Package(readme: "# Demo\n\n[Inline](missing.md)\n![Image](missing.png)\n[Reference][guide]\n\n[guide]: docs/absent.md\n"));
        var errors = report.Diagnostics.Where(row => row.Code == "NRC006").ToArray();
        Assert.Equal(3, report.LocalLinksChecked);
        Assert.Equal([3, 4, 5], errors.Select(row => row.Line));
        Assert.Equal(["missing.md", "missing.png", "docs/absent.md"], errors.Select(row => row.Target));
    }

    [Fact]
    public void CodeExamplesExternalUrlsAnchorsAndHtmlAreNotFetchedOrChecked()
    {
        var report = Inspector.Inspect(Package(readme: "```md\n[x](absent.md)\n```\n`[x](absent.md)`\n[x](https://invalid.invalid/no) [x](mailto:a@example.com) [x](#missing) [x](/site/root) [x](//invalid.invalid/a)\n<a href=\"missing.md\">HTML</a>\n"));
        Assert.Empty(report.Diagnostics);
        Assert.Equal(0, report.LocalLinksChecked);
    }

    [Fact]
    public void RelativeLinksDecodeSpacesAndIgnoreQueriesAndFragments()
    {
        var report = Inspector.Inspect(Package(readme: "[Guide](docs/a%20b.md?raw=1#part)\n", extra: [("docs/a b.md", Encoding.UTF8.GetBytes("[Back](../README.md)\n"))]));
        Assert.Empty(report.Diagnostics);
        Assert.Equal(2, report.LocalLinksChecked);
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("%2e%2e/outside.md")]
    [InlineData("docs/%2e%2e/%2e%2e/outside.md")]
    [InlineData("readme.md")]
    public void MissingCaseMismatchAndRootEscapesFail(string target) =>
        Assert.Contains(Inspector.Inspect(Package(readme: $"[Bad]({target})\n")).Diagnostics, row => row.Code == "NRC006");

    [Fact]
    public void BothImplicitAndEmptyExplicitDirectoriesResolve()
    {
        var report = Inspector.Inspect(Package(readme: "[Docs](docs/) [Empty](empty/) [Root](.)\n", extra: [("docs/guide.md", []), ("empty/", [])]));
        Assert.Empty(report.Diagnostics);
        Assert.Equal(3, report.LocalLinksChecked);
    }

    [Fact]
    public void TrailingSlashCannotResolveToARegularFile()
    {
        Assert.Contains(Inspector.Inspect(Package(readme: "[Bad](README.md/)\n")).Diagnostics, row => row.Code == "NRC006");
    }

    [Fact]
    public void NuspecReadmeIsALiteralArchivePathRatherThanAUrl()
    {
        var manifest = Manifest.Replace("<readme>README.md</readme>", "<readme>docs/a%20b#c.md</readme>");
        Assert.Empty(Inspector.Inspect(Package(manifest, extra: [("docs/a%20b#c.md", Encoding.UTF8.GetBytes("# Readme\n"))])).Diagnostics);
    }

    [Fact]
    public void InvalidUtf8AndOversizedTextProduceBoundedDiagnostics()
    {
        Assert.Contains(Inspector.Inspect(Package(extra: [("docs/bad.md", [0xff, 0xfe])])).Diagnostics, row => row.Code == "NRC007");
        Assert.Contains(Inspector.Inspect(Package(extra: [("docs/large.md", new byte[2 * 1024 * 1024 + 1])])).Diagnostics, row => row.Code == "NRC007");
    }

    [Fact]
    public void NonZipInputIsAFindingAndFileErrorsAreCliErrors()
    {
        var path = Path.Combine(directory, "bad.nupkg"); File.WriteAllText(path, "not zip");
        Assert.Contains(Inspector.Inspect(path).Diagnostics, row => row.Code == "NRC001");
        Assert.Equal(1, Run([path]).Code);
        var result = Run([Path.Combine(directory, "absent.nupkg"), "--format", "json"]);
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void JsonSchemaAndStrictWarningExitArePredictable()
    {
        var result = Run([Package(), "--format", "json", "--strict"]);
        Assert.Equal(0, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Demo.Tool", json.RootElement.GetProperty("packageId").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("errors").GetInt32());
        var missing = Package(Manifest.Replace(Commit, ""));
        Assert.Equal(0, Run([missing]).Code);
        Assert.Equal(1, Run([missing, "--strict"]).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--unknown")]
    [InlineData("--format")]
    [InlineData("--format yaml")]
    [InlineData("--strict --strict")]
    [InlineData("--expect-id A --expect-id B")]
    [InlineData("extra.nupkg")]
    public void InvalidArgumentsReturnTwo(string arguments)
    {
        var args = arguments.Length == 0 ? Array.Empty<string>() : new[] { Package() }.Concat(arguments.Split(' ')).ToArray();
        Assert.Equal(2, Run(args).Code);
    }

    [Fact]
    public void VersionAndHelpAreAvailableWithoutAFile()
    {
        Assert.Equal("0.1.0", Run(["--version"]).Output.Trim());
        Assert.Equal(0, Run(["--help"]).Code);
    }

    private static (int Code, string Output, string Error) Run(string[] args)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        var code = Application.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
