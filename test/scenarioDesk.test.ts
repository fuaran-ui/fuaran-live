// Phase 2759 – the Scenario Desk, exercised headlessly over the Fable output.
//
// The page lives in app/showcase/ScenarioDesk.fs and its headless surface in
// app/showcase/testing/ScenarioDeskProbes.fs. The desk is the published fragment
// library's parameter panel, two-scenario comparison and refinable table, plus two
// totals that call the same model.
//
// Five legs, one per acceptance line:
//   1. Each parameter is read by exactly the sites the readout names, from the tier's
//      read walk; and moving it changes those sites' values and holds every other
//      site's value still, so the read sets are not merely asserted.
//   2. The comparison's figures are recomputed here from the ten lines, so a model and
//      a resolver that agreed on the wrong arithmetic would still fail.
//   3. A copied view pasted back gives the rows the table shows, byte for byte, for
//      every view of a grid of sorts, category selections and pages; a stale view does
//      not, and a pipeline that still reads a parameter is refused.
//   4. The desk's tree is canonical in a second host, and the empty states say what
//      the page holds, with no placeholder wording.
//   5. The publication-boundary sweep over the files this phase added.
//
// Requires `pnpm run fable:app` to have produced app/showcase/output/.

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';
import { decodeNode, encodeNode } from '@fuaran-ui/ops';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import * as P from '../app/showcase/output/testing/ScenarioDeskProbes.js';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

// The ten lines, restated independently of the page: item, category, units, price.
const lines: [string, string, number, number][] = [
  ['Sourdough', 'Bread', 120, 4.5],
  ['Rye', 'Bread', 60, 4.0],
  ['Baguette', 'Bread', 150, 2.2],
  ['Focaccia', 'Bread', 45, 3.8],
  ['Croissant', 'Pastry', 200, 1.9],
  ['Pain au chocolat', 'Pastry', 140, 2.1],
  ['Cinnamon bun', 'Pastry', 90, 2.6],
  ['Scone', 'Bake', 80, 1.8],
  ['Flapjack', 'Bake', 70, 1.5],
  ['Brownie', 'Bake', 110, 2.4],
];

const keys: string[] = Array.from(P.parameterKeys as string[]);
const readers = (key: string): string[] => Array.from(P.readers(key) as string[]);

/** `id=value` lines → a map. */
function siteValues(filters: Record<string, unknown>): Map<string, string> {
  const text = P.siteValues(JSON.stringify(filters)) as string;
  return new Map(
    text.split('\n').map((line) => {
      const at = line.indexOf('=');
      return [line.slice(0, at), line.slice(at + 1)] as [string, string];
    }),
  );
}

describe('Phase 2759 – the desk is built from the fragment library', () => {
  it('builds', () => {
    expect(P.deskBuilt).toBe('ok');
  });

  it('moves three parameters', () => {
    expect(keys).toEqual(['a-growth', 'b-growth', 'weeks']);
  });
});

describe('Phase 2759 – one parameter dirties only its readers', () => {
  it('names the readers of each parameter from the read walk', () => {
    expect(readers('a-growth')).toEqual([
      'desk-comparison-grid',
      'desk-total-a',
      'parameter-panel-controls',
    ]);
    expect(readers('b-growth')).toEqual([
      'desk-comparison-grid',
      'desk-total-b',
      'parameter-panel-controls',
    ]);
    expect(readers('weeks')).toEqual([
      'desk-comparison-grid',
      'desk-total-a',
      'desk-total-b',
      'parameter-panel-controls',
    ]);
  });

  it('the table reads none of them, and no reader is opaque to the walk', () => {
    const none = Array.from(P.noParameterReaders as string[]);
    expect(none).toContain('desk-lines-grid');
    for (const site of ['desk-comparison-grid', 'desk-total-a', 'desk-total-b']) {
      expect(none).not.toContain(site);
    }
    expect(Array.from(P.opaqueReaders as string[])).toEqual([]);
  });

  const moves: Record<string, number> = { 'a-growth': 0.3, 'b-growth': -0.1, weeks: 9 };
  const valueSites = ['desk-total-a', 'desk-total-b', 'desk-comparison-grid', 'desk-lines-grid'];

  for (const key of ['a-growth', 'b-growth', 'weeks']) {
    it(`moving ${key} changes exactly the value sites that read it`, () => {
      const before = siteValues({});
      const after = siteValues({ [key]: moves[key] });
      for (const site of valueSites) {
        expect(before.get(site)!.startsWith('error'), `${site}: ${before.get(site)}`).toBe(false);
        if (readers(key).includes(site)) {
          expect(after.get(site), `${site} reads ${key}`).not.toBe(before.get(site));
        } else {
          expect(after.get(site), `${site} does not read ${key}`).toBe(before.get(site));
        }
      }
    });
  }
});

