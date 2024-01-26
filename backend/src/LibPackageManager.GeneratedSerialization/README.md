This project exists because:

- we use System.Text.Json for deserializing package items from the web-based package manager at https://packages.darklang.com
- normally, System.Text.Json uses reflection
- but, the CLI application is AOT-compiled, and we can't use reflection in that
- so, we need to use source-generator-based deserializtion instead
- typical instructions are outlined here: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation?pivots=dotnet-8-0
- F# doesn't support source generators yet (https://github.com/fsharp/fslang-suggestions/issues/864)
- so, we have to do this dance in C#, like this
