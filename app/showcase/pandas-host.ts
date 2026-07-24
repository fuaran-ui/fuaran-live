// ============================================================================
//  Pandas Dashboard – the Pyodide host that runs the visitor's Python cell.
//
//  Loads CPython + pandas in the browser (jsDelivr, lazy – nothing here touches
//  first paint), writes the bundled CSV into the Pyodide virtual filesystem so
//  `pd.read_csv("sales.csv")` just works, and runs `dash-host.py` (the ergonomic
//  `fuaran` authoring surface + `run_cell`). `runCell` executes the visitor's
//  code and returns the canonical wire JSON the cell authored – the F# side
//  decodes and renders it. Mirrors the Rosetta Pyodide host.
// ============================================================================

const PYODIDE_VERSION = '0.26.4';
const PYODIDE_BASE = `https://cdn.jsdelivr.net/pyodide/v${PYODIDE_VERSION}/full/`;

let pyodidePromise: Promise<any> | null = null;

async function boot(onProgress: (msg: string) => void): Promise<any> {
  onProgress('downloading CPython (~10 MB)…');
  const mod = await import(/* @vite-ignore */ `${PYODIDE_BASE}pyodide.mjs`);
  const pyodide = await mod.loadPyodide({ indexURL: PYODIDE_BASE });

  onProgress('loading pandas…');
  await pyodide.loadPackage(['pandas']);

  onProgress('bundling the dataset + authoring surface…');
  const [csv, host] = await Promise.all([
    fetch(new URL('pandas/sales.csv', document.baseURI)).then((r) => {
      if (!r.ok) throw new Error(`sales.csv ${r.status}`);
      return r.text();
    }),
    fetch(new URL('pandas/dash-host.py', document.baseURI)).then((r) => {
      if (!r.ok) throw new Error(`dash-host.py ${r.status}`);
      return r.text();
    }),
  ]);

  // Put the CSV where `pd.read_csv("sales.csv")` will find it.
  pyodide.FS.writeFile('sales.csv', csv);
  pyodide.runPython(host);
  onProgress('ready');
  return pyodide;
}

function ensure(onProgress: (msg: string) => void): Promise<any> {
  if (pyodidePromise === null) pyodidePromise = boot(onProgress);
  return pyodidePromise;
}

// Run the visitor's cell → the canonical wire JSON it authored.
async function runCell(code: string, onProgress: (msg: string) => void): Promise<string> {
  const pyodide = await ensure(onProgress);
  const fn = pyodide.globals.get('run_cell');
  try {
    return fn(code) as string;
  } finally {
    if (fn && typeof fn.destroy === 'function') fn.destroy();
  }
}

// ─── Callback wrappers (Fable interops cleanly with plain callbacks) ─────────

export function runCellCb(
  code: string,
  onProgress: (msg: string) => void,
  onOk: (wire: string) => void,
  onError: (message: string) => void,
): void {
  runCell(code, onProgress).then(onOk, (e) => onError(String(e && e.message ? e.message : e)));
}
