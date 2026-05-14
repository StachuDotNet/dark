/// PDD interactive HTML view.
///
/// Generates a live HTML file at `rundir/pdd-view/<sessionId>.html` that
/// shows the state of every Pending fn the runtime has touched, alongside
/// a chronological event log.
///
/// Two-pane layout:
///
///   ┌──────────────────────────┬──────────────────────────┐
///   │ functions                │ events                   │
///   │                          │                          │
///   │  ✓ addOne (real, 312ms)  │  16:03:12 start: addOne  │
///   │      body: x + 1L        │  16:03:13 llm: "x + 1L"  │
///   │                          │  16:03:13 compiled       │
///   │  ⋯ greet (in-progress)   │  16:03:14 done: addOne   │
///   │                          │  16:03:14 start: greet   │
///   │                          │                          │
///   └──────────────────────────┴──────────────────────────┘
///
/// State badges:
///   ✓ real         green   (materialized + executed)
///   ⋯ in-progress  yellow  (LLM call in flight)
///   ▼ fake         gray    (fallback identity / EmptyBody)
///   ↻ cached       blue    (hit pendingFnInstrCache, no LLM call)
///   ✗ failed       red     (materialization errored)
///
/// Self-refreshes every 1s via meta-refresh until session_end.
module LibExecution.PDDHTMLView

open System
open System.Collections.Generic
open System.IO
open System.Text

module M = LibExecution.PDDMaterializer


// ---------------------------------------------------------------------------
// Per-session state
// ---------------------------------------------------------------------------

type FnRecord =
  { name : string
    mutable state : M.FnState
    mutable sig_ : string
    mutable body : string
    mutable latencyMs : int
    mutable error : string option }

type Session =
  { id : string
    startedAt : DateTime
    htmlPath : string
    fns : Dictionary<string, FnRecord>
    events : List<string * string>  // (timestamp, html-escaped text)
    mutable topLevel : string
    mutable closed : bool }


// ---------------------------------------------------------------------------
// HTML rendering — hand-written so we have zero deps.
// ---------------------------------------------------------------------------

let private htmlEscape (s : string) : string =
  s
    .Replace("&", "&amp;")
    .Replace("<", "&lt;")
    .Replace(">", "&gt;")
    .Replace("\"", "&quot;")

let private stateBadge (s : M.FnState) : string * string * string =
  // (glyph, label, css-class)
  match s with
  | M.InProgress -> "⋯", "in-progress", "in-progress"
  | M.Real -> "✓", "real", "real"
  | M.Provisional -> "~", "provisional", "provisional"
  | M.Fake -> "▼", "fake", "fake"
  | M.Cached -> "↻", "cached", "cached"
  | M.Failed -> "✗", "failed", "failed"

