/// Generators
module FuzzTests.Generators

open System
open FsCheck
open NodaTime

open Prelude
open Prelude.Tablecloth
open Tablecloth
open TestUtils.TestUtils

module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes

/// List of all a..z, A..Z, 0..9, and _ characters
let alphaNumericCharacters =
  List.concat [ [ 'a' .. 'z' ]; [ '0' .. '9' ]; [ 'A' .. 'Z' ]; [ '_' ] ]

/// Generates a string that 'normalizes' successfully
let safeUnicodeString =
  /// We disallow `\u0000` because Postgres doesn't like it
  let isSafeString (s : string) : bool = s <> null && not (s.Contains('\u0000'))

  let normalizesSuccessfully (s : string) : bool =
    try
      String.normalize s |> ignore<string>
      true
    with
    | e ->
      // debuG
      //   "Failed to normalize :"
      //   $"{e}\n '{s}': (len {s.Length}, {System.BitConverter.ToString(toBytes s)})"

      false

  Arb.generate<UnicodeString>
  |> Gen.map (fun (UnicodeString s) -> s)
  |> Gen.filter normalizesSuccessfully
  // Now that we know it can be normalized, actually normalize it
  |> Gen.map String.normalize
  |> Gen.filter isSafeString

let SafeUnicodeString = safeUnicodeString |> Arb.fromGen

let char : Gen<string> =
  safeUnicodeString
  |> Gen.map String.toEgcSeq
  |> Gen.map Seq.toList
  |> Gen.map List.head
  |> Gen.filter Option.isSome
  |> Gen.map (Option.defaultValue "")
  |> Gen.filter ((<>) "")

/// Generates an `int` >= 0
let nonNegativeInt =
  gen {
    let! (NonNegativeInt i) = Arb.generate<NonNegativeInt>
    return i
  }

let safeFloat =
  gen {
    let specials = interestingFloats |> List.map Tuple2.second |> Gen.elements

    return! Gen.frequency [ (5, specials); (5, Arb.generate<float>) ]
  }

let SafeFloat = Arb.fromGen safeFloat

let safeInt64 =
  gen {
    let specials = interestingInts |> List.map Tuple2.second |> Gen.elements

    return! Gen.frequency [ (5, specials); (5, Arb.generate<int64>) ]
  }

let SafeInt64 = Arb.fromGen safeInt64

/// Helper function to generate allowed function name parts, bindings, etc.
let nameHelper (first : char list) (other : char list) : Gen<string> =
  gen {
    let! tailLength = Gen.choose (0, 20)
    let! head = Gen.elements first
    let! tail = Gen.arrayOfLength tailLength (Gen.elements other)
    return System.String(Array.append [| head |] tail)
  }

module NodaTime =
  let instant =
    Arb.generate<System.DateTime>
    |> Gen.map (fun dt -> dt.ToUniversalTime())
    |> Gen.map (fun dt -> Instant.FromDateTimeUtc dt)

  let localDateTime : Gen<NodaTime.LocalDateTime> =
    Arb.generate<System.DateTime> |> Gen.map NodaTime.LocalDateTime.FromDateTime

  let Instant = instant |> Arb.fromGen
  let LocalDateTime = localDateTime |> Arb.fromGen

module FQFnName =
  let ownerName =
    nameHelper [ 'a' .. 'z' ] (List.concat [ [ 'a' .. 'z' ]; [ '0' .. '9' ] ])

  let packageName =
    nameHelper [ 'a' .. 'z' ] (List.concat [ [ 'a' .. 'z' ]; [ '0' .. '9' ] ])

  let modName = nameHelper [ 'A' .. 'Z' ] alphaNumericCharacters

  let fnName = nameHelper [ 'a' .. 'z' ] alphaNumericCharacters



