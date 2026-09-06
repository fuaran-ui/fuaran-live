// =============================================================================
//  In-page wire-emitter audit + CI locks (Phase 657).
//
//  WHY THIS FILE EXISTS. fuaran-live's showcase pages hand-author wire-format
//  JSON in three shapes - TypeScript object literals, Python dicts in the Pyodide
//  host files, and canned strings in F#/TS. No CI oracle ever decoded them, so a
//  canonical-format rev broke pages SILENTLY until a visitor clicked the demo. On
//  2026-07-23 three in-page emitters were caught mid-drift by humans: Rosetta's TS
//  encoder and its Python host emitted the pre-0.2.0 Metric shape (fixed eabe621),
//  and the Pandas Dashboard authoring surface emitted the retired `source` field
//  so every Run died on MISSING_FIELD at kind.value (fixed 763f572 / bc13d4f).
//
//  This file is the standing lock. Every CANONICAL in-page emitter is decoded here
//  with the real strict decoder (`@fuaran-ui/ops` decodeNode) and its output must
//  re-encode byte-for-byte through the real encoder (encodeNode) - i.e. the emitter
//  is (a) strictly decodable and (b) already canonical (decode -> re-encode is the
//  identity). That single assertion catches every 2026-07-23 drift class:
//    - the 0.2.0 value/source law (a Metric/LabelValueRow `source` field is a HARD
//      decode error - MISSING_FIELD at kind.value);
//    - bare-string Literal collapse (canonical label/text/heading are plain JSON
//      strings; a `{"$type":"Literal",...}` envelope survives decode but re-encodes
//      to the collapsed bare string, so the round-trip diverges);
//    - omit-when-default (an explicit emphasis/format/tone/weight re-encodes away,
//      so the round-trip diverges);
//    - filters unification (a stale filter shape fails decode or re-encodes off).
//
//  MUTATION-VERIFIED (each lock was proven to bite during development, then reverted):
//    - TS: flipping `value` -> `source` in rosetta-hosts.ts `encodeWireTs` -> decode
//      fails MISSING_FIELD at kind.value (lock red). Reverted.
//    - Python (rosetta): flipping the Metric `value` key -> `source` in
//      public/rosetta/py-host.py `_metric` -> decode fails MISSING_FIELD (lock red).
//      Reverted.
//    - Python (pandas): re-introducing a `{"$type":"Literal","text":...}` envelope
//      for the Markdown text in public/pandas/dash-host.py `_lit` -> decode ok but
//      the round-trip re-encodes to the bare string, so `=== wire` fails (lock red).
//      Reverted. (That file is GONE as of Phase 1166 - the page now authors through
//      the published fuaran-py package, so there is no second encoder left to drift.
//      The mutation is kept here because it is the clearest statement of the class
//      the surviving locks still catch.)
//    - Wheel digest (Phase 1166): appending one byte to the vendored
//      public/pandas/fuaran_py-0.0.4-py3-none-any.whl -> the digest assertion is red.
//      Worth recording because the OTHER legs stayed green: zipimport tolerates
//      trailing bytes, so the corrupted wheel still imported and still emitted
//      canonical wire. The digest lock is the only thing standing between the page
//      and a wheel that is not the published one. Reverted.
//    - Canonicality (Phase 1166): making PY_BOOTSTRAP return
//      `json.dumps(json.loads(encode(app)), sort_keys=False)` -> decode ok, re-encode
//      differs on key order and separators, `=== wire` fails (lock red). Reverted.
//    - Id stability (Phase 1166): dropping `name="insight"` from the locked cell's
//      markdown call -> the id derives from the recomputed prose instead, the two
//      runs' id sets diverge, the stability lock is red. That is exactly the hazard
//      quick.markdown's own docstring warns about, and the reason the page's
//      DEFAULT_CELL passes a name. Reverted.
//
//  ---------------------------------------------------------------------------
//  THE INVENTORY (every surface that could hand-author wire; the deliverable). Re-run
//  the sweep with: grep -rn '\$type' app/showcase/*.ts public/**/*.py src/hosts/ app/*.fs
//  Classify each new hit before shipping it - a new canonical emitter without a lock
//  here is a review defect (see CLAUDE.md "Emitter-lock convention").
//
//  CANONICAL EMITTERS (locked below - decode-strict + canonical round-trip):
//    1. app/showcase/rosetta-hosts.ts  `encodeWireTs`   - independent TS Node encoder.
//       (Also byte-pinned against the Fable reference in rosettaParity.test.ts.)
//    2. public/rosetta/py-host.py      `rosetta_encode` - independent Python Node
//       encoder. Headless here; its Pyodide page-run is the same builder.
//    3. app/showcase/pandas-host.ts    `PY_BOOTSTRAP`   - the Pandas Dashboard cell
//       runner. Since Phase 1166 the page authors through the PUBLISHED fuaran-py
//       package (`fuaran_py.ui.quick`) rather than a hand-rolled shim, so this file
//       hand-authors no wire of its own - but it is still where the emitter LIVES
//       (the bootstrap that binds the cell's `fuaran` name and calls the package's
//       `encode`, plus the DEFAULT_CELL the page ships), which is why it moved out
//       of the exclusions below and into this list. Locked headlessly by exec'ing
//       that bootstrap against the VENDORED WHEEL - a pure-Python wheel is
//       importable straight off sys.path, so the lock runs the exact bytes the
//       browser installs, with no pip, no venv and no network. Three claims:
//       the round-trip is canonical; the vendored bytes are the published ones
//       (sha256 vs PyPI's JSON API); and the ids are STABLE across a data change,
//       which is the property the page's op ticker rests on.
//
//  LOCKED ELSEWHERE (a canonical emitter whose lock lives with its own suite):
//    4. app/navigator/StructuralEdit.fs `synthesiseJs` - the navigator's insert
//       palette builds a minimal-valid node per kind by walking the canonical schema
//       and emitting its REQUIRED fields as wire JSON. Locked in
//       structuralEdit.test.ts (it needs the Fable app output, which this file does
//       not otherwise load), and NOT by plain byte-identity: a required-fields-only
//       emission is a §16 lenient-accept shape, and the decoder normalises six kinds
//       upward (defaulted optionals `Chart.stacked` / `Tabs.activeIndex` /
//       `Stepper.onSelect`, and the typed empty payload of a slot-typed `Static`).
//       The lock asserts the two claims that survive that: strict decode reaching a
//       FIXED POINT in one normalisation pass, and normalisation being ADDITIVE ONLY
//       (every emitted key/value survives unchanged) - which is what catches the
//       surviving-Literal-envelope class this file exists for.
//       MUTATION-VERIFIED: returning `{"$type":"Literal",text}` from the string leg
//       of `synthesiseJs` -> decode ok, re-encode collapses to the bare string, the
//       emitted value does not survive (lock red). Reverted.
//
//  DELIBERATE EXCLUSIONS (named, with reasons):
//    - public/relay/py-host.py - a SHA-256 HASHER. It RECEIVES a station's wire bytes
//      and returns hashlib's digest; it authors no wire. (relay-hosts.ts is the same:
//      SubtleCrypto hashing + a Pyodide loader, no wire.)
//    - src/hosts/parityFixtures.ts - imports the authoritative `wire-format-fixtures/`
//      corpus files verbatim (`?raw`). Canon-by-construction: the corpus IS the oracle
//      (its own conformance harness certifies it); nothing is hand-authored here.
//    - app/showcase/WireVersioning.fs - canon-by-construction: the artefact is built
//      through the REAL `Fuaran.Core.Wire` canonical encoder (`Canon.typed` +
//      `Versioning.render`), not a hand-typed string. The reference encoder is its own
//      oracle (the phase's stated out-of-scope class).
//    - app/showcase/Bouncer.fs - ADVERSARIAL reject fixtures: each string is a hostile
//      payload the page runs through the SHIPPED decoder to demonstrate it is REFUSED
//      (assert-reject, not assert-canonical). A canonical round-trip lock is
//      inapplicable, and the payloads are `private` F# literals not exported to the
//      Fable surface - so the lock stays page-manual (the Rosetta Pyodide-leg
//      precedent). The decode-reject IS the live demo.
//    - app/showcase/GoSessions.fs - hand-authored OP wire (`editHeadlineOp` /
//      `invalidOp`) in the lenient-accept form a client sends to a BYOS server. The
//      RESOLVED projections these produce are already locked headlessly by
//      goSessions.test.ts (every recorded frame decoded through the real decoder);
//      the strings are `private` [<Literal>]s not exported.
//    - app/Agent.fs - scripted-provider fixtures emulating an LLM's emissions
//      (`runEmitRepairProbe` etc.). The verbose `{"$type":"Literal",...}` form is
//      INTENTIONAL - it is what a model emits and what the lenient decoder normalises;
//      a canonical round-trip lock would wrongly reject the deliberate lenient shape.
//      Decodability is exercised by agentLoop.test.ts.
//    - app/SystemPrompt.fs - the LLM instruction prompt. The wire teaching is the
//      language repo's drift-checked prompt pack inlined at build time (`?raw`
//      sibling import, corpus-generated examples); the module hand-authors only the
//      chat-surface overlay's two CodeBlock/Math examples. All of it is certified
//      elsewhere: closedLoop.test.ts ingests every fenced example through the real
//      loop, and promptPack.test.ts pins the pack-byte sourcing itself.
//    - app/showcase/{degradation,infinite-skins-audit,kintsugi-senses,surveyor-measure,
//      teleport-qr}.ts - DOM read-back / geometry / QR helpers; author no wire.
//      (app/showcase/pandas-host.ts was excluded here as "loader glue" until Phase
//      1166 moved the authoring surface into it; it is emitter 3 above now.)
//    - app/showcase/AgentReadable.fs - authors NO wire: its page tree is built through
//      the real `Fuaran.*` constructors, and the JSON it does emit is a different
//      vocabulary entirely (the declared-affordance `data-fuaran-*` attributes), minted
//      by `Fuaran.Core`'s canonical encoder over the shipped affordance types' own
//      `toWire` projections. That payload is nonetheless a contract a reader parses, so
//      it carries its own lock in agentReadable.test.ts (closed-set tokens, payload
//      shapes, and the omitted-open-bound property), with a go-red self-test.
//    - app/Contribute.fs - the session-corpus bundle builder. It hand-authors no wire:
//      the trees it embeds are produced by the REAL `CanonicalJson` encoder and the ops
//      are the canonical documents the session already recorded, so both are
//      canon-by-construction. The envelope AROUND them is a sidecar document (kind /
//      version / metadata / two trees / an op array), not a wire-format document, and a
//      canonical round-trip lock is inapplicable to it. Its own contract - every member,
//      the embedded op's `$type`, and the members that must NOT be there - is pinned in
//      corpusSink.test.ts.
//    - Every .fs showcase page built through the real `Fuaran.*` constructors +
//      `Fuaran.UI.Renderer` - canon-by-construction, the phase's explicit out-of-scope
//      class (the reference encoder is its own oracle).
// =============================================================================

