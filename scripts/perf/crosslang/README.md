# crosslang

The same small programs in Dark, python, node and bash, timed side by side, for scale. Nothing in
CI runs it.

One directory per scenario, a `workload.<ext>` per language inside it. Each prints `result=<v>`.
`steady` is the shape of `workloads/steady.dark`, the gated workload. Bash sits out three
scenarios; `driver.py` says why next to each.

Inside the container, against a published AOT binary:

    scripts/run-local-exec export-seed rundir/seed.db
    scripts/build/build-release-cli-exes.sh --mode=aot
    DARK_EXE=clis/darklang-alpha-<sha>-linux-x64 python3 scripts/perf/crosslang/driver.py

Each cell is min / max ms over the rounds, whole process, startup included; read the min. The
driver exits 1 if any language crashed or disagreed on a result.
