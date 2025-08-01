module LibPackageManager.PackageManager

open Prelude

module RT = LibExecution.RuntimeTypes
module PT = LibExecution.ProgramTypes

open LibPackageManager.Caching

module PMPT = LibPackageManager.ProgramTypes
module PMRT = LibPackageManager.RuntimeTypes


// TODO: bring back eager loading
let rt : RT.PackageManager =
  { getType = withCache PMRT.Type.get
    getFn = withCache PMRT.Fn.get
    getConstant = withCache PMRT.Constant.get

    init =
      uply {
        //eagerLoad
        return ()
      } }


let pt : PT.PackageManager =
  { findType = withCache PMPT.Type.find
    findConstant = withCache PMPT.Constant.find
    findFn = withCache PMPT.Fn.find

    getType = withCache PMPT.Type.get
    getFn = withCache PMPT.Fn.get
    getConstant = withCache PMPT.Constant.get

    search = LibPackageManager.ProgramTypes.search

    init = uply { return () } }
