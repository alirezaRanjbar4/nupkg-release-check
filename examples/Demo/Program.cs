using System.IO.Compression;
using System.Text;

if (args is not [var directory])
{
    Console.Error.WriteLine("Pass one destination directory; existing output files are refused.");
    return 2;
}
Directory.CreateDirectory(directory);
var good = Path.Combine(directory, "good.nupkg");
var bad = Path.Combine(directory, "bad.nupkg");
if (File.Exists(good) || File.Exists(bad))
{
    Console.Error.WriteLine("An output already exists; use a fresh directory.");
    return 2;
}
Create(good, includeGuide: true);
Create(bad, includeGuide: false);
Console.WriteLine("Created synthetic good.nupkg and bad.nupkg (not installable packages).");
return 0;

static void Create(string path, bool includeGuide)
{
    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    Add("Demo.nuspec", "<package><metadata><id>Demo.Tool</id><version>1.2.3</version><repository type=\"git\" commit=\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"/><readme>README.md</readme></metadata></package>");
    Add("README.md", "# Demo\n\n[Guide](docs/guide.md)\n");
    if (includeGuide) Add("docs/guide.md", "# Guide\n\n[Back](../README.md)\n");

    void Add(string name, string text)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }
}
