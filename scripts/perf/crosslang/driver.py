#!/usr/bin/env python3
"""Time the programs in this directory side by side, in Dark, python, node and bash.

Run from /home/dark/app inside the container, with DARK_EXE pointing at a published AOT CLI:

    DARK_EXE=clis/darklang-alpha-<sha>-linux-x64 python3 scripts/perf/crosslang/driver.py

- Round robin, not language by language, so drift on the box doesn't land on whichever went last.
- Read the minimum: a run can be slowed by the machine and never sped up by it.
- Every program prints `result=<v>`, and the driver exits 1 unless every language agrees, so it
  can't time a program that crashed.
"""
import argparse
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
DARK_EXE = os.environ.get("DARK_EXE")

SCENARIOS = ["hello", "steady", "lists", "strings", "dict", "records", "recursion-deep",
             "recursion-fib", "arith", "sort", "jsonrt", "fileread"]
LANGS = ["dark", "python", "node", "bash"]
EXT = {"dark": "dark", "python": "py", "node": "js", "bash": "sh"}

# Where bash has no honest equivalent.
EXCLUDED = {
    ("recursion-deep", "bash"): "bash segfaults at depth 5000 (it manages 4000)",
    ("records", "bash"): "bash has no record or struct type",
    ("jsonrt", "bash"): "bash has no JSON; jq would be a different program",
}

FIXTURE = "rundir/crosslang-lines.txt"


def command(scenario, lang):
    p = os.path.join(HERE, scenario, f"workload.{EXT[lang]}")
    if lang == "dark":
        return [DARK_EXE, "--no-trace", "run", p]
    return {"python": ["python3", p], "node": ["node", p], "bash": ["bash", p]}[lang]


def run_once(scenario, lang):
    # The python programs `import harness`, which lives one directory up from them.
    env = dict(os.environ, PYTHONPATH=HERE)
    t0 = time.perf_counter()
    p = subprocess.run(command(scenario, lang), capture_output=True, text=True, env=env)
    ms = (time.perf_counter() - t0) * 1000
    results = [l[len("result="):] for l in p.stdout.splitlines() if l.startswith("result=")]
    return ms, (results[-1] if p.returncode == 0 and results else None)


def setup():
    """Write the fileread fixture and let the Dark binary read it.

    The instance policy denies file reads by default, and without the grant the Dark row exits 1
    in 40 ms, which looks like a very fast pass.
    """
    with open(FIXTURE, "w") as f:
        for i in range(1, 100001):
            level = "ERROR" if i % 7 == 0 else "INFO"
            f.write(f"2026-10-13T00:00:00 line {i} LEVEL={level} payload=abcdefgh\n")
    path = os.path.join(os.environ["DARK_CONFIG_RUNDIR"], "crosslang-lines.txt")
    subprocess.run([DARK_EXE, "permissions", "allow", "file", "read", path], capture_output=True)


def load1():
    with open("/proc/loadavg") as f:
        return float(f.read().split()[0])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rounds", type=int, default=7)
    rounds = ap.parse_args().rounds
    if not DARK_EXE:
        sys.exit("driver: set DARK_EXE to the published CLI")
    setup()

    pairs = [(s, l) for s in SCENARIOS for l in LANGS if (s, l) not in EXCLUDED]
    # One unrecorded pass: the first Dark run may seed its store, and every page cache is cold.
    for s, l in pairs:
        run_once(s, l)

    times = {pair: [] for pair in pairs}
    results = {pair: set() for pair in pairs}
    loads = [load1()]
    for r in range(rounds):
        for pair in pairs:
            ms, result = run_once(*pair)
            times[pair].append(ms)
            results[pair].add(result)
        loads.append(load1())
        print(f"round {r + 1}/{rounds}, load1 {loads[-1]}", file=sys.stderr)

    print(f"{'scenario':<16}" + "".join(f"{l:>22}" for l in LANGS))
    for s in SCENARIOS:
        row = f"{s:<16}"
        for l in LANGS:
            t = times.get((s, l))
            row += f"{'n/a' if t is None else '%.1f / %.1f' % (min(t), max(t)):>22}"
        print(row)
    print(f"\nmin / max ms over {rounds} rounds, load1 {min(loads):.2f}-{max(loads):.2f}")

    bad = []
    for s in SCENARIOS:
        seen = {l: results[(s, l)] for l in LANGS if (s, l) in results}
        if None in set().union(*seen.values()) or len(set().union(*seen.values())) != 1:
            bad.append((s, seen))
    for s, seen in bad:
        print(f"DISAGREE or FAILED: {s} {seen}")
    if bad:
        sys.exit(1)


if __name__ == "__main__":
    main()