module RuntimeTypes =
  /// Used to avoid `toString` on Dvals that contains bytes,
  /// as OCaml backend raises an exception when attempted.
  /// CLEANUP can be removed with OCaml
  let rec containsBytes (dv : RT.Dval) =
    match dv with
    | RT.DBytes _ -> true

    | RT.DDB _
    | RT.DInt _
    | RT.DBool _
    | RT.DFloat _
    | RT.DNull
    | RT.DStr _
    | RT.DChar _
    | RT.DIncomplete _
    | RT.DFnVal _
    | RT.DError _
    | RT.DDate _
    | RT.DPassword _
    | RT.DUuid _
    | RT.DHttpResponse (RT.Redirect _)
    | RT.DOption None -> false

    | RT.DList dv -> List.any containsBytes dv
    | RT.DTuple (first, second, theRest) ->
      List.any containsBytes ([ first; second ] @ theRest)
    | RT.DObj o -> o |> Map.values |> List.any containsBytes

    | RT.DHttpResponse (RT.Response (_, _, dv))
    | RT.DOption (Some dv)
    | RT.DErrorRail dv
    | RT.DResult (Ok dv)
    | RT.DResult (Error dv) -> containsBytes dv

  let Dval =
    Arb.Default.Derive()
    |> Arb.filter (fun dval ->
      match dval with
      // These all break the serialization to OCaml
      // TODO allow all Dvals to be generated
      | RT.DPassword _ -> false
      | RT.DFnVal _ -> false
      | _ -> true)

  let DType =
    let rec isSupportedType dtype =
      match dtype with
      | RT.TInt
      | RT.TStr
      | RT.TVariable _
      | RT.TFloat
      | RT.TBool
      | RT.TNull
      | RT.TNull
      | RT.TDate
      | RT.TChar
      | RT.TUuid
      | RT.TBytes
      | RT.TError
      | RT.TDB (RT.TUserType _)
      | RT.TDB (RT.TRecord _)
      | RT.TUserType _ -> true
      | RT.TList t
      | RT.TDict t
      | RT.TOption t
      | RT.THttpResponse t -> isSupportedType t
      | RT.TTuple (first, second, theRest) ->
        List.all isSupportedType ([ first; second ] @ theRest)
      | RT.TResult (t1, t2) -> isSupportedType t1 && isSupportedType t2
      | RT.TFn (ts, rt) -> isSupportedType rt && List.all isSupportedType ts
      | RT.TRecord (pairs) ->
        pairs |> List.map Tuple2.second |> List.all isSupportedType

      // FSTODO: support all types
      | RT.TDB _
      | RT.TIncomplete
      | RT.TPassword
      | RT.TErrorRail -> false

    Arb.Default.Derive() |> Arb.filter isSupportedType

  let dType = DType.Generator

let id = gid ()

// TODO: figure out a way to ensure that these bottom-up generators exhaust all
// cases (as opposed to the generate-then-filter ones)

// TODO: for things like strings, mostly generate a small-ish pool of random
// values. That way, a generated PString pattern is more likely to match some
// generated EString value.

// TODO: clone all of this, and _really_ only generate a few values of each type.
// For example, for ints we really only need a handful of different options. That way,
// patterns are more likely to _actually_ match

// todo: stop using this.
let darkString =
  nameHelper [ 'a' .. 'z' ] (List.concat [ [ 'a' .. 'z' ]; [ '0' .. '9' ] ])


