/// The types that the user sees
module LibExecution.ProgramTypes

type id = Prelude.id
type tlid = Prelude.tlid
type Sign = Prelude.Sign

/// Used to reference a canvas-level type written by a Developer
///
/// TODO: wrap this in a FQTypeName module+type,
/// once we support stdlib-defined types (soon!)
/// ```fsharp
/// type FQTypeName =
///   | User of UsertypeName
///   | Stdlib of StdlibTypeName
///   | Package of PackageTypeName
/// ```
/// (steal from `FQFnName`)
type UserTypeName = { type_ : string; version : int }

/// A Fully-Qualified Function Name
/// Includes package, module, and version information where relevant.
module FQFnName =

  /// Standard Library Function NameDF
  type StdlibFnName = { module_ : string; function_ : string; version : int }

  /// Standard Library Infix Function Name
  // CLEANUP The module is only there for a few functions in the Date module, such as
  // Date::<. Making these infix wasn't a great idea, and we should remove them.
  type InfixStdlibFnName = { module_ : Option<string>; function_ : string }

  /// A UserFunction is a function written by a Developer in their canvas
  type UserFnName = string

  /// The name of a function in the package manager
  type PackageFnName =
    { owner : string
      package : string
      module_ : string
      function_ : string
      version : int }

  // We don't include InfixStdlibFnName here as that is used directly by EInfix
  type T =
    | User of UserFnName
    | Stdlib of StdlibFnName
    | Package of PackageFnName

/// Used for pattern matching in a match statement
type MatchPattern =
  | MPVariable of id * string
  | MPConstructor of id * string * List<MatchPattern>
  | MPInteger of id * int64
  | MPBool of id * bool
  | MPCharacter of id * string
  | MPString of id * string
  | MPFloat of id * Sign * string * string
  | MPUnit of id
  | MPTuple of id * MatchPattern * MatchPattern * List<MatchPattern>

type BinaryOperation =
  | BinOpAnd
  | BinOpOr

type Infix =
  | InfixFnCall of FQFnName.InfixStdlibFnName
  | BinOp of BinaryOperation

/// Expressions - the main part of the language.
type Expr =
  | EInteger of id * int64
  | EBool of id * bool
  | EString of id * List<StringSegment>
  /// A character is an Extended Grapheme Cluster (hence why we use a string). This
  /// is equivalent to one screen-visible "character" in Unicode.
  | ECharacter of id * string
  // Allow the user to have arbitrarily big numbers, even if they don't make sense as
  // floats. The float is split as we want to preserve what the user entered.
  // Strings are used as numbers lose the leading zeros (eg 7.00007)
  | EFloat of id * Sign * string * string
  | EUnit of id
  | ELet of id * string * Expr * Expr
  | EIf of id * Expr * Expr * Expr
  | EInfix of id * Infix * Expr * Expr
  // the id in the varname list is the analysis id, used to get a livevalue
  // from the analysis engine
  | ELambda of id * List<id * string> * Expr
  | EFieldAccess of id * Expr * string
  | EVariable of id * string
  | EFnCall of id * FQFnName.T * List<Expr>
  | EList of id * List<Expr>
  | ETuple of id * Expr * Expr * List<Expr>
  | EAnonRecord of id * List<string * Expr>
  | EPipe of id * Expr * Expr * List<Expr>

  // Constructors include `Just`, `Nothing`, `Error`, `Ok`.  In practice the
  // expr list is currently always length 1 (for `Just`, `Error`, and `Ok`)
  // or length 0 (for `Nothing`).
  // TODO: migrate usages of this to usages of EDefinedEnum(FQTypeName.T, ...
  | EConstructor of id * string * List<Expr>

  /// Supports `match` expressions
  /// ```fsharp
  /// match x + 2 with // arg
  /// // cases
  /// | pattern -> expr
  /// | pattern -> expr
  /// | ...
  /// ```
  | EMatch of id * arg : Expr * cases : List<MatchPattern * Expr>

  // Placeholder that indicates the target of the Thread. May be movable at
  // some point
  | EPipeTarget of id

  /// Like an if statement, but with a label
  /// TODO: continue describing
  | EFeatureFlag of
    id *
    flagName : string *
    cond : Expr *
    caseA : Expr *
    caseB : Expr


  // TODO:
  // - define EUser
  // migrate existing EAnonRecords to EUserRecords and
  // /// Given a User type of:
  // ///   `type MyRecord = { A: int;  B: int * MyRecord }`
  // /// , this is the expression
  // ///   `EUserRecord(UserType.MyRecord, [EInteger(1), EString("title")]`
  // | EUserRecord of id * UserTypeName * fields: List<string * Expr>


  // TODO one of these:
  // - implement EStdlibEnum and EStdlibRecord, then EPackageEnum and EPackagEAnonRecordcord
  // - implement a more generic EDefinedEnum and EDefinedRecord
  //   that reference awith `User`, `Stdlib`, and `Package` cases

  /// Given a User type of:
  ///   `type MyEnum = A | B of int | C of int * (label: string) | D of MyEnum`
  /// , this is the expression
  ///   `EUserEnum(UserType.MyEnum, "C", [EInteger(1), EString("title")]`
  | EUserEnum of id * UserTypeName * caseName : string * fields : List<Expr>


