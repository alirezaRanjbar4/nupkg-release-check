# Required files and SHA-256 policy

This workflow is available in version 0.2.0. The earlier 0.1.0 tool does not accept `--policy`.

## Contract

Pass `--policy policy.json` after the package path. The entire policy is validated before the package is opened. Use a strict UTF-8 JSON object, optionally with a UTF-8 BOM:

```json
{
  "schemaVersion": 1,
  "requiredFiles": [
    { "path": "README.md" },
    {
      "path": "tools/net10.0/any/MyTool.dll",
      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    }
  ]
}
```

The example digest is for **zero bytes**, not an actual DLL. Replace it with a trusted expected digest. Omit `sha256` to require existence without decompressing that file. Required paths refer to files, not explicit or implicit directories. Extra archive files remain permitted.

Field names are case-sensitive. All fields shown are required except `sha256`. Unknown or duplicate fields (including escaped duplicate names), unsupported schema versions, null/wrong types, comments and trailing commas are rejected. `requiredFiles` needs 1–1,000 objects. Each `path` needs 1–1,024 characters and the archive's safe path grammar: relative `/`-separated nonempty segments, no `.`/`..`, leading/trailing slash, backslash, control characters or `<>:|?*`. Duplicate paths and case collisions are rejected. Paths are literal: spaces, Unicode, `%` and `#` are preserved, without URL decoding or normalization. Matching archive paths is case-sensitive on every OS. A digest needs exactly 64 hexadecimal characters, ignoring hex letter case; `null` is invalid.

The policy is at most 256 KiB, with JSON depth at most 8. Hashing streams only explicitly requested files: at most **64 MiB per file** and **256 MiB total** decompressed bytes, with both declared and observed lengths checked. Missing entries and existence-only requirements consume no hash budget. These bounds are separate from nuspec/Markdown limits, so a requested Markdown file can be read once for hashing and again for text inspection. No extraction, execution, download or network lookup occurs.

## Diagnostics and evidence

| Result | Code / exit |
| --- | --- |
| Missing required file, wrong case or directory in place of file | `NRC008`, exit 1 |
| File bytes differ from the supplied digest | `NRC009`, exit 1 |
| Requested hashing exceeds decompression bounds | `NRC010`, exit 1 |
| Invalid/unreadable policy | stderr, exit 2, no JSON report |

The existing `--strict` warning behavior and other checks still apply. Policy diagnostics use the requested archive path and do not echo file bodies or expected/observed entry digests. JSON report schema 1 adds `policySha256` (hash of the original policy bytes, including BOM/whitespace), `requiredFilesChecked` (requirements visited, including missing/limited ones) and `fileHashesChecked` (completed entry hashes, including mismatches). Without a policy these are `null`, `0` and `0`. Inspection can stop early on an invalid archive or existing resource limit; counts describe work actually performed and do not imply all checks completed.

Supply the policy from your trusted release inputs. Generating expected hashes from the suspect archive and immediately accepting them cannot detect stale payloads. Neither a policy digest nor matching self-reported nuspec commit establishes who built a DLL, authenticity or binary provenance. ZIP entries representing symlinks are opaque bytes; existence/hash checks do not establish filesystem type or follow a target. External process/memory/time limits remain appropriate for untrusted archives. Inputs must not be modified concurrently.

## Runnable reproduction

From the repository root with the .NET 10 SDK:

```sh
dotnet run --project examples/Demo -- artifacts/policy-demo
dotnet run --project src/Cli -- artifacts/policy-demo/good.nupkg --policy artifacts/policy-demo/policy.json --strict --format json
dotnet run --project src/Cli -- artifacts/policy-demo/changed.nupkg --expect-commit aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa --policy artifacts/policy-demo/policy.json --format json
dotnet run --project src/Cli -- artifacts/policy-demo/missing.nupkg --policy artifacts/policy-demo/policy.json --format json
```

The good archive exits 0; changed and missing payloads exit 1 with `NRC009` and `NRC008`. All retain exactly the same nuspec commit metadata, and both negative policy examples pass default inspection when `--policy` is omitted. The fixture DLL is inert text, never executed. The generator refuses existing destinations. See [examples](../examples/README.md), [design](DESIGN.md) and [troubleshooting](TROUBLESHOOTING.md).
