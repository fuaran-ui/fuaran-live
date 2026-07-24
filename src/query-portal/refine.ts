// ============================================================================
//  Local-refinement fast-path resolver (Phase 324, task 4).
//
//  "Client-side refinement = data, not round-trips." Once a query has resolved to
//  rows in hand, a follow-on tweak – "sort descending", "filter region X",
//  "regroup by month" – is applied as a `Fuaran.Core.DataFrame` pipeline over the
//  ALREADY-FETCHED columns, through the shipped F# `QueryRefine.refineLocally`.
//  No re-query, no LLM call: the model writes the coarse query once, interaction
//  is native-speed local algebra. This is the "fluent" feel the portal promises.
//
//  This resolver decides, per refinement, whether it is LOCALLY SATISFIABLE:
//    • `refined`      – the pipeline evaluated AND the dashboard still types: hand
//                       back the refined schema + rows, re-render with ZERO
//                       re-query / ZERO token spend (the fast path);
//    • `typeMismatch` – the pipeline evaluated but dropped / re-typed a bound
//                       column, so the dashboard no longer types (FUARAN066/067):
//                       surface the typed defects, do NOT silently re-emit;
//    • `fallback`     – the pipeline could not evaluate over the in-hand columns
//                       (unknown column, unexpressible in the algebra): the
//                       caller's signal to fall back to a fresh NL→query emission
//                       (the slow path).
//
//  The algebra lives in ONE place (the F# core); this module is only the
//  fast-path/slow-path decision over its verdict – no second algebra to drift.
// ============================================================================

import {
  refineLocally as bridgeRefine,
  type BindingDefect,
  type RefinedRow,
  type ResultSchema,
} from './core';
import type { ColumnarResult } from './sources';

/** The resolver's decision for one proposed refinement. */
export type RefineOutcome =
  | {
      readonly kind: 'refined';
      readonly schema: ResultSchema;
      readonly rows: readonly RefinedRow[];
    }
  | { readonly kind: 'typeMismatch'; readonly defects: readonly BindingDefect[] }
  | { readonly kind: 'fallback'; readonly reason: string };

/**
 * Try to satisfy a refinement locally over the in-hand `current` result. `current`
 * must carry rows (a schema-only resolution has nothing to refine – re-fetch with
 * rows first). `pipelineWire` is a canonical `Transform[]` JSON; `dashboardWire`
 * is the current dashboard. Returns the fast-path/slow-path decision.
 */
export function tryLocalRefine(
  current: ColumnarResult,
  pipelineWire: string,
  dashboardWire: string,
): RefineOutcome {
  if (current.rows === null) {
    return {
      kind: 'fallback',
      reason: 'no rows in hand (schema-only resolution); re-fetch with rows before refining',
    };
  }
  const result = bridgeRefine(current.schema, current.rows, pipelineWire, dashboardWire);
  if (result.ok) {
    return { kind: 'refined', schema: result.schema ?? current.schema, rows: result.rows ?? [] };
  }
  if (result.defects.length > 0) {
    return { kind: 'typeMismatch', defects: result.defects };
  }
  return { kind: 'fallback', reason: result.error ?? 'refinement could not be applied locally' };
}
