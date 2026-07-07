/// Lexer for the canonical Darklang syntax this repo uses (suffixed ints `1L`,
/// `fun ->`, `;`-lists, `::`, space-application; rejects `=>`). Produces
/// `Tokenizer.Token`s with source ranges for the parser to consume.
///
/// Regular string/char escapes are processed (`unescape`); triple-quoted strings
/// stay raw.
module LibParser.InterpTokenizer

open LibParser.Tokenizer // Pos, TokenRange, Token

type TriviaKind =
  | LineComment // `// …` (and `////…`)
  | DocComment // `/// …`
  | BlockComment // `(* … *)`

/// A comment between the previous token and the one carrying it. Whitespace is
/// not stored — blank lines are derivable from the token/trivia range gaps.
type Trivia = { kind : TriviaKind; text : string; range : TokenRange }

type SpannedToken =
  { token : Token
    text : string
    range : TokenRange
    // accumulated `///` doc-comment text immediately preceding this token (the
    // declaration it documents), if any. Used to recover fn/type descriptions.
    docComment : string option
    // comments between the previous token and this one, in source order — kept
    // so tooling (formatter, lossless round-trip) can reproduce the source.
    // Trailing comments at EOF land on the TEOF token.
    leadingTrivia : List<Trivia> }

/// THE escape table: decode the escape starting at `s[i]` (which must be `\`).
/// Returns `Some(charsConsumed, decodedText)`, or `None` if invalid: an unknown
/// escape letter, a short/non-hex `\x`/`\X`/`\u`/`\U`, or a codepoint that isn't
/// a valid Unicode scalar (surrogate or > 0x10FFFF). Both the decoder
/// (`unescape`) and the validators (`hasInvalidEscape*`) are driven by this one
/// function so they can't drift.
let private decodeEscape (s : string) (i : int) : Option<int * string> =
  let len = s.Length
  let hexInt (start : int) (count : int) : int option =
    if start + count <= len then
      match
        System.Int32.TryParse(
          s.Substring(start, count),
          System.Globalization.NumberStyles.AllowHexSpecifier,
          null
        )
      with
      | true, v -> Some v
      | _ -> None
    else
      None
  // a Unicode scalar value → its UTF-16 string (a surrogate pair above the BMP)
  let scalar (start : int) (count : int) : string option =
    match hexInt start count with
    | Some cp when cp >= 0 && cp <= 0x10FFFF && not (cp >= 0xD800 && cp <= 0xDFFF) ->
      Some(System.Char.ConvertFromUtf32 cp)
    | _ -> None
  if i + 1 >= len then
    None // trailing backslash
  else
    match s[i + 1] with
    | 'n' -> Some(2, "\n")
    | 't' -> Some(2, "\t")
    | 'r' -> Some(2, "\r")
    | 'a' -> Some(2, string (char 7)) // bell
    | 'b' -> Some(2, string (char 8)) // backspace
    | 'v' -> Some(2, string (char 11)) // vertical tab
    | 'f' -> Some(2, string (char 12)) // form feed
    | '\\' -> Some(2, "\\")
    | '"' -> Some(2, "\"")
    | '\'' -> Some(2, "'")
    | '/' -> Some(2, "/")
    | '0' -> Some(2, "\000")
    | '{' -> Some(2, "{")
    | '}' -> Some(2, "}")
    | 'x' -> hexInt (i + 2) 2 |> Option.map (fun v -> (4, string (char v))) // \xHH
    // \XHHHH and \uHHHH both denote a Unicode scalar, so both reject surrogates /
    // out-of-range codepoints via `scalar` (a lone `\XD800` is invalid, like `\uD800`)
    | 'X' -> scalar (i + 2) 4 |> Option.map (fun t -> (6, t)) // \XHHHH
    | 'u' -> scalar (i + 2) 4 |> Option.map (fun t -> (6, t)) // \uHHHH (BMP)
    | 'U' -> scalar (i + 2) 8 |> Option.map (fun t -> (10, t)) // \UHHHHHHHH
    | _ -> None

