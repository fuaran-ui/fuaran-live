// ============================================================================
//  Pandas Dashboard – the Pyodide host that runs the visitor's Python cell.
//
//  Loads CPython + pandas in the browser (jsDelivr, lazy – nothing here touches
//  first paint), installs the PUBLISHED `fuaran-ui` package with micropip, writes
//  the bundled CSV into the Pyodide virtual filesystem so `pd.read_csv("sales.csv")`
//  just works, and defines `run_cell`. `runCell` executes the visitor's code and
//  returns the canonical wire JSON the cell authored – the F# side decodes and
//  renders it. Mirrors the Rosetta Pyodide host.
//
//  Phase 1166 retargeted this at the real package. Until then the cell ran against
//  `public/pandas/dash-host.py`, a hand-rolled surface that MIRRORED the package's
//  model with its own canonical-JSON encoder – so the page demonstrated an API the
//  package does not ship, and drifted from it silently. The cell's `fuaran` name is
//  now bound to `fuaran_ui.ui.quick`, the package's own terse authoring layer, and
//  the wire is produced by the package's own `encode`. There is one encoder again.
//
//  THE DISTRIBUTION WAS RENAMED, and this page followed on a separate act. Phase
//  1694 renamed the Python tier's PyPI distribution `fuaran-py` → `fuaran-ui` and
//  its import package `fuaran_py` → `fuaran_ui`; `fuaran-py` stops at 0.5.0 and is
//  never published to again. The conformance arm moved to `fuaran-ui==0.7.0` first
//  (68e9ad9) and deliberately left this wheel behind, because re-vendoring it is a
//  digest-locked act of its own — the one this file now records. The wheel is the
//  published 0.7.0, which carries Phase 1821's `project.columns` / `column` wire
//  rename. The `quick` authoring surface is unchanged across the move: every
//  builder the page calls (`dashboard` / `metric_strip` / `markdown` / `grid`) has
//  the same signature it had at 0.0.4, so the only adaptation here is the import.
//  (The HOST_ID the capability manifest reports is still `fuaran-py` — it names the
//  HOST, not the distribution. Do not "fix" it to match this file.)
//
//  WHY THE WHEEL IS VENDORED RATHER THAN FETCHED FROM PyPI. The showcase entries
//  are the zero-key-egress half of the site: their CSP is `connect-src 'self'` plus
//  the pinned Pyodide CDN and loopback, and every allowance in vite.config.ts is a
//  narrow, named exception. `micropip.install("fuaran-ui==0.7.0")` from PyPI would
//  need `pypi.org` + `files.pythonhosted.org` added to that policy — widening the
//  shipped egress surface for every visitor of every showcase page, including the
//  ones who never click Run. So the exact published artefact is committed under
//  public/pandas/ and installed from this site's own origin instead; the policy is
//  untouched, and the install is a same-origin fetch.
//
//  The vendored bytes are pinned to the PUBLISHED ones. The digest below is the
//  `bdist_wheel` sha256 that PyPI's JSON API reports for fuaran-ui 0.7.0
//  (https://pypi.org/pypi/fuaran-ui/0.7.0/json); test/emitterLocks.test.ts hashes
//  the committed file and asserts it matches, so a locally-built wheel — or a
//  version bumped in one place and not the other — fails CI rather than shipping.
// ============================================================================

const PYODIDE_VERSION = '0.26.4';
const PYODIDE_BASE = `https://cdn.jsdelivr.net/pyodide/v${PYODIDE_VERSION}/full/`;

/** The published `fuaran-ui` release this page authors against. */
export const FUARAN_UI_VERSION = '0.7.0';

/** The vendored wheel's filename, under `public/pandas/`. */
export const FUARAN_UI_WHEEL = `fuaran_ui-${FUARAN_UI_VERSION}-py3-none-any.whl`;

/** The sha256 PyPI publishes for that wheel — the pin the vendored bytes are held to. */
export const FUARAN_UI_WHEEL_SHA256 =
  '7e8fc778750f25565349527db0fb92390bc3db3bccccf14c3c67a0894e4e65aa';

