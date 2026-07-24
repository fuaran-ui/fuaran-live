// ============================================================================
//  Serverless-HTTP `IClientQuerySource` driver (Phase 324, task 1).
//
//  The BYO-remote story: connect a browser straight to an HTTP-native serverless
//  database – Neon (`@neondatabase/serverless`), Turso / libSQL, PlanetScale,
//  Supabase / PostgREST, Cloudflare D1 – and resolve a query into a typed
//  columnar result with NO server we run in the path. The honest boundary
//  (docs/BYO-DATA.md): a browser cannot dial raw TCP-on-5432, so this targets the
//  HTTP/`fetch` surfaces those engines expose, not arbitrary Postgres strings.
//
//  Wire-shape variety is contained in a tiny `HttpQueryAdapter` seam: one adapter
//  per provider family turns (sql, config) → a `fetch` request and the response
//  JSON → a `ColumnarResult`. The driver itself owns only the cross-cutting
//  concerns – the read-only-by-default guard, the `fetch` call, error mapping,
//  and the schema-only projection. `fetch` is injected so the whole path is
//  headlessly testable without a live endpoint.
//
//  Security posture: the connection config (endpoint URL + scoped token) lives in
//  browser memory only (see ./ephemeral). A read-only token is the recommended
//  default; as a client-side belt-and-braces, the driver also refuses to send a
//  statement that is not read-only unless `allowWrite` is explicitly opted in –
//  so a mis-scoped token cannot silently mutate through this surface.
// ============================================================================

import type { ColumnType, ResultSchema } from './core';
import type {
  ColumnarResult,
  IClientQuerySource,
  QueryRequest,
  ResolveOutcome,
  ResultRow,
} from './sources';

/** Connection config for a serverless-HTTP source – endpoint + scoped token, in memory only. */
export interface HttpSourceConfig {
  /** The provider's HTTP query endpoint (e.g. a Neon/Turso/PlanetScale URL). */
  readonly endpoint: string;
  /** A scoped access token. Prefer a READ-ONLY token (see the module header). */
  readonly token: string;
  /** Opt in to mutating statements. Default `false` – read-only is the default. */
  readonly allowWrite?: boolean;
}

/**
 * The minimal `fetch` shape the driver needs – `globalThis.fetch` conforms, and a
 * test passes a stub. Kept structural (no `lib.dom` `Response`) so the module
 * type-checks under the node test environment.
 */
export type FetchLike = (
  input: string,
  init?: {
    method?: string;
    headers?: Record<string, string>;
    body?: string;
  },
) => Promise<{
  readonly ok: boolean;
  readonly status: number;
  readonly statusText: string;
  json(): Promise<unknown>;
  text(): Promise<string>;
}>;

/**
 * A provider-family adapter: it knows one engine's request shape + response wire,
 * nothing else. `parseResponse` returns either a typed result or a string error
 * (a malformed/error payload is a typed failure, never a thrown surprise).
 */
export interface HttpQueryAdapter {
  readonly name: string;
  buildRequest(
    sql: string,
    config: HttpSourceConfig,
  ): { url: string; method: string; headers: Record<string, string>; body: string };
  parseResponse(
    payload: unknown,
  ): { ok: true; value: ColumnarResult } | { ok: false; error: string };
}

// ─── read-only guard ─────────────────────────────────────────────────────────

// Statements that only read. A query the driver will send under a read-only
// token: a leading SELECT / WITH / EXPLAIN / SHOW / PRAGMA (read forms), after
// stripping leading comments + whitespace. Anything else needs `allowWrite`.
const READ_ONLY_LEAD = /^\s*(select|with|explain|show|pragma|table|values)\b/i;

/** True when `sql`'s first significant keyword is a read-only one. */
export function isReadOnlySql(sql: string): boolean {
  // Strip leading line/block comments so `-- note\nSELECT …` still reads as read-only.
  const stripped = sql.replace(/^\s*(--[^\n]*\n|\/\*[\s\S]*?\*\/)\s*/g, '');
  return READ_ONLY_LEAD.test(stripped);
}

// ─── value → ColumnType inference (for providers that ship untyped rows) ──────

/** Infer a `ColumnType` from a single JSON scalar (PostgREST-style untyped rows). */
function inferType(value: unknown): ColumnType {
  switch (typeof value) {
    case 'boolean':
      return 'bool';
    case 'number':
      return Number.isInteger(value) ? 'int' : 'float';
    case 'string':
      // ISO-8601 date (YYYY-MM-DD) vs a fuller timestamp vs free string.
      if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return 'date';
      if (/^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(value)) return 'timestamp';
      return 'string';
    default:
      return 'string';
  }
}

/** Infer a `ResultSchema` by scanning rows for the first non-null value per column. */
export function inferSchema(rows: readonly ResultRow[]): ResultSchema {
  const names: string[] = [];
  const seen = new Set<string>();
  for (const row of rows) {
    for (const k of Object.keys(row)) {
      if (!seen.has(k)) {
        seen.add(k);
        names.push(k);
      }
    }
  }
  return names.map((name) => {
    const firstNonNull = rows.find((r) => r[name] !== null && r[name] !== undefined);
    return { name, type: firstNonNull ? inferType(firstNonNull[name]) : 'string' };
  });
}

