using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace NupkgReleaseCheck;

public sealed class InputException(string message) : Exception(message);
public sealed record Finding(string Code, string Severity, string? Entry, int? Line, string Message, string? Target = null);
public sealed record PackageReport(int SchemaVersion, string Package, string? PackageSha256, string? PackageId,
    string? Version, string? RepositoryCommit, int EntriesChecked, int MarkdownFilesChecked, int LocalLinksChecked,
    IReadOnlyList<Finding> Diagnostics)
{
    public int Errors => Diagnostics.Count(row => row.Severity == "error");
    public int Warnings => Diagnostics.Count(row => row.Severity == "warning");
}

public static class Inspector
{
    private const long MaxPackageBytes = 512L * 1024 * 1024;
    private const int MaxEntries = 10_000;
    private const int MaxTextBytes = 2 * 1024 * 1024;
    private const int MaxTotalTextBytes = 16 * 1024 * 1024;
    private const int MaxLinks = 100_000;
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();

    public static PackageReport Inspect(string path, string? expectedId = null, string? expectedVersion = null, string? expectedCommit = null)
    {
        foreach (var expected in new[] { expectedId, expectedVersion, expectedCommit })
            if (expected is not null && (string.IsNullOrWhiteSpace(expected) || expected.Length > 128))
                throw new InputException("Expected metadata values must be nonblank and at most 128 characters.");
        if (expectedCommit is not null && (expectedCommit.Length is not (40 or 64) || !expectedCommit.All(Uri.IsHexDigit)))
            throw new InputException("Expected commit must be a full 40- or 64-character hexadecimal Git hash.");

        var findings = new List<Finding>();
        string? hash = null, id = null, version = null, commit = null;
        var entryCount = 0; var markdownCount = 0; var linkCount = 0; var textBytes = 0;
        PackageReport Report() => new(1, path, hash, id, version, commit, entryCount, markdownCount, linkCount, findings.ToArray());
        void Error(string code, string? entry, string message, int? line = null, string? target = null) => findings.Add(new(code, "error", entry, line, message, target));

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaxPackageBytes) { Error("NRC001", null, "Package exceeds the 512 MiB inspection limit."); return Report(); }
        hash = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
        file.Position = 0;
        try
        {
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
            entryCount = archive.Entries.Count;
            if (entryCount > MaxEntries) { Error("NRC001", null, "Archive exceeds the 10,000-entry inspection limit."); return Report(); }
            var allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            var directories = new HashSet<string>(StringComparer.Ordinal) { "" };
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.TrimEnd('/');
                if (!SafeArchivePath(name) || !allPaths.Add(name))
                    Error("NRC001", entry.FullName, "Unsafe, duplicate or case-colliding archive path.");
                else
                {
                    if (entry.FullName.EndsWith('/')) directories.Add(name);
                    else files.Add(entry.FullName, entry);
                    var parent = name.LastIndexOf('/');
                    while (parent >= 0)
                    {
                        directories.Add(name[..parent]);
                        parent = name.LastIndexOf('/', parent - 1);
                    }
                }
            }
            if (findings.Count > 0) return Report();
            var manifests = files.Values.Where(entry => !entry.FullName.Contains('/') && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (manifests.Length != 1) { Error("NRC002", null, "Expected exactly one root nuspec file."); return Report(); }
            var manifest = manifests[0];
            var manifestBytes = ReadBytes(manifest);
            if (manifestBytes is null) return Report();
            XElement metadata;
            try
            {
                using var reader = XmlReader.Create(new MemoryStream(manifestBytes), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxTextBytes });
                var root = XDocument.Load(reader).Root;
                var nodes = root?.Elements().Where(node => node.Name.LocalName == "metadata").ToArray();
                if (root?.Name.LocalName != "package" || nodes?.Length != 1) throw new XmlException();
                metadata = nodes[0];
                foreach (var name in new[] { "id", "version", "repository", "readme" })
                    if (metadata.Elements().Count(node => node.Name.LocalName == name) > 1) throw new XmlException();
            }
            catch (XmlException) { Error("NRC002", manifest.FullName, "Malformed or unsupported nuspec XML; DTDs are prohibited."); return Report(); }
            string? Value(string name) => metadata.Elements().SingleOrDefault(node => node.Name.LocalName == name)?.Value.Trim();
            id = Value("id"); version = Value("version");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version) || id.Length > 100 || version.Length > 128)
            { Error("NRC002", manifest.FullName, "Manifest needs a nonblank ID (at most 100 characters) and version (at most 128)."); return Report(); }
            commit = metadata.Elements().SingleOrDefault(node => node.Name.LocalName == "repository")?.Attribute("commit")?.Value.Trim();
            if (commit?.Length > 128) { Error("NRC002", manifest.FullName, "Repository commit metadata exceeds the inspection limit."); return Report(); }
            if ((expectedId is not null && !id.Equals(expectedId, StringComparison.OrdinalIgnoreCase)) ||
                (expectedVersion is not null && !version.Equals(expectedVersion, StringComparison.Ordinal)))
                Error("NRC003", manifest.FullName, "Package ID or version does not match the supplied expectation.");
            if (expectedCommit is not null && !string.Equals(commit, expectedCommit, StringComparison.OrdinalIgnoreCase))
                Error("NRC004", manifest.FullName, "Repository commit does not match the supplied Git hash.");
            else if (string.IsNullOrWhiteSpace(commit))
                findings.Add(new("NRC004", "warning", manifest.FullName, null, "Repository commit metadata is missing; no build-source attestation is performed."));

            var readme = Value("readme");
            if (string.IsNullOrWhiteSpace(readme)) findings.Add(new("NRC005", "warning", manifest.FullName, null, "Readme metadata is missing."));
            else if (!Resolve(manifest.FullName, readme.Replace('\\', '/'), out var readmeTarget) || readmeTarget is null || !files.ContainsKey(readmeTarget))
                Error("NRC005", manifest.FullName, "Declared readme is not a package-local archive file.");

            foreach (var entry in files.Values.Where(entry => entry.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).OrderBy(entry => entry.FullName, StringComparer.Ordinal))
            {
                markdownCount++;
                var bytes = ReadBytes(entry);
                if (bytes is null) continue;
                string text;
                try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
                catch (DecoderFallbackException) { Error("NRC007", entry.FullName, "Markdown is not valid UTF-8."); continue; }
                var document = Markdown.Parse(text, Pipeline);
                foreach (var link in document.Descendants<LinkInline>())
                {
                    var url = link.Url;
                    if (string.IsNullOrEmpty(url) || url.StartsWith('#') || url.StartsWith('/') || Uri.TryCreate(url, UriKind.Absolute, out _)) continue;
                    linkCount++;
                    if (linkCount > MaxLinks) { Error("NRC007", entry.FullName, "Package exceeds the 100,000-link inspection limit."); return Report(); }
                    if (!Resolve(entry.FullName, url, out var target))
                        Error("NRC006", entry.FullName, "Relative Markdown target escapes the package root or uses an unsupported path.", link.Line + 1);
                    else if (target is not null && !files.ContainsKey(target) && !directories.Contains(target))
                        Error("NRC006", entry.FullName, "Relative Markdown target is missing from the archive (case-sensitive).", link.Line + 1, target);
                }
            }

            byte[]? ReadBytes(ZipArchiveEntry entry)
            {
                if (entry.Length > MaxTextBytes || entry.Length + textBytes > MaxTotalTextBytes)
                { Error("NRC007", entry.FullName, "Text entry exceeds the 2 MiB per-file or 16 MiB total inspection limit."); return null; }
                using var input = entry.Open();
                using var output = new MemoryStream();
                var buffer = new byte[8192];
                int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    if (output.Length + read > MaxTextBytes || output.Length + read + textBytes > MaxTotalTextBytes)
                    { Error("NRC007", entry.FullName, "Decompressed text exceeds the inspection limit."); return null; }
                    output.Write(buffer, 0, read);
                }
                textBytes += checked((int)output.Length);
                return output.ToArray();
            }
        }
        catch (InvalidDataException) { Error("NRC001", null, "Invalid or unsupported ZIP archive data."); }
        return Report();
    }

    private static bool SafeArchivePath(string path) => path.Length > 0 && !path.StartsWith('/') &&
        !path.Any(c => char.IsControl(c) || "<>:\\|?*".Contains(c)) &&
        path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));

    private static bool Resolve(string source, string url, out string? target)
    {
        target = null;
        var end = url.IndexOfAny(['#', '?']);
        var raw = end < 0 ? url : url[..end];
        if (raw.Length == 0) return true;
        string decoded;
        try { decoded = Uri.UnescapeDataString(raw); }
        catch (UriFormatException) { return false; }
        if (decoded.StartsWith('/') || decoded.Any(c => char.IsControl(c) || "<>:\\|?*".Contains(c))) return false;
        var parts = source.Split('/').SkipLast(1).ToList();
        foreach (var part in decoded.Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..") { if (parts.Count == 0) return false; parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        target = string.Join('/', parts);
        return true;
    }
}
