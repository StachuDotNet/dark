/// Where the bytes in an op actually go.
///
/// A first sync moves hundreds of megabytes for a package set whose SOURCE is a few megabytes, which is
/// the wrong shape by an order of magnitude. This walks the real op log and attributes every byte of
/// every op to what put it there, so the answer is measured rather than argued.
///
/// The accounting is exact rather than sampled. Each category's cost is known from the serializer:
/// an id is a fixed 8 bytes (`w.Write (id : uint64)`), a hash is written as a 64-char UTF-8 string so it
/// costs 65 with its length prefix, and every other string costs its UTF-8 length plus a varint. So
/// counting the structures gives byte totals directly, and whatever the categories do not explain is
/// reported as the remainder rather than quietly dropped.
module LocalExec.Experiments.OpAnatomy

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude

open Fumble
open LibDB.Sqlite

module PT = LibExecution.ProgramTypes
module BS = LibSerialization.Binary.Serialization


/// What a category cost, and how many of it there were.
type Bucket = { mutable count : int64; mutable bytes : int64 }

type Tally =
  {
    ids : Bucket
    hashes : Bucket
    /// Identifiers: variable names, field names, case names, parameter names, type variables.
    names : Bucket
    /// Doc comments: the fn/type/value description and per-parameter descriptions.
    docs : Bucket
    /// Literal content the program actually contains: strings, chars, floats.
    literals : Bucket
    /// The name AS TYPED, kept beside the resolution: `["Stdlib"; "List"; "map"]`.
    originalNames : Bucket
    /// The resolved location kept beside the hash: owner, modules and name, again.
    locations : Bucket
    mutable totalBlob : int64
    mutable ops : int64
    /// Distinct hash VALUES seen. The gap between this and `hashes.count` is what a per-bundle
    /// dictionary would collapse: the same callee hash is written afresh at every call site.
    distinctHashes : System.Collections.Generic.HashSet<string>
    /// Distinct whole locations (`owner|modules|name`), and how many times one was written.
    distinctLocations : System.Collections.Generic.HashSet<string>
    mutable locationUses : int64
    /// Distinct as-typed names, and how many times one was written.
    distinctTyped : System.Collections.Generic.HashSet<string>
    mutable typedUses : int64
    /// Every distinct name SEGMENT ("Darklang", "Stdlib", "List", "map"), across both.
    distinctSegments : System.Collections.Generic.HashSet<string>
    /// The location each hash was seen bound to. A hash and the name it had are the SAME entity, so
    /// one dictionary can carry both and a reference becomes a single index into it.
    hashToLoc : System.Collections.Generic.Dictionary<string, string>
  }

let private bucket () = { count = 0L; bytes = 0L }

let private emptyTally () =
  { ids = bucket ()
    hashes = bucket ()
    names = bucket ()
    docs = bucket ()
    literals = bucket ()
    originalNames = bucket ()
    locations = bucket ()
    totalBlob = 0L
    ops = 0L
    distinctHashes = System.Collections.Generic.HashSet<string>()
    distinctLocations = System.Collections.Generic.HashSet<string>()
    locationUses = 0L
    distinctTyped = System.Collections.Generic.HashSet<string>()
    typedUses = 0L
    distinctSegments = System.Collections.Generic.HashSet<string>()
    hashToLoc = System.Collections.Generic.Dictionary<string, string>() }

let private add (b : Bucket) (bytes : int) =
  b.count <- b.count + 1L
  b.bytes <- b.bytes + int64 bytes

/// A varint costs one byte per 7 bits, which is what `String.write` prefixes every string with.
let private varintLen (n : int) : int =
  if n < 128 then 1
  elif n < 16384 then 2
  elif n < 2097152 then 3
  elif n < 268435456 then 4
  else 5

let private utf8 (s : string) : int =
  if isNull s then 0 else System.Text.Encoding.UTF8.GetByteCount s

/// A string costs its bytes plus the varint that frames it.
let private strCost (s : string) : int = let n = utf8 s in n + varintLen n

// A hash is a 1-byte form tag plus 32 raw bytes. It was `String.write` of 64 hex characters (65 bytes
// with its varint) until 2026-08-29, which cost 65 to carry 32 on the single largest category in the log.
let private hashCost = 33

// Every id-carrying node writes exactly 8 bytes, unframed.
//
// Kept, after a round trip through removing them. They are random and the content hash ignores them, but
// they are what a stored trace uses to point at a lambda, and freezing them in the bytes is what makes a
// trace recorded in one process still match in the next. Minting them on read instead broke that.
//
// This row is the one to change if that ever changes again: it read 0 for a while and the structure
// remainder went NEGATIVE, which is the accounting saying it is wrong.
let private idCost = 8


