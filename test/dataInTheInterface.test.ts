// Phase 1887 – the "data in the interface" showcase pages: Linked Brushing, the
// Pivot Sandbox and the Typed Refusal, exercised headlessly over the Fable output.
//
// The pages live in app/showcase/{LinkedBrushing,PivotSandbox,TypedRefusal}.fs and
// their headless surface in app/showcase/testing/DataInTheInterfaceProbes.fs. Every
// tree crosses the Fable boundary as the canonical wire the real encoder produced,
// and every row set is the shipped resolver's own resolution of the binding the page
// renders, so each claim below is about what the visitor sees, not a restatement.
//
// Four legs, one per acceptance line:
//   1. Every page's tree is canonical: it decodes through a SECOND host's strict
//      decoder (@fuaran-ui/ops) and re-encodes to the same bytes.
//   2. Each page's claim holds: selections filter the other views; a pipeline
//      evaluates, and a runaway is refused at exactly the shipped step limit;
//      a broken binding is a located, coded refusal and a sound one is not.
//   3. The expected numbers are recomputed here from the page's stated rules, so a
//      resolver that returned plausible rows of the wrong data would fail.
//   4. The publication-boundary sweep over the files this phase added.
//
// Requires `pnpm run fable:app` to have produced app/showcase/output/.

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';
import { decodeNode, encodeNode } from '@fuaran-ui/ops';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import * as P from '../app/showcase/output/testing/DataInTheInterfaceProbes.js';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

function assertCanonical(wire: string): void {
  const r = decodeNode(wire);
  if (!r.ok) {
    throw new Error(`page tree did not decode: ${r.error.code} - ${r.error.message ?? ''}`);
  }
  expect(encodeNode(r.value)).toBe(wire);
}

type Row = Record<string, string>;

/** `k=v,k=v;k=v,…` (the probes' flat form) back to rows. */
function rowsOf(resolved: string): Row[] {
  expect(resolved.startsWith('ok:'), resolved).toBe(true);
  const body = resolved.slice('ok:'.length);
  if (body === '') return [];
  return body.split(';').map((line) =>
    Object.fromEntries(
      line.split(',').map((pair) => {
        const i = pair.indexOf('=');
        return [pair.slice(0, i), pair.slice(i + 1)];
      }),
    ),
  );
}

// ─── Linked Brushing ────────────────────────────────────────────────────────

// The page's stated rule, restated independently: units = scale × product% × season%
// / 10000 (integer), revenue = units × price.
const regions: [string, number][] = [
  ['North', 120],
  ['South', 95],
  ['East', 140],
  ['West', 80],
];
const products: [string, number, number][] = [
  ['Tea', 100, 3],
  ['Coffee', 160, 4],
  ['Cocoa', 70, 5],
];
const seasons: [string, number][] = [
  ['Q1', 80],
  ['Q2', 100],
  ['Q3', 110],
  ['Q4', 140],
];
const facts = regions.flatMap(([region, scale]) =>
  products.flatMap(([product, pct, price]) =>
    seasons.map(([quarter, season]) => {
      const units = Math.trunc((scale * pct * season) / 10000);
      return { region, product, quarter, units, revenue: units * price };
    }),
  ),
);

function revenueBy(key: 'region' | 'product' | 'quarter', keep: (f: (typeof facts)[0]) => boolean) {
  const totals = new Map<string, number>();
  for (const f of facts.filter(keep)) totals.set(f[key], (totals.get(f[key]) ?? 0) + f.revenue);
  return totals;
}

