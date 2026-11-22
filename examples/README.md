# Synthetic examples

`Demo` creates minimal archive inspection fixtures, not installable NuGet packages. Both declare `Demo.Tool`, version `1.2.3`, Git commit `aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa`, and a README linking `docs/guide.md`. Only `good.nupkg` includes that guide.

From the repository root, `dotnet run --project examples/Demo -- artifacts/demo` writes both files. Existing files are refused; choose a fresh directory for another run. The good fixture passes `--strict`; the bad fixture returns `NRC006` at README line 3 and exit 1. No fixture includes private data.

After packing the CLI, run `pwsh -File examples/Smoke.ps1`. It creates an isolated installation/fixture directory under `artifacts`, checks 0/1/2 exits, parses JSON, checks SHA-256/source preservation and inspects the final CLI package against the current Git commit. This is also used by [CI](../.github/workflows/ci.yml). See [contributing](../CONTRIBUTING.md) for the complete sequence.
