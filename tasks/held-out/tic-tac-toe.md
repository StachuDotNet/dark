Build a command-line tic-tac-toe game where a person plays against the computer. The board is shown after every move, with squares numbered 1 to 9. The person types a square number to move; occupied squares and invalid input are rejected with a message. The computer never loses. `--first computer` lets the computer move first. At the end the game announces the result and exits 0. It reads moves from standard input, so it can be played through a pipe.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** medium
- **Kind:** small game
- **Areas:** lists, strings; also search (minimax) and stdin
- **Network sites:** none
- **Seed:** none

## Acceptance checks

1. A piped game where the person plays 1, 2, 3, ... (skipping squares as needed) ends with a result and exit 0.
2. Typing `5` twice, `0`, `10` and `x` are each rejected without ending the game.
3. Over 50 games with random legal moves from the person (a small driver script feeds them), the computer never loses, playing first and second.
4. With the person playing 5 then a corner, the computer still at least draws.
5. The board is printed after every move, and the final result line says who won or that it was a draw.
