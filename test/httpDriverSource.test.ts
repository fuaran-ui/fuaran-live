// Phase 324, task 1 – the serverless-HTTP `IClientQuerySource` driver, exercised
// headlessly with an injected `fetch` stub (no live endpoint). Proves: the
// read-only-by-default guard, the typed (Neon / libSQL) + inferred (generic)
// schema paths, error mapping to typed `QuerySourceError`, and the schema-only
// privacy projection.

import { describe, it, expect } from 'vitest';
import {
  httpDriverSource,
  genericRowsAdapter,
  neonHttpAdapter,
  libsqlHttpAdapter,
  isReadOnlySql,
  inferSchema,
  type FetchLike,
  type HttpSourceConfig,
} from '../src/query-portal/httpDriverSource';

const config: HttpSourceConfig = { endpoint: 'https://db.example/query', token: 'ro-token' };

/** A `fetch` stub that returns `payload` as JSON with status 200. */
const okFetch =
  (payload: unknown): FetchLike =>
  async () => ({
    ok: true,
    status: 200,
    statusText: 'OK',
    json: async () => payload,
    text: async () => JSON.stringify(payload),
  });

/** A `fetch` stub that returns a non-2xx with a body. */
const errFetch =
  (status: number, statusText: string, body: string): FetchLike =>
  async () => ({
    ok: false,
    status,
    statusText,
    json: async () => ({}),
    text: async () => body,
  });

/** A `fetch` stub that rejects (network down). */
const downFetch = (): FetchLike => async () => {
  throw new Error('network unreachable');
};

describe('isReadOnlySql', () => {
  it('accepts SELECT / WITH / EXPLAIN, including after a leading comment', () => {
    expect(isReadOnlySql('SELECT * FROM t')).toBe(true);
    expect(isReadOnlySql('  with x as (select 1) select * from x')).toBe(true);
    expect(isReadOnlySql('-- a note\nSELECT 1')).toBe(true);
  });
  it('rejects INSERT / UPDATE / DELETE / DROP', () => {
    expect(isReadOnlySql('INSERT INTO t VALUES (1)')).toBe(false);
    expect(isReadOnlySql('update t set a=1')).toBe(false);
    expect(isReadOnlySql('DROP TABLE t')).toBe(false);
  });
});

describe('read-only guard', () => {
  it('blocks a mutating statement by default (no fetch issued)', async () => {
    let called = false;
    const fetchImpl: FetchLike = async () => {
      called = true;
      return {
        ok: true,
        status: 200,
        statusText: 'OK',
        json: async () => [],
        text: async () => '',
      };
    };
    const src = httpDriverSource(genericRowsAdapter, config, fetchImpl);
    const out = await src.resolve({ sql: 'DELETE FROM t', schemaOnly: false });
    expect(out.ok).toBe(false);
    if (!out.ok) expect(out.error.kind).toBe('failed');
    expect(called).toBe(false);
  });

  it('allows a mutating statement when allowWrite is opted in', async () => {
    const src = httpDriverSource(
      genericRowsAdapter,
      { ...config, allowWrite: true },
      okFetch({ rows: [{ id: 1 }] }),
    );
    const out = await src.resolve({ sql: 'INSERT INTO t VALUES (1)', schemaOnly: false });
    expect(out.ok).toBe(true);
  });
});

describe('genericRowsAdapter – inferred schema', () => {
  it('infers column types from the first non-null values', () => {
    const schema = inferSchema([
      { revenue: 42.5, category: 'books', live: true, day: '2026-06-27', n: 3 },
    ]);
    expect(schema).toContainEqual({ name: 'revenue', type: 'float' });
    expect(schema).toContainEqual({ name: 'category', type: 'string' });
    expect(schema).toContainEqual({ name: 'live', type: 'bool' });
    expect(schema).toContainEqual({ name: 'day', type: 'date' });
    expect(schema).toContainEqual({ name: 'n', type: 'int' });
  });

  it('resolves rows + inferred schema from a {rows:[...]} payload', async () => {
    const src = httpDriverSource(
      genericRowsAdapter,
      config,
      okFetch({
        rows: [
          { revenue: 10, category: 'a' },
          { revenue: 20, category: 'b' },
        ],
      }),
    );
    const out = await src.resolve({ sql: 'select * from t', schemaOnly: false });
    expect(out.ok).toBe(true);
    if (out.ok) {
      expect(out.value.schema).toContainEqual({ name: 'revenue', type: 'int' });
      expect(out.value.rows).toHaveLength(2);
    }
  });
});

