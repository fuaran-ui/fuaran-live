// Phase 2395 – the Undo Grid showcase page, exercised headlessly over the Fable output.
//
// The page lives in app/showcase/UndoGrid.fs and its headless surface in
// app/showcase/testing/UndoGridProbes.fs. A session is a list of gestures run through
// the page's own `step` from its own `initial`, so every claim below is about the model
// the page renders, not a restatement of it.
//
// Four legs, one per claim the page makes:
//   1. An edit is one op naming the cell; undo appends the inverse, redo the op again,
//      and a new edit clears what could be redone.
//   2. The record replays: the first k records rebuild the table the first k gestures
//      left, and all of them rebuild the live table, past the log's bound too.
//   3. A gesture the table cannot take is refused in the substrate's terms and leaves
//      the table and the record untouched.
//   4. The payload the page shows is the published codec's canonical form, and the
//      strict decoder takes it back byte for byte.
// Then the publication-boundary sweep over the files this phase added.
//
// Requires `pnpm run fable:app` to have produced app/showcase/output/.

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import * as P from '../app/showcase/output/testing/UndoGridProbes.js';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

// The page's inline table, restated independently.
type Stock = { sku: string; item: string; qty: number; price: number };
const start: Stock[] = [
  { sku: 'S-01', item: 'Tomato seeds', qty: 40, price: 250 },
  { sku: 'S-02', item: 'Basil seeds', qty: 25, price: 180 },
  { sku: 'S-03', item: 'Sweet pea seeds', qty: 18, price: 320 },
  { sku: 'S-04', item: 'Runner bean seeds', qty: 30, price: 275 },
  { sku: 'S-05', item: 'Sunflower seeds', qty: 12, price: 199 },
];

function flat(rows: Stock[]): string {
  return rows.map((r) => `sku=${r.sku},item=${r.item},qty=${r.qty},price=${r.price}`).join(';');
}

function edited(
  sku: string,
  column: 'qty' | 'price',
  value: number,
  rows: Stock[] = start,
): Stock[] {
  return rows.map((r) => (r.sku === sku ? { ...r, [column]: value } : r));
}

type SetCell = { $type: string; col: string; row: number; value: { $type: string; v: number } };

const table = (g: string[]): string => P.ugTable(g);
const ops = (g: string[]): SetCell[] => (P.ugRecordOps(g) as string[]).map((o) => JSON.parse(o));

describe('Phase 2395 – an edit is one op, and undo and redo come from the log', () => {
  it('starts from the inline table with an empty record', () => {
    expect(table([])).toBe(flat(start));
    expect(P.ugRecordCount([])).toBe(0);
    expect(P.ugFlags([])).toBe('undo=false,redo=false');
    expect(P.ugLastPayload([])).toBe('');
  });

  it('records a cell edit as one setCell naming the row the key resolves to', () => {
    const session = ['edit:S-02:qty:12'];
    expect(table(session)).toBe(flat(edited('S-02', 'qty', 12)));
    expect(ops(session)).toEqual([
      { $type: 'setCell', col: 'qty', row: 1, value: { $type: 'Int', v: 12 } },
    ]);
    expect(P.ugRecordLines(session)).toEqual(['set qty of row 1 to 12']);
    expect(P.ugFlags(session)).toBe('undo=true,redo=false');
  });

  it('undo APPENDS the inverse rather than deleting the edit, and restores the table', () => {
    const session = ['edit:S-02:qty:12', 'undo'];
    expect(table(session)).toBe(flat(start));
    expect(P.ugRecordLines(session)).toEqual(['set qty of row 1 to 12', 'set qty of row 1 to 25']);
    expect(P.ugFlags(session)).toBe('undo=false,redo=true');
  });

  it('redo applies the op again, and appends it again', () => {
    const session = ['edit:S-02:qty:12', 'undo', 'redo'];
    expect(table(session)).toBe(flat(edited('S-02', 'qty', 12)));
    expect(P.ugRecordCount(session)).toBe(3);
    expect(P.ugFlags(session)).toBe('undo=true,redo=false');
  });

  it('undoes in reverse order across rows and columns', () => {
    const session = ['edit:S-02:qty:12', 'edit:S-05:price:210', 'undo'];
    expect(table(session)).toBe(flat(edited('S-02', 'qty', 12)));
    expect(table([...session, 'undo'])).toBe(flat(start));
    expect(P.ugRefusal([...session, 'undo', 'undo'])).toBe('Nothing to undo.');
  });

  it('a new edit after an undo clears what could be redone', () => {
    const session = ['edit:S-02:qty:12', 'undo', 'edit:S-03:qty:20'];
    expect(P.ugFlags(session)).toBe('undo=true,redo=false');
    expect(P.ugRefusal([...session, 'redo'])).toBe('Nothing to redo.');
    expect(table(session)).toBe(flat(edited('S-03', 'qty', 20)));
  });
});

