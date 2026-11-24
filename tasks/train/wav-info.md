Build a command-line tool that reads a WAV file and prints what's in it: the audio format (PCM or not), number of channels, sample rate, bits per sample, number of sample frames, duration in seconds to 3 decimal places, and the peak absolute sample value per channel. It must handle 8-bit and 16-bit PCM, mono and stereo, and skip chunks it doesn't know (such as `LIST`) before the `data` chunk. A file that isn't a RIFF/WAVE file, or is cut short, prints a clear error to stderr and exits 1.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** small
- **Kind:** binary format parsing, CLI tool
- **Areas:** bytes, integers (little-endian), files, permissions, exit codes
- **Network sites:** none
- **Seed:** none

## Acceptance checks

Generate the inputs with Python's `wave` module (or `sox`).

1. A 16-bit stereo 44100 Hz file of 22050 frames: format PCM, 2 channels, 44100 Hz, 16 bits, 22050 frames, duration `0.500`.
2. Peaks match the generated data: put a sample of -32768 in the left channel and 1000 as the right channel's largest; the tool prints 32768 and 1000.
3. An 8-bit mono 8000 Hz file (unsigned samples, silence = 128) prints peak 0 for a silent file.
4. A file with a `LIST` chunk between `fmt ` and `data` gives the same output as without it.
5. A text file and a WAV file truncated to 30 bytes each print an error and exit 1, with no crash or stack trace.
