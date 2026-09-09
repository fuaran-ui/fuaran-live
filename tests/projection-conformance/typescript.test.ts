// Codegen-conformance — TypeScript arm (in-process executor).
//
// For every Node fixture in the workspace wire-format-fixtures/ corpus:
//   1. project the canonical wire JSON to @fuaran-ui/ui smart-constructor
//      source via the F#/Fable projector (app/Projection.fs, Fable-compiled to
//      app/output/Projection.js);
//   2. EXECUTE the generated source (evaluated against the real @fuaran-ui/ui
//      surface) to reconstruct an in-memory Node;
//   3. re-encode it via the canonical encoder and assert the JSON is
//      byte-identical to the fixture.
//
// This is the validation engine that keeps the TS projection honest: any drift
// between the projector and the @fuaran-ui/ui contract fails the gate. It
// restores the verified byte-round-trip guarantee for the TypeScript leg that
// the pre-rebuild TS-shell harness carried. The Python leg has its own arm
// beside this one (python.test.ts); the F# / C# / VB legs remain demo-grade —
// see docs/PROJECTION_FIDELITY.md.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

// Namespace imports rather than named ones, since Phase 1584: the shared
// quarantine's TypeScript construct resolver answers "does the pinned host model
// X?" by looking a symbol up on these surfaces, and it can only do that honestly
// if it holds the whole module rather than the handful of names destructured
// below. `filterKind` was a named import here until 2026-08-30 and no longer
// exists in `@fuaran-ui/ui` — nothing broke, which is the hazard worth naming:
// vitest transpiles via esbuild, so a missing export resolves to `undefined` and
// is passed into the evaluated source as a silently dead binding rather than a
// load error. Only strict Node ESM refuses it. Keep the destructured list to
// names the projector actually emits.
import * as ops from '@fuaran-ui/ops';
import * as ui from '@fuaran-ui/ui';
import type { Node } from '@fuaran-ui/schema';

import {
  emissionPattern,
  entriesFor,
  registerQuarantineChecks,
  resolveTypeScriptConstruct,
  type ConstructVerdict,
} from './quarantine';

const { encodeNode } = ops;
const { fuaran, binding, action, format, formFieldKind, nodeId, iconSource } = ui;

// Fable-generated JS — no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import {
  projectTypeScriptExpr,
  projectByName,
  absentRequiredMembers,
} from '../../app/output/Projection.js';

const here = dirname(fileURLToPath(import.meta.url));
const corpusDir = resolve(here, '../../../wire-format-fixtures');

interface ManifestEntry {
  readonly id: string;
  readonly kind: string;
  readonly inputFile: string;
}

const manifest = JSON.parse(readFileSync(resolve(corpusDir, 'manifest.json'), 'utf8')) as {
  fixtures: ManifestEntry[];
};

const nodeFixtures = manifest.fixtures.filter((f) => f.kind === 'node-round-trip');

/** Evaluate a projected expression against the real @fuaran-ui/ui surface. */
const evalExpr = (expr: string): Node<unknown> => {
  const factory = new Function(
    'fuaran',
    'binding',
    'action',
    'format',
    'formFieldKind',
    'nodeId',
    'iconSource',
    `return (${expr});`,
  );
  return factory(
    fuaran,
    binding,
    action,
    format,
    formFieldKind,
    nodeId,
    iconSource,
  ) as Node<unknown>;
};

// The quarantine is the SHARED table (Phase 1584) — `./quarantine.ts`, one row
// per fixture with a cell per arm, so a fixture quarantined here and not on the
// Python arm is readable in the row rather than by diffing two files. This arm's
// slice is EMPTY as of 2026-08-30 and its emptiness is the assertion: every
// node-round-trip fixture is required to re-encode byte-identically, so a
// projector that falls behind the corpus fails by name rather than being
// absorbed into a list. Why it is empty, and the two failure shapes this arm's
// shortfalls take, are recorded in that file's header beside the Python arm's.
//
// If a future corpus addition lands here as a failure, the choice is to teach
// the projector or — where a slot genuinely has no reachable ctor and no literal
// form — to add a `{ arm: 'typescript', … }` entry with its construct token and
// reason. Prefer teaching it: the previous list decayed for eight days precisely
// because a list is easier to append to than an emitter is to extend.
const tsQuarantine = entriesFor('typescript');

