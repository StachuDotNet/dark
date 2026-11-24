# strutil

Five small text helpers. Nothing tests them yet.

- **slugify(s)** lowercases `s` and keeps ASCII letters and digits. Every run of other characters becomes one `-`, and there is never a `-` at the start or the end. `slugify("Hello, World!")` is `"hello-world"`; `slugify("  --  ")` is `""`.
- **truncate(s, n)** returns `s` unchanged when it is at most `n` characters long. Otherwise it returns the first `n - 1` characters followed by `…` (one character), so the result is exactly `n` characters. `truncate("abcdef", 4)` is `"abc…"`; `truncate("abcd", 4)` is `"abcd"`. `n` is at least 1.
- **wrap(s, width)** splits `s` into words on whitespace and packs them greedily into lines of at most `width` characters, joined by single spaces. A word longer than `width` goes on a line by itself, unbroken. Returns the list of lines; an empty or all-space `s` gives an empty list.
- **titleCase(s)** splits `s` on whitespace, lowercases every word, then capitalises its first letter, except `a`, `an`, `and`, `of` and `the`, which stay lowercase unless they are the first word. Words are joined by single spaces. `"the LORD of the rings"` becomes `"The Lord of the Rings"`.
- **countWords(s)** is the number of whitespace-separated words. `countWords("  two   words ")` is 2.

## Where the code is

The package module `Bench.Strutil` in this Dark instance's package store. `strutil.dark` is a copy of the source it was loaded from.
