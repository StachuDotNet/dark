Build an HTTP JSON API for a small shop's stock. An item has a SKU, a name, a quantity and a price in cents. `POST /items` creates one (201; 409 if the SKU exists). `GET /items` lists items sorted by SKU; `GET /items?below=N` lists only items whose quantity is below N. `GET /items/<sku>` returns one item. `POST /items/<sku>/adjust` with `{"delta": N, "reason": "..."}` changes the quantity and records the change; a change that would make the quantity negative answers 409 and changes nothing. `GET /items/<sku>/history` lists that item's changes, oldest first, each with its delta, reason, the quantity after it and a timestamp. Bad JSON or missing fields answer 400 with `{"error": ...}`; an unknown SKU answers 404. Everything is kept on disk, so it survives a restart. The port is a command-line argument.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** HTTP server, JSON API, persistence
- **Areas:** HTTP server, JSON, dicts, lists, files, query strings, permissions
- **Network sites:** none (localhost only)
- **Seed:** none

## Acceptance checks

Start the server on port 18091.

1. `POST /items` for `B-2` (qty 5) and `A-1` (qty 1) answer 201; a second `A-1` answers 409.
2. `GET /items` returns `A-1` then `B-2`; `GET /items?below=3` returns only `A-1`.
3. Adjusting `B-2` by -2 then +10 gives quantity 13; its history has two entries with quantity-after 3 and 13.
4. Adjusting `A-1` by -5 answers 409 and its quantity stays 1.
5. `not json`, a create without `sku`, and an adjust without `delta` answer 400 with an `error` field; `GET /items/ZZZ` answers 404.
6. After a restart, `GET /items/B-2` shows 13 and its history still has both entries.
