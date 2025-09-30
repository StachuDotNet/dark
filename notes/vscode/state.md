# VS Code Impl State Notes

```fsharp
module Darklang.VSCode.State =
  type State = {
    currentInstance: String
    currentSession: (Option Uuid)
    activePatch: (Option Uuid)
    lastActivity: DateTime
  }
```