# Client-side retrieval split — thin in-browser query-embed, heavy server-side ingest

> Phase 325b. [Phase 325](../../roadmap/phases/325-rag-backed-retrieval-source.md) puts the whole
> RAG pipeline on a server/peer. This note documents the slice that can move into the browser
> **without** moving the corpus — and, just as importantly, the slice that must **not**.

## The split (what runs where)

The corpus-volume worry ("you can't put a RAG index in a tab") is real but mis-scoped. You never
put a _general_ corpus in a tab — you **split the pipeline**:

| Stage                                                                                    | Where                                            | Why                                                                            |
| ---------------------------------------------------------------------------------------- | ------------------------------------------------ | ------------------------------------------------------------------------------ |
| **Ingestion** — chunking, batch-embedding the whole corpus, building the HNSW/BM25 index | **Server / peer** (a retrieval ingestion server) | Heavy, batch, security + cost boundary. The browser never sees the raw corpus. |
| **Query embedding** — one short string → one vector                                      | **Browser** (`onnxruntime-web`, WASM ± WebGPU)   | Cheap: one forward pass of a small model.                                      |
| **Retrieval (top-k)** — score the query vector against the index                         | **Browser** (scoped) **or** server (large)       | Config choice by corpus size — see below.                                      |

The browser only ever issues a query; it never embeds or indexes the corpus. That split is the
security + cost boundary. The code: [`retrieval.ts`](../src/query-portal/retrieval.ts) —
`IQueryEmbedder` (query-only), `scopedIndexSource`, `remoteVectorSource`, all behind ONE
`IClientRetrievalSource` surface.

## Choosing the retrieval path (scoped vs large)

The index lives in one of two places, and **the index location is a config choice, not an API
change** — both paths are the same `IClientRetrievalSource`:

### Scoped — in-browser quantized index (`scopedIndexSource`)

For a small, scoped corpus: **one legal matter, one project, one user's notes**. The
server embeds + quantizes the corpus and ships the index down; the browser holds it in
memory/OPFS and searches it with an in-tab cosine ANN.

Footprint (the ceiling where this stops being viable):

| Quantization | Bytes / chunk (384-dim) | 100k chunks                    |
| ------------ | ----------------------- | ------------------------------ |
| float32      | ~1.5 KB                 | ~150 MB (too big)              |
| **int8**     | **~0.4 KB**             | **~40 MB** (practical ceiling) |
| binary       | ~48 B                   | ~5 MB (recall trade-off)       |

Rule of thumb: **int8 up to ~100k chunks** in the tab. Above that, go remote.

### Large — remote HTTP vector endpoint (`remoteVectorSource`)

For a large corpus: the query vector is POSTed to a remote HTTP vector endpoint
(Turso vector / Neon+pgvector / Qdrant or Pinecone REST), or a static Parquet/Lance index read
via HTTP range requests (DuckDB-WASM). This is exactly the
[Phase 324](324-byo-db-client-query-dashboard-portal.md) BYO-serverless-DB shape applied to a
vector index — the browser issues only the query vector, never the corpus.

## The model-parity invariant (the one that bites silently)

**The client query-embedder MUST match the server ingest-embedder's model AND dimensions.** If
the corpus was ingested with `bge-small-en-v1.5` (384-dim) and the tab embeds the query with a
different model — or even the same model at a different dimension — the cosine scores are
**meaningless**: you are comparing vectors from two different spaces. There is no error, just
silently bad ranking.

The code makes this explicit: `remoteVectorSource` refuses up front when
`embedder.dim !== config.dim` (the server ingest dimension), turning a silent ranking corruption
into a loud configuration error. The embedder is a seam precisely so the model is pinned in **one
place**, shared by config with the ingest side.

## What stays put

Ingestion runs on a retrieval ingestion server (hosted or paired-device per
[Phase 325](../../roadmap/phases/325-rag-backed-retrieval-source.md)) — chunking, corpus
embedding, HNSW build. **The client never ingests.** This is not a performance nicety; it is the
boundary that keeps the corpus under the user's control and the cost on the server.

## See also

- [`retrieval.ts`](../src/query-portal/retrieval.ts) — the embedder seam + scoped/remote sources.
- [BYO-DATA.md](BYO-DATA.md) — the relational client-only portal (the sibling data plane).
- [Phase 325](../../roadmap/phases/325-rag-backed-retrieval-source.md) /
  [Phase 325b](../../roadmap/phases/325b-client-side-retrieval-split.md).
