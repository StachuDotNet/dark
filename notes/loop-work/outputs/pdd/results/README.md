# results/ — PDD bench results (nested)

This directory is the **PDD-specific results home**, nested inside the
overnight-wave deliverable. The whole `outputs/` tree is "results of this
wave" in the broad sense — the reviewable output of the run. This `results/`
directory is the narrower thing: the place where the PDD bench's *raw data and
per-sweep reports* live, and nothing else.

Nothing in here is real yet. The actual numbers get generated later by Stachu
running the bench; this README fixes the *shape* those results take so the data
lands in a known structure on day one.

## What lives here

- **Raw bench data.** The append-only row-per-run record the harness writes —
  one row per `(project, language, attempt)` — plus the per-run artifacts a
  sweep emits (transcripts, per-run telemetry, the agent's own end-of-run
  summary).
- **One report per sweep.** Each sweep produces exactly one report. Either a
  single report file named for the sweep, or a directory per sweep containing a
  single **FINAL** summary `.md`. Pick one convention and keep to it.

## The hard rule: no intermediary summaries

**Never keep per-iteration / per-attempt summary files.** A sweep produces raw
data plus one final report — full stop. Intermediate "progress so far" summaries
are dead weight: they go stale the moment the next attempt lands, and they
invite a reader to trust a half-finished number. If a sweep is re-run, its
report is overwritten, not appended to. The raw data is the durable record; the
report is a single current view over it.

## Layout (when data exists)

```
results/
  <raw row-per-run record>        # append-only; the durable cross-sweep history
  <sweep-id>/                     # one dir per sweep
    FINAL.md                      # the single report for that sweep
    <per-run artifacts>           # transcripts, telemetry, per-run summaries
```

Or, flat:

```
results/
  <raw row-per-run record>
  <sweep-id>.md                   # the single report for that sweep
```

Either works. What's not allowed is more than one summary `.md` per sweep.

## What a sweep report says

A report is a view over that sweep's rows. It carries the headline outcome per
project and language, the supporting and diagnostic signals, and a comparison
against the prior sweep where one exists. Expected-to-fail projects are rendered
distinctly so a reader doesn't read an intended failure as a regression. The
report states results; it does not reflect on the process and does not describe
the rest of the system.

## What does NOT live here

- **Specs** (goal + acceptance criteria per project) live in `projects/`. Do not
  copy them here — a report references a project by name, it does not restate the
  spec.
- **Process learnings** (how the loop / the bench-building itself is going) live
  in `meta-reflections/`.
- **Design decisions** live in `design/`.

The reporting-shape conventions above were lifted out of the older bench notes;
everything in those notes that was a process reflection went to
`meta-reflections/` instead, and the per-project spec content stayed in
`projects/`. This file keeps only the results-reporting shape.
