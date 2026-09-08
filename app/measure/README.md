# Render-latency measurement harness (Phase 1628)

A real-browser measurement of how long it takes the F# `Fuaran.UI.Renderer` to put a
wire-format tree on the page, over a fixed corpus that varies width and depth. It emits the
shared perf-baseline artefact shape, so the performance release gate can read it.

Phase 202 built a harness of this kind in the TypeScript shell; the 2026-06-25 flip to the
F#/Fable app retired the shell and took the harness with it. **What was carried forward is the
corpus design, not the code** — the width axis, and the `DeepNested` spine that the depth axis
needs. What was not carried forward is the thing that made the original inert: it never took a
reading. Its own evidence document recorded the real-browser run as deferred and its decision
table was four rows of `pending`. This one captures.

## The pieces

| File                           | Role                                                                                          | Needs a browser? |
| ------------------------------ | --------------------------------------------------------------------------------------------- | ---------------- |
| `Corpus.fs`                    | the fixed corpus as canonical wire JSON — Small / Medium / Large (width) + DeepNested (depth) | no               |
| `Timing.fs`                    | the clock, the aggregation, the metric ids, the browser-identity reader                       | no               |
| `Baseline.fs`                  | the perf-baseline artefact shape and its emitter                                              | no               |
| `Capture.fs`                   | the driver: decode once, render many, force layout, stamp the host                            | **yes**          |
| `render-latency-baseline.json` | the committed capture (see the run record below)                                              | —                |
| `../../tests/measure/`         | the Playwright spec + config that opens the page and runs the capture                         | **yes**          |

The pure/browser split is deliberate: everything except `Capture.fs` runs in the ordinary unit
suite (`test/measure.test.ts`), so most of the harness is checked on every push without a
browser at all.

## Running it

```bash
pnpm run measure                         # build the measure page, then capture
MEASURE_OUT=app/measure/render-latency-baseline.json pnpm run measure   # …and rewrite the baseline
MEASURE_REPEATS=25 MEASURE_WARMUPS=2 pnpm run measure                   # override the sample counts
```

`pnpm run build:measure` alone emits `dist-measure/measure.html`, which can be served and driven
by hand from a browser console:

```js
JSON.parse(window.__fuaranMeasure.capture(JSON.stringify({ repeats: 25 })));
```

**The measure page is not part of the site.** It is emitted only under `VITE_MEASURE=1`, into its
own `dist-measure/`, and no deploy path sets that flag — so the shipped artifact is byte-for-byte
what it was before this harness existed.

## What the two metrics mean

| Metric                   | From render start to…                                                              |
| ------------------------ | ---------------------------------------------------------------------------------- |
| `render.ttfp.<Label>.ms` | the tree is **committed to the DOM** — the first instant anything could be painted |
| `render.full.<Label>.ms` | committed **and laid out** — the same commit plus a forced style + layout pass     |

`full >= ttfp` by construction; the gap is the browser's own layout cost. This is a deliberate
reframing of the Phase 202 pair, which meant "first committed subtree" versus "whole tree
committed" and existed to decide whether _progressive_ render beat whole-tree render. That
question is settled — Phase 1147 measured it and found no crossover at any tree size — and this
renderer commits whole trees, so under the old definitions the two marks would be the same
number and one of the gate's two axes would be decoration.

Three method notes that are load-bearing rather than incidental, and the code carries the long
form of each:

- **One sample is a batch of 40 renders, divided.** `performance.now()` outside a
  cross-origin-isolated context is clamped to 100 µs in Chromium, and the smallest corpus tree
  renders in about one tick. The first version of this harness timed single renders and returned
  a whole corpus of exact multiples of 0.100 ms.
- **The baseline records the fastest batch, not the median.** Timing noise in a browser is
  one-sided: an interruption can only add time. Measured over the same configuration, the
  run-to-run spread was 36.1% recording the p50 and 25.0% recording the minimum.
- **Decoding is outside the clock; the tree projection is inside it.** The decoder has its own
  axes; projecting a node tree into elements is the Fuaran-specific half of a render and is where
  a renderer regression lands.

## The corpus, and why `DeepNested` stops where it does

| Label                  | Shape                                                                   |       Nodes | Levels |
| ---------------------- | ----------------------------------------------------------------------- | ----------: | -----: |
| Small / Medium / Large | a flat surface of 4 / 24 / 96 alternating heading and markdown children | 5 / 25 / 97 |      2 |
| DeepNested             | a right-leaning spine of 20 nested boxes, each with a heading label     |          41 |     21 |

The spine's depth is a deliberate number. The canonical wire decoder enforces
`WireLimits.MaxDepth = 24` and refuses a deeper document outright, so a corpus that nests past it
measures nothing — it fails to decode. Phase 1147 met the same bound from the other side and
stopped its depth row at 20 for the same reason; the Phase 202 corpus, written before the limit
existed, nested 32 deep and today would not decode at all.

## The run-to-run spread, and what it means for the budget

**Ten captures of unchanged code**, back to back, on the reference workstation, in the committed
configuration (minimum of 25 batches of 40 renders):

> `Intel(R) Core(TM) Ultra 9 386H, 16lp` · Windows 11 (10.0.26200) ·
> HeadlessChrome 151.0.7922.34 · a developer workstation, not a quiesced rig

| Metric                      | worst pair over the ten runs |
| --------------------------- | ---------------------------: |
| `render.ttfp.Small.ms`      |                        25.0% |
| `render.full.Small.ms`      |                        25.6% |
| `render.ttfp.Medium.ms`     |                        19.2% |
| `render.full.Medium.ms`     |                        22.1% |
| `render.ttfp.Large.ms`      |                        11.4% |
| `render.full.Large.ms`      |                        14.0% |
| `render.ttfp.DeepNested.ms` |                    **42.7%** |
| `render.full.DeepNested.ms` |                        32.5% |

**So the two `render.*` latency axes are deliberately UNBUDGETED — measured, reported, never
enforced.** A budget has to be wider than the instrument's own spread or it fires on noise, and
twice 42.7% is a threshold that only catches a regression of roughly 1.9×. A number nobody can
trust is worse than no number, so the gate reports these metrics with their deltas and fails on
neither. The reasoning, and the same table, is recorded beside the budget table the gate reads.

Two details of that measurement are worth keeping, because they are the reason the conclusion is
not "measure more carefully":

- **The `DeepNested` samples are bimodal**, clustering at 0.24–0.26 ms and 0.31–0.34 ms with
  nothing in between. That is a property of the machine — core scheduling and clock behaviour —
  not of the renderer, and no amount of averaging inside a run moves it.
- **A five-run estimate of the spread said 25%; ten runs said 42.7%.** The spread estimate is
  itself unstable, which is a stronger argument against a tight budget than any single figure.

Note also that the metric unit is `ms`, which the gate treats as host-sensitive: it declines to
compare a wall-time metric across artefacts whose hosts differ, and `runtime.cpu` here names the
machine **and** the browser engine and version. So on any hosted runner these axes are withheld
from comparison regardless — a browser upgrade alone changes the host string.

## What CI does with it

`ci.yml` runs the capture on every push and pull request. It is a **probe check, not a latency
gate**: it asserts that every declared metric came back finite and positive, that full-render is
never below time-to-commit, and that the numbers scale with tree size — 97 nodes must cost
materially more than 5, or the harness is timing something other than the tree it was handed. It
asserts no threshold in milliseconds, because a wall-clock bound on a shared runner would fail
for reasons that have nothing to do with the code.
