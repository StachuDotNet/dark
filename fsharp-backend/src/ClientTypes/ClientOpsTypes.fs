module ClientTypes.Ops

module CPT = ClientTypes.Program

// todo: maybe these should really go to ClientTypes.Api or something?

module AddOpResultV1 =
  type T =
    { handlers : List<CPT.Handler.T> //
      deletedHandlers : List<CPT.Handler.T>
      dbs : List<CPT.DB.T>
      deletedDBs : List<CPT.DB.T>
      userFunctions : List<CPT.UserFunction.T>
      deletedUserFunctions : List<CPT.UserFunction.T>
      userTypes : List<CPT.UserType.T>
      deletedUserTypes : List<CPT.UserType.T> }


module AddOpParamsV1 =
  type T = { ops : List<CPT.Op.T>; opCtr : int; clientOpCtrID : string }