let private countId (t : Tally) = add t.ids idCost
let private countHash (t : Tally) (PT.Hash h : PT.Hash) =
  add t.hashes hashCost
  let _ : bool = t.distinctHashes.Add h
  ()
let private countName (t : Tally) (s : string) = add t.names (strCost s)
let private countDoc (t : Tally) (s : string) = add t.docs (strCost s)
let private countLiteral (t : Tally) (s : string) = add t.literals (strCost s)



/// A resolved name costs three separate things, and this is where most of the surprise lives:
/// the name AS THE AUTHOR TYPED IT (a list of strings), then the 64-char hash it resolved to, then
/// optionally the full location it resolved to (owner, modules, name) -- so a single call site can
/// carry the same name three times over.
let private countNameRes
  (t : Tally)
  (countPayload : Tally -> 'a -> unit)
  (hashOfPayload : 'a -> Option<string>)
  (nr : PT.NameResolution<'a>)
  : unit =
  add t.originalNames (varintLen (List.length nr.originalName))
  nr.originalName |> List.iter (fun s -> add t.originalNames (strCost s))

  if not (List.isEmpty nr.originalName) then
    t.typedUses <- t.typedUses + 1L
    let _ : bool = t.distinctTyped.Add(String.concat "." nr.originalName)
    nr.originalName
    |> List.iter (fun seg -> t.distinctSegments.Add seg |> ignore<bool>)

  match nr.resolved with
  | Ok r ->
    countPayload t r.name

    match r.location with
    | Some loc ->
      (match hashOfPayload r.name with
       | Some h ->
         t.hashToLoc[h] <-
           loc.owner + "." + String.concat "." loc.modules + "." + loc.name
       | None -> ())

      add t.locations (strCost loc.owner)
      add t.locations (varintLen (List.length loc.modules))
      loc.modules |> List.iter (fun m -> add t.locations (strCost m))
      add t.locations (strCost loc.name)

      t.locationUses <- t.locationUses + 1L

      let joined = loc.owner + "|" + String.concat "." loc.modules + "|" + loc.name
      let _ : bool = t.distinctLocations.Add joined
      t.distinctSegments.Add loc.owner |> ignore<bool>
      t.distinctSegments.Add loc.name |> ignore<bool>
      loc.modules |> List.iter (fun m -> t.distinctSegments.Add m |> ignore<bool>)
    | None -> ()
  | Error _ -> ()


let private hashOfFn (n : PT.FQFnName.FQFnName) : Option<string> =
  match n with
  | PT.FQFnName.Package(PT.Hash h) -> Some h
  | PT.FQFnName.Builtin _ -> None

let private hashOfType (n : PT.FQTypeName.FQTypeName) : Option<string> =
  match n with
  | PT.FQTypeName.Package(PT.Hash h) -> Some h

let private hashOfValue (n : PT.FQValueName.FQValueName) : Option<string> =
  match n with
  | PT.FQValueName.Package(PT.Hash h) -> Some h
  | PT.FQValueName.Builtin _ -> None

let private payloadFn (t : Tally) (n : PT.FQFnName.FQFnName) : unit =
  match n with
  | PT.FQFnName.Package h -> countHash t h
  | PT.FQFnName.Builtin b -> countName t b.name

let private payloadType (t : Tally) (n : PT.FQTypeName.FQTypeName) : unit =
  match n with
  | PT.FQTypeName.Package h -> countHash t h

let private payloadValue (t : Tally) (n : PT.FQValueName.FQValueName) : unit =
  match n with
  | PT.FQValueName.Package h -> countHash t h
  | PT.FQValueName.Builtin b -> countName t b.name


let rec private walkTypeRef (t : Tally) (tr : PT.TypeReference) : unit =
  match tr with
  | PT.TUnit
  | PT.TBool
  | PT.TInt8
  | PT.TUInt8
  | PT.TInt16
  | PT.TUInt16
  | PT.TInt32
  | PT.TUInt32
  | PT.TInt64
  | PT.TUInt64
  | PT.TInt128
  | PT.TUInt128
  | PT.TInt
  | PT.TFloat
  | PT.TChar
  | PT.TString
  | PT.TUuid
  | PT.TDateTime
  | PT.TBlob -> ()
  | PT.TStream inner
  | PT.TList inner
  | PT.TDB inner -> walkTypeRef t inner
  | PT.TDict(key, value) ->
    walkTypeRef t key
    walkTypeRef t value
  | PT.TTuple(a, b, rest) ->
    walkTypeRef t a
    walkTypeRef t b
    List.iter (walkTypeRef t) rest
  | PT.TCustomType(name, args) ->
    countNameRes t payloadType hashOfType name
    List.iter (walkTypeRef t) args
  | PT.TFn(args, ret) ->
    NEList.iter (walkTypeRef t) args
    walkTypeRef t ret
  | PT.TVariable v -> countName t v


let rec private walkLetPattern (t : Tally) (p : PT.LetPattern) : unit =
  countId t

  match p with
  | PT.LPVariable(_, name) -> countName t name
  | PT.LPWildcard _ -> ()
  | PT.LPUnit _ -> ()
  | PT.LPTuple(_, a, b, rest) ->
    walkLetPattern t a
    walkLetPattern t b
    List.iter (walkLetPattern t) rest


let rec private walkMatchPattern (t : Tally) (p : PT.MatchPattern) : unit =
  countId t

  match p with
  | PT.MPUnit _
  | PT.MPBool _
  | PT.MPInt8 _
  | PT.MPUInt8 _
  | PT.MPInt16 _
  | PT.MPUInt16 _
  | PT.MPInt32 _
  | PT.MPUInt32 _
  | PT.MPInt64 _
  | PT.MPUInt64 _
  | PT.MPInt128 _
  | PT.MPUInt128 _
  | PT.MPInt _ -> ()
  | PT.MPFloat(_, _, whole, part) ->
    countLiteral t whole
    countLiteral t part
  | PT.MPChar(_, c) -> countLiteral t c
  | PT.MPString(_, s) -> countLiteral t s
  | PT.MPVariable(_, name) -> countName t name
  | PT.MPList(_, pats) -> List.iter (walkMatchPattern t) pats
  | PT.MPListCons(_, h, tl) ->
    walkMatchPattern t h
    walkMatchPattern t tl
  | PT.MPTuple(_, a, b, rest) ->
    walkMatchPattern t a
    walkMatchPattern t b
    List.iter (walkMatchPattern t) rest
  | PT.MPEnum(_, caseName, fields) ->
    countName t caseName
    List.iter (walkMatchPattern t) fields
  | PT.MPOr(_, pats) -> NEList.iter (walkMatchPattern t) pats


let rec private walkExpr (t : Tally) (e : PT.Expr) : unit =
  countId t

  match e with
  | PT.EUnit _
  | PT.ESelf _
  | PT.EBool _
  | PT.EInt8 _
  | PT.EUInt8 _
  | PT.EInt16 _
  | PT.EUInt16 _
  | PT.EInt32 _
  | PT.EUInt32 _
  | PT.EInt64 _
  | PT.EUInt64 _
  | PT.EInt128 _
  | PT.EUInt128 _
  | PT.EInt _
  | PT.EArg _ -> ()

  | PT.EFloat(_, _, whole, part) ->
    countLiteral t whole
    countLiteral t part
  | PT.EChar(_, c) -> countLiteral t c
  | PT.EString(_, segments) ->
    segments
    |> List.iter (fun seg ->
      match seg with
      | PT.StringText s -> countLiteral t s
      | PT.StringInterpolation e -> walkExpr t e)

  | PT.EVariable(_, name) -> countName t name

  | PT.EIf(_, cond, thenE, elseE) ->
    walkExpr t cond
    walkExpr t thenE
    Option.iter (walkExpr t) elseE

  | PT.EPipe(_, lhs, parts) ->
    walkExpr t lhs
    List.iter (walkPipeExpr t) parts

  | PT.EMatch(_, arg, cases) ->
    walkExpr t arg

    cases
    |> List.iter (fun c ->
      walkMatchPattern t c.pat
      Option.iter (walkExpr t) c.whenCondition
      walkExpr t c.rhs)

  | PT.ELet(_, pat, rhs, body) ->
    walkLetPattern t pat
    walkExpr t rhs
    walkExpr t body

  | PT.EList(_, exprs) -> List.iter (walkExpr t) exprs
  | PT.EDict(_, pairs) ->
    // Keys are expressions now, not names, so they are walked like any other subexpression.
    pairs
    |> List.iter (fun (k, v) ->
      walkExpr t k
      walkExpr t v)
  | PT.ETuple(_, a, b, rest) ->
    walkExpr t a
    walkExpr t b
    List.iter (walkExpr t) rest

  | PT.EApply(_, fn, typeArgs, args) ->
    walkExpr t fn
    List.iter (walkTypeRef t) typeArgs
    NEList.iter (walkExpr t) args

  | PT.EFnName(_, name) -> countNameRes t payloadFn hashOfFn name

  | PT.ELambda(_, pats, body) ->
    NEList.iter (walkLetPattern t) pats
    walkExpr t body

  | PT.EInfix(_, _, lhs, rhs) ->
    walkExpr t lhs
    walkExpr t rhs

  | PT.ERecord(_, typeName, typeArgs, fields) ->
    countNameRes t payloadType hashOfType typeName
    List.iter (walkTypeRef t) typeArgs

    fields
    |> List.iter (fun (f, e) ->
      countName t f
      walkExpr t e)

  | PT.ERecordFieldAccess(_, record, fieldName) ->
    walkExpr t record
    countName t fieldName

  | PT.ERecordUpdate(_, record, updates) ->
    walkExpr t record

    updates
    |> NEList.iter (fun (f, e) ->
      countName t f
      walkExpr t e)

  | PT.EEnum(_, typeName, typeArgs, caseName, fields) ->
    countNameRes t payloadType hashOfType typeName
    List.iter (walkTypeRef t) typeArgs
    countName t caseName
    List.iter (walkExpr t) fields

  | PT.EValue(_, name) -> countNameRes t payloadValue hashOfValue name

  | PT.EStatement(_, first, next) ->
    walkExpr t first
    walkExpr t next

and private walkPipeExpr (t : Tally) (p : PT.PipeExpr) : unit =
  countId t

  match p with
  | PT.EPipeLambda(_, pats, body) ->
    NEList.iter (walkLetPattern t) pats
    walkExpr t body
  | PT.EPipeInfix(_, _, e) -> walkExpr t e
  | PT.EPipeFnCall(_, name, typeArgs, args) ->
    countNameRes t payloadFn hashOfFn name
    List.iter (walkTypeRef t) typeArgs
    List.iter (walkExpr t) args
  | PT.EPipeEnum(_, typeName, caseName, fields) ->
    countNameRes t payloadType hashOfType typeName
    countName t caseName
    List.iter (walkExpr t) fields
  | PT.EPipeVariable(_, name, args) ->
    countName t name
    List.iter (walkExpr t) args


let private walkTypeDecl (t : Tally) (d : PT.TypeDeclaration.T) : unit =
  List.iter (countName t) d.typeParams

  match d.definition with
  | PT.TypeDeclaration.Alias tr -> walkTypeRef t tr
  | PT.TypeDeclaration.Record fields ->
    fields
    |> NEList.iter (fun f ->
      countName t f.name
      walkTypeRef t f.typ
      countDoc t f.description)
  | PT.TypeDeclaration.Enum cases ->
    cases
    |> NEList.iter (fun c ->
      countName t c.name
      countDoc t c.description

      c.fields
      |> List.iter (fun f ->
        walkTypeRef t f.typ
        Option.iter (countName t) f.label
        countDoc t f.description))


/// Attribute one op's bytes. The op's own blob length is the ground truth; everything the walk explains
/// is subtracted from it, and the rest is reported as structure.
let private walkOp (t : Tally) (op : PT.PackageOp) : unit =
  match op with
  | PT.PackageOp.AddFn fn ->
    countHash t fn.hash
    walkExpr t fn.body
    List.iter (countName t) fn.typeParams

    fn.parameters
    |> NEList.iter (fun p ->
      countName t p.name
      walkTypeRef t p.typ
      countDoc t p.description)

    walkTypeRef t fn.returnType
    countDoc t fn.description

  | PT.PackageOp.AddType typ ->
    countHash t typ.hash
    walkTypeDecl t typ.declaration
    countDoc t typ.description

  | PT.PackageOp.AddValue v ->
    countHash t v.hash
    countDoc t v.description
    walkExpr t v.body

  | PT.PackageOp.SetName(loc, _target, previous) ->
    countName t loc.owner
    List.iter (countName t) loc.modules
    countName t loc.name
    countHash t _target.hash

    match previous with
    | Some h -> countHash t h
    | None -> ()

  | PT.PackageOp.Unbind(loc, previous) ->
    countName t loc.owner
    List.iter (countName t) loc.modules
    countName t loc.name

    match previous with
    | Some h -> countHash t h
    | None -> ()

  | PT.PackageOp.Deprecate(target, kind, message) ->
    countHash t target.hash

    (match kind with
     | PT.DeprecationKind.SupersededBy r -> countHash t r.hash
     | _ -> ())

    countDoc t message

  | PT.PackageOp.Undeprecate target -> countHash t target.hash

  | PT.PackageOp.Decision(id, loc, reason, kind) ->
    countName t id
    countName t loc.owner
    List.iter (countName t) loc.modules
    countName t loc.name
    countDoc t reason

    match kind with
    | PT.DecisionKind.Override r -> countHash t r.hash
    | PT.DecisionKind.Ack findingId -> countName t findingId
    | PT.DecisionKind.Propagation _ -> ()

  | PT.PackageOp.BranchEvent(_, _, at) -> countName t at


let private pct (part : int64) (whole : int64) : string =
  if whole = 0L then "0.0%" else $"%.1f{100.0 * float part / float whole}%%"

let private mb (n : int64) : string = $"%.2f{float n / 1048576.0} MB"


/// `LocalExec experiments op-anatomy`
let run () : Ply<Result<unit, string>> =
  uply {
    let! rows =
      Sql.query "SELECT id, op_blob FROM package_ops ORDER BY rowid"
      |> Sql.executeAsync (fun read -> (read.uuid "id", read.bytes "op_blob"))

    let t = emptyTally ()
    let byKind = System.Collections.Generic.Dictionary<string, Tally>()
    let mutable undecodable = 0

    let kindOf (op : PT.PackageOp) =
      match op with
      | PT.PackageOp.AddFn _ -> "AddFn"
      | PT.PackageOp.AddType _ -> "AddType"
      | PT.PackageOp.AddValue _ -> "AddValue"
      | PT.PackageOp.SetName _ -> "SetName"
      | PT.PackageOp.Unbind _ -> "Unbind"
      | PT.PackageOp.Deprecate _ -> "Deprecate"
      | PT.PackageOp.Undeprecate _ -> "Undeprecate"
      | PT.PackageOp.Decision _ -> "Decision"
      | PT.PackageOp.BranchEvent _ -> "BranchEvent"

    for (opId, blob) in rows do
      match
        (try
          Some(BS.PT.PackageOp.deserialize opId blob)
         with _ ->
           None)
      with
      | None -> undecodable <- undecodable + 1
      | Some op ->
        let kind = kindOf op

        if not (byKind.ContainsKey kind) then byKind[kind] <- emptyTally ()
        let k = byKind[kind]

        // Walked into both the global tally and this kind's, so the per-kind rows sum to the total.
        for tally in [ t; k ] do
          tally.ops <- tally.ops + 1L
          tally.totalBlob <- tally.totalBlob + int64 blob.Length
          walkOp tally op

    let explained (x : Tally) =
      x.ids.bytes
      + x.hashes.bytes
      + x.names.bytes
      + x.docs.bytes
      + x.literals.bytes
      + x.originalNames.bytes
      + x.locations.bytes

    let line (label : string) (b : Bucket) (total : int64) =
      print
        $"""  %-14s{label} %12s{string b.count} %14s{string b.bytes} %10s{pct b.bytes total}"""

    print ""
    print "=== op anatomy: where the bytes go ==================================="
    print
      $"ops decoded: {t.ops}   undecodable: {undecodable}   total blob: {mb t.totalBlob}"
    print ""
    print $"""  %-14s{"category"} %12s{"count"} %14s{"bytes"} %10s{"share"}"""
    line "node ids" t.ids t.totalBlob
    line "hashes" t.hashes t.totalBlob
    line "names" t.names t.totalBlob
    line "doc comments" t.docs t.totalBlob
    line "literals" t.literals t.totalBlob
    line "typed names" t.originalNames t.totalBlob
    line "resolved locs" t.locations t.totalBlob

    let rest = t.totalBlob - explained t
    print
      $"""  %-14s{"structure"} %12s{"-"} %14s{string rest} %10s{pct rest t.totalBlob}"""

    print ""
    print "=== by op kind ======================================================="
    print
      $"""  %-12s{"kind"} %8s{"ops"} %12s{"bytes"} %9s{"mean"} %9s{"ids"} %9s{"hashes"}"""

    for KeyValue(kind, k) in
      byKind |> Seq.sortByDescending (fun kv -> kv.Value.totalBlob) do
      let mean = if k.ops = 0L then 0L else k.totalBlob / k.ops
      print
        $"""  %-12s{kind} %8s{string k.ops} %12s{string k.totalBlob} %9s{string mean} %9s{pct k.ids.bytes k.totalBlob} %9s{pct k.hashes.bytes k.totalBlob}"""
    print ""
    return Ok()
  }
