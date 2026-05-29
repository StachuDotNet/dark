# parallel-downloader

**Goal:** Download multiple URLs concurrently (bounded concurrency) and write each response body to a file, completing materially faster than a sequential run.

**Kind:** greenfield

## Acceptance criteria
- [ ] `parallel-downloader urls.txt` reads URLs one per line, downloads each, writes them to `./<basename>` files, and exits 0.
- [ ] `cat urls.txt | parallel-downloader` reads URLs from stdin when no positional arg is given.
- [ ] `parallel-downloader --concurrency 5 urls.txt` caps simultaneous requests at 5.
- [ ] Wall-clock parallelism: 10 URLs each behind a 200 ms delay complete in roughly 250–500 ms total (materially less than 2000 ms).
- [ ] Running with `--concurrency 1` takes about 10× longer than `--concurrency 10`, confirming the cap is honored.
- [ ] HTTP errors (4xx/5xx) are reported per-URL on stderr without aborting the rest.
- [ ] Empty input (no URLs) exits 0 with no output.
- [ ] `parallel-downloader --help` exits 0.
