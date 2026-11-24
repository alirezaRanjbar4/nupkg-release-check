# Contributing

Open a focused issue with a synthetic `.nupkg` reproduction or a test that builds one. Keep private packages, credentials and company code out of reports. Propose changes within the [roadmap](ROADMAP.md), and read the [design](docs/DESIGN.md) and [troubleshooting guide](docs/TROUBLESHOOTING.md).

With the .NET 10 SDK, run from the root:

```sh
dotnet restore
dotnet test -c Release --no-restore
dotnet format --verify-no-changes --no-restore
dotnet pack src/Cli -c Release -o artifacts
pwsh -File examples/Smoke.ps1
```

The smoke script needs PowerShell 7 on Linux/macOS (included in GitHub-hosted runners). It installs the locally packed tool into a fresh location, runs positive/negative synthetic fixtures and validates the final package against the current Git commit. **Commit your intended source and repack first** for that last assertion; it is expected to fail for a stale archive. Do not change timestamps or create empty commits to affect activity graphs.

Tests construct archives in isolated temporary directories and cover malformed/ambiguous XML, archive paths, metadata expectations, CommonMark resolution, limits, read-only behavior and exit/JSON contracts. Add meaningful regression tests when changing those behaviors. Three-platform CI repeats tests, formatting, packaging and installed-tool smoke checks.

Policy regressions also cover strict schema/duplicate fields and paths, exact file matching, unchanged-commit/changed-payload detection, original policy-byte hashes and 64 MiB/256 MiB streaming boundaries. Read the [policy contract](docs/POLICY.md) before changing these additive JSON fields or limits. The installed smoke uses version 0.2.0 and a fresh package cache with the local feed; the earlier 0.1.0 package has no policy option.

Keep diagnostics value-light and deterministic, and avoid network access, subprocess execution or extraction in the inspector. Update the changelog/docs when observable behavior changes. The project uses the [MIT license](LICENSE); bundled dependency notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
