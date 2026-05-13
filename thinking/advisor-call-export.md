# Darklang Advisor Call, 2026, May 10

- We got some good on-track work done, making Dark a better tool for AI to write software with
  - We've been building various projects with Agents+Dark, taking notes around problems faced, recording #s of tokens used, time-to-done, etc.
  - We've solved some of the direct pain points we've seen:
  - Introduced Blobs and Streams as new language/runtime features
    - Blobs
      - We used to hold all binary blobs as `List<UInt8>`
      - now we have a `Blob` type to support bags of bytes
      - Blobs are by default in-mem, and referred to by ID, until we save something that references them
      - then, we promote them to be persisted to a package_blobs table, referenced by hash
      - Per-request blob scope + finalizer cleanup ships. Cross-request retention (long-lived blobs in `package_blobs`) still TODO.
    - Streams
      - Context: had a hacky one-off streaming HttpClient for LLM responses; nothing else streamed.
      - Now: `Stream<'a>` is a first-class language type
      - Composable like a list: `map`, `filter`, `take`, `chunks`, `lines`. Nothing materialized until consumed.
      - Quick taste of composing one:

        ```
        // filter evens, +1, take first 3
        let s = Stdlib.Stream.fromList [1L; 2L; 3L; 4L; 5L; 6L; 7L; 8L; 9L; 10L]
        let evens  = Stdlib.Stream.filter s     (fun n -> n % 2L == 0L)
        let plus1  = Stdlib.Stream.map    evens (fun n -> n + 1L)
        let first3 = Stdlib.Stream.take   plus1 3L
        Stdlib.Stream.toList first3
        // = [3L; 5L; 7L]
        // only 3 elements ever pulled through the pipeline
        ```

      - Powers SSE: `Stdlib.Sse.parse : Stream<Bytes> -> Stream<SseEvent>` is itself a stream-to-stream transform — LLM responses stay lazy end-to-end, parser included.
      - `Stream.nextChunk` bulk-drain fast path keeps per-byte overhead manageable on hot HTTP paths.
      - Stream-based HTTP/SSE handling now just uses 'normal' streaming
    - Rest of codebase adjusted accordingly -- updated file IO, HTTP client+server, crypto/base64 stuff, etc.
    - Memory + wall-time wins on the operations agents actually hit:
      - `File.read` 10 MB: 1.96 GB → 31.5 MB allocated (≈60× less)
      - `File.read` 38 MB: used to OOM → 40 MB / 136 ms
      - HTTP body 10 MB: 1.82 GB → 36.9 MB allocated (≈50× less)
      - hex-encode 1 MB: 336 MB / 1.05 s → 14.6 MB / 45 ms (≈25× both)
  - HTTP servers are now possible to write+test wholly in Dark
    - BwdServer has been fully replaced by a single Builtin and a bunch of surrounding Dark code
    - Removed wholesale: `BwdServer/`, `LibService/`, `LibHttpMiddleware/`, the `canvases/dark-editor` and `canvases/dark-packages` trees. ASP.NET is fully out of the runtime graph; the new `serve` is built directly on `System.Net.HttpListener`. Most byte-exact `.test` HTTP fixtures pass unchanged after the rewrite; a handful diverge in spec-compliant ways from Kestrel (duplicate-header semantics, redirect handling).
    - Added: `serve` command
    - Demo — any router-shaped fn can be served, no F# rebuild between routers. Body-size cap, HSTS, `Server: darklang`, X-Forwarded-Proto canonicalization, multi-handler conflict detection all wired in.

      ```
      $ darklang serve Darklang.DemoData.HttpServerTest.router --port 8080
      Serving Darklang.DemoData.HttpServerTest.router on http://localhost:8080
      [HttpServer] GET /hello → 200 (3 ms)
      [HttpServer] POST /echo → 200 (12 ms)
      ^C
      Shutting down... done.
      ```

    - Source of the demo router — just Dark code, no F# in sight:

      ```
      module Darklang.DemoData.HttpServerTest

      let helloHandlerFn (req: Stdlib.Http.Request) : Stdlib.Http.Response =
        Stdlib.Http.responseWithText "Hello, World!" 200L

      let helloHandler: Stdlib.HttpServer.Handler =
        Stdlib.HttpServer.Handler
          { route = "/hello"; method = "GET"; handler = helloHandlerFn }

      let echoHandlerFn (req: Stdlib.Http.Request) : Stdlib.Http.Response =
        let bodyText = Stdlib.String.fromBlobWithReplacement req.body
        Stdlib.Http.responseWithText bodyText 200L

      let echoHandler: Stdlib.HttpServer.Handler =
        Stdlib.HttpServer.Handler
          { route = "/echo"; method = "POST"; handler = echoHandlerFn }

      let handlers = [ helloHandler; echoHandler ]

      let router (req: Stdlib.Http.Request) : Stdlib.Http.Response =
        Stdlib.HttpServer.routeRequest handlers req
      ```

  - we made several fixes to documentation, as AI ran into various related issues
    - Blob.toHex returns uppercase; undocumented; most APIs expect lowercase
    - Env vs Cli.File namespace inconsistency (Env at top, file ops under Cli)
    - Env.get returns Option, Cli.File.* returns Result — same semantics, different shapes
    - No Stdlib.Crypto.sha256Hex (sha256 returns Blob, toHex is uppercase)
    - String.random returns Result only because of negative length — should take UInt64
    - Builtin.* surfaces in search alongside Stdlib.* — confusing for new users
  - multi-line stdin support for fun/type/val
  - Typechecker: generic type-variable inference/scoping fixes, stdlib wrapper behavior
  - Package deps now location-aware (was hash-only) — fixes reverse-dep lookups when items share a hash
  - And made some improvements that should eventually improve the AI's UX
  - tracing expansion
    - `traces` subcommands: `list`, `tail`, `follow` (live), `view`, `find`, `replay`, `inspect`, `values`, `stats`, `hotspots`. Trace IDs accept any unambiguous prefix. `--json` everywhere for piping. Tab-completion, focused error messages, case-insensitive filters.
    - Built `view --with-trace` this month on a branch. `view <fn>` already pretty-prints source; `--with-trace` inlined recorded values next to every annotated AST node. Worked great — basically the dark-classic killer feature in text. Pulled it back out: per-AST-node value capture made traces too heavy on disk. Plan to revisit; the UX is what we want, the storage shape isn't.

      ```
      fun req ->
        let userId = req.url.params.id          // = "42"
        let user = Stdlib.DB.get userId         // = Some User { name="alice" }
        match user with
        | Some u -> Http.responseJson u         // = { status=200; ... }
      ```

    - `hotspots` command — top fns by total time across recent traces

      ```
      $ darklang traces hotspots
      Across last 100 traces (top 50 fns by total time):
        Stdlib.DB.get          152ms   38 calls
        Http.responseJson       41ms   42 calls
        Stdlib.String.split     12ms   91 calls
      ```

  - it's now possible to deprecate package items via the CLI
    - Deprecate/Undeprecate are now real SCM ops (not just metadata)
    - functions marked as `Harmful` halt at runtime — unless you flag 'harmful fns ok'
    - view/status/ls/search surfaces deprecations properly
  - We've also identified further things to fix to get better:
  - use fewer tokens, and get bulk of information in one or two 'turns' rather than spinning back and forth having AI fetch docs
    - TODO: detect pty, no coloration or logos or such
    - TODO: have the first doc referenced be mostly a directory of other docs to fetch. then allow the next agent to request _just those docs_ by some sort of ID
    - make 'dark tree' less verbose, no fancy characters
    - get rid of ansi logo
    - (detect when interactive, plaintext otherwise)
    - dark edit (diff-based); --replace/--with
    - auto-emit diagnostics after every write
    - --update required to overwrite existing fns
    - parse errors with suggested fixes (`,` vs `;`)
  - (Discovery) help agents find existing code faster
    - ranked dark search --json
    - "did you mean" on misses
    - --json across CLI commands; fix builtins --json bug
    - surface trace tools better (traces tail, replay --diff)
  - non-interactive commit
  - fix CLI string-escape issue
  - support better 'bulk editing'
    - still constrained to editing one thing at a time, for no good reason
  - resolve confusion with numbers and math
    - int confusion -- AI repeatedly confused about having to specify "L" suffix for our 'default' integer
      - TODO we need to support a flexible Int
      - Also: bare `5` against `5L` scrutinee silently falls through in match (no parse-time error)
    - basic math/operations on numbers are still annoying
      - parser locks down "+" to a specific type, etc.
  - "search" doesn't show sigs, and doesn't handle dotted paths
  - stderr not accessible 'natively'/comfortably -- only good control of stdout, not stderr
  - native test/constraint support
  - support `dark rename` with caller updates
  - support `dark uncommit/revert`
  - persist branch context across CLI commands, somehow. (while also allowing concurrent agents to work on different branches)
    - `--branch` must be repeated on every invocation; no session context (fix: `dark use-branch <name>`)
  - `run @fn` auto-prints return value (incl. `()` for Unit) — fns can't both print AND return cleanly
  - Make agents reach for Dark's existing trace surface
    - auto-attach trace on failing run
    - run --replay <id> shorthand
    - dark traces gen-test <trace-id> generates regression tests from a trace
  - improve CLI performance; AI tells us that the CLI takes too long to start up
    - maybe CLI daemon
    - native AOT
  - we don't have a good way of managing what should/shouldn't be included in a commit.
  - language/runtime level support for concurrency, async
  - Our routine for auditing AI's ability to use Dark as _the_ tool to write software, compared to other tech, is roughly:
    - (pre-step) define task and passing criteria
    - spawn AI agents to build the project with Dark, as well as several other languages (python, typescript, go)
    - compare success/failure, token usage, turns used, wall time, etc
    - review conversation, extract confusion+failure points, etc.
    - All implementations are currently thrown away at the end, in a branch. we should do a better job of extracting any useful stdlib
  - We've been running these A/B tests ad-hoc at first, with some success at automating them and going them in bulk. But they eat up tokens quickly, and many of the dark sessions failed. We can invest a bit more on a common harness and system to run these benchmarks and record comparable numbers
  - Some really rough numbers: at this point, we take ~3–10× cost and ~5–10× turns vs Python. Goal of baselining is to tighten or refute these.
  - Here are the things we're having AI build with dark, to take these metrics on:
    - **One-shot CLIs**: fizzbuzz, password-gen, unit-convert, dice, markdown-toc, json2table, grep-lite. Cheap, fast feedback.
    - **HTTP servers**: url-shortener, paste-bin, status-page, webhook-relay. Stress `serve`, routing, DB, JSON.
    - **Daemons / long-running**: cron-lite, health-checker, backup-daemon, heartbeat. Stress signal handling, file locks, long-lived state.
    - **TUIs**: todo, file-picker, kanban. Stress `readKey`, ANSI redraw, terminal state, general ability to interact with TUIs
    - **Libraries** — port a small lib from another language, build a thin CLI on top. Barely tested yet. Examples to grab: markdown→HTML, CSV reader, CRON parser, small diff lib, templating. Editing into existing package code, writing tests, getting the API right — all the things 'build a greenfield CLI' never exercises.
    - Out of reach without more stdlib regardless of category: anything needing async, raw sockets, AES, JWT-RS256, image/video, streaming JSON.
    - Trying to stick to stable set of projects we can iterate on, and track tokens/time on as we improve the product.
