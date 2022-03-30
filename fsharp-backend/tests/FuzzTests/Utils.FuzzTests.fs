/// Utilities useful for writing and running FuzzTests
module FuzzTests.Utils

open Expecto
open Expecto.ExpectoFsCheck
open FsCheck

open System.Threading.Tasks
open FSharp.Control.Tasks
open System.Text.RegularExpressions

open Prelude
open Prelude.Tablecloth
open Tablecloth
open TestUtils.TestUtils

/// Extracts the result from a task
let result (t : Task<'a>) : 'a = t.Result

let (.=.) actual expected : bool =
  if actual = expected then
    Expect.equal actual expected ""
    true
  else
    let o = string actual |> UTF8.toBytes
    let e = string expected |> UTF8.toBytes
    Expect.equal (actual, o) (expected, e) ""
    false

type FuzzTestConfig = { MaxTests: int }

let private baseConfigWithGenerator config (arb : System.Type) : FsCheckConfig =
  { FsCheckConfig.defaultConfig with maxTest = config.MaxTests; arbitrary = [ arb ] }

let testProperty config (arb : System.Type) (name : string) (propertyToTest : 'a) : Test =
  propertyToTest |> testPropertyWithConfig (baseConfigWithGenerator config arb) name