// -- Omit-at-default cover (Phase 1603) --------------------------------------
//
// A member the canonical encoder OMITS AT ITS DEFAULT is this projector's
// sharpest failure mode, because the omitted form is the one a byte comparison
// can fail on while the spelled form sails through. `Tabs.activeIndex` is the
// worked example: the projector read it with the total accessor, handed the
// resulting absence to the binding projector, and got back an EXPLICIT `Static`
// that the builder's own `?? Static 0` could no longer fill and the encoder's
// `value === 0` test could no longer drop -- so a key the fixture does not have
// survived the re-encode, on three fixtures at once, and read as a package-pin
// lag rather than as a projector defect.
//
// The loop below already round-trips every node fixture, so this cover is not
// about reaching more fixtures; it is about making the omitted side NAMED and
// NON-VACUOUS. Per slot it requires the corpus to exercise BOTH sides -- at
// least one fixture that omits the member and at least one that spells it --
// and requires neither side to be quarantined. Without that, a corpus in which
// every carrier happened to spell the slot would pass this arm with the omitted
// path never executed, which is precisely the state that let the defect ship.
//
// A NEW omit-at-default slot adds a row here in the same change-set that
// teaches the encoder to omit it. `Navigate.target` is listed beside
// `Tabs.activeIndex` because `Navigate` already omits it at `Self`, and it is
// the slot the next binding-level omission lands on -- covered before it lands.
interface OmitAtDefault {
  /** The member the encoder drops when it holds the slot's identity value. */
  readonly slot: string;
  /** The `$type` of the object that carries it. */
  readonly carrier: string;
}

const OMIT_AT_DEFAULT: readonly OmitAtDefault[] = [
  { slot: 'activeIndex', carrier: 'Tabs' },
  { slot: 'target', carrier: 'Navigate' },
];

/** Every object in a decoded wire tree whose `$type` is `carrier`. */
const objectsOfType = (v: unknown, carrier: string): Record<string, unknown>[] => {
  const found: Record<string, unknown>[] = [];
  const walk = (x: unknown): void => {
    if (Array.isArray(x)) {
      for (const e of x) walk(e);
      return;
    }
    if (x === null || typeof x !== 'object') return;
    const o = x as Record<string, unknown>;
    if (o.$type === carrier) found.push(o);
    for (const e of Object.values(o)) walk(e);
  };
  walk(v);
  return found;
};

const wireOfFixture = (f: ManifestEntry): string =>
  readFileSync(resolve(corpusDir, f.inputFile), 'utf8').trim();

describe('omit-at-default members (Phase 1603)', () => {
  for (const { slot, carrier } of OMIT_AT_DEFAULT) {
    const carrying = nodeFixtures
      .map((f) => ({ f, objs: objectsOfType(JSON.parse(wireOfFixture(f)), carrier) }))
      .filter((x) => x.objs.length > 0);
    const omitting = carrying.filter((x) => x.objs.some((o) => !(slot in o)));
    const spelling = carrying.filter((x) => x.objs.some((o) => slot in o));

    it(`${carrier}.${slot}: the corpus exercises BOTH the omitted and the spelled form`, () => {
      expect(
        omitting.map((x) => x.f.id),
        `no ${carrier} fixture omits '${slot}' - the omitted path is unexercised, so byte-identity here proves nothing about it`,
      ).not.toHaveLength(0);
      expect(
        spelling.map((x) => x.f.id),
        `no ${carrier} fixture spells '${slot}' - the present path is unexercised`,
      ).not.toHaveLength(0);
    });

    it(`${carrier}.${slot}: no fixture carrying it is quarantined`, () => {
      const quarantined = carrying.map((x) => x.f.id).filter((id) => tsQuarantine.has(id));
      expect(
        quarantined,
        `quarantining a ${carrier} fixture drops '${slot}' out of the byte cover`,
      ).toEqual([]);
    });

    it(`${carrier}.${slot}: the omitted form re-encodes WITHOUT the member`, () => {
      for (const { f } of omitting) {
        const wire = wireOfFixture(f);
        const back = encodeNode(evalExpr(projectTypeScriptExpr(wire) as string));
        expect(back, `projected TS source for ${f.id} must re-encode byte-identically`).toBe(wire);
        const spelledInWire = objectsOfType(JSON.parse(wire), carrier).filter(
          (o) => slot in o,
        ).length;
        const spelledInBack = objectsOfType(JSON.parse(back), carrier).filter(
          (o) => slot in o,
        ).length;
        expect(
          spelledInBack,
          `${f.id}: the wire spells '${slot}' on ${spelledInWire} ${carrier}(s); an explicit default in the re-encode is the activeIndex defect`,
        ).toBe(spelledInWire);
      }
    });
  }
});

