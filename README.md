# nupkg-release-check

[![CI](https://github.com/alirezaRanjbar4/nupkg-release-check/actions/workflows/ci.yml/badge.svg)](https://github.com/alirezaRanjbar4/nupkg-release-check/actions/workflows/ci.yml)

An offline C#/.NET 10 command-line check for the **final `.nupkg` you intend to release**. Assert its package ID, exact version and Git commit, and find relative Markdown links whose targets were left out of the archive.

A repository README can work on GitHub while the packaged README points at missing guides. A package built before the release commit can also contain an older `repository@commit`. This tool checks the archive itself against expectations you supply. It reads the package without extracting files, installing its contents or making network requests.

## Try it from source

Requires the .NET 10 SDK. Run from the repository root:

```sh
dotnet test -c Release
dotnet run --project examples/Demo -- artifacts/demo
dotnet run --project src/Cli -- artifacts/demo/good.nupkg --expect-id Demo.Tool --expect-version 1.2.3 --expect-commit aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa --strict
dotnet run --project src/Cli -- artifacts/demo/bad.nupkg --format json
```

The good example exits `0`. The bad example deliberately leaves out `docs/guide.md` and exits `1` with `NRC006`. The generator refuses existing output files. These are synthetic inspection fixtures, not installable NuGet packages. See [examples](examples/README.md) for an automated smoke command.

## Install a released tool

Packages are distributed as [GitHub Release assets](https://github.com/alirezaRanjbar4/nupkg-release-check/releases), **not on NuGet.org**. The .NET 10 runtime is required for the installed tool; an SDK is needed for `dotnet tool install`.

From a checkout, download `NupkgReleaseCheck.Tool.0.1.0.nupkg` and `SHA256SUMS.txt` from release `v0.1.0` into `artifacts/`. The optional GitHub CLI command is:

```sh
gh release download v0.1.0 --repo alirezaRanjbar4/nupkg-release-check --dir artifacts
dotnet tool install NupkgReleaseCheck.Tool --version 0.1.0 --tool-path artifacts/tool --configfile nuget.local.config --no-cache
./artifacts/tool/nupkg-release-check --version
```

`nuget.local.config` uses only the local `artifacts/` feed. Compare the downloaded package's SHA-256 with `SHA256SUMS.txt` before installing it, using `Get-FileHash -Algorithm SHA256` in PowerShell or `sha256sum` on Linux. A checksum published beside an asset detects corruption; it is not an independent signature or an attestation.

## Release gate

After committing the source, pack it, then check that final artifact. In a PowerShell CI step, for example:

```powershell
$commit = git rev-parse HEAD
dotnet pack src/Cli -c Release -o artifacts
./artifacts/tool/nupkg-release-check artifacts/NupkgReleaseCheck.Tool.0.1.0.nupkg --expect-id NupkgReleaseCheck.Tool --expect-version 0.1.0 --expect-commit $commit --strict --format json
```

Use the expected commit from the trusted build context. An archive's own metadata is self-reported. Matching it does not prove how a DLL was built. The CI workflow also tests, formats, packs, installs from the local feed and runs examples on Windows, Linux and macOS.

## Checks and output

| Code | Check | Default severity |
| --- | --- | --- |
| `NRC001` | Invalid ZIP, unsafe/duplicate/case-colliding paths, package or entry-count limit | Error |
| `NRC002` | Exactly one root nuspec; bounded well-formed XML; unambiguous ID/version fields | Error |
| `NRC003` | Supplied ID/version expectations | Error |
| `NRC004` | Supplied full Git hash, or missing commit metadata | Error for mismatch; warning if missing without an expectation |
| `NRC005` | Declared readme exists inside the archive | Error if missing target; warning if no readme declared |
| `NRC006` | Package-local CommonMark links/images/reference links resolve | Error |
| `NRC007` | Valid UTF-8 Markdown and bounded text/link processing | Error |

ID and commit comparisons ignore case; version comparison is exact, with no NuGet version normalization. Local paths are case-sensitive on every OS. References inside code spans/fences are ignored. Directory links are accepted when the directory exists explicitly or has archived children. See the [design and limits](docs/DESIGN.md) for URL handling and size limits.

`--format text` is the default. `--format json` emits a single report on stdout with `schemaVersion: 1`, package SHA-256, observed metadata, counts and diagnostics. Findings include an archive `entry`, a one-based Markdown `line` when applicable, and a normalized `target` for missing links. The report contains paths and metadata; review them before sharing. Nuspec/Markdown bodies are not echoed.

| Exit | Meaning |
| --- | --- |
| `0` | No errors; warnings permitted unless `--strict` |
| `1` | Inspection errors, or warnings with `--strict` |
| `2` | Invalid arguments, unreadable/missing file; message on stderr, no report |

The tool checks neither heading anchors, remote URLs, HTML links, full nuspec schema/SemVer validity, signatures, API compatibility, binary provenance nor vulnerabilities. It complements existing validators; [research and alternatives](docs/RESEARCH.md) explain the scope. No AI service or key is needed.

## Project information

- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Contributing and validation](CONTRIBUTING.md)
- [Bounded roadmap](ROADMAP.md)
- [Changelog](CHANGELOG.md)
- [MIT license](LICENSE) and [third-party notices](THIRD-PARTY-NOTICES.md)
