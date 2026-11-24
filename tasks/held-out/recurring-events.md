Build a command-line tool that lists upcoming occurrences of recurring events. Events are in a text file, one per line, as `<name> | <rule> | <time>`, where the rule is one of: `daily`, `weekly <weekday>` (e.g. `weekly mon`), `every <N> weeks <weekday> from <YYYY-MM-DD>`, `monthly <day>` (day 1 to 31; months without that day are skipped), or `last <weekday> of month`. The time is `HH:MM`. `agenda FILE --from YYYY-MM-DD --days N` prints every occurrence in those N days in time order, grouped under a heading per date that shows the date and weekday name. `next FILE NAME` prints the next occurrence of one event after a `--from` date. A line that can't be parsed is reported with its line number and skipped.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** dates and scheduling, CLI tool
- **Areas:** dates and weekdays, parsing, sorting, strings, files
- **Network sites:** none
- **Seed:** none

## Acceptance checks

Use this file:

```
standup | daily | 09:30
review | weekly fri | 15:00
retro | every 2 weeks wed from 2026-01-07 | 16:00
rent | monthly 31 | 08:00
board | last thu of month | 10:00
bad line here
```

1. `agenda --from 2026-01-26 --days 14` (Mon 26 Jan to Sun 8 Feb): standup every day; review on 30 Jan and 6 Feb; retro on 4 Feb (not 28 Jan); rent on 31 Jan; board on 29 Jan; each date heading has the right weekday.
2. Within a day, entries are in time order (rent 08:00 before standup 09:30).
3. `agenda --from 2026-02-01 --days 28` has no rent (February has no 31st) and board on 26 Feb.
4. `next ... rent --from 2026-04-01` gives 2026-05-31.
5. Line 6 is reported as unparseable and the rest still works.