/// Process escape sequences in regular (NOT triple-quoted) string/char content.
/// Error-recovering: an invalid escape keeps the backslash as-is.
/// Module-level so the parser can reuse it for interpolated-string literal parts.
/// The result is NFC-normalized so a literal's stored form is canonical (a
/// decomposed vs composed grapheme hashes/compares identically) — done in the
/// SHARED tokenizer so the F# and Dark lowerings both see the same bytes.
let unescape (s : string) : string =
  let decoded =
    if not (s.Contains '\\') then
      s
    else
      let sb = System.Text.StringBuilder(s.Length)
      let len = s.Length
      let mutable i = 0
      while i < len do
        if s[i] = '\\' then
          match decodeEscape s i with
          | Some(k, text) ->
            sb.Append text |> ignore
            i <- i + k
          | None ->
            sb.Append s[i] |> ignore
            i <- i + 1
        else
          sb.Append s[i] |> ignore
          i <- i + 1
      sb.ToString()
  decoded.Normalize()

/// Does `content` (RAW inner text of a regular — NOT triple-quoted — string/char)
/// contain an invalid escape? Callers turn a `true` into a `ParseError.Unparseable`.
let hasInvalidEscape (content : string) : bool =
  if not (content.Contains '\\') then
    false
  else
    let len = content.Length
    let mutable i = 0
    let mutable bad = false
    while i < len && not bad do
      if content[i] = '\\' then
        match decodeEscape content i with
        | Some(k, _) -> i <- i + k
        | None -> bad <- true
      else
        i <- i + 1
    bad


/// Index of the `}` closing an interpolation expression region, scanning
/// `s[start..limit-1]` with `start` just past the opening `{`. Tracks nested
/// braces and skips embedded string AND char literals (a `{`/`}` inside one
/// must not desync the scan). Returns -1 if unclosed. The tokenizer, the escape
/// validator, and the parser's rescan all delimit regions with THIS scanner —
/// they must agree on where a region ends.
let findInterpExprClose (s : string) (limit : int) (start : int) : int =
  let mutable k = start
  let mutable depth = 0
  let mutable found = -1
  while found < 0 && k < limit do
    if s[k] = '"' then
      let mutable m = k + 1
      let mutable inStr = true
      while inStr && m < limit do
        if s[m] = '\\' then
          m <- m + 2
        elif s[m] = '"' then
          m <- m + 1
          inStr <- false
        else
          m <- m + 1
      k <- m
    // char literals `'x'` / `'\n'` / `'{'` / `'}'` / `'\{'` / `'\}'` — skip so
    // a brace inside can't desync. A leading `'` NOT closing like a char literal
    // (type var `'a`, tick-ident tail `x'`) falls through harmlessly (no braces).
    elif s[k] = '\'' && k + 1 < limit && s[k + 1] = '\\' then
      // start at the `\` (k+1), not past it — so an escaped quote `'\''` skips the
      // escape via the `\\`→m+2 branch instead of reading it as the closing quote
      let mutable m = k + 1
      let mutable inCh = true
      while inCh && m < limit do
        if s[m] = '\\' then
          m <- m + 2
        elif s[m] = '\'' then
          m <- m + 1
          inCh <- false
        else
          m <- m + 1
      k <- m
    elif s[k] = '\'' && k + 2 < limit && s[k + 2] = '\'' then
      k <- k + 3
    elif s[k] = '{' then
      depth <- depth + 1
      k <- k + 1
    elif s[k] = '}' && depth = 0 then
      found <- k
    elif s[k] = '}' then
      depth <- depth - 1
      k <- k + 1
    else
      k <- k + 1
  found