let private cssStyles = """
  body { font: 14px/1.5 -apple-system, "SF Mono", Consolas, monospace; margin: 0; padding: 16px; background: #1a1a1a; color: #d4d4d4; }
  h1 { font-size: 16px; font-weight: normal; color: #888; margin: 0 0 16px; }
  .layout { display: grid; grid-template-columns: 1fr 1fr; gap: 24px; }
  .pane h2 { font-size: 13px; text-transform: uppercase; letter-spacing: 0.1em; color: #777; margin: 0 0 12px; font-weight: normal; }
  .fn { background: #242424; border-left: 3px solid #444; padding: 10px 12px; margin-bottom: 8px; border-radius: 0 4px 4px 0; }
  .fn-header { display: flex; align-items: baseline; gap: 8px; }
  .badge { font-weight: bold; padding: 1px 6px; border-radius: 3px; font-size: 11px; }
  .badge.in-progress { background: #4a3a00; color: #e0c060; }
  .badge.real { background: #053515; color: #6fcf90; }
  .badge.provisional { background: #3a2a00; color: #d0a040; }
  .badge.fake { background: #2a2a2a; color: #888; }
  .badge.cached { background: #002545; color: #6080d0; }
  .badge.failed { background: #4a0010; color: #e07080; }
  .fn-name { font-weight: bold; }
  .fn-latency { color: #666; font-size: 12px; margin-left: auto; }
  .fn-body { color: #b0b0b0; font-size: 13px; margin-top: 6px; white-space: pre-wrap; }
  .fn-error { color: #e07080; font-size: 12px; margin-top: 6px; }
  .fn[data-state="in-progress"] { border-left-color: #c0a040; }
  .fn[data-state="real"] { border-left-color: #4fa070; }
  .fn[data-state="fake"] { border-left-color: #666; }
  .fn[data-state="cached"] { border-left-color: #4070a0; }
  .fn[data-state="failed"] { border-left-color: #c05060; }
  .events { font-size: 12px; }
  .event { padding: 3px 0; border-bottom: 1px solid #2a2a2a; }
  .event-ts { color: #666; margin-right: 8px; }
  .header-stamp { color: #555; font-size: 11px; }
  .top-level { background: #0f1f0f; border: 1px solid #305030; padding: 14px 16px; margin-bottom: 20px; border-radius: 4px; font-size: 15px; white-space: pre-wrap; word-break: break-word; }
  .top-level .label { color: #555; font-size: 11px; text-transform: uppercase; letter-spacing: 0.1em; margin-bottom: 6px; }
  details.top-level summary { cursor: pointer; list-style: none; }
  details.top-level summary::-webkit-details-marker { display: none; }
  details.top-level[open] summary { margin-bottom: 12px; padding-bottom: 8px; border-bottom: 1px solid #2a3a2a; }
  .annot { padding: 0 4px; border-radius: 3px; font-weight: bold; }
  .annot.in-progress { background: #4a3a00; color: #e0c060; }
  .annot.real { background: #053515; color: #6fcf90; }
  .annot.provisional { background: #3a2a00; color: #d0a040; }
  .annot.fake { background: #2a2a2a; color: #888; }
  .annot.cached { background: #002545; color: #6080d0; }
  .annot.failed { background: #4a0010; color: #e07080; }
"""