describe('Phase 1887 – Linked Brushing', () => {
  it('the dashboard and each view are canonical wire', () => {
    assertCanonical(P.lbWire);
    for (const w of Array.from(P.lbViewWires as string[])) assertCanonical(w);
  });

  it('declares the coordination: Selection params on the other chart, no code', () => {
    const wire: string = P.lbWire;
    expect(wire).toContain('"$type":"Selection"');
    expect(wire).toContain('"nodeId":"lb-by-region"');
    expect(wire).toContain('"nodeId":"lb-by-product"');
    expect(wire).not.toContain('<closure>');
    expect(P.lbTableRows).toBe(48);
  });

  it('with nothing selected every view reads every row', () => {
    const byRegion = rowsOf(P.lbRows('lb-by-region', '', ''));
    expect(byRegion.map((r) => r.region)).toEqual(['East', 'North', 'South', 'West']);
    const expected = revenueBy('region', () => true);
    for (const r of byRegion) expect(Number(r.revenue)).toBe(expected.get(r.region!));
    expect(rowsOf(P.lbRows('lb-detail', '', ''))).toHaveLength(12);
    expect(rowsOf(P.lbRows('lb-by-quarter', '', ''))).toHaveLength(4);
  });

  it('a region click filters the product chart, the quarter line and the grid', () => {
    const byProduct = rowsOf(P.lbRows('lb-by-product', 'North', ''));
    const expected = revenueBy('product', (f) => f.region === 'North');
    expect(byProduct).toHaveLength(3);
    for (const r of byProduct) expect(Number(r.revenue)).toBe(expected.get(r.product!));

    const detail = rowsOf(P.lbRows('lb-detail', 'North', ''));
    expect(detail).toHaveLength(3);
    expect(detail.every((r) => r.region === 'North')).toBe(true);

    const quarters = rowsOf(P.lbRows('lb-by-quarter', 'North', ''));
    const q = revenueBy('quarter', (f) => f.region === 'North');
    for (const r of quarters) expect(Number(r.revenue)).toBe(q.get(r.quarter!));
  });

  it('a chart is not filtered by its own selection, only by the other one', () => {
    // The region chart reads the PRODUCT selection: a region click leaves it whole.
    expect(rowsOf(P.lbRows('lb-by-region', 'North', ''))).toHaveLength(4);
    const tea = rowsOf(P.lbRows('lb-by-region', '', 'Tea'));
    const expected = revenueBy('region', (f) => f.product === 'Tea');
    for (const r of tea) expect(Number(r.revenue)).toBe(expected.get(r.region!));
  });

  it('both selections narrow the grid to one cell of the cross-tab', () => {
    const detail = rowsOf(P.lbRows('lb-detail', 'East', 'Cocoa'));
    expect(detail).toHaveLength(1);
    const units = facts
      .filter((f) => f.region === 'East' && f.product === 'Cocoa')
      .reduce((s, f) => s + f.units, 0);
    expect(Number(detail[0]!.units)).toBe(units);
  });
});

// ─── the Pivot Sandbox ──────────────────────────────────────────────────────

describe('Phase 1887 – the Pivot Sandbox', () => {
  const ids: string[] = Array.from(P.psPaletteIds as string[]);
  const limit: number = P.psStepLimit;

  it('every pipeline the palette builds is canonical wire, the runaway included', () => {
    assertCanonical(P.psWire([]));
    for (const id of ids) assertCanonical(P.psWire([id]));
    assertCanonical(P.psWire(['price', 'running', 'by-region', 'sort']));
  });

  it('with no steps the grid shows the sixteen source rows', () => {
    expect(rowsOf(P.psResolve([]))).toHaveLength(16);
    expect(P.psColumns([])).toBe('region,quarter,units,revenue');
  });

  it('a pivot spreads the quarters into columns named by the data', () => {
    expect(P.psColumns(['pivot'])).toBe('region,Q1,Q2,Q3,Q4');
    const rows = rowsOf(P.psResolve(['pivot']));
    expect(rows).toHaveLength(4);
    // North at Q4: 120 × 140 / 100 = 168 units at 3.5.
    expect(Number(rows.find((r) => r.region === 'North')!.Q4)).toBe(168 * 3.5);
  });

  it('a running total and a derived column are computed, not supplied', () => {
    const rows = rowsOf(P.psResolve(['running']));
    const north = rows.filter((r) => r.region === 'North').map((r) => Number(r.running));
    expect(north).toEqual([96, 216, 348, 516].map((u) => u * 3.5));
    expect(P.psColumns(['price'])).toBe('region,quarter,units,revenue,price');
    for (const r of rowsOf(P.psResolve(['price']))) expect(Number(r.price)).toBe(3.5);
  });

  it('a step whose column an earlier step removed is refused, by the evaluator, by name', () => {
    // After the pivot there is no `revenue` column for the price to divide.
    const r: string = P.psResolve(['pivot', 'price']);
    expect(r.startsWith('error:Transform evaluation failed')).toBe(true);
    expect(r).not.toContain(P.psRefusalMarker);
    expect(r).toContain("unknown column 'revenue'");
  });

  it('the runaway is over the shipped step limit and refused before it runs', () => {
    expect(P.psRunawayLength).toBeGreaterThan(limit);
    expect(P.psStepCount(['runaway'])).toBe(P.psRunawayLength);
    const r: string = P.psResolve(['runaway']);
    expect(r.startsWith('error:' + P.psRefusalMarker)).toBe(true);
    expect(r).toContain(`pipeline steps ${P.psRunawayLength} exceeds the limit of ${limit}`);
  });

  it('the refusal is at exactly the limit: N steps run, N + 1 do not', () => {
    const at = Array.from({ length: limit }, () => 'sort');
    expect(rowsOf(P.psResolve(at))).toHaveLength(16);
    const over: string = P.psResolve([...at, 'sort']);
    expect(over.startsWith('error:' + P.psRefusalMarker)).toBe(true);
  });
});

// ─── the Typed Refusal ──────────────────────────────────────────────────────

