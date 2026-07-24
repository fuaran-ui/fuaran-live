// ============================================================================
//  Client query-source seam (TS) + the resolve→check→render gate (Phase 324).
//
//  The TS-native sibling of the F# `IClientQuerySource`: a host-pluggable source
//  resolves a request into a typed columnar result (schema ± rows), then the
//  dashboard is gated against the schema by the shipped F# `checkDashboard`
//  (via ./core). A type-mismatched dashboard NEVER renders (default-deny before
//  render). The async (Promise) boundary is TS-native – the browser drivers are
//  fetch/WASM-based – while the type-check stays the one F# relation.
//
//  Driver edges implement `IClientQuerySource`:
//    - the bundled "Try it now" source (DuckDB-WASM over Chinook / a Parquet) –
//      the offline, never-breaks demo;
//    - a serverless-HTTP source (one provider – Turso/Neon, or a Datasette public
//      instance) – the BYO-remote story.
//  Those drivers are browser/WASM/live-endpoint bound and are added on top of
//  this seam; `inMemorySource` below is the headlessly-testable reference impl
//  the drivers drop in for.
// ============================================================================

import { checkDashboard, type ResultSchema, type CheckResult } from './core';

/** A row of a resolved result – column name → scalar value. */
export type ResultRow = Readonly<Record<string, unknown>>;

/**
 * A resolved query result: the typed schema (always present – it types the UI),
 * with `rows` present only in full-fetch mode. `rows === null` is the schema-only
 * privacy path – nothing but `(name, type)` pairs left the source.
 */
export interface ColumnarResult {
  readonly schema: ResultSchema;
  readonly rows: readonly ResultRow[] | null;
}

/** Why a client-side resolve failed. */
export type QuerySourceError =
  | { readonly kind: 'unavailable'; readonly detail: string }
  | { readonly kind: 'failed'; readonly detail: string }
  | { readonly kind: 'timeout' };

/** A query request: a dialect string a driver executes + the privacy toggle. */
export interface QueryRequest {
  /** Dialect SQL the driver runs (DuckDB-WASM / serverless HTTP / Datasette). */
  readonly sql: string;
  /** Privacy mode: when true, the source returns schema only – never fetches rows. */
  readonly schemaOnly: boolean;
}

export type ResolveOutcome =
  | { readonly ok: true; readonly value: ColumnarResult }
  | { readonly ok: false; readonly error: QuerySourceError };

/**
 * The host-pluggable client query-source seam. Async-at-the-boundary (the
 * browser drivers are fetch/WASM-based); a DuckDB-WASM engine, a serverless-HTTP
 * driver, or the in-memory reference all conform.
 */
export interface IClientQuerySource {
  resolve(request: QueryRequest): Promise<ResolveOutcome>;
}

/** The portal's decision after resolve + check: render, source error, or type mismatch. */
export type PortalOutcome =
  | {
      readonly kind: 'render';
      readonly schema: ResultSchema;
      readonly rows: readonly ResultRow[] | null;
    }
  | { readonly kind: 'sourceError'; readonly error: QuerySourceError }
  | { readonly kind: 'typeMismatch'; readonly defects: CheckResult['defects'] };

/**
 * Resolve `request` through `source`, then gate `dashboardWire` against the
 * resolved schema with the shipped F# `checkDashboard`. A type-mismatched
 * dashboard returns `typeMismatch` (the typed FUARAN066/067 defects) and NEVER
 * renders – the TS sibling of the F# `QuerySource.resolveAndCheck`. `schemaOnly`
 * threads the privacy mode through to the source.
 */
export async function resolveAndCheck(
  source: IClientQuerySource,
  request: QueryRequest,
  dashboardWire: string,
): Promise<PortalOutcome> {
  const resolved = await source.resolve(request);
  if (!resolved.ok) {
    return { kind: 'sourceError', error: resolved.error };
  }
  const result = checkDashboard(resolved.value.schema, dashboardWire);
  if (!result.ok) {
    return { kind: 'typeMismatch', defects: result.defects };
  }
  return { kind: 'render', schema: resolved.value.schema, rows: resolved.value.rows };
}

/**
 * The bundled-data reference source – resolves every request to a fixed schema
 * (+ rows, unless `schemaOnly`). It is the "Try it now" seam's headlessly-testable
 * stand-in and the exact shape a DuckDB-WASM / serverless-HTTP driver drops into.
 */
export function inMemorySource(
  schema: ResultSchema,
  rows: readonly ResultRow[],
): IClientQuerySource {
  return {
    async resolve(request: QueryRequest): Promise<ResolveOutcome> {
      return { ok: true, value: { schema, rows: request.schemaOnly ? null : rows } };
    },
  };
}
