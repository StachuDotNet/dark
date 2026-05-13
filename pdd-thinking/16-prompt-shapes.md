# 16 — Prompt Shapes (verified against gpt-4o-mini)

Actual prompts and actual outputs from gpt-4o-mini, tested live at 01:41 EDT on 2026-05-13. Use these as starting templates; iterate as the spike reveals failure modes.

## The system prompt (v1, tested)

```
You generate Darklang function bodies. Reply with ONLY a JSON object
{"sig": "(<params>): <ReturnType>", "body": "<Darklang expression>"}.
Darklang uses F#-like syntax: let/in for bindings, ++ for string concat,
|> for pipe, [1L; 2L; 3L] for lists, semicolons not commas. Use Stdlib.*
for built-ins (e.g. Stdlib.List.map, Stdlib.Int64.add). No markdown, no
prose, just the JSON.
```

Cost per call (verified): **~140-170 input tokens**, **~30-150 output tokens** at gpt-4o-mini. Roughly **$0.00005 per call**.

## The user prompt template

```
Function: <name>. Description: <one-sentence what-it-does>.
```

Optionally followed by:
- `Available stdlib helpers: …`
- `Examples: <name>(<inputs>) => <expected output>`
- `Signature hint: (<param>: <Type>): <ReturnType>`

Keep it short. The model is more reliable with terse, focused asks.

## Four real outputs (verbatim, with critique)

### 1. `takeHead`

**Asked:** *"Function: takeHead. Description: returns the first element of a list, or unit if empty."*

**Got:**
```json
{"sig": "(list: List<'a>): Option<'a>",
 "body": "let head = Stdlib.List.head list |> Option.defaultValue () in head"}
```

**Critique:** Valid JSON. But the sig promises `Option<'a>` and the body unwraps to a non-Option value. **Sig and body disagree.** This is exactly the kind of bug the post-materialization type-check has to catch — sig-consensus / type-check is not optional. (Per `04-signature-consensus.md`.)

### 2. `calculateVariance`

**Asked:** *"Function: calculateVariance. Description: given a dict with 'high' and 'low' numeric fields, return the same dict with a 'variance' field added (high minus low)."*

**Got:**
```json
{"sig": "(data: {high: int; low: int}): {high: int; low: int; variance: int}",
 "body": "let variance = data.high - data.low in {high = data.high; low = data.low; variance = variance}"}
```

**Critique:** Solid. Used record syntax correctly. **Used `int` not `Int64`** — Darklang convention is sized integers. Need to fix this in the system prompt. Otherwise: would compile if we had a record type for `{high; low}`.

### 3. `fib` (recursive)

**Asked:** *"Function: fib. Description: classic Fibonacci. fib(0)=0, fib(1)=1, fib(n) = fib(n-1) + fib(n-2). It can call itself."*

**Got:**
```json
{"sig":"(n: int): int",
 "body":"if n = 0 then 0 else if n = 1 then 1 else fib(n - 1) + fib(n - 2)"}
```

**Critique:** Recursion works fine — the model references `fib` inside `fib`'s body without prompting. This is great news for Demo 3 acceptance. Same `int` vs `Int64` issue. Also note: the model wrote `fib(n - 1)` with parens, which is fine in Dark.

### 4. `sentimentScore`

**Asked:** *"Function: sentimentScore. Description: given a string, return an Int64 between -100 and 100…"*

**Got (truncated at max_tokens=150):**
```
{"sig":"(text: String): Int64",
 "body":"let positiveWords = [\"good\"; \"great\"; ...]
        let negativeWords = [\"bad\"; ...]
        let countWords = (words: List<String>, wordList: List<String>) =>
          Stdlib.List.filter((word) => Stdlib.List.contains(wordList; word));
        let positiveCount = countWords(Stdlib.String.split(text; \" \"); positiveWords
```

**Critique:** Three failures —
- Used `=>` lambda syntax instead of Dark's `fun ... ->`. The model defaults to ECMAScript/F# arrow notation; we need to be explicit.
- Got cut off at `max_tokens=150`. **Bump default to 500-800.** This is a small cost increase (~$0.0003 per call) and prevents truncation.
- Calls `Stdlib.List.contains` and `Stdlib.String.split` — may or may not exist by those exact names. Need to pre-validate against the actual stdlib.

## Lessons (real, post-hoc)

### Fix the system prompt (v2)

