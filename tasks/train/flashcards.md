Build an interactive command-line flashcard trainer. Cards are in a text file, one per line, as `question<TAB>answer`. `study FILE` asks questions one at a time, reads the user's answer from standard input, says whether it was right (ignoring case and surrounding spaces) and shows the right answer when it wasn't. It uses the Leitner system: each card is in box 1 to 5; a right answer moves it up one box, a wrong one sends it back to box 1, and a session asks the cards from the lowest boxes first, at most 10 per session. Typing `:skip` skips a card and `:quit` ends the session early. At the end it prints how many were right out of how many asked. Boxes are saved next to the card file, so the next session continues from them. `stats FILE` prints how many cards are in each box. A `--seed N` option makes the order within a box repeatable.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** small to medium
- **Kind:** interactive CLI, persistence
- **Areas:** stdin, strings, lists, sorting, files, randomness
- **Network sites:** none
- **Seed:** none

## Acceptance checks

Use a deck of 12 cards.

1. A first `study` session with answers piped in asks exactly 10 cards and prints right/asked at the end.
2. A right answer with different case and extra spaces counts as right; a wrong answer shows the right one.
3. After that session, `stats` shows the right-answered cards in box 2 and the rest in box 1, 12 cards in all.
4. A second session asks the two never-asked and the box-1 cards before any box-2 card.
5. `:skip` leaves a card's box unchanged; `:quit` ends early and still saves and prints the score.
6. Input ending mid-session (EOF) ends it the same way as `:quit`, with no crash.
7. The same `--seed` on the same state asks the cards in the same order.