let private renderHtml (session : Session) : string =
  let sb = StringBuilder()
  let append (s : string) = sb.Append s |> ignore<StringBuilder>

  append "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n"
  append (sprintf "<title>PDD view — %s</title>\n" (htmlEscape session.id))
  if not session.closed then
    append "<meta http-equiv=\"refresh\" content=\"1\">\n"
  append (sprintf "<style>%s</style>\n" cssStyles)
  append "</head>\n<body>\n"

  let stamp = DateTime.UtcNow.ToString("HH:mm:ss")
  let status = if session.closed then "done" else "live"
  append
    (sprintf
      "<h1>PDD view <span class=\"header-stamp\">— %s · session %s · %s · %d fns · %d events</span></h1>\n"
      status
      (htmlEscape session.id)
      stamp
      session.fns.Count
      session.events.Count)

  // Top-level expression with inline pending-fn annotations.
  // Walk each fn name in session.fns and wrap matches in <span class="annot ...">.
  if not (String.IsNullOrWhiteSpace session.topLevel) then
    let originalLen = session.topLevel.Length
    let mutable rendered = htmlEscape session.topLevel
    for kv in session.fns do
      let fn = kv.Value
      let _, _, cls = stateBadge fn.state
      // Word-boundary regex on the (escaped) name avoids partial-token highlights.
      let escapedName = System.Text.RegularExpressions.Regex.Escape(htmlEscape fn.name)
      let pattern = @"\b" + escapedName + @"\b"
      let replacement = sprintf "<span class=\"annot %s\">%s</span>" cls (htmlEscape fn.name)
      rendered <- System.Text.RegularExpressions.Regex.Replace(rendered, pattern, replacement)
    // Long top-levels (e.g. the 24K-char http-server expression) collapse
    // by default; user clicks to expand. Short ones render inline as before.
    if originalLen <= 800 then
      append "<div class=\"top-level\">\n"
      append "<div class=\"label\">top-level expression</div>\n"
      append rendered
      append "\n</div>\n"
    else
      append "<details class=\"top-level\">\n"
      append (sprintf
        "<summary><span class=\"label\">top-level expression</span> <span style=\"color:#888\">(%d chars · click to expand)</span></summary>\n"
        originalLen)
      append rendered
      append "\n</details>\n"

  append "<div class=\"layout\">\n"

  // Functions pane
  append "<div class=\"pane\"><h2>functions</h2>\n"
  if session.fns.Count = 0 then
    append "<div style=\"color:#555\">(no Pending fns yet)</div>\n"
  for kv in session.fns do
    let fn = kv.Value
    let glyph, label, cls = stateBadge fn.state
    append (sprintf "<div class=\"fn\" data-state=\"%s\">\n" cls)
    append "  <div class=\"fn-header\">\n"
    append (sprintf "    <span class=\"badge %s\">%s %s</span>\n" cls glyph label)
    append (sprintf "    <span class=\"fn-name\">%s</span>\n" (htmlEscape fn.name))
    if fn.latencyMs > 0 then
      append (sprintf "    <span class=\"fn-latency\">%d ms</span>\n" fn.latencyMs)
    append "  </div>\n"
    if not (String.IsNullOrEmpty fn.sig_) then
      append (sprintf "  <div class=\"fn-body\">sig: %s</div>\n" (htmlEscape fn.sig_))
    if not (String.IsNullOrEmpty fn.body) then
      append (sprintf "  <div class=\"fn-body\">body: %s</div>\n" (htmlEscape fn.body))
    match fn.error with
    | Some e -> append (sprintf "  <div class=\"fn-error\">error: %s</div>\n" (htmlEscape e))
    | None -> ()
    append "</div>\n"
  append "</div>\n"

  // Events pane
  append "<div class=\"pane\"><h2>events</h2>\n<div class=\"events\">\n"
  if session.events.Count = 0 then
    append "<div style=\"color:#555\">(no events yet)</div>\n"
  for (ts, text) in session.events do
    append
      (sprintf
        "<div class=\"event\"><span class=\"event-ts\">%s</span>%s</div>\n"
        (htmlEscape ts)
        text)
  append "</div></div>\n"

  append "</div>\n</body>\n</html>\n"
  sb.ToString()


let private getOrCreateFn (session : Session) (name : string) : FnRecord =
  match session.fns.TryGetValue name with
  | true, fn -> fn
  | false, _ ->
    let fn =
      { name = name
        state = M.InProgress
        sig_ = ""
        body = ""
        latencyMs = 0
        error = None }
    session.fns[name] <- fn
    fn


let private jsonEscape (s : string) : string =
  s
    .Replace("\\", "\\\\")
    .Replace("\"", "\\\"")
    .Replace("\n", "\\n")
    .Replace("\r", "")
    .Replace("\t", "\\t")

let private writeSidecar (session : Session) : unit =
  try
    let dir = Path.GetDirectoryName session.htmlPath
    let sidecar = Path.Combine(dir, sprintf "%s.json" session.id)
    let countByState (s : M.FnState) =
      session.fns
      |> Seq.filter (fun kv -> kv.Value.state = s)
      |> Seq.length
    let llmCalls =
      session.events
      |> Seq.filter (fun (_, t) -> t.Contains "llm-rsp")
      |> Seq.length
    let elapsedMs =
      int (DateTime.UtcNow - session.startedAt).TotalMilliseconds
    let sb = StringBuilder()
    sb.Append("{") |> ignore<StringBuilder>
    sb.AppendFormat("\"id\":\"{0}\",", jsonEscape session.id)
    |> ignore<StringBuilder>
    sb.AppendFormat(
      "\"startedAt\":\"{0}\",",
      session.startedAt.ToString("o")
    )
    |> ignore<StringBuilder>
    sb.AppendFormat("\"closed\":{0},", (if session.closed then "true" else "false"))
    |> ignore<StringBuilder>
    sb.AppendFormat("\"elapsedMs\":{0},", elapsedMs) |> ignore<StringBuilder>
    sb.AppendFormat("\"topLevel\":\"{0}\",", jsonEscape session.topLevel)
    |> ignore<StringBuilder>
    sb.AppendFormat("\"fnCount\":{0},", session.fns.Count) |> ignore<StringBuilder>
    sb.AppendFormat("\"real\":{0},", countByState M.Real) |> ignore<StringBuilder>
    sb.AppendFormat("\"fake\":{0},", countByState M.Fake) |> ignore<StringBuilder>
    sb.AppendFormat("\"cached\":{0},", countByState M.Cached) |> ignore<StringBuilder>
    sb.AppendFormat("\"failed\":{0},", countByState M.Failed) |> ignore<StringBuilder>
    sb.AppendFormat("\"inProgress\":{0},", countByState M.InProgress)
    |> ignore<StringBuilder>
    sb.AppendFormat("\"llmCalls\":{0}", llmCalls) |> ignore<StringBuilder>
    sb.Append("}") |> ignore<StringBuilder>
    File.WriteAllText(sidecar, sb.ToString())
  with _ -> ()

