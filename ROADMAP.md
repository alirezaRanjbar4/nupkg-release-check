# Bounded roadmap

The first completion scope was fixed on 2026-11-22. A usable initial release ships before the follow-up feature. Completion means the following small workflow is implemented, documented and validated; it does not mean solving all NuGet quality checks.

- [x] Offline `.nupkg` archive/manifest checks, explicit identity/commit expectations, CommonMark package-local links, JSON/text diagnostics and bounded read-only processing.
- [x] Synthetic positive/negative examples, tests and design/troubleshooting/alternative-tool documentation.
- [ ] Verified v0.1.0 GitHub Release, isolated installed-tool smoke checks and Windows/Linux/macOS CI.
- [ ] Versioned JSON release policy for required archive files and optional expected SHA-256 for those files. Validate strict policy input, report absent/changed payloads without extraction, and include useful positive/negative policy fixtures. File hashing must retain explicit decompression limits. This checks supplied expectations, not binary provenance.
- [ ] Validated closing release containing that policy workflow, complete bundled documentation and final-commit/tag/asset verification.

After this scope, keep the repository public and maintain reproducible bugs. Optional expansion does not reopen the completed scope. No network/link crawling, heading-anchor checks, HTML parsing, signature verification, API comparison, AI service or package publishing is required. Those areas have different contracts and often existing tools.

See [research](docs/RESEARCH.md) for the observed problem and alternatives, and [changelog](CHANGELOG.md) for delivered behavior. A checkbox means delivered behavior; uncompleted items are not advertised as available CLI options.