import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { decodeNode, encodeNode } from '@fuaran-ui/ops';

import { encodeWireTs } from '../app/showcase/rosetta-hosts';
import {
  DEFAULT_CELL,
  FUARAN_PY_VERSION,
  FUARAN_PY_WHEEL,
  FUARAN_PY_WHEEL_SHA256,
  PY_BOOTSTRAP,
} from '../app/showcase/pandas-host';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

// The six typed holes the Rosetta page ships as its default state - the same
// defaults rosettaParity.test.ts pins the byte-parity strip against.
const defaultHoles = {
  labelA: 'Signups',
  valueA: 1280,
  labelB: 'Revenue',
  valueB: 42.5,
  labelC: 'Churn %',
  valueC: 12.4,
};

/**
 * The strongest available lock for a canonical emitter: the hand-authored wire
 * must decode through the real strict decoder AND re-encode to itself. Decode
 * failure catches a hard-rejected retired field (the value/source law); a
 * re-encode mismatch catches a non-canonical-but-decodable shape (a surviving
 * Literal envelope, an explicit default, a stale layout).
 */
function assertCanonical(wire: string): void {
  const r = decodeNode(wire);
  if (!r.ok) {
    throw new Error(`emitter output did not decode: ${r.error.code} - ${r.error.message ?? ''}`);
  }
  expect(encodeNode(r.value)).toBe(wire);
}

