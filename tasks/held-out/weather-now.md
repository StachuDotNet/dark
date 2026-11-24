Build a command-line tool that prints the current temperature and wind speed for a city named on the command line, using the free Open-Meteo APIs (geocoding at https://geocoding-api.open-meteo.com and forecasts at https://api.open-meteo.com; no API key is needed). If the city isn't found, say so and exit 1.

Test the program and explain what you tested and whether it worked.

<!-- Everything below this line is for the people running the benchmark. Send the agent only the text above it. -->

## About this task

- **Size:** tiny
- **Kind:** HTTP client against a public API
- **Areas:** HTTP client, JSON, strings, permissions
- **Network sites:** `geocoding-api.open-meteo.com`, `api.open-meteo.com`
- **Seed:** none

## Acceptance checks

1. `<tool> Berlin` prints a temperature in °C and a wind speed with units, and names the place it found.
2. The temperature is within 1 °C of `curl 'https://api.open-meteo.com/v1/forecast?latitude=52.52&longitude=13.41&current=temperature_2m'` at the same time.
3. `<tool> "San Francisco"` works (a name with a space).
4. `<tool> Qwxzzy` says the city wasn't found and exits 1.
