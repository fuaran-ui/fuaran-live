// ============================================================================
//  Typed TS facade over the Fable-compiled core bridge
//  (fable-host/QueryPortalBridge.fs → emitted by `dotnet fable` to
//  fable-host/output/QueryPortalBridge.js).
//
//  The hybrid architecture (Phase 324/325): the TS driver edges fetch rows and
//  produce a result SCHEMA; the type-check that decides "can this dashboard
//  render against this data?" is the SHIPPED F# `Fuaran.UI.QueryBinding.check`,
//  reached through this bridge – NOT reimplemented in TS. One relation, one
//  source of truth; the TS shell calls it. The check crosses as schema JSON
//  only – no row data – which is also exactly the schema-only privacy path.
//
//  Fable emits no `.d.ts`, and TS cannot type a relative import of generated JS,
//  so the untyped import is isolated HERE behind a hand-authored typed seam that
//  mirrors the bridge's stable export surface; every other portal module imports
//  from this file, fully typed.
// ============================================================================

// The six Arrow-aligned scalar types a result column ranges over – the tags the
// F# `Fuaran.Core.ColumnType` serialises to.
export type ColumnType = 'int' | 'float' | 'bool' | 'string' | 'date' | 'timestamp';

export interface SchemaColumn {
  readonly name: string;
  readonly type: ColumnType;
}

/** A resolved query result schema – the contract the dashboard is typed against. */
export type ResultSchema = readonly SchemaColumn[];

/** One typed binding defect (the §4d AI-recovery shape), mirroring the F# `Defect`. */
export interface BindingDefect {
  /** Stable code: `FUARAN066` (incompatible sink) or `FUARAN067` (absent column). */
  readonly code: string;
  readonly nodeId: string;
  readonly column: string;
  /** The sink class: `numeric` | `temporal` | `categorical` | `boolean`. */
  readonly sink: string;
  readonly message: string;
  /** Recovery hint: the columns that WOULD type-check here / the available columns. */
  readonly availableFields: readonly string[];
  readonly suggestion: string | null;
}

/** The bridge's check result: `ok` ⇒ render; otherwise the typed defects (or a parse error). */
export interface CheckResult {
  readonly ok: boolean;
  readonly error?: string;
  readonly defects: readonly BindingDefect[];
}

/** A refined row – column name → scalar value (post-refinement, `null` for absent). */
export type RefinedRow = Readonly<Record<string, unknown>>;

/**
 * The bridge's local-refinement result (Phase 324 fast-path). `ok` ⇒ the refined
 * `schema` + `rows` (re-render locally, zero re-query); otherwise `defects` carries
 * the typed FUARAN066/067 when the refinement dropped/re-typed a bound column, or
 * `error` alone when the refinement could not evaluate (the re-emit signal).
 */
export interface RefineResult {
  readonly ok: boolean;
  readonly schema?: ResultSchema;
  readonly rows?: readonly RefinedRow[];
  readonly error?: string;
  readonly defects: readonly BindingDefect[];
}

// ─── the single untyped seam ─────────────────────────────────────────────────

// The Fable-compiled bridge (fable-host/QueryPortalBridge.fs, emitted by
// `dotnet fable`). Fable emits no `.d.ts` and TS cannot type a relative import of
// generated JS, so this one import is the untyped seam; the namespace is cast to
// the bridge's stable, hand-authored export surface below.
// @ts-ignore -- Fable-emitted JS has no declaration file (see above).
import * as bridge from '../../fable-host/output/QueryPortalBridge.js';

interface BridgeModule {
  checkDashboard(schemaJson: string, dashboardWireJson: string): CheckResult;
  retrievalHitSchema(): ResultSchema;
  refineLocally(
    schemaJson: string,
    rowsJson: string,
    pipelineJson: string,
    dashboardJson: string,
  ): RefineResult;
}

const fableBridge = bridge as unknown as BridgeModule;

// ─── the typed API ───────────────────────────────────────────────────────────

/**
 * Type-check a dashboard (canonical Fuaran wire JSON) against a resolved result
 * `schema`, through the shipped F# `QueryBinding.check`. `ok` ⇒ the dashboard is
 * sound (render it); otherwise `defects` carries the typed FUARAN066 / FUARAN067
 * mismatches. The "can't render wrong" gate – render only when `ok`.
 */
export function checkDashboard(schema: ResultSchema, dashboardWire: string): CheckResult {
  return fableBridge.checkDashboard(JSON.stringify(schema), dashboardWire);
}

/**
 * The canonical retrieval-hit schema (Phase 325), bridged from the F#
 * `RetrievalSource.hitSchema` – so a retrieval (case-history) dashboard types
 * against the SAME contract the relational plane uses. One UI contract, both
 * data planes.
 */
export function retrievalHitSchema(): ResultSchema {
  return fableBridge.retrievalHitSchema();
}

/**
 * Apply a local refinement (a canonical `Transform[]` pipeline) to ALREADY-FETCHED
 * `rows` and re-type `dashboardWire` against the refined schema – through the
 * shipped F# `Fuaran.UI.QueryRefine.refineLocally` (the pinned `Fuaran.Core.DataFrame`
 * evaluator). The Phase 324 fast-path: a follow-on tweak costs ZERO re-query and
 * ZERO LLM tokens. `ok` ⇒ render the refined `schema`/`rows`; otherwise `defects`
 * (the refinement broke a binding) or `error` (it could not evaluate – re-emit).
 */
export function refineLocally(
  schema: ResultSchema,
  rows: readonly RefinedRow[],
  pipelineWire: string,
  dashboardWire: string,
): RefineResult {
  const raw = fableBridge.refineLocally(
    JSON.stringify(schema),
    JSON.stringify(rows),
    pipelineWire,
    dashboardWire,
  );
  // The bridge omits `defects` on the success path; normalise to always-present.
  return { ...raw, defects: raw.defects ?? [] };
}
