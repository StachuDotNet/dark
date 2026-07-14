/// Persistence + state surface — UserDB, package manager, traces.
module Builtins.Matter.Builtin

open Prelude
open LibExecution.RuntimeTypes

module Builtin = LibExecution.Builtin
module PT = LibExecution.ProgramTypes


let fnRenames : Builtin.FnRenames = []

let builtins (pm : PT.PackageManager) : Builtins =
  Builtin.combine
    [ Libs.DB.builtins ()
      Libs.Sqlite.builtins ()

      Libs.Sync.Store.builtins ()
      Libs.Sync.OpLog.builtins ()
      Libs.Sync.Blobs.builtins ()
      Libs.Conflicts.builtins ()

      Libs.PM.Packages.builtins pm
      Libs.PM.PackageOps.builtins pm
      Libs.PM.Branches.builtins ()
      Libs.PM.Rebase.builtins ()
      Libs.PM.Merge.builtins ()
      Libs.PM.Scripts.builtins ()
      Libs.PM.Dependencies.builtins ()
      Libs.PM.Seed.builtins
      Libs.PM.Caps.builtins

      Libs.Traces.builtins ()
      Libs.Account.builtins () ]
    fnRenames
