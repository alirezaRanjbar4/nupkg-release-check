# Troubleshooting

## A GitHub README works but NRC006 fails

The link is resolved within the `.nupkg`, not your working tree. Pack each referenced guide/image/license, or use an absolute repository URL when the content intentionally stays on the web. Check the reported archive entry, line and target, then repack. Paths remain case-sensitive on Windows, so `docs/Guide.md` and `docs/guide.md` differ.

## Commit or version mismatch

Commit the intended source before running `dotnet pack`. Reusing a package from an earlier build can preserve an old `repository@commit`. Inspect the new package against `git rev-parse HEAD`. `--expect-commit` needs a full 40- or 64-character hexadecimal hash. Do not copy the package's observed commit as the expectation when the purpose is to detect a stale build. Version matching is literal: `1.2.3` differs from `1.2.3.0`.

## Warnings in an otherwise valid package

Missing nuspec readme or repository commit metadata is a warning without an explicit expectation. Use `--strict` to gate releases on warnings. Supplying `--expect-commit` makes a missing commit an error. The checker does not add or repair metadata.

## Bad archive, unsafe paths or text limits

`NRC001` includes invalid ZIP data, duplicate/case-colliding paths and inspection limits. `NRC007` includes invalid UTF-8, oversized text or too many local links. Rebuild malformed packages and remove unnecessary large bundled Markdown files. Input limits are fixed; there is no unlimited mode. The tool does not extract the archive as a fallback.

## Exit 2 and no JSON

Check the file path, file access and CLI options. Put the package path first, give each option exactly once and use `--format text` or `--format json`. Read stderr. `--help` and `--version` run without a package.

For `--policy`, use source 0.2.0; the published 0.1.0 tool lacks this option. Unknown/duplicate fields, case-colliding/unsafe paths, null digests and unsupported schema versions are errors. The [policy contract](POLICY.md) includes the exact strict JSON structure and size limits. Policy validation completes before package inspection, so fix that input first.

## Missing or changed policy payloads

`NRC008` requires the exact case-sensitive file path; directory names do not satisfy it. `NRC009` means actual bytes differ from your supplied SHA-256, even if nuspec commit metadata matches. Rebuild from the trusted source and check expected inputs; do not replace a trusted hash with the suspect package's digest merely to pass. `NRC010` is a fixed 64 MiB per-file/256 MiB total requested-hash limit. Existence-only checks omit `sha256` and do not decompress that file, but they cannot detect changed bytes. No check establishes binary provenance.

## Install or run fails

Use the .NET 10 SDK to install a GitHub Release package. Put that asset in `artifacts/` beside the repository's `nuget.local.config`, then use the [README installation command](../README.md). The config intentionally has no online source; it will not download a missing release for you. Delete neither an existing package nor a working installation to troubleshoot: use a fresh directory to reproduce first. The generated executable is `nupkg-release-check.exe` on Windows and `nupkg-release-check` on Linux/macOS.

## Reporting a bug

Include the tool/runtime version, command with sensitive paths removed, exit code, relevant diagnostic rows and a minimal synthetic archive. Avoid uploading private packages or secrets. A JSON report contains metadata and paths even though file bodies are not echoed. See [contributing](../CONTRIBUTING.md).
