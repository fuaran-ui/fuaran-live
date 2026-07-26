# public/conformance/

This directory holds the **conformance-gate report** the showcase's shared status
panel renders (`app/showcase/Conformance.fs`).

`report.generated.json` is **generated per deploy** by
[`scripts/conformance-report.mjs`](../../scripts/conformance-report.mjs), run as
`pnpm run conformance:report`. It executes the projection-conformance gate (every
Node fixture in the workspace `wire-format-fixtures/` corpus, projected to
`@fuaran-ui/ui` source, executed, and byte-compared on re-encode) and distils the
counts into this file. The showcase deploy workflow
([`azure-static-web-apps-showcase.yml`](../../.github/workflows/azure-static-web-apps-showcase.yml))
runs it before `pnpm run build:showcase`, so Vite copies the fresh report into the
artifact. It is **gitignored**: a committed copy would be stale by definition.

To produce one locally, from the repo root:

```bash
pnpm run fable:app && pnpm run conformance:report
```

When the file is **absent or unparseable**, the panel renders **grey** and says so;
it never fakes a green. That honest-staleness behaviour is the whole point of
publishing the real artefact rather than a hand-written badge, and it is why the
publisher writes **nothing** when the harness yields no machine-readable result.
The publisher also exits with the gate's status, so a failing gate halts the deploy
instead of shipping a red panel.

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

`ok` is deliberately conservative: green requires the runner's own verdict, a zero
failure count, **and** a non-empty corpus. A harness that ran nothing is not a pass.
