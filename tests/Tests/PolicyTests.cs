using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NupkgReleaseCheck;

namespace Tests;

public sealed class PolicyTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "nrc-policy-" + Guid.NewGuid().ToString("N"));
    private const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Payload = "expected binary payload\n";
    private static readonly string PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Payload))).ToLowerInvariant();

    public PolicyTests() => Directory.CreateDirectory(directory);

    private string Policy(string json)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }

    private string FilePolicy(string path, string? hash = null) => Policy(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        requiredFiles = new[] { hash is null ? new Dictionary<string, string> { ["path"] = path } : new Dictionary<string, string> { ["path"] = path, ["sha256"] = hash } }
    }));

    private string Package(string? payload = Payload)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Add("Demo.nuspec", $"<package><metadata><id>Demo.Tool</id><version>1.2.3</version><repository commit=\"{Commit}\"/><readme>README.md</readme></metadata></package>");
        Add("README.md", "# Demo\n");
        if (payload is not null) Add("tools/payload.dll", payload);
        archive.CreateEntry("empty/");
        return path;

        void Add(string name, string value)
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(Encoding.UTF8.GetBytes(value));
        }
    }

    [Fact]
    public void PolicyPassHasExactByteProvenanceCountsAndPreservesBothFiles()
    {
        var package = Package();
        var policy = Policy("\ufeff" + JsonSerializer.Serialize(new { schemaVersion = 1, requiredFiles = new object[] { new { path = "README.md" }, new { path = "tools/payload.dll", sha256 = PayloadHash.ToUpperInvariant() } } }));
        var original = File.ReadAllBytes(package);
        var policyBytes = File.ReadAllBytes(policy);
        var result = Run(package, policy);
        Assert.Equal(0, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(policyBytes)).ToLowerInvariant(), json.RootElement.GetProperty("policySha256").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("requiredFilesChecked").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("fileHashesChecked").GetInt32());
        Assert.Equal(original, File.ReadAllBytes(package));
        Assert.Equal(policyBytes, File.ReadAllBytes(policy));
        Assert.Equal(2, Directory.GetFileSystemEntries(directory).Length);
    }

    [Fact]
    public void ChangedPayloadFailsDespiteUnchangedManifestCommitAndDefaultStillPasses()
    {
        var package = Package("unexpected changed DLL\n");
        var policy = FilePolicy("tools/payload.dll", PayloadHash);
        var report = Inspector.Inspect(package, expectedCommit: Commit, policyPath: policy);
        var finding = Assert.Single(report.Diagnostics);
        Assert.Equal("NRC009", finding.Code);
        Assert.Equal("tools/payload.dll", finding.Entry);
        Assert.Equal(Commit, report.RepositoryCommit);
        Assert.Equal(1, Run(package, policy).Code);
        Assert.Empty(Inspector.Inspect(package).Diagnostics);
        var text = Run(package, policy, json: false);
        Assert.Contains("NRC009 error", text.Output);
        Assert.DoesNotContain("unexpected changed DLL", text.Output);
        Assert.DoesNotContain(PayloadHash, text.Output);
    }

    [Theory]
    [InlineData("tools/absent.dll")]
    [InlineData("Tools/payload.dll")]
    [InlineData("empty")]
    [InlineData("tools")]
    public void RequiredPathsAreCaseSensitiveFilesRatherThanDirectories(string name)
    {
        var result = Run(Package(), FilePolicy(name));
        Assert.Equal(1, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("NRC008", json.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":2,\"requiredFiles\":[{\"path\":\"a\"}]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"requiredFiles\":[{\"path\":\"a\"}]}")]
    [InlineData("{\"schemaVersion\":1.0,\"requiredFiles\":[{\"path\":\"a\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{}]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{\"path\":null}]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{\"Path\":\"a\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{\"path\":\"a\",\"sha256\":null}]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{\"path\":\"a\",\"sha256\":\"abc\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{\"path\":\"a\",\"sha256\":\"gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"requiredFiles\":[{\"path\":\"a\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{\"path\":\"a\",\"p\\u0061th\":\"b\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{\"path\":\"a\",\"extra\":1}]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{\"path\":\"a\"}],\"extra\":1}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{\"path\":\"a\"}, {\"path\":\"A\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{\"path\":\"a\"}, {\"path\":\"a\"}]}")]
    [InlineData("{\"schemaVersion\":1,/* comment */\"requiredFiles\":[{\"path\":\"a\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{\"path\":\"a\"}],}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{\"path\":\"\\ud800\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"requiredFiles\":[{\"path\":\"a\",\"sha256\":\"\\udfff\"}]}")]
    public void InvalidPolicyFailsBeforePackageIsOpenedWithoutPartialReport(string json)
    {
        var result = Run(Path.Combine(directory, "does-not-exist.nupkg"), Policy(json));
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
        Assert.Contains("Policy", result.Error);
        Assert.DoesNotContain("could not be read", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../outside")]
    [InlineData("/rooted")]
    [InlineData("C:/drive")]
    [InlineData("a\\b")]
    [InlineData("./a")]
    [InlineData("a/../b")]
    [InlineData("a//b")]
    [InlineData("a/")]
    [InlineData("a\nb")]
    public void UnsafePolicyPathsAreRejectedRatherThanNormalized(string name)
    {
        var result = Run(Package(), FilePolicy(name));
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
    }

    [Fact]
    public void PolicySizeCountPathLengthAndUtf8AreBounded()
    {
        Assert.Equal(2, Run(Package(), Policy(new string(' ', 256 * 1024 + 1))).Code);
        Assert.Equal(2, Run(Package(), FilePolicy(new string('a', 1025))).Code);
        var many = Policy(JsonSerializer.Serialize(new { schemaVersion = 1, requiredFiles = Enumerable.Range(0, 1001).Select(i => new { path = i.ToString() }) }));
        Assert.Equal(2, Run(Package(), many).Code);
        var invalidUtf8 = Policy("{}"); File.WriteAllBytes(invalidUtf8, [0xff]);
        Assert.Equal(2, Run(Package(), invalidUtf8).Code);
        Assert.Equal(2, Run(Package(), Policy(new string('[', 9) + new string(']', 9))).Code);
        var allowed = Policy(JsonSerializer.Serialize(new { schemaVersion = 1, requiredFiles = Enumerable.Range(0, 1000).Select(i => new { path = i.ToString() }) }));
        Assert.Equal(1000, Inspector.Inspect(Package(), policyPath: allowed).RequiredFilesChecked);
    }

    [Fact]
    public void LiteralUnicodeSpacePercentAndHashPathsAndEmptyPayloadWork()
    {
        var package = Package();
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update)) archive.CreateEntry("docs/آزمون %20#x.bin");
        var emptyHash = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>()));
        Assert.Empty(Inspector.Inspect(package, policyPath: FilePolicy("docs/آزمون %20#x.bin", emptyHash)).Diagnostics);
    }

    [Fact]
    public void OnlyRequestedHashesAreDecompressedAndPerFileLimitIsEnforced()
    {
        var package = Package();
        AddZeros(package, "large.bin", 64L * 1024 * 1024 + 1);
        Assert.Empty(Inspector.Inspect(package, policyPath: FilePolicy("large.bin")).Diagnostics);
        var report = Inspector.Inspect(package, policyPath: FilePolicy("large.bin", new string('0', 64)));
        Assert.Equal("NRC010", Assert.Single(report.Diagnostics).Code);
        Assert.Equal(0, report.FileHashesChecked);
        Assert.Empty(Inspector.Inspect(package, policyPath: FilePolicy("tools/payload.dll", PayloadHash)).Diagnostics);
    }

    [Fact]
    public void TotalHashBudgetAcceptsExactBoundaryThenRefusesNextEntry()
    {
        var package = Package();
        const int size = 64 * 1024 * 1024;
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[8192];
        for (var offset = 0; offset < size; offset += buffer.Length) hasher.AppendData(buffer);
        var hash = Convert.ToHexString(hasher.GetHashAndReset());
        for (var i = 0; i < 5; i++) AddZeros(package, $"{i}.bin", size);
        var policy = Policy(JsonSerializer.Serialize(new { schemaVersion = 1, requiredFiles = Enumerable.Range(0, 5).Select(i => new { path = $"{i}.bin", sha256 = hash }) }));
        var report = Inspector.Inspect(package, policyPath: policy);
        var failure = Assert.Single(report.Diagnostics);
        Assert.Equal("NRC010", failure.Code);
        Assert.Equal("4.bin", failure.Entry);
        Assert.Equal(4, report.FileHashesChecked);
        Assert.Equal(5, report.RequiredFilesChecked);
    }

    private static void AddZeros(string path, string name, long length)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        using var output = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
        var buffer = new byte[8192];
        while (length > 0)
        {
            var count = (int)Math.Min(length, buffer.Length);
            output.Write(buffer, 0, count);
            length -= count;
        }
    }

    private static (int Code, string Output, string Error) Run(string package, string policy, bool json = true)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        var code = Application.Run([package, "--policy", policy, "--format", json ? "json" : "text"], output, error);
        return (code, output.ToString(), error.ToString());
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
