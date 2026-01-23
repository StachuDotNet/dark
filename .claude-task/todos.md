# Task Todos: Restrict Builtin Usage to One Package Function Each

## Phase 1: Initial Infrastructure
- [x] Add `UsageRestriction` type to RuntimeTypes.fs
  - Add type definition: `type UsageRestriction = AllowAny | AllowOne of FQFnName.Package`
  - Location: `/home/dark/app/backend/src/LibExecution/RuntimeTypes.fs` around line 1320

- [x] Add `usageRestriction` field to `BuiltInFn` record
  - Add field: `usageRestriction : UsageRestriction`
  - Location: `/home/dark/app/backend/src/LibExecution/RuntimeTypes.fs` line 1323
  - Default to `AllowAny` for now in all existing builtins

- [x] Update all builtin library files to include `usageRestriction = AllowAny`
  - Update template in each file in `/home/dark/app/backend/src/BuiltinExecution/Libs/*.fs`
  - Files: Bool.fs, Int8.fs, UInt8.fs, Int16.fs, UInt16.fs, Int32.fs, UInt32.fs, Int64.fs, UInt64.fs, Int128.fs, UInt128.fs, Float.fs, Math.fs, Bytes.fs, Char.fs, String.fs, List.fs, Dict.fs, DateTime.fs, Uuid.fs, Base64.fs, Json.fs, AltJson.fs, HttpClient.fs, LanguageTools.fs, Parser.fs, Crypto.fs, X509.fs, NoModule.fs
  - Also updated: BuiltinCli, BuiltinCliHost, BuiltinCloudExecution, BuiltinDarkInternal, BuiltinPM, and test files

- [x] Implement restriction checking in NameResolver.fs
  - Enhance `resolveFnName` to accept caller context (current package function ID)
  - Add validation logic to check if builtin's `usageRestriction` allows the caller
  - Return appropriate error if restriction violated
  - Location: `/home/dark/app/backend/src/LibParser/NameResolver.fs` around line 210-234

- [x] Thread caller context through WrittenTypesToProgramTypes.fs
  - Update `Context` type to include current package function ID
  - Pass this context through to `resolveFnName` calls
  - Location: `/home/dark/app/backend/src/LibParser/WrittenTypesToProgramTypes.fs`

- [ ] Add tests for restriction enforcement
  - Create test in `/home/dark/app/backend/tests/Tests/Builtin.Tests.fs`
  - Test that AllowAny works for any caller
  - Test that AllowOne blocks unauthorized callers
  - Test that AllowOne allows the specific authorized caller

- [x] Run tests to verify infrastructure works
  - Command: `./scripts/run-backend-tests`
  - Verify all tests pass with AllowAny defaults (9,313 tests passed!)

- [x] Commit initial infrastructure
  - Commit message: "Add UsageRestriction infrastructure for builtins"

## Phase 2: Easy Restrictions (AllowOne)

### Strategy
For each builtin that has a clear 1:1 wrapper in a package function, change `usageRestriction` from `AllowAny` to `AllowOne` with the specific package function.

### Int8 builtins
- [ ] Restrict Int8 builtins to Darklang.Stdlib.Int8 functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/Int8.fs`
  - Set `usageRestriction = AllowOne(FQFnName.Package for each wrapper)`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/int8.dark`
  - Test and commit

### UInt8 builtins
- [ ] Restrict UInt8 builtins to Darklang.Stdlib.UInt8 functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/UInt8.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/uint8.dark`
  - Test and commit

### Int16 builtins
- [ ] Restrict Int16 builtins to Darklang.Stdlib.Int16 functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/Int16.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/int16.dark`
  - Test and commit

### UInt16 builtins
- [ ] Restrict UInt16 builtins to Darklang.Stdlib.UInt16 functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/UInt16.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/uint16.dark`
  - Test and commit

### Int32 builtins
- [ ] Restrict Int32 builtins to Darklang.Stdlib.Int32 functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/Int32.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/int32.dark`
  - Test and commit

### UInt32 builtins
- [ ] Restrict UInt32 builtins to Darklang.Stdlib.UInt32 functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/UInt32.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/uint32.dark`
  - Test and commit

### Int64 builtins
- [ ] Restrict Int64 builtins to Darklang.Stdlib.Int64 functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/Int64.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/int64.dark`
  - Test and commit

