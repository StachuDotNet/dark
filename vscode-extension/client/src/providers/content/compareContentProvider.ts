import { ParsedUrl } from "../urlPatternRouter";

/**
 * Content provider for comparison URLs
 * Handles: dark://compare/version1/version2[?target=item]
 */
export class CompareContentProvider {
  static getContent(parsedUrl: ParsedUrl): string {
    const { context, queryParams, view } = parsedUrl;
    const target = queryParams?.target;

    if (!context) {
      return this.getCompareListContent();
    }

    const [version1, version2] = context.split('/');

    switch (view) {
      case 'unified':
        return this.getUnifiedDiffView(version1, version2, target);
      case 'split':
        return this.getSplitView(version1, version2, target);
      case 'stats':
        return this.getStatsView(version1, version2, target);
      default:
        return this.getSideBySideView(version1, version2, target);
    }
  }

  private static getSideBySideView(version1: string, version2: string, target?: string): string {
    if (version1 === 'v1.2.0' && version2 === 'v1.2.1') {
      return this.getListMapComparison();
    } else if (version1 === 'current' && version2.startsWith('patch-')) {
      return this.getCurrentVsPatchComparison(version2);
    } else {
      return this.getGenericComparison(version1, version2, target);
    }
  }

  private static getListMapComparison(): string {
    return `# Compare: Darklang.Stdlib.List.map v1.2.0 → v1.2.1

## Side-by-Side Comparison

### Version Information
| Field | v1.2.0 | v1.2.1 |
|-------|--------|--------|
| **Author** | bob | alice |
| **Date** | 2024-01-05 | 2024-01-10 |
| **Patch** | Type system improvements | Performance optimization |
| **Lines** | 15 | 23 |

### Implementation Changes

#### v1.2.0 (Previous)
\`\`\`fsharp
let map (fn: 'a -> 'b) (list: List<'a>): List<'b> =
  match list with
  | [] -> []
  | head :: tail -> (fn head) :: (map fn tail)

// Maps a function over a list, returning a new list
// Example usage:
// map (fun x -> x * 2) [1L; 2L; 3L]
// Returns: [2L; 4L; 6L]

// Tests:
Stdlib.Test.expect (map (fun x -> x * 2L) []) []
Stdlib.Test.expect (map (fun x -> x * 2L) [1L; 2L; 3L]) [2L; 4L; 6L]
\`\`\`

#### v1.2.1 (Current)
\`\`\`fsharp
let map (fn: 'a -> 'b) (list: List<'a>): List<'b> =
  let rec mapTailRec acc fn remaining =
    match remaining with
    | [] -> Stdlib.List.reverse acc
    | head :: tail -> mapTailRec ((fn head) :: acc) fn tail
  mapTailRec [] fn list

// Maps a function over a list, returning a new list
// Optimized tail-recursive implementation for large lists
// Performance: O(n) time, O(1) stack space

// Example usage:
// Stdlib.List.map (fun x -> x * 2) [1L; 2L; 3L]
// Returns: [2L; 4L; 6L]

// Tests:
Stdlib.Test.expect (map (fun x -> x * 2L) []) []
Stdlib.Test.expect (map (fun x -> x * 2L) [1L; 2L; 3L]) [2L; 4L; 6L]

// Performance test for large lists
Stdlib.Test.benchmark "map_large_list" (fun () ->
  let largeList = Stdlib.List.range 1L 10000L
  Stdlib.List.map (fun x -> x * 2L) largeList
)

// Stack overflow prevention test
Stdlib.Test.expect_no_overflow (fun () ->
  let veryLargeList = Stdlib.List.range 1L 100000L
  Stdlib.List.map (fun x -> x) veryLargeList
)
\`\`\`

## Change Summary

### Added Lines (8)
\`\`\`diff
+ let rec mapTailRec acc fn remaining =
+   match remaining with
+   | [] -> Stdlib.List.reverse acc
+   | head :: tail -> mapTailRec ((fn head) :: acc) fn tail
+ mapTailRec [] fn list
+ // Optimized tail-recursive implementation for large lists
+ // Performance: O(n) time, O(1) stack space
+ // Stdlib.List.map (fun x -> x * 2) [1L; 2L; 3L]
\`\`\`

### Removed Lines (3)
\`\`\`diff
- match list with
- | [] -> []
- | head :: tail -> (fn head) :: (map fn tail)
\`\`\`

### Modified Lines (1)
\`\`\`diff
- // map (fun x -> x * 2) [1L; 2L; 3L]
+ // Stdlib.List.map (fun x -> x * 2) [1L; 2L; 3L]
\`\`\`

## Performance Impact

### Benchmark Results
| List Size | v1.2.0 Time | v1.2.1 Time | Improvement |
|-----------|-------------|-------------|-------------|
| 100       | 0.5ms       | 0.5ms       | 0%          |
| 1,000     | 5.2ms       | 3.8ms       | 27%         |
| 10,000    | 52ms        | 31ms        | 40%         |
| 100,000   | Stack overflow | 290ms    | ∞ (Fixed)   |

### Memory Usage
- **v1.2.0:** O(n) stack space (recursive calls)
- **v1.2.1:** O(1) stack space (tail-recursive)
- **v1.2.1:** Additional O(n) heap for accumulator

## API Compatibility

✅ **Fully Compatible**
- Function signature unchanged
- Behavior identical for all inputs
- Return type unchanged
- No breaking changes

## Test Changes

### New Tests Added
- Performance benchmark for large lists
- Stack overflow prevention test
- Memory usage validation

### Test Results
- **v1.2.0:** 3 tests, all passing
- **v1.2.1:** 5 tests, all passing
- **Coverage:** 85% → 95%

## View Options

- [📊 Statistics View](dark://compare/v1.2.0/v1.2.1?view=stats&target=Darklang.Stdlib.List.map)
- [📄 Unified Diff](dark://compare/v1.2.0/v1.2.1?view=unified&target=Darklang.Stdlib.List.map)
- [🔀 Split View](dark://compare/v1.2.0/v1.2.1?view=split&target=Darklang.Stdlib.List.map)

## Actions

- [📝 View v1.2.0](dark://package/Darklang.Stdlib.List.map@v1.2.0)
- [📝 View v1.2.1](dark://package/Darklang.Stdlib.List.map@v1.2.1)
- [📈 View History](dark://history/Darklang.Stdlib.List.map)
- [🔧 Create Patch](command:darklang.patch.create?base=v1.2.1)`;
  }

