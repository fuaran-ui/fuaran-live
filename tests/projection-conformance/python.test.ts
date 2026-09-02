// Codegen-conformance — Python arm (single-process CPython executor).
//
// For every Node fixture in the workspace wire-format-fixtures/ corpus:
//   1. project the canonical wire JSON to `fuaran_py.ui` authoring source via
//      the F#/Fable projector (app/Projection.fs, Fable-compiled to
//      app/output/Projection.js);
//   2. EXECUTE the generated source against the real `fuaran_py` surface —
//      every fixture in ONE CPython process, not one spawn each;
//   3. re-encode via `fuaran_py.ui.encode` and assert the JSON is
//      byte-identical to the fixture.
//
// The sibling TypeScript arm's shape, with one structural difference forced by
// the host: `fuaran_py`'s `encode` calls `.to_wire()` on the root, and its
// structural `Obj` has no such method, so there is no escape hatch a construct
// outside the typed model can take. Where the TS leg can always emit a typed
// in-memory literal, the Python leg cannot — see PY_UNMODELLED below.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/,
// and a CPython with `fuaran-py` installed (see resolvePython).

import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

import { beforeAll, describe, expect, it } from 'vitest';

// Fable-generated JS — no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { projectPythonExpr } from '../../app/output/Projection.js';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '../..');
const corpusDir = resolve(repoRoot, '../wire-format-fixtures');

interface ManifestEntry {
  readonly id: string;
  readonly kind: string;
  readonly inputFile: string;
}

const manifest = JSON.parse(readFileSync(resolve(corpusDir, 'manifest.json'), 'utf8')) as {
  fixtures: ManifestEntry[];
};

const nodeFixtures = manifest.fixtures.filter((f) => f.kind === 'node-round-trip');

/**
 * The interpreter to run the executor with. A repo-local `.venv` wins (the
 * documented local setup: `python -m venv .venv` then
 * `pip install fuaran-py==0.0.1`); FUARAN_PY_PYTHON overrides it for CI, where
 * the interpreter is whatever actions/setup-python provisioned.
 */
const resolvePython = (): string => {
  const override = process.env.FUARAN_PY_PYTHON;
  if (override) return override;

  const candidates = [
    resolve(repoRoot, '.venv/Scripts/python.exe'),
    resolve(repoRoot, '.venv/bin/python'),
  ];
  for (const c of candidates) if (existsSync(c)) return c;

  return process.platform === 'win32' ? 'python' : 'python3';
};

