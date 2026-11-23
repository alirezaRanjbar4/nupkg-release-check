using System.Security.Cryptography;
using System.Text.Json;

namespace NupkgReleaseCheck;

internal sealed record RequiredFile(string Path, string? Sha256);
internal sealed record ReleasePolicy(string Sha256, IReadOnlyList<RequiredFile> RequiredFiles)
{
    private const int MaxBytes = 256 * 1024;

    public static ReleasePolicy Load(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaxBytes) throw new InputException("Policy exceeds the 256 KiB limit.");
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = file.Read(buffer)) > 0)
        {
            if (bytes.Length + read > MaxBytes) throw new InputException("Policy exceeds the 256 KiB limit.");
            bytes.Write(buffer, 0, read);
        }
        var payload = bytes.ToArray();
        var json = payload.AsMemory();
        if (payload.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) json = json[3..];
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = Fields(document.RootElement, ["schemaVersion", "requiredFiles"]);
            if (!root.TryGetValue("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != 1)
                throw new InputException("Policy schemaVersion must be the integer 1.");
            if (!root.TryGetValue("requiredFiles", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() is < 1 or > 1000)
                throw new InputException("Policy requiredFiles must contain 1 to 1,000 entries.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var required = new List<RequiredFile>();
            foreach (var element in files.EnumerateArray())
            {
                var fields = Fields(element, ["path", "sha256"]);
                if (!fields.TryGetValue("path", out var name) || name.ValueKind != JsonValueKind.String ||
                    name.GetString() is not { Length: > 0 and <= 1024 } archivePath || !Inspector.SafeArchivePath(archivePath) || !paths.Add(archivePath))
                    throw new InputException("Policy needs safe, unique file paths of at most 1,024 characters; case collisions are rejected.");
                string? hash = null;
                if (fields.TryGetValue("sha256", out var digest))
                {
                    if (digest.ValueKind != JsonValueKind.String || digest.GetString() is not { Length: 64 } value || !value.All(Uri.IsHexDigit))
                        throw new InputException("Policy sha256 must be 64 hexadecimal characters when supplied.");
                    hash = value.ToLowerInvariant();
                }
                required.Add(new(archivePath, hash));
            }
            return new(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), required.ToArray());
        }
        catch (JsonException) { throw new InputException("Policy must be strict UTF-8 JSON with depth at most 8; see the documented schema."); }
        // JsonDocument defers validation of escaped UTF-16 until a string is read.
        catch (InvalidOperationException) { throw new InputException("Policy JSON strings must contain valid Unicode, including paired surrogate escapes."); }
    }

    private static Dictionary<string, JsonElement> Fields(JsonElement element, string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InputException("Policy root and required file entries must be objects.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !fields.TryAdd(property.Name, property.Value))
                throw new InputException("Policy contains an unknown or duplicate field; names are case-sensitive.");
        return fields;
    }
}
