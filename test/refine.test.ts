// Phase 324, task 4 – the local-refinement fast-path, run by the SHIPPED F#
// QueryRefine.refineLocally via the Fable bridge (no re-query, no LLM call). The
// three decisions: a refinement that evaluates + still types (fast path, zero
// re-query), one that drops a bound column (typed FUARAN066/067 – surface, don't
// re-emit), and one the algebra can't evaluate (fallback to a fresh emission).

import { describe, it, expect } from 'vitest';
import { fuaran, binding } from '@fuaran-ui/ui';
import { encodeNode } from '@fuaran-ui/ops';

import { tryLocalRefine } from '../src/query-portal/refine';
import type { ColumnarResult } from '../src/query-portal/sources';
import type { ResultSchema } from '../src/query-portal/core';

const schema: ResultSchema = [
  { name: 'revenue', type: 'float' },
  { name: 'category', type: 'string' },
];

const current: ColumnarResult = {
  schema,
  rows: [
    { revenue: 17, category: 'toys' },
    { revenue: 42, category: 'books' },
  ],
};

// A metric whose numeric value is bound to query column `col`.
const dashboardBoundTo = (col: string): string =>
  encodeNode(
    fuaran.metric({
      id: 'm1',
      label: 'Top',
      value: binding.query(col, (row: Record<string, number>) => row[col] as number),
    }),
  );

// Canonical Transform[] wire (the $type-discriminated shape the F# codec decodes).
const sortDesc = (col: string) => JSON.stringify([{ $type: 'sort', by: [{ col, dir: 'desc' }] }]);
const projectKeep = (col: string) =>
  JSON.stringify([{ $type: 'project', cols: [{ a: col, b: col }] }]);
// A derive whose expression reads a column not in hand – the evaluator raises
// UnknownColumn, the algebra's "I cannot express this locally" signal.
const deriveFromUnknown = (col: string) =>
  JSON.stringify([{ $type: 'derive', name: 'derived', expr: { $type: 'col', name: col } }]);

describe('tryLocalRefine – the fast path', () => {
  it('a sort that keeps the schema refines locally (zero re-query) and reorders rows', () => {
    const out = tryLocalRefine(current, sortDesc('revenue'), dashboardBoundTo('revenue'));
    expect(out.kind).toBe('refined');
    if (out.kind === 'refined') {
      // The dashboard still types against the unchanged schema; rows are sorted desc.
      expect(out.schema).toContainEqual({ name: 'revenue', type: 'float' });
      expect(out.rows[0]!.revenue).toBe(42);
      expect(out.rows[1]!.revenue).toBe(17);
    }
  });
});

describe('tryLocalRefine – typed defect (refinement drops a bound column)', () => {
  it('projecting away the bound numeric column is a typed FUARAN067, not a broken render', () => {
    // Keep only `category`; the dashboard still binds `revenue` → now absent.
    const out = tryLocalRefine(current, projectKeep('category'), dashboardBoundTo('revenue'));
    expect(out.kind).toBe('typeMismatch');
    if (out.kind === 'typeMismatch') {
      expect(out.defects[0]!.code).toBe('FUARAN067');
      expect(out.defects[0]!.column).toBe('revenue');
    }
  });
});

describe('tryLocalRefine – fallback (not locally satisfiable)', () => {
  it('a refinement the algebra cannot evaluate (unknown column) falls back to re-emission', () => {
    const out = tryLocalRefine(
      current,
      deriveFromUnknown('nonexistent'),
      dashboardBoundTo('revenue'),
    );
    expect(out.kind).toBe('fallback');
    if (out.kind === 'fallback') expect(out.reason).toMatch(/unknown|nonexistent/i);
  });

  it('a schema-only resolution has nothing to refine → fallback', () => {
    const schemaOnly: ColumnarResult = { schema, rows: null };
    const out = tryLocalRefine(schemaOnly, sortDesc('revenue'), dashboardBoundTo('revenue'));
    expect(out.kind).toBe('fallback');
    if (out.kind === 'fallback') expect(out.reason).toMatch(/schema-only|no rows/i);
  });
});
