/// Proves meaning-stable hashing: after alpha-normalization, a package item's content hash depends on
/// its MEANING, not on incidental bound-variable names. Two items identical up to renaming of
/// parameters / `let` / lambda / match binders hash IDENTICALLY; items that differ in meaning (which
/// variable is used, binder order, a free variable) still hash DIFFERENTLY.
///
/// Everything is compared through `computeFnHash` (which skips ids), since two freshly-built exprs have
/// different ids and so can't be compared structurally with `=`.
module Tests.AlphaNormalize

open Expecto
open Prelude

open TestUtils.TestUtils
open TestUtils.PTShortcuts

module PT = LibExecution.ProgramTypes
open LibSerialization.Hashing



// ── helpers: a fn with the given parameter names + body; its raw vs alpha-normalized hash ──

let private fnOf
  (paramNames : List<string>)
  (body : PT.Expr)
  : PT.PackageFn.PackageFn =
  let ps =
    match paramNames with
    | [] -> NEList.singleton "unused"
    | h :: t -> NEList.ofList h t
  testPackageFn [] ps PT.TInt64 body

// the LIVE content hash — `computeFnHash` alpha-normalizes internally, so this *is* the
// meaning-stable hash. (`normalizeFn` stays public + idempotent; the last test pins that directly.)
let private h (paramNames : List<string>) (body : PT.Expr) : PT.Hash =
  Hashing.computeFnHash Hashing.Normal (fnOf paramNames body)

let private mpVar (name : string) : PT.MatchPattern = PT.MPVariable(gid (), name)

let private caseOf (pat : PT.MatchPattern) (rhs : PT.Expr) : PT.MatchCase =
  { pat = pat; whenCondition = None; rhs = rhs }


let tests =
  testList
    "AlphaNormalize"
    [ test
        "parameter names don't affect the hash (a parameter use is positional, EArg)" {
        // the body references parameters by position (EArg index), and the parameter name isn't hashed,
        // so two fns identical up to parameter names share one content hash. (Meaning-preservation —
        // which parameter goes where — is pinned by the next test and the let/lambda/match tests below.)
        let body = eTuple (eArg 0) (eArg 1) [] // (param0, param1) — positional
        Expect.equal
          (h [ "x"; "y" ] body)
          (h [ "a"; "b" ] body)
          "same meaning, same hash — the function's identity ignores incidental parameter names"
      }

      test "parameter POSITION is meaning: `(arg0, arg1)` ≠ `(arg1, arg0)`" {
        Expect.notEqual
          (h [ "x"; "y" ] (eTuple (eArg 0) (eArg 1) []))
          (h [ "x"; "y" ] (eTuple (eArg 1) (eArg 0) []))
          "which parameter goes where is meaning — the positional reference keeps it"
      }

      test "let binder rename: `let x = 1 in x` ≡ `let y = 1 in y`" {
        let lx = eLet (lpVar "x") (eInt64 1L) (eVar "x")
        let ly = eLet (lpVar "y") (eInt64 1L) (eVar "y")
        Expect.equal (h [] lx) (h [] ly) "same meaning, same hash"
      }

      test "lambda binder rename: `fun x -> x` ≡ `fun y -> y`" {
        let lx = eLambda (gid ()) [ lpVar "x" ] (eVar "x")
        let ly = eLambda (gid ()) [ lpVar "y" ] (eVar "y")
        Expect.equal (h [] lx) (h [] ly) "alpha-equivalent lambdas hash equal"
      }

      test "lambda meaning preserved: `fun x y -> x` ≠ `fun x y -> y`" {
        let first = eLambda (gid ()) [ lpVar "x"; lpVar "y" ] (eVar "x")
        let second = eLambda (gid ()) [ lpVar "x"; lpVar "y" ] (eVar "y")
        Expect.notEqual
          (h [] first)
          (h [] second)
          "returning the first vs the second argument is a real difference"
      }

      test "match binder rename: `match 0 with | x -> x` ≡ `| y -> y`" {
        let mx = eMatch (eInt64 0L) [ caseOf (mpVar "x") (eVar "x") ]
        let my = eMatch (eInt64 0L) [ caseOf (mpVar "y") (eVar "y") ]
        Expect.equal (h [] mx) (h [] my) "alpha-equivalent match cases hash equal"
      }

      test "match binder position matters: `| (x,y) -> x` ≢ `| (x,y) -> y`" {
        // guards that multi-binder normalization keeps WHICH bound var the rhs uses — not just that it
        // renames binders. Both patterns bind the same two names; only the rhs's choice differs.
        let tuplePat = PT.MPTuple(gid (), mpVar "x", mpVar "y", [])
        let mFirst = eMatch (eInt64 0L) [ caseOf tuplePat (eVar "x") ]
        let mSecond = eMatch (eInt64 0L) [ caseOf tuplePat (eVar "y") ]
        Expect.notEqual
          (h [] mFirst)
          (h [] mSecond)
          "which tuple-bound variable the rhs returns is a real difference"
      }

      test
        "free variables are preserved (a free var is a real reference, not a binder)" {
        // `z` / `w` are neither parameters nor locally bound — they must survive normalization distinctly
        Expect.notEqual
          (h [] (eVar "z"))
          (h [] (eVar "w"))
          "two different free variables stay different after normalization"
      }

      test "shadowing: inner-use ≢ outer-use; and inner-use is alpha-stable" {
        // let _ = 1 in let _ = 2 in <use>
        let useInnerXY =
          eLet (lpVar "x") (eInt64 1L) (eLet (lpVar "y") (eInt64 2L) (eVar "y"))
        let useInnerAB =
          eLet (lpVar "a") (eInt64 1L) (eLet (lpVar "b") (eInt64 2L) (eVar "b"))
        let useOuter =
          eLet (lpVar "a") (eInt64 1L) (eLet (lpVar "b") (eInt64 2L) (eVar "a"))
        Expect.equal
          (h [] useInnerXY)
          (h [] useInnerAB)
          "using the inner binding is alpha-stable across renames"
        Expect.notEqual
          (h [] useInnerAB)
          (h [] useOuter)
          "using the inner vs the outer binding is a real difference (shadowing respected)"
      }

      test
        "normalization is idempotent (re-normalizing an already-normalized expr is a structural no-op)" {
        // Compare the normalized EXPR structures directly (not through computeFnHash, which normalizes
        // internally — that would re-normalize both sides and hide a first-pass difference). normalizeExpr
        // preserves ids and only renames binders, so a second pass over the canonical form must be identity.
        // Use real binders (let + lambda + a shadowing inner let) so there is something to normalize.
        let body =
          eLet
            (lpVar "outer")
            (eInt64 1L)
            (eLambda
              (gid ())
              [ lpVar "p" ]
              (eLet (lpVar "inner") (eVar "p") (eVar "inner")))
        let once = Hashing.normalizeExpr body
        let twice = Hashing.normalizeExpr once
        Expect.equal
          twice
          once
          "normalizeExpr (normalizeExpr e) is structurally identical to normalizeExpr e"
      } ]
