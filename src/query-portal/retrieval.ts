// ============================================================================
//  Client-side retrieval split (Phase 325b) – thin in-browser query-embed,
//  heavy server-side ingest.
//
//  Phase 325 puts the whole RAG pipeline on a server/peer. This module carves out
//  the ONLY slice cheap enough to run in a tab: the **query embedding** – one
//  short string → one vector. Ingestion (chunking, batch-embedding a corpus,
//  building the HNSW/BM25 index) stays server-side, where it belongs; the browser
//  never embeds the corpus. The retrieval index then lives in one of two places by
//  corpus size, behind ONE `IClientRetrievalSource` surface – the index location
//  is a config choice, not an API change:
//
//    • `scopedIndexSource`  – a small, scoped corpus quantized into the tab
//      (int8/binary vectors in memory/OPFS), searched with an in-browser cosine
//      ANN. For one legal matter / one project / one user's notes.
//    • `remoteVectorSource` – a large corpus: the query vector is POSTed to a
//      remote HTTP vector endpoint (Turso vector / Neon+pgvector / Qdrant REST).
//
//  Retrieval results land as the SAME typed `hitSchema` table the relational
//  portal's gate already speaks (score/title/snippet/sourceId/date – bridged from
//  the F# `RetrievalSource.hitSchema`), so a case-history dashboard type-checks
//  through the identical `checkDashboard`. One UI contract, both data planes.
//
//  MODEL-PARITY INVARIANT (see docs/RETRIEVAL-SPLIT.md): the client query-embedder
//  MUST match the server ingest-embedder's model + dimensions, or the cosine
//  scores are meaningless. The embedder is a seam so the model is pinned in one
//  place; `dim` is carried on the source for an explicit guard.
// ============================================================================

import { retrievalHitSchema, type ResultSchema } from './core';
import type { ColumnarResult, ResultRow } from './sources';

/** A retrieval request: the natural-language query + how many hits to return. */
export interface RetrievalRequest {
  readonly query: string;
  readonly topK: number;
}

/** Why a retrieval failed (mirrors the relational `QuerySourceError` shape). */
export type RetrievalError =
  | { readonly kind: 'unavailable'; readonly detail: string }
  | { readonly kind: 'failed'; readonly detail: string }
  | { readonly kind: 'embedFailed'; readonly detail: string };

export type RetrievalOutcome =
  | { readonly ok: true; readonly value: ColumnarResult }
  | { readonly ok: false; readonly error: RetrievalError };

/**
 * The host-pluggable client retrieval seam – the TS sibling of the F#
 * `IClientRetrievalSource`. A scoped in-browser index, a remote HTTP vector
 * endpoint, or an in-memory mock all conform; the result is always the canonical
 * `hitSchema` columnar table, so it threads the SAME typed gate as a relational
 * result.
 */
export interface IClientRetrievalSource {
  retrieve(request: RetrievalRequest): Promise<RetrievalOutcome>;
}

/** One retrieval hit – the canonical `hitSchema` shape (Phase 325). */
export interface RetrievalHit {
  readonly score: number;
  readonly title: string;
  readonly snippet: string;
  readonly sourceId: string;
  readonly date: string; // ISO-8601 YYYY-MM-DD
}

/** Project ranked hits into the canonical retrieval `ColumnarResult` (typed by `hitSchema`). */
export function hitsToColumnar(hits: readonly RetrievalHit[]): ColumnarResult {
  const schema: ResultSchema = retrievalHitSchema();
  const rows: ResultRow[] = hits.map((h) => ({
    score: h.score,
    title: h.title,
    snippet: h.snippet,
    sourceId: h.sourceId,
    date: h.date,
  }));
  return { schema, rows };
}

// ─── the in-browser query embedder seam ──────────────────────────────────────

/**
 * The query embedder: one short string → one dense vector. Query-ONLY – the
 * corpus is NEVER embedded client-side. The real impl loads a small model
 * (MiniLM-384 / bge-small / EmbeddingGemma class) via `onnxruntime-web` (WASM,
 * optional WebGPU); a test passes a deterministic fake. `dim` lets the source
 * assert model-parity with the server ingest-embedder.
 */
