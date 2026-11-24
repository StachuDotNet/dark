Build a command-line tool that analyses web server access logs in the Common Log Format with one extra field at the end, the response time in milliseconds:

    203.0.113.9 - - [10/Mar/2026:13:55:36 +0000] "GET /api/items?id=3 HTTP/1.1" 200 512 37

Given one or more log files, it reports: the number of requests; the count per status class (2xx, 3xx, 4xx, 5xx); the 10 most requested paths (ignoring query strings) with their counts; requests per hour of the day; and the median and 95th-percentile response time, overall and for the top 5 paths. `--since` and `--until` (ISO date-times) limit the time range. `--json` prints the same report as JSON. Malformed lines are counted and reported, not fatal. Organise the code as a parser, an aggregator and report formatters, with tests for the parser and the percentile calculation.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** larger (a few modules, about 200+ lines)
- **Kind:** data processing, text parsing; multi-module project
- **Areas:** strings, lists, dicts, JSON, files, permissions, tests; also dates
- **Network sites:** none
- **Seed:** none. The checker generates its logs and the expected report with `seeds/log-analyzer-check/gen.py`.

## Acceptance checks

Run `python3 seeds/log-analyzer-check/gen.py OUTDIR`. It writes `access.log` (10,000 lines, 20 malformed), `small.log` (200 lines) and `expected.json`, computed independently of the tool.

1. Request count, malformed count and status-class counts match exactly.
2. The top-10 paths and their counts match, with query strings ignored.
3. Median and p95 overall match the reference to the millisecond, using the nearest-rank method; if the tool uses another method, its summary must say which, and its numbers must match that method.
4. `--since 2026-03-10T14:00:00 --until 2026-03-10T15:00:00` limits every figure to that hour.
5. `--json` output parses and holds the same numbers as the text report.
6. Two files given together give the sum of their separate reports.
7. 10,000 lines take under 10 seconds.
8. Parser, aggregator and formatters are separate parts; the tests run with one command and pass.
