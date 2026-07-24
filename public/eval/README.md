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