  private static getCurrentVsPatchComparison(patchVersion: string): string {
    const patchId = patchVersion.replace('patch-', '');
    return `# Compare: Current → ${patchVersion}

## Comparing Current State with Patch Changes

### Patch Information
- **Patch ID:** ${patchId}
- **Author:** stachu
- **Intent:** Add user validation to authentication system
- **Status:** Draft

## Changed Items

### 1. MyApp.User.validate (NEW)
**Operation:** CREATE
**Status:** ✅ New function added

\`\`\`diff
+ let validate (user: MyApp.User.UserInput): Result<MyApp.User.User, List<ValidationError>> =
+   let errors = []
+
+   // Email validation
+   let emailErrors =
+     if Stdlib.String.isEmpty user.email then
+       [MissingField "email"]
+     elif not (Stdlib.String.contains user.email "@") then
+       [InvalidEmail]
+     else
+       []
+
+   // Password validation
+   let passwordErrors =
+     if Stdlib.String.isEmpty user.password then
+       [MissingField "password"]
+     elif Stdlib.String.length user.password < 8L then
+       [WeakPassword]
+     else
+       []
+
+   let allErrors = Stdlib.List.append emailErrors passwordErrors
+
+   if Stdlib.List.isEmpty allErrors then
+     Ok {
+       id = Stdlib.Uuid.generate ()
+       email = user.email
+       hashedPassword = MyApp.Auth.hashPassword user.password
+       createdAt = Stdlib.DateTime.now ()
+     }
+   else
+     Error allErrors
\`\`\`

### 2. MyApp.User.ValidationError (NEW)
**Operation:** CREATE
**Status:** ✅ New type added

\`\`\`diff
+ type ValidationError =
+   | InvalidEmail
+   | WeakPassword
+   | MissingField of String
\`\`\`

### 3. MyApp.User.create (MODIFIED)
**Operation:** MODIFY
**Status:** ✅ Updated to use validation

\`\`\`diff
let create (input: UserInput): Result<User, String> =
- // TODO: Add validation
- Ok {
-   id = Stdlib.Uuid.generate ()
-   email = input.email
-   hashedPassword = MyApp.Auth.hashPassword input.password
-   createdAt = Stdlib.DateTime.now ()
- }
+ match MyApp.User.validate input with
+ | Ok user -> Ok user
+ | Error errors ->
+   let errorMsg =
+     errors
+     |> Stdlib.List.map (fun err ->
+       match err with
+       | InvalidEmail -> "Invalid email format"
+       | WeakPassword -> "Password too weak"
+       | MissingField field -> field ++ " is required"
+     )
+     |> Stdlib.String.join ", "
+   Error errorMsg
\`\`\`

## Impact Summary

### Lines Changed
- **Added:** 52 lines
- **Removed:** 8 lines
- **Modified:** 3 lines
- **Net Change:** +44 lines

### API Changes
- ✅ No breaking changes to existing APIs
- 🆕 New public function: MyApp.User.validate
- 🆕 New public type: MyApp.User.ValidationError
- 🔄 Enhanced error handling in MyApp.User.create

### Test Impact
- **New Tests:** 12 tests added
- **Coverage Change:** 78% → 95%
- **All Tests:** Passing

## Validation Results

### Automated Checks
- ✅ Syntax validation passed
- ✅ Type checking passed
- ✅ Style check passed
- ✅ Security scan passed
- ✅ Performance analysis passed

### Manual Review Status
- ⏳ Peer review pending
- ⏳ Architecture review pending
- ⏳ Security review pending

## Actions

- [📝 View Patch Details](dark://patch/${patchId})
- [🔍 Review Patch](command:darklang.patch.review?${patchId})
- [✅ Apply Patch](command:darklang.patch.apply?${patchId})
- [❌ Reject Patch](command:darklang.patch.reject?${patchId})
- [📊 Impact Analysis](dark://patch/${patchId}/impact)`;
  }

