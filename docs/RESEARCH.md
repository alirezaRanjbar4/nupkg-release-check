# Problem and existing tools

Reviewed on 2026-11-22. The immediate use case came from releasing a small .NET tool: a packed README linked to documentation omitted from the archive, and a package packed before the final commit could report an earlier source commit. Both are release-artifact questions that a source-tree documentation check alone cannot answer.

Primary sources considered:

- [.NET package validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/overview) covers API/runtime/framework compatibility checks.
- [`dotnet nuget verify`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-verify) verifies package signatures.
- [Meziantou.Framework NuGetPackageValidation](https://github.com/meziantou/Meziantou.Framework/blob/main/src/Meziantou.Framework.NuGetPackageValidation/readme.md) and its [CLI](https://github.com/meziantou/Meziantou.Framework/blob/main/src/Meziantou.Framework.NuGetPackageValidation.Tool/readme.md) provide a broader best-practice validator, including readme/repository metadata rules and symbol/Source Link checks.
- [Markdig](https://github.com/xoofx/markdig) provides CommonMark parsing; [NuGet's 1.6.0 page](https://www.nuget.org/packages/Markdig/1.6.0) identifies the dependency used here.

The inference from those documented scopes and the observed packaging failure is that a narrow offline check of caller-supplied release expectations plus bundled Markdown target closure is useful. This is not a claim that no other tool can do it. Use the existing validators for their broader checks; this tool does not replace them.

The initial implementation has no paid service, credential, training data or AI dependency. It occupies a general .NET tooling workstream alongside separate AI/data/training projects. The [roadmap](../ROADMAP.md) fixes its small first completion scope before subsequent feature work.