/// Scan rundir/pdd-view/*.json and emit index.html listing all sessions
/// newest-first with description, fn-state counts, llm calls, status.
let private writeIndex (dir : string) : unit =
  try
    if not (Directory.Exists dir) then
      ()
    else
      let files = Directory.GetFiles(dir, "*.json")
      let entries =
        files
        |> Array.choose (fun f ->
          try
            use doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText f)
            let root = doc.RootElement
            let getString (k : string) =
              match root.TryGetProperty k with
              | true, v -> v.GetString()
              | _ -> ""
            let getInt (k : string) =
              match root.TryGetProperty k with
              | true, v -> v.GetInt32()
              | _ -> 0
            let getBool (k : string) =
              match root.TryGetProperty k with
              | true, v -> v.GetBoolean()
              | _ -> false
            Some(
              {| id = getString "id"
                 startedAt = getString "startedAt"
                 closed = getBool "closed"
                 elapsedMs = getInt "elapsedMs"
                 topLevel = getString "topLevel"
                 fnCount = getInt "fnCount"
                 real = getInt "real"
                 fake = getInt "fake"
                 cached = getInt "cached"
                 failed = getInt "failed"
                 inProgress = getInt "inProgress"
                 llmCalls = getInt "llmCalls" |}
            )
          with _ -> None)
        |> Array.sortByDescending (fun e -> e.startedAt)
      let sb = StringBuilder()
      sb.Append "<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">"
      |> ignore<StringBuilder>
      sb.Append "<title>PDD sessions</title>" |> ignore<StringBuilder>
      sb.Append "<meta http-equiv=\"refresh\" content=\"3\">" |> ignore<StringBuilder>
      sb.AppendFormat("<style>{0}</style>", cssStyles) |> ignore<StringBuilder>
      sb.Append """<style>
        table { width: 100%; border-collapse: collapse; font-size: 13px; }
        th, td { text-align: left; padding: 8px 10px; border-bottom: 1px solid #2a2a2a; vertical-align: top; }
        th { color: #888; font-weight: normal; text-transform: uppercase; letter-spacing: 0.08em; font-size: 11px; }
        td.expr { color: #d4d4d4; font-family: "SF Mono", Consolas, monospace; max-width: 480px; word-break: break-word; }
        td.id a { color: #6080d0; text-decoration: none; font-weight: bold; }
        td.id a:hover { text-decoration: underline; }
        .pill { display: inline-block; padding: 1px 6px; border-radius: 3px; font-size: 11px; font-weight: bold; margin-right: 4px; }
        .pill-real    { background: #053515; color: #6fcf90; }
        .pill-fake    { background: #2a2a2a; color: #888; }
        .pill-cached  { background: #002545; color: #6080d0; }
        .pill-failed  { background: #4a0010; color: #e07080; }
        .pill-prog    { background: #4a3a00; color: #e0c060; }
        .status-live  { color: #e0c060; }
        .status-done  { color: #6fcf90; }
        .empty        { color: #555; }
      </style>"""
      |> ignore<StringBuilder>
      sb.Append "</head><body>" |> ignore<StringBuilder>
      sb.AppendFormat(
        "<h1>PDD sessions <span class=\"header-stamp\">— {0} total · refreshed {1}</span></h1>",
        entries.Length,
        DateTime.UtcNow.ToString("HH:mm:ss")
      )
      |> ignore<StringBuilder>
      sb.Append "<p><a href=\"fns.html\">fn registry →</a></p>"
      |> ignore<StringBuilder>
      if entries.Length = 0 then
        sb.Append "<p class=\"empty\">no sessions yet — run <code>dark prompt \"...\"</code></p>"
        |> ignore<StringBuilder>
      else
        sb.Append "<table><thead><tr>"
        |> ignore<StringBuilder>
        sb.Append
          "<th>id</th><th>status</th><th>top-level</th><th>fns</th><th>llm</th><th>elapsed</th>"
        |> ignore<StringBuilder>
        sb.Append "</tr></thead><tbody>" |> ignore<StringBuilder>
        for e in entries do
          let statusCls = if e.closed then "status-done" else "status-live"
          let statusText = if e.closed then "done" else "live"
          let pill cls n label =
            if n > 0 then sprintf "<span class=\"pill pill-%s\">%d %s</span>" cls n label
            else ""
          let pills =
            pill "real" e.real "real"
            + pill "cached" e.cached "cached"
            + pill "fake" e.fake "fake"
            + pill "failed" e.failed "failed"
            + pill "prog" e.inProgress "...."
          let displayExpr =
            if String.IsNullOrEmpty e.topLevel then "(no top-level)"
            else
              // Truncate long top-levels (e.g. the 32-route http-server
              // expression is 24K chars). Keep the head + a "… (Nk total)"
              // marker so the index stays compact.
              let s = e.topLevel
              if s.Length <= 200 then htmlEscape s
              else
                let kb = s.Length / 1024
                sprintf "%s<span style=\"color:#666\">… (%dk total)</span>"
                  (htmlEscape (s.Substring(0, 200)))
                  kb
          sb.AppendFormat(
            "<tr><td class=\"id\"><a href=\"{0}.html\">{0}</a></td><td class=\"{1}\">{2}</td><td class=\"expr\">{3}</td><td>{4}</td><td>{5}</td><td>{6} ms</td></tr>",
            htmlEscape e.id,
            statusCls,
            statusText,
            displayExpr,
            (if pills = "" then "<span class=\"empty\">—</span>" else pills),
            (if e.llmCalls > 0 then string e.llmCalls else "<span class=\"empty\">0</span>"),
            e.elapsedMs
          )
          |> ignore<StringBuilder>
        sb.Append "</tbody></table>" |> ignore<StringBuilder>
      sb.Append "</body></html>" |> ignore<StringBuilder>
      File.WriteAllText(Path.Combine(dir, "index.html"), sb.ToString())
  with _ -> ()