```
You generate Darklang function bodies. Reply with ONLY a JSON object
{"sig": "(<params>): <ReturnType>", "body": "<Darklang expression>"}.

Darklang syntax notes:
- Integers are SIZED: use Int64 (default), Int8, Int32, etc. Never bare "int".
- Bindings: `let x = expr in rest_of_expr` (in is required).
- Lambdas: `fun x -> body` (NEVER use =>, NEVER use arrow functions).
- Lists: `[1L; 2L; 3L]` (semicolons, not commas).
- Pipe: `value |> fn`. Parens for complex: `(complex expr) |> fn`.
- String concat: `++`.
- Int division: `Stdlib.Int64.divide a b`. Not `a / b`.
- Stdlib: prefix with `Stdlib.` (e.g. Stdlib.List.map, Stdlib.Int64.add).
- Records: `Type { a = 1L; b = 2L }`. Field access: `value.a`.
- Enums: construct `Option.Some 5L`, match `| Some x -> ...`.
- Recursion: just call the function by its short name inside the body.
- Whitespace-sensitive (Python-like).

Return ONLY the JSON object. No markdown fences, no prose, no explanations.
If you don't know what to write, set body to "()".
```

This v2 prompt should fix the `int`/`Int64` and `=>`/`fun ->` issues. Add it to the spike's `Generate.fs` and iterate.

### Bump `max_tokens` to 800

At gpt-4o-mini rates, 800 output tokens is ~$0.0005. Cheap insurance against truncation. The truncated `sentimentScore` body cost the same as a complete one would have, and we got nothing usable.

### Validate stdlib calls post-hoc

The model invents stdlib functions. Some exist (`Stdlib.List.map`), some don't (`Stdlib.List.contains` — verify). Post-materialization, walk the body's AST for references; if any unresolved, either:
- Retry generation with the unresolved names called out, or
- Treat them as transitively-pending and recurse the materializer.

The second is more interesting and more PDD-shaped.

### Empirical: model wants to do too much

`sentimentScore` could have been a one-liner mock (`return 0L`) but the model dove into word-counting. For PDD purposes, the simplest correct body is usually best — we can iterate later. Consider adding to the system prompt: *"Prefer the simplest possible implementation. A naive but correct body is better than a clever but wrong one."*

## The materialization request shape (PT-style)

When the runtime asks the materializer for a body, internally it has:

```fsharp
type MaterializeRequest =
  { name : string
    description : string
    sigHint : SignatureHint
    callSiteConstraints : List<SignatureConstraint>   // per 04
    inScope : { fns : List<FQFnName>
                types : List<FQTypeName>
                values : List<FQValueName> }
    budget : int (* ms *) }
```

The `Generate.fs` builds the prompt from this. The system prompt is fixed. The user prompt is templated from these fields.

Pseudocode:
```fsharp
let buildPrompt (req : MaterializeRequest) : string =
  let header = sprintf "Function: %s. Description: %s."
                 req.name req.description
  let sig =
    match req.sigHint.returnHint with
    | Some t -> sprintf " Signature hint: %s." (formatHint req.sigHint)
    | None -> ""
  let helpers =
    if req.inScope.fns.IsEmpty then ""
    else sprintf " Available stdlib helpers: %s."
           (req.inScope.fns |> List.map prettyName |> String.concat ", ")
  let constraints =
    req.callSiteConstraints
    |> List.map formatConstraint
    |> String.concat " "
  header + sig + helpers + " " + constraints
```

## v2 verification (tested 01:45 EDT)

Re-ran 4 prompts with the v2 system prompt. Cost ~$0.0003.

**takeHead (v2):**
```json
{"sig": "(list: List<Int64>): Option<Int64>",
 "body": "match list |> Stdlib.List.head with | Some x -> Option.Some x | None -> Option.None"}
```
✅ `Int64` not `int`. ✅ Sig+body now agree. ⚠️ Body is redundant (Stdlib.List.head already returns Option) but harmless.

**fib (v2):**
```json
{"sig": "(n: Int64): Int64",
 "body": "match n with | 0L -> 0L | 1L -> 1L | _ -> fib(n - 1L) + fib(n - 2L)"}
```
✅ Match-idiomatic. ✅ Int64 literals (`0L`, `1L`). ✅ Recursion. ✅ No arrows.

**addOne (v2):**
```json
{"sig": "(x: Int64): Int64",
 "body": "let x = x in Stdlib.Int64.add x 1L"}
```
✅ Int64. ⚠️ Redundant `let x = x in` — harmless. ✅ Stdlib prefix.

