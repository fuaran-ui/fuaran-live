# The published evaluation-results feed

`results.json` in this folder is the **live feed** the Evaluation page reads. The
page fetches it in the browser and polls every 60 seconds, so publishing a new
`results.json` (by redeploying the site with an updated file, or by pointing the
feed at a public URL) updates the dashboard with no code change.

The dashboard renders **only what the feed reports**. Until a real run publishes,
the file carries `"status": "pending"` and the page shows an honest "awaiting the
first run" state – never placeholder figures.

## Overriding the feed URL (no rebuild)

The page reads `localStorage["fuaran-eval-feed"]` first, falling back to
`./eval/results.json` (same origin). Set that key to point the live feed at a
published public URL (e.g. a raw file or a static host) without rebuilding the
site – mirroring the receiver-origin override pattern used elsewhere on the site.

## Schema

### Pending (the seed / between runs)

```json
{
  "status": "pending",
  "message": "…optional, shown in the awaiting state…",
  "sourceUrl": "https://…"
}
```

### Published (a real run)

```json
{
  "status": "published",
  "generatedAt": "2026-07-15T09:00:00Z",
  "commit": "abc1234",
  "suiteVersion": "1.0.0",
  "sourceUrl": "https://…link to the public prompt set + scoring methodology…",
  "summary": {
    "totalPrompts": 200,
    "passed": 184,
    "passRate": 0.92,
    "providersEvaluated": 3,
    "adversarialPassRate": 0.81
  },
  "providers": [
    {
      "name": "Provider display name",
      "model": "model identifier (optional)",
      "passed": 190,
      "total": 200,
      "passRate": 0.95,
      "meanScore": 4.6
    }
  ],
  "categories": [{ "name": "Layout & structure", "passed": 48, "total": 50, "passRate": 0.96 }]
}
```

### `sessionEconomics` (optional — Tier-B multi-turn companion)

Present only when the publish carried a multi-turn session window. Multi-turn cells
publish **here and only here** — `parsePassRate` and every other primary metric remain
single-shot claims (their multi-turn cells are routed out by the publisher).

```json
{
  "sessionEconomics": {
    "cohort": "618-stage2-20260807",
    "excludedTasks": [{ "id": "…", "reason": "…" }],
    "arms": [
      {
        "condition": "fuaran",
        "modelPin": "claude-opus-4-8@low",
        "cells": 27,
        "usdAtTurn3": 0.1566,
        "cellsAtTurn3": 27,
        "usdAtTurn5": 0.3339,
        "cellsAtTurn5": 6,
        "identityMean": 0.986,
        "identityCells": 24,
        "pricingRetrieved": "2026-07-30"
      }
    ],
    "note": "…self-describing measurement note…"
  }
}
```

- `usdAtTurnK` is the mean cumulative **billed** USD through turn K over the sessions
  that reached turn K — `cellsAtTurnK` is that denominator, and it rides beside every
  figure deliberately. A missing `usdAtTurnK` means the model is unpriced, never zero.
- `identityMean` is the fraction of prompt-named element ids present in both the first
  and final emission, mean over measured cells only (`identityCells`).
- `excludedTasks` disclose section-level exclusions with reasons.

### `repairRecovery` (optional — Tier-C repair-under-error companion)

Present only when the publish carried a repair-under-error window. Tier-C cells never
enter the primary metrics.

```json
{
  "repairRecovery": {
    "cohort": "tierc-cohort1-20260810",
    "rubricVersion": "v2.1",
    "conditions": [{ "label": "fuaran", "fired": 83, "recovered": 60 }],
    "families": [{ "label": "Moonshot Kimi", "fired": 40, "recovered": 39 }],
    "fuaranChannels": [
      { "channel": "structured", "fired": 26, "recovered": 16, "meanRecoveryTokens": 5701 }
    ],
    "note": "…the design-framing note, carried on the wire…"
  }
}
```

**Methodology.** Each Tier-C task's prompt DELIBERATELY instructs an invalid form (a
text string in a numeric-only slot, an arithmetic expression as a fraction, an
invented enum value), inducing a first-emission failure. The harness then measures
recovery: each condition is fed its own natural error signal — the typed decoder's
structured rejection for Fuaran, the compile/render gate for JSX, otherwise the
judge's prose — for up to three attempts. `fired` counts cells where the induced
failure actually occurred (models may decline the wrong instruction and emit correctly
first-shot — a measured behaviour); `recovered` counts cells that reached a passing
state within the cap. `fuaranChannels` splits the Fuaran condition by which signal
drove the last recovery feedback.

**Read the note before the numbers.** These rates are NOT a cross-condition ranking:
the instructed invalid form stays LEGAL in the markup conditions — a wrong value in
JSX renders plausibly and can pass first-shot — while only the typed-tree condition
catches it, so only there does recovery require overriding the instruction. What the
section measures is how models resolve a conflict between an insistent instruction
and authoritative error feedback, plus per-family induced-failure and recovery rates.
Judging uses the deliberate-bait rubric framing (`rubricVersion` v2.1): the recovered
state is graded against the criteria only, never penalised for replacing the
instructed invalid form with a valid equivalent.

### Field notes

- **`passRate`** / **`adversarialPassRate`** are fractions in `0..1` (the page
  formats them as percentages). **`meanScore`** is on the run's own scale (shown
  as `mean N.N / 5`).
- **`providers`** and **`categories`** are optional arrays – the page renders a
  section only when its array is non-empty, so a summary-only feed is valid.
- All fields are read tolerantly: a missing field reads as empty / `0`, never an
  error. A malformed file (unparseable JSON, or a `404`) drops the page to its
  grey "feed unavailable" state rather than showing anything false.
- **`sourceUrl`** should link to the public source of the numbers so a reader can
  reproduce them; the page surfaces it as "Public source & methodology →".