// ─── Python interpreter discovery (headless-lock precondition) ────────────────
// The py host files are stdlib-only, so we spawn a plain interpreter and exec the
// file. When no interpreter exists (a CI image without Python), the Python legs
// SKIP with a named reason rather than fail - the established skip convention.
function findPython(): string | null {
  for (const bin of ['python', 'python3']) {
    const probe = spawnSync(bin, ['--version'], { encoding: 'utf8' });
    if (!probe.error && probe.status === 0) return bin;
  }
  return null;
}
const pythonBin = findPython();
const noPy = pythonBin === null;
const noPyReason =
  'no python interpreter on PATH (python/python3) - Pyodide page-run is the fallback';
// Surface the skip reason in the test title (vitest shows the title on a skip).
const pyTitle = (t: string): string => (noPy ? `${t} [SKIPPED: ${noPyReason}]` : t);

/** Exec a py host file headlessly via a driver and return its stdout. */
function runPyHost(hostRelPath: string, driver: string): string {
  const res = spawnSync(pythonBin as string, ['-c', driver, join(repoRoot, hostRelPath)], {
    encoding: 'utf8',
  });
  if (res.status !== 0) {
    throw new Error(`python host run failed (${hostRelPath}): ${res.stderr || res.stdout}`);
  }
  return res.stdout;
}

