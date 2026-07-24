// Phase 324/325 – the query-portal F#↔TS gate, exercised end-to-end across the
// Fable boundary. The dashboards are built with the TS @fuaran-ui/ui smart
// constructors + encoded to canonical wire JSON; the type-check verdict comes
// from the SHIPPED F# Fuaran.UI.QueryBinding.check via the Fable bridge. This is
// the headless proof that "the TS shell calls the Fable core" – no second type
// system, one relation.

import { describe, it, expect } from 'vitest';
import { fuaran, binding } from '@fuaran-ui/ui';
import { encodeNode } from '@fuaran-ui/ops';

import { checkDashboard, retrievalHitSchema, type ResultSchema } from '../src/query-portal/core';
import { inMemorySource, resolveAndCheck } from '../src/query-portal/sources';

// A relational result: revenue (float) + category (string).
const schema: ResultSchema = [
  { name: 'revenue', type: 'float' },
  { name: 'category', type: 'string' },
];

// A single-metric dashboard whose numeric value is bound to query column `col`.
const metricBoundTo = (col: string): string =>
  encodeNode(
    fuaran.metric({
      id: 'm1',
      label: 'Top',
      value: binding.query(col, (row: Record<string, number>) => row[col] as number),
    }),
  );

describe('the query→Binding gate across the F#/TS boundary', () => {
  it('accepts a float column bound to a numeric metric', () => {
    const result = checkDashboard(schema, metricBoundTo('revenue'));
    expect(result.ok).toBe(true);
    expect(result.defects).toHaveLength(0);
  });

  it('rejects a string column bound to a numeric metric – FUARAN066, with recovery', () => {
    const result = checkDashboard(schema, metricBoundTo('category'));
    expect(result.ok).toBe(false);
    expect(result.defects).toHaveLength(1);
    const defect = result.defects[0]!;
    expect(defect.code).toBe('FUARAN066');
    expect(defect.column).toBe('category');
    expect(defect.sink).toBe('numeric');
    // §4d recovery – the numeric column it could have bound instead.
    expect(defect.availableFields).toContain('revenue');
  });

  it('rejects a column absent from the schema – FUARAN067 (default-deny)', () => {
    const result = checkDashboard(schema, metricBoundTo('profit'));
    expect(result.ok).toBe(false);
    expect(result.defects[0]!.code).toBe('FUARAN067');
    expect(result.defects[0]!.column).toBe('profit');
  });

  it('reports a wire-decode failure as a non-ok parse error (not a throw)', () => {
    const result = checkDashboard(schema, '{ not valid wire json');
    expect(result.ok).toBe(false);
    expect(result.error).toBeTruthy();
  });
});

describe('retrievalHitSchema bridges the canonical Phase 325 contract', () => {
  it('exposes the score/title/snippet/sourceId/date columns with their types', () => {
    const hs = retrievalHitSchema();
    expect(hs.find((c) => c.name === 'score')?.type).toBe('float');
    expect(hs.find((c) => c.name === 'title')?.type).toBe('string');
    expect(hs.find((c) => c.name === 'date')?.type).toBe('date');
  });

  it('types a retrieval (case-history) dashboard through the SAME gate', () => {
    // A relevance metric bound to the retrieval `score` column – one UI contract.
    const dashboard = encodeNode(
      fuaran.metric({
        id: 'top-score',
        label: 'Relevance',
        value: binding.query('score', (row: Record<string, number>) => row.score as number),
      }),
    );
    const result = checkDashboard(retrievalHitSchema(), dashboard);
    expect(result.ok).toBe(true);
  });
});

describe('resolveAndCheck – TS orchestration over the shipped gate', () => {
  const rows = [
    { revenue: 42, category: 'books' },
    { revenue: 17, category: 'toys' },
  ];

  it('renders a well-typed dashboard against a bundled source (one resolve)', async () => {
    const source = inMemorySource(schema, rows);
    const outcome = await resolveAndCheck(
      source,
      { sql: 'select * from t', schemaOnly: false },
      metricBoundTo('revenue'),
    );
    expect(outcome.kind).toBe('render');
    if (outcome.kind === 'render') {
      expect(outcome.rows).toHaveLength(2);
    }
  });

  it('a type-mismatched dashboard is rejected before render', async () => {
    const source = inMemorySource(schema, rows);
    const outcome = await resolveAndCheck(
      source,
      { sql: 'select * from t', schemaOnly: false },
      metricBoundTo('category'),
    );
    expect(outcome.kind).toBe('typeMismatch');
    if (outcome.kind === 'typeMismatch') {
      expect(outcome.defects[0]!.code).toBe('FUARAN066');
    }
  });

  it('schema-only mode types the dashboard with no rows crossing', async () => {
    const source = inMemorySource(schema, rows);
    const outcome = await resolveAndCheck(
      source,
      { sql: 'select * from t', schemaOnly: true },
      metricBoundTo('revenue'),
    );
    expect(outcome.kind).toBe('render');
    if (outcome.kind === 'render') {
      expect(outcome.rows).toBeNull();
    }
  });
});
