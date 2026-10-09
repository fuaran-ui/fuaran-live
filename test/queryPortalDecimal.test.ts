// Phase 2143 – a decimal column round-trips through the query bridge. The tier
// carries a decimal cell as its canonical text (a JSON string, never a number), so
// "decimal in, the same decimal value out" means the same canonical text back out
// of `refineLocally`. Before the bridge matched `Decimal`, `cellOf` fell to `Null`
// and the column came back all-null.

import { describe, it, expect } from 'vitest';
import { fuaran } from '@fuaran-ui/ui';
import { encodeNode } from '@fuaran-ui/ops';

import { tryLocalRefine } from '../src/query-portal/refine';
import type { ColumnarResult } from '../src/query-portal/sources';
import type { ResultSchema } from '../src/query-portal/core';

// The portal's TS `ColumnType` union predates the `decimal` tag; the F# schema
// parser accepts it, so the fixture states the schema the bridge actually reads.
const schema = [
  { name: 'price', type: 'decimal' },
  { name: 'label', type: 'string' },
] as unknown as ResultSchema;

const current: ColumnarResult = {
  schema,
  rows: [
    { price: '12.50', label: 'b' },
    { price: '0.1', label: 'a' },
    { price: '-7', label: 'c' },
    { price: 3, label: 'd' },
    { price: 'not a number', label: 'e' },
  ],
};

// A dashboard that binds no query column: the refinement keeps its schema and types.
const dashboard = encodeNode(fuaran.metric({ id: 'm1', label: 'Fixed', value: 1 }));

const sortBy = (col: string) => JSON.stringify([{ $type: 'sort', by: [{ col, dir: 'asc' }] }]);

describe('the query bridge – decimal cells', () => {
  it('round-trips decimal text exactly (canonicalised), widens an int, nulls non-decimal text', () => {
    const out = tryLocalRefine(current, sortBy('label'), dashboard);
    expect(out.kind).toBe('refined');
    if (out.kind !== 'refined') return;
    expect(out.schema).toContainEqual({ name: 'price', type: 'decimal' });
    const byLabel = Object.fromEntries(out.rows.map((r) => [r.label as string, r.price]));
    expect(byLabel['b']).toBe('12.5'); // 12.50 is canonicalised to 12.5
    expect(byLabel['a']).toBe('0.1');
    expect(byLabel['c']).toBe('-7');
    expect(byLabel['d']).toBe('3'); // Int -> Decimal is the lossless promotion
    expect(byLabel['e']).toBeNull(); // not decimal text: best-effort Null, as every other type
  });
});
