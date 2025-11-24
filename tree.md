.
├── backend
│   ├── Directory.Build.props
│   ├── fsdark.sln
│   ├── global.json
│   ├── migrations
│   │   ├── 20250717_214941_initial.sql
│   │   ├── 20250730_000000_add_package_rt_columns.sql
│   │   ├── 20250805_152617_add_scripts_table.sql
│   │   ├── 20250820_000000_rename_package_constants_to_values.sql
│   │   └── 20251015_192755_package_schema_rewrite.sql
│   ├── NuGet.Config
│   ├── paket.dependencies
│   ├── paket.lock
│   ├── serialization
│   │   ├── oplist-binary-latest.bin
│   │   ├── oplist-binary-pretty-latest.json
│   │   ├── toplevels-binary-latest.bin
│   │   ├── vanilla_LibCloud-Queue-NotificationData_simple.json
│   │   ├── vanilla_LibExecution-AnalysisTypes-TraceData_testTraceData.json
│   │   ├── vanilla_LibExecution-DvalReprInternalRoundtrippable-FormatV0-Dval_complete.json
│   │   ├── vanilla_LibExecution-ProgramTypes-PackageFn-PackageFn_function.json
│   │   ├── vanilla_LibExecution-ProgramTypes-PackageType-PackageType_type.json
│   │   ├── vanilla_LibExecution-ProgramTypes-PackageValue-PackageValue_value.json
│   │   ├── vanilla_LibExecution-ProgramTypes-Toplevel-T_db.json
│   │   ├── vanilla_LibExecution-ProgramTypes-Toplevel-T_httphandler.json
│   │   ├── vanilla_LibService-Rollbar-HoneycombJson_simple.json
│   │   ├── vanilla_Microsoft-FSharp-Collections-FSharpList-1-LibExecution-ProgramTypes-Toplevel-T-_complete.json
│   │   ├── vanilla_Microsoft-FSharp-Collections-FSharpMap-2-System-String-System-String-_baseline.json
│   │   ├── vanilla_System-Tuple-2-System-Guid-Microsoft-FSharp-Collections-FSharpList-1-System-UInt64-_simple.json
│   │   └── vanilla_System-Tuple-5-System-String-System-String-System-String-NodaTime-Instant-System-Guid-_simple.json
│   ├── src
│   │   ├── BuiltinCli
│   │   │   ├── BuiltinCli.fsproj
│   │   │   ├── Builtin.fs
│   │   │   ├── Libs
│   │   │   │   ├── Directory.fs
│   │   │   │   ├── Environment.fs
│   │   │   │   ├── Execution.fs
│   │   │   │   ├── File.fs
│   │   │   │   ├── Output.fs
│   │   │   │   ├── Stdin.fs
│   │   │   │   ├── Terminal.fs
│   │   │   │   └── Time.fs
│   │   │   └── paket.references
│   │   ├── BuiltinCliHost
│   │   │   ├── BuiltinCliHost.fsproj
│   │   │   ├── Builtin.fs
│   │   │   ├── Libs
│   │   │   │   └── Cli.fs
│   │   │   ├── paket.references
│   │   │   └── Utils.fs
│   │   ├── BuiltinCloudExecution
│   │   │   ├── BuiltinCloudExecution.fsproj
│   │   │   ├── Builtin.fs
│   │   │   ├── Libs
│   │   │   │   ├── DB.fs
│   │   │   │   └── Event.fs
│   │   │   ├── paket.references
│   │   │   └── README.md
│   │   ├── BuiltinDarkInternal
│   │   │   ├── BuiltinDarkInternal.fsproj
│   │   │   ├── Builtin.fs
│   │   │   ├── Helpers
│   │   │   │   └── Permissions.fs
│   │   │   ├── Libs
│   │   │   │   ├── Canvases.fs
│   │   │   │   ├── DBs.fs
│   │   │   │   ├── Domains.fs
│   │   │   │   ├── F404s.fs
│   │   │   │   ├── Infra.fs
│   │   │   │   ├── Secrets.fs
│   │   │   │   ├── Users.fs
│   │   │   │   └── Workers.fs
│   │   │   ├── paket.references
│   │   │   └── README.md
│   │   ├── BuiltinExecution
│   │   │   ├── BuiltinExecution.fsproj
│   │   │   ├── Builtin.fs
│   │   │   ├── Libs
│   │   │   │   ├── AltJson.fs
│   │   │   │   ├── Base64.fs
│   │   │   │   ├── Bool.fs
│   │   │   │   ├── Bytes.fs
│   │   │   │   ├── Char.fs
│   │   │   │   ├── Crypto.fs
│   │   │   │   ├── DateTime.fs
│   │   │   │   ├── Dict.fs
│   │   │   │   ├── Float.fs
│   │   │   │   ├── HttpClient.fs
│   │   │   │   ├── Int128.fs
│   │   │   │   ├── Int16.fs
│   │   │   │   ├── Int32.fs
│   │   │   │   ├── Int64.fs
│   │   │   │   ├── Int8.fs
│   │   │   │   ├── Json.fs
│   │   │   │   ├── LanguageTools.fs
│   │   │   │   ├── List.fs
│   │   │   │   ├── Math.fs
│   │   │   │   ├── NoModule.fs
│   │   │   │   ├── Parser.fs
│   │   │   │   ├── Reflection.fs
│   │   │   │   ├── String.fs
│   │   │   │   ├── UInt128.fs
│   │   │   │   ├── UInt16.fs
│   │   │   │   ├── UInt32.fs
│   │   │   │   ├── UInt64.fs
│   │   │   │   ├── UInt8.fs
│   │   │   │   ├── Uuid.fs
│   │   │   │   └── X509.fs
│   │   │   └── paket.references
│   │   ├── BuiltinPM
│   │   │   ├── Builtin.fs
│   │   │   ├── BuiltinPM.fsproj
│   │   │   ├── Libs
│   │   │   │   ├── Branches.fs
│   │   │   │   ├── Instances.fs
│   │   │   │   ├── PackageOps.fs
│   │   │   │   ├── Packages.fs
│   │   │   │   ├── Scripts.fs
│   │   │   │   └── Sync.fs
│   │   │   └── paket.references
│   │   ├── BwdServer
│   │   │   ├── BwdServer.fsproj
│   │   │   ├── paket.references
│   │   │   ├── README.md
│   │   │   └── Server.fs
│   │   ├── Cli
│   │   │   ├── Cli.fs
│   │   │   ├── Cli.fsproj
│   │   │   ├── EmbeddedResources.fs
│   │   │   ├── paket.references
│   │   │   ├── README.md
│   │   │   └── README-to-embed.md
│   │   ├── CronChecker
│   │   │   ├── CronChecker.fs
│   │   │   ├── CronChecker.fsproj
│   │   │   ├── paket.references
│   │   │   └── README.md
│   │   ├── DvalReprDeveloper
│   │   │   ├── DvalReprDeveloper.fs
│   │   │   ├── DvalReprDeveloper.fsproj
│   │   │   ├── paket.references
│   │   │   └── README.md
│   │   ├── LibClientTypes
│   │   │   ├── ClientPusherTypes.fs
│   │   │   ├── LibClientTypes.fsproj
│   │   │   ├── paket.references
│   │   │   └── README.md
│   │   ├── LibClientTypesToCloudTypes
│   │   │   ├── LibClientTypesToCloudTypes.fsproj
│   │   │   ├── paket.references
│   │   │   └── Pusher.fs
│   │   ├── LibCloud
│   │   │   ├── Account.fs
│   │   │   ├── Canvas.fs
│   │   │   ├── Config.fs
│   │   │   ├── Cron.fs
│   │   │   ├── DvalReprInternalHash.fs
│   │   │   ├── DvalReprInternalQueryable.fs
│   │   │   ├── DvalReprInternalRoundtrippable.fs
│   │   │   ├── File.fs
│   │   │   ├── Init.fs
│   │   │   ├── LibCloud.fsproj
│   │   │   ├── paket.references
│   │   │   ├── Password.fs
│   │   │   ├── Pusher.fs
│   │   │   ├── Queue.fs
│   │   │   ├── QueueSchedulingRules.fs
│   │   │   ├── README.md
│   │   │   ├── Routing.fs
│   │   │   ├── Secret.fs
│   │   │   ├── Serialize.fs
│   │   │   ├── SqlCompiler.fs
│   │   │   ├── Stats.fs
│   │   │   ├── Tracing.fs
│   │   │   └── UserDB.fs
│   │   ├── LibCloudExecution
│   │   │   ├── CloudExecution.fs
│   │   │   ├── HttpClient.fs
│   │   │   ├── Init.fs
│   │   │   ├── LibCloudExecution.fsproj
│   │   │   ├── paket.references
│   │   │   └── README.md
│   │   ├── LibConfig
│   │   │   ├── ConfigDsl.fs
│   │   │   ├── Config.fs
│   │   │   ├── LibConfig.fsproj
│   │   │   ├── paket.references
│   │   │   └── README.md
│   │   ├── LibDB
│   │   │   ├── Db.fs
│   │   │   ├── LibDB.fsproj
│   │   │   └── paket.references
│   │   ├── LibExecution
│   │   │   ├── AnalysisTypes.fs
│   │   │   ├── Builtin.fs
│   │   │   ├── CommonToDarkTypes.fs
│   │   │   ├── DarkDateTime.fs
│   │   │   ├── DvalDecoder.fs
│   │   │   ├── Dval.fs
│   │   │   ├── Execution.fs
│   │   │   ├── Interpreter.fs
│   │   │   ├── LibExecution.fsproj
│   │   │   ├── PackageIDs.fs
│   │   │   ├── paket.references
│   │   │   ├── ProgramTypesAst.fs
│   │   │   ├── ProgramTypes.fs
│   │   │   ├── ProgramTypesParser.fs
│   │   │   ├── ProgramTypesToDarkTypes.fs
│   │   │   ├── ProgramTypesToRuntimeTypes.fs
│   │   │   ├── RuntimeTypes.fs
│   │   │   ├── RuntimeTypesToDarkTypes.fs
│   │   │   ├── TypeChecker.fs
│   │   │   └── ValueType.fs
│   │   ├── LibHttpMiddleware
│   │   │   ├── Http.fs
│   │   │   ├── LibHttpMiddleware.fsproj
│   │   │   ├── paket.references
│   │   │   └── README.md
│   │   ├── LibPackageManager
│   │   │   ├── Caching.fs
│   │   │   ├── LibPackageManager.fsproj
│   │   │   ├── PackageManager.fs
│   │   │   ├── paket.references
│   │   │   ├── PT
│   │   │   │   ├── Compose.fs
│   │   │   │   ├── Empty.fs
│   │   │   │   ├── InMemory.fs
│   │   │   │   └── SQL
│   │   │   │       ├── Branches.fs
│   │   │   │       ├── Fns.fs
│   │   │   │       ├── Instances.fs
│   │   │   │       ├── OpPlayback.fs
│   │   │   │       ├── PM.fs
│   │   │   │       ├── Purge.fs
│   │   │   │       ├── Scripts.fs
│   │   │   │       ├── Search.fs
│   │   │   │       ├── Stats.fs
│   │   │   │       ├── Sync.fs
│   │   │   │       ├── Types.fs
│   │   │   │       └── Values.fs
│   │   │   ├── README.md
│   │   │   └── RT
│   │   │       └── SQL.fs
│   │   ├── LibParser
│   │   │   ├── Canvas.fs
│   │   │   ├── FSharpToWrittenTypes.fs
│   │   │   ├── LibParser.fsproj
│   │   │   ├── NameResolver.fs
│   │   │   ├── Package.fs
│   │   │   ├── paket.references
│   │   │   ├── ParserException.fs
│   │   │   ├── Parser.fs
│   │   │   ├── README.md
│   │   │   ├── TestModule.fs
│   │   │   ├── Utils.fs
│   │   │   ├── WrittenTypes.fs
│   │   │   └── WrittenTypesToProgramTypes.fs
│   │   ├── LibSerialization
│   │   │   ├── Binary
│   │   │   │   ├── BaseFormat.fs
│   │   │   │   ├── Serialization.fs
│   │   │   │   └── Serializers
│   │   │   │       ├── Common.fs
│   │   │   │       ├── PT
│   │   │   │       │   ├── Common.fs
│   │   │   │       │   ├── Expr.fs
│   │   │   │       │   ├── PackageFn.fs
│   │   │   │       │   ├── PackageOp.fs
│   │   │   │       │   ├── PackageType.fs
│   │   │   │       │   ├── PackageValue.fs
│   │   │   │       │   ├── Toplevel.fs
│   │   │   │       │   └── TypeReference.fs
│   │   │   │       └── RT
│   │   │   │           ├── Common.fs
│   │   │   │           ├── Dval.fs
│   │   │   │           ├── Instructions.fs
│   │   │   │           ├── PackageFn.fs
│   │   │   │           ├── PackageType.fs
│   │   │   │           ├── PackageValue.fs
│   │   │   │           ├── TypeReference.fs
│   │   │   │           └── ValueType.fs
│   │   │   ├── Hashing
│   │   │   │   └── ContentHash.fs
│   │   │   ├── LibSerialization.fsproj
│   │   │   ├── paket.references
│   │   │   └── README.md
│   │   ├── LibService
│   │   │   ├── Config.fs
│   │   │   ├── FireAndForget.fs
│   │   │   ├── HSTS.fs
│   │   │   ├── Init.fs
│   │   │   ├── Kestrel.fs
│   │   │   ├── Kubernetes.fs
│   │   │   ├── LaunchDarkly.fs
│   │   │   ├── LibService.fsproj
│   │   │   ├── Logging.fs
│   │   │   ├── paket.references
│   │   │   ├── README.md
│   │   │   ├── Rollbar.fs
│   │   │   └── Telemetry.fs
│   │   ├── LibTreeSitter
│   │   │   ├── Helpers.fs
│   │   │   ├── LibTreeSitter.fsproj
│   │   │   ├── paket.references
│   │   │   ├── README.md
│   │   │   ├── TreeSitter.Darklang.fs
│   │   │   └── TreeSitter.fs
│   │   ├── LocalExec
│   │   │   ├── Builtins.fs
│   │   │   ├── Canvas.fs
│   │   │   ├── LoadPackagesFromDisk.fs
│   │   │   ├── LocalExec.fs
│   │   │   ├── LocalExec.fsproj
│   │   │   ├── Migrations.fs
│   │   │   ├── paket.references
│   │   │   ├── README.md
│   │   │   └── Utils.fs
│   │   ├── Prelude
│   │   │   ├── Base64.fs
│   │   │   ├── Dictionary.fs
│   │   │   ├── Exception.fs
│   │   │   ├── HashSet.fs
│   │   │   ├── Json.fs
│   │   │   ├── Lazy.fs
│   │   │   ├── List.fs
│   │   │   ├── Map.fs
│   │   │   ├── NEList.fs
│   │   │   ├── NonBlockingConsole.fs
│   │   │   ├── Option.fs
│   │   │   ├── paket.references
│   │   │   ├── Ply.fs
│   │   │   ├── Prelude.fs
│   │   │   ├── Prelude.fsproj
│   │   │   ├── README.md
│   │   │   ├── Regex.fs
│   │   │   ├── ResizeArray.fs
│   │   │   ├── Result.fs
│   │   │   ├── String.fs
│   │   │   ├── Task.fs
│   │   │   ├── Tuple2.fs
│   │   │   └── UTF8.fs
│   │   ├── ProdExec
│   │   │   ├── paket.references
│   │   │   ├── ProdExec.fs
│   │   │   ├── ProdExec.fsproj
│   │   │   └── README.md
│   │   ├── QueueWorker
│   │   │   ├── paket.references
│   │   │   ├── QueueWorker.fs
│   │   │   ├── QueueWorker.fsproj
│   │   │   └── README.md
│   │   └── Wasm
│   │       ├── Builtin.fs
│   │       ├── DarkEditor.fs
│   │       ├── EvalHelpers.fs
│   │       ├── Init.fs
│   │       ├── Libs
│   │       │   └── Editor.fs
│   │       ├── paket.references
│   │       ├── Program.fs
│   │       ├── README.md
│   │       ├── Wasm.fsproj
│   │       └── WasmHelpers.fs
│   ├── static
│   │   ├── dark-wasm-webworker.js
│   │   ├── editor-bootstrap.js
│   │   ├── favicon-32x32.png
│   │   ├── README.md
│   │   └── webworker-fake-env.js
│   ├── testfiles
│   │   ├── data
│   │   │   ├── boring-text
│   │   │   ├── favicon-32x32.png
│   │   │   ├── naughty-strings.txt
│   │   │   ├── sample-gettingstarted.json
│   │   │   └── sample_image_bytes.png
│   │   ├── execution
│   │   │   ├── cli
│   │   │   │   └── file.tests
│   │   │   ├── cloud
│   │   │   │   ├── db.dark
│   │   │   │   ├── _events.dark
│   │   │   │   ├── internal.dark
│   │   │   │   └── _middleware.dark
│   │   │   ├── language
│   │   │   │   ├── apply
│   │   │   │   │   ├── eapply.dark
│   │   │   │   │   └── einfix.dark
│   │   │   │   ├── basic
│   │   │   │   │   ├── dfloat.dark
│   │   │   │   │   ├── eand.dark
│   │   │   │   │   ├── elet.dark
│   │   │   │   │   ├── eor.dark
│   │   │   │   │   ├── estring.dark
│   │   │   │   │   └── evariable.dark
│   │   │   │   ├── big.dark
│   │   │   │   ├── collections
│   │   │   │   │   ├── dlist.dark
│   │   │   │   │   ├── dtuple.dark
│   │   │   │   │   └── edict.dark
│   │   │   │   ├── custom-data
│   │   │   │   │   ├── aliases.dark
│   │   │   │   │   ├── enums.dark
│   │   │   │   │   ├── record-field-acess.dark
│   │   │   │   │   ├── records.dark
│   │   │   │   │   └── values.dark
│   │   │   │   ├── derror.dark
│   │   │   │   ├── elambda.dark
│   │   │   │   ├── flow-control
│   │   │   │   │   ├── eif.dark
│   │   │   │   │   ├── ematch.dark
│   │   │   │   │   └── epipe.dark
│   │   │   │   └── interpreter.dark
│   │   │   ├── README.md
│   │   │   └── stdlib
│   │   │       ├── alt-json.dark
│   │   │       ├── base64.dark
│   │   │       ├── bool.dark
│   │   │       ├── bytes.dark
│   │   │       ├── char.dark
│   │   │       ├── crypto.dark
│   │   │       ├── date.dark
│   │   │       ├── dict.dark
│   │   │       ├── earg.dark
│   │   │       ├── eself.dark
│   │   │       ├── float.dark
│   │   │       ├── html.dark
│   │   │       ├── httpclient.dark
│   │   │       ├── http.dark
│   │   │       ├── ints
│   │   │       │   ├── int128.dark
│   │   │       │   ├── int16.dark
│   │   │       │   ├── int32.dark
│   │   │       │   ├── int64.dark
│   │   │       │   ├── int8.dark
│   │   │       │   ├── uint128.dark
│   │   │       │   ├── uint16.dark
│   │   │       │   ├── uint32.dark
│   │   │       │   ├── uint64.dark
│   │   │       │   └── uint8.dark
│   │   │       ├── json.dark
│   │   │       ├── language-tools
│   │   │       │   ├── parser.dark
│   │   │       │   └── semanticTokenization.dark
│   │   │       ├── list.dark
│   │   │       ├── math.dark
│   │   │       ├── nomodule.dark
│   │   │       ├── option.dark
│   │   │       ├── result.dark
│   │   │       ├── string.dark
│   │   │       ├── tuple.dark
│   │   │       ├── uuid.dark
│   │   │       └── x509.dark
│   │   ├── httpclient
│   │   │   ├── README.md
│   │   │   └── v0
│   │   │       ├── basic-delete-helper-function.test
│   │   │       ├── basic-delete.test
│   │   │       ├── basic-get-helper-function.test
│   │   │       ├── basic-get.test
│   │   │       ├── basic-head-returns-body-helper-function.test
│   │   │       ├── basic-head-returns-body.test
│   │   │       ├── basic-head.test
│   │   │       ├── basic-options-helper-function.test
│   │   │       ├── basic-options.test
│   │   │       ├── basic-patch.test
│   │   │       ├── basic-post-helper-function.test
│   │   │       ├── basic-post.test
│   │   │       ├── basic-put-helper-function.test
│   │   │       ├── basic-put.test
│   │   │       ├── _request-content-type-empty.test
│   │   │       ├── _request-content-type-invalid.test
│   │   │       ├── request-content-type-unknown-charset.test
│   │   │       ├── request-content-type-unknown-ct.test
│   │   │       ├── request-form-simple.test
│   │   │       ├── request-query-param-float.test
│   │   │       ├── request-query-param-int.test
│   │   │       ├── _response-cookie.test
│   │   │       ├── response-header-duplicate.test
│   │   │       ├── response-redirect-300.test
│   │   │       ├── response-redirect-301.test
│   │   │       ├── response-redirect-302.test
│   │   │       ├── response-redirect-303.test
│   │   │       ├── _response-redirect-304.test
│   │   │       ├── response-redirect-305.test
│   │   │       ├── response-redirect-306.test
│   │   │       ├── response-redirect-307.test
│   │   │       ├── response-redirect-308.test
│   │   │       ├── _response-redirect-destination.test
│   │   │       ├── response-redirect-to-file.test
│   │   │       ├── _response-redirect-to-same-place-absolute.test
│   │   │       ├── todo
│   │   │       │   ├── readme.md
│   │   │       │   ├── request-form-with-body-and-charset.test
│   │   │       │   ├── request-form-with-body-and-no-charset.test
│   │   │       │   ├── request-header-override-content-length-get.test
│   │   │       │   ├── request-header-override-content-length-post-bad.test
│   │   │       │   ├── request-header-override-content-length-post.test
│   │   │       │   ├── request-header-override-default.test
│   │   │       │   ├── request-header-string.test
│   │   │       │   ├── request-multipart-form-with-body-and-charset.test
│   │   │       │   ├── request-query-param-list.test
│   │   │       │   ├── request-query-param-null.test
│   │   │       │   ├── request-query-param-string-basic.test
│   │   │       │   ├── request-query-param-string-emoji-key.test
│   │   │       │   ├── request-query-param-string-emoji-value.test
│   │   │       │   ├── request-query-param-string-empty-key.test
│   │   │       │   ├── request-query-param-string-empty-values.test
│   │   │       │   ├── request-query-param-string-empty-value.test
│   │   │       │   ├── request-query-param-string-punctuation-key.test
│   │   │       │   ├── request-query-param-string-punctuation-value.test
│   │   │       │   ├── request-query-param-string-spaces-key.test
│   │   │       │   ├── request-query-param-string-spaces-value.test
│   │   │       │   ├── response-body-invalid-string.test
│   │   │       │   ├── response-body-unicode.test
│   │   │       │   ├── response-content-encoding-brotli.test
│   │   │       │   ├── response-content-encoding-deflate.test
│   │   │       │   ├── response-content-encoding-gzipped.test
│   │   │       │   ├── response-content-encoding-invalid.test
│   │   │       │   ├── response-content-type-invalid.test
│   │   │       │   ├── response-content-type-latin1.test
│   │   │       │   ├── response-content-type-no-charset.test
│   │   │       │   ├── response-form-encoded.test
│   │   │       │   ├── response-form-with-body-and-charset.test
│   │   │       │   ├── response-form-with-body-no-charset.test
│   │   │       │   ├── response-header-empty.test
│   │   │       │   ├── response-header-int.test
│   │   │       │   ├── response-header-lowercase.test
│   │   │       │   ├── response-header-object.test
│   │   │       │   ├── response-header-string.test
│   │   │       │   ├── response-redirect-conflicting-charset-dest.test
│   │   │       │   ├── response-redirect-conflicting-charset.test
│   │   │       │   ├── response-redirect-delete.test
│   │   │       │   ├── response-redirect-dest-post-with-form-urlencoded-content-type-no-body.test
│   │   │       │   ├── response-redirect-dest-post-with-form-urlencoded-content-type-with-body.test
│   │   │       │   ├── response-redirect-dest-post-with-json-content-type-no-body.test
│   │   │       │   ├── response-redirect-dest-post-with-json-content-type-with-body.test
│   │   │       │   ├── response-redirect-dest-post-with-no-content-type-no-body.test
│   │   │       │   ├── response-redirect-dest-post-with-no-content-type-with-body.test
│   │   │       │   ├── response-redirect-dest-post-with-plain-content-type-no-body.test
│   │   │       │   ├── response-redirect-dest-post-with-plain-content-type-with-body.test
│   │   │       │   ├── response-redirect-dest-post-with-weird-content-type-no-body.test
│   │   │       │   ├── response-redirect-dest-post-with-weird-content-type-with-body.test
│   │   │       │   ├── response-redirect-dest-put-with-content-type-no-body.test
│   │   │       │   ├── response-redirect-dest-put-with-content-type-with-body.test
│   │   │       │   ├── response-redirect-dest-with-auth-header.test
│   │   │       │   ├── response-redirect-dest-with-cookies.test
│   │   │       │   ├── response-redirect-dest-with-query-params.test
│   │   │       │   ├── response-redirect-post-with-form-urlencoded-content-type-no-body.test
│   │   │       │   ├── response-redirect-post-with-form-urlencoded-content-type-with-body.test
│   │   │       │   ├── response-redirect-post-with-json-content-type-no-body.test
│   │   │       │   ├── response-redirect-post-with-json-content-type-with-body.test
│   │   │       │   ├── response-redirect-post-with-no-content-type-no-body.test
│   │   │       │   ├── response-redirect-post-with-no-content-type-with-body.test
│   │   │       │   ├── response-redirect-post-with-plain-content-type-no-body.test
│   │   │       │   ├── response-redirect-post-with-plain-content-type-with-body.test
│   │   │       │   ├── response-redirect-post-with-weird-content-type-no-body.test
│   │   │       │   ├── response-redirect-post-with-weird-content-type-with-body.test
│   │   │       │   ├── response-redirect-put-with-content-type-no-body.test
│   │   │       │   ├── response-redirect-put-with-content-type-with-body.test
│   │   │       │   ├── response-redirect-to-ftp.test
│   │   │       │   ├── response-redirect-to-http-absolute.test
│   │   │       │   ├── response-redirect-to-http-relative-200.test
│   │   │       │   ├── response-redirect-to-http-relative-404.test
│   │   │       │   ├── response-redirect-to-same-place-relative.test
│   │   │       │   ├── response-redirect-with-auth-header.test
│   │   │       │   ├── response-redirect-with-cookies.test
│   │   │       │   ├── response-redirect-with-query-params.test
│   │   │       │   ├── uri-with-auth-emoji-password.test
│   │   │       │   ├── uri-with-auth-emoji-username.test
│   │   │       │   ├── uri-with-auth-just-password.test
│   │   │       │   ├── uri-with-auth-just-username.test
│   │   │       │   ├── uri-with-auth-just-username-with-colon.test
│   │   │       │   └── uri-with-auth-plus-header.test
│   │   │       ├── _uri-with-auth-both.test
│   │   │       ├── uri-with-path-basic.test
│   │   │       ├── uri-with-path-dots.test
│   │   │       ├── _uri-with-path-fragment.test
│   │   │       └── uri-with-path-slash.test
│   │   ├── httphandler
│   │   │   ├── bad-response-just-int.test
│   │   │   ├── injected-icon-post-length.test
│   │   │   ├── injected-icon-post-roundtrip.test
│   │   │   ├── query-string.test
│   │   │   ├── README.md
│   │   │   ├── response-with-500.test
│   │   │   ├── simple-injected-string-post.test
│   │   │   ├── simple-inline-string-post.test
│   │   │   ├── _simple-request-headers.test
│   │   │   ├── simple-response-headers.test
│   │   │   ├── url-custom-domain.test
│   │   │   ├── url-https.test
│   │   │   ├── url-http.test
│   │   │   └── x-forwarded-proto-ignored.test
│   │   └── README.md
│   └── tests
│       ├── Tests
│       │   ├── AnalysisTypes.Tests.fs
│       │   ├── Builtin.Tests.fs
│       │   ├── BwdServer.Tests.fs
│       │   ├── Canvas.Tests.fs
│       │   ├── Cron.Tests.fs
│       │   ├── DvalRepr.Tests.fs
│       │   ├── Execution.Tests.fs
│       │   ├── HttpClient.Tests.fs
│       │   ├── Interpreter.Tests.fs
│       │   ├── LibExecution.Tests.fs
│       │   ├── LibParser.Tests.fs
│       │   ├── NewParser.Tests.fs
│       │   ├── paket.references
│       │   ├── Prelude.Tests.fs
│       │   ├── PT2RT.Tests.fs
│       │   ├── QueueSchedulingRules.Tests.fs
│       │   ├── Queue.Tests.fs
│       │   ├── Routing.Tests.fs
│       │   ├── Serialization.Binary.Tests.fs
│       │   ├── Serialization.DarkTypes.Tests.fs
│       │   ├── Serialization.TestValues.fs
│       │   ├── Serialization.Vanilla.Tests.fs
│       │   ├── SqlCompiler.Tests.fs
│       │   ├── TestConfig.fs
│       │   ├── Tests.fs
│       │   ├── Tests.fsproj
│       │   ├── TestValues.fs
│       │   └── TreeSitter.Tests.fs
│       └── TestUtils
│           ├── LibTest.fs
│           ├── paket.references
│           ├── PTShortcuts.fs
│           ├── TestUtils.fs
│           └── TestUtils.fsproj
├── canvases
│   └── dark-packages
│       ├── config.yml
│       └── main.dark
├── CHANGELOG.md
├── .circleci
│   ├── config.yml
│   └── gcp-workload-identity-config.json
├── .claude
│   └── settings.local.json
├── CLAUDE.md
├── CODE-OF-CONDUCT.md
├── CODING-GUIDE.md
├── config
│   ├── circleci
│   ├── dev
│   ├── local.template
│   └── production
├── containers
│   ├── base-service-Dockerfile
│   ├── bwdserver
│   │   └── Dockerfile
│   ├── cronchecker
│   │   └── Dockerfile
│   ├── fsharp-service-Dockerfile
│   ├── prodexec
│   │   ├── Dockerfile
│   │   ├── README.md
│   │   └── run.sh
│   └── queueworker
│       └── Dockerfile
├── CONTRIBUTING.md
├── .devcontainer
│   └── devcontainer.json
├── Dockerfile
├── .dockerignore
├── docs
│   ├── benchmarking.md
│   ├── dblock-serialization.md
│   ├── dev-setup
│   │   ├── README.md
│   │   └── vscode-setup.md
│   ├── dnsmasq.md
│   ├── errors.md
│   ├── logging-and-telemetry.md
│   ├── production
│   │   ├── accounts.md
│   │   ├── auditlogs.md
│   │   ├── db-creds-rotation.md
│   │   ├── deployment.md
│   │   ├── emergency-login.md
│   │   ├── honeycomb.md
│   │   ├── README.md
│   │   ├── styles.puml
│   │   ├── tls.md
│   │   └── what-to-do-if-something-goes-wrong.md
│   ├── queues.md
│   ├── release.md
│   ├── serialization.md
│   ├── unittests.md
│   └── writing-docstrings.md
├── .editorconfig
├── .fantomasignore
├── fsharplint.json
├── .gitattributes
├── .github
│   ├── dependabot.yml
│   └── FUNDING.yml
├── .gitignore
├── LICENSE.md
├── LICENSES
├── packages
│   ├── darklang
│   │   ├── cli
│   │   │   ├── clear.dark
│   │   │   ├── core.dark
│   │   │   ├── execution
│   │   │   │   ├── eval.dark
│   │   │   │   └── run.dark
│   │   │   ├── experiments
│   │   │   │   ├── CliAbstractions.dark
│   │   │   │   ├── demos
│   │   │   │   │   ├── DataEntryDemo.dark
│   │   │   │   │   └── Demo.dark
│   │   │   │   ├── launcher.dark
│   │   │   │   ├── ui
│   │   │   │   │   └── UIComponents.dark
│   │   │   │   └── ui-catalog
│   │   │   │       ├── catalog.dark
│   │   │   │       ├── components
│   │   │   │       │   ├── button.dark
│   │   │   │       │   ├── card.dark
│   │   │   │       │   ├── divider.dark
│   │   │   │       │   ├── dropdown.dark
│   │   │   │       │   ├── forms.dark
│   │   │   │       │   ├── label.dark
│   │   │   │       │   ├── layout.dark
│   │   │   │       │   ├── listview.dark
│   │   │   │       │   ├── message.dark
│   │   │   │       │   ├── modal.dark
│   │   │   │       │   ├── navigation.dark
│   │   │   │       │   ├── pagination.dark
│   │   │   │       │   ├── panel.dark
│   │   │   │       │   ├── progress.dark
│   │   │   │       │   ├── scrollbar.dark
│   │   │   │       │   ├── statusbar.dark
│   │   │   │       │   └── textblock.dark
│   │   │   │       └── core
│   │   │   │           ├── rendering.dark
│   │   │   │           └── types.dark
│   │   │   ├── experiments.dark
│   │   │   ├── help.dark
│   │   │   ├── installation
│   │   │   │   ├── config.dark
│   │   │   │   ├── download.dark
│   │   │   │   ├── helpers.dark
│   │   │   │   ├── install.dark
│   │   │   │   ├── README.md
│   │   │   │   ├── status.dark
│   │   │   │   ├── system.dark
│   │   │   │   ├── uninstall.dark
│   │   │   │   ├── update.dark
│   │   │   │   └── version.dark
│   │   │   ├── instances.dark
│   │   │   ├── old_notes
│   │   │   │   ├── _command.dark
│   │   │   │   ├── _model.dark
│   │   │   │   ├── _msg.dark
│   │   │   │   └── README.md
│   │   │   ├── packages
│   │   │   │   ├── back.dark
│   │   │   │   ├── branch.dark
│   │   │   │   ├── core.dark
│   │   │   │   ├── display.dark
│   │   │   │   ├── fn.dark
│   │   │   │   ├── listing.dark
│   │   │   │   ├── location.dark
│   │   │   │   ├── nav.dark
│   │   │   │   ├── navInteractive.dark
│   │   │   │   ├── query.dark
│   │   │   │   ├── search.dark
│   │   │   │   ├── traversal.dark
│   │   │   │   ├── tree.dark
│   │   │   │   ├── type.dark
│   │   │   │   ├── value.dark
│   │   │   │   └── view.dark
│   │   │   ├── prompt.dark
│   │   │   ├── quit.dark
│   │   │   ├── scripts.dark
│   │   │   ├── sync.dark
│   │   │   ├── tests
│   │   │   │   └── tests.dark
│   │   │   └── utils
│   │   │       ├── colors.dark
│   │   │       ├── completion.dark
│   │   │       ├── logo.dark
│   │   │       ├── syntaxHighlighting.dark
│   │   │       └── terminal.dark
│   │   ├── dark-packages.dark
│   │   ├── github.dark
│   │   ├── internal
│   │   │   └── darklang-internal-mcp-server
│   │   │       ├── darklang-internal-mcp-server.dark
│   │   │       ├── prompts.dark
│   │   │       ├── README.md
│   │   │       ├── resources.dark
│   │   │       └── tools.dark
│   │   ├── internal.dark
│   │   ├── json-rpc.dark
│   │   ├── languageServerProtocol
│   │   │   ├── common.dark
│   │   │   ├── documentSync
│   │   │   │   ├── common.dark
│   │   │   │   ├── notebook.dark
│   │   │   │   ├── README.md
│   │   │   │   └── textDocument.dark
│   │   │   ├── io.dark
│   │   │   ├── language
│   │   │   │   ├── callHierarchy.dark
│   │   │   │   ├── codeAction.dark
│   │   │   │   ├── codeLens.dark
│   │   │   │   ├── colorProvider.dark
│   │   │   │   ├── completion.dark
│   │   │   │   ├── diagnostics.dark
│   │   │   │   ├── documentHighlight.dark
│   │   │   │   ├── documentSymbols.dark
│   │   │   │   ├── findReferences.dark
│   │   │   │   ├── foldingRange.dark
│   │   │   │   ├── formatting.dark
│   │   │   │   ├── getDocumentLinks.dark
│   │   │   │   ├── goToDeclaration.dark
│   │   │   │   ├── goToDefinition.dark
│   │   │   │   ├── goToImplementation.dark
│   │   │   │   ├── handleRename.dark
│   │   │   │   ├── inlayHint.dark
│   │   │   │   ├── inlineCompletion.dark
│   │   │   │   ├── inlineValue.dark
│   │   │   │   ├── linkedEditingRange.dark
│   │   │   │   ├── monikor.dark
│   │   │   │   ├── onHover.dark
│   │   │   │   ├── selectionRange.dark
│   │   │   │   ├── semanticToken.dark
│   │   │   │   ├── signatureHelp.dark
│   │   │   │   ├── typeDefinition.dark
│   │   │   │   └── typeHierarchy.dark
│   │   │   ├── lifecycle
│   │   │   │   ├── capabilityRegistration.dark
│   │   │   │   ├── exit.dark
│   │   │   │   ├── initialize.dark
│   │   │   │   ├── initialized.dark
│   │   │   │   └── shutdown.dark
│   │   │   ├── README.md
│   │   │   ├── tracing.dark
│   │   │   ├── window
│   │   │   │   ├── logMessage.dark
│   │   │   │   ├── showDocument.dark
│   │   │   │   ├── showMessage.dark
│   │   │   │   ├── showMessageRequest.dark
│   │   │   │   └── telemetry.dark
│   │   │   ├── workInProgress.dark
│   │   │   └── workspace
│   │   │       ├── configuration.dark
│   │   │       ├── executeCommand.dark
│   │   │       ├── fileOperations.dark
│   │   │       ├── onDidChangeWatchedFiles.dark
│   │   │       ├── workspaceEdit.dark
│   │   │       ├── workspaceFolder.dark
│   │   │       └── workspaceSymbols.dark
│   │   ├── languageTools
│   │   │   ├── common.dark
│   │   │   ├── keywordDescription.dark
│   │   │   ├── lsp.dark
│   │   │   ├── lsp-server
│   │   │   │   ├── aaaa-state.dark
│   │   │   │   ├── branches.dark
│   │   │   │   ├── completions.dark
│   │   │   │   ├── cursorPosition.dark
│   │   │   │   ├── diagnostics.dark
│   │   │   │   ├── docSync.dark
│   │   │   │   ├── fileSystemProvider.dark
│   │   │   │   ├── handleIncomingMessage.dark
│   │   │   │   ├── hover.dark
│   │   │   │   ├── hoverInformation.dark
│   │   │   │   ├── initialize.dark
│   │   │   │   ├── logging.dark
│   │   │   │   ├── lsp-server.dark
│   │   │   │   ├── semanticTokens.dark
│   │   │   │   ├── showDocument.dark
│   │   │   │   ├── switchBranch.dark
│   │   │   │   ├── sync.dark
│   │   │   │   └── treeView.dark
│   │   │   ├── nameResolver.dark
│   │   │   ├── packageManager.dark
│   │   │   ├── parser
│   │   │   │   ├── canvas.dark
│   │   │   │   ├── cliScript.dark
│   │   │   │   ├── core.dark
│   │   │   │   ├── expr.dark
│   │   │   │   ├── functionDeclaration.dark
│   │   │   │   ├── identifiers.dark
│   │   │   │   ├── matchPattern.dark
│   │   │   │   ├── moduleDeclaration.dark
│   │   │   │   ├── pipeExpr.dark
│   │   │   │   ├── sourceFile.dark
│   │   │   │   ├── testParsing.dark
│   │   │   │   ├── typeDeclaration.dark
│   │   │   │   ├── typeReference.dark
│   │   │   │   └── valueDeclaration.dark
│   │   │   ├── programTypes.dark
│   │   │   ├── runtimeErrors.dark
│   │   │   ├── runtimeTypes.dark
│   │   │   ├── semanticTokens.dark
│   │   │   ├── writtenTypes.dark
│   │   │   └── writtenTypesToProgramTypes.dark
│   │   ├── lsp-extensions
│   │   │   └── fileSystemProvider.dark
│   │   ├── modelContextProtocol
│   │   │   ├── aliases.dark
│   │   │   ├── common.dark
│   │   │   ├── completion.dark
│   │   │   ├── examples
│   │   │   │   └── simpleTestServer.dark
│   │   │   ├── io.dark
│   │   │   ├── lifecycle
│   │   │   │   └── initialize.dark
│   │   │   ├── logging.dark
│   │   │   ├── progress.dark
│   │   │   ├── prompts
│   │   │   │   └── prompts.dark
│   │   │   ├── README.md
│   │   │   ├── resources
│   │   │   │   └── resources.dark
│   │   │   ├── roots.dark
│   │   │   ├── sampling.dark
│   │   │   ├── serverBuilder
│   │   │   │   ├── aliases.dark
│   │   │   │   ├── common.dark
│   │   │   │   ├── handleIncomingMessage.dark
│   │   │   │   ├── initialize.dark
│   │   │   │   ├── io.dark
│   │   │   │   ├── logging.dark
│   │   │   │   ├── main.dark
│   │   │   │   ├── prompts.dark
│   │   │   │   ├── resources.dark
│   │   │   │   ├── state.dark
│   │   │   │   └── tools.dark
│   │   │   ├── tools
│   │   │   │   └── tools.dark
│   │   │   └── tracing.dark
│   │   ├── openai.dark
│   │   ├── prettyPrinter
│   │   │   ├── canvas.dark
│   │   │   ├── cliScript.dark
│   │   │   ├── common.dark
│   │   │   ├── moduleDeclaration.dark
│   │   │   ├── programTypes.dark
│   │   │   ├── runtimeError.dark
│   │   │   └── runtimeTypes.dark
│   │   ├── scm
│   │   │   ├── branch.dark
│   │   │   ├── instances.dark
│   │   │   ├── packageOps.dark
│   │   │   └── sync.dark
│   │   ├── stdlib
│   │   │   ├── alt-json.dark
│   │   │   ├── base64.dark
│   │   │   ├── bool.dark
│   │   │   ├── bytes.dark
│   │   │   ├── canvas.dark
│   │   │   ├── char.dark
│   │   │   ├── cli
│   │   │   │   ├── bash.dark
│   │   │   │   ├── curl.dark
│   │   │   │   ├── execution.dark
│   │   │   │   ├── fileSystem.dark
│   │   │   │   ├── gunzip.dark
│   │   │   │   ├── host.dark
│   │   │   │   ├── powershell.dark
│   │   │   │   ├── process.dark
│   │   │   │   ├── stdin.dark
│   │   │   │   ├── unix.dark
│   │   │   │   └── zsh.dark
│   │   │   ├── crypto.dark
│   │   │   ├── dateTime.dark
│   │   │   ├── db.dark
│   │   │   ├── dict.dark
│   │   │   ├── float.dark
│   │   │   ├── fun.dark
│   │   │   ├── html.dark
│   │   │   ├── httpclient.dark
│   │   │   ├── http.dark
│   │   │   ├── int128.dark
│   │   │   ├── int16.dark
│   │   │   ├── int32.dark
│   │   │   ├── int64.dark
│   │   │   ├── int8.dark
│   │   │   ├── json.dark
│   │   │   ├── list.dark
│   │   │   ├── math.dark
│   │   │   ├── noModule.dark
│   │   │   ├── option.dark
│   │   │   ├── print.dark
│   │   │   ├── result.dark
│   │   │   ├── string.dark
│   │   │   ├── tuple2.dark
│   │   │   ├── tuple3.dark
│   │   │   ├── uint128.dark
│   │   │   ├── uint16.dark
│   │   │   ├── uint32.dark
│   │   │   ├── uint64.dark
│   │   │   ├── uint8.dark
│   │   │   ├── uuid.dark
│   │   │   └── x509.dark
│   │   ├── test
│   │   │   ├── test.dark
│   │   │   ├── test_earg.dark
│   │   │   └── test_eself.dark
│   │   └── vscode
│   │       ├── main.dark
│   │       ├── README.md
│   │       └── tree-view.dark
│   ├── feriel
│   │   └── modelContextProtocol
│   │       └── push-notification
│   │           ├── pushNotificationServer.dark
│   │           ├── README.md
│   │           └── tools
│   │               ├── ntfy.dark
│   │               └── pushover.dark
│   ├── internal
│   │   └── tests.dark
│   ├── README.md
│   └── stachu
│       ├── json.dark
│       └── timespan.dark
├── .prettierignore
├── .prettierrc.toml
├── README.md
├── .roo
│   └── rules
│       └── rules.md
├── rundir
├── scripts
│   ├── build
│   │   ├── build-parser
│   │   ├── build-release-cli-exes.sh
│   │   ├── _build-server
│   │   ├── build-sqlite.sh
│   │   ├── build-tree-sitter.sh
│   │   ├── clear-all-local-dbs
│   │   ├── clear-builder-volumes
│   │   ├── clear-dotnet-build
│   │   ├── compile
│   │   ├── compile-project
│   │   ├── _dotnet-wrapper
│   │   └── reload-packages
│   ├── builder
│   ├── contributors
│   │   └── checkout-pull-request
│   ├── deployment
│   │   ├── buildcontainers.dark
│   │   ├── deploy-lock-all-clear
│   │   ├── deploy-lock-all-list
│   │   ├── deploy-lock-one-add
│   │   ├── deploy-lock-one-get-name
│   │   ├── deploy-lock-one-remove
│   │   ├── _deploy-lock-request
│   │   ├── deploy-lock-wait-and-acquire
│   │   ├── gke-deploy
│   │   ├── manual-deploy
│   │   ├── new-build-containers.sh
│   │   ├── new-deploy.sh
│   │   ├── new-push-containers.sh
│   │   ├── _notify-deployment-honeycomb
│   │   ├── _notify-deployment-rollbar
│   │   ├── publish-github-release
│   │   ├── publish-vs-code-extension
│   │   ├── README.md
│   │   └── replace-prod-packages
│   ├── devcontainer
│   │   ├── _allow-docker-access
│   │   ├── _assert-in-container
│   │   ├── chrome-seccomp.json
│   │   ├── _create-app-directories
│   │   ├── _create-cache-directories
│   │   ├── _create-dark-dev-network
│   │   ├── _setup-circleci-environment
│   │   ├── _setup-hosts
│   │   ├── sqlite-monitor.sh
│   │   ├── _vscode-post-start-command
│   │   └── _write-config-file
│   ├── formatting
│   │   ├── format
│   │   └── pre-commit-hook.sh
│   ├── installers
│   │   ├── install-dotnet8
│   │   ├── install-exe-file
│   │   ├── install-gz-file
│   │   └── install-targz-file
│   ├── launch-extension.sh
│   ├── linting
│   │   ├── _check-linked-libs
│   │   ├── shellchecker
│   │   └── yamllinter
│   ├── migrations
│   │   ├── mark-not-run
│   │   └── new
│   ├── package-extension.sh
│   ├── production
│   │   ├── connect-to-prod-exec
│   │   └── gcp-get-logs
│   ├── run-backend-datatests
│   ├── run-backend-server
│   ├── run-backend-tests
│   ├── run-cli
│   ├── run-in-docker
│   ├── run-local-exec
│   ├── run-parser-tests
│   ├── run-prod-exec
│   ├── run-pubsub-emulator
│   ├── run-second-instance
│   └── stop-second-instance
├── .style.yapf
├── tf
│   ├── apis.tf
│   ├── artifact_registry.tf
│   ├── bwdserver.tf
│   ├── cloudrun.tf
│   ├── cloudstorage.tf
│   ├── custom-domains.tf
│   ├── darklangio.tf
│   ├── iam.tf
│   ├── locals.tf
│   ├── main.tf
│   ├── pubsub.tf
│   ├── README.md
│   ├── secrets.tf
│   ├── service_env_vars.tf
│   ├── vpc.tf
│   └── workload_identity_pool.tf
├── tree-sitter-darklang
│   ├── binding.gyp
│   ├── Cargo.toml
│   ├── grammar.js
│   ├── package.json
│   ├── package-lock.json
│   ├── README.md
│   ├── src
│   │   ├── grammar.json
│   │   ├── node-types.json
│   │   ├── scanner.c
│   │   └── tree_sitter
│   │       └── parser.h
│   └── test
│       └── corpus
│           ├── exhaustive
│           │   ├── cli_scripts.txt
│           │   ├── comments.txt
│           │   ├── constant_decls.txt
│           │   ├── exprs
│           │   │   ├── binary_operation.txt
│           │   │   ├── bools.txt
│           │   │   ├── char.txt
│           │   │   ├── dicts.txt
│           │   │   ├── Enum.txt
│           │   │   ├── field_access.txt
│           │   │   ├── floats.txt
│           │   │   ├── fn_calls.txt
│           │   │   ├── if_else.txt
│           │   │   ├── ints.txt
│           │   │   ├── lambda.txt
│           │   │   ├── let_expr.txt
│           │   │   ├── list.txt
│           │   │   ├── match.txt
│           │   │   ├── parens.txt
│           │   │   ├── pipe.txt
│           │   │   ├── Record.txt
│           │   │   ├── record_update.txt
│           │   │   ├── statement.txt
│           │   │   ├── strings.txt
│           │   │   ├── tuple.txt
│           │   │   ├── units.txt
│           │   │   ├── value.txt
│           │   │   └── variables.txt
│           │   ├── fn_decls.txt
│           │   ├── module_decls.txt
│           │   ├── type_decls.txt
│           │   └── type_refs.txt
│           ├── README.md
│           ├── samples
│           │   └── README.md
│           └── _template.txt
├── user-code
│   └── darklang
│       └── scripts
│           ├── add.dark
│           ├── prompt.dark
│           ├── sample-for-testing-extension.dark
│           └── test.dark
├── .vscode
│   ├── extensions.json
│   ├── launch.json
│   ├── settings.json
│   └── tasks.json
├── vscode-extension
│   ├── client
│   │   ├── package.json
│   │   ├── package-lock.json
│   │   ├── src
│   │   │   ├── commands
│   │   │   │   ├── branchCommands.ts
│   │   │   │   ├── instanceCommands.ts
│   │   │   │   ├── packageCommands.ts
│   │   │   │   ├── scriptCommands.ts
│   │   │   │   └── syncCommands.ts
│   │   │   ├── data
│   │   │   │   └── branchStateManager.ts
│   │   │   ├── extension.ts
│   │   │   ├── panels
│   │   │   │   └── branchManagerPanel.ts
│   │   │   ├── providers
│   │   │   │   ├── content
│   │   │   │   │   └── packageContentProvider.ts
│   │   │   │   ├── darkContentProvider.ts
│   │   │   │   ├── darkFileSystemProvider.ts
│   │   │   │   ├── fileDecorationProvider.ts
│   │   │   │   ├── treeviews
│   │   │   │   │   ├── packagesTreeDataProvider.ts
│   │   │   │   │   └── workspaceTreeDataProvider.ts
│   │   │   │   ├── urlMetadataSystem.ts
│   │   │   │   └── urlPatternRouter.ts
│   │   │   ├── types
│   │   │   │   └── index.ts
│   │   │   └── ui
│   │   │       └── statusbar
│   │   │           └── statusBarManager.ts
│   │   └── tsconfig.json
│   ├── language-configuration.json
│   ├── package.json
│   ├── package-lock.json
│   ├── README.md
│   ├── static
│   │   ├── logo-dark.png
│   │   ├── logo-dark-transparent-low-margin.svg
│   │   ├── logo-dark-transparent.svg
│   │   └── logo-light-transparent.svg
│   ├── syntaxes
│   │   └── darklang.tmLanguage.json
│   └── tsconfig.json
└── .yamllint

171 directories, 1071 files