/// Build a registry of PDD fns across all sessions. Reads
/// `rundir/pdd-cache/promoted.jsonl` (working revs, append-only) and
/// `rundir/pdd-cache/promoted_hashes.jsonl` (committed snapshots).
/// Emits `<viewDir>/fns.html` — one row per unique fn with rev count,
/// latest body length, committed-hash count + most recent hash.
let private writeFnsIndex (viewDir : string) : unit =
  try
    let promotedPath = "rundir/pdd-cache/promoted.jsonl"
    let hashesPath = "rundir/pdd-cache/promoted_hashes.jsonl"
    if not (File.Exists promotedPath) then () else
    // Per-fn aggregate: rev count, latest body length, latest body sample
    let revs = System.Collections.Generic.Dictionary<string, int * int * string>()
    for line in File.ReadAllLines promotedPath do
      if not (String.IsNullOrWhiteSpace line) then
        try
          let doc = System.Text.Json.JsonDocument.Parse line
          let r = doc.RootElement
          let n = r.GetProperty("name").GetString()
          let b = r.GetProperty("body").GetString()
          let (c, _, _) =
            match revs.TryGetValue n with
            | true, x -> x
            | _ -> (0, 0, "")
          revs[n] <- (c + 1, b.Length, b)
        with _ -> ()
    let committed = System.Collections.Generic.Dictionary<string, int * string>()
    if File.Exists hashesPath then
      for line in File.ReadAllLines hashesPath do
        if not (String.IsNullOrWhiteSpace line) then
          try
            let doc = System.Text.Json.JsonDocument.Parse line
            let r = doc.RootElement
            let n = r.GetProperty("name").GetString()
            let h = r.GetProperty("hash").GetString()
            let (c, _) =
              match committed.TryGetValue n with
              | true, x -> x
              | _ -> (0, "")
            committed[n] <- (c + 1, h)
          with _ -> ()
    let sb = StringBuilder()
    sb.Append "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">"
    |> ignore<StringBuilder>
    sb.Append "<title>PDD fns</title><meta http-equiv=\"refresh\" content=\"5\">"
    |> ignore<StringBuilder>
    sb.AppendFormat("<style>{0}</style>", cssStyles) |> ignore<StringBuilder>
    sb.Append """<style>
      table { width: 100%; border-collapse: collapse; font-size: 13px; }
      th, td { text-align: left; padding: 8px 10px; border-bottom: 1px solid #2a2a2a; vertical-align: top; }
      th { color: #888; font-weight: normal; text-transform: uppercase; letter-spacing: 0.08em; font-size: 11px; }
      td.name a { color: #d4d4d4; font-weight: bold; text-decoration: none; }
      td.body { color: #b0b0b0; font-family: "SF Mono", Consolas, monospace; max-width: 480px; word-break: break-word; }
      .pill { display: inline-block; padding: 1px 6px; border-radius: 3px; font-size: 11px; font-weight: bold; margin-right: 4px; }
      .pill-rev { background: #002545; color: #6080d0; }
      .pill-hash { background: #053515; color: #6fcf90; }
      .pill-fresh { background: #4a3a00; color: #e0c060; }
    </style></head><body>"""
    |> ignore<StringBuilder>
    sb.AppendFormat(
      "<h1>PDD fns <span class=\"header-stamp\">— {0} unique · refreshed {1}</span></h1>",
      revs.Count,
      DateTime.UtcNow.ToString("HH:mm:ss"))
    |> ignore<StringBuilder>
    if revs.Count = 0 then
      sb.Append "<p class=\"empty\">no fns yet</p>" |> ignore<StringBuilder>
    else
      sb.Append "<p><a href=\"index.html\">← sessions</a></p>"
      |> ignore<StringBuilder>
      sb.Append "<table><thead><tr><th>name</th><th>revs</th><th>committed</th><th>size</th><th>preview</th></tr></thead><tbody>"
      |> ignore<StringBuilder>
      let sorted =
        revs
        |> Seq.sortBy (fun kv -> kv.Key)
        |> Seq.toList
      for kv in sorted do
        let name = kv.Key
        let revCount, latestLen, latestBody = kv.Value
        let preview =
          if latestBody.Length > 200 then latestBody.Substring(0, 200) + "…"
          else latestBody
        let commitInfo =
          match committed.TryGetValue name with
          | true, (n, h) ->
            sprintf "<span class=\"pill pill-hash\">✓ %d</span> <span style=\"color:#888;font-family:monospace\">%s</span>" n h
          | _ -> "<span class=\"pill pill-fresh\">~ working</span>"
        sb.AppendFormat(
          "<tr><td class=\"name\">{0}</td><td><span class=\"pill pill-rev\">{1}</span></td><td>{2}</td><td>{3}</td><td class=\"body\">{4}</td></tr>",
          htmlEscape name,
          revCount,
          commitInfo,
          latestLen,
          htmlEscape preview)
        |> ignore<StringBuilder>
      sb.Append "</tbody></table>" |> ignore<StringBuilder>
    sb.Append "</body></html>" |> ignore<StringBuilder>
    File.WriteAllText(Path.Combine(viewDir, "fns.html"), sb.ToString())
  with _ -> ()

