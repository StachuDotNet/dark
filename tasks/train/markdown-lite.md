Build a converter from a subset of Markdown to HTML. It reads Markdown from a file or standard input and writes HTML to standard output. Support: `#` to `######` headings; paragraphs separated by blank lines; `*emphasis*`, `**strong**` and `` `code` ``; links `[text](url)`; unordered lists with `-` and ordered lists with `1.`; fenced code blocks with three backticks, whose content is not processed; and block quotes with `>`. Characters `<`, `>` and `&` in text are escaped. Anything else is treated as plain text. Organise it as a block parser and an inline parser, with tests for each feature.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** larger (about 200+ lines with tests)
- **Kind:** text parsing; library with tests
- **Areas:** strings, lists, tests; also stdin and files
- **Network sites:** none
- **Seed:** none

## Acceptance checks

Compare after collapsing whitespace between tags.

1. `# Title` gives `<h1>Title</h1>`; `###### x` gives `<h6>x</h6>`; `####### x` is a paragraph.
2. ``a *b* **c** `d` `` gives `<p>a <em>b</em> <strong>c</strong> <code>d</code></p>`.
3. `[Dark](https://darklang.com)` gives `<a href="https://darklang.com">Dark</a>`.
4. Two `- ` lines give one `<ul>` with two `<li>`; two `1. ` lines give one `<ol>`.
5. A fenced block containing `*not em* <tag>` gives `<pre><code>*not em* &lt;tag&gt;</code></pre>`.
6. `> quoted` gives a `<blockquote>` holding a paragraph; `a < b & c` is escaped in a paragraph.
7. Two paragraphs separated by a blank line give two `<p>` elements.
8. A block parser and an inline parser are separate parts; the tests run with one command and pass.
