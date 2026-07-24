// ============================================================================
//  Rosetta – the non-F# authoring hosts (TypeScript + Python), executed live.
//
//  This page's claim is "one wire, many hosts": the SAME canonical wire bytes
//  emitted independently by three language runtimes. The F# host runs the real
//  `Fuaran.UI` canonical encoder (compiled to JS via Fable) – see Rosetta.fs.
//  This module supplies the OTHER two hosts, each a genuinely independent
//  implementation of the pinned canonical-JSON rules (WIRE_FORMAT.md §2):
//
//    • TypeScript – a from-scratch canonical encoder in this file, hashed with
//      Web Crypto SubtleCrypto (a third, browser-native SHA-256).
//    • Python – CPython compiled to WebAssembly via Pyodide, running an
//      independent canonical encoder + `hashlib.sha256`, lazy-loaded behind a
//      click so first paint is never gated on the ~10 MB runtime download.
//
//  All three converge because the wire format is canonical: sorted keys,
//  shortest-round-trip floats, deterministic escaping. The "naïve" host below
//  deliberately breaks the float rule to show WHY canonical bytes are hard.
// ============================================================================

// The 0.1+0.2 problem in one file: the canonical numeric form is the shortest
// round-tripping decimal, laid out in .NET "R" notation. This is a faithful
// port of the `formatFiniteDouble` the F# encoder uses on the Fable pipeline
// (Fuaran.UI.OpStream.Abstractions.CanonicalJson) – the byte-parity oracle.
function formatFiniteDouble(n: number): string {
  if (n === 0) return '0';
  const neg = n < 0;
  const s = Math.abs(n).toString();

  let digits = '';
  let exp = 0;
  const eIdx = s.indexOf('e');

  if (eIdx >= 0) {
    const mant = s.substring(0, eIdx);
    const mantExp = parseInt(s.substring(eIdx + 1), 10);
    const dot = mant.indexOf('.');
    if (dot < 0) {
      digits = mant;
      exp = mantExp + (mant.length - 1);
    } else {
      digits = mant.substring(0, dot) + mant.substring(dot + 1);
      exp = mantExp + (dot - 1);
    }
  } else {
    const dot = s.indexOf('.');
    if (dot < 0) {
      digits = s;
      exp = s.length - 1;
    } else {
      const intPart = s.substring(0, dot);
      const fracPart = s.substring(dot + 1);
      if (intPart === '0') {
        const trimmed = fracPart.replace(/^0+/, '');
        const leadingZeros = fracPart.length - trimmed.length;
        digits = fracPart.substring(leadingZeros);
        exp = -(leadingZeros + 1);
      } else {
        digits = intPart + fracPart;
        exp = intPart.length - 1;
      }
    }
  }

  digits = digits.replace(/0+$/, '');
  if (digits === '') digits = '0';

  let out: string;
  if (exp >= -4 && exp <= 16) {
    if (exp >= 0) {
      if (digits.length <= exp + 1) out = digits + '0'.repeat(exp + 1 - digits.length);
      else out = digits.substring(0, exp + 1) + '.' + digits.substring(exp + 1);
    } else {
      out = '0.' + '0'.repeat(-exp - 1) + digits;
    }
  } else {
    const mantissa = digits.length === 1 ? digits : digits[0] + '.' + digits.substring(1);
    const expSign = exp >= 0 ? '+' : '-';
    const expDigits = Math.abs(exp).toString().padStart(2, '0');
    out = mantissa + 'E' + expSign + expDigits;
  }

  return neg ? '-' + out : out;
}

// Canonical string escaping – mirrors the F# `appendRawString`: escape only
// `"` and `\`, emit control chars as `\uXXXX` (no `\n`/`\t` shortforms), pass
// everything else (incl. non-ASCII) through raw for identical UTF-8 bytes.
function quote(s: string): string {
  let out = '"';
  for (const ch of s) {
    const code = ch.codePointAt(0)!;
    if (ch === '"') out += '\\"';
    else if (ch === '\\') out += '\\\\';
    else if (code < 0x20) out += '\\u' + code.toString(16).padStart(4, '0');
    else out += ch;
  }
  return out + '"';
}

type Json = null | string | number | boolean | Json[] | { [k: string]: Json };

