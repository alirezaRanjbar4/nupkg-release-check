# Synthetic examples

`Demo` creates minimal archive inspection fixtures, not installable NuGet packages. All declare `Demo.Tool`, version `1.2.3`, Git commit `aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa`, and a README linking `docs/guide.md`. `bad.nupkg` leaves out that guide; the other three include it.

From the repository root, `dotnet run --project examples/Demo -- artifacts/demo` writes four archives and `policy.json`. Existing destinations are refused; choose a fresh directory for another run. The good fixture passes `--strict`; the bad fixture returns `NRC006` at README line 3 and exit 1. No fixture includes private data.

With source 0.2.0, add `--policy artifacts/demo/policy.json`. `good.nupkg` passes; `changed.nupkg` contains different payload bytes (`NRC009`) and `missing.nupkg` omits the required payload (`NRC008`), each exiting 1. Both pass policy-free checks with unchanged commit metadata, demonstrating why metadata alone cannot catch omitted/stale binaries. The `.dll` fixture contains inert text and is never executed. See the [runnable policy commands and strict input contract](../docs/POLICY.md). This option is absent in the public 0.1.0 release.

After packing the CLI, run `pwsh -File examples/Smoke.ps1`. It creates an isolated installation/fixture directory under `artifacts`, checks 0/1/2 exits, parses JSON, checks SHA-256/source preservation and inspects the final CLI package against the current Git commit. This is also used by [CI](../.github/workflows/ci.yml). See [contributing](../CONTRIBUTING.md) for the complete sequence.