describe('Phase 2759 – the scenarios are the model the page states', () => {
  const takings = (units: number, price: number, growth: number, weeks: number) =>
    units * (1 + growth) * price * weeks;

  it('starts at steady 0%, busy season 15%, four weeks, and the figures follow', () => {
    const values = siteValues({});
    const sumA = lines.reduce((s, [, , u, p]) => s + takings(u, p, 0.0, 4), 0);
    const sumB = lines.reduce((s, [, , u, p]) => s + takings(u, p, 0.15, 4), 0);
    expect(Number(values.get('desk-total-a'))).toBeCloseTo(sumA, 6);
    expect(Number(values.get('desk-total-b'))).toBeCloseTo(sumB, 6);

    const grid = JSON.parse(values.get('desk-comparison-grid')!) as Record<string, unknown>[];
    expect(grid).toHaveLength(lines.length);
    for (const [i, [item, , u, p]] of lines.entries()) {
      const row = grid[i]!;
      expect(row['item']).toBe(item);
      expect(Number(row['scenario-a'])).toBeCloseTo(takings(u, p, 0.0, 4), 9);
      expect(Number(row['scenario-b'])).toBeCloseTo(takings(u, p, 0.15, 4), 9);
      expect(Number(row['difference'])).toBeCloseTo(
        takings(u, p, 0.15, 4) - takings(u, p, 0.0, 4),
        9,
      );
    }
  });

  it('a moved parameter reaches the comparison through the same model', () => {
    const grid = JSON.parse(
      siteValues({ 'a-growth': 0.2, 'b-growth': 0.4, weeks: 2 }).get('desk-comparison-grid')!,
    ) as Record<string, unknown>[];
    const [, , u, p] = lines[0]!;
    expect(Number(grid[0]!['scenario-a'])).toBeCloseTo(takings(u, p, 0.2, 2), 9);
    expect(Number(grid[0]!['scenario-b'])).toBeCloseTo(takings(u, p, 0.4, 2), 9);
  });
});

// ─── the copied view ────────────────────────────────────────────────────────