// The canonical encoder: objects sort keys Ordinal (`$` 0x24 sorts before every
// lowercase field so `$type` is always first), arrays keep order, numbers take
// the shortest-round-trip form. This is the whole algorithm.
function canon(v: Json): string {
  if (v === null) return 'null';
  const t = typeof v;
  if (t === 'string') return quote(v as string);
  if (t === 'boolean') return (v as boolean) ? 'true' : 'false';
  if (t === 'number') return formatFiniteDouble(v as number);
  if (Array.isArray(v)) return '[' + v.map(canon).join(',') + ']';
  const obj = v as { [k: string]: Json };
  const keys = Object.keys(obj).sort();
  return '{' + keys.map((k) => quote(k) + ':' + canon(obj[k]!)).join(',') + '}';
}

// The naïve host: same tree, but floats formatted with a fixed one-decimal rule
// (`toFixed(1)`) instead of shortest-round-trip. A whole-number metric (1280)
// serialises as `1280.0` where the canonical form is `1280` – a real byte
// divergence, a different hash, and the entire "canonical bytes are hard" story
// in one visual.
function naiveNum(n: number): string {
  return n.toFixed(1);
}

function canonNaive(v: Json): string {
  if (v === null) return 'null';
  const t = typeof v;
  if (t === 'string') return quote(v as string);
  if (t === 'boolean') return (v as boolean) ? 'true' : 'false';
  if (t === 'number') return naiveNum(v as number);
  if (Array.isArray(v)) return '[' + v.map(canonNaive).join(',') + ']';
  const obj = v as { [k: string]: Json };
  const keys = Object.keys(obj).sort();
  return '{' + keys.map((k) => quote(k) + ':' + canonNaive(obj[k]!)).join(',') + '}';
}

// ─── The exemplar's wire tree, built idiomatically from the six typed holes ──
//  A dashboard box (heading + a horizontal metric strip of three metrics). This
//  is TypeScript independently constructing the same tree the F# host builds –
//  no shared object crosses the boundary, only the six hole scalars.

export interface Holes {
  labelA: string;
  valueA: number;
  labelB: string;
  valueB: number;
  labelC: string;
  valueC: number;
}

// The current canonical Metric: `value` (the 0.2.0 value/source law), a bare-string
// label (canonical Literal collapse), and every default omitted (emphasis, format,
// tone, weight) – omit-when-default is part of the canonical byte contract.
function metric(id: string, label: string, value: number): Json {
  return {
    id,
    kind: {
      $type: 'Metric',
      label,
      value: { $type: 'Static', value },
    },
  };
}

function wireModel(h: Holes): Json {
  return {
    id: 'rosetta-root',
    kind: {
      $type: 'Box',
      children: [
        {
          id: 'rosetta-strip',
          kind: {
            $type: 'Box',
            children: [
              metric('rosetta-m-a', h.labelA, h.valueA),
              metric('rosetta-m-b', h.labelB, h.valueB),
              metric('rosetta-m-c', h.labelC, h.valueC),
            ],
            layout: { $type: 'Flex', direction: 'Horizontal', wrap: true },
            role: 'Group',
          },
        },
      ],
      heading: 'Revenue snapshot',
      layout: { $type: 'Flex', direction: 'Vertical', wrap: false },
      role: 'Dashboard',
    },
  };
}

export function encodeWireTs(h: Holes): string {
  return canon(wireModel(h));
}

export function encodeWireNaive(h: Holes): string {
  return canonNaive(wireModel(h));
}

// A third, browser-native SHA-256 (SubtleCrypto), distinct from the F# managed
// digest and Python's hashlib – three independent hashers over identical bytes.
export async function sha256Hex(input: string): Promise<string> {
  const bytes = new TextEncoder().encode(input);
  const digest = await crypto.subtle.digest('SHA-256', bytes);
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('');
}

// ─── Python host – Pyodide, lazy-loaded ──────────────────────────────────────
//  Pinned Pyodide, loaded from the jsDelivr CDN only when the visitor clicks
//  "Run the Python host". First paint never touches this path.

const PYODIDE_VERSION = '0.26.4';
const PYODIDE_BASE = `https://cdn.jsdelivr.net/pyodide/v${PYODIDE_VERSION}/full/`;

let pyodidePromise: Promise<any> | null = null;

