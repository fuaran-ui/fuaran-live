// ============================================================================
//  The showcase's one Pyodide loader.
//
//  Three pages run Python in the browser — Rosetta's independent encoder, the
//  Relay's hasher and the Pandas Dashboard's notebook cell. Each used to pin
//  Pyodide and keep its own loader singleton, so a visitor who opened all three
//  started three CPython interpreters (~10 MB and several seconds each). Now one
//  module owns the pin and the interpreter: the first page to ask boots it, and
//  every later page reuses it.
//
//  Sharing an interpreter must not mean sharing a namespace. Each host's Python
//  source runs in its own fresh globals dict (`runIsolated`), so one page's
//  helper names can never shadow another's. Packages load once each; a second
//  request for a package already loading waits on the first.
//
//  Loaded from the pinned jsDelivr distribution, lazily — nothing here runs
//  until a page asks, and first paint never touches it. That CDN is the
//  showcase CSP's one non-'self' allowance (vite.config.ts).
// ============================================================================

export const PYODIDE_VERSION = '0.26.4';
export const PYODIDE_BASE = `https://cdn.jsdelivr.net/pyodide/v${PYODIDE_VERSION}/full/`;

interface PyodideModule {
  loadPyodide(options: { indexURL: string }): Promise<any>;
}

type Importer = (url: string) => Promise<PyodideModule>;

// @vite-ignore keeps Vite from trying to bundle the remote ESM entry.
const cdnImporter: Importer = (url) => import(/* @vite-ignore */ url);

let importer: Importer = cdnImporter;
let interpreter: Promise<any> | null = null;
const packages = new Map<string, Promise<void>>();

/** The shared interpreter, booted on first call. A failed boot is forgotten, so
 *  a later request (a retry click) starts a fresh attempt rather than replaying
 *  the failure. */
export function interpreterPromise(): Promise<any> {
  if (interpreter === null) {
    const boot = importer(`${PYODIDE_BASE}pyodide.mjs`).then((mod) =>
      mod.loadPyodide({ indexURL: PYODIDE_BASE }),
    );
    interpreter = boot;
    boot.catch(() => {
      if (interpreter === boot) interpreter = null;
    });
  }
  return interpreter;
}

/** Load Pyodide packages into the shared interpreter, each at most once. */
export async function loadPackages(names: string[]): Promise<any> {
  const py = await interpreterPromise();
  await Promise.all(
    names.map((name) => {
      let p = packages.get(name);
      if (p === undefined) {
        p = Promise.resolve(py.loadPackage([name])).then(() => undefined);
        packages.set(name, p);
        p.catch(() => packages.delete(name));
      }
      return p;
    }),
  );
  return py;
}

/** Run `source` in its own fresh globals dict and return that namespace; read a
 *  host's entry point back with `ns.get(name)`. */
export function runIsolated(py: any, source: string): any {
  const ns = py.globals.get('dict')();
  py.runPython(source, { globals: ns });
  return ns;
}

/** Test seam: swap the module importer (and forget any interpreter) so a test can
 *  count how many interpreters the hosts start. Not used by the pages. */
export function setImporterForTests(next: Importer | null): void {
  importer = next ?? cdnImporter;
  interpreter = null;
  packages.clear();
}
