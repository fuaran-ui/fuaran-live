// Phase 2756 – the Show Your Working page, exercised headlessly over the Fable output.
//
// The page lives in app/showcase/ShowYourWorking.fs and its headless surface in
// app/showcase/testing/ShowYourWorkingProbes.fs. Every figure on the page is a Metric
// whose value is a pipeline over twelve inline orders; the stepped view is the language
// tier's derivation trace of that binding.
//
// Four legs, one per acceptance line:
//   1. The last step's table equals the value on screen, for EVERY figure: the trace's
//      final table is one row and one column, and its cell is the number the renderer
//      resolves for the slot, as a number and through the figure's own format. Read
//      twice, from the trace value and from the trace's canonical wire text parsed here.
//   2. The numbers are recomputed here from the twelve orders, so a trace and a resolver
//      that agreed on the wrong data would still fail.
//   3. Every figure's node is canonical: it decodes through a SECOND host's strict decoder
//      (@fuaran-ui/ops) and re-encodes to the same bytes; and the empty state says what
//      the page holds, with no placeholder wording.
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
import * as P from '../app/showcase/output/testing/ShowYourWorkingProbes.js';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

const ids: string[] = Array.from(P.figureIds as string[]);

// The twelve orders, restated independently of the page.
const orders: [string, string, number, number][] = [
  ['North', 'Tea', 24, 3.5],
  ['North', 'Coffee', 12, 4.25],
  ['North', 'Cocoa', 6, 5.0],
  ['South', 'Tea', 18, 3.5],
  ['South', 'Coffee', 30, 4.25],
  ['South', 'Cocoa', 9, 5.0],
  ['East', 'Tea', 40, 3.5],
  ['East', 'Coffee', 22, 4.25],
  ['East', 'Cocoa', 15, 5.0],
  ['West', 'Tea', 8, 3.5],
  ['West', 'Coffee', 16, 4.25],
  ['West', 'Cocoa', 20, 5.0],
];
const revenueOf = (rows: typeof orders) => rows.reduce((s, [, , u, p]) => s + u * p, 0);
const unitsOf = (rows: typeof orders) => rows.reduce((s, [, , u]) => s + u, 0);
const regions = ['North', 'South', 'East', 'West'];
const inRegion = (r: string) => orders.filter(([region]) => region === r);

const expected: Record<string, number> = {
  revenue: revenueOf(orders),
  'large-orders': orders.filter(([, , u]) => u >= (P.largeOrderUnits as number)).length,
  'average-order': revenueOf(orders) / orders.length,
  'best-region': Math.max(...regions.map((r) => revenueOf(inRegion(r)))),
};
for (const r of regions) {
  expected[`${r.toLowerCase()}-units`] = unitsOf(inRegion(r));
  expected[`${r.toLowerCase()}-revenue`] = revenueOf(inRegion(r));
}

function screenNumber(id: string): number {
  const v = P.screenValue(id) as string;
  expect(v.startsWith('ok:'), `${id}: ${v}`).toBe(true);
  return Number(v.slice('ok:'.length));
}

/** The lone cell of a trace wire's last table, read from the JSON alone. */
function lastCellOfWire(wire: string): { rowCount: number; columns: string[]; cell: unknown } {
  const trace = JSON.parse(wire) as {
    steps: {
      table?: { rowCount: number; rows: { columns: Record<string, { values: unknown[] }> } };
    }[];
  };
  const last = trace.steps[trace.steps.length - 1];
  expect(last?.table, 'the last step carries its table').toBeDefined();
  const table = last!.table!;
  const columns = Object.keys(table.rows.columns);
  return { rowCount: table.rowCount, columns, cell: table.rows.columns[columns[0]!]!.values[0] };
}

describe('Phase 2756 – the last step’s table is the value on screen', () => {
  it('covers every number the page draws: four headline figures and eight cells', () => {
    expect(ids.filter((id) => P.group(id) === 'headline')).toHaveLength(4);
    expect(ids.filter((id) => P.group(id) === 'cell')).toHaveLength(8);
    expect(new Set(ids).size).toBe(ids.length);
  });

  for (const id of ids) {
    it(`${id}: the trace ends on one cell holding the resolved value`, () => {
      const onScreen = screenNumber(id);
      expect(P.traceFinal(id)).toMatch(/^rows=1;cols=[a-z]+;cell=/);
      const cell = Number((P.traceFinal(id) as string).split('cell=')[1]);
      expect(cell).toBe(onScreen);
      // The same comparison through the figure's own format: the text on the page.
      expect(P.traceFinalText(id)).toBe(P.screenText(id));
    });

    it(`${id}: the trace's canonical wire ends on the same cell`, () => {
      const { rowCount, columns, cell } = lastCellOfWire(P.traceWire(id) as string);
      expect(rowCount).toBe(1);
      expect(columns).toHaveLength(1);
      expect(Number(cell)).toBe(screenNumber(id));
    });

    it(`${id}: the number is the one the orders give`, () => {
      expect(screenNumber(id)).toBeCloseTo(expected[id]!, 9);
    });
  }

  it('names each step by its wire verb, and the best region walks five of them', () => {
    expect(P.traceVerbs('best-region')).toBe('derive,groupBy,sort,limit,project');
    expect(P.traceVerbs('north-units')).toBe('filter,groupBy');
    expect(P.traceVerbs('north-revenue')).toBe('derive,filter,groupBy');
  });
});

describe('Phase 2756 – the trees are canonical and the empty state is real', () => {
  for (const id of ids) {
    it(`${id}: its node decodes in a second host and re-encodes to itself`, () => {
      const wire = P.nodeWire(id) as string;
      const r = decodeNode(wire);
      if (!r.ok) {
        throw new Error(`${id} did not decode: ${r.error.code} - ${r.error.message ?? ''}`);
      }
      expect(encodeNode(r.value)).toBe(wire);
      // The data and the pipeline are both in the node: there is no other source.
      expect(wire).toContain('"$type":"Transform"');
      expect(wire).toContain('A-112');
    });
  }

  it('the empty state says what the page holds and what a click will show', () => {
    const body = P.emptyStateBody as string;
    expect(P.emptyStateHeading).toBe('Pick any number above');
    expect(body).toContain('4 headline figures');
    expect(body).toContain('8 cells');
    expect(body).toContain('12 orders');
    for (const text of [body, P.emptyWireNote as string]) {
      expect(text).not.toMatch(/todo|coming soon|lorem|placeholder|tbd/i);
    }
  });

  it('before a click, the wire panel shows the orders every figure reads', () => {
    const wire = JSON.parse(P.ordersWire as string) as {
      columns: Record<string, { values: unknown[] }>;
    };
    expect(Object.keys(wire.columns).sort()).toEqual([
      'order',
      'price',
      'product',
      'region',
      'units',
    ]);
    expect(wire.columns['units']!.values).toEqual(orders.map(([, , u]) => u));
  });
});

// ─── the publication-boundary sweep ─────────────────────────────────────────

describe('Phase 2756 – the new files keep to the publication boundary', () => {
  // The same two halves as the other page sweeps: a SHAPE check that needs no list
  // (no machine path, no off-machine URL), and the maintainers' deny-list read from
  // FUARAN_DENY_LIST, reported by index so no failure prints a pattern.
  const files = [
    'app/showcase/ShowYourWorking.fs',
    'app/showcase/testing/ShowYourWorkingProbes.fs',
    'test/showYourWorking.test.ts',
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