/// Like `hasInvalidEscape`, but for the inner text of a regular `$"…"` interpolation:
/// `{{`/`}}` are literal braces and `{ … }` regions are interpolated expressions (code,
/// not string content), so both are skipped — only the literal text is escape-checked.
let hasInvalidEscapeInterp (inner : string) : bool =
  let len = inner.Length
  let mutable i = 0
  let mutable bad = false
  while i < len && not bad do
    if i + 1 < len && inner[i] = '{' && inner[i + 1] = '{' then
      i <- i + 2
    elif i + 1 < len && inner[i] = '}' && inner[i + 1] = '}' then
      i <- i + 2
    elif inner[i] = '{' then
      // skip the `{ … }` interpolation region
      let close = findInterpExprClose inner len (i + 1)
      i <- if close < 0 then len else close + 1
    elif inner[i] = '\\' then
      match decodeEscape inner i with
      | Some(k, _) -> i <- i + k
      | None -> bad <- true
    else
      i <- i + 1
  bad


/// The parser re-parses each `{expr}` body recursively (an inner `$"…"` recurses
/// again), so interpolation nesting = recursion depth there. Cap it or a
/// pathological `$"{$"{…}"}"` bomb is an uncatchable StackOverflow that KILLS the
/// process (the parser's own expression-depth guard can't help: each nested parse
/// constructs fresh state). 64 levels is far beyond real code. The tokenizer itself
/// doesn't recurse here — `scanInterp` skips `{expr}` regions iteratively.
let maxInterpNesting = 64

