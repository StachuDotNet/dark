module LibCloud.Init

open System.Threading.Tasks

open Prelude

let init (serviceName : string) : Task<unit> =
  task {
    printTime $"Initing LibCloud in {serviceName}"
    printTime $" Inited LibCloud in {serviceName}"
  }