// ─── 1. Rosetta - independent TypeScript Node encoder ────────────────────────
describe('emitter lock - Rosetta TS encoder (app/showcase/rosetta-hosts.ts)', () => {
  it('emits strictly-decodable, already-canonical wire', () => {
    assertCanonical(encodeWireTs(defaultHoles));
  });
});

// ─── 2. Rosetta - independent Python Node encoder (headless) ─────────────────
describe('emitter lock - Rosetta Python host (public/rosetta/py-host.py)', () => {
  it.skipIf(noPy)(
    pyTitle('emits strictly-decodable, already-canonical wire; equals the TS host'),
    () => {
      const driver = [
        'import sys, json',
        'ns = {}',
        "exec(open(sys.argv[1], encoding='utf-8').read(), ns)",
        `holes = ${JSON.stringify(defaultHoles)}`,
        "sys.stdout.write(ns['rosetta_encode'](json.dumps(holes)))",
      ].join('\n');
      const out = JSON.parse(runPyHost('public/rosetta/py-host.py', driver)) as { wire: string };
      assertCanonical(out.wire);
      // Cross-host: the Python emitter must agree byte-for-byte with the TS emitter,
      // which is itself byte-pinned against the F# reference in rosettaParity.test.ts.
      expect(out.wire).toBe(encodeWireTs(defaultHoles));
    },
  );
});

