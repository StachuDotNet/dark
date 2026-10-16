# Stdlib principles

The rules we design Dark's standard library by, what's settled, and what's still open. Read this before adding or changing a stdlib fn. Any person or agent writing stdlib code should follow it.

Sources:
- the 2026-10-07 stdlib call (Paul, Ocean, Stachu)
- Paul's pure-core stdlib audit (September 2026)
- the team's answers to its follow-up questionnaire (Paul, Feriel, Stachu)

Each rule is marked:
- **Decided:** follow it
- **Leaning:** follow it, but expect it may change
- **Open:** don't build on it yet; the question is written out

When a change contradicts a Decided rule, change the rule here in the same PR, or don't make the change.

---

## How we decide

**Write for the mainstream.** (Decided)
- Assume people know Python, JS, Go or Rust, and design so Dark fits in beside them and competes with them.
- Borrow from Rust, Elm, Roc and ReScript; be wary of copying OCaml and F# habits.
- The reader is an experienced programmer who shouldn't need a functional-programming background. Prefer familiar names over abstract ones: `flatMap`, not `bind`; `andThen` for chaining Option and Result.

**Consistency over cleverness.** (Decided)
- The same task gets the same shape everywhere: the same name, argument order, error style and units.
- Consistency is what lets an LLM guess an API it hasn't seen, and it helps people for the same reason.

**When there's no true answer, measure.** (Decided)
- Most of these questions don't have a right answer, so don't decide them on taste.
- Run the cheapest experiment that separates the options: which shape does an LLM write correctly in the fewest tokens and turns?
- Record the result here, beside the rule it settled.

**Immutable values, pure by default.** (Decided)
- Values don't change and fns don't reach outside themselves unless their effects say so.
- It's why code that compiles usually works, why pieces compose, and why it takes fewer tokens to get right.

**Expose details when callers need them.** (Decided)
- Limits, widths and representations are part of the API when a caller can trip over them: `Int8.maxValue`, `Float.toBits`, the unit a DateTime counts in.
- Hiding them doesn't make them go away; it makes them surprises.

**Builtins are primitives; policy lives in Dark.** (Decided)
- A builtin does one OS-level or performance-critical thing and carries data, not formatting or policy.
- Naming, error shapes, defaults and convenience belong in the Dark wrapper.
- Every builtin has exactly one stdlib wrapper, and Dark code calls the wrapper, never `Builtin.*`.
- Prefer deleting a builtin to adding one.

---

## Shape of a function

**Data first, callback last.** (Decided)
- The thing being operated on is the first argument, so `|>` pipes it in, and any callback goes last.
- Paul and Feriel chose this; Stachu preferred flipping the pipe so the data goes last.
- ReScript moved off pipe-last because JS developers found it confusing.

```
list |> List.map (fun x -> x + 1)
Tuple2.mapFirst pair fn
```

