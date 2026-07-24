// ============================================================================
//  DuckDB-WASM `IClientQuerySource` driver (Phase 324, task 2).
//
//  The *fully* offline, zero-network query path: an in-browser engine running
//  over a Parquet / CSV / Arrow file (or an HTTP-range-fetched one) with no DB
//  server at all – the "Try it now" demo that never breaks because nothing has to
//  be reachable. DuckDB-WASM is Arrow-native, so a result's columns map straight
//  onto the same typed `ResultSchema` the relational thread already speaks.
//
//  The engine itself is browser/WASM-bound, so it sits behind a tiny `IDuckDbConn`
//  seam: the mapping logic (DuckDB type → `ColumnType`, schema-only projection,
//  error mapping) is pure and headlessly testable against a fake connection,
//  while the real `@duckdb/duckdb-wasm` instance is wired by `connectDuckDb` and
//  verified in the browser. Same discipline as the HTTP driver – one seam, one
//  testable resolve path, the heavy edge injected.
// ============================================================================

import type { ColumnType, ResultSchema } from './core';
import type { IClientQuerySource, QueryRequest, ResolveOutcome, ResultRow } from './sources';

/** One result column as DuckDB reports it: a name + its SQL type string. */
export interface DuckDbColumn {
  readonly name: string;
  /** The DuckDB SQL type, e.g. `BIGINT`, `DOUBLE`, `VARCHAR`, `DATE`, `TIMESTAMP`. */
  readonly sqlType: string;
}

/** A DuckDB query result reduced to what the driver reads: typed columns + rows. */
export interface DuckDbResult {
  readonly columns: readonly DuckDbColumn[];
  /** The rows as plain name-keyed objects (`arrow.Table#toArray()` shape). */
  toRows(): readonly ResultRow[];
}

/**
 * The injected DuckDB connection seam. A real `@duckdb/duckdb-wasm`
 * `AsyncDuckDBConnection` is adapted to this by `connectDuckDb`; a test passes a
 * fake. `query` rejects on a DuckDB error – the driver maps the rejection to a
 * typed `QuerySourceError`.
 */
export interface IDuckDbConn {
  query(sql: string): Promise<DuckDbResult>;
}

/** Map a DuckDB SQL type string to a `ColumnType` (prefix/affinity match). */
export function duckdbTypeToColumnType(sqlType: string): ColumnType {
  const t = sqlType.toUpperCase();
  if (t === 'BOOLEAN' || t === 'BOOL') return 'bool';
  if (
    t.includes('INT') || // TINYINT/SMALLINT/INTEGER/BIGINT/HUGEINT/UINTEGER…
    t === 'UTINYINT' ||
    t === 'USMALLINT' ||
    t === 'UBIGINT'
  )
    return 'int';
  if (
    t.startsWith('DEC') ||
    t.startsWith('NUMERIC') ||
    t === 'FLOAT' ||
    t === 'REAL' ||
    t === 'DOUBLE'
  )
    return 'float';
  if (t === 'DATE') return 'date';
  if (t.startsWith('TIMESTAMP') || t === 'DATETIME') return 'timestamp';
  // VARCHAR / TEXT / UUID / BLOB / anything structured → string sink.
  return 'string';
}

/** Build the `ResultSchema` from a DuckDB result's typed columns. */
export function schemaOf(result: DuckDbResult): ResultSchema {
  return result.columns.map((c) => ({ name: c.name, type: duckdbTypeToColumnType(c.sqlType) }));
}

/**
 * Build an `IClientQuerySource` over an in-browser DuckDB-WASM connection.
 * Schema-only mode runs the query but returns the schema with no rows – and, as a
 * stronger guarantee, can wrap the SQL in a `WHERE false` so the engine fetches no
 * data at all (`zeroRowProbe`, default on). The resolve path mirrors the HTTP
 * driver: query → map columns → schema-only projection → typed result.
 */
export function duckdbSource(
  conn: IDuckDbConn,
  opts: { readonly zeroRowProbe?: boolean } = {},
): IClientQuerySource {
  const zeroRowProbe = opts.zeroRowProbe ?? true;
  return {
    async resolve(request: QueryRequest): Promise<ResolveOutcome> {
      // In schema-only mode, optionally ask the engine for the shape with no data:
      // `SELECT * FROM (<sql>) WHERE false` types the columns, fetches zero rows.
      const sql =
        request.schemaOnly && zeroRowProbe
          ? `SELECT * FROM (${stripTrailingSemicolon(request.sql)}) WHERE false`
          : request.sql;
      let result: DuckDbResult;
      try {
        result = await conn.query(sql);
      } catch (e) {
        return { ok: false, error: { kind: 'failed', detail: errorDetail(e) } };
      }
      const schema = schemaOf(result);
      const rows = request.schemaOnly ? null : result.toRows();
      return { ok: true, value: { schema, rows } };
    },
  };
}

// ─── the real engine loader (browser/WASM – operator-verified) ────────────────

/** Where a DuckDB-WASM source reads its data from. */
export type DuckDbDataSource =
  | { readonly kind: 'parquet'; readonly url: string; readonly tableName?: string }
  | { readonly kind: 'csv'; readonly url: string; readonly tableName?: string }
  | { readonly kind: 'arrow'; readonly url: string; readonly tableName?: string };

/**
 * Bootstrap a real `@duckdb/duckdb-wasm` instance, register `source` as a view,
 * and adapt the live connection to `IDuckDbConn`. Browser/WASM-only – never
 * called by the headless suite (the mapping logic is tested against a fake conn).
 * The package is an optional dependency; the dynamic import is guarded so a build
 * without it fails with a legible message rather than a bundler crash.
 */
export async function connectDuckDb(source: DuckDbDataSource): Promise<IDuckDbConn> {
  // Typed `any`: the optional dep has no types resolvable at lint time. The shape
  // used below is the documented `@duckdb/duckdb-wasm` async API.
  let duckdb: any;
  try {
    // @ts-ignore -- optional dependency; resolved at runtime in the browser bundle.
    duckdb = await import('@duckdb/duckdb-wasm');
  } catch (e) {
    throw new Error(
      '@duckdb/duckdb-wasm is not installed; add it to run the in-browser engine source: ' +
        errorDetail(e),
    );
  }
  const bundle = await duckdb.selectBundle(duckdb.getJsDelivrBundles());
  const worker = new Worker(bundle.mainWorker!);
  const logger = new duckdb.ConsoleLogger();
  const db = new duckdb.AsyncDuckDB(logger, worker);
  await db.instantiate(bundle.mainModule, bundle.pthreadWorker);
  const connection = await db.connect();

  const table = source.tableName ?? 'data';
  const reader =
    source.kind === 'parquet'
      ? `read_parquet('${source.url}')`
      : source.kind === 'csv'
        ? `read_csv_auto('${source.url}')`
        : `read_arrow('${source.url}')`;
  await connection.query(`CREATE OR REPLACE VIEW ${table} AS SELECT * FROM ${reader}`);

  return {
    async query(sql: string): Promise<DuckDbResult> {
      const arrow = await connection.query(sql);
      const columns: DuckDbColumn[] = arrow.schema.fields.map(
        (f: { name: string; type: { toString(): string } }) => ({
          name: f.name,
          sqlType: f.type.toString(),
        }),
      );
      return {
        columns,
        toRows: () => arrow.toArray().map((r: { toJSON(): ResultRow }) => r.toJSON()),
      };
    },
  };
}

// ─── helpers ──────────────────────────────────────────────────────────────────

function stripTrailingSemicolon(sql: string): string {
  return sql.replace(/;\s*$/, '');
}

function errorDetail(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}