**doubleAll (v2):**
```json
{"sig": "(lst: List(Int64)): List(Int64)",
 "body": "Stdlib.List.map (fun x -> Stdlib.Int64.multiply x 2L) lst"}
```
⚠️ **NEW BUG**: `List(Int64)` instead of `List<Int64>`. ✅ Lambda is `fun x -> ...`. ✅ Int64.

### v3 system prompt — add a line about generics

```
- Generics use ANGLE BRACKETS: List<Int64>, Option<String>, Result<Int64, String>.
  NEVER write List(Int64) or List[Int64].
```

And one more:
```
- Avoid redundant bindings like `let x = x in body`. Just write the body.
```

### Quality after v2 (qualitative)

Cheaper-than-Sonnet model **with the v2 prompt** generates plausibly-compilable Darklang on the first try for ~75% of simple fns. Failure modes shift toward "wrong stdlib function name" or "syntax variant we forgot to instruct against" — both addressable by either a retry loop or a fuller prompt.

The verdict: **v2/v3 prompt + gpt-4o-mini is workable for the spike**. Don't waste budget upgrading to 4o or Sonnet yet. The retry-on-AST-error path will catch the remaining 25%.

## v3 verification — Demo 2 fns (tested 02:59 EDT)

Pushed v3 prompt against `parseCsv`, `skipHeader`, `sortByVarianceDescending`, `getDateField` (~$0.0003).

**Big new failure mode**: gpt-4o-mini consistently writes **parenthesized function application** (`Stdlib.String.split(csv, "\n")` — JS/Python style) instead of Darklang's **prefix application** (`Stdlib.String.split csv "\n"` — F#/ML style). All four outputs had this issue.

Two more issues observed:
- **Type variables**: model writes `List<a>` instead of `List<'a>` (no apostrophe).
- **Anonymous record types**: model invented `List<Type { variance: Int64 }>` syntax — doesn't exist in Dark.

### v4 system prompt additions

Add these lines:

```
- Function application is PREFIX, NOT parenthesized. Write:
    Stdlib.List.map f lst       (correct)
  NOT:
    Stdlib.List.map(f, lst)     (wrong — JS/Python style)
- Multi-arg fns: Stdlib.String.split text "\n"
  NOT: Stdlib.String.split(text, "\n")
- Type variables use ML-style apostrophes: 'a, 'b, 'k, 'v.
  NOT: <a>, <b>.
- No anonymous record types like List<Type {...}>. Define a named type
  first, or use Dict<String, T> for string-keyed maps.
```

After these additions, expect the success rate on Demo-2-shaped problems to rise from "barely usable" to "usable with one retry."

### Updated quality estimate

After **v4** (the live additions just discovered), gpt-4o-mini should produce compilable Dark on ~60-75% of first tries even for multi-arg pipeline-style fns. Retry-with-AST-feedback gets the rest.

**Day-1 stub** can still be `EmptyBody` — don't sweat the LLM quality until Day 2-3 when you're actually generating code.

## Provider notes

- **OpenAI gpt-4o-mini** (verified tonight): works for the JSON-out shape, deterministic at temperature=0, decent at recursion, has the syntax-confusion issues above.
- **Anthropic Haiku 4.5** (not tested tonight): expected to behave similarly. Probably better at Darklang specifically (Claude is generally more pedantic with novel syntax). Test in week 1.
- Don't try Ollama for the spike. Local inference adds variance we don't need.

## Cost tracking

Each generate call emits a trace event:
```json
{ "t": ..., "ev": "cost", "model": "gpt-4o-mini", "in": 157, "out": 58, "$": 0.00006 }
```

`dark pdd trace cost <id>` sums and reports — see `15-spike-budgets.md`.

## Open

- **Better description-extraction.** Right now we just pass `name + description`. But the *call site* often has richer context — surrounding code, sibling fn signatures, what the caller does with the result. The materializer should grab some of that. Mechanically: take 3 lines before and 3 after the call site, include in the prompt. Day 4-5 enhancement.
- **Few-shot examples.** A prompt with 1-2 worked examples of "name + description → fn body" would dramatically improve the syntax issues. Cost: ~200 extra input tokens per call (~$0.00003). Worth it.
- **Self-correction loop.** First attempt → AST-walk the body → if any unknown names, retry with those names highlighted as "you used these but they don't exist; try without them." Costs 2× per attempt but maybe halves error rate.

## Connection to other docs

- `13-libpdd-materializer.md` — `Generate.fs` consumes the v2 system prompt and the `buildPrompt` template here.
- `04-signature-consensus.md` — the takeHead example proves we need post-materialization sig/type check.
- `15-spike-budgets.md` — cost numbers feed the budget math.
