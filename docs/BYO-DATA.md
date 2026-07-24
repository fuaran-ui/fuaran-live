# BYO-data client portal — which databases work client-only, and why

> Phase 324. The query-portal lets a visitor bring **their own LLM key** and **their own
> data source**, type a request in natural language, and watch the model emit _both_ a query
> and a dashboard — the query runs in the browser, the result schema type-checks the UI, and
> **nothing touches any server we run**. This note is the honest boundary: what "client-only"
> can and cannot mean, and where the line is drawn.

## The TCP wall (why not "paste any Postgres string")

A browser tab cannot open a raw TCP socket. The classic `postgres://user:pass@host:5432/db`
connection string assumes a TCP dial to port 5432 — a browser physically cannot do this, and
no amount of WASM changes it. So a _truly_ client-only portal targets exactly two things:

1. **HTTP-native serverless databases** — engines that expose a `fetch`-able HTTP (or
   WebSocket) query endpoint, so the browser talks to them directly with no server of ours in
   the middle.
2. **In-browser engines** — a query engine compiled to WASM that runs _inside_ the tab over a
   file you point it at (Parquet / CSV / Arrow), with zero network at all.

Anything else — a Postgres on `:5432`, a corporate warehouse behind a VPN, a classic MySQL —
needs a server-or-peer leg, which is explicitly **not** this portal. That path is the
paired-device / hosted sibling ([Phase 325](../../roadmap/phases/325-rag-backed-retrieval-source.md));
it is honest about being server-or-peer-backed.

## What works client-only

| Source                                | How the browser reaches it         | Schema typing                | Driver               |
| ------------------------------------- | ---------------------------------- | ---------------------------- | -------------------- |
| **Neon** (`@neondatabase/serverless`) | HTTPS query endpoint               | declared (pg type OIDs)      | `neonHttpAdapter`    |
| **Turso / libSQL**                    | HTTPS `/v2/pipeline`               | declared (SQLite `decltype`) | `libsqlHttpAdapter`  |
| **PlanetScale**                       | HTTPS query API                    | inferred from rows           | `genericRowsAdapter` |
| **Supabase / PostgREST**              | HTTPS REST                         | inferred from rows           | `genericRowsAdapter` |
| **Cloudflare D1**                     | HTTPS query API                    | inferred from rows           | `genericRowsAdapter` |
| **DuckDB-WASM**                       | in-tab WASM over Parquet/CSV/Arrow | declared (DuckDB types)      | `duckdbSource`       |

All of these are `IClientQuerySource` implementations behind the one seam in
[`sources.ts`](../src/query-portal/sources.ts). The HTTP drivers live in
[`httpDriverSource.ts`](../src/query-portal/httpDriverSource.ts); the in-browser engine in
[`duckdbSource.ts`](../src/query-portal/duckdbSource.ts).

## The type-check is the point

The driver fetches rows and produces a **result schema**. The dashboard the LLM emitted is
gated against that schema by the shipped F# `Fuaran.UI.QueryBinding.check` (reached through the
Fable bridge — one relation, not a TS re-implementation). A `string` column **cannot** bind
into a numeric gauge: the validator rejects it with a legible `FUARAN066`/`FUARAN067` defect
_before_ anything renders. "Show the customer-name column as a gauge" is refused, not drawn
wrong. The result schema _is_ the UI contract.

## Refinement is data, not round-trips

Once rows are in hand, a follow-on tweak — "sort descending", "filter to region X", "regroup
by month" — is applied as a `Fuaran.Core.DataFrame` pipeline **locally**, over the columns
already fetched ([`refine.ts`](../src/query-portal/refine.ts)). No re-query, no LLM call where
the algebra resolves it. The model writes the coarse query once; interaction is native-speed
local compute. A refinement that drops a bound column is caught as a typed defect (the same
gate), never a broken render. A refinement the algebra cannot express falls back to a fresh
emission — the slow path, taken only when needed.

## Read-only by default

A serverless source is configured with a **read-only token by default**; writes are an
explicit opt-in (`allowWrite`). As a client-side belt-and-braces, the HTTP driver also refuses
to send a statement that is not read-only (a leading `SELECT`/`WITH`/`EXPLAIN`…) unless
`allowWrite` is set — so a mis-scoped token cannot silently mutate through this surface.

## The data-governance line (schema-to-LLM)

This is the precise promise, and how it is enforced:

- **We persist nothing.** There is no server of ours in the query path. Credentials + the LLM
  key live in **browser memory or `sessionStorage` only** — never `localStorage`, never disk,
  never a cookie ([`ephemeral.ts`](../src/query-portal/ephemeral.ts)). Closing the tab leaves
  nothing; a visible "nothing persisted" affordance reflects the store's posture, and a
  one-click purge wipes every held secret.
- **The BYO-LLM endpoint sees what you send it — and you choose how much.** In the default
  mode the model is given the query result so it can reason over the data. In **schema-only
  mode** (a first-class, prominent toggle) the model is sent only the `(name, type)` schema —
  _never a sample row_. The whole dashboard is then validated against structure alone; not one
  row value crosses to the model. The mechanism is enforced end-to-end: the source returns
  `rows: null`, and the type-check needs only the schema.

So the honest one-liner: **we** persist nothing; the **BYO-LLM endpoint** sees structure, plus
rows only if you opt in.

## See also

- [`sources.ts`](../src/query-portal/sources.ts) — the `IClientQuerySource` seam + the
  `resolveAndCheck` gate.
- [`emission.ts`](../src/query-portal/emission.ts) — the NL→query+UI emission loop.
- [Phase 324](../../roadmap/phases/324-byo-db-client-query-dashboard-portal.md) — the roadmap
  phase; [Phase 325](../../roadmap/phases/325-rag-backed-retrieval-source.md) — the
  server-or-peer-backed retrieval sibling.
