// Phase 325b – the client-side retrieval split. The real onnxruntime-web embedder
// is browser-bound (operator-verified); here the split logic is exercised with a
// deterministic embedder: in-browser cosine over a scoped quantized index, the
// remote-vector-endpoint path (mock fetch), the model-parity guard, and that hits
// land typed against the canonical retrieval `hitSchema`.

import { describe, it, expect } from 'vitest';
import { fuaran, binding } from '@fuaran-ui/ui';
import { encodeNode } from '@fuaran-ui/ops';

import { checkDashboard } from '../src/query-portal/core';
import {
  scopedIndexSource,
  remoteVectorSource,
  hitsToColumnar,
  quantizeInt8,
  cosine,
  type IQueryEmbedder,
  type QuantizedEntry,
  type RetrievalHit,
  type VectorFetchLike,
} from '../src/query-portal/retrieval';

// A deterministic 3-dim "embedder": maps a few keywords to fixed unit-ish vectors,
// so cosine ordering is predictable. Query-only (never embeds a corpus).
const KEYWORD_VECS: Record<string, number[]> = {
  contract: [1, 0, 0],
  tax: [0, 1, 0],
  injury: [0, 0, 1],
};
const fakeEmbedder = (dim = 3): IQueryEmbedder => ({
  dim,
  async embed(text: string) {
    const key = Object.keys(KEYWORD_VECS).find((k) => text.toLowerCase().includes(k));
    return key ? KEYWORD_VECS[key]! : new Array(dim).fill(0);
  },
});

/** Build a scoped quantized index entry for a keyword. */
const entry = (keyword: string, title: string): QuantizedEntry => {
  const { q, scale } = quantizeInt8(KEYWORD_VECS[keyword]!);
  return {
    q,
    scale,
    hit: { title, snippet: `…${keyword}…`, sourceId: `doc:${title}`, date: '2026-01-01' },
  };
};

describe('cosine + quantization', () => {
  it('cosine ranks the aligned vector highest', () => {
    expect(cosine([1, 0, 0], [1, 0, 0])).toBeCloseTo(1);
    expect(cosine([1, 0, 0], [0, 1, 0])).toBeCloseTo(0);
  });
  it('int8 quantization round-trips within tolerance', () => {
    const { q, scale } = quantizeInt8([0.9, -0.2, 0.05]);
    expect(q.length).toBe(3);
    expect(q[0]! * scale).toBeCloseTo(0.9, 1);
  });
});

describe('scopedIndexSource – in-browser query embed + quantized cosine', () => {
  const index = [
    entry('contract', 'Acme v Beta'),
    entry('tax', 'Gamma Filing'),
    entry('injury', 'Delta Claim'),
  ];

  it('returns the top-k hits ranked by cosine, typed by hitSchema', async () => {
    const src = scopedIndexSource(fakeEmbedder(), index);
    const out = await src.retrieve({ query: 'a contract dispute', topK: 2 });
    expect(out.ok).toBe(true);
    if (out.ok) {
      expect(out.value.schema.find((c) => c.name === 'score')?.type).toBe('float');
      expect(out.value.rows).toHaveLength(2);
      // The 'contract' doc ranks first for a contract query.
      expect((out.value.rows as Record<string, unknown>[])[0]!.title).toBe('Acme v Beta');
    }
  });

  it('surfaces an embedder failure as embedFailed', async () => {
    const broken: IQueryEmbedder = {
      dim: 3,
      async embed() {
        throw new Error('onnx session failed');
      },
    };
    const out = await scopedIndexSource(broken, index).retrieve({ query: 'x', topK: 1 });
    expect(out.ok).toBe(false);
    if (!out.ok) expect(out.error.kind).toBe('embedFailed');
  });
});

describe('remoteVectorSource – query vector to a remote HTTP endpoint', () => {
  const config = { endpoint: 'https://vec.example/search', token: 't', dim: 3 };
  let sentBody: string | null = null;
  const fetchHits =
    (hits: object[]): VectorFetchLike =>
    async (_url, init) => {
      sentBody = init?.body ?? null;
      return { ok: true, status: 200, statusText: 'OK', json: async () => ({ hits }) };
    };

  it('POSTs only the query vector and renders the returned hits', async () => {
    sentBody = null;
    const src = remoteVectorSource(
      fakeEmbedder(),
      config,
      fetchHits([
        { score: 0.9, title: 'Remote A', snippet: 's', sourceId: 'r:a', date: '2026-02-02' },
      ]),
    );
    const out = await src.retrieve({ query: 'tax question', topK: 5 });
    expect(out.ok).toBe(true);
    if (out.ok) expect((out.value.rows as Record<string, unknown>[])[0]!.title).toBe('Remote A');
    // The body carried the query vector + topK – never the corpus.
    expect(sentBody).toContain('"vector"');
    expect(sentBody).toContain('"topK":5');
  });

  it('refuses a model-parity (dimension) mismatch up front', async () => {
    const src = remoteVectorSource(fakeEmbedder(3), { ...config, dim: 384 }, fetchHits([]));
    const out = await src.retrieve({ query: 'tax', topK: 1 });
    expect(out.ok).toBe(false);
    if (!out.ok) {
      expect(out.error.kind).toBe('failed');
      expect(out.error.kind === 'failed' && out.error.detail).toContain('model-parity');
    }
  });

  it('maps a network rejection to unavailable', async () => {
    const down: VectorFetchLike = async () => {
      throw new Error('offline');
    };
    const out = await remoteVectorSource(fakeEmbedder(), config, down).retrieve({
      query: 'tax',
      topK: 1,
    });
    expect(out.ok).toBe(false);
    if (!out.ok) expect(out.error.kind).toBe('unavailable');
  });
});

describe('retrieval results thread the SAME typed gate as relational results', () => {
  it('a case-history dashboard binding score (float) → numeric metric type-checks', () => {
    const hits: RetrievalHit[] = [
      { score: 0.91, title: 'Acme v Beta', snippet: '…', sourceId: 'doc:1', date: '2026-01-01' },
    ];
    const columnar = hitsToColumnar(hits);
    const dashboard = encodeNode(
      fuaran.metric({
        id: 'relevance',
        label: 'Relevance',
        value: binding.query('score', (row: Record<string, number>) => row.score as number),
      }),
    );
    const result = checkDashboard(columnar.schema, dashboard);
    expect(result.ok).toBe(true);
  });

  it('binding title (string) → numeric metric is rejected (FUARAN066) – one contract, both planes', () => {
    const columnar = hitsToColumnar([]);
    const dashboard = encodeNode(
      fuaran.metric({
        id: 'bad',
        label: 'Bad',
        value: binding.query(
          'title',
          (row: Record<string, number>) => row.title as unknown as number,
        ),
      }),
    );
    const result = checkDashboard(columnar.schema, dashboard);
    expect(result.ok).toBe(false);
    expect(result.defects[0]!.code).toBe('FUARAN066');
  });
});
