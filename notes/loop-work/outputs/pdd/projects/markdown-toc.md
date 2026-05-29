# markdown-toc

**Goal:** Emit a Markdown table-of-contents block for a given Markdown file, with GitHub-flavored anchors.

**Kind:** greenfield

## Acceptance criteria
- [ ] `markdown-toc --file <path>` reads the file and prints a TOC; exits 0.
- [ ] `cat file.md | markdown-toc` reads from stdin when no `--file` is given.
- [ ] A single `# Top` header produces `- [Top](#top)`.
- [ ] Headers are indented by depth (`#` = level 1, `##` = level 2, …), with each entry a `[Header Title](#anchor)` link.
- [ ] Anchors follow the GitHub convention: lowercase, spaces become hyphens, punctuation stripped (`## What's New?` → `whats-new`).
- [ ] Both ATX-style (`#` prefixed) and Setext-style (text followed by `===` for h1, `---` for h2) headers are recognized.
- [ ] Headers inside fenced or 4-space-indented code blocks are excluded from the TOC.
- [ ] Non-ASCII header text keeps its lowercased non-ASCII alphanumerics (no transliteration): `## Café` → anchor `café`.
- [ ] A document with no headers produces empty stdout and exits 0.
- [ ] No args and no piped stdin shows usage and exits non-zero.
- [ ] `--file /no/such/path` exits non-zero with a clear file-not-found error.
- [ ] `markdown-toc --help` prints usage and exits 0.