  private static getGenericComparison(version1: string, version2: string, target?: string): string {
    const targetDisplay = target ? ` - ${target}` : '';
    return `# Compare: ${version1} → ${version2}${targetDisplay}

## Comparison Summary

### Version Information
| Field | ${version1} | ${version2} |
|-------|-------------|-------------|
| **Date** | Unknown | Unknown |
| **Author** | Unknown | Unknown |
| **Status** | Available | Available |

### Changes Overview
- Lines added: Unknown
- Lines removed: Unknown
- Functions changed: Unknown
- Types changed: Unknown

## Detailed Diff

This comparison view would show the detailed differences between ${version1} and ${version2}.

${target ? `\n### Target: ${target}\nSpecific changes to ${target} would be highlighted here.` : ''}

## Actions

- [📝 View ${version1}](dark://package/${target || 'Unknown'}@${version1})
- [📝 View ${version2}](dark://package/${target || 'Unknown'}@${version2})
- [📈 View History](dark://history/${target || 'Unknown'})

*This is a placeholder comparison view.*`;
  }

  private static getUnifiedDiffView(version1: string, version2: string, target?: string): string {
    return `# Unified Diff: ${version1} → ${version2}

## ${target || 'Multiple Files'}

\`\`\`diff
@@ -1,8 +1,15 @@
 let map (fn: 'a -> 'b) (list: List<'a>): List<'b> =
-  match list with
-  | [] -> []
-  | head :: tail -> (fn head) :: (map fn tail)
+  let rec mapTailRec acc fn remaining =
+    match remaining with
+    | [] -> Stdlib.List.reverse acc
+    | head :: tail -> mapTailRec ((fn head) :: acc) fn tail
+  mapTailRec [] fn list

 // Maps a function over a list, returning a new list
+// Optimized tail-recursive implementation for large lists
+// Performance: O(n) time, O(1) stack space
+
 // Example usage:
-// map (fun x -> x * 2) [1L; 2L; 3L]
+// Stdlib.List.map (fun x -> x * 2) [1L; 2L; 3L]
 // Returns: [2L; 4L; 6L]

@@ -12,0 +19,12 @@
+// Performance test for large lists
+Stdlib.Test.benchmark "map_large_list" (fun () ->
+  let largeList = Stdlib.List.range 1L 10000L
+  Stdlib.List.map (fun x -> x * 2L) largeList
+)
+
+// Stack overflow prevention test
+Stdlib.Test.expect_no_overflow (fun () ->
+  let veryLargeList = Stdlib.List.range 1L 100000L
+  Stdlib.List.map (fun x -> x) veryLargeList
+)
\`\`\`

## Summary
- **Added:** 12 lines
- **Removed:** 3 lines
- **Modified:** 1 line

[📊 View Statistics](dark://compare/${version1}/${version2}?view=stats&target=${target})
[🔄 Switch to Side-by-Side](dark://compare/${version1}/${version2}?view=side-by-side&target=${target})`;
  }

  private static getSplitView(version1: string, version2: string, target?: string): string {
    return `# Split View: ${version1} → ${version2}

<div style="display: grid; grid-template-columns: 1fr 1fr; gap: 20px;">

<div>
## ${version1}
\`\`\`fsharp
let map (fn: 'a -> 'b) (list: List<'a>): List<'b> =
  match list with
  | [] -> []
  | head :: tail -> (fn head) :: (map fn tail)

// Maps a function over a list, returning a new list
// Example usage:
// map (fun x -> x * 2) [1L; 2L; 3L]
// Returns: [2L; 4L; 6L]

// Tests:
Stdlib.Test.expect (map (fun x -> x * 2L) []) []
Stdlib.Test.expect (map (fun x -> x * 2L) [1L; 2L; 3L]) [2L; 4L; 6L]
\`\`\`
</div>

<div>
## ${version2}
\`\`\`fsharp
let map (fn: 'a -> 'b) (list: List<'a>): List<'b> =
  let rec mapTailRec acc fn remaining =
    match remaining with
    | [] -> Stdlib.List.reverse acc
    | head :: tail -> mapTailRec ((fn head) :: acc) fn tail
  mapTailRec [] fn list

// Maps a function over a list, returning a new list
// Optimized tail-recursive implementation for large lists
// Performance: O(n) time, O(1) stack space

// Example usage:
// Stdlib.List.map (fun x -> x * 2) [1L; 2L; 3L]
// Returns: [2L; 4L; 6L]

// Tests:
Stdlib.Test.expect (map (fun x -> x * 2L) []) []
Stdlib.Test.expect (map (fun x -> x * 2L) [1L; 2L; 3L]) [2L; 4L; 6L]

// Performance test for large lists
Stdlib.Test.benchmark "map_large_list" (fun () ->
  let largeList = Stdlib.List.range 1L 10000L
  Stdlib.List.map (fun x -> x * 2L) largeList
)
\`\`\`
</div>

</div>

## Change Indicators
- 🟢 **Added:** Performance optimizations, better documentation
- 🔴 **Removed:** Simple recursive implementation
- 🟡 **Modified:** Function call style in examples

[📊 View Statistics](dark://compare/${version1}/${version2}?view=stats&target=${target})
[📄 Switch to Unified](dark://compare/${version1}/${version2}?view=unified&target=${target})`;
  }

  private static getStatsView(version1: string, version2: string, target?: string): string {
    return `# Statistics: ${version1} → ${version2}

## Change Metrics

### Code Size
| Metric | ${version1} | ${version2} | Change |
|--------|-------------|-------------|--------|
| **Lines of Code** | 15 | 23 | +8 (+53%) |
| **Functions** | 1 | 1 | 0 |
| **Types** | 0 | 0 | 0 |
| **Tests** | 3 | 5 | +2 |
| **Comments** | 4 | 7 | +3 |

### Complexity
| Metric | ${version1} | ${version2} | Change |
|--------|-------------|-------------|--------|
| **Cyclomatic Complexity** | 3 | 6 | +3 |
| **Max Nesting Depth** | 2 | 3 | +1 |
| **Maintainability Index** | 85 | 88 | +3 |

### Performance
| Metric | ${version1} | ${version2} | Improvement |
|--------|-------------|-------------|-------------|
| **Small Lists (100)** | 0.5ms | 0.5ms | 0% |
| **Large Lists (10K)** | 52ms | 31ms | 40% |
| **Memory Usage** | O(n) stack | O(1) stack | Significant |
| **Stack Safety** | No | Yes | ✅ |

### Test Coverage
| Metric | ${version1} | ${version2} | Change |
|--------|-------------|-------------|--------|
| **Line Coverage** | 85% | 95% | +10% |
| **Branch Coverage** | 75% | 90% | +15% |
| **Test Count** | 3 | 5 | +2 |

## Change Breakdown

### Additions (+12 lines)
- Tail-recursive helper function (5 lines)
- Performance documentation (3 lines)
- Performance tests (4 lines)

### Deletions (-3 lines)
- Simple recursive implementation
- Basic pattern matching

### Modifications (1 line)
- Updated function call style in examples

## Quality Impact

### Code Quality Score
- **Before:** 8.5/10
- **After:** 9.2/10
- **Improvement:** +0.7

### Risk Assessment
- **Breaking Changes:** None
- **API Compatibility:** Full
- **Performance Risk:** Low (improvement only)
- **Maintainability:** Improved

## Review Metrics

### Reviewer Feedback
- Performance: Excellent improvement
- Readability: Slightly more complex but well-documented
- Maintainability: Improved with better tests

### Approval Status
- ✅ Automated checks passed
- ✅ Performance benchmarks passed
- ✅ Security scan clean
- ✅ Manual review approved`;
  }

  private static getCompareListContent(): string {
    return `# Version Comparison Browser

## Recent Comparisons

### Today
- [v1.2.0 → v1.2.1](dark://compare/v1.2.0/v1.2.1?target=Darklang.Stdlib.List.map) - Performance optimization
- [current → patch-abc123](dark://compare/current/patch-abc123) - User validation patch

### This Week
- [v1.1.3 → v1.2.0](dark://compare/v1.1.3/v1.2.0) - Type system improvements
- [current → patch-def456](dark://compare/current/patch-def456) - Email validation fixes

## Quick Comparisons

### Standard Library
- [List.map improvements](dark://compare/v1.2.0/v1.2.1?target=Darklang.Stdlib.List.map)
- [String.split optimization](dark://compare/v1.1.0/v1.2.0?target=Darklang.Stdlib.String.split)
- [Option.map changes](dark://compare/v1.0.0/v1.1.0?target=Darklang.Stdlib.Option.map)

### Application Code
- [User module evolution](dark://compare/v1.0.0/current?target=MyApp.User)
- [Auth improvements](dark://compare/v2.0.0/current?target=MyApp.Auth)

## Compare Options

### Version Types
- **Tags:** v1.0.0, v1.1.0, v1.2.0, v1.2.1
- **Branches:** main, develop, feature-auth
- **Patches:** patch-abc123, patch-def456
- **Special:** current, working-copy

### Comparison Tools
- [📊 Create Custom Comparison](command:darklang.compare.create)
- [📈 Bulk Comparison](command:darklang.compare.bulk)
- [⏱️ Time Range Analysis](command:darklang.compare.timeRange)

*Select two versions to compare their differences.*`;
  }
}