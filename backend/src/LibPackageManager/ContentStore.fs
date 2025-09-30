module LibPackageManager.ContentStore

open System.Collections.Concurrent
open Prelude
open LibExecution.ProgramTypes

module PT = LibExecution.ProgramTypes

/// Content storage - immutable content addressed by hash
type ContentType =
  | Function
  | Type
  | Value

type Content =
  | FunctionContent of PT.PackageFn.PackageFn
  | TypeContent of PT.PackageType.PackageType
  | ValueContent of PT.PackageValue.PackageValue

type DeprecationInfo = {
  reason: string
  replacement: string option
}

// In-memory content cache
let private contentCache = ConcurrentDictionary<string, Content>()

// In-memory deprecation tracking
let private deprecationCache = ConcurrentDictionary<string, DeprecationInfo>()

let hashContent (content: Content) : string =
  // For now, simple hash implementation - in production use proper hashing
  match content with
  | FunctionContent fn -> $"fn-{fn.id}"
  | TypeContent t -> $"type-{t.id}"
  | ValueContent v -> $"val-{v.id}"

let addContent (hash: string) (content: Content) : unit =
  contentCache.TryAdd(hash, content) |> ignore<bool>

let getContent (hash: string) : Option<Content> =
  match contentCache.TryGetValue(hash) with
  | true, content -> Some content
  | false, _ -> None

let deprecateContent (hash: string) (reason: string) (replacement: string option) : unit =
  let info = { reason = reason; replacement = replacement }
  deprecationCache.AddOrUpdate(hash, info, fun _ _ -> info) |> ignore<DeprecationInfo>

let isDeprecated (hash: string) : Option<DeprecationInfo> =
  match deprecationCache.TryGetValue(hash) with
  | true, info -> Some info
  | false, _ -> None