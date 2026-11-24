Build a command-line tool that converts a JSON array of objects into CSV. The header is the union of all keys, in the order they are first seen. Nested objects are flattened with dotted keys (`{"a": {"b": 1}}` gives a column `a.b`). Arrays are written as their JSON text. A missing key gives an empty cell; `null` also gives an empty cell. Cells containing a comma, a quote or a newline are quoted, with quotes doubled. Input that isn't a JSON array of objects gives an error and exit status 1. It reads a file or standard input.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** data processing
- **Areas:** JSON, dicts, lists, strings, files
- **Network sites:** none
- **Seed:** none

## Acceptance checks

1. `[{"id":1,"name":"Ann"},{"id":2,"city":"Oslo"}]` gives a header `id,name,city` and rows `1,Ann,` and `2,,Oslo`.
2. `[{"a":{"b":1,"c":{"d":true}}}]` gives header `a.b,a.c.d` and row `1,true`.
3. `[{"t":"x, \"y\""}]` gives the cell `"x, ""y"""`.
4. `[{"tags":["a","b"]}]` gives a cell holding the JSON text of the array (`["a","b"]` or `["a", "b"]`), quoted because it contains a comma.
5. `[{"n":null,"f":1.5}]` gives `,1.5`.
6. `{"a":1}`, `[1,2]` and `not json` each give an error and exit 1.
7. The output parses back with Python's `csv` module into the expected rows.
