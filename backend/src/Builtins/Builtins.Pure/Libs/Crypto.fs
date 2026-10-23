/// <summary>
/// Builtin functions for cryptography
///
/// Computes hashes such as sha256, md5, etc.
/// </summary>
module Builtins.Pure.Libs.Crypto

open System.Security.Cryptography

open Prelude
open LibExecution.RuntimeTypes
open LibExecution.Builtin.Shortcuts

module VT = LibExecution.ValueType
module Dval = LibExecution.Dval
module Blob = LibExecution.Blob
module NR = LibExecution.RuntimeTypes.NameResolution
module PackageRefs = LibExecution.PackageRefs

let private digestAlgorithmType () =
  FQTypeName.fqPackage (PackageRefs.Type.Stdlib.Crypto.digestAlgorithm ())


let fns () : List<BuiltInFn> =
  [ // One builtin for every digest, picked by `Stdlib.Crypto.DigestAlgorithm`. HMAC is
    // Dark over this; the hash itself stays native, since a Dark SHA-256 would be
    // thousands of interpreted operations per block on paths that hash per item.
    { name = fn "cryptoDigest" 0
      typeParams = []
      parameters =
        [ Param.make "algorithm" (TCustomType(NR.ok (digestAlgorithmType ()), [])) ""
          Param.make "data" TBlob "" ]
      returnType = TBlob
      description = "Computes the <param algorithm> digest of the given <param data>"
      fn =
        (function
        | state, _, _, [| DEnum(_, _, [], algorithm, []); DBlob ref |] ->
          let hash : byte[] -> byte[] =
            match algorithm with
            | "Md5" -> MD5.HashData
            | "Sha1" -> SHA1.HashData
            | "Sha256" -> SHA256.HashData
            | "Sha384" -> SHA384.HashData
            | _ -> incorrectArgs ()
          Blob.withBytes state ref (fun data -> Ply(Blob.newEphemeral (hash data)))
        | _ -> incorrectArgs ())
      previewable = Pure
      callEffects = Set.empty
      deprecated = NotDeprecated }


    // A secret compared with `==` leaks how many leading bytes matched, through how long the
    // comparison took. `CryptographicOperations.FixedTimeEquals` takes the same time whatever the
    // inputs. The lengths are compared first and in the open: the length of a secret is not the secret.
    { name = fn "cryptoConstantTimeEquals" 0
      typeParams = []
      parameters =
        [ Param.make "presented" TString "what the caller sent"
          Param.make "expected" TString "what it should be" ]
      returnType = TBool
      description =
        "Whether <param presented> equals <param expected>, in time that does not depend on where they first differ. For comparing a secret or a token against a request."
      fn =
        (function
        | _, _, _, [| DString presented; DString expected |] ->
          let a = System.Text.Encoding.UTF8.GetBytes presented
          let b = System.Text.Encoding.UTF8.GetBytes expected
          if a.Length <> b.Length then
            Ply(DBool false)
          else
            Ply(
              DBool(
                CryptographicOperations.FixedTimeEquals(
                  System.ReadOnlySpan(a),
                  System.ReadOnlySpan(b)
                )
              )
            )
        | _ -> incorrectArgs ())
      previewable = Pure
      callEffects = Set.empty
      deprecated = NotDeprecated } ]

let builtins () = LibExecution.Builtin.make [] (fns ())
