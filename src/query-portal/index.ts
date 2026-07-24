// ============================================================================
//  The BYO-data query-portal public surface (Phase 324/325).
//
//  One barrel for the whole portal: the F#↔TS bridge facade, the client query
//  seam + gate, the serverless-HTTP + DuckDB-WASM drivers, the NL→query+UI
//  emission loop, the local-refinement fast-path, and the ephemeral credential
//  store + privacy config. The fuaran-live app imports the portal from here.
// ============================================================================

export * from './core';
export * from './sources';
export * from './httpDriverSource';
export * from './duckdbSource';
export * from './emission';
export * from './refine';
export * from './ephemeral';
export * from './retrieval';