/**
 * The host bootstrap, executed once after the package is installed.
 *
 * It binds the cell's `fuaran` name to `fuaran_ui.ui.quick` — the terse,
 * title-first, ids-derived layer — and hands the wire back through the package's
 * own `encode`. Nothing here authors wire itself; the emitter is the package plus
 * the cell, which is what test/emitterLocks.test.ts locks (it execs THIS string
 * against the vendored wheel).
 *
 * `pd` is bound when pandas imported, so the same bootstrap runs headlessly in a
 * plain CPython that has no pandas.
 */
export const PY_BOOTSTRAP = `
from fuaran_ui.ui import quick, encode


def run_cell(code):
    ns = {"fuaran": quick}
    try:
        import pandas as pd

        ns["pd"] = pd
    except ImportError:
        pass
    exec(code, ns)
    app = ns.get("app")
    if app is None:
        raise ValueError("Your cell must end by assigning \`app = fuaran.dashboard(...)\`.")
    return encode(app)
`;

/**
 * The cell the page opens with.
 *
 * `markdown(..., name="insight")` is deliberate and is what keeps the op ticker
 * honest: quick's ids derive from a node's kind, its human-meaningful label and an
 * occurrence index — never from the data — so a re-run over changed numbers yields
 * a short `UpdateProp` script against the SAME nodes. The narrative line is the one
 * node whose text is recomputed every run, so without an explicit name its id would
 * derive from the prose and a re-run would remove one node and insert another
 * instead of updating one.
 */
export const DEFAULT_CELL = [
  'df = pd.read_csv("sales.csv")',
  'totals = df.groupby("region")["revenue"].sum().sort_values(ascending=False)',
  '',
  'app = fuaran.dashboard("Regional revenue",',
  '    fuaran.metric_strip([(r, int(v)) for r, v in totals.items()]),',
  '    fuaran.markdown(f"**{totals.index[0]}** leads on revenue.", name="insight"),',
  '    fuaran.grid(df.to_dict("records")))',
].join('\n');

/** The default cell, for the F# page (Fable imports a function, not a binding). */
export function defaultCell(): string {
  return DEFAULT_CELL;
}

let pyodidePromise: Promise<any> | null = null;

async function boot(onProgress: (msg: string) => void): Promise<any> {
  onProgress('downloading CPython (~10 MB)…');
  const mod = await import(/* @vite-ignore */ `${PYODIDE_BASE}pyodide.mjs`);
  const pyodide = await mod.loadPyodide({ indexURL: PYODIDE_BASE });

  onProgress('loading pandas…');
  await pyodide.loadPackage(['pandas', 'micropip']);

  onProgress(`installing fuaran-ui ${FUARAN_UI_VERSION}…`);
  const micropip = pyodide.pyimport('micropip');
  try {
    // `deps: false` keeps the install to this one same-origin URL. Belt-and-braces
    // today — every Requires-Dist in the 0.7.0 wheel is gated behind an extra
    // (`dev`, `live-host`), so the runtime dependency set is EMPTY and a resolving
    // install would query nothing — but it means the day the package grows a
    // runtime dependency this fails loudly here instead of silently attempting an
    // egress the CSP then blocks.
    await micropip.install.callKwargs(new URL(`pandas/${FUARAN_UI_WHEEL}`, document.baseURI).href, {
      deps: false,
    });
  } finally {
    if (typeof micropip.destroy === 'function') micropip.destroy();
  }

  onProgress('bundling the dataset…');
  const csv = await fetch(new URL('pandas/sales.csv', document.baseURI)).then((r) => {
    if (!r.ok) throw new Error(`sales.csv ${r.status}`);
    return r.text();
  });

  // Put the CSV where `pd.read_csv("sales.csv")` will find it.
  pyodide.FS.writeFile('sales.csv', csv);
  pyodide.runPython(PY_BOOTSTRAP);
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
