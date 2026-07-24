# public/conformance/

This directory holds the **conformance-gate report** the site's Ladder / Surveyor
pages (and the shared status panel) render.

`report.generated.json` is **generated per deploy** by
[`.github/workflows/conformance.yml`](../../.github/workflows/conformance.yml) – it
runs the cross-implementation wire-format conformance gate + the SSR-parity suite
over the site's exhibit trees and publishes the result here. It is **gitignored**
(see the repo `.gitignore`): a committed copy would be stale by definition.

When the file is **absent or stale**, the panel component renders **grey** and says
so – it never fakes a green. That honest-staleness behaviour is the whole point of
publishing the real artefact rather than a hand-written badge.

Report shape:

```json
{
  "generated": "2026-07-08T12:00:00Z",
  "commit": "<sha>",
  "passed": 0,
  "failed": 0,
  "total": 0,
  "ok": true
}
```