let tokenize
  (input : string)
  : Result<List<SpannedToken> * List<TokenRange * string>, string> =
  let n = input.Length

  // `///` doc comments lex as trivia, but we stash their text here so it lands on
  // the next emitted token (the declaration). Consumed + cleared by `emit`.
  let mutable pendingDoc : string option = None

  // comments scanned since the last emitted token; drained onto that token's
  // `leadingTrivia` by `emit`.
  let pendingTrivia = ResizeArray<Trivia>()

  let step (p : Pos) (c : char) : Pos =
    if c = '\n' then
      { row = p.row + 1; column = 0 }
    else
      { p with column = p.column + 1 }

  let advance (p : Pos) (i : int) (j : int) : Pos =
    let mutable q = p
    let mutable k = i
    while k < j do
      q <- step q input[k]
      k <- k + 1
    q

  let rec skipTrivia (i : int) (p : Pos) : int * Pos =
    if i >= n then
      (i, p)
    else
      let c = input[i]
      if c = ' ' || c = '\t' || c = '\r' || c = '\n' then
        skipTrivia (i + 1) (step p c)
      elif c = '/' && i + 1 < n && input[i + 1] = '/' then
        let mutable j = i
        while j < n && input[j] <> '\n' do
          j <- j + 1
        // `/// …` is a doc comment — accumulate its text for the next declaration.
        // (`////` and plain `//` are ordinary comments.)
        let isDoc =
          i + 2 < n && input[i + 2] = '/' && (i + 3 >= n || input[i + 3] <> '/')
        if isDoc then
          let text = input.Substring(i + 3, j - (i + 3)).Trim()
          pendingDoc <-
            Some(
              match pendingDoc with
              | Some d -> d + " " + text
              | None -> text
            )
        pendingTrivia.Add
          { kind = (if isDoc then DocComment else LineComment)
            text = input.Substring(i, j - i)
            range = { start = p; end_ = advance p i j } }
        skipTrivia j (advance p i j)
      elif
        c = '('
        && i + 1 < n
        && input[i + 1] = '*'
        && not (i + 2 < n && input[i + 2] = ')')
      then
        // F#-style block comment `(* ... *)`, nestable. `(*)` (with `)` right after)
        // is the multiply operator section, NOT a comment, so it's excluded here.
        let rec skipBlock (j : int) (depth : int) : int =
          if j + 1 < n && input[j] = '(' && input[j + 1] = '*' then
            skipBlock (j + 2) (depth + 1)
          elif j + 1 < n && input[j] = '*' && input[j + 1] = ')' then
            if depth = 0 then j + 2 else skipBlock (j + 2) (depth - 1)
          elif j >= n then
            j
          else
            skipBlock (j + 1) depth
        let endIdx = skipBlock (i + 2) 0
        pendingTrivia.Add
          { kind = BlockComment
            text = input.Substring(i, endIdx - i)
            range = { start = p; end_ = advance p i endIdx } }
        skipTrivia endIdx (advance p i endIdx)
      else
        (i, p)

  // longest-match operators (order matters)
  let operators : (string * Token) list =
    [ "~~~", TBitNot
      "|||", TBitOr
      "...", TDotDotDot
      "++", TPlusPlus
      "->", TArrow
      "==", TEqEq
      "!=", TNeq
      "<<", TShl
      "<=", TLte
      ">>", TShr
      ">=", TGte
      "&&", TAnd
      "||", TOr
      "|>", TPipe
      "::", TCons
      "+", TPlus
      "-", TMinus
      "*", TStar
      "/", TSlash
      "(", TLParen
      ")", TRParen
      "{", TLBrace
      "}", TRBrace
      "[", TLBracket
      "]", TRBracket
      ":", TColon
      ",", TComma
      ";", TSemicolon
      ".", TDot
      "=", TEquals
      "!", TNot
      "<", TLt
      ">", TGt
      "&", TBitAnd
      "^", TBitXor
      "%", TPercent
      "@", TAt
      "|", TBar ]

  let keyword (s : string) : Token =
    match s with
    | "let" -> TLet
    // `val x = e` is ALWAYS a top-level VALUE declaration (unlike a no-param `let`,
    // which is a value decl only inside a module and a let-expression at file top
    // level). The distinct token lets parseItems keep them apart.
    | "val" -> TVal
    | "in" -> TIn
    | "if" -> TIf
    | "elif" -> TElif
    | "then" -> TThen
    | "else" -> TElse
    // note: `def` is NOT a keyword in the interpreter dialect — it's a valid
    // identifier (e.g. `(def: Type)`), so it lexes as TIdent
    | "type" -> TType
    | "of" -> TOf
    | "match" -> TMatch
    | "with" -> TWith
    | "fun" -> TFun
    | "when" -> TWhen
    | "true" -> TTrue
    | "false" -> TFalse
    | "_" -> TUnderscore
    // `___` is the blank-name placeholder: an identifier with an empty name, used
    // by tests that exercise empty field/var/key errors.
    | "___" -> TIdent ""
    | _ -> TIdent s

  let matchesAt (s : string) (i : int) : bool =
    i + s.Length <= n && System.String.CompareOrdinal(input, i, s, 0, s.Length) = 0

  let emit (tok : Token) (i : int) (j : int) (p : Pos) : SpannedToken * int * Pos =
    let endP = advance p i j
    let dc = pendingDoc
    pendingDoc <- None
    let trivia = List.ofSeq pendingTrivia
    pendingTrivia.Clear()
    ({ token = tok
       text = input.Substring(i, j - i)
       range = { start = p; end_ = endP }
       docComment = dc
       leadingTrivia = trivia },
     j,
     endP)

  // integer suffix → token; returns (token, charsConsumedAfterDigits)
  let intToken (digits : string) (i : int) : Result<Token * int, string> =
    let parseInt64 () =
      match System.Int64.TryParse digits with
      | true, v -> Ok(TInt64 v)
      | _ when digits = "9223372036854775808" -> Ok(TInt64 System.Int64.MinValue)
      | _ -> Error $"Integer literal too large: {digits}"
    if matchesAt "uy" i then
      (match System.Byte.TryParse digits with
       | true, v -> Ok(TUInt8 v, 2)
       | _ -> Error $"out of range for UInt8: {digits}")
    elif matchesAt "us" i then
      (match System.UInt16.TryParse digits with
       | true, v -> Ok(TUInt16 v, 2)
       | _ -> Error $"out of range for UInt16: {digits}")
    elif matchesAt "ul" i then
      (match System.UInt32.TryParse digits with
       | true, v -> Ok(TUInt32 v, 2)
       | _ -> Error $"out of range for UInt32: {digits}")
    elif matchesAt "UL" i then
      (match System.UInt64.TryParse digits with
       | true, v -> Ok(TUInt64 v, 2)
       | _ -> Error $"out of range for UInt64: {digits}")
    elif matchesAt "L" i then
      parseInt64 () |> Result.map (fun t -> (t, 1))
    elif matchesAt "Q" i then
      (match System.Int128.TryParse digits with
       | true, v -> Ok(TInt128 v, 1)
       | _ when digits = "170141183460469231731687303715884105728" ->
         Ok(TInt128 System.Int128.MinValue, 1)
       | _ -> Error $"out of range for Int128: {digits}")
    elif matchesAt "Z" i then
      (match System.UInt128.TryParse digits with
       | true, v -> Ok(TUInt128 v, 1)
       | _ -> Error $"out of range for UInt128: {digits}")
    elif matchesAt "y" i then
      (match System.SByte.TryParse digits with
       | true, v -> Ok(TInt8 v, 1)
       | _ when digits = "128" -> Ok(TInt8 System.SByte.MinValue, 1)
       | _ -> Error $"out of range for Int8: {digits}")
    elif matchesAt "s" i then
      (match System.Int16.TryParse digits with
       | true, v -> Ok(TInt16 v, 1)
       | _ when digits = "32768" -> Ok(TInt16 System.Int16.MinValue, 1)
       | _ -> Error $"out of range for Int16: {digits}")
    elif matchesAt "l" i then
      (match System.Int32.TryParse digits with
       | true, v -> Ok(TInt32 v, 1)
       | _ when digits = "2147483648" -> Ok(TInt32 System.Int32.MinValue, 1)
       | _ -> Error $"out of range for Int32: {digits}")
    // explicit `I` suffix → arbitrary-precision `Int`. Bare literals are already Int,
    // so `I` is redundant, but some package files use it; accept and consume it.
    elif matchesAt "I" i then
      (match System.Numerics.BigInteger.TryParse digits with
       | true, v -> Ok(TInt v, 1)
       | _ -> Error $"Invalid integer literal: {digits}")
    // bare literal (no suffix) → arbitrary-precision `Int` (the default).
    else
      (match System.Numerics.BigInteger.TryParse digits with
       | true, v -> Ok(TInt v, 0)
       | _ -> Error $"Invalid integer literal: {digits}")

  let rec scanString (j : int) (closing : char) : Result<int, string> =
    if j >= n then Error "Unterminated string literal"
    elif input[j] = '\\' && j + 1 < n then scanString (j + 2) closing
    elif input[j] = closing then Ok(j + 1)
    else scanString (j + 1) closing

  // error-recovery: scan to the end of an unterminated lexeme — the current line or
  // EOF — so a half-typed string/char doesn't swallow the rest of the document.
  let rec scanToLineEnd (j : int) : int =
    if j >= n || input[j] = '\n' then j else scanToLineEnd (j + 1)

  // `$"text {expr} text"` (or triple-quoted `$"""…"""`). `start` points at the `$`.
  // Returns (TInterpString, end-offset). The token carries no payload — the parser
  // re-reads the token's source text and re-scans the `{expr}` bodies itself — so
  // this only needs the end offset: scan for the closing quote, skipping `\"`
  // escapes, `{{`/`}}` literal braces, and each `{expr}` region via
  // `findInterpExprClose` (the same scanner the parser uses, so the two agree on
  // where the string ends). `findInterpExprClose` is iterative and skips embedded
  // strings, so a nested `$"…"` needs no recursion and can't overflow the stack.
  let scanInterp (start : int) : Result<Token * int, string> =
    // triple-quoted `$"""…"""` (raw; a single `"` is literal text)
    let triple =
      start + 3 < n
      && input[start + 1] = '"'
      && input[start + 2] = '"'
      && input[start + 3] = '"'
    let atClose (j : int) : bool =
      if triple then
        j + 2 < n && input[j] = '"' && input[j + 1] = '"' && input[j + 2] = '"'
      else
        input[j] = '"'
    let closeLen = if triple then 3 else 1
    let rec loop (j : int) : Result<int, string> =
      if j >= n then
        Error "Unterminated interpolated string"
      elif atClose j then
        Ok(j + closeLen)
      elif input[j] = '\\' && j + 1 < n && not triple then
        loop (j + 2)
      // `{{` / `}}` are escaped literal braces, not an interpolation
      elif input[j] = '{' && j + 1 < n && input[j + 1] = '{' then
        loop (j + 2)
      elif input[j] = '}' && j + 1 < n && input[j + 1] = '}' then
        loop (j + 2)
      elif input[j] = '{' then
        match findInterpExprClose input n (j + 1) with
        | -1 -> Error "Unterminated interpolated expression"
        | closeIdx -> loop (closeIdx + 1)
      else
        loop (j + 1)
    match loop (if triple then start + 4 else start + 2) with
    | Ok endIdx -> Ok(TInterpString, endIdx)
    | Error e -> Error e

  // Lexical diagnostics collected during recovery: the tokenizer keeps lexing past a
  // malformed lexeme (so partial files still highlight), but records the failure here
  // so it isn't silently accepted.
  let diagnostics = System.Collections.Generic.List<TokenRange * string>()
  let lexDiag (range : TokenRange) (msg : string) = diagnostics.Add((range, msg))

  let rec go
    (i : int)
    (p : Pos)
    (acc : List<SpannedToken>)
    : Result<List<SpannedToken>, string> =
    let (i, p) = skipTrivia i p
    if i >= n then
      let (eof, _, _) = emit TEOF i i p
      Ok(List.rev (eof :: acc))
    else
      let c = input[i]
      // tolerate the compiler-syntax lambda arrow `=>` — lex `=` then `>` (the parser
      // rejects it semantically) rather than aborting the whole tokenization
      if c = '=' && i + 1 < n && input[i + 1] = '>' then
        let (st, i', p') = emit TEquals i (i + 1) p
        go i' p' (st :: acc)
      // backtick identifiers: ``name``
      elif c = '`' && i + 1 < n && input[i + 1] = '`' then
        let mutable j = i + 2
        while j + 1 < n
              && not (input[j] = '`' && input[j + 1] = '`')
              && input[j] <> '\n' do
          j <- j + 1
        if j + 1 < n && input[j] = '`' && input[j + 1] = '`' then
          let (st, i', p') =
            emit (TIdent(input.Substring(i + 2, j - i - 2))) i (j + 2) p
          go i' p' (st :: acc)
        else
          // unterminated ``…`` — best-effort ident to end of line
          let e = scanToLineEnd (i + 2)
          let (st, i', p') = emit (TIdent(input.Substring(i + 2, e - i - 2))) i e p
          lexDiag st.range "unterminated backtick identifier"
          go i' p' (st :: acc)
      // identifiers / keywords
      elif System.Char.IsLetter c || c = '_' then
        let mutable j = i + 1
        while j < n
              && (System.Char.IsLetterOrDigit input[j]
                  || input[j] = '_'
                  || input[j] = '\'') do
          j <- j + 1
        let (st, i', p') = emit (keyword (input.Substring(i, j - i))) i j p
        go i' p' (st :: acc)
      // numbers
      elif System.Char.IsDigit c then
        // a number token must end at a non-identifier boundary; digits/letters/
        // `_`/`'` glued to its end (`123abc`, `12l3`) are a typo, not two tokens,
        // so diagnose rather than silently split.
        let isIdentCont k =
          k < n
          && (System.Char.IsLetterOrDigit input[k]
              || input[k] = '_'
              || input[k] = '\'')
        let mutable j = i + 1
        while j < n && System.Char.IsDigit input[j] do
          j <- j + 1
        // float?
        let isFrac = j + 1 < n && input[j] = '.' && System.Char.IsDigit input[j + 1]
        let isExp = j < n && (input[j] = 'e' || input[j] = 'E')
        if isFrac || isExp then
          let mutable k = j
          if isFrac then
            k <- k + 1
            while k < n && System.Char.IsDigit input[k] do
              k <- k + 1
          if k < n && (input[k] = 'e' || input[k] = 'E') then
            k <- k + 1
            if k < n && (input[k] = '+' || input[k] = '-') then k <- k + 1
            while k < n && System.Char.IsDigit input[k] do
              k <- k + 1
          let text = input.Substring(i, k - i)
          match
            System.Double.TryParse(
              text,
              System.Globalization.NumberStyles.Float,
              System.Globalization.CultureInfo.InvariantCulture
            )
          with
          | true, v when not (isIdentCont k) ->
            let (st, i', p') = emit (TFloat v) i k p in go i' p' (st :: acc)
          | true, _ ->
            // float glued to identifier chars (`1.5abc`) — consume the run and diagnose
            let mutable e = k
            while isIdentCont e do
              e <- e + 1
            let (st, i', p') = emit (TFloat 0.0) i e p
            lexDiag st.range $"invalid number literal: {input.Substring(i, e - i)}"
            go i' p' (st :: acc)
          | _ ->
            // malformed float — emit a placeholder so it still lexes/highlights as a number
            let (st, i', p') = emit (TFloat 0.0) i k p
            lexDiag st.range $"malformed float literal: {text}"
            go i' p' (st :: acc)
        else
          let digits = input.Substring(i, j - i)
          match intToken digits j with
          | Ok(tok, suffixLen) when not (isIdentCont (j + suffixLen)) ->
            let (st, i', p') = emit tok i (j + suffixLen) p in go i' p' (st :: acc)
          | Ok(_, suffixLen) ->
            // int glued to identifier chars (`123abc`, `12l3`) — consume the run and diagnose
            let mutable e = j + suffixLen
            while isIdentCont e do
              e <- e + 1
            let (st, i', p') = emit (TInt64 0L) i e p
            lexDiag st.range $"invalid number literal: {input.Substring(i, e - i)}"
            go i' p' (st :: acc)
          | Error msg ->
            // out-of-range/malformed int — emit a placeholder so it still highlights
            let (st, i', p') = emit (TInt64 0L) i j p
            lexDiag st.range msg
            go i' p' (st :: acc)
      // triple-quoted string: """ ... """ (raw, may contain single/double quotes)
      elif c = '"' && i + 2 < n && input[i + 1] = '"' && input[i + 2] = '"' then
        let rec findTriple (j : int) : Result<int, string> =
          if
            j + 2 < n && input[j] = '"' && input[j + 1] = '"' && input[j + 2] = '"'
          then
            Ok(j + 3)
          elif j >= n then
            Error "Unterminated triple-quoted string literal"
          else
            findTriple (j + 1)
        match findTriple (i + 3) with
        | Ok j ->
          // triple-quoted is raw (no unescape), so NFC-normalize here to match
          // the regular-literal path (unescape) and keep both lowerings consistent
          let (st, i', p') =
            emit (TStringLit((input.Substring(i + 3, j - i - 6)).Normalize())) i j p
          go i' p' (st :: acc)
        | Error _ ->
          // unterminated `"""…` — take the rest as the string content
          let (st, i', p') =
            emit (TStringLit((input.Substring(i + 3, n - i - 3)).Normalize())) i n p
          lexDiag st.range "unterminated triple-quoted string literal"
          go i' p' (st :: acc)
      // strings / chars (escape processing deferred — raw content)
      elif c = '"' then
        match scanString (i + 1) '"' with
        | Ok j ->
          let (st, i', p') =
            emit (TStringLit(unescape (input.Substring(i + 1, j - i - 2)))) i j p in
          go i' p' (st :: acc)
        | Error _ ->
          // unterminated `"…` — take to end of line (best-effort for mid-typing)
          let j = scanToLineEnd (i + 1)
          let (st, i', p') =
            emit (TStringLit(unescape (input.Substring(i + 1, j - i - 1)))) i j p
          lexDiag st.range "unterminated string literal"
          go i' p' (st :: acc)
      elif c = '\'' then
        // A leading `'` is a type variable (`'a`) when we're in a type context
        // (decided by the previous token), and a char literal otherwise. A type
        // var lexes to the bare name as a TIdent.
        let inTypeContext =
          match acc with
          | prev :: _ ->
            match prev.token with
            | TLParen
            | TStar
            | TLt
            | TComma
            | TColon
            | TArrow
            | TEquals
            | TOf -> true
            | _ -> false
          | [] -> false
        if
          inTypeContext
          && i + 1 < n
          && (System.Char.IsLetter input[i + 1] || input[i + 1] = '_')
        then
          let mutable j = i + 1
          while j < n && (System.Char.IsLetterOrDigit input[j] || input[j] = '_') do
            j <- j + 1
          // a closing quote right after the name means it was a char literal
          if j < n && input[j] = '\'' then
            match scanString (i + 1) '\'' with
            | Ok j2 ->
              let (st, i', p') =
                emit (TCharLit(unescape (input.Substring(i + 1, j2 - i - 2)))) i j2 p
              go i' p' (st :: acc)
            | Error _ ->
              let j2 = scanToLineEnd (i + 1)
              let (st, i', p') =
                emit (TCharLit(unescape (input.Substring(i + 1, j2 - i - 1)))) i j2 p
              lexDiag st.range "unterminated char literal"
              go i' p' (st :: acc)
          else
            let (st, i', p') = emit (TIdent(input.Substring(i + 1, j - i - 1))) i j p
            go i' p' (st :: acc)
        else if
          // char literal: '<one char>' — read exactly one char (or an escape)
          // then the closing quote, so `'''` is the apostrophe char, not empty.
          i + 1 < n && input[i + 1] = '\\'
        then
          // escaped char: scan to the closing quote so multi-char escapes
          // (`'\x41'`, `'é'`, `'\U0001F600'`) decode, not just `'\n'`.
          match scanString (i + 1) '\'' with
          | Ok j2 ->
            let (st, i', p') =
              emit (TCharLit(unescape (input.Substring(i + 1, j2 - i - 2)))) i j2 p
            go i' p' (st :: acc)
          | Error _ ->
            let j2 = scanToLineEnd (i + 1)
            let (st, i', p') =
              emit (TCharLit(unescape (input.Substring(i + 1, j2 - i - 1)))) i j2 p
            lexDiag st.range "unterminated char literal"
            go i' p' (st :: acc)
        else
          // unescaped char: one extended grapheme cluster (which may span several
          // UTF-16 code units — a surrogate-pair emoji `'😀'` or a base+combining
          // sequence) then the closing quote. `'''` is the apostrophe char: the
          // grapheme is the inner `'`, and the third quote closes it.
          let contentEnd =
            if i + 1 < n then
              i
              + 1
              + (System.Globalization.StringInfo.GetNextTextElement(input, i + 1))
                .Length
            else
              i + 1
          if contentEnd < n && input[contentEnd] = '\'' then
            let (st, i', p') =
              emit
                (TCharLit(unescape (input.Substring(i + 1, contentEnd - i - 1))))
                i
                (contentEnd + 1)
                p
            go i' p' (st :: acc)
          else
            // unterminated/half-typed char — best-effort to the grapheme end
            let endIdx = min n (max (i + 1) contentEnd)
            let (st, i', p') =
              emit
                (TCharLit(unescape (input.Substring(i + 1, endIdx - i - 1))))
                i
                endIdx
                p
            lexDiag st.range "unterminated char literal"
            go i' p' (st :: acc)
      elif c = '$' && i + 1 < n && input[i + 1] = '"' then
        match scanInterp i with
        | Ok(tok, j) -> let (st, i', p') = emit tok i j p in go i' p' (st :: acc)
        | Error _ ->
          // unterminated `$"…` — take to end of line
          let j = scanToLineEnd (i + 2)
          let (st, i', p') = emit TInterpString i j p
          lexDiag st.range "unterminated interpolated string"
          go i' p' (st :: acc)
      else
        match operators |> List.tryFind (fun (s, _) -> matchesAt s i) with
        | Some(s, tok) ->
          let (st, i', p') = emit tok i (i + s.Length) p in go i' p' (st :: acc)
        | None ->
          // unknown character — record it, skip, and keep lexing (don't abort the file)
          lexDiag
            { start = p; end_ = advance p i (i + 1) }
            $"unexpected character: '{input[i]}'"
          go (i + 1) (advance p i (i + 1)) acc

  match go 0 { row = 0; column = 0 } [] with
  | Ok toks -> Ok(toks, List.ofSeq diagnostics)
  | Error e -> Error e