// ─── adapters ────────────────────────────────────────────────────────────────

/**
 * Untyped-rows adapter (Supabase / PostgREST, PlanetScale JSON, D1 `results`):
 * the response is an array of row objects (or `{ rows | results | data: [...] }`)
 * and the schema is INFERRED from the values. The widest-compatibility adapter;
 * a provider that ships declared column types should use a typed adapter instead.
 */
export const genericRowsAdapter: HttpQueryAdapter = {
  name: 'generic-rows',
  buildRequest(sql, config) {
    return {
      url: config.endpoint,
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        authorization: `Bearer ${config.token}`,
      },
      body: JSON.stringify({ query: sql }),
    };
  },
  parseResponse(payload) {
    const rows = extractRowArray(payload);
    if (rows === null) {
      const err = extractError(payload);
      return { ok: false, error: err ?? 'response carried no recognisable row array' };
    }
    return { ok: true, value: { schema: inferSchema(rows), rows } };
  },
};

/**
 * Neon serverless adapter (`@neondatabase/serverless` HTTP): the response carries
 * `fields: [{ name, dataTypeID }]` (Postgres type OIDs) + `rows: [...]`, so the
 * schema is DECLARED, not inferred.
 */
export const neonHttpAdapter: HttpQueryAdapter = {
  name: 'neon-http',
  buildRequest(sql, config) {
    return {
      url: config.endpoint,
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        authorization: `Bearer ${config.token}`,
        'neon-raw-text-output': 'false',
      },
      body: JSON.stringify({ query: sql, params: [] }),
    };
  },
  parseResponse(payload) {
    if (typeof payload !== 'object' || payload === null)
      return { ok: false, error: 'non-object response' };
    const p = payload as { fields?: unknown; rows?: unknown };
    if (!Array.isArray(p.fields) || !Array.isArray(p.rows)) {
      const err = extractError(payload);
      return { ok: false, error: err ?? 'Neon response missing fields/rows' };
    }
    const schema: ResultSchema = (p.fields as Array<{ name: string; dataTypeID: number }>).map(
      (f) => ({
        name: f.name,
        type: pgOidToColumnType(f.dataTypeID),
      }),
    );
    return { ok: true, value: { schema, rows: p.rows as readonly ResultRow[] } };
  },
};

/**
 * Turso / libSQL HTTP adapter (the `/v2/pipeline` shape): a `execute` request,
 * the response carrying `cols: [{ name, decltype }]` + `rows: [...]` (SQLite
 * declared types). Schema is DECLARED via `decltype`.
 */
export const libsqlHttpAdapter: HttpQueryAdapter = {
  name: 'libsql-http',
  buildRequest(sql, config) {
    return {
      url: config.endpoint,
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        authorization: `Bearer ${config.token}`,
      },
      body: JSON.stringify({
        requests: [{ type: 'execute', stmt: { sql } }, { type: 'close' }],
      }),
    };
  },
  parseResponse(payload) {
    // Pipeline response: { results: [{ type:'ok', response:{ result:{ cols, rows } } }, …] }
    const result = navigate(payload, ['results', 0, 'response', 'result']) as {
      cols?: Array<{ name: string; decltype: string | null }>;
      rows?: unknown[][];
    } | null;
    if (!result || !Array.isArray(result.cols) || !Array.isArray(result.rows)) {
      const err = extractError(payload);
      return { ok: false, error: err ?? 'libSQL response missing cols/rows' };
    }
    const schema: ResultSchema = result.cols.map((c) => ({
      name: c.name,
      type: sqliteDeclTypeToColumnType(c.decltype),
    }));
    // libSQL rows are positional cells `{ type, value }`; map to name-keyed objects.
    const rows: ResultRow[] = result.rows.map((cells) => {
      const row: Record<string, unknown> = {};
      cells.forEach((cell, i) => {
        const col = schema[i];
        if (col) row[col.name] = unwrapLibsqlCell(cell, col.type);
      });
      return row;
    });
    return { ok: true, value: { schema, rows } };
  },
};

// ─── the driver ──────────────────────────────────────────────────────────────

/**
 * Build an `IClientQuerySource` over a serverless-HTTP database via `adapter`.
 * `fetchImpl` defaults to the ambient `fetch`; tests inject a stub. The resolve
 * path: read-only guard → adapter request → `fetch` → adapter parse → schema-only
 * projection. Every failure is a typed `QuerySourceError`, never a throw.
 */
