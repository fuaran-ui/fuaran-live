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
// the pre-rebuild TS-shell harness carried (the Python / F# legs remain
// demo-grade — see docs/PROJECTION_FIDELITY.md).
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

import { encodeNode } from '@fuaran-ui/ops';
import {
  fuaran,
  binding,
  action,
  format,
  formFieldKind,
  filterKind,
  nodeId,
  iconSource,
} from '@fuaran-ui/ui';
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
    'filterKind',
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
    filterKind,
    nodeId,
    iconSource,
  ) as Node<unknown>;
};

// Quarantine (dated 2026-08-21): fixtures whose vocabulary post-dates the
// projector's verified per-kind emitter (app/Projection.fs). These are NOT
// package drift — the @fuaran-ui/* pins are current — they are corpus
// vocabulary the projector has not been taught yet (charts axis
// titles/labels/scale/legend, the grid sort/edit/page family, master-detail
// selection seeding, Icon, Duration format, environment bindings, …). Each is
// asserted to STILL fail, so teaching the projector a fixture forces its
// removal from this list — the list cannot go silently stale. The teaching
// work is tracked estate-side (the source-projection round-trip backlog).
const PROJECTOR_LAGGING = new Set([
  'badge-transform-live',
  'button-setstate-valuefrom',
  'chart-axis-titles',
  'chart-data-labels',
  'chart-legend-position',
  'chart-temporal-x',
  'chart-value-format',
  'composite-tabs-panels',
  'drawing-tipped-shapes',
  'filterable-static-dashboard',
  'grid-bound-sort',
  'grid-declared-edit',
  'grid-field-named',
  'grid-paged',
  'grid-paged-sorted',
  'grid-reorderable',
  'grid-sort-state-key',
  'grid-toned-pill',
  'grid-transform',
  'grid-transform-param',
  'icon-1',
  'link-protected-1',
  'master-detail-multi-field',
  'master-detail-preselected',
  'master-detail-preselected-second-row',
  'metric-duration-1',
  'now-environment-binding',
  'scalar-transform-composition',
  'switch-on-selection',
  'table-sortable-1',
]);

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
      it(`${f.id} is quarantined (projector vocabulary lag, 2026-08-21)`, () => {
        const wire = wireOf();
        let reEncoded: string | undefined;
        try {
          reEncoded = roundTrip(wire);
        } catch {
          return; // still un-projectable — quarantine holds
        }
        expect(
          reEncoded,
          `'${f.id}' now round-trips — the projector learned it; REMOVE it from PROJECTOR_LAGGING`,
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