describe('neonHttpAdapter – declared schema via pg OIDs', () => {
  it('maps Postgres type OIDs to ColumnType', async () => {
    const src = httpDriverSource(
      neonHttpAdapter,
      config,
      okFetch({
        fields: [
          { name: 'revenue', dataTypeID: 701 }, // float8
          { name: 'category', dataTypeID: 25 }, // text
          { name: 'day', dataTypeID: 1082 }, // date
        ],
        rows: [{ revenue: 1.5, category: 'x', day: '2026-06-01' }],
      }),
    );
    const out = await src.resolve({ sql: 'select * from t', schemaOnly: false });
    expect(out.ok).toBe(true);
    if (out.ok) {
      expect(out.value.schema).toEqual([
        { name: 'revenue', type: 'float' },
        { name: 'category', type: 'string' },
        { name: 'day', type: 'date' },
      ]);
    }
  });
});

describe('libsqlHttpAdapter – declared schema via decltype + positional cells', () => {
  it('maps SQLite decltypes and unwraps positional {type,value} cells', async () => {
    const src = httpDriverSource(
      libsqlHttpAdapter,
      config,
      okFetch({
        results: [
          {
            type: 'ok',
            response: {
              result: {
                cols: [
                  { name: 'id', decltype: 'INTEGER' },
                  { name: 'name', decltype: 'TEXT' },
                ],
                rows: [
                  [
                    { type: 'integer', value: '7' },
                    { type: 'text', value: 'alice' },
                  ],
                ],
              },
            },
          },
        ],
      }),
    );
    const out = await src.resolve({ sql: 'select * from t', schemaOnly: false });
    expect(out.ok).toBe(true);
    if (out.ok) {
      expect(out.value.schema).toEqual([
        { name: 'id', type: 'int' },
        { name: 'name', type: 'string' },
      ]);
      expect(out.value.rows?.[0]).toEqual({ id: 7, name: 'alice' });
    }
  });
});

describe('schema-only privacy projection', () => {
  it('keeps the typed schema and drops every row', async () => {
    const src = httpDriverSource(
      genericRowsAdapter,
      config,
      okFetch({ rows: [{ revenue: 10, secret: 'pii' }] }),
    );
    const out = await src.resolve({ sql: 'select * from t', schemaOnly: true });
    expect(out.ok).toBe(true);
    if (out.ok) {
      expect(out.value.rows).toBeNull();
      expect(out.value.schema.map((c) => c.name)).toContain('revenue');
    }
  });
});

describe('error mapping', () => {
  it('maps a network rejection to unavailable', async () => {
    const src = httpDriverSource(genericRowsAdapter, config, downFetch());
    const out = await src.resolve({ sql: 'select 1', schemaOnly: false });
    expect(out.ok).toBe(false);
    if (!out.ok) expect(out.error.kind).toBe('unavailable');
  });

  it('maps a non-2xx HTTP status to failed (with the body)', async () => {
    const src = httpDriverSource(
      genericRowsAdapter,
      config,
      errFetch(403, 'Forbidden', 'bad token'),
    );
    const out = await src.resolve({ sql: 'select 1', schemaOnly: false });
    expect(out.ok).toBe(false);
    if (!out.ok) {
      expect(out.error.kind).toBe('failed');
      expect(out.error.kind === 'failed' && out.error.detail).toContain('403');
    }
  });

  it('maps a provider error payload to failed', async () => {
    const src = httpDriverSource(
      neonHttpAdapter,
      config,
      okFetch({ error: 'relation "t" does not exist' }),
    );
    const out = await src.resolve({ sql: 'select * from t', schemaOnly: false });
    expect(out.ok).toBe(false);
    if (!out.ok) {
      expect(out.error.kind).toBe('failed');
      expect(out.error.kind === 'failed' && out.error.detail).toContain('does not exist');
    }
  });
});
