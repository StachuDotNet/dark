/// Minimal SQLite builtin to get canvas persistence working
module BuiltinCliHost.Libs.SQLite

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

let fns : List<BuiltInFn> = []

let builtins = LibExecution.Builtin.make [] fns