// Constructs the corpus reaches that `fuaran_py`'s typed authoring surface does
// not model, so the projection cannot be exact. Named, not counted: each entry
// says WHICH Python construct is absent, so the day fuaran-py grows it the entry
// is removable by search rather than by re-derivation.
//
// This is the shape the sibling TS arm's own note sanctions for a slot with no
// reachable constructor and no literal form — with the difference that in the TS
// tier that case is rare (an in-memory object literal is always available) and
// here it is structural: `encode` requires a `.to_wire()` root, so a kind or a
// binding the typed model omits has NO spelling at all.
//
// Dated 2026-09-02, measured against fuaran-py 0.0.1 (PyPI) — the newest
// published release. Every entry below was verified to be absent from BOTH that
// release and the current development tree, so none of them is a pin-lag
// artefact.
// Every entry below was measured, not assumed: the projector emits the shape the
// typed model WOULD take, and the executor's failure — an `AttributeError` naming
// an absent class, or a byte difference in a slot the record cannot carry — is
// what identified the cause. The causes fall into four families:
//
//   1. NO TYPED KIND. `encode` requires a `.to_wire()` root, so a node kind the
//      model omits has no spelling at all: Mount, Fact, Drawing.
//   2. NO TYPED BINDING / ACTION. Query, I18n and Invoke bindings; Call, AiTool
//      and Invoke actions.
//   3. A HARDCODED CLOSURE SENTINEL. Several records emit `onChange` / `onToggle`
//      / `onSelect` / `value` unconditionally, so the canonical minimal control
//      (`{"$type":"Text"}`) and the declarative field-named grid column cannot be
//      reached — the slot is not optional in the record, and the encoder has
//      nothing to omit.
//   4. A NARROWER RECORD. `Chart` reaches six slots fewer than the wire;
//      `TransformBinding` carries neither `params` nor a `Live` source;
//      `DataGrid` carries none of the declarative sort / page / edit-state slots;
//      `Link` has no `protection`; `Table` no `sortable` / `defaultSort`; `Media`
//      no `tracks` / `transcript`.
//
// None of this is projector lag, and none of it is fixable in this repo — which
// is why these are named with their cause rather than left to fail. The day
// fuaran-py grows one of these constructs, the entries naming it are removable by
// search, and the test below FAILS if one starts round-tripping while still
// listed, so the set cannot quietly outlive its cause.
//
// Dated 2026-09-02, measured against fuaran-py 0.0.1 (PyPI) — the newest
// published release. Each absent construct was checked against the current
// development tree as well, so no entry here is a pin-lag artefact.
const PY_UNMODELLED = new Map<string, string>([
  // 1 — no typed node kind.
  ['mount-1', 'no t.Mount'],
  ['mount-2', 'no t.Mount'],
  ['fact-1', 'no t.Fact'],
  ['now-environment-binding', 'no t.Fact'],
  ['master-detail-multi-field', 'no t.Fact'],
  ['master-detail-preselected', 'no t.Fact'],
  ['master-detail-preselected-second-row', 'no t.Fact'],
  ['drawing-1', 'no t.Drawing'],
  ['drawing-empty', 'no t.Drawing'],
  ['drawing-nonfinite-sentinels', 'no t.Drawing'],
  ['drawing-rotated-labels', 'no t.Drawing'],
  ['drawing-tipped-shapes', 'no t.Drawing'],

  // 2 — no typed binding / action case.
  ['query-dependson', 'no Binding.Query'],
  ['metric-invoke', 'no Binding.Invoke'],
  ['image-caption-i18n-1', 'no TextSource.I18n'],
  ['btn-invoke', 'no Action.Invoke'],
  ['btn-json-payloads', 'no Action.AiTool'],
  ['call-into', 'no Action.Call'],

  // 3 — a hardcoded closure sentinel the record cannot omit.
  ['form-declarative', 'TextField hardcodes onChange'],
  ['form-declarative-minimal', 'TextField hardcodes onChange'],
  ['form-field-rules', 'TextField hardcodes onChange'],
  ['composite-tabs-panels', 'TextField hardcodes onChange'],
  ['form-toggle', 'CheckboxField hardcodes onToggle'],
  ['form-date-range', 'DateRangeField hardcodes onChange'],
  ['filters-declarative', 'TextFilter hardcodes onChange'],
  ['filters-date-range', 'DateRangeField hardcodes onChange'],
  ['frag-stdlib-filter-bar', 'TextFilter hardcodes onChange'],
  ['filterable-static-dashboard', 'ChoiceFilter hardcodes onChange'],
  ['multiselect-chip-list-param', 'Select hardcodes onChange'],
  ['controls-declarative', 'Tabs hardcodes onSelect'],
  ['controls-closure', 'Tabs has no onSelectTag'],
  ['grid-bound-sort', 'Column has no field, hardcodes value'],
  ['grid-declared-edit', 'Column has no field, hardcodes value'],
  ['grid-editable-state', 'Column has no field, hardcodes value'],
  ['grid-field-named', 'Column has no field, hardcodes value'],
  ['grid-paged', 'Column has no field, hardcodes value'],
  ['grid-paged-sorted', 'Column has no field, hardcodes value'],
  ['grid-reorderable', 'Column has no field, hardcodes value'],
  ['grid-sort-state-key', 'Column has no field, hardcodes value'],
  ['grid-toned-pill', 'Column has no field, hardcodes value'],
  ['scalar-transform-composition', 'Column has no field, hardcodes value'],
  ['shared-source-seeded-pair', 'Column has no field, hardcodes value'],
  ['switch-on-selection', 'Column has no field, hardcodes value'],

  // 4 — a record narrower than the wire.
  ['chart-axis-titles', 'Chart has no subtitle / xTitle / yTitle'],
  ['chart-data-labels', 'Chart has no dataLabels'],
  ['chart-legend-position', 'Chart has no legendPosition'],
  ['chart-temporal-x', 'Chart has no xScale'],
  ['chart-value-format', 'Chart has no valueFormat'],
  ['badge-transform-live', 'TransformBinding has no Live source'],
  ['grid-transform-param', 'TransformBinding has no params'],
  ['link-protected-1', 'Link has no protection'],
  ['table-sortable-1', 'Table has no sortable / defaultSort'],
  ['media-audio-transcript-1', 'Media has no transcript'],
  ['media-video-captions-1', 'Media has no tracks'],
  ['media-video-tracks-2', 'Media has no tracks'],
]);

