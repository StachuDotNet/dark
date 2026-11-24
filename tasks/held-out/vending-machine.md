Build a vending machine simulator that reads commands from standard input, one per line, and prints a response to each. The machine has slots (A1, A2, ...) with a product, a price and a count, loaded from a JSON file given as an argument. Commands: `insert <coin>` (accepts 5, 10, 25, 100 cents; other coins are returned), `select <slot>`, `cancel`, `status`. The machine is a state machine: idle, collecting money, and dispensing. Selecting with enough money dispenses the product and returns change using the fewest coins from the machine's own coin stock; if exact change can't be made, it refuses the sale and keeps the money in the session. Selecting an empty slot or an unknown slot says so. `cancel` returns the inserted coins. A `service` command reports the coin stock and the totals sold. Keep the states and transitions explicit in the code, with tests for the transitions.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** state machine, simulation, stdin-driven CLI
- **Areas:** records, enums/sum types, lists, JSON, stdin, tests
- **Network sites:** none
- **Seed:** none

## Acceptance checks

Use slots `A1` cola 125 cents count 2, `A2` chips 65 cents count 0, and a coin stock of five each of 5, 10 and 25.

1. `insert 100`, `insert 25`, `select A1` dispenses cola with no change.
2. `insert 100`, `insert 100`, `select A1` dispenses cola and returns 75 as three 25s.
3. `insert 3` returns the coin; `select A2` says empty; `select Z9` says unknown slot.
4. `insert 100`, `cancel` returns 100.
5. A sale that needs change the stock can't make is refused, the money stays, and `cancel` returns it.
6. After both colas, `select A1` says empty; `service` shows 250 sold and the updated coin stock.
7. States and transitions are explicit, and the tests run with one command and pass.