module ProgramTypes =
  module Pattern =
    let genInt = Arb.generate<int64> |> Gen.map (fun i -> PT.PInteger(gid (), i))
    let genBool = Arb.generate<bool> |> Gen.map (fun b -> PT.PBool(gid (), b))
    let genBlank = gen { return PT.PBlank(gid ()) }
    let genNull = gen { return PT.PNull(gid ()) }
    let genChar = char |> Gen.map (fun c -> PT.PCharacter(gid (), c))
    let genStr = darkString |> Gen.map (fun s -> PT.PString(gid (), s))

    // todo: this might take a bit? idk.
    // let genFloat = gen {return PT.PBlank (gid()) }

    let genVar = darkString |> Gen.map (fun s -> PT.PVariable(gid (), s))

    let constructor (s, genArg) : Gen<PT.Pattern> =
      let withMostlyFixedArgLen (name, expectedParamCount) =
        gen {
          let! argCount =
            Gen.frequency [ (95, Gen.constant expectedParamCount)
                            (5, Gen.elements [ 1..20 ]) ]

          let! args = Gen.listOfLength argCount (genArg (s / 2))

          return PT.PConstructor(gid (), name, args)
        }

      let ok = withMostlyFixedArgLen ("Ok", 1)
      let error = withMostlyFixedArgLen ("Error", 1)
      let just = withMostlyFixedArgLen ("Just", 1)
      let nothing = withMostlyFixedArgLen ("Nothing", 0)

      Gen.frequency [ (24, ok) // OK [p] (usually 1 arg; rarely, more)
                      (24, error) // Error [p] (usually 1 arg; rarely, more)

                      (24, just) // Just [p] (usually 1 arg; rarely, more)
                      (24, nothing) // Nothing [] (usually 0 args; rarely, more)

                      //(4, ok) // TODO: random string, with 0-5 args
                       ]

  open Pattern

  let pattern =
    // TODO: consider adding 'weight' such that certain patterns are generated more often than others
    let rec gen' s : Gen<PT.Pattern> =
      let finitePatterns =
        [ genInt; genBool; genBlank; genNull; genChar; genStr; genVar ]

      let allPatterns = constructor (s, gen') :: finitePatterns

      match s with
      | 0 -> Gen.oneof finitePatterns
      | n when n > 0 -> Gen.oneof allPatterns
      | _ -> invalidArg "s" "Only positive arguments are allowed"

    Gen.sized gen' // todo: depth of 20 seems kinda reasonable


  let patternsForMatch : Gen<List<PT.Pattern>> =
    gen {
      let! len = Gen.choose (1, 20)
      return! Gen.listOfLength len pattern
    }

  module Expr =
    // Non-recursive exprs
    let genInt = Arb.generate<int64> |> Gen.map (fun i -> PT.EInteger(gid (), i))

    let genBool = Arb.generate<bool> |> Gen.map (fun b -> PT.EBool(gid (), b))

    let genBlank = gen { return PT.EBlank(gid ()) }

    let genNull = gen { return PT.ENull(gid ()) }

    let genChar = char |> Gen.map (fun c -> PT.ECharacter(gid (), c))

    let genStr = darkString |> Gen.map (fun s -> PT.EString(gid (), s))

    let genVar = darkString |> Gen.map (fun s -> PT.EVariable(gid (), s))

    // TODO: genFloat

    // Recursive exprs
    let genLet (s, genSubExpr) =
      gen {
        let! varName = darkString
        let! rhsExpr = genSubExpr (s / 2)
        let! nextExpr = genSubExpr (s / 2)

        return PT.ELet(gid (), varName, rhsExpr, nextExpr)
      }

    let genIf (s, genSubExpr) =
      gen {
        let! condExpr = Gen.frequency [ (90, genBool); (10, genSubExpr (s / 2)) ]
        let! thenExpr = genSubExpr (s / 2)
        let! elseExpr = genSubExpr (s / 2)

        return PT.EIf(gid (), condExpr, thenExpr, elseExpr)
      }

    let genConstructor (s, genSubExpr) : Gen<PT.Expr> =
      let withMostlyFixedArgLen (name, expectedParamCount) =
        gen {
          let! argCount =
            Gen.frequency [ (95, Gen.constant expectedParamCount)
                            (5, Gen.elements [ 1..20 ]) ]

          let! args = Gen.listOfLength argCount (genSubExpr (s / 2))

          return PT.EConstructor(gid (), name, args)
        }

      let ok = withMostlyFixedArgLen ("Ok", 1)
      let error = withMostlyFixedArgLen ("Error", 1)
      let just = withMostlyFixedArgLen ("Just", 1)
      let nothing = withMostlyFixedArgLen ("Nothing", 0)

      Gen.frequency [ (24, ok) // OK [p] (usually 1 arg; rarely, more)
                      (24, error) // Error [p] (usually 1 arg; rarely, more)

                      (24, just) // Just [p] (usually 1 arg; rarely, more)
                      (24, nothing) // Nothing [] (usually 0 args; rarely, more)

                      //(4, ok) // TODO: random string, with 0-5 args
                       ]

    let genTuple (s, genSubExpr) =
      gen {
        let! first = genSubExpr (s / 2)
        let! second = genSubExpr (s / 2)

        // 7-element tuples seem sufficient
        let! tailLength = Gen.elements [ 0..5 ]
        let! theRest = Gen.listOfLength tailLength (genSubExpr (s / 2))

        return PT.ETuple(gid (), first, second, theRest)
      }

    let genList (s, genSubExpr) =
      gen {
        let! els = Gen.listOf (genSubExpr (s / 2))
        return PT.EList(gid (), els)
      }

    let genRecord (s, genSubExpr) =
      gen {
        let! pairs =
          gen {
            let! name = darkString
            let! v = genSubExpr (s / 2)
            return (name, v)
          }
          |> Gen.listOf

        return PT.ERecord(gid (), pairs)
      }

    let genMatch genPattern (s, genSubExpr) =
      gen {
        // TODO: consider limiting the # of cases - something between 1 and 10?
        let! cases =
          gen {
            let! p = genPattern
            let! v = genSubExpr (s / 2)
            return (p, v)
          }
          |> Gen.listOf

        let! matchExpr = genSubExpr (s / 2)

        return PT.EMatch(gid (), matchExpr, cases)
      }


  // We haven't yet created generators for these
  // They eventually belong above in the Expr sub-module
  // TODO: EBinOp
  // TODO: ELambda
  // TODO: EFieldAccess
  // TODO: EFnCall
  // TODO: EPartial
  // TODO: ERightPartial
  // TODO: ELeftPartial
  // TODO: EPipe
  // TODO: EPipeTarget
  // TODO: EFeatureFlag

  open Expr

  let expr =
    // TODO: consider adding 'weight' such that certain patterns are generated more often than others
    let rec gen' s : Gen<PT.Expr> =
      let finiteExprs =
        [ genInt; genBool; genBlank; genNull; genChar; genStr; genVar ]

      let recursiveExprs =
        [ genConstructor
          genLet
          genIf
          genTuple
          genRecord
          genList
          genMatch pattern ]
        |> List.map (fun g -> g (s, gen'))

      let allExprs = recursiveExprs @ finiteExprs

      match s with
      | 0 -> Gen.oneof finiteExprs
      | n when n > 0 -> Gen.oneof allExprs
      | _ -> invalidArg "s" "Only positive arguments are allowed"

    Gen.sized gen' // TODO: depth of 20 seems kinda reasonable



// todo: matchExpr