- We also got some good tangential work done
  - Auth
    - some accounts (darklang, stachu, feriel, paul) seeded
    - can log in/out via the CLI
  - GCP costs are down
    - Back in ~March, we did various cleanups to GCP
    - Finally see bill brought down from ~2k/mo to ~1100/mo given this work
    - This week, having another call to lower things further, down to ~500/mo
    - (That said, we're itching to get -next good enough to port folks over — then optimize cost on the new infra (smaller, fresh).)
  - Internal codebase cleanups
    - Serializer cleanup
      - got rid of Vanilla JSON
      - only F# serializers remaining are
        - raw binary serializers, used generally
        - hand-rolled JSON "queryable" for UserDBs
    - Builtins flattened/tidied a lot
    - trimmed the .exe quite a bit (esp given ASP.NET removal), and closer to native AOT building.
    - **Cloud-era plumbing/branding removed**: 25 → 18 .fsproj projects (−7). LibCloud → LibPackageManager; BuiltinCloudExecution → BuiltinDB.
- Annoying blocker for my usage: stability+sharing (a better/complete minimal prod + dist/sync story)
  - I actively want to use Dark to manage my personal software -- local .sh scripts I run etc
  - the primary thing blocking MY usage of darklang all day long is inability to sync my experience between machines
  - for my .sh scripts, I just sync them with syncthing or git depending on the context, but I have no real way to do this, so far, with Dark code
  - the solution of "just stand up a shared server and use that" doesn't quite fit my need as many of my scripts interact with local programs, data, etc.
  - still no stable environment, still reloading .dark files in local dev, and "expecting things to kill-and-fill" in prod (no migration story)
  - if we're running dark all the time on our machines, security topics like "capabilities control" are going to be thing we reach for, and build along the way
- Some directions+goals for the next month
  - Concrete goals
    - Get stable "AI benchmark" numbers: pick 5–10 bench projects, run agents against Dark + Python (+ TS?), record cost / turns / pass-rate / wall time.
    - Set up system to re-run baseline ~weekly or per-PR, so movement is attributable.
    - Diversify the bench: stop only building greenfield CLIs. Cover library ports, edits to existing package code, and the different app shapes (HTTP server, daemon, TUI, LLM agent, prompt flow) — each stresses different parts of Dark, and today's numbers only reflect one shape.
    - Build prompts into our solution -- share them in the package tree, etc
    - Advance "stability and sharing" story - somehow.
    - stand up a server that's not in my house; isolated server somewhere
    - port+host wip.darklang.com with dark
    - port+sync my personal scripts
    - Bring back crons - I want them. I have several crons I want to run for myself.
      - I could be cheap and just set up systemd/crontab or something in the meantime.
    - We need to empower the EASY creation of local CLI daemons
      - Some of those daemons will be for internal use -- sync, language server, etc.
      - I imagine we end up with a dashboard where you view all of the running Dark programs -- apps, daemons, etc. and can inspect their traces, etc.
    - lower GCP bill further, given steps already outlined
    - lower Honeycomb usage, so we can drop to free tier
    - Manage shell aliases via Dark — `print-md` should resolve to `@Stachu.Scripts.printMarkdown` (today it's a .sh script). Might be managed `.bashrc` edits, or something fancier.
  - Directions
    - Humans need a UX to inspect all running apps + daemons (live traces: user, agent, HTTP, crons).
      - Needs a better App concept — we long ago identified 'app template' vs 'app' as separate things.
      - State sync is puntable for now; template sync isn't.
    - Focus more on CLI Views -- AI should have a really nice way to create custom pretty-printers for any data, really quickly.
    - Focus more on allowing for interactions with Dark directly, by humans, while AI is doing work. How do we encourage engagement to the benefit of both, and how do we have them communicate completed work?
    - Build more Components in Dark. What does this look like?
    - use a better parser. make reloading-packages much much faster.
      - (maybe puntable if/when we get dist working - won't have to load so much)
    - goal: ai should be able to access, read, edit code as fast as a human could in Classic
    - What was rails good at? Let's do that. same question for Lua. I think we kinda fit in their space.
    - build out constraints
      - AI has a flow of: write fake function, just sig, and a bunch of tests. fill in and adjust impl until all tests pass. the loop needs to be incredibly tight. submit edit -> get test results. repeat until done, then continue
    - 80% of my AI usage = generating + reading spec markdown. Currently unmanaged. Should live in Dark matter (synced), with native md viewers/editors integrated.
    - Stability/sharing isn't a separate priority from the AI experiment — they merge as soon as a bench project's reference impl needs to be shareable for review. `dark publish` MVP belongs in both.
