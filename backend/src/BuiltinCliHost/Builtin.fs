module BuiltinCliHost.Builtin

open Prelude
open LibExecution.RuntimeTypes

module Builtin = LibExecution.Builtin


let fnRenames : Builtin.FnRenames =
  // old names, new names
  // eg: fn "Http" "respond" 0, fn "Http" "response" 0
  []

let builtins = Builtin.combine [ Libs.Cli.builtins; Libs.SQLite.builtins ] fnRenames
