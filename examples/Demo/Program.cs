using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args is not [var directory])
{
    Console.Error.WriteLine("Pass one destination directory; existing output files are refused.");
    return 2;
}
Directory.CreateDirectory(directory);
var good = Path.Combine(directory, "good.nupkg");
var bad = Path.Combine(directory, "bad.nupkg");
var changed = Path.Combine(directory, "changed.nupkg");
var missing = Path.Combine(directory, "missing.nupkg");
var policy = Path.Combine(directory, "policy.json");
if (new[] { good, bad, changed, missing, policy }.Any(path => File.Exists(path) || Directory.Exists(path)))
{
    Console.Error.WriteLine("An output already exists; use a fresh directory.");
    return 2;
}
const string expectedPayload = "expected binary payload\n";
Create(good, includeGuide: true, expectedPayload);
Create(bad, includeGuide: false, expectedPayload);
Create(changed, includeGuide: true, "unexpected changed payload\n");
Create(missing, includeGuide: true, payload: null);
using (var output = new FileStream(policy, FileMode.CreateNew, FileAccess.Write))
    JsonSerializer.Serialize(output, new
    {
        schemaVersion = 1,
        requiredFiles = new object[] { new { path = "README.md" }, new { path = "tools/payload.dll", sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expectedPayload))).ToLowerInvariant() } }
    }, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine("Created synthetic good/bad/changed/missing archives and policy.json (not installable packages; payload.dll is inert text).");
return 0;

static void Create(string path, bool includeGuide, string? payload)
{
    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    Add("Demo.nuspec", "<package><metadata><id>Demo.Tool</id><version>1.2.3</version><repository type=\"git\" commit=\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"/><readme>README.md</readme></metadata></package>");
    Add("README.md", "# Demo\n\n[Guide](docs/guide.md)\n");
    if (includeGuide) Add("docs/guide.md", "# Guide\n\n[Back](../README.md)\n");
    if (payload is not null) Add("tools/payload.dll", payload);

    void Add(string name, string text)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }
}