describe('Phase 1887 – the Typed Refusal', () => {
  const check = (x: string, y: string, margin = false, byRegion = false, named = false) =>
    (P.trCheck(x, y, margin, byRegion, named) as string).split('|');

  it('every binding the page can build is canonical wire', () => {
    for (const named of [false, true])
      for (const [x, y] of [
        ['month', 'revenue'],
        ['region', 'revenu'],
        ['month', 'margin'],
      ])
        for (const margin of [false, true])
          for (const byRegion of [false, true])
            assertCanonical(P.trWire(x, y, margin, byRegion, named));
  });

  it('the initial binding is checked, finds nothing, and renders four regional totals', () => {
    const [grade, findings, produced] = check('region', 'revenue', false, true);
    expect(grade).toBe('checked');
    expect(findings).toBe('');
    expect(produced).toBe('region:string,revenue:float,units:int');
    expect(rowsOf(P.trResolve('region', 'revenue', false, true))).toHaveLength(4);
  });

  it('over the ungrouped orders the same binding reads twelve rows', () => {
    const [, findings, produced] = check('month', 'revenue');
    expect(findings).toBe('');
    expect(produced).toBe('region:string,month:string,units:int,revenue:float');
    expect(rowsOf(P.trResolve('month', 'revenue', false, false))).toHaveLength(12);
  });

  it('a misspelt value field is FUARAN086, located at the slot the author wrote', () => {
    const [grade, findings] = check('region', 'revenu', false, true);
    expect(grade).toBe('checked');
    expect(findings).toBe('FUARAN086@$.kind.yFields[0]');
  });

  it('a derived column is grounded only once the pipeline derives it', () => {
    expect(check('region', 'margin', false, true)[1]).toBe('FUARAN086@$.kind.yFields[0]');
    for (const byRegion of [false, true]) {
      const [, findings, produced] = check('region', 'margin', true, byRegion);
      expect(findings).toBe('');
      expect(produced).toContain('margin:float');
    }
  });

  it('a column the group-by removed is refused at the x slot', () => {
    const [, findings, produced] = check('month', 'revenue', false, true);
    expect(findings).toBe('FUARAN086@$.kind.xField');
    expect(produced).not.toContain('month');
  });

  it('a text column as the value is FUARAN087, a type refusal rather than a name one', () => {
    expect(check('month', 'region')[1]).toBe('FUARAN087@$.kind.yFields[0]');
  });

  it('a source only the host can resolve is unchecked, and nothing is refused', () => {
    const [grade, findings] = check('month', 'revenu', false, false, true);
    expect(grade).toBe('unchecked');
    expect(findings).toBe('');
  });

  it('over that same unseen table a group-by closes the columns, so a typo is still refused', () => {
    const [grade, findings, produced] = check('region', 'revenu', false, true, true);
    expect(grade).toBe('checked');
    expect(findings).toBe('FUARAN086@$.kind.yFields[0]');
    // The names are known; their types depend on a table nobody has seen.
    expect(produced).toBe('region:?,revenue:?,units:?');
    expect(check('region', 'revenue', false, true, true)[1]).toBe('');
  });
});

// ─── the publication-boundary sweep ─────────────────────────────────────────

describe('Phase 1887 – the new files keep to the publication boundary', () => {
  // The same two halves as the Go-sessions artefact sweep: a SHAPE check that needs
  // no list (no machine path, no off-machine URL), and the maintainers' deny-list
  // read from FUARAN_DENY_LIST, reported by index so no failure prints a pattern.
  const files = [
    'app/showcase/LinkedBrushing.fs',
    'app/showcase/PivotSandbox.fs',
    'app/showcase/TypedRefusal.fs',
    'app/showcase/testing/DataInTheInterfaceProbes.fs',
    'test/dataInTheInterface.test.ts',
  ];
  const text = files.map((f) => readFileSync(join(repoRoot, f), 'utf8')).join('\n');

  const denyPatterns = (process.env['FUARAN_DENY_LIST'] ?? '')
    .split(/[\r\n,]+/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith('#'));

  it('names no machine path and no off-machine URL', () => {
    expect(text.match(/[A-Za-z]:[\\/][A-Za-z0-9_.-]{2,}/g) ?? []).toEqual([]);
    expect(text.match(/\/(?:home|Users|root|mnt)\/[A-Za-z0-9_.-]{2,}/g) ?? []).toEqual([]);
    expect(text.match(/https?:\/\/[^"'\s\\]+/g) ?? []).toEqual([]);
  });

  it.skipIf(denyPatterns.length === 0)('carries nothing the configured deny-list names', () => {
    const lower = text.toLowerCase();
    const hits = denyPatterns
      .map((pattern, index) => ({ index, hit: new RegExp(pattern, 'i').test(lower) }))
      .filter((row) => row.hit)
      .map((row) => `deny-list entry #${row.index + 1}`);
    expect(hits).toEqual([]);
  });
});
