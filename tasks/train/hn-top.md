Build a command-line tool that prints the current top N stories on Hacker News (default 10), using the public API at https://hacker-news.firebaseio.com/v0/. For each story print its rank, score, title and comment count, then sort the output by score, highest first. Fetch the stories at the same time rather than one after another, and print how long the fetching took. A story that fails to load is reported and skipped.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** HTTP client against a public API
- **Areas:** HTTP client, JSON, lists, concurrency, permissions
- **Network sites:** `hacker-news.firebaseio.com`
- **Seed:** none

## Acceptance checks

1. `<tool>` prints 10 stories with rank, score, title and comment count, sorted by score descending.
2. `<tool> 30` prints 30.
3. The titles match `curl -s https://hacker-news.firebaseio.com/v0/topstories.json` fetched at the same time (allow a few to move).
4. The fetches overlap: with 30 stories the reported fetch time is well under 30 times the time of one item fetch, or the transcript shows they were issued together.
5. A story id that returns `null` (job posts sometimes do) doesn't crash the tool.
