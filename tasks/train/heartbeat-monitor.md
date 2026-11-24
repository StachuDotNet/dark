Build a monitoring job that runs until stopped. Every N seconds (`--every N`) it checks each URL given on the command line and appends one line per check to a log file (`--log FILE`): the time, the URL, up or down, and the status code or error. When a URL has been down for 3 checks in a row, it prints an alert to stderr once, and prints a recovery message when it comes back up. A `--checks K` option stops it after K rounds, so it can be tested. Stopping it with Ctrl-C leaves the log file complete (no half-written line).

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** scheduled / long-running job, HTTP client
- **Areas:** HTTP client, files, permissions, concurrency (timers); also time
- **Network sites:** `github.com`
- **Seed:** none

## Acceptance checks

1. `<tool> --every 2 --checks 4 --log hb.log https://github.com/darklang/dark https://github.com/darklang/no-such-repo-bench-404` takes about 6 to 8 seconds and writes 8 lines to `hb.log`, each with a time, the URL and up or down.
2. The 404 URL is down on every line, and exactly one alert for it appears on stderr, after the third check.
3. The first URL is never alerted.
4. Start it without `--checks`, wait 5 seconds, press Ctrl-C (or send SIGINT): it exits, and every line in the log is complete.
5. Consecutive checks of the same URL are N seconds apart, give or take a second; the schedule doesn't drift by the time each request takes.