let private writeFile (session : Session) : unit =
  try
    let html = renderHtml session
    let dir = Path.GetDirectoryName session.htmlPath
    if not (String.IsNullOrEmpty dir) && not (Directory.Exists dir) then
      Directory.CreateDirectory dir |> ignore<DirectoryInfo>
    File.WriteAllText(session.htmlPath, html)
    writeSidecar session
    if not (String.IsNullOrEmpty dir) then writeFnsIndex dir
    if not (String.IsNullOrEmpty dir) then writeIndex dir
  with _ -> ()  // never break a run on a render failure


let private logEvent (session : Session) (text : string) : unit =
  let ts = DateTime.UtcNow.ToString("HH:mm:ss.fff")
  session.events.Add(ts, text)


// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/// Create a fresh session backed by an HTML file at the given path. Caller
/// is responsible for `installSink session` and `close session` at end.
let createSession (id : string) (path : string) : Session =
  let s =
    { id = id
      startedAt = DateTime.UtcNow
      htmlPath = path
      fns = Dictionary<string, FnRecord>()
      events = List<string * string>()
      topLevel = ""
      closed = false }
  writeFile s
  s

/// Set / replace the top-level expression text shown at the top of the view.
/// Annotations on Pending fn names are computed on each render from current
/// session state.
let setTopLevel (session : Session) (expr : string) : unit =
  session.topLevel <- expr
  writeFile session