// -- The absent-is-error classification, checked (Phase 1603) ----------------
//
// `app/Projection.fs` reads every wire member through one of three accessors,
// and which one a site uses IS its classification: `fieldOpt` (absent-is-omit,
// spelled as a source omission), `fieldOrIdentity` (absent-is-omit, spelled as
// the slot's identity default, where the target model has no absence there),
// and `fieldReq` (absent-is-error - a canonical emission always carries the
// member at this site). The byte comparison below checks the first two. This is
// what checks the third: `fieldReq` records every absence BY NAME, and a name
// appearing after the whole canonical corpus has been projected through every
// target is a site whose classification is wrong.
//
// It is the assertion the `activeIndex` defect needed and did not have. That
// member was read as mandatory at a site where the wire omits it, and the only
// signal was three fixtures failing a byte comparison for a reason that read as
// a package-pin lag. Here the same mistake fails by member name, on the first
// corpus that omits it, whether or not any byte comparison notices.
describe('absent-is-error sites (Phase 1603)', () => {
  it('no `fieldReq` member is ever absent over the canonical node corpus', () => {
    const targets = [
      'json',
      'typescript',
      'python',
      'fsharp',
      'csharp',
      'vb',
      'go',
      'kotlin',
      'rust',
      'swift',
    ];
    for (const f of nodeFixtures) {
      const wire = wireOfFixture(f);
      for (const t of targets) {
        // An illustrative leg may not model a kind at all, and a throw there is
        // a different finding from the one this test makes.
        try {
          projectByName(t, wire);
        } catch {
          /* not what this test measures */
        }
      }
    }
    expect(
      [...(absentRequiredMembers() as Iterable<string>)].sort(),
      "each name is a member the corpus omits at a site classified absent-is-error in app/Projection.fs - move that site to `fieldOpt` (source omission) or `fieldOrIdentity` (the slot's identity default)",
    ).toEqual([]);
  });
});

// -- The shared quarantine's TypeScript arm (Phase 1584) ---------------------
//
// The surfaces the resolver answers against. `fuaran` … `iconSource` are exactly
// the bindings `evalExpr` hands the generated source, so a claim about what "the
// pinned host models" is a claim about the very surface the round trip executes
// against; `ui` and `ops` are the whole modules, for a token naming something the
// projector reaches for without it being one of the seven.
const TS_NAMESPACES: Readonly<Record<string, unknown>> = {
  fuaran,
  binding,
  action,
  format,
  formFieldKind,
  nodeId,
  iconSource,
  ui,
  ops,
};

const tsHostModels = (token: string): ConstructVerdict =>
  resolveTypeScriptConstruct(token, TS_NAMESPACES);

const tsFixtureById = new Map(nodeFixtures.map((f) => [f.id, f]));
const tsWireOf = (id: string): string => wireOfFixture(tsFixtureById.get(id)!);

const tsProjected = (id: string): string | undefined => {
  const f = tsFixtureById.get(id);
  if (f === undefined) return undefined;
  try {
    return projectTypeScriptExpr(wireOfFixture(f)) as string;
  } catch {
    return undefined; // un-projectable — the quarantine's own outcome check says so
  }
};