export interface IQueryEmbedder {
  readonly dim: number;
  embed(text: string): Promise<readonly number[]>;
}

/** Cosine similarity of two equal-length vectors (0 when either is degenerate). */
export function cosine(a: readonly number[], b: readonly number[]): number {
  let dot = 0;
  let na = 0;
  let nb = 0;
  const n = Math.min(a.length, b.length);
  for (let i = 0; i < n; i++) {
    dot += a[i]! * b[i]!;
    na += a[i]! * a[i]!;
    nb += b[i]! * b[i]!;
  }
  if (na === 0 || nb === 0) return 0;
  return dot / (Math.sqrt(na) * Math.sqrt(nb));
}

// ─── int8 quantization (the scoped-index footprint lever) ─────────────────────

/** A quantized scoped-index entry: an int8 vector + its scale + the hit metadata. */
export interface QuantizedEntry {
  /** int8-quantized vector (~0.4 KB/chunk at 384-dim vs ~1.5 KB float32). */
  readonly q: Int8Array;
  /** The per-vector scale used to quantize (dequantize = `q[i] * scale`). */
  readonly scale: number;
  readonly hit: Omit<RetrievalHit, 'score'>;
}

/** Quantize a float vector to int8 with a symmetric per-vector scale. */
export function quantizeInt8(vec: readonly number[]): { q: Int8Array; scale: number } {
  let max = 0;
  for (const v of vec) max = Math.max(max, Math.abs(v));
  const scale = max === 0 ? 1 : max / 127;
  const q = new Int8Array(vec.length);
  for (let i = 0; i < vec.length; i++)
    q[i] = Math.max(-127, Math.min(127, Math.round(vec[i]! / scale)));
  return { q, scale };
}

/** Dequantize an int8 entry back to a float vector for scoring. */
export function dequantize(entry: QuantizedEntry): number[] {
  const out = new Array<number>(entry.q.length);
  for (let i = 0; i < entry.q.length; i++) out[i] = entry.q[i]! * entry.scale;
  return out;
}

// ─── scoped in-browser index source ──────────────────────────────────────────

/**
 * A retrieval source over a SMALL, scoped quantized index held in the tab. The
 * query is embedded in-browser, scored by cosine against each dequantized entry,
 * and the top-k are returned as the canonical hit table. The corpus was embedded
 * + quantized SERVER-SIDE and shipped down; the browser only embeds the query.
 *
 * Footprint guidance (docs/RETRIEVAL-SPLIT.md): int8 ≈ 0.4 KB/chunk, so ~100k
 * chunks ≈ 40 MB – the practical ceiling for the in-tab path. Above that, use
 * `remoteVectorSource`.
 */
export function scopedIndexSource(
  embedder: IQueryEmbedder,
  index: readonly QuantizedEntry[],
): IClientRetrievalSource {
  return {
    async retrieve(request: RetrievalRequest): Promise<RetrievalOutcome> {
      let qvec: readonly number[];
      try {
        qvec = await embedder.embed(request.query);
      } catch (e) {
        return { ok: false, error: { kind: 'embedFailed', detail: detailOf(e) } };
      }
      const scored = index
        .map((entry) => ({ score: cosine(qvec, dequantize(entry)), hit: entry.hit }))
        .sort((a, b) => b.score - a.score)
        .slice(0, Math.max(0, request.topK));
      const hits: RetrievalHit[] = scored.map((s) => ({ score: s.score, ...s.hit }));
      return { ok: true, value: hitsToColumnar(hits) };
    },
  };
}

// ─── remote-index source ──────────────────────────────────────────────────────

/** The `fetch` shape the remote source needs (kept structural for node tests). */
export type VectorFetchLike = (
  input: string,
  init?: { method?: string; headers?: Record<string, string>; body?: string },
) => Promise<{ ok: boolean; status: number; statusText: string; json(): Promise<unknown> }>;

/** Config for a remote HTTP vector endpoint (Turso vector / pgvector / Qdrant REST). */
export interface RemoteVectorConfig {
  readonly endpoint: string;
  readonly token: string;
  /** The server ingest-embedder dimension – asserted against the client embedder. */
  readonly dim: number;
}

