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
import { projectTypeScriptExpr } from '../../app/output/Projection.js';

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
