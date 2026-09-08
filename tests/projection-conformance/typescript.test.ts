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

import { encodeNode } from '@fuaran-ui/ops';
// `filterKind` was imported here until 2026-08-30 and no longer exists in
// `@fuaran-ui/ui`. Nothing broke, which is the hazard worth naming: vitest
// transpiles via esbuild, so a missing named export resolves to `undefined`
// and is passed into the evaluated source as a silently dead binding rather
// than a load error. Only strict Node ESM refuses it. Keep this list to names
// the projector actually emits.
import { fuaran, binding, action, format, formFieldKind, nodeId, iconSource } from '@fuaran-ui/ui';
import type { Node } from '@fuaran-ui/schema';

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

// Quarantine — EMPTY as of 2026-08-30, and the empty set is the assertion.
// Every node-round-trip fixture in the corpus is now required to re-encode
// byte-identically, so a projector that falls behind the corpus fails here by
// name rather than being absorbed into a list.
//
// It held 47 ids the day before, under two stated causes. Both are closed, and
// the split between them is worth keeping because it was not knowable until the
// pins moved — the previous note said so explicitly, and separating them was
// the first thing this pass did:
//
//   1. PACKAGE DRIFT — 2 ids. package.json pinned every @fuaran-ui/*
//      dependency at ^0.9.0 while the registry served eight to ten minor
//      versions newer. Raising the pins alone fixed `drawing-nonfinite-
//      sentinels` and `spark-nonfinite-sentinel` and nothing else, which is a
//      far smaller share than the note's masonry example implied.
//   2. PROJECTOR VOCABULARY LAG — the other 45, taught in app/Projection.fs.
//      The largest single cause was not a missing slot but a MOVED contract:
//      `Binding.Transform.source` became a `TransformSource` DU (Data | Live),
//      and the projector still emitted a bare `DataSource`, which crashed the
//      encoder rather than merely dropping a key — 13 ids at once.
//
// The pins were current as of 2026-08-30: ops 0.19.0, schema 0.18.0, ui 0.17.0,
// renderer 0.17.0, charts 0.11.0, ai-tools 0.11.0 — each the newest version its
// own package line publishes, verified against registry.npmjs.org. They are NOT
// one uniform number, and reading the v0.19.0 release tag as one is how this
// repo would have re-pinned five of the six packages to a version that does not
// exist. They have since moved (ops 0.22.0, schema 0.20.0, ui 0.19.0, renderer
// 0.21.0, charts 0.14.0, ai-tools 0.12.0) — still six independent lines.
//
// 2026-09-07 — the corpus had grown past the projector again, 48 ids' worth,
// and the set is STILL empty because every one of them was taught rather than
// listed. Two of the sixteen families were not missing slots but missing KINDS
// (`Embed`, `Tree`): the projector fell through to the illustrative generic
// sketch, which emits `fuaran.embed('id', {…})` — a two-argument call no ctor
// in this tier takes — so those failed as a TypeError rather than as a byte
// difference. Worth knowing, because a `Cannot read properties of undefined`
// from this arm reads like a harness fault and is not one: it is the fallback
// telling you a kind has no arm.
//
// Nothing here was package lag. Each of the 48 was checked against the pinned
// `@fuaran-ui/ops` encoder before it was taught, and every field the corpus
// asked for was already there to be emitted.
//
// 2026-09-07, second pass — the corpus moved 27 commits under that measurement
// and brought eleven more, taught the same way and again with nothing left to
// list: node-level `visible`, predicate (`when`) switch cases, `Binding.Expr`
// with its `params`, the declarative `Local` buffer (`codec` + `commitTo`),
// `Navigate` over a `TextSource` with a `target`, and `Action.Confirm` /
// `Action.Focus`.
//
// Two of those are worth knowing about, because both fail SILENTLY rather than
// loudly:
//
//   • A CTOR THAT DROPS WHAT IT DOES NOT RECOGNISE. `fuaran.switch` maps every
//     case through `{ match: c.match, child: c.child }`, so a `when` case
//     reaches the encoder carrying NEITHER key — and an absent `match` is
//     simply omitted, so nothing throws and the bytes are merely wrong. The
//     projection post-edits `spec.cases`, which is the same escape the Phase
//     768 `on` selector already takes.
//   • A SLOT WITH TWO MUTUALLY EXCLUSIVE SPELLINGS. `binding.local` REQUIRES an
//     `onCommit` closure, and a document carrying both `onCommit` and
//     `commitTo` is a decode refusal — so the declarative buffer is not
//     reachable through the ctor at all, and takes the literal form.
//
// Package lag again empty: `Binding.Expr`, `SwitchCase.when`, `Node.visible`,
// `LocalBinding.codec` / `.commitTo`, `NavigateTarget` and the `Confirm` /
// `Focus` action cases are all in the pinned `@fuaran-ui/schema` 0.20.0 and are
// all encoded by the pinned `ops` 0.22.0 — checked in the dist before each was
// taught, not assumed from the version number.
//
// If a future corpus addition lands here as a failure, the choice is to teach
// the projector or — where a slot genuinely has no reachable ctor and no
// literal form — to reinstate this set with the id and a DATED reason. Prefer
// teaching it: the previous list decayed for eight days precisely because a
// list is easier to append to than an emitter is to extend.
const PROJECTOR_LAGGING = new Set<string>([]);

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
      const quarantined = carrying.map((x) => x.f.id).filter((id) => PROJECTOR_LAGGING.has(id));
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

describe('TS projection conformance (Node corpus)', () => {
  it('the corpus is present and non-trivial', () => {
    expect(nodeFixtures.length).toBeGreaterThanOrEqual(70);
  });

  it('every quarantined id names a real fixture', () => {
    const ids = new Set(nodeFixtures.map((f) => f.id));
    for (const q of PROJECTOR_LAGGING) {
      expect(ids.has(q), `quarantined '${q}' is not in the corpus — remove it`).toBe(true);
    }
  });

  for (const f of nodeFixtures) {
    const wireOf = () => readFileSync(resolve(corpusDir, f.inputFile), 'utf8').trim();
    const roundTrip = (wire: string) => {
      const expr = projectTypeScriptExpr(wire) as string;
      const reconstructed = evalExpr(expr);
      return encodeNode(reconstructed);
    };

    if (PROJECTOR_LAGGING.has(f.id)) {
      it(`${f.id} is quarantined (projector lag — see PROJECTOR_LAGGING)`, () => {
        const wire = wireOf();
        let reEncoded: string | undefined;
        try {
          reEncoded = roundTrip(wire);
        } catch {
          return; // still un-projectable — quarantine holds
        }
        expect(
          reEncoded,
          `'${f.id}' now round-trips — the pins moved or the projector learned it; REMOVE it from PROJECTOR_LAGGING`,
        ).not.toBe(wire);
      });
    } else {
      it(`${f.id} round-trips byte-identically`, () => {
        const wire = wireOf();
        const reEncoded = roundTrip(wire);
        expect(reEncoded, `projected TS source for ${f.id} must re-encode byte-identically`).toBe(
          wire,
        );
      });
    }
  }
});
