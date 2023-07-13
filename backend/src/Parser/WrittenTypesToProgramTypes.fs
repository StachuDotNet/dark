/// Conversion functions from WrittenTypes to ProgramTypes
module Parser.WrittenTypesToProgramTypes

open Prelude
open Tablecloth

module WT = WrittenTypes
module PT = LibExecution.ProgramTypes
module FS2WT = FSharpToWrittenTypes

type NameResolver =
  { userFns : Set<PT.FnName.UserProgram>
    userTypes : Set<PT.TypeName.UserProgram>
    builtinFns : Set<PT.FnName.BuiltIn>
    builtinTypes : Set<PT.TypeName.BuiltIn> }

module NameResolver =
  let empty : NameResolver =
    { userFns = Set.empty
      userTypes = Set.empty
      builtinFns = Set.empty
      builtinTypes = Set.empty }

  // TODO: there's a lot going on her to resolve the outer portion of the name and to
  // avoid repeat work for package/modules, but it's also adding a lot of code and
  // complexity. Perhaps this changes when packages become identified by ID


  // Desired resolution order:
  // - 1. PACKAGE.* -> Exact package name
  // - 2. Check exact name is a user name
  // - 3. Check exact name is a builtin name
  // - 4. Check exact name in
  //   - a. current module // NOT IMPLEMENTED
  //   - b. parent module(s) // NOT IMPLEMENTED
  //   - c. darklang.stdlib package space // NOT IMPLEMENTED
  let resolveName
    (nameValidator : PT.FQName.NameValidator<'name>)
    (constructor : string -> 'name)
    (parser : string -> Result<string * int, string>)
    (userThings : Set<PT.FQName.UserProgram<'name>>)
    (builtinThings : Set<PT.FQName.BuiltIn<'name>>)
    (name : WT.Name)
    : PT.NameResolution<PT.FQName.T<'name>> =

    // These are named exactly during parsing
    match name with
    | WT.KnownBuiltin(modules, name, version) ->
      // TODO assert it's a valid builtin name
      Ok(PT.FQName.fqBuiltIn nameValidator modules (constructor name) version)

    // Packages are unambiguous
    | WT.Unresolved(("PACKAGE" :: owner :: rest) as names) ->
      match List.rev rest with
      | [] -> Error(LibExecution.Errors.MissingModuleName names)
      | name :: moduleLast :: moduleInit ->
        match parser name with
        | Ok(name, version) ->
          let modules = List.rev (moduleLast :: moduleInit) |> NonEmptyList.ofList
          Ok(
            PT.FQName.fqPackage
              nameValidator
              owner
              modules
              (constructor name)
              version
          )
        | Error _ -> Error(LibExecution.Errors.InvalidPackageName names)
      | _ -> Error(LibExecution.Errors.InvalidPackageName names)

    | WT.Unresolved names ->
      match List.rev names with
      | [] -> Error(LibExecution.Errors.MissingModuleName names)
      | name :: moduleLast :: moduleInit ->
        match parser name with
        | Error _msg -> Error(LibExecution.Errors.InvalidPackageName names)
        | Ok(name, version) ->
          let modules = List.rev (moduleLast :: moduleInit)

          // 2. Exact name is UserProgram
          let (userProgram : PT.FQName.UserProgram<'name>) =
            { modules = modules; name = constructor name; version = version }
          if Set.contains userProgram userThings then
            Ok(
              PT.FQName.fqUserProgram
                nameValidator
                modules
                (constructor name)
                version
            )
          else

            // 3. Exact name is BuiltIn
            let (builtIn : PT.FQName.BuiltIn<'name>) =
              { modules = modules; name = constructor name; version = version }
            if Set.contains builtIn builtinThings then
              Ok(PT.FQName.BuiltIn builtIn)
            else

              // 4. Check exact name in
              //   - a. current module // NOT IMPLEMENTED
              //   - b. parent module(s) // NOT IMPLEMENTED
              //   - c. darklang.stdlib package space
              Error(LibExecution.Errors.NotFound names)
      | _ -> Error(LibExecution.Errors.MissingModuleName names)


  module TypeName =
    let resolve
      (resolver : NameResolver)
      (name : WT.Name)
      : PT.NameResolution<PT.TypeName.T> =
      resolveName
        PT.TypeName.assert'
        PT.TypeName.TypeName
        // TODO: move parsing fn into PT or WT
        FS2WT.Expr.parseTypeName
        resolver.userTypes
        resolver.builtinTypes
        name

  module FnName =
    let resolve
      (resolver : NameResolver)
      (name : WT.Name)
      : PT.NameResolution<PT.FnName.T> =
      resolveName
        PT.FnName.assert'
        PT.FnName.FnName
        // TODO: move parsing fn into PT or WT
        FS2WT.Expr.parseFn
        resolver.userFns
        resolver.builtinFns
        name


module InfixFnName =
  let toPT (name : WT.InfixFnName) : PT.InfixFnName =
    match name with
    | WT.ArithmeticPlus -> PT.ArithmeticPlus
    | WT.ArithmeticMinus -> PT.ArithmeticMinus
    | WT.ArithmeticMultiply -> PT.ArithmeticMultiply
    | WT.ArithmeticDivide -> PT.ArithmeticDivide
    | WT.ArithmeticModulo -> PT.ArithmeticModulo
    | WT.ArithmeticPower -> PT.ArithmeticPower
    | WT.ComparisonGreaterThan -> PT.ComparisonGreaterThan
    | WT.ComparisonGreaterThanOrEqual -> PT.ComparisonGreaterThanOrEqual
    | WT.ComparisonLessThan -> PT.ComparisonLessThan
    | WT.ComparisonLessThanOrEqual -> PT.ComparisonLessThanOrEqual
    | WT.ComparisonEquals -> PT.ComparisonEquals
    | WT.ComparisonNotEquals -> PT.ComparisonNotEquals
    | WT.StringConcat -> PT.StringConcat

module TypeReference =
  let rec toPT
    (nameResolver : NameResolver)
    (t : WT.TypeReference)
    : PT.TypeReference =
    let toPT = toPT nameResolver
    match t with
    | WT.TInt -> PT.TInt
    | WT.TFloat -> PT.TFloat
    | WT.TBool -> PT.TBool
    | WT.TUnit -> PT.TUnit
    | WT.TString -> PT.TString
    | WT.TList typ -> PT.TList(toPT typ)
    | WT.TTuple(firstType, secondType, otherTypes) ->
      PT.TTuple(toPT firstType, toPT secondType, List.map toPT otherTypes)
    | WT.TDict typ -> PT.TDict(toPT typ)
    | WT.TDB typ -> PT.TDB(toPT typ)
    | WT.TDateTime -> PT.TDateTime
    | WT.TChar -> PT.TChar
    | WT.TPassword -> PT.TPassword
    | WT.TUuid -> PT.TUuid
    | WT.TCustomType(t, typeArgs) -> PT.TCustomType(t, List.map toPT typeArgs)
    | WT.TBytes -> PT.TBytes
    | WT.TVariable(name) -> PT.TVariable(name)
    | WT.TFn(paramTypes, returnType) ->
      PT.TFn(List.map toPT paramTypes, toPT returnType)

module BinaryOperation =
  let toPT (binop : WT.BinaryOperation) : PT.BinaryOperation =
    match binop with
    | WT.BinOpAnd -> PT.BinOpAnd
    | WT.BinOpOr -> PT.BinOpOr

module Infix =
  let toPT (infix : WT.Infix) : PT.Infix =
    match infix with
    | WT.InfixFnCall(fn) -> PT.InfixFnCall(InfixFnName.toPT fn)
    | WT.BinOp binop -> PT.BinOp(BinaryOperation.toPT binop)

module LetPattern =
  let rec toPT (p : WT.LetPattern) : PT.LetPattern =
    match p with
    | WT.LPVariable(id, str) -> PT.LPVariable(id, str)
    | WT.LPTuple(id, first, second, theRest) ->
      PT.LPTuple(id, toPT first, toPT second, List.map toPT theRest)

module MatchPattern =
  let rec toPT (p : WT.MatchPattern) : PT.MatchPattern =
    match p with
    | WT.MPVariable(id, str) -> PT.MPVariable(id, str)
    | WT.MPEnum(id, caseName, fieldPats) ->
      PT.MPEnum(id, caseName, List.map toPT fieldPats)
    | WT.MPInt(id, i) -> PT.MPInt(id, i)
    | WT.MPBool(id, b) -> PT.MPBool(id, b)
    | WT.MPChar(id, c) -> PT.MPChar(id, c)
    | WT.MPString(id, s) -> PT.MPString(id, s)
    | WT.MPFloat(id, s, w, f) -> PT.MPFloat(id, s, w, f)
    | WT.MPUnit id -> PT.MPUnit id
    | WT.MPTuple(id, first, second, theRest) ->
      PT.MPTuple(id, toPT first, toPT second, List.map toPT theRest)
    | WT.MPList(id, pats) -> PT.MPList(id, List.map toPT pats)
    | WT.MPListCons(id, head, tail) -> PT.MPListCons(id, toPT head, toPT tail)


module Expr =
  let rec toPT (resolver : NameResolver) (e : WT.Expr) : PT.Expr =
    let toPT = toPT resolver
    match e with
    | WT.EChar(id, char) -> PT.EChar(id, char)
    | WT.EInt(id, num) -> PT.EInt(id, num)
    | WT.EString(id, segment) ->
      PT.EString(id, List.map (stringSegmentToPT resolver) segment)
    | WT.EFloat(id, sign, whole, fraction) -> PT.EFloat(id, sign, whole, fraction)
    | WT.EBool(id, b) -> PT.EBool(id, b)
    | WT.EUnit id -> PT.EUnit id
    | WT.EVariable(id, var) -> PT.EVariable(id, var)
    | WT.EFieldAccess(id, obj, fieldname) -> PT.EFieldAccess(id, toPT obj, fieldname)
    | WT.EApply(id, WT.FnTargetName name, typeArgs, args) ->
      let name = NameResolver.FnName.resolve resolver name
      PT.EApply(
        id,
        PT.FnTargetName name,
        List.map (TypeReference.toPT resolver) typeArgs,
        List.map toPT args
      )
    | WT.EApply(id, WT.FnTargetExpr name, typeArgs, args) ->
      PT.EApply(
        id,
        PT.FnTargetExpr(toPT name),
        List.map (TypeReference.toPT resolver) typeArgs,
        List.map toPT args
      )
    | WT.ELambda(id, vars, body) -> PT.ELambda(id, vars, toPT body)
    | WT.ELet(id, pat, rhs, body) ->
      PT.ELet(id, LetPattern.toPT pat, toPT rhs, toPT body)
    | WT.EIf(id, cond, thenExpr, elseExpr) ->
      PT.EIf(id, toPT cond, toPT thenExpr, toPT elseExpr)
    | WT.EList(id, exprs) -> PT.EList(id, List.map toPT exprs)
    | WT.ETuple(id, first, second, theRest) ->
      PT.ETuple(id, toPT first, toPT second, List.map toPT theRest)
    | WT.ERecord(id, typeName, fields) ->
      PT.ERecord(
        id,
        NameResolver.TypeName.resolve resolver typeName,
        List.map (Tuple2.mapSecond toPT) fields
      )
    | WT.ERecordUpdate(id, record, updates) ->
      PT.ERecordUpdate(
        id,
        toPT record,
        updates |> List.map (fun (name, expr) -> (name, toPT expr))
      )
    | WT.EPipe(pipeID, expr1, expr2, rest) ->
      PT.EPipe(
        pipeID,
        toPT expr1,
        pipeExprToPT resolver expr2,
        List.map (pipeExprToPT resolver) rest
      )
    | WT.EEnum(id, typeName, caseName, exprs) ->
      // NAMETODO: Should we be checking casenames here? At least for warnings? Do we
      // check them in the interpreter at construction time?
      PT.EEnum(
        id,
        NameResolver.TypeName.resolve resolver typeName,
        caseName,
        List.map toPT exprs
      )
    | WT.EMatch(id, mexpr, pairs) ->
      PT.EMatch(
        id,
        toPT mexpr,
        List.map (Tuple2.mapFirst MatchPattern.toPT << Tuple2.mapSecond toPT) pairs
      )
    | WT.EInfix(id, infix, arg1, arg2) ->
      PT.EInfix(id, Infix.toPT infix, toPT arg1, toPT arg2)
    | WT.EDict(id, pairs) -> PT.EDict(id, List.map (Tuple2.mapSecond toPT) pairs)

  and stringSegmentToPT
    (resolver : NameResolver)
    (segment : WT.StringSegment)
    : PT.StringSegment =
    match segment with
    | WT.StringText text -> PT.StringText text
    | WT.StringInterpolation expr -> PT.StringInterpolation(toPT resolver expr)

  and pipeExprToPT (resolver : NameResolver) (pipeExpr : WT.PipeExpr) : PT.PipeExpr =
    let toPT = toPT resolver
    match pipeExpr with
    | WT.EPipeVariable(id, name) -> PT.EPipeVariable(id, name)
    | WT.EPipeLambda(id, args, body) -> PT.EPipeLambda(id, args, toPT body)
    | WT.EPipeInfix(id, infix, first) ->
      PT.EPipeInfix(id, Infix.toPT infix, toPT first)
    | WT.EPipeFnCall(id, name, typeArgs, args) ->
      PT.EPipeFnCall(
        id,
        NameResolver.FnName.resolve resolver name,
        List.map (TypeReference.toPT resolver) typeArgs,
        List.map toPT args
      )
    | WT.EPipeEnum(id, typeName, caseName, fields) ->
      PT.EPipeEnum(
        id,
        NameResolver.TypeName.resolve resolver typeName,
        caseName,
        List.map toPT fields
      )


module TypeDeclaration =
  module RecordField =

    let toPT
      (resolver : NameResolver)
      (f : WT.TypeDeclaration.RecordField)
      : PT.TypeDeclaration.RecordField =
      { name = f.name
        typ = TypeReference.toPT resolver f.typ
        description = f.description }

  module EnumField =

    let toPT
      (resolver : NameResolver)
      (f : WT.TypeDeclaration.EnumField)
      : PT.TypeDeclaration.EnumField =
      { typ = TypeReference.toPT resolver f.typ
        label = f.label
        description = f.description }

  module EnumCase =

    let toPT
      (resolver : NameResolver)
      (c : WT.TypeDeclaration.EnumCase)
      : PT.TypeDeclaration.EnumCase =
      { name = c.name
        fields = List.map (EnumField.toPT resolver) c.fields
        description = c.description }

  module Definition =
    let toPT
      (resolver : NameResolver)
      (d : WT.TypeDeclaration.Definition)
      : PT.TypeDeclaration.Definition =
      match d with
      | WT.TypeDeclaration.Alias typ ->
        PT.TypeDeclaration.Alias(TypeReference.toPT resolver typ)
      | WT.TypeDeclaration.Record(firstField, additionalFields) ->
        PT.TypeDeclaration.Record(
          RecordField.toPT resolver firstField,
          List.map (RecordField.toPT resolver) additionalFields
        )
      | WT.TypeDeclaration.Enum(firstCase, additionalCases) ->
        PT.TypeDeclaration.Enum(
          EnumCase.toPT resolver firstCase,
          List.map (EnumCase.toPT resolver) additionalCases
        )


  let toPT
    (resolver : NameResolver)
    (d : WT.TypeDeclaration.T)
    : PT.TypeDeclaration.T =
    { typeParams = d.typeParams; definition = Definition.toPT resolver d.definition }


module Handler =
  module CronInterval =
    let toPT (ci : WT.Handler.CronInterval) : PT.Handler.CronInterval =
      match ci with
      | WT.Handler.EveryDay -> PT.Handler.EveryDay
      | WT.Handler.EveryWeek -> PT.Handler.EveryWeek
      | WT.Handler.EveryFortnight -> PT.Handler.EveryFortnight
      | WT.Handler.EveryHour -> PT.Handler.EveryHour
      | WT.Handler.Every12Hours -> PT.Handler.Every12Hours
      | WT.Handler.EveryMinute -> PT.Handler.EveryMinute

  module Spec =
    let toPT (s : WT.Handler.Spec) : PT.Handler.Spec =
      match s with
      | WT.Handler.HTTP(route, method) -> PT.Handler.HTTP(route, method)
      | WT.Handler.Worker name -> PT.Handler.Worker name
      | WT.Handler.Cron(name, interval) ->
        PT.Handler.Cron(name, CronInterval.toPT interval)
      | WT.Handler.REPL name -> PT.Handler.REPL name

  let toPT (nameResolver : NameResolver) (h : WT.Handler.T) : PT.Handler.T =
    { tlid = gid (); ast = Expr.toPT nameResolver h.ast; spec = Spec.toPT h.spec }

module DB =
  let toPT (nameResolver : NameResolver) (db : WT.DB.T) : PT.DB.T =
    { tlid = gid ()
      name = db.name
      version = db.version
      typ = TypeReference.toPT nameResolver db.typ }

module UserType =
  let toPT (resolver : NameResolver) (t : WT.UserType.T) : PT.UserType.T =
    { tlid = gid ()
      name = t.name
      declaration = TypeDeclaration.toPT resolver t.declaration
      description = t.description
      deprecated = PT.NotDeprecated }


module UserFunction =
  module Parameter =
    let toPT
      (resolver : NameResolver)
      (p : WT.UserFunction.Parameter)
      : PT.UserFunction.Parameter =
      { name = p.name
        typ = TypeReference.toPT resolver p.typ
        description = p.description }

  let toPT (resolver : NameResolver) (f : WT.UserFunction.T) : PT.UserFunction.T =
    { tlid = gid ()
      name = f.name
      typeParams = f.typeParams
      parameters = List.map (Parameter.toPT resolver) f.parameters
      returnType = TypeReference.toPT resolver f.returnType
      description = f.description
      deprecated = PT.NotDeprecated
      body = Expr.toPT resolver f.body }

module PackageFn =
  module Parameter =
    let toPT
      (resolver : NameResolver)
      (p : WT.PackageFn.Parameter)
      : PT.PackageFn.Parameter =
      { name = p.name
        typ = TypeReference.toPT resolver p.typ
        description = p.description }

  let toPT (resolver : NameResolver) (fn : WT.PackageFn.T) : PT.PackageFn.T =
    { name = fn.name
      parameters = List.map (Parameter.toPT resolver) fn.parameters
      returnType = TypeReference.toPT resolver fn.returnType
      description = fn.description
      deprecated = PT.NotDeprecated
      body = Expr.toPT resolver fn.body
      typeParams = fn.typeParams
      id = System.Guid.NewGuid()
      tlid = gid () }

module PackageType =
  let toPT (resolver : NameResolver) (pt : WT.PackageType.T) : PT.PackageType.T =
    { name = pt.name
      description = pt.description
      declaration = TypeDeclaration.toPT resolver pt.declaration
      deprecated = PT.NotDeprecated
      id = System.Guid.NewGuid()
      tlid = gid () }
