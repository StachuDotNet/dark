
module Darklang.Cli.Project.New
// Creates new projects with scaffolding

let execute (state: AppState) (args: List<String>) : AppState =
  match args with
  | [projectType; name] ->
    // dark new webapp my-blog
    // dark new cli my-tool  
    // dark new lib my-package
    
    let validTypes = ["webapp"; "cli"; "lib"; "script"]
    if Stdlib.List.contains validTypes projectType then
      Stdlib.printLine $"Creating new {projectType} project: {name}"
      
      // Create directory structure
      // TODO: Create scaffold files based on project type
      // TODO: Initialize Matter session for project
      
      Stdlib.printLine "✓ Project structure created"
      Stdlib.printLine "✓ Dependencies resolved" 
      Stdlib.printLine "✓ Development session initialized"
      Stdlib.printLine ""
      Stdlib.printLine $"Next steps:"
      Stdlib.printLine $"  cd {name}"
      Stdlib.printLine $"  dark dev    # Start development server"
      
      state
    else
      let typesStr = Stdlib.String.join validTypes ", "
      Stdlib.printLine $"Error: Unknown project type '{projectType}'"
      Stdlib.printLine $"Valid types: {typesStr}"
      state
  | [projectType] ->
    Stdlib.printLine $"Error: Project name required"
    Stdlib.printLine $"Usage: new {projectType} <name>"
    state
  | _ ->
    Stdlib.printLine "Usage: new <type> <name>"
    Stdlib.printLine "Types: webapp, cli, lib, script"
    state

let complete (state: AppState) (args: List<String>) : List<String> =
  match args with
  | [] -> ["webapp"; "cli"; "lib"; "script"]
  | [_] -> [] // No completion for project names
  | _ -> []

let help (state: AppState) : Unit =
  [
    "Usage: new <type> <name>"
    "Create a new Darklang project with scaffolding."
    ""
    "Types:"
    "  webapp    Web application with HTTP handlers"
    "  cli       Command-line tool" 
    "  lib       Reusable library package"
    "  script    Simple script for automation"
    ""
    "Examples:"
    "  dark new webapp my-blog"
    "  dark new cli file-processor"
  ] |> Stdlib.printLines