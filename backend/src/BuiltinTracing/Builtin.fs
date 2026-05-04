/// Builtins that read and manipulate the trace store.
///
/// Companion to `LibDB.Tracing` (the recorder side); this project owns
/// the *reader* surface — `tracesList`, `tracesView`, `tracesFind`, etc.
/// Used by the `darklang traces` CLI commands and any other consumer
/// (dashboards, dev tools) that wants to query recorded executions.
module BuiltinTracing.Builtin

open LibExecution.Builtin.Shortcuts

module Builtin = LibExecution.Builtin


let fnRenames : Builtin.FnRenames =
  // Rename map for the move from `BuiltinCliHost/Libs/Traces.fs`. The
  // historical names had a `cli` prefix because traces were CLI-only
  // (eval/run); HTTP traces now flow through the same path so the
  // prefix has stopped being accurate. Keep the renames live for one
  // release cycle so external scripts pinned to the old names don't
  // immediately break.
  [ fn "cliTracesList" 0, fn "tracesList" 0
    fn "cliTracesView" 0, fn "tracesView" 0
    fn "cliTracesListByFn" 0, fn "tracesListByFn" 0
    fn "cliTracesStatsByHandler" 0, fn "tracesStatsByHandler" 0
    fn "cliTracesHotspots" 0, fn "tracesHotspots" 0
    fn "cliTracesFind" 0, fn "tracesFind" 0
    fn "cliTracesGetExprValues" 0, fn "tracesGetExprValues" 0
    fn "cliTracesResolveID" 0, fn "tracesResolveID" 0
    fn "cliTracesGetInput" 0, fn "tracesGetInput" 0
    fn "cliTracesGetExpectedOutput" 0, fn "tracesGetExpectedOutput" 0
    fn "cliTracesExportJson" 0, fn "tracesExportJson" 0
    fn "cliTracesReplayHttp" 0, fn "tracesReplayHttp" 0
    fn "cliTracesGetSummary" 0, fn "tracesGetSummary" 0
    fn "cliTracesGetHandlerName" 0, fn "tracesGetHandlerName" 0
    fn "cliTracesGenTest" 0, fn "tracesGenTest" 0
    fn "cliTracesImport" 0, fn "tracesImport" 0
    fn "cliTracesClearBefore" 0, fn "tracesClearBefore" 0
    fn "cliTracesClear" 0, fn "tracesClear" 0
    fn "cliTracesDelete" 0, fn "tracesDelete" 0
    fn "cliTracesPruneKeep" 0, fn "tracesPruneKeep" 0 ]


let builtins () = Builtin.combine [ Libs.Traces.builtins () ] fnRenames
