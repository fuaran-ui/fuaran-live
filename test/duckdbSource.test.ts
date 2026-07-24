// Phase 324, task 2 – the DuckDB-WASM `IClientQuerySource` driver. The real
// engine is browser/WASM-bound and operator-verified; here the pure mapping logic
// (DuckDB type → ColumnType, schema-only zero-row probe, rows passthrough, error
// mapping) is exercised against a fake `IDuckDbConn`.

import { describe, it, expect } from 'vitest';
import {
  duckdbSource,
  duckdbTypeToColumnType,
  schemaOf,
  type DuckDbResult,
  type IDuckDbConn,
} from '../src/query-portal/duckdbSource';
import type { ResultRow } from '../src/query-portal/sources';

/** A fake connection that returns a fixed result and records the SQL it saw. */
function fakeConn(
  columns: { name: string; sqlType: string }[],
  rows: ResultRow[],
): IDuckDbConn & { lastSql: string | null } {
  const state = { lastSql: null as string | null };
  return {
    get lastSql() {
      return state.lastSql;
    },
    async query(sql: string): Promise<DuckDbResult> {
      state.lastSql = sql;
      return { columns, toRows: () => rows };
    },
  };
}

describe('duckdbTypeToColumnType', () => {
  it('maps the DuckDB scalar types', () => {
    expect(duckdbTypeToColumnType('BOOLEAN')).toBe('bool');
    expect(duckdbTypeToColumnType('BIGINT')).toBe('int');
    expect(duckdbTypeToColumnType('UINTEGER')).toBe('int');
    expect(duckdbTypeToColumnType('DOUBLE')).toBe('float');
    expect(duckdbTypeToColumnType('DECIMAL(10,2)')).toBe('float');
    expect(duckdbTypeToColumnType('VARCHAR')).toBe('string');
    expect(duckdbTypeToColumnType('DATE')).toBe('date');
    expect(duckdbTypeToColumnType('TIMESTAMP')).toBe('timestamp');
    expect(duckdbTypeToColumnType('TIMESTAMP WITH TIME ZONE')).toBe('timestamp');
  });
});

describe('schemaOf', () => {
  it('projects typed columns into a ResultSchema', () => {
    const result: DuckDbResult = {
      columns: [
        { name: 'revenue', sqlType: 'DOUBLE' },
        { name: 'category', sqlType: 'VARCHAR' },
      ],
      toRows: () => [],
    };
    expect(schemaOf(result)).toEqual([
      { name: 'revenue', type: 'float' },
      { name: 'category', type: 'string' },
    ]);
  });
});

describe('duckdbSource resolve', () => {
  const columns = [
    { name: 'revenue', sqlType: 'DOUBLE' },
    { name: 'category', sqlType: 'VARCHAR' },
  ];
  const rows: ResultRow[] = [
    { revenue: 42.5, category: 'books' },
    { revenue: 17, category: 'toys' },
  ];

  it('returns schema + rows in full-fetch mode', async () => {
    const conn = fakeConn(columns, rows);
    const src = duckdbSource(conn);
    const out = await src.resolve({ sql: 'select * from data', schemaOnly: false });
    expect(out.ok).toBe(true);
    if (out.ok) {
      expect(out.value.schema).toContainEqual({ name: 'revenue', type: 'float' });
      expect(out.value.rows).toHaveLength(2);
    }
    expect(conn.lastSql).toBe('select * from data');
  });

  it('schema-only mode returns no rows and wraps the SQL in a zero-row probe', async () => {
    const conn = fakeConn(columns, rows);
    const src = duckdbSource(conn);
    const out = await src.resolve({ sql: 'select * from data;', schemaOnly: true });
    expect(out.ok).toBe(true);
    if (out.ok) {
      expect(out.value.rows).toBeNull();
      expect(out.value.schema).toHaveLength(2);
    }
    // The probe wraps the (semicolon-stripped) query in WHERE false – no data fetched.
    expect(conn.lastSql).toBe('SELECT * FROM (select * from data) WHERE false');
  });

  it('schema-only with zeroRowProbe off runs the raw SQL but still drops rows', async () => {
    const conn = fakeConn(columns, rows);
    const src = duckdbSource(conn, { zeroRowProbe: false });
    const out = await src.resolve({ sql: 'select * from data', schemaOnly: true });
    expect(out.ok).toBe(true);
    if (out.ok) expect(out.value.rows).toBeNull();
    expect(conn.lastSql).toBe('select * from data');
  });

  it('maps a DuckDB query rejection to a typed failed error', async () => {
    const conn: IDuckDbConn = {
      async query() {
        throw new Error('Parser Error: syntax error at "frmo"');
      },
    };
    const src = duckdbSource(conn);
    const out = await src.resolve({ sql: 'select * frmo data', schemaOnly: false });
    expect(out.ok).toBe(false);
    if (!out.ok) {
      expect(out.error.kind).toBe('failed');
      expect(out.error.kind === 'failed' && out.error.detail).toContain('Parser Error');
    }
  });
});
