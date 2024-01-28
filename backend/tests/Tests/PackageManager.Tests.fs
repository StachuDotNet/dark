module Tests.PackageManager

open System.Threading.Tasks
open FSharp.Control.Tasks

open Expecto
open Prelude
open TestUtils.TestUtils

// module PT = LibExecution.ProgramTypes
// module RT = LibExecution.RuntimeTypes

module PMT = LibPackageManager.Types
module PMPT = LibPackageManager.Types.ProgramTypes


(* TODOs
- [ ] get a sample json payload for a PackageType, as returned by the API
- [ ] write a test that deserializes that payload into a PackageType
- [ ] same for PackageConstant
- [ ] and then for PackageFunction
- [ ] then, update the relevant code to actually use these deserializers
- [ ] test that it actually works when AOT'd
- [ ] remove any BS about thoth
- [ ] squash some commits
- [ ] create the PR
- [ ] merge it
- [ ] test again after the build is done
- [ ] mention in discord
- [ ] do something else
*)
module ParsesAndDecodesOk =
  let deprecation =
    test "assertFn" {
      let json = """{ "NotDeprecated": [] }"""
      let deserialized =
        LibPackageManager.JsonDeserialization.deserialize<PMPT.Deprecation<string>>
          (LibPackageManager.JsonDeserialization.ProgramTypes.Deprecation.decoder
            LibPackageManager.JsonDeserialization.Decoders.string)
          json

      let expected = Ok PMPT.Deprecation.NotDeprecated

      assertEq "msg" expected deserialized
    }

  let packageType =
    test "assertFn" {
      let json =
        """{
          "tlid": 3492342083173715726,
          "id": "f6345e32-f0c6-422a-a9f1-1039a63ce781",
          "name": { "modules": [ "Stdlib", "Result" ], "name": "Result", "owner": "Darklang", "version": 0 },
          "description": "",
          "declaration": {
            "definition": {
              "Enum": [
                [
                  {
                    "name": "Ok",
                    "fields": [ { "description": "", "label": { "None": [] }, "typ": { "TVariable": [ "Ok" ] } } ],
                    "description": ""
                  },
                  {
                    "name": "Error",
                    "fields": [ { "description": "", "label": { "None": [] }, "typ": { "TVariable": [ "Err" ] } } ],
                    "description": ""
                  }
                ]
              ]
            },
            "typeParams": [ "Ok", "Err" ]
          },
          "deprecated": { "NotDeprecated": [] }
        }"""

      let deserialized =
        LibPackageManager.JsonDeserialization.deserialize<PMPT.PackageType>
          LibPackageManager.JsonDeserialization.ProgramTypes.PackageType.decoder
          json

      let expected : PMPT.PackageType =
        { tlid = 3492342083173715726UL
          id = System.Guid.Parse "f6345e32-f0c6-422a-a9f1-1039a63ce781"
          name =
            { owner = "Darklang"
              modules = [ "Stdlib"; "Result" ]
              name = "Result"
              version = 0 }
          description = ""
          declaration =
            { typeParams = [ "Ok"; "Err" ]
              definition =
                PMPT.TypeDeclaration.Definition.Enum(
                  NEList.ofListUnsafe
                    ""
                    []
                    [ { name = "Ok"
                        fields =
                          [ { description = ""
                              label = None
                              typ = PMPT.TypeReference.TVariable "Ok" } ]
                        description = "" }
                      { name = "Error"
                        fields =
                          [ { description = ""
                              label = None
                              typ = PMPT.TypeReference.TVariable "Err" } ]
                        description = "" } ]
                )

            }
          deprecated = PMPT.Deprecation.NotDeprecated }

      match deserialized with
      | Ok deserialized ->
        assertEq "msg" expected deserialized
      | Error (ParseError parseError) ->
        Exception.raiseInternal
          "Failed to parse package type"
          [ "json", json; "error", parseError ]
          e
      | Error (DecodeError decodeError) ->
        Exception.raiseInternal
          "Failed to decode package type"
          [ "json", json; "error", decodeError ]
          e

    }

let tests = testList "PackageManager" [ ParsesAndDecodesOk.deprecation; ParsesAndDecodesOk.packageType ]
