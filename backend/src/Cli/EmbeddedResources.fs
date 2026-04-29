module Cli.EmbeddedResources

open System
open System.IO
open System.Reflection

// The data.db / seed.db is no longer embedded — see SeedLoader.fs for the
// new discovery-based flow. This file now only handles the small static
// README that we still ship alongside the binary.

let private extractResource (resourceName : string) (targetPath : string) : unit =
  let assembly = Assembly.GetExecutingAssembly()

  let targetDir = Path.GetDirectoryName(targetPath)
  if not (Directory.Exists(targetDir)) then
    Directory.CreateDirectory(targetDir) |> ignore<DirectoryInfo>

  use stream = assembly.GetManifestResourceStream(resourceName)

  if stream = null then
    // Resource not found — acceptable in debug builds.
    ()
  else
    use fileStream = File.Create(targetPath)
    stream.CopyTo(fileStream)

let private hasEmbeddedResource (resourceName : string) : bool =
  let assembly = Assembly.GetExecutingAssembly()
  assembly.GetManifestResourceNames() |> Array.contains resourceName

/// SeedLoader.installSeed sets up DARK_CONFIG_RUNDIR. Once that's done, we
/// drop a static README into the data dir if one shipped with the binary
/// and isn't already extracted.
let extract () : unit =
  let darklangDir = Environment.GetEnvironmentVariable("DARK_CONFIG_RUNDIR")
  if String.IsNullOrEmpty darklangDir then
    ()
  else if hasEmbeddedResource "README.md" then
    let readmePath = Path.Combine(darklangDir, "README.md")
    if not (File.Exists readmePath) then extractResource "README.md" readmePath
