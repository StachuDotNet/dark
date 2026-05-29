# hello-cli

**Goal:** Print `Hello, <name>!` where `<name>` is the first positional argument.

**Kind:** greenfield

## Acceptance criteria
- [ ] `hello-cli World` prints exactly `Hello, World!\n` to stdout and exits 0.
- [ ] A quoted argument with a space (`hello-cli "Jane Doe"`) prints `Hello, Jane Doe!\n`.
- [ ] With no argument, exits non-zero with a usage line on stderr (e.g. `Usage: hello-cli <name>`).
- [ ] An empty-string argument (`hello-cli ""`) prints `Hello, !\n` — an empty name is accepted, not an error.
- [ ] A non-ASCII name (e.g. `hello-cli "Älice"`) is printed byte-correctly and exits 0.
- [ ] `hello-cli --help` (or `-h`) prints usage and exits 0.
- [ ] The program reads no stdin and writes no files; stdout receives only the greeting line, stderr only errors.
