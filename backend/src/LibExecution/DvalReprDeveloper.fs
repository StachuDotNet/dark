/// Ways of converting Dvals to strings, intended for developers to read
module LibExecution.DvalReprDeveloper

open Prelude
open Tablecloth

open RuntimeTypes

let rec typeName (t : TypeReference) : string =
  match t with
  | TInt -> "Int"
  | TFloat -> "Float"
  | TBool -> "Bool"
  | TUnit -> "Unit"
  | TChar -> "Char"
  | TString -> "String"
  | TList nested -> $"List<{typeName nested}>"
  | TTuple(n1, n2, rest) ->
    let nested = (n1 :: n2 :: rest) |> List.map typeName |> String.concat ", "
    $"({nested})"
  | TDict nested -> $"Dict<{typeName nested}>"
  | TFn _ -> "Function"
  | TVariable varname -> $"'{varname}"
  | TDB _ -> "Datastore"
  | TDateTime -> "DateTime"
  | TPassword -> "Password"
  | TUuid -> "Uuid"
  | TCustomType(t, typeArgs) ->
    let typeArgsPortion =
      match typeArgs with
      | [] -> ""
      | args ->
        args
        |> List.map (fun t -> typeName t)
        |> String.concat ", "
        |> fun betweenBrackets -> "<" + betweenBrackets + ">"
    TypeName.toString t + typeArgsPortion
  | TBytes -> "Bytes"



let rec knownTypeName (vt : KnownType) : string =
  match vt with
  | KTInt -> "Int"
  | KTFloat -> "Float"
  | KTBool -> "Bool"
  | KTUnit -> "Unit"
  | KTChar -> "Char"
  | KTString -> "String"
  | KTDateTime -> "DateTime"
  | KTPassword -> "Password"
  | KTUuid -> "Uuid"
  | KTBytes -> "Bytes"

  | KTList typ -> $"List<{valueTypeName typ}>"
  | KTDict typ -> $"Dict<{valueTypeName typ}>"
  | KTDB typ -> $"Datastore<{valueTypeName typ}>"

  | KTFn (argTypes, retType) ->
    argTypes @ [retType]
    |> List.map valueTypeName
    |> String.concat " -> "
    |> fun s -> "(" + s + ")" // VTTODO: maybe not include ()?

  | KTTuple(t1, t2, trest) ->
    t1 :: t2 :: trest
    |> List.map valueTypeName
    |> String.concat ", "
    |> fun s -> $"({s})"

  | KTCustomType(name, typeArgs) ->
    let typeArgsPortion =
      match typeArgs with
      | [] -> ""
      | args ->
        args
        |> List.map (fun t -> valueTypeName t)
        |> String.concat ", "
        |> fun betweenBrackets -> "<" + betweenBrackets + ">"

    TypeName.toString name + typeArgsPortion

and valueTypeName (typ: ValueType): string =
  match typ with
  | Known typ -> knownTypeName typ
  | Unknown -> "_"

let toTypeName (dv : Dval) : string =
  dv
  |> Dval.toKnownType
  |> knownTypeName

// SERIALIZER_DEF Custom DvalReprDeveloper.toRepr
/// For printing something for the developer to read, as a live-value, error
/// message, etc. Redacts passwords.
///
/// Customers should not come to rely on this format. Do not use in stdlib fns
/// or other places a developer could rely on it (i.e. telemetry and error
/// messages are OK)
let toRepr (dv : Dval) : string =
  let rec toRepr_ (indent : int) (dv : Dval) : string =
    let makeSpaces len = "".PadRight(len, ' ')
    let nl = "\n" + makeSpaces indent
    let inl = "\n" + makeSpaces (indent + 2)
    let indent = indent + 2
    let typename = dv |> toTypeName
    let wrap str = $"<{typename}: {str}>"
    let justType = $"<{typename}>"

    match dv with
    | DPassword _ -> "<password>"
    | DString s -> $"\"{s}\""
    | DChar c -> $"'{c}'"
    | DInt i -> string i
    | DBool true -> "true"
    | DBool false -> "false"
    | DFloat f ->
      if System.Double.IsPositiveInfinity f then
        "Infinity"
      else if System.Double.IsNegativeInfinity f then
        "-Infinity"
      else if System.Double.IsNaN f then
        "NaN"
      else
        let result = sprintf "%.12g" f
        if result.Contains "." then result else $"{result}.0"
    | DUnit -> "unit"
    | DFnVal _ ->
      // TODO: we should print this, as this use case is safe
      // See docs/dblock-serialization.md
      justType
    | DIncomplete _ -> justType
    | DError(_, msg) -> $"<error: {msg}>"
    | DDateTime d -> wrap (DarkDateTime.toIsoString d)
    | DDB name -> wrap name
    | DUuid uuid -> wrap (string uuid)
    | DList (_, l) ->
      if List.isEmpty l then
        "[]"
      else
        let elems = String.concat ", " (List.map (toRepr_ indent) l)
        $"[{inl}{elems}{nl}]"
    | DTuple(first, second, theRest) ->
      let l = [ first; second ] @ theRest
      let elems = String.concat ", " (List.map (toRepr_ indent) l)
      $"({elems})"
    | DRecord(_, typeName, o) ->
      let strs =
        o
        |> Map.toList
        |> List.map (fun (key, value) -> ($"{key}: {toRepr_ indent value}"))

      let elems = String.concat $",{inl}" strs
      let typeStr = TypeName.toString typeName
      $"{typeStr} {{" + $"{inl}{elems}{nl}" + "}"
    | DDict o ->
      if Map.isEmpty o then
        "{}"
      else
        let strs =
          o
          |> Map.toList
          |> List.map (fun (key, value) -> ($"{key}: {toRepr_ indent value}"))

        let elems = String.concat $",{inl}" strs
        "{" + $"{inl}{elems}{nl}" + "}"
    | DBytes bytes -> Base64.defaultEncodeToString bytes
    | DEnum(_, typeName, caseName, fields) ->
      let fieldStr =
        fields |> List.map (fun value -> toRepr_ indent value) |> String.concat ", "

      let fieldStr = if fieldStr = "" then "" else $"({fieldStr})"

      let typeStr = TypeName.toString typeName
      $"{typeStr}.{caseName}{fieldStr}"


  toRepr_ 0 dv