describe('Phase 2759 – a pasted view reproduces the table', () => {
  const sorts: ({ column: number; direction: string } | null)[] = [null];
  for (let column = 0; column < 4; column++) {
    for (const direction of ['asc', 'desc']) sorts.push({ column, direction });
  }
  const selections: (string[] | null)[] = [null, [], ['Bread'], ['Pastry', 'Bake']];
  const pages: (number | null)[] = [null, 1, 2, 3, 9];

  const stateOf = (sort: (typeof sorts)[number], page: number | null) => {
    const state: Record<string, unknown> = {};
    if (sort) state['desk-lines-sort'] = sort;
    if (page !== null) state['desk-lines-page'] = { page };
    return JSON.stringify(state);
  };
  const filtersOf = (selected: string[] | null) =>
    JSON.stringify(selected === null ? {} : { 'desk-lines-filter-category': selected });

  it('byte for byte, for every view of the grid', () => {
    // The fixed pipeline does read one, so the check below can bite.
    expect(P.fixedPipelineWire as string).toContain('"param":');
    let compared = 0;
    for (const sort of sorts) {
      for (const selected of selections) {
        for (const page of pages) {
          const state = stateOf(sort, page);
          const filters = filtersOf(selected);
          const shown = P.shownWire(state, filters) as string;
          const copied = P.copiedView(state, filters) as string;
          expect(shown.startsWith('error'), shown).toBe(false);
          expect(copied.startsWith('error'), copied).toBe(false);
          // The copy reads no parameter: every value of the view is written in.
          expect(copied).not.toContain('"param":');
          expect(P.pastedWire(copied), `${state} ${filters}`).toBe(shown);
          compared++;
        }
      }
    }
    expect(compared).toBe(9 * 4 * 5);
  });

  it('the shown rows are the view: sorted, filtered and paged as asked', () => {
    const shown = JSON.parse(
      P.shownWire(stateOf({ column: 3, direction: 'desc' }, 1), filtersOf(['Pastry', 'Bake'])),
    ) as Record<string, unknown>[];
    const expected = lines
      .filter(([, c]) => c === 'Pastry' || c === 'Bake')
      .sort((a, b) => b[3] - a[3])
      .slice(0, 4)
      .map(([item]) => item);
    expect(shown.map((r) => r['item'])).toEqual(expected);
  });

  it('a stale view gives a different table – the comparison is not vacuous', () => {
    const filters = filtersOf(['Bread']);
    const page1 = P.copiedView(stateOf({ column: 2, direction: 'asc' }, 1), filters) as string;
    const shownDesc = P.shownWire(stateOf({ column: 2, direction: 'desc' }, 1), filters) as string;
    expect(P.pastedWire(page1)).not.toBe(shownDesc);
  });

  it('refuses a pipeline that still reads a parameter, and text that is no pipeline', () => {
    const withParam = P.fixedPipelineWire as string;
    const r = P.pastedWire(withParam) as string;
    expect(r.startsWith('error:')).toBe(true);
    expect((P.pastedWire('not json') as string).startsWith('error:')).toBe(true);
  });
});

describe('Phase 2759 – the tree is canonical and the empty states are real', () => {
  it('the desk decodes in a second host and re-encodes to itself', () => {
    const wire = P.deskWire as string;
    const r = decodeNode(wire);
    if (!r.ok) {
      throw new Error(`the desk did not decode: ${r.error.code} - ${r.error.message ?? ''}`);
    }
    expect(encodeNode(r.value)).toBe(wire);
    expect(wire).toContain('"$type":"Transform"');
    expect(wire).toContain('Pain au chocolat');
  });

  it('says what the page holds before anything moves, with no placeholder wording', () => {
    expect(P.emptyReadoutHeading).toBe('Nothing moved yet');
    const body = P.emptyReadoutBody as string;
    expect(body).toContain('3 parameters');
    expect(body).toContain('15% up');
    expect(body).toContain('4 weeks');
    expect(P.whatIfContrast).toContain('What-If Machine');
    for (const text of [body, P.emptyPasteNote as string, P.whatIfContrast as string]) {
      expect(text).not.toMatch(/todo|coming soon|lorem|placeholder|tbd/i);
    }
  });
});

// ─── the publication-boundary sweep ─────────────────────────────────────────

describe('Phase 2759 – the new files keep to the publication boundary', () => {
  // The same two halves as the other page sweeps: a SHAPE check that needs no list
  // (no machine path, no off-machine URL), and the maintainers' deny-list read from
  // FUARAN_DENY_LIST, reported by index so no failure prints a pattern.
  const files = [
    'app/showcase/ScenarioDesk.fs',
    'app/showcase/testing/ScenarioDeskProbes.fs',
    'test/scenarioDesk.test.ts',
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
