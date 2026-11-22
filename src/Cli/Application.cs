using System.Text.Json;

namespace NupkgReleaseCheck;

public static class Application
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is ["--help"] or ["-h"])
        {
            output.WriteLine("nupkg-release-check package.nupkg [--expect-id ID] [--expect-version VERSION] [--expect-commit FULL_GIT_HASH] [--format text|json] [--strict]\nOffline manifest expectations and package-local Markdown links. No extraction, installation or network requests. Exit: 0 pass, 1 findings (warnings only with --strict), 2 arguments or file error.");
            return 0;
        }
        if (args is ["--version"]) { output.WriteLine("0.1.0"); return 0; }
        try
        {
            if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith('-')) throw new InputException("Pass one package path first; see --help.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var strict = false;
            for (var i = 1; i < args.Length; i++)
            {
                var key = args[i];
                if (key == "--strict") { if (strict) throw new InputException("Repeated option."); strict = true; continue; }
                if (key is not ("--expect-id" or "--expect-version" or "--expect-commit" or "--format")) throw new InputException("Unknown argument; see --help.");
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal)) throw new InputException("Option requires a nonblank value.");
                if (!values.TryAdd(key, args[i])) throw new InputException("Repeated option.");
            }
            var format = values.GetValueOrDefault("--format", "text");
            if (format is not ("text" or "json")) throw new InputException("Format must be text or json.");
            var report = Inspector.Inspect(args[0], values.GetValueOrDefault("--expect-id"), values.GetValueOrDefault("--expect-version"), values.GetValueOrDefault("--expect-commit"));
            if (format == "json") output.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
            else
            {
                foreach (var finding in report.Diagnostics)
                    output.WriteLine($"{finding.Code} {finding.Severity} {JsonSerializer.Serialize(finding.Entry)}{(finding.Line is { } line ? $":{line}" : "")}: {finding.Message}" +
                        (finding.Target is { } target ? $" Target: {JsonSerializer.Serialize(target)}" : ""));
                output.WriteLine($"Checked {report.EntriesChecked} archive entries, {report.MarkdownFilesChecked} Markdown files, {report.LocalLinksChecked} local links: {report.Errors} error(s), {report.Warnings} warning(s).");
            }
            return report.Errors > 0 || (strict && report.Warnings > 0) ? 1 : 0;
        }
        catch (InputException exception) { error.WriteLine(exception.Message); return 2; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { error.WriteLine("Package could not be read. Check the path, permissions and arguments."); return 2; }
    }
}