let defaultPathFor (sessionId : string) : string =
  Path.Combine("rundir", "pdd-view", sprintf "%s.html" sessionId)

/// Build the EventSink that updates a session's state + rewrites the
/// HTML file on each event.
let sinkFor (session : Session) : M.EventSink =
  fun ev ->
    match ev with
    | M.MaterializeStart(name, model) ->
      let fn = getOrCreateFn session name
      fn.state <- M.InProgress
      logEvent session (sprintf "<b>start</b> %s &middot; <span style=\"color:#888\">%s</span>" name model)
    | M.LLMResponse(name, elapsed, _body) ->
      logEvent session (sprintf "<b>llm-rsp</b> %s &middot; <span style=\"color:#666\">%d ms</span>" name elapsed)
    | M.ParseOk(name, sig_, body) ->
      let fn = getOrCreateFn session name
      fn.sig_ <- sig_
      fn.body <- body
      logEvent session (sprintf "<b>parsed</b> %s" name)
    | M.CompileBody(name, kind, _regCount) ->
      logEvent session (sprintf "<b>compiled</b> %s &middot; <span style=\"color:#888\">%s</span>" name kind)
    | M.MaterializeDone(name, state, elapsed) ->
      let fn = getOrCreateFn session name
      fn.state <- state
      fn.latencyMs <- elapsed
      let _, label, _ = stateBadge state
      logEvent session (sprintf "<b>done</b> %s &middot; <span style=\"color:#888\">%s &middot; %d ms</span>" name label elapsed)
    | M.MaterializeFailed(name, reason) ->
      let fn = getOrCreateFn session name
      fn.state <- M.Failed
      fn.error <- Some reason
      logEvent session (sprintf "<b style=\"color:#e07080\">failed</b> %s &middot; %s" name reason)
    | M.TestRan(name, label, detail) ->
      let color =
        match label with
        | "pass" -> "#6fcf90"
        | "fail" -> "#e07080"
        | "error" -> "#e07080"
        | _ -> "#888"
      logEvent session
        (sprintf "<b style=\"color:%s\">test %s</b> %s &middot; <span style=\"color:#888\">%s</span>"
          color label name (htmlEscape detail))
    writeFile session

/// Install this session's sink as the global currentSink. Returns the
/// previous sink so the caller can restore (if they care).
let install (session : Session) : M.EventSink =
  let prev = M.currentSink
  M.currentSink <- sinkFor session
  prev

let close (session : Session) : unit =
  session.closed <- true
  writeFile session