/**
 * A retrieval source over a LARGE corpus: embed the query in-browser, POST the
 * vector to a remote HTTP vector endpoint, render the returned ranked hits. The
 * browser issues ONLY the query vector – it never embeds or indexes the corpus.
 * The model-parity guard refuses a dimension mismatch up front (a silent mismatch
 * makes every score meaningless).
 */
export function remoteVectorSource(
  embedder: IQueryEmbedder,
  config: RemoteVectorConfig,
  fetchImpl?: VectorFetchLike,
): IClientRetrievalSource {
  const doFetch: VectorFetchLike = fetchImpl ?? (globalThis.fetch as unknown as VectorFetchLike);
  return {
    async retrieve(request: RetrievalRequest): Promise<RetrievalOutcome> {
      if (embedder.dim !== config.dim) {
        return {
          ok: false,
          error: {
            kind: 'failed',
            detail: `model-parity violation: client embedder dim ${embedder.dim} ≠ server ingest dim ${config.dim}`,
          },
        };
      }
      let qvec: readonly number[];
      try {
        qvec = await embedder.embed(request.query);
      } catch (e) {
        return { ok: false, error: { kind: 'embedFailed', detail: detailOf(e) } };
      }
      let resp: Awaited<ReturnType<VectorFetchLike>>;
      try {
        resp = await doFetch(config.endpoint, {
          method: 'POST',
          headers: { 'content-type': 'application/json', authorization: `Bearer ${config.token}` },
          body: JSON.stringify({ vector: qvec, topK: request.topK }),
        });
      } catch (e) {
        return { ok: false, error: { kind: 'unavailable', detail: detailOf(e) } };
      }
      if (!resp.ok) {
        return {
          ok: false,
          error: { kind: 'failed', detail: `${resp.status} ${resp.statusText}` },
        };
      }
      let payload: unknown;
      try {
        payload = await resp.json();
      } catch (e) {
        return { ok: false, error: { kind: 'failed', detail: 'malformed JSON: ' + detailOf(e) } };
      }
      const hits = parseHits(payload);
      if (hits === null)
        return {
          ok: false,
          error: { kind: 'failed', detail: 'no recognisable hit array in response' },
        };
      return { ok: true, value: hitsToColumnar(hits) };
    },
  };
}

// ─── the real in-browser embedder loader (browser/WASM – operator-verified) ───

/**
 * Load a real query embedder over `onnxruntime-web` for `modelUrl` (a small
 * MiniLM/bge-small/EmbeddingGemma ONNX). Browser/WASM-bound – never called by the
 * headless suite (the scoring + split logic is tested with a deterministic
 * embedder). The package is an optional dependency; the import is guarded.
 */
export async function onnxQueryEmbedder(_modelUrl: string, _dim: number): Promise<IQueryEmbedder> {
  let ort: any;
  try {
    // @ts-ignore -- optional dependency; resolved at runtime in the browser bundle.
    ort = await import('onnxruntime-web');
  } catch (e) {
    throw new Error(
      'onnxruntime-web is not installed; add it to run the in-browser embedder: ' + detailOf(e),
    );
  }
  // The concrete tokenization + session.run wiring is model-specific and verified
  // in the browser; the seam below is what the rest of the portal depends on.
  void ort;
  throw new Error(
    'onnxQueryEmbedder requires browser wiring (operator-verified); use a seam impl in tests',
  );
}

// ─── helpers ──────────────────────────────────────────────────────────────────

function parseHits(payload: unknown): RetrievalHit[] | null {
  const arr = Array.isArray(payload)
    ? payload
    : typeof payload === 'object' &&
        payload !== null &&
        Array.isArray((payload as { hits?: unknown }).hits)
      ? (payload as { hits: unknown[] }).hits
      : null;
  if (arr === null) return null;
  return arr.map((h) => {
    const o = (h ?? {}) as Record<string, unknown>;
    return {
      score: typeof o.score === 'number' ? o.score : 0,
      title: String(o.title ?? ''),
      snippet: String(o.snippet ?? ''),
      sourceId: String(o.sourceId ?? o.source_id ?? ''),
      date: String(o.date ?? ''),
    };
  });
}

function detailOf(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}
