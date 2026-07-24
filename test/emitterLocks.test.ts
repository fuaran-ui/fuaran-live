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
//      Reverted.
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
//    3. public/pandas/dash-host.py     `run_cell`       - the `fuaran` Python authoring
//       surface (metric/markdown/grid=DataGrid/dashboard). Headless here via a
//       pandas-free default cell.
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
//    - app/showcase/pandas-host.ts - the Pyodide loader glue that EXECUTES
//      dash-host.py; it authors no wire itself (the Python surface does, locked above).
//    - Every .fs showcase page built through the real `Fuaran.*` constructors +
//      `Fuaran.UI.Renderer` - canon-by-construction, the phase's explicit out-of-scope
//      class (the reference encoder is its own oracle).
// =============================================================================

import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { decodeNode, encodeNode } from '@fuaran-ui/ops';

import { encodeWireTs } from '../app/showcase/rosetta-hosts';

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

// ─── 3. Pandas Dashboard - the `fuaran` Python authoring surface (headless) ──
describe('emitter lock - Pandas Dashboard host (public/pandas/dash-host.py)', () => {
  it.skipIf(noPy)(pyTitle('run_cell emits strictly-decodable, already-canonical wire'), () => {
    // A pandas-free default cell that exercises every builder: metric_strip
    // (value/source law + omit-when-default), markdown (bare-string Literal
    // collapse), grid (a real DataGrid over an embedded columnar frame via a
    // Transform source), and dashboard (bare-string heading).
    const cell = [
      'strip = fuaran.metric_strip([("Signups", 1280), ("Revenue", 42.5)])',
      'insight = fuaran.markdown("Revenue climbed after the promo.")',
      'grid = fuaran.grid([{"region": "North", "sales": 100}, {"region": "South", "sales": 205}])',
      'app = fuaran.dashboard("Sales snapshot", strip, insight, grid)',
    ].join('\n');
    const driver = [
      'import sys',
      'ns = {}',
      "exec(open(sys.argv[1], encoding='utf-8').read(), ns)",
      `cell = ${JSON.stringify(cell)}`,
      "sys.stdout.write(ns['run_cell'](cell))",
    ].join('\n');
    assertCanonical(runPyHost('public/pandas/dash-host.py', driver));
  });
});