const tsRoundTrip = (id: string): { ok: boolean; encoded?: string } => {
  const expr = tsProjected(id);
  if (expr === undefined) return { ok: false };
  try {
    return { ok: true, encoded: encodeNode(evalExpr(expr)) };
  } catch {
    return { ok: false };
  }
};

registerQuarantineChecks({
  arm: 'typescript',
  fixtureIds: nodeFixtures.map((f) => f.id),
  wireOf: tsWireOf,
  projectedSource: tsProjected,
  hostModels: tsHostModels,
  roundTrip: tsRoundTrip,
});

// The resolver above has NO live entries to exercise it — this arm's quarantine
// is empty and is meant to stay that way — so it is proved here instead. A probe
// nothing runs is worth less than no probe, because it reads as coverage; the
// Python arm's equivalent resolver is exercised by six standing entries and needs
// no self-test. Both directions and the refusal, so a resolver that answered
// "models" (or "error") unconditionally fails one of these three.
describe("the TypeScript arm's construct resolver (Phase 1584)", () => {
  it('reports a symbol the pinned surface HAS', () => {
    const verdict = tsHostModels('fuaran.tree');
    expect(verdict, `'fuaran.tree' must resolve: ${JSON.stringify(verdict)}`).not.toHaveProperty(
      'error',
    );
    expect((verdict as { models: boolean }).models).toBe(true);
  });

  it('reports a symbol the pinned surface LACKS', () => {
    const verdict = tsHostModels('fuaran.noSuchConstructorExists');
    expect(verdict).not.toHaveProperty('error');
    expect((verdict as { models: boolean }).models).toBe(false);
  });

  it('REFUSES a token it cannot answer, rather than guessing', () => {
    // An unknown namespace, and the RETIRED `optional:` prefix — a token that
    // resolved to a shrug would hold an entry vacuously, which is the whole
    // failure Phase 1578's probes exist to make impossible. The second case is
    // what keeps the retirement enforced: a stale entry carrying the old grammar
    // must refuse loudly, on this arm and on the Python one alike.
    expect(tsHostModels('noSuchNamespace.thing')).toHaveProperty('error');
    expect(tsHostModels('optional:fuaran.tree')).toHaveProperty('error');
  });

  it('the emission pattern reads a real projected source, both ways', () => {
    // The projector probe is a TEXT probe over generated source, so the two
    // shapes it anchors on — a factory call and an object property — are checked
    // against source this arm actually produces, in both directions.
    const sample = tsProjected(nodeFixtures[0]!.id);
    expect(sample, 'the first node fixture must project').toBeDefined();
    expect(emissionPattern('typescript', 'fuaran.noSuchConstructorExists').test(sample!)).toBe(
      false,
    );
    expect(
      emissionPattern('typescript', 'fuaran.tree').test('fuaran.tree({ id: nodeId(1) })'),
    ).toBe(true);
    expect(emissionPattern('typescript', 'Navigate.target').test('{ target: "Blank" }')).toBe(true);
    // The anchoring is what stops a fixture id from reading as an emission —
    // `tree-1` inside a node id is not a `fuaran.tree(` call.
    expect(
      emissionPattern('typescript', 'fuaran.tree').test("fuaran.card({ id: nodeId('tree-1') })"),
    ).toBe(false);
  });
});

describe('TS projection conformance (Node corpus)', () => {
  it('the corpus is present and non-trivial', () => {
    expect(nodeFixtures.length).toBeGreaterThanOrEqual(70);
  });

  for (const f of nodeFixtures) {
    // A quarantined fixture's self-clearing check is registered from the shared
    // table (`registerQuarantineChecks` above), so only the required byte
    // round-trip is left here.
    if (tsQuarantine.has(f.id)) continue;

    it(`${f.id} round-trips byte-identically`, () => {
      const wire = wireOfFixture(f);
      const expr = projectTypeScriptExpr(wire) as string;
      const reEncoded = encodeNode(evalExpr(expr));
      expect(reEncoded, `projected TS source for ${f.id} must re-encode byte-identically`).toBe(
        wire,
      );
    });
  }
});
