# Design and limits

## Inspection pipeline

1. Open one read-only file handle. Reject files above 512 MiB, compute SHA-256, then rewind the same handle.
2. Read ZIP metadata, with at most 10,000 entries. Reject rooted paths, dot/traversal segments, empty interior segments, control characters, Windows path separators/reserved punctuation, exact duplicates and case collisions. No entries are extracted.
3. Require exactly one root `.nuspec`. Read bounded bytes and parse XML with DTD processing prohibited and the resolver disabled. Require one metadata block, a nonblank ID/version and no duplicate ID/version/repository/readme fields. This is a deliberately small structural check, not a complete nuspec validator.
4. Compare explicitly provided metadata expectations. Verify the declared readme's archive path. Missing optional commit/readme metadata produces warnings; explicit mismatches are errors.
5. Parse all bundled `.md` files with Markdig's CommonMark parser, in ordinal path order. Resolve link/image/reference-link destinations against their source directory and the archive index. Return diagnostics without modifying the package.

The inspector and command parser are separate C# classes. The CLI has no network client, subprocess execution or package-install code. Markdig 1.6.0 is the runtime dependency used to avoid treating fenced examples as real links. Its license is included in the distribution.

## Local URL contract

Empty destinations, fragment-only links, URLs with an absolute URI scheme, protocol-relative URLs and site-root links starting with `/` are skipped. No remote endpoint is contacted, and a site-root link is not treated as a package path.

For other URLs, remove a literal query/fragment suffix, percent-decode the path once, resolve `.` and `..`, and reject traversal above the archive root or unsupported path characters. Query-only links refer to the current document and need no target check. Directory destinations resolve against explicit ZIP directories or implicit parent directories; a trailing slash cannot turn a regular file into a directory. Paths are compared ordinally, case-sensitively, even on Windows. Encoded spaces and ordinary Unicode are supported; symlinks are not followed. ZIP entries that happen to represent symlinks are treated as opaque entries. Unlike Markdown URLs, the nuspec readme value is a literal archive path: `%` and `#` are preserved, backslashes are normalized to `/`, and a leading `./` is accepted.

Only CommonMark link nodes are inspected. Raw HTML, custom documentation extensions, autolink-like prose outside CommonMark, web availability and anchor/heading existence are outside this contract. Broken links to files outside the package need either bundled targets or absolute web URLs. Editing the source README alone does not fix an already-packed archive.

## Resource bounds and errors

Text entries (nuspec plus Markdown) are limited to 2 MiB each and 16 MiB total decompressed bytes. There is also a 100,000 local-link limit. Both declared and observed decompressed text lengths are checked. Binary payloads are indexed but not decompressed or executed; archive hashing reads compressed bytes. A large valid package may be refused by these inspection limits.

These limits bound common workloads; they are not a sandbox or a general malicious-archive security guarantee. The ZIP central directory is processed by the .NET ZIP reader before its entry count can be checked. Keep external process/time/memory limits for untrusted inputs. Do not modify the input concurrently; the report's hash describes the bytes read, not a filesystem snapshot guarantee across concurrent writes on all platforms.

Invalid archive data yields `NRC001` and exit 1. File access and argument failures yield exit 2. A JSON report is emitted only after an inspection returns; invalid input arguments do not produce a partial JSON object. Diagnostic codes and exit meanings are stable in 0.x; `schemaVersion` marks breaking JSON schema changes. New diagnostic rows may be added.

## Trust boundary

An expected commit comes from the caller. Matching self-reported nuspec metadata cannot establish binary provenance. SHA-256 identifies the inspected file; it is not an authenticity claim. Neither full NuGet schema validation nor signature, dependency, malware or API compatibility analysis is performed. See [alternatives](RESEARCH.md) for complementary tools and [the roadmap](../ROADMAP.md) for the fixed completion boundary.
