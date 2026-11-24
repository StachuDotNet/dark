Build a command-line backup tool. `backup <source-dir> <dest-dir>` copies every `.txt` file directly inside the source folder into the destination folder, creating the destination when it is missing. A file whose content is the same as the copy already in the destination is skipped. Files larger than the number of bytes in the environment variable `BACKUP_MAX_BYTES` (1000000 when it is not set) are skipped and reported. Each run appends one line to `<dest-dir>/backup.log` with the time, the number of files copied and the number skipped. A missing source folder is an error with exit code 1.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** small to medium
- **Kind:** new command-line tool; files in two folders, environment, clock
- **Areas:** files, directories, environment variables, clock, permissions scoped to two folders, exit codes
- **Network sites:** none
- **Seed:** none

## Acceptance checks

1. With `a.txt`, `b.txt` and `c.md` in `src`, `backup src dst` creates `dst` with `a.txt` and `b.txt` only, byte-identical, and exits 0.
2. A second run copies nothing and skips 2; after changing `a.txt`, a third run copies 1 and skips 1.
3. `dst/backup.log` has three lines, each with a time and the two counts.
4. With `BACKUP_MAX_BYTES=3` and a 10-byte `big.txt`, `big.txt` is skipped and named in the output.
5. `backup missing dst` exits 1 with a message on stderr.
