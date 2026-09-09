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
// in-memory literal, the Python leg cannot — which is why this arm carries every
// standing quarantine entry and the TS arm carries none. Both now live in the ONE
// shared table (`./quarantine.ts`, Phase 1584); the entries, the token grammar
// and the measurement history are all recorded there.
//
// Three slots typed as a raw wire `Value` (`UiNode.visible`, `SwitchCase.when`,
// `Navigate.route`) would accept a hand-built `Obj` and so would let the
// projector spell anything at all, which would dissolve this whole map. The
// projector's own rule — stated where it is enforced, in app/Projection.fs's
// Python-leg header — is to build every such value from a TYPED RECORD and lower
// it with that record's `to_wire`, so an absent case stays absent and this arm
// keeps measuring what it claims to measure.
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

import { entriesFor, registerQuarantineChecks, type ConstructVerdict } from './quarantine';

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
 * `pip install fuaran-py==0.4.0`); FUARAN_PY_PYTHON overrides it for CI, where
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

// This arm's slice of the SHARED quarantine (Phase 1584). The table itself,
// the construct-token grammar and the six dated measurement passes that produced
// the standing entries all live in ./quarantine.ts, keyed by fixture id with one
// entry per arm — so a fixture unmodelled here but fine on the TypeScript arm is
// a row with one empty cell rather than two files to compare.
//
// What is worth repeating HERE, because it is about this host rather than about
// the table: none of the standing entries is projector lag and none is fixable in
// this repo. `encode` requires a `.to_wire()` root, so a kind or a binding the
// typed model omits has NO spelling at all — where the TS leg can always fall back
// to an in-memory object literal. Closing an entry is a matter of teaching
// `fuaran-py` the named construct and raising the pin; the falsifier below is what
// stops an entry outliving the gap it names.
const pyQuarantine = entriesFor('python');

interface ExecResult {
  readonly id: string;
  readonly ok: boolean;
  readonly encoded?: string;
  readonly error?: string;
}

let executed: Map<string, ExecResult> = new Map();
let projected: Map<string, string> = new Map();
let constructs: Record<string, ConstructVerdict> = {};
let fatal: string | undefined;

beforeAll(() => {
  const cases = nodeFixtures.map((f) => ({
    id: f.id,
    expr: projectPythonExpr(readFileSync(resolve(corpusDir, f.inputFile), 'utf8').trim()) as string,
  }));
  projected = new Map(cases.map((c) => [c.id, c.expr]));

  // Every token any entry names, host- and projector-side alike, resolved in the
  // same process that executes the corpus — so the probe and the round-trip can
  // never disagree about which interpreter they measured.
  const tokens = [
    ...new Set(
      [...pyQuarantine.values()].flatMap((q) =>
        q.projectorConstruct ? [q.construct, q.projectorConstruct] : [q.construct],
      ),
    ),
  ];

  const proc = spawnSync(resolvePython(), [resolve(here, 'python_exec.py')], {
    input: JSON.stringify({ cases, constructs: tokens }),
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

  const payload = JSON.parse(proc.stdout) as {
    fatal?: string;
    results?: ExecResult[];
    constructs?: Record<string, ConstructVerdict>;
  };
  if (payload.fatal) {
    fatal = payload.fatal;
    return;
  }

  executed = new Map((payload.results ?? []).map((r) => [r.id, r]));
  constructs = payload.constructs ?? {};
}, 300_000);

describe('Python projection conformance (Node corpus)', () => {
  it('the Python executor ran', () => {
    // A hard failure, never a skip: a conformance arm that goes green without
    // its oracle is worse than no arm at all. Install the host with
    // `python -m venv .venv && .venv/…/pip install fuaran-py==0.4.0`, or point
    // FUARAN_PY_PYTHON at an interpreter that already has it.
    expect(fatal, fatal ?? '').toBeUndefined();
  });

  it('the corpus is present and non-trivial', () => {
    expect(nodeFixtures.length).toBeGreaterThanOrEqual(70);
  });

  for (const f of nodeFixtures) {
    // A quarantined fixture's falsifier and self-clearing checks are registered
    // from the shared table below; only the required byte round-trip is left here.
    if (pyQuarantine.has(f.id)) continue;

    it(`${f.id} round-trips byte-identically`, () => {
      const result = executed.get(f.id);
      expect(result, `no executor result for '${f.id}'`).toBeDefined();
      expect(result!.ok, `projected Python for ${f.id} failed to execute: ${result!.error}`).toBe(
        true,
      );
      expect(
        result!.encoded,
        `projected Python source for ${f.id} must re-encode byte-identically`,
      ).toBe(readFileSync(resolve(corpusDir, f.inputFile), 'utf8').trim());
    });
  }
});

// -- The shared quarantine's Python arm (Phase 1584) -------------------------
//
// The census, the "names a real fixture" check, the construct-token resolution
// check, and per entry the 1578 falsifier + self-clearing check, all registered
// from `./quarantine.ts` — the same implementation the TypeScript arm invokes,
// so the two arms cannot drift into checking their tables differently, which is
// how they came to mean two different things by `arm` in the first place.
//
// Everything this arm supplies below is resolved in the SAME CPython process
// that executes the corpus (`python_exec.py`), so a probe's verdict and a
// round-trip's outcome can never disagree about which interpreter they measured.
const pyFixtureById = new Map(nodeFixtures.map((f) => [f.id, f]));

registerQuarantineChecks({
  arm: 'python',
  fixtureIds: nodeFixtures.map((f) => f.id),
  wireOf: (id) => readFileSync(resolve(corpusDir, pyFixtureById.get(id)!.inputFile), 'utf8').trim(),
  projectedSource: (id) => projected.get(id),
  hostModels: (token) => constructs[token],
  roundTrip: (id) => {
    const result = executed.get(id);
    return result?.ok === true ? { ok: true, encoded: result.encoded } : { ok: false };
  },
  blocked: () => fatal,
});
