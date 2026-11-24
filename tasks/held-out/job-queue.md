Build an HTTP job queue with a worker pool. `POST /jobs` with `{"kind": "sleep", "seconds": N}` or `{"kind": "fib", "n": N}` queues a job and answers 202 with its id. Up to 3 jobs run at the same time; the rest wait in order. `GET /jobs/<id>` shows the job's state (queued, running, done, failed or cancelled), its timings and, when done, its result. `DELETE /jobs/<id>` cancels a queued or running job. `GET /stats` shows how many jobs are in each state. A job that runs longer than 30 seconds is failed with a timeout. Invalid job bodies answer 400. Organise it as an HTTP layer, a queue and the workers, with tests for the queue's ordering and the concurrency limit.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** larger (a few modules, about 200+ lines)
- **Kind:** HTTP server, long-running job, concurrency; multi-module project
- **Areas:** HTTP server, JSON, dicts, lists, concurrency, tests
- **Network sites:** none (localhost only)
- **Seed:** none

## Acceptance checks

Start it on port 18084.

1. Five `sleep` jobs of 3 seconds posted at once: within a second, 3 are running and 2 queued; all 5 are done by about 6 to 7 seconds.
2. A `fib` job with `n` 25 finishes with the result 75025.
3. `DELETE` on a queued job makes it cancelled and it never runs; `DELETE` on a running `sleep 20` job makes it cancelled within 2 seconds, and its slot is used by the next job.
4. A `sleep 40` job is failed with a timeout at about 30 seconds.
5. `{"kind": "nope"}` and `{"kind": "fib"}` answer 400.
6. `GET /stats` counts match the states seen.
7. While 3 long jobs run, `GET /stats` still answers in under a second.
8. HTTP layer, queue and workers are separate parts; the tests run with one command and pass.
