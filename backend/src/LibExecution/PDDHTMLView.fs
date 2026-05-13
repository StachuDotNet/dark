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
  .top-level { background: #0f1f0f; border: 1px solid #305030; padding: 14px 16px; margin-bottom: 20px; border-radius: 4px; font-size: 15px; white-space: pre-wrap; }
  .top-level .label { color: #555; font-size: 11px; text-transform: uppercase; letter-spacing: 0.1em; margin-bottom: 6px; }
  .annot { padding: 0 4px; border-radius: 3px; font-weight: bold; }
  .annot.in-progress { background: #4a3a00; color: #e0c060; }
  .annot.real { background: #053515; color: #6fcf90; }
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
    let mutable rendered = htmlEscape session.topLevel
    for kv in session.fns do
      let fn = kv.Value
      let _, _, cls = stateBadge fn.state
      // Word-boundary regex on the (escaped) name avoids partial-token highlights.
      let escapedName = System.Text.RegularExpressions.Regex.Escape(htmlEscape fn.name)
      let pattern = @"\b" + escapedName + @"\b"
      let replacement = sprintf "<span class=\"annot %s\">%s</span>" cls (htmlEscape fn.name)
      rendered <- System.Text.RegularExpressions.Regex.Replace(rendered, pattern, replacement)
    append "<div class=\"top-level\">\n"
    append "<div class=\"label\">top-level expression</div>\n"
    append rendered
    append "\n</div>\n"

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


let private writeFile (session : Session) : unit =
  try
    let html = renderHtml session
    let dir = Path.GetDirectoryName session.htmlPath
    if not (String.IsNullOrEmpty dir) && not (Directory.Exists dir) then
      Directory.CreateDirectory dir |> ignore<DirectoryInfo>
    File.WriteAllText(session.htmlPath, html)
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
