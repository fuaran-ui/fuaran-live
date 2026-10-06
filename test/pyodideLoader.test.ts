// The showcase's three Python pages share ONE Pyodide interpreter (Phase 2080).
//
// Rosetta's encoder, the Relay's hasher and the Pandas Dashboard's cell host each
// used to pin Pyodide and keep their own loader singleton, so a visitor to all
// three started three CPython interpreters. They now boot through
// app/showcase/pyodide.ts. This drives the three hosts' real entry points against
// a counting stand-in for the CDN module (no network, no WebAssembly) and asserts
// the visitor's three pages start exactly one interpreter — while each host still
// runs its Python in a namespace of its own.

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// Fresh module instances per test: every host caches its namespace, which is the
// behaviour under test, so a test must not inherit the previous test's caches.
async function loadHosts() {
  vi.resetModules();
  const loader = await import('../app/showcase/pyodide');
  const relay = await import('../app/showcase/relay-hosts');
  const rosetta = await import('../app/showcase/rosetta-hosts');
  const pandas = await import('../app/showcase/pandas-host');
  return { loader, relay, rosetta, pandas };
}

interface FakeCounts {
  imports: number;
  boots: number;
  namespaces: number;
  packages: string[];
}

function fakePyodide(counts: FakeCounts) {
  return {
    loadPackage: async (names: string[]) => {
      counts.packages.push(...names);
    },
    pyimport: () => ({ install: { callKwargs: async () => undefined }, destroy: () => undefined }),
    FS: { writeFile: () => undefined },
    globals: {
      get: (name: string) => {
        expect(name).toBe('dict');
        return () => {
          counts.namespaces += 1;
          const entries = new Map<string, unknown>();
          return { entries, get: (k: string) => entries.get(k) };
        };
      },
    },
    runPython: (src: string, opts: { globals: { entries: Map<string, unknown> } }) => {
      // Each host's source defines its entry point in the namespace it was given.
      const ns = opts.globals.entries;
      if (src.includes('relay')) ns.set('relay_sha256', (s: string) => `sha:${s.length}`);
      else if (src.includes('rosetta')) ns.set('rosetta_encode', () => '{"wire":"w","hash":"h"}');
      else ns.set('run_cell', (code: string) => `{"cell":${code.length}}`);
    },
  };
}

let counts: FakeCounts;
let hosts: Awaited<ReturnType<typeof loadHosts>>;
const realFetch = globalThis.fetch;

beforeEach(async () => {
  counts = { imports: 0, boots: 0, namespaces: 0, packages: [] };
  hosts = await loadHosts();
  hosts.loader.setImporterForTests(async () => {
    counts.imports += 1;
    return {
      loadPyodide: async () => {
        counts.boots += 1;
        return fakePyodide(counts);
      },
    };
  });
  (globalThis as any).document = { baseURI: 'http://showcase.test/' };
  globalThis.fetch = (async (url: URL | string) => {
    const u = String(url);
    const body = u.includes('relay/')
      ? '# relay host'
      : u.includes('rosetta/')
        ? '# rosetta host'
        : 'region,revenue';
    return { ok: true, status: 200, text: async () => body };
  }) as typeof fetch;
});

afterEach(() => {
  hosts.loader.setImporterForTests(null);
  globalThis.fetch = realFetch;
  delete (globalThis as any).document;
});

const runCell = (code: string) =>
  new Promise<string>((resolve, reject) =>
    hosts.pandas.runCellCb(code, () => undefined, resolve, reject),
  );
const relaySha = (s: string) =>
  new Promise<string>((resolve, reject) => hosts.relay.pythonSha256Cb(s, resolve, reject));

describe('the shared Pyodide loader', () => {
  it('a visitor to all three Python pages starts ONE interpreter', async () => {
    // Visited one after another, as a reader clicking through the site would.
    expect(await relaySha('abc')).toBe('sha:3');
    await hosts.rosetta.ensurePython();
    expect(await runCell('app = 1')).toBe('{"cell":7}');

    expect(counts.imports).toBe(1);
    expect(counts.boots).toBe(1);
    // ...and each host ran in a namespace of its own.
    expect(counts.namespaces).toBe(3);
    expect(counts.packages.sort()).toEqual(['micropip', 'pandas']);
  });

  it('concurrent first requests still start one interpreter', async () => {
    await Promise.all([hosts.relay.ensurePython(), hosts.rosetta.ensurePython(), runCell('x')]);
    expect(counts.boots).toBe(1);
  });

  it('a failed boot is forgotten, so a retry starts afresh', async () => {
    let attempt = 0;
    hosts.loader.setImporterForTests(async () => {
      attempt += 1;
      if (attempt === 1) throw new Error('offline');
      return { loadPyodide: async () => fakePyodide(counts) };
    });
    await expect(hosts.rosetta.ensurePython()).rejects.toThrow('offline');
    await expect(hosts.rosetta.ensurePython()).resolves.toBeDefined();
    expect(attempt).toBe(2);
  });
});