and StringSegment =
  | StringText of string
  | StringInterpolation of Expr

/// Darklang's available types
/// - `int`
/// - `List<T>`
/// - user-defined enums
/// - etc.
type DType =
  | TInt
  | TFloat
  | TBool
  | TUnit
  | TStr
  | TList of DType
  | TTuple of DType * DType * List<DType>
  | TDict of DType
  | TIncomplete
  | TError
  | THttpResponse of DType
  | TDB of DType
  | TDateTime
  | TChar
  | TPassword
  | TUuid
  | TOption of DType
  | TUserType of UserTypeName
  | TBytes
  | TResult of DType * DType
  // A named variable, eg `a` in `List<a>`, matches anything
  | TVariable of string // replaces TAny
  | TFn of List<DType> * DType // replaces TLambda
  | TRecord of List<string * DType>
  | TDbList of DType // TODO: cleanup and remove


module Handler =
  type CronInterval =
    | EveryDay
    | EveryWeek
    | EveryFortnight
    | EveryHour
    | Every12Hours
    | EveryMinute

  // We need to keep the IDs around until we get rid of them on the client
  type ids = { moduleID : id; nameID : id; modifierID : id }

  type Spec =
    | HTTP of route : string * method : string * ids : ids
    | Worker of name : string * ids : ids
    | Cron of name : string * interval : Option<CronInterval> * ids : ids
    | REPL of name : string * ids : ids

  type T = { tlid : tlid; ast : Expr; spec : Spec }


module DB =
  type Col = { name : Option<string>; typ : Option<DType>; nameID : id; typeID : id }

  type T =
    { tlid : tlid
      name : string
      nameID : id
      version : int
      cols : List<Col> }

module UserType =
  // TODO: move this to some ComplexType or CustomType type
  type RecordField = { id : id; name : string; typ : DType }

  type EnumField = { id : id; type_ : DType; label : Option<string> }
  type EnumCase = { id : id; name : string; fields : List<EnumField> }

  type Definition =
    // TODO: records need at least 1 field - model this.
    | Record of fields : List<RecordField>
    | Enum of firstCase : EnumCase * additionalCases : List<EnumCase>

  type T = { tlid : tlid; name : UserTypeName; definition : Definition }

module UserFunction =
  type Parameter = { id : id; name : string; typ : DType; description : string }

  type T =
    { tlid : tlid
      name : string
      parameters : List<Parameter>
      returnType : DType
      description : string
      infix : bool
      body : Expr }

module Toplevel =
  type T =
    | TLHandler of Handler.T
    | TLDB of DB.T
    | TLFunction of UserFunction.T
    | TLType of UserType.T

  let toTLID (tl : T) : tlid =
    match tl with
    | TLHandler h -> h.tlid
    | TLDB db -> db.tlid
    | TLFunction f -> f.tlid
    | TLType t -> t.tlid


/// An Operation on a Canvas
///
/// "Op" is an abbreviation for Operation,
/// and is preferred throughout code and documentation.
type Op =
  | SetHandler of tlid * Handler.T
  | CreateDB of tlid * string
  | AddDBCol of tlid * id * id
  | SetDBColName of tlid * id * string
  | SetDBColType of tlid * id * string
  | DeleteTL of tlid // CLEANUP move Deletes to API calls instead of Ops
  | SetFunction of UserFunction.T
  | ChangeDBColName of tlid * id * string
  | ChangeDBColType of tlid * id * string
  | UndoTL of tlid
  | RedoTL of tlid
  | SetExpr of tlid * id * Expr
  | TLSavepoint of tlid
  | DeleteFunction of tlid // CLEANUP move Deletes to API calls instead of Ops
  | DeleteDBCol of tlid * id
  | RenameDBname of tlid * string
  | CreateDBWithBlankOr of tlid * id * string
  | SetType of UserType.T
  | DeleteType of tlid // CLEANUP move Deletes to API calls instead of Ops

type Oplist = List<Op>

type TLIDOplists = List<tlid * Oplist>

module Secret =
  type T = { name : string; value : string }

module Package =
  type Parameter = { name : string; typ : DType; description : string }

  type Fn =
    { name : FQFnName.PackageFnName
      body : Expr
      parameters : List<Parameter>
      returnType : DType
      description : string
      author : string
      deprecated : bool
      tlid : tlid }