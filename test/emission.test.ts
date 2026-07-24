// Phase 324, task 3 – the NL→query+UI emission loop, exercised end-to-end with a
// canned model response (no key, no network) against the in-memory source and the
// SHIPPED F# type-check gate via the Fable bridge. Proves both halves are
// validated before render: a mistyped dashboard is rejected (FUARAN066), a
// malformed emission never reaches the driver, a well-typed one renders.

import { describe, it, expect } from 'vitest';
import { fuaran, binding } from '@fuaran-ui/ui';
import { encodeNode } from '@fuaran-ui/ops';

import {
  buildSystemPrompt,
  parseEmission,
  runEmission,
  type IEmissionProvider,
} from '../src/query-portal/emission';
import { inMemorySource } from '../src/query-portal/sources';
import type { ResultSchema } from '../src/query-portal/core';

const schema: ResultSchema = [
  { name: 'revenue', type: 'float' },
  { name: 'category', type: 'string' },
];
const rows = [
  { revenue: 42, category: 'books' },
  { revenue: 17, category: 'toys' },
];

/** A metric whose numeric value is bound to query column `col`, as a wire object. */
const metricWireObject = (col: string): object =>
  JSON.parse(
    encodeNode(
      fuaran.metric({
        id: 'm1',
        label: 'Top',
        value: binding.query(col, (row: Record<string, number>) => row[col] as number),
      }),
    ),
  );

/** A provider that returns a fixed emission, optionally wrapped in prose/fences. */
const cannedProvider = (
  emission: object,
  wrap: 'bare' | 'fenced' | 'prose' = 'bare',
): IEmissionProvider => ({
  async complete() {
    const json = JSON.stringify(emission);
    if (wrap === 'fenced') return '```json\n' + json + '\n```';
    if (wrap === 'prose') return `Here is your dashboard:\n${json}\nHope that helps!`;
    return json;
  },
});

describe('buildSystemPrompt', () => {
  it('appends the known schema columns when supplied', () => {
    const p = buildSystemPrompt(schema);
    expect(p).toContain('revenue: float');
    expect(p).toContain('category: string');
  });
  it('omits the schema clause when none is known', () => {
    expect(buildSystemPrompt()).not.toContain('exposes these columns');
  });
});

describe('parseEmission', () => {
  it('parses a bare {query,dashboard} object', () => {
    const r = parseEmission(JSON.stringify({ query: 'select 1', dashboard: { kind: 'Text' } }));
    expect(r.ok).toBe(true);
    if (r.ok) expect(r.value.sql).toBe('select 1');
  });
  it('parses a ```json fenced block with surrounding prose', () => {
    const raw = 'Sure:\n```json\n{"query":"select 1","dashboard":{"kind":"Text"}}\n```\n';
    const r = parseEmission(raw);
    expect(r.ok).toBe(true);
  });
  it('rejects a response with no JSON object', () => {
    const r = parseEmission('I cannot help with that.');
    expect(r.ok).toBe(false);
  });
  it('rejects a JSON object missing query/dashboard', () => {
    const r = parseEmission(JSON.stringify({ query: 'select 1' }));
    expect(r.ok).toBe(false);
  });
});

describe('runEmission', () => {
  it('renders a well-typed emission against the source', async () => {
    const provider = cannedProvider({
      query: 'select * from t',
      dashboard: metricWireObject('revenue'),
    });
    const out = await runEmission(provider, inMemorySource(schema, rows), {
      nl: 'top revenue',
      schema,
      schemaOnly: false,
    });
    expect(out.kind).toBe('render');
    if (out.kind === 'render') expect(out.rows).toHaveLength(2);
  });

  it('rejects a mistyped emission BEFORE render (string column → numeric metric)', async () => {
    const provider = cannedProvider(
      { query: 'select * from t', dashboard: metricWireObject('category') },
      'fenced',
    );
    const out = await runEmission(provider, inMemorySource(schema, rows), {
      nl: 'top category',
      schema,
      schemaOnly: false,
    });
    expect(out.kind).toBe('typeMismatch');
    if (out.kind === 'typeMismatch') expect(out.defects[0]!.code).toBe('FUARAN066');
  });

  it('rejects a malformed emission as emissionInvalid (driver never runs)', async () => {
    const provider: IEmissionProvider = {
      async complete() {
        return 'no json here';
      },
    };
    const out = await runEmission(provider, inMemorySource(schema, rows), {
      nl: 'anything',
      schemaOnly: false,
    });
    expect(out.kind).toBe('emissionInvalid');
  });

  it('surfaces a provider failure as providerError', async () => {
    const provider: IEmissionProvider = {
      async complete() {
        throw new Error('401 unauthorized');
      },
    };
    const out = await runEmission(provider, inMemorySource(schema, rows), {
      nl: 'x',
      schemaOnly: false,
    });
    expect(out.kind).toBe('providerError');
    if (out.kind === 'providerError') expect(out.error).toContain('401');
  });

  it('threads schema-only through to the source (no rows cross)', async () => {
    const provider = cannedProvider(
      { query: 'select * from t', dashboard: metricWireObject('revenue') },
      'prose',
    );
    const out = await runEmission(provider, inMemorySource(schema, rows), {
      nl: 'top revenue',
      schema,
      schemaOnly: true,
    });
    expect(out.kind).toBe('render');
    if (out.kind === 'render') expect(out.rows).toBeNull();
  });
});
