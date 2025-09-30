module LibPackageManager.NameStore

open System.Collections.Concurrent
open Prelude
open LibExecution.ProgramTypes

module PT = LibExecution.ProgramTypes

/// Name resolution - maps package locations to content hashes
type NameEntry = {
  location: PT.PackageLocation.T
  hash: string
  contentType: ContentStore.ContentType
}

// In-memory name mappings
let private nameStore = ConcurrentDictionary<string, NameEntry>()

let private locationKey (location: PT.PackageLocation.T) : string =
  PT.PackageLocation.toString location

let setName (location: PT.PackageLocation.T) (hash: string) (contentType: ContentStore.ContentType) : unit =
  let entry = {
    location = location
    hash = hash
    contentType = contentType
  }
  nameStore.AddOrUpdate(locationKey location, entry, fun _ _ -> entry) |> ignore<NameEntry>

let getName (location: PT.PackageLocation.T) : Option<NameEntry> =
  match nameStore.TryGetValue(locationKey location) with
  | true, entry -> Some entry
  | false, _ -> None

let deleteName (location: PT.PackageLocation.T) : bool =
  let key = locationKey location
  nameStore.TryRemove(key) |> fst