**No partial application.** (Decided, #5948)
- Supplying too few arguments is an error. To get a partially applied function, write a lambda.
- So argument order is chosen for how the call reads, not for currying.

**In a fold, the accumulator comes first.** (Decided 10-15)
- `List.fold list init (fun acc x -> ...)`, and the same for `Dict.fold` (acc, key, value).
- Paul, Stachu, the 10-07 call, Rust and Elm. Feriel picked an alternative whose wording is lost.

**One canonical name per operation; same job, same name.** (Leaning)
- Don't keep accidental duplicates (`ceiling`/`roundUp`, `dedup`/`unique`, `collect`/`combine`). A deliberate alias is different; see "Aliases".
- When two modules do the same job, they share the name: `contains` in List and String, `flatten` for removing a layer.
- Keep a different name when the job differs: `List.flatMap` expands a list, `Result.andThen` continues a computation, `Dict.size` vs `List.length`, `String.join` with a separator.
- Open: Paul would scope the sharing. Collections share names with each other and containers (Option, Result) share names with each other, but the two families needn't match.

**Name variants by behaviour.** (Decided)
- A variant that fails gets the prefix `checked` (`checkedAdd`), never `safe`.
- A variant that behaves differently says how in its name: `zipShortest`, `withDefault`, `toIntTruncated`, `decodeWithReplacement`.

**Keep internals out of the public surface.** (Decided)
- Helpers like `repeatUnsafe` and `SortByComparatorHelpers` are implementation details.
- If an internal helper is reachable, people and LLMs will call it.

---

## Failure

**Ordinary bad input is a value, not a crash.** (Decided as a direction; how far it goes is Open)
- If a caller can reasonably pass the input, the fn returns an Option or Result for it.
- Examples: division by zero, a key that isn't there, malformed Base64, NaN to Int.
- A runtime error means a defect in Dark or in the program, never "the user typed something odd".
- A declared Result has to be the whole truth: the native code must not raise on a path the signature says returns Error.

Open, and the biggest open question in this doc:
- how far do we go? Paul doesn't want anyone to ever ask "will this crash?". Stachu proposes Result everywhere, auto-unwrapped, so `let x = 2 / 2` reads as 1 and only a real error surfaces. Paul wants a spike first, for example unwrapping inside a loop whose fn doesn't return that type
- `?` to propagate errors instead of a `match` (`root ?|> print`), early return, `break`/`continue`
- a `Crash` effect, with Stachu's objection from the call (Paul agreed): it's just a box around the crash
- the checker noticing a fallible `List.map` body and pointing at `mapResult`
- showing people the happy path: views that hide Result plumbing by default
- Paul's spike should use a worked example; the quadratic formula has two roots, a sqrt of a negative and a divide by zero

**Errors are domain variants, not strings.** (Decided)
- `Error DivisionByZero`, `Error (DuplicateKey k)`, `Error OutOfRange`.
- Not `Error "Cannot divide by 0"`.
- Strings can carry the offending data or a message, never the classification.
- Each domain owns its error type; there's no catch-all error.
- A lint could keep this true: flag every `Result<_, String>` signature, along with conversions owned by the wrong module and families that drift apart.
- Open: Paul likes Roc's approach to error types, and the 10-07 call floated an open, extensible error type. Neither is designed yet.

**Option for absent, Result for failed.** (Decided 10-15)
- Option means "there's nothing there"; Result means "it failed, and here's why". So `List.head []` is `None`.
- The 10-07 call had leaned toward Result everywhere (Roc has no Option); the audit and Stachu chose the split.

**No crashing twins.** (Decided 10-15)
- A fallible fn doesn't get a crashing version beside it. Paul: offer a default-value variant (`divideWithDefault`) instead. Feriel had said yes.
- Open: whether "can crash" should be visible some other way.

**Out of range: interpret when there's a natural answer, fail when the input contradicts the operation.** (Leaning)
- `List.take 10 [1; 2; 3]` is the whole list; a negative take is empty. Feriel and Stachu chose this, and Paul agreed but wants unsigned counts.
- A negative repeat count is an error, because there's no sensible answer.
- Slicing is stricter: Feriel's answer is that an out-of-range or negative index is `Error OutOfRange`.
- Still to do: list every fn that takes a count or index, sort them into "interpret" or "fail", and write the reason for each group here.

---

## Numbers

**Integers.** (Leaning)
- `Int` is arbitrary precision today. Open: the default. Stachu's current view: it's decided at parse time, a literal defaults to Int64, and if the context narrows it (the other operand is a `UInt32`) it takes that width. Paul's audit argues for keeping arbitrary `Int`. Literal suffixes: `1u32`, `1s8`.
- Fixed-width arithmetic wraps. It never crashes on overflow.
- Each fixed-width type also gets a `checked` family that returns `Result<_, Overflow>` (#5950). Arbitrary Int doesn't need one.
- Fixed-width types expose `minValue`, `maxValue` and `bitWidth`.
- Lengths, counts and indexes stay `Int`. Paul preferred unsigned. Open (Stachu): should we ever convert between int types automatically?

**Conversions never wrap or round silently.** (Decided)
- A conversion that always fits returns the bare value; one that might not returns `Result<_, OutOfRange>`.
- The unqualified conversion is exact. A lossy one names what it does: `Float.toIntTruncated`, `Float.toIntRounded`.
- Wrapping is something you choose explicitly, in arithmetic, never a side effect of a conversion.

**Float stays in its own domain.** (Decided)
- NaN and the infinities are ordinary Float values, with names (`Float.nan`) and predicates.
- Float operations return Float: `round` gives `2.0`, and `acos 2.0` gives NaN rather than None.
- Leaving Float is where failure lives: `Float.toInt nan` is an Error.
- min and max propagate NaN whichever side it's on.
- Open: the tie rule for `round`. Today it's ties-to-even (`round 2.5 = 2.0`, .NET's default). Everyone picked "the alternative" in the questionnaire and its wording is lost; half away from zero is the likely reading.

**Make illegal states unrepresentable.** (Decided)
- A value with a fixed domain gets a type that holds only that domain:
  - bytes and ASCII codes: UInt8
  - UTF-16 code units: UInt16
  - code points: UInt32 (#5943)
  - Float bits: UInt64
  - calendar fields: UInt8 or UInt16
- OS values and protocol values get their own types (#5941, #5942).
- Open: the 10-07 call asked whether LLMs cope worse with this. Check.

---

## Conversions and ownership

**The more specialized type owns both directions.** (Decided 10-15)
- With a few aliases (below), who owns it matters less than it looks.
- `Int8.fromInt` / `Int8.toInt`, not `Int.toInt8`. A new type can then interoperate with core types without core changing.
- `Result.fromOption`, not `Option.toResult`. Between peer fixed-width types, the destination owns it: `Int16.fromInt8`.
- Float owns its conversions to and from integers.
- Text representations live on String and Char, byte representations on Bytes, and codecs in their own modules.

**`fromX` constructs, `toX` projects.** (Decided)
- Names come in pairs that mean the same representation in both directions: `String.toCodepoints`/`fromCodepoints`, `Float.toBits`/`fromBits`.
- `toX` says nothing about cost or loss; the docs do.

**A few deliberate aliases.** (Decided 10-15)
- Where a familiar name is common in other languages, the stdlib answers to it too: `Option.toResult` beside `Result.fromOption`, `Dict.toList` beside `Dict.toEntries`, `Tuple2.mapAll` beside `Tuple2.map`.
- A model's first guess then works instead of hitting "not found". The cost is more names to document and test, so they're chosen, not accumulated.
- Docs and `dark view` show the canonical name.

---

## Text

**A String is always valid Unicode.** (Decided)
- Every way in enforces it: constructors, decoders, JSON, regex output.
- Malformed data stays in Bytes.
- Strict decoders reject bad input; only a decoder named `WithReplacement` substitutes.

**A Char is one user-visible character, and String counts in Chars.** (Decided)
- Char is an extended grapheme cluster.
- Length, search, slice, take and drop all count Chars, so an index from one fn works in the next.
- Code points and encodings are reached explicitly: `String.toCodepoints`, `Utf8`, `Utf16`, `Ascii`.

**Regex never backtracks.** (Decided 10-15)
- RE2-style, like Go. Agents write most of our regexes, and a backtracking engine can hang on bad input.
- Patterns can be Strings (each call returns a Result for a bad pattern) or compiled once with `Regex.compile`.

**Casing follows full Unicode, and normalization is explicit.** (Leaning)
- Uppercasing can turn one Char into several, so `Char.toUppercase` returns a String.
- Normalization happens only when you ask for it.
- Open: Paul's question of how two spellings of "café" compare equal if we don't normalize. Also Turkish dotted and dotless i.

---

## Modules and boundaries

**Core modules stay foundational; each format gets its own module.** (Decided)
- These move out of String: English inflection, display-width padding, HTML escaping, digests, Base64.
- UTF-8, UTF-16, ASCII, hex and Base64 each get one module and one way to fail.

**At a boundary, be conservative by default and opt into more.** (Decided)
- We don't know what's on the other end. JSON rejects NaN by default and keeps big integers exact, and presets exist for float-only systems.
- Base64 decoding is strict by default, and `decodeAny` is the liberal version.
- Open: whether `Json.serialize config value` becomes the general pattern for other formats (protobuf and so on).

**Replacing is the default; strict is the named variant.** (Decided 10-15)
- `Dict.set` replaces, and so do `fromEntries` (last wins) and `merge` (right wins). `insert`, `fromEntriesUnique` and `mergeUnique` refuse a duplicate key with `DuplicateKey`.

**Collections.** (Leaning)
- No new collection types for now (Set, Array, NonEmptyList); add useful operations to the existing ones.
- Open: consolidating Array and List was put off at the 10-07 call.

---

## Traits

**Traits as behaviours or operations.** (Open)
- Today there's one trait per operator (`Add`, `Subtract` and so on).
- Paul's view is that traits should describe behaviours, like Java or C# interfaces. If they stay operator-shaped, they should carry laws (commutative, associative).
- Stachu owes a follow-up with more conventional traits.
- Decide before much user code implements the per-operator ones.

**Generic fns go through traits; equality is structural.** (Decided)
- min, max, abs, clamp and sort are generic over `Compare`. Don't write seven per-type copies of a generic fn.
- `==` is not a trait. Equality is structural for every value and a type can't override it. So `List.contains`, `unique`, `sort` and dict keys always mean the same thing as `==`.

---

## Open questions, collected

- How far "never crash" goes: auto-unwrapping, `?`, a `Crash` effect, and what `/` on two Ints returns
- The default Int, decided at parse time or not, and literal suffixes
- How far shared vocabulary reaches: across collections and containers, or within each; `collect`/`flatten` or `combine`/`join`
- Unsigned counts for repeat, take and drop
- The tie rule for `Float.round`, and what `Float.clamp` does with a NaN bound
- The exponent type for integer `power` (the `Power` trait fixes it to the base type today)
- `String.first`/`last` (counts) vs `take`/`takeLast`
- Whether a regex match reports positions, and in what unit
- `Dict.mapValues` and `mapKeys`
- Normalization and equality
- Traits as behaviours or operations
- An open error type
- Masked or mathematical bit shifts (Feriel and the 10-07 call differ from the audit)

Each one should end with an experiment or a decision recorded here.
