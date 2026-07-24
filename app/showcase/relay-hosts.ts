// ============================================================================
//  The Relay – the independent hashers behind the cross-host parity seal.
//
//  The Relay dramatises an in-progress app crossing four runtimes with its
//  hash chain intact. Its spine – canonical encode, tree-op apply, hash-chain,
//  Verify.chain – is the real `Fuaran.UI` engine compiled to JavaScript via
//  Fable (see Relay.fs). What THIS module supplies is the genuinely-per-host
//  part of the claim: two more SHA-256 implementations that recompute each
//  station's canonical-wire head hash and must agree with the F# managed digest.
//
//    • TypeScript – Web Crypto SubtleCrypto, always-on (browser-native).
//    • Python     – CPython compiled to WebAssembly via Pyodide, running
//                   hashlib.sha256, lazy-loaded behind a click so first paint is
//                   never gated on the ~10 MB runtime download.
//
//  Three languages, three hashers, one set of bytes – the same posture Rosetta
//  takes for its static parity strip, here in motion across a relay.
// ============================================================================

// A browser-native SHA-256 (SubtleCrypto) – the TypeScript host's independent
// digest, distinct from the F# managed hash and Python's hashlib.
export async function sha256Hex(input: string): Promise<string> {
  const bytes = new TextEncoder().encode(input);
  const digest = await crypto.subtle.digest('SHA-256', bytes);
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('');
}

// ─── Python host – Pyodide, lazy-loaded ──────────────────────────────────────
//  Pinned Pyodide from the jsDelivr CDN, loaded only when the visitor turns on
//  the Python verifier. First paint never touches this path.

const PYODIDE_VERSION = '0.26.4';
const PYODIDE_BASE = `https://cdn.jsdelivr.net/pyodide/v${PYODIDE_VERSION}/full/`;

let pyodidePromise: Promise<any> | null = null;

async function loadPyodide(): Promise<any> {
  // @vite-ignore keeps Vite from trying to bundle the remote ESM entry.
  const mod = await import(/* @vite-ignore */ `${PYODIDE_BASE}pyodide.mjs`);
  const pyodide = await mod.loadPyodide({ indexURL: PYODIDE_BASE });
  // The Python hasher lives beside this module as a static asset (served
  // same-origin, so `connect-src 'self'` covers the fetch).
  const src = await fetch(new URL('relay/py-host.py', document.baseURI)).then((r) => {
    if (!r.ok) throw new Error(`py-host.py ${r.status}`);
    return r.text();
  });
  pyodide.runPython(src);
  return pyodide;
}

export function ensurePython(): Promise<any> {
  if (pyodidePromise === null) pyodidePromise = loadPyodide();
  return pyodidePromise;
}

// Runs hashlib.sha256 fully in-browser over the exact canonical-wire bytes.
export async function pythonSha256(input: string): Promise<string> {
  const pyodide = await ensurePython();
  const fn = pyodide.globals.get('relay_sha256');
  try {
    return fn(input) as string;
  } finally {
    if (fn && typeof fn.destroy === 'function') fn.destroy();
  }
}

// ─── Callback wrappers ───────────────────────────────────────────────────────
//  Fable interops with plain callbacks far more cleanly than with JS Promises,
//  so the F# page drives every async host through these thin adapters.

export function sha256HexCb(input: string, cb: (hex: string) => void): void {
  sha256Hex(input).then(cb);
}

export function ensurePythonCb(onReady: () => void, onError: (message: string) => void): void {
  ensurePython().then(
    () => onReady(),
    (e) => onError(String(e)),
  );
}

export function pythonSha256Cb(
  input: string,
  onOk: (hex: string) => void,
  onError: (message: string) => void,
): void {
  pythonSha256(input).then(
    (h) => onOk(h),
    (e) => onError(String(e)),
  );
}