interface ExecResult {
  readonly id: string;
  readonly ok: boolean;
  readonly encoded?: string;
  readonly error?: string;
}

let executed: Map<string, ExecResult> = new Map();
let fatal: string | undefined;

beforeAll(() => {
  const cases = nodeFixtures.map((f) => ({
    id: f.id,
    expr: projectPythonExpr(readFileSync(resolve(corpusDir, f.inputFile), 'utf8').trim()) as string,
  }));

  const proc = spawnSync(resolvePython(), [resolve(here, 'python_exec.py')], {
    input: JSON.stringify({ cases }),
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  });

  if (proc.error) {
    fatal = `could not run the Python executor (${resolvePython()}): ${proc.error.message}`;
    return;
  }
  if (proc.status !== 0) {
    fatal = `the Python executor exited ${proc.status}:\n${proc.stderr}`;
    return;
  }

  const payload = JSON.parse(proc.stdout) as { fatal?: string; results?: ExecResult[] };
  if (payload.fatal) {
    fatal = payload.fatal;
    return;
  }

  executed = new Map((payload.results ?? []).map((r) => [r.id, r]));
}, 300_000);

describe('Python projection conformance (Node corpus)', () => {
  it('the Python executor ran', () => {
    // A hard failure, never a skip: a conformance arm that goes green without
    // its oracle is worse than no arm at all. Install the host with
    // `python -m venv .venv && .venv/…/pip install fuaran-py==0.0.1`, or point
    // FUARAN_PY_PYTHON at an interpreter that already has it.
    expect(fatal, fatal ?? '').toBeUndefined();
  });

  it('the corpus is present and non-trivial', () => {
    expect(nodeFixtures.length).toBeGreaterThanOrEqual(70);
  });

  it('every unmodelled id names a real fixture', () => {
    const ids = new Set(nodeFixtures.map((f) => f.id));
    for (const q of PY_UNMODELLED.keys()) {
      expect(ids.has(q), `unmodelled '${q}' is not in the corpus — remove it`).toBe(true);
    }
  });

  for (const f of nodeFixtures) {
    const wireOf = () => readFileSync(resolve(corpusDir, f.inputFile), 'utf8').trim();

    if (PY_UNMODELLED.has(f.id)) {
      it(`${f.id} is unmodelled by fuaran_py (${PY_UNMODELLED.get(f.id)})`, () => {
        const result = executed.get(f.id);
        if (!result?.ok) return; // still un-projectable — the entry holds
        expect(
          result.encoded,
          `'${f.id}' now round-trips — fuaran-py grew the construct; REMOVE it from PY_UNMODELLED`,
        ).not.toBe(wireOf());
      });
    } else {
      it(`${f.id} round-trips byte-identically`, () => {
        const result = executed.get(f.id);
        expect(result, `no executor result for '${f.id}'`).toBeDefined();
        expect(result!.ok, `projected Python for ${f.id} failed to execute: ${result!.error}`).toBe(
          true,
        );
        expect(
          result!.encoded,
          `projected Python source for ${f.id} must re-encode byte-identically`,
        ).toBe(wireOf());
      });
    }
  }
});