// ─── 3. Pandas Dashboard - the published-package cell runner (headless) ──────
//
// Phase 1166. The page's Python is the published `fuaran-py` package plus two
// things this repo authors: the bootstrap that binds the cell's `fuaran` name and
// calls the package's `encode` (PY_BOOTSTRAP), and the cell the page opens with
// (DEFAULT_CELL). Both are imported from the shipped module rather than restated
// here, so the lock cannot drift from the page by copy.
//
// The install is deliberately NOT a pip install: the wheel committed under
// public/pandas/ is pure Python, so putting it on `sys.path` imports it as a zip.
// The lock therefore runs the exact bytes the browser installs, offline, with no
// interpreter setup at all beyond CPython itself.
describe('emitter lock - Pandas Dashboard host (app/showcase/pandas-host.ts)', () => {
  const wheelPath = join(repoRoot, 'public/pandas', FUARAN_PY_WHEEL);

  // A pandas-free cell exercising every builder the DEFAULT_CELL uses: metric_strip
  // (value/source law + omit-when-default), markdown (bare-string Literal collapse),
  // grid (a real DataGrid over an embedded columnar frame via a Transform source),
  // and dashboard (bare-string heading). Pandas-free because pandas is not
  // installable in a plain CPython, and a lock that skips on the machines that run
  // it is not a lock; `pd` is bound only when pandas imports, which is exactly what
  // makes the same bootstrap runnable here.
  const cell = (north: number, south: number, insight: string): string =>
    [
      `strip = fuaran.metric_strip([("Signups", ${north}), ("Revenue", ${south})])`,
      `insight = fuaran.markdown(${JSON.stringify(insight)}, name="insight")`,
      'grid = fuaran.grid([{"region": "North", "sales": 100}, {"region": "South", "sales": 205}])',
      'app = fuaran.dashboard("Sales snapshot", strip, insight, grid)',
    ].join('\n');

  /** Exec PY_BOOTSTRAP against the vendored wheel and run `cellSource` through it. */
  function runCell(cellSource: string): string {
    const driver = [
      'import sys',
      'sys.path.insert(0, sys.argv[1])',
      `bootstrap = ${JSON.stringify(PY_BOOTSTRAP)}`,
      `cell = ${JSON.stringify(cellSource)}`,
      'ns = {}',
      'exec(bootstrap, ns)',
      "sys.stdout.write(ns['run_cell'](cell))",
    ].join('\n');
    const res = spawnSync(pythonBin as string, ['-c', driver, wheelPath], { encoding: 'utf8' });
    if (res.status !== 0) {
      throw new Error(`pandas-host cell run failed: ${res.stderr || res.stdout}`);
    }
    return res.stdout;
  }

  // Not skipped: reading a committed file needs no interpreter. This is the pin that
  // makes "the published package" a checkable claim rather than a comment - a wheel
  // built locally, or a version bumped in the filename and not the digest, fails here.
  it('the vendored wheel is the exact artefact PyPI published', () => {
    expect(FUARAN_PY_WHEEL).toBe(`fuaran_py-${FUARAN_PY_VERSION}-py3-none-any.whl`);
    const bytes = readFileSync(wheelPath);
    expect(createHash('sha256').update(bytes).digest('hex')).toBe(FUARAN_PY_WHEEL_SHA256);
  });

  it.skipIf(noPy)(
    pyTitle('the shipped default cell is valid Python for the interpreter the package targets'),
    () => {
      const driver = [
        'import sys',
        `cell = ${JSON.stringify(DEFAULT_CELL)}`,
        "compile(cell, '<default-cell>', 'exec')",
        "sys.stdout.write('ok')",
      ].join('\n');
      const res = spawnSync(pythonBin as string, ['-c', driver], { encoding: 'utf8' });
      expect(res.stderr).toBe('');
      expect(res.status).toBe(0);
    },
  );

  it.skipIf(noPy)(
    pyTitle('the cell runner emits strictly-decodable, already-canonical wire'),
    () => {
      assertCanonical(runCell(cell(1280, 42.5, 'Revenue climbed after the promo.')));
    },
  );

  // Task 2 of the phase, as a property rather than a claim. quick's ids derive from
  // kind + label + occurrence and NEVER from the data, so a re-run over changed
  // numbers and changed prose must address the same nodes - which is what lets the
  // page derive a short UpdateProp script and say "patched, not re-rendered". If a
  // future release derived ids from content, the ticker would silently become a
  // remove-and-insert storm; this fails instead.
  it.skipIf(noPy)(pyTitle('node ids are stable across a re-run over changed data'), () => {
    const idsOf = (wire: string): string[] =>
      [...wire.matchAll(/"id":"([^"]+)"/g)].map((m) => m[1] ?? '').sort();
    const first = runCell(cell(1280, 42.5, 'Revenue climbed after the promo.'));
    const second = runCell(cell(9310, 88.1, 'Revenue fell back in the second week.'));
    expect(first).not.toBe(second);
    expect(idsOf(second)).toEqual(idsOf(first));
  });
});