async function loadPyodide(): Promise<any> {
  // @vite-ignore keeps Vite from trying to bundle the remote ESM entry.
  const mod = await import(/* @vite-ignore */ `${PYODIDE_BASE}pyodide.mjs`);
  const pyodide = await mod.loadPyodide({ indexURL: PYODIDE_BASE });
  // The independent Python canonical encoder lives beside this module as a
  // static asset (served same-origin, so `connect-src 'self'` covers the fetch).
  const src = await fetch(new URL('rosetta/py-host.py', document.baseURI)).then((r) => {
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

export interface HostResult {
  wire: string;
  hash: string;
}

// Runs the Python encoder + hashlib.sha256 fully in-browser. The six holes cross
// as a JSON string; Python parses, builds its own tree, and returns {wire, hash}.
export async function pythonCompute(h: Holes): Promise<HostResult> {
  const pyodide = await ensurePython();
  const fn = pyodide.globals.get('rosetta_encode');
  try {
    const out = fn(JSON.stringify(h)) as string;
    return JSON.parse(out) as HostResult;
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

export function pythonComputeCb(
  h: Holes,
  onOk: (wire: string, hash: string) => void,
  onError: (message: string) => void,
): void {
  pythonCompute(h).then(
    (r) => onOk(r.wire, r.hash),
    (e) => onError(String(e)),
  );
}

// ─── Rust host – fuaran-rs, wasm32, eager ───────────────────────────────────
//  The certified reference core compiled to `wasm32-unknown-unknown`. Its
//  additive `fuaran_rosetta_encode` export receives the six scalar holes (as a
//  JSON object, exactly as the TS/Python hosts do), independently builds the
//  exemplar tree with the crate's own typed model, and runs the corpus-certified
//  canonical encoder over it. The module is dependency-free (no wasm-bindgen); we
//  marshal UTF-8 across linear memory through its `fuaran_alloc` / `fuaran_dealloc`
//  + packed `(ptr<<32 | len)` return ABI. Loaded eagerly (like TS), so it
//  recomputes on every edit with no click – the ~1 MB module downloads in the
//  background after first paint.

const RUST_WASM_URL = 'rosetta/rosetta-rs.wasm';

interface RustExports {
  memory: WebAssembly.Memory;
  fuaran_alloc(len: number): number;
  fuaran_dealloc(ptr: number, len: number): void;
  fuaran_rosetta_encode(ptr: number, len: number): bigint;
}

let rustExportsPromise: Promise<RustExports> | null = null;

async function loadRust(): Promise<RustExports> {
  const url = new URL(RUST_WASM_URL, document.baseURI);
  let instance: WebAssembly.Instance;
  try {
    ({ instance } = await WebAssembly.instantiateStreaming(fetch(url), {}));
  } catch {
    // Fallback for hosts without the correct `application/wasm` MIME type.
    const bytes = await fetch(url).then((r) => {
      if (!r.ok) throw new Error(`rosetta-rs.wasm ${r.status}`);
      return r.arrayBuffer();
    });
    ({ instance } = await WebAssembly.instantiate(bytes, {}));
  }
  return instance.exports as unknown as RustExports;
}

export function ensureRust(): Promise<RustExports> {
  if (rustExportsPromise === null) rustExportsPromise = loadRust();
  return rustExportsPromise;
}

// Call `fuaran_rosetta_encode` over the module's linear memory: write the holes
// JSON into a module-owned input buffer, read the packed `(ptr, len)` return,
// copy the UTF-8 out, and free both buffers.
function rustEncode(x: RustExports, h: Holes): string {
  const bytes = new TextEncoder().encode(JSON.stringify(h));
  const inPtr = x.fuaran_alloc(bytes.length);
  new Uint8Array(x.memory.buffer).set(bytes, inPtr);
  const packed = BigInt.asUintN(64, x.fuaran_rosetta_encode(inPtr, bytes.length));
  x.fuaran_dealloc(inPtr, bytes.length);
  const outPtr = Number(packed >> 32n);
  const outLen = Number(packed & 0xffffffffn);
  if (outLen === 0) {
    if (outPtr !== 0) x.fuaran_dealloc(outPtr, outLen);
    return '';
  }
  // Copy before dealloc – the freed buffer may be reused by the next call.
  const out = new Uint8Array(x.memory.buffer).slice(outPtr, outPtr + outLen);
  x.fuaran_dealloc(outPtr, outLen);
  return new TextDecoder().decode(out);
}

export async function rustCompute(h: Holes): Promise<HostResult> {
  const x = await ensureRust();
  const wire = rustEncode(x, h);
  const hash = await sha256Hex(wire);
  return { wire, hash };
}

// ─── Go host – fuaran-go codec, GOOS=js GOARCH=wasm, lazy ────────────────────
//  fuaran-go's stdlib-only codec compiled to `js/wasm`. Its `cmd/rosetta-wasm`
//  entry registers `fuaranGoRosettaEncode(holesJSON)`, which builds the exemplar
//  tree from the six holes and runs `wire.EncodeNode` – the Go host's own
//  independent canonical encode. The Go runtime rides the toolchain's
//  `wasm_exec.js` glue and the module is several MB, so – like the Pyodide host –
//  it is lazy-loaded behind a click and never gates first paint.

const GO_WASM_URL = 'rosetta/rosetta-go.wasm';
const GO_EXEC_URL = 'rosetta/wasm_exec.js';

interface GoGlobal {
  Go: new () => { importObject: WebAssembly.Imports; run(i: WebAssembly.Instance): Promise<void> };
  fuaranGoRosettaEncode?: (holesJSON: string) => string;
}

function goGlobal(): GoGlobal {
  return globalThis as unknown as GoGlobal;
}

// Inject the toolchain's `wasm_exec.js` (served same-origin, so `script-src
// 'self'` covers it) once; it defines `globalThis.Go`.
function loadGoGlue(): Promise<void> {
  return new Promise((resolve, reject) => {
    if (typeof goGlobal().Go === 'function') {
      resolve();
      return;
    }
    const existing = document.querySelector('script[data-fuaran-go-glue]');
    if (existing) {
      existing.addEventListener('load', () => resolve());
      existing.addEventListener('error', () => reject(new Error('wasm_exec.js failed to load')));
      return;
    }
    const s = document.createElement('script');
    s.src = new URL(GO_EXEC_URL, document.baseURI).toString();
    s.dataset.fuaranGoGlue = '1';
    s.onload = () => resolve();
    s.onerror = () => reject(new Error('wasm_exec.js failed to load'));
    document.head.appendChild(s);
  });
}

let goReadyPromise: Promise<void> | null = null;

async function loadGo(): Promise<void> {
  await loadGoGlue();
  const G = goGlobal();
  if (typeof G.Go !== 'function') throw new Error('Go runtime glue unavailable');
  const go = new G.Go();
  const url = new URL(GO_WASM_URL, document.baseURI);
  let instance: WebAssembly.Instance;
  try {
    ({ instance } = await WebAssembly.instantiateStreaming(fetch(url), go.importObject));
  } catch {
    const bytes = await fetch(url).then((r) => {
      if (!r.ok) throw new Error(`rosetta-go.wasm ${r.status}`);
      return r.arrayBuffer();
    });
    ({ instance } = await WebAssembly.instantiate(bytes, go.importObject));
  }
  // `main` registers the global then blocks on `select{}`, so `run` never
  // resolves – do not await it; wait for the exported function to appear.
  void go.run(instance);
  for (let i = 0; i < 200 && typeof G.fuaranGoRosettaEncode !== 'function'; i++) {
    await new Promise((r) => setTimeout(r, 10));
  }
  if (typeof G.fuaranGoRosettaEncode !== 'function') {
    throw new Error('Go host did not register its encoder');
  }
}

export function ensureGo(): Promise<void> {
  if (goReadyPromise === null) goReadyPromise = loadGo();
  return goReadyPromise;
}

export async function goCompute(h: Holes): Promise<HostResult> {
  await ensureGo();
  const fn = goGlobal().fuaranGoRosettaEncode;
  if (typeof fn !== 'function') throw new Error('Go encoder unavailable');
  const wire = fn(JSON.stringify(h));
  const hash = await sha256Hex(wire);
  return { wire, hash };
}

// ─── Callback wrappers for the Rust + Go hosts (Fable-facing) ────────────────

export function ensureRustCb(onReady: () => void, onError: (message: string) => void): void {
  ensureRust().then(
    () => onReady(),
    (e) => onError(String(e)),
  );
}

export function rustComputeCb(
  h: Holes,
  onOk: (wire: string, hash: string) => void,
  onError: (message: string) => void,
): void {
  rustCompute(h).then(
    (r) => onOk(r.wire, r.hash),
    (e) => onError(String(e)),
  );
}

export function ensureGoCb(onReady: () => void, onError: (message: string) => void): void {
  ensureGo().then(
    () => onReady(),
    (e) => onError(String(e)),
  );
}

export function goComputeCb(
  h: Holes,
  onOk: (wire: string, hash: string) => void,
  onError: (message: string) => void,
): void {
  goCompute(h).then(
    (r) => onOk(r.wire, r.hash),
    (e) => onError(String(e)),
  );
}
