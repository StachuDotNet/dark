Build a small web server that answers `GET /` with an HTML page saying "Hello, visitor" and `GET /time` with the current time as JSON. Then, with the server still running and without restarting it, change the greeting to "Welcome back" and add a new route, `GET /version`, that answers `{"version": 2}`. Show that the running server picked up both changes.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** tiny (the code), but the point is the edit-while-running loop
- **Kind:** HTTP server; Dark strength: live serve while editing
- **Areas:** HTTP server, JSON, store/CLI authoring
- **Network sites:** none (localhost only)
- **Seed:** none
- **Note for Python:** the standard library has no auto-reload, so a fair Python answer is a server that reloads its handler module (for example with `importlib.reload` on change) or a framework that is already installed. Restarting the process doesn't count.

## Acceptance checks

1. The transcript shows the server's process started once (one PID, or one serve command) and answering before and after the edits.
2. After the edits, `curl -s localhost:<port>/` contains "Welcome back" and not "Hello, visitor".
3. `curl -s localhost:<port>/version` returns JSON whose `version` is 2.
4. `curl -s localhost:<port>/time` still returns JSON with a time in it.
5. The checker restarts the server from the final code and checks 2 to 4 again.
