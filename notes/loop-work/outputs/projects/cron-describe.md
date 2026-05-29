# cron-describe

**Goal:** Translate a 5-field cron expression into a one-line plain-English description.

**Kind:** greenfield

## Acceptance criteria
- [ ] Supports literal numbers, ranges (`1-5`), step values (`*/15`), comma lists (`15,30,45`), and the wildcard `*`; day-of-week accepts numeric (`0`–`6`) and short-name (`mon`, `tue`, …) forms.
- [ ] `cron-describe "*/5 * * * *"` produces output containing "every 5 minutes".
- [ ] `cron-describe "0 9 * * *"` produces output containing "09:00" or "9:00 AM".
- [ ] `cron-describe "0 9 * * 1-5"` produces output containing both the time and "weekdays" (or "Monday through Friday").
- [ ] `cron-describe "15,30,45 * * * *"` names all three minute values: 15, 30, 45.
- [ ] `cron-describe "0 0 1 * *"` references midnight (or 00:00) and the 1st (day-of-month).
- [ ] `cron-describe "* * * * *"` produces output containing "every minute".
- [ ] `cron-describe "0 0 1 1 *"` references both January and the 1st.
- [ ] A 4-field or 6-field input exits non-zero with a clear error mentioning the field count.
- [ ] A non-numeric character in a numeric field exits non-zero with a clear error.
- [ ] Extra positional arguments error rather than being silently ignored.
- [ ] Extended formats (Quartz seconds field, `@yearly`-style shortcuts) are out of scope and fail cleanly.
- [ ] `cron-describe --help` (or `-h`) prints usage and exits 0.
