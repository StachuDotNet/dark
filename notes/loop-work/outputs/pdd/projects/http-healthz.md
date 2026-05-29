# http-healthz

**Goal:** Serve `GET /healthz` returning a JSON status document with status, uptime, and version.

**Kind:** greenfield

## Acceptance criteria
- [ ] The server starts on a free port within a few seconds.
- [ ] `GET /healthz` returns 200 with valid JSON `{"status":"ok","uptimeMs":N,"version":"<sha>"}`.
- [ ] `uptimeMs` increases between two successive requests.
- [ ] `GET /` (or any unknown route) returns 404.
- [ ] `http-healthz --help` prints usage and exits 0.