describe('Phase 2395 – the record replays', () => {
  const session = [
    'edit:S-01:qty:44',
    'edit:S-04:price:300',
    'undo',
    'edit:S-02:qty:9',
    'undo',
    'redo',
    'edit:S-05:qty:1',
  ];

  it('each prefix of the record rebuilds the table that prefix of gestures left', () => {
    // Every gesture in this session applies, and each appends exactly one record.
    expect(P.ugRecordCount(session)).toBe(session.length);
    for (let k = 0; k <= session.length; k++) {
      expect(P.ugReplayTo(session, k), `after ${k} ops`).toBe(table(session.slice(0, k)));
    }
  });

  it('the whole record rebuilds the live table, and the chain is intact', () => {
    expect(P.ugReplayTo(session, session.length)).toBe(table(session));
    expect(P.ugIntegrity(session)).toBe('intact');
  });

  it('past the bound the oldest ops fold into the base, and the rest still replays', () => {
    // 105 edits against a bound of 100: the record keeps the newest 100.
    const long = Array.from({ length: 105 }, (_, i) => `edit:S-03:qty:${i + 1}`);
    expect(P.ugRecordCount(long)).toBe(100);
    expect(P.ugReplayTo(long, 100)).toBe(table(long));
    expect(P.ugReplayTo(long, 0)).toBe(flat(edited('S-03', 'qty', 5)));
    expect(P.ugIntegrity(long)).toBe('intact');
  });
});

describe('Phase 2395 – a gesture the table cannot take is refused, and changes nothing', () => {
  const before = ['edit:S-02:qty:12'];

  it.each([
    [
      'a fractional quantity',
      'edit:S-02:qty:2.5',
      "Refused: column 'qty' holds int, and the edit carried float.",
    ],
    [
      'text in a number column',
      'edit:S-02:qty:"lots"',
      "Refused: column 'qty' holds int, and the edit carried string.",
    ],
    ['a key no row has', 'edit:S-99:qty:1', "Refused: no row has sku 'S-99'."],
  ])('%s', (_label, gesture, why) => {
    const session = [...before, gesture];
    expect(P.ugRefusal(session)).toBe(why);
    expect(table(session)).toBe(table(before));
    expect(P.ugRecordCount(session)).toBe(1);
    expect(P.ugFlags(session)).toBe('undo=true,redo=false');
  });

  it('the next gesture that applies clears the refusal', () => {
    expect(P.ugRefusal([...before, 'edit:S-02:qty:2.5', 'undo'])).toBe('');
  });
});

describe('Phase 2395 – the payload is the published codec’s canonical form', () => {
  it('names the State key, the row-identity column and the op', () => {
    expect(JSON.parse(P.ugLastPayload(['edit:S-02:qty:12']))).toEqual({
      $type: 'GridEdit',
      key: 'undo-grid.stock',
      op: { $type: 'setCell', col: 'qty', row: 1, value: { $type: 'Int', v: 12 } },
      rowKeyField: 'sku',
    });
  });

  it('an undo’s payload is an edit carrying the inverse op', () => {
    expect(JSON.parse(P.ugLastPayload(['edit:S-02:qty:12', 'undo'])).op).toEqual({
      $type: 'setCell',
      col: 'qty',
      row: 1,
      value: { $type: 'Int', v: 25 },
    });
  });

  it('the strict decoder takes every shown payload back byte for byte', () => {
    for (const session of [
      ['edit:S-02:qty:12'],
      ['edit:S-02:qty:12', 'undo'],
      ['edit:S-01:price:999'],
    ]) {
      const payload: string = P.ugLastPayload(session);
      expect(P.ugPayloadRoundTrip(payload)).toBe(payload);
    }
  });

  it('the strict decoder refuses a payload carrying a member it would drop', () => {
    const payload = JSON.parse(P.ugLastPayload(['edit:S-02:qty:12']));
    const widened = JSON.stringify({ ...payload, note: 'extra' });
    expect(P.ugPayloadRoundTrip(widened)).toBe("error:GridEdit: unknown member 'note'");
  });

  it('the grid is handed the live table’s numbers as numbers', () => {
    expect(P.ugGridRows(['edit:S-02:qty:12']).split(';')[1]).toBe(
      'item=Basil seeds,price=180,qty=12,sku=S-02',
    );
  });
});

describe('Phase 2395 – the new files keep to the publication boundary', () => {
  // The same two halves as the Phase 1887 sweep: a SHAPE check that needs no list (no
  // machine path, no off-machine URL), and the maintainers' deny-list read from
  // FUARAN_DENY_LIST, reported by index so no failure prints a pattern.
  const files = [
    'app/showcase/UndoGrid.fs',
    'app/showcase/testing/UndoGridProbes.fs',
    'test/undoGrid.test.ts',
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
