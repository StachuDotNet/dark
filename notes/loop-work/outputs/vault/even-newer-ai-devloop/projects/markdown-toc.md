---
title: markdown-toc
tier: S
class: app
modules: [Stdlib.String, Stdlib.Cli, Stdlib.Regex, Stdlib.List]
languages: [dark, ts, py, go, rust]
expected_outcome: pass
known_blockers: []
framework_hint: null
core: true
---

# Description

A command-line tool that emits a Markdown table-of-contents for a given Markdown file. The user passes a file path (or pipes content through stdin); the program prints a TOC block to stdout.

The TOC uses `-` bullets, with indentation matching the header depth (`#` = depth 1, `##` = depth 2, etc.). Each bullet is a Markdown link of the form `[Header Title](#anchor)`. Anchors follow the GitHub-flavored convention: lowercase, spaces become hyphens, punctuation stripped, leading/trailing whitespace trimmed.

Both ATX-style headers (lines starting with one or more `#`) and Setext-style headers (a line of text followed by `===` for h1 or `---` for h2) are recognized.

Headers inside fenced code blocks (between ` ``` ` lines) are *not* included — code-block content is intentionally invisible to the TOC. Headers in indented code blocks (4-space indent) are similarly excluded.

The program reads no other files and writes none. Stdout receives the TOC; stderr receives errors.

# Behaviours

- `markdown-toc --file <path>` reads the file at `<path>` and prints a TOC; exits 0.
- `cat file.md | markdown-toc` reads stdin (no `--file`) and prints a TOC.
- A single `# Top` header produces `- [Top](#top)`.
- A document with `# A` and `## B` produces a TOC where `B` is indented one level under `A`.
- Header text with spaces becomes a hyphen-separated anchor: `## Hello World` → anchor `hello-world`.
- Header text with punctuation strips it from the anchor: `## What's New?` → anchor `whats-new`.
- A Setext-style h1 (text followed by `===`) is treated identically to `# `.
- A Setext-style h2 (text followed by `---`) is treated identically to `## `.
- Headers inside ` ``` `-fenced code blocks are *not* in the TOC.
- Non-ASCII header text (e.g. `## Café`) produces an anchor that includes the lowercase letters; non-ASCII alphanumerics are kept (not transliterated): `## Café` → anchor `café`.
- A document with no headers produces empty stdout and exits 0.
- `markdown-toc` with no args and no piped stdin shows usage and exits non-zero.
- `markdown-toc --file /no/such/path` exits non-zero with a clear file-not-found error.
- `markdown-toc --help` exits 0 with usage.

# Self-verification

Before declaring `<phase>DONE</phase>`, the agent itself runs through:

1. `markdown-toc --file README.md` (or any markdown file with multiple headers) — output reflects actual headers, in order, indented by depth.
2. Build a fixture file with a header inside a fenced code block; run `markdown-toc --file <fixture>` — code-block headers are excluded.
3. `markdown-toc --file empty.md` (empty file) → empty output, exit 0.
4. `markdown-toc --file /tmp/not-here.md` → exit non-zero, error mentions the path.
5. UTF-8 test: a header `## Crème brûlée` produces the anchor `crème-brûlée` (non-ASCII chars preserved, GitHub-renderer-compatible).
6. Record uncertainty in `SUMMARY.md` per the reflection template.

# Smoke commands (pre-rubric sanity)

- markdown-toc --file README.md
- echo "# A\n## B" | markdown-toc
- markdown-toc
- markdown-toc --file /no/such/path
- markdown-toc --help

---

**Role**: §6 #12 (first-parse-success attempts) channel. Tests `Stdlib.Regex` + `Stdlib.String` integration on UTF-8 input. The byte-vs-codepoint pitfall is a known Dark gotcha (per `word-count` in original 10 starter projects). Rubric verifies anchor generation byte-for-byte — if Dark's String functions over-strip non-ASCII, rubric catches it.