### UInt64 builtins
- [ ] Restrict UInt64 builtins to Darklang.Stdlib.UInt64 functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/UInt64.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/uint64.dark`
  - Test and commit

### Int128 builtins
- [ ] Restrict Int128 builtins to Darklang.Stdlib.Int128 functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/Int128.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/int128.dark`
  - Test and commit

### UInt128 builtins
- [ ] Restrict UInt128 builtins to Darklang.Stdlib.UInt128 functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/UInt128.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/uint128.dark`
  - Test and commit

### Bool builtins
- [ ] Restrict Bool builtins to Darklang.Stdlib.Bool functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/Bool.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/bool.dark`
  - Test and commit

### Char builtins
- [ ] Restrict Char builtins to Darklang.Stdlib.Char functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/Char.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/char.dark`
  - Test and commit

### UUID builtins
- [ ] Restrict Uuid builtins to Darklang.Stdlib.Uuid functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/Uuid.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/uuid.dark`
  - Test and commit

### Base64 builtins
- [ ] Restrict Base64 builtins to Darklang.Stdlib.Base64 functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/Base64.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/base64.dark`
  - Test and commit

### Bytes builtins
- [ ] Restrict Bytes builtins to Darklang.Stdlib.Bytes functions
  - Update `/home/dark/app/backend/src/BuiltinExecution/Libs/Bytes.fs`
  - Verify package functions in `/home/dark/app/packages/darklang/stdlib/bytes.dark`
  - Test and commit

## Phase 3: Complex Restrictions (Require Analysis)

These builtins may be used by multiple package functions or have more complex usage patterns. Each needs careful analysis.

### Float builtins
- [ ] Analyze Float builtin usage across packages
  - Check all uses in `/home/dark/app/packages/darklang/stdlib/float.dark`
  - Check for any uses in Math or other modules
  - Decide on restriction strategy (might need multiple AllowOne or stay AllowAny)
  - Test and commit

### Math builtins
- [ ] Analyze Math builtin usage across packages
  - Similar analysis as Float
  - Test and commit

### String builtins
- [ ] Analyze String builtin usage across packages
  - Check all 25 uses found in grep
  - Likely need to stay AllowAny or have selective restrictions
  - Test and commit

### List builtins
- [ ] Analyze List builtin usage across packages
  - Check usage patterns
  - Test and commit

### Dict builtins
- [ ] Analyze Dict builtin usage across packages
  - Check usage patterns
  - Test and commit

### DateTime builtins
- [ ] Analyze DateTime builtin usage across packages
  - Check 23 uses found in grep
  - Test and commit

### Json builtins
- [ ] Analyze Json builtin usage across packages
  - Check both Json.fs and AltJson.fs
  - Test and commit

### HttpClient builtins
- [ ] Analyze HttpClient builtin usage across packages
  - Check usage in httpclient.dark
  - Test and commit

### Crypto builtins
- [ ] Analyze Crypto builtin usage across packages
  - Check usage in crypto.dark
  - Test and commit

### X509 builtins
- [ ] Analyze X509 builtin usage across packages
  - Check usage in x509.dark
  - Test and commit

### CLI-related builtins
- [ ] Analyze LanguageTools, Parser and other CLI builtins
  - Check usage across CLI modules
  - Test and commit

### NoModule builtins
- [ ] Analyze NoModule builtins (special case)
  - These may need to remain AllowAny
  - Test and commit

## Phase 4: Final Testing and Documentation

- [ ] Run full backend test suite
  - Command: `./scripts/run-backend-tests`
  - Fix any issues that arise

- [ ] Verify package loading works
  - Check logs in `./rundir/logs/packages-canvas.log`
  - Ensure no restriction violations during normal package loading

- [ ] Document the restriction system
  - Add comments in RuntimeTypes.fs explaining UsageRestriction
  - Add examples of how to add new restricted builtins

- [ ] Final commit
  - Commit message: "Complete builtin usage restrictions"

## Notes

- Each commit should be focused on a specific set of related builtins
- Test after each change to catch issues early
- If a builtin is used by multiple package functions legitimately, keep it as AllowAny for now
- The goal is gradual restriction, not immediate 100% coverage