export function httpDriverSource(
  adapter: HttpQueryAdapter,
  config: HttpSourceConfig,
  fetchImpl?: FetchLike,
): IClientQuerySource {
  const doFetch: FetchLike = fetchImpl ?? (globalThis.fetch as unknown as FetchLike);
  return {
    async resolve(request: QueryRequest): Promise<ResolveOutcome> {
      if (!config.allowWrite && !isReadOnlySql(request.sql)) {
        return {
          ok: false,
          error: {
            kind: 'failed',
            detail: 'write blocked: this source is read-only (set allowWrite to opt in)',
          },
        };
      }
      const req = adapter.buildRequest(request.sql, config);
      let resp: Awaited<ReturnType<FetchLike>>;
      try {
        resp = await doFetch(req.url, { method: req.method, headers: req.headers, body: req.body });
      } catch (e) {
        return { ok: false, error: { kind: 'unavailable', detail: errorDetail(e) } };
      }
      if (!resp.ok) {
        const body = await safeText(resp);
        return {
          ok: false,
          error: {
            kind: 'failed',
            detail: `${resp.status} ${resp.statusText}${body ? `: ${body}` : ''}`,
          },
        };
      }
      let payload: unknown;
      try {
        payload = await resp.json();
      } catch (e) {
        return {
          ok: false,
          error: { kind: 'failed', detail: `malformed JSON: ${errorDetail(e)}` },
        };
      }
      const parsed = adapter.parseResponse(payload);
      if (!parsed.ok) {
        return { ok: false, error: { kind: 'failed', detail: parsed.error } };
      }
      // Schema-only privacy projection: keep the typed schema, drop every row.
      const value: ColumnarResult = request.schemaOnly
        ? { schema: parsed.value.schema, rows: null }
        : parsed.value;
      return { ok: true, value };
    },
  };
}

// ─── small total helpers ─────────────────────────────────────────────────────

function extractRowArray(payload: unknown): readonly ResultRow[] | null {
  if (Array.isArray(payload)) return payload as readonly ResultRow[];
  if (typeof payload === 'object' && payload !== null) {
    const p = payload as Record<string, unknown>;
    for (const key of ['rows', 'results', 'data']) {
      const v = p[key];
      if (Array.isArray(v)) return v as readonly ResultRow[];
    }
  }
  return null;
}

function extractError(payload: unknown): string | null {
  if (typeof payload === 'object' && payload !== null) {
    const p = payload as Record<string, unknown>;
    const e = p.error ?? p.message;
    if (typeof e === 'string') return e;
    if (
      typeof e === 'object' &&
      e !== null &&
      typeof (e as { message?: unknown }).message === 'string'
    ) {
      return (e as { message: string }).message;
    }
  }
  return null;
}

/** Walk a payload by a path of object keys / array indices; `null` if any hop misses. */
function navigate(payload: unknown, path: readonly (string | number)[]): unknown {
  let cur: unknown = payload;
  for (const hop of path) {
    if (cur === null || cur === undefined) return null;
    if (typeof hop === 'number') {
      if (!Array.isArray(cur)) return null;
      cur = cur[hop];
    } else {
      if (typeof cur !== 'object') return null;
      cur = (cur as Record<string, unknown>)[hop];
    }
  }
  return cur ?? null;
}

function unwrapLibsqlCell(cell: unknown, type: ColumnType): unknown {
  // libSQL cells are `{ type: 'integer'|'float'|'text'|'null'|'blob', value }`.
  if (typeof cell !== 'object' || cell === null) return cell;
  const c = cell as { type?: string; value?: unknown };
  if (c.type === 'null') return null;
  if (c.value === undefined) return null;
  // libSQL ships integers as strings; coerce numeric sinks back to numbers.
  if ((type === 'int' || type === 'float') && typeof c.value === 'string') {
    const n = Number(c.value);
    return Number.isNaN(n) ? c.value : n;
  }
  return c.value;
}

// Postgres type OIDs → ColumnType (the common scalar set; default to string).
function pgOidToColumnType(oid: number): ColumnType {
  switch (oid) {
    case 16:
      return 'bool';
    case 20:
    case 21:
    case 23:
    case 26:
      return 'int';
    case 700:
    case 701:
    case 1700:
      return 'float';
    case 1082:
      return 'date';
    case 1114:
    case 1184:
      return 'timestamp';
    default:
      return 'string';
  }
}

// SQLite declared type → ColumnType (affinity-style prefix match; default string).
function sqliteDeclTypeToColumnType(decltype: string | null): ColumnType {
  if (!decltype) return 'string';
  const t = decltype.toLowerCase();
  if (t.includes('int')) return 'int';
  if (
    t.includes('real') ||
    t.includes('floa') ||
    t.includes('doub') ||
    t.includes('numeric') ||
    t.includes('decimal')
  )
    return 'float';
  if (t.includes('bool')) return 'bool';
  if (t === 'date') return 'date';
  if (t.includes('timestamp') || t.includes('datetime')) return 'timestamp';
  return 'string';
}

function errorDetail(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

async function safeText(resp: { text(): Promise<string> }): Promise<string> {
  try {
    return (await resp.text()).slice(0, 500);
  } catch {
    return '';
  }
}
