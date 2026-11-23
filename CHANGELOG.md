# Changelog

## 0.2.0 — Unreleased

- Add opt-in strict schema-1 `--policy` JSON for exact required file paths and optional expected SHA-256, validated before package inspection.
- Detect absent/changed payloads with `NRC008`/`NRC009`, independently of self-reported nuspec commit metadata. Hash only requested entries with 64 MiB per-file/256 MiB total limits (`NRC010`), without extraction or execution.
- Add policy-byte SHA-256 and requirement/hash counts to JSON schema 1, preserving default policy-free inspection and 0/1/2 exits.
- Include synthetic changed/missing payload examples, strict-input and resource-bound regressions, installed-tool CI smoke and complete policy/design/troubleshooting documentation. A verified closing release remains on the roadmap.

## 0.1.0 — 2026-11-22

- Inspect the final `.nupkg` without extraction, installation or network calls.
- Assert package ID, literal version and a supplied full Git commit.
- Validate bounded root nuspec XML, archive paths and package-local CommonMark links/images/reference links, with one-based source lines.
- Emit stable `NRC001`–`NRC007` diagnostics in text or JSON, package SHA-256 and 0/1/2 exit contracts.
- Include synthetic examples, design/troubleshooting/research guides, automated tests and three-platform CI.

The JSON file policy is not part of 0.1.0; it is implemented in the unreleased source described above. See the [roadmap](ROADMAP.md) for closing-release acceptance.
