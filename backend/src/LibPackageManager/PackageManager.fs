module LibPackageManager.PackageManager

open Prelude

module RT = LibExecution.RuntimeTypes
module PT = LibExecution.ProgramTypes

open LibPackageManager.Caching

module PMPT = LibPackageManager.ProgramTypes
module PMRT = LibPackageManager.RuntimeTypes

/// The default RuntimeTypes.PackageManager using the default session
let rt : RT.PackageManager =
  SessionAwarePackageManager.createDefault ()

/// Create a RuntimeTypes.PackageManager for a specific session
let rtForSession (sessionId: uuid) : RT.PackageManager =
  SessionAwarePackageManager.createForSpecificSession sessionId

/// The default ProgramTypes.PackageManager using session-aware lookups
let pt : PT.PackageManager =
  { findType = withCache PMPT.Type.find
    findValue = withCache PMPT.Value.find
    findFn = withCache PMPT.Fn.find

    getType = withCache (fun id -> SessionManager.getType None id)
    getFn = withCache (fun id -> SessionManager.getFn None id)
    getValue = withCache (fun id -> SessionManager.getValue None id)

    search = fun query -> SessionManager.searchInSession None query

    init =
      uply {
        do! SessionManager.ensureDefaultSession ()
        return ()
      } }

/// Create a ProgramTypes.PackageManager for a specific session
let ptForSession (sessionId: uuid) : PT.PackageManager =
  { findType = withCache PMPT.Type.find
    findValue = withCache PMPT.Value.find
    findFn = withCache PMPT.Fn.find

    getType = withCache (fun id -> SessionManager.getType (Some sessionId) id)
    getFn = withCache (fun id -> SessionManager.getFn (Some sessionId) id)
    getValue = withCache (fun id -> SessionManager.getValue (Some sessionId) id)

    search = fun query -> SessionManager.searchInSession (Some sessionId) query

    init =
      uply {
        do! SessionManager.ensureDefaultSession ()
        return ()
      } }
