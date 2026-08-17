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

import { decodeNode, encodeNode } from '@fuaran-ui/ops';
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

// ── Package-vocabulary ceiling ───────────────────────────────────────────────
//
// The shared wire-format corpus is versioned with the language repos and can
// run AHEAD of the published `@fuaran-ui/*` packages: a fixture may carry wire
// vocabulary (a new node kind, spec field, or binding form) that the installed
// decoder/encoder does not know yet. Such a fixture is unprojectable here BY
// CONSTRUCTION — the projection executes against the installed packages, so its
// fidelity ceiling is their vocabulary; no projector change can round-trip a
// field the installed encoder cannot emit.
//
// Those fixtures are skipped, but never silently: the gap is detected
// mechanically (decode→encode through the installed package is not the
// identity), and the detected set must match the pinned inventory below
// byte-for-byte — a dedicated test fails when they diverge in EITHER direction:
//
//   • a new corpus fixture outruns the installed packages → add it here,
//     deliberately, with the wire feature it needs named;
//   • a package update closes a gap → remove the entry; the fixture rejoins the
//     byte-round-trip gate, which then fails honestly if the projector has not
//     caught up with the new vocabulary.
//
// Every entry names the wire feature the installed packages lack.
const PACKAGE_GAP_REASONS: ReadonlyMap<string, string> = new Map([
  [
    'badge-transform-live',
    'Transform.source as a Binding (State) — installed packages decode only DataSource shapes',
  ],
  [
    'button-setstate-valuefrom',
    'SetState.valueFrom (declarative value source) — not in the installed action vocabulary',
  ],
  [
    'chart-axis-titles',
    'Chart valueFormat / xTitle / yTitle / subtitle — not in the installed chart spec',
  ],
  ['chart-data-labels', 'Chart.dataLabels — not in the installed chart spec'],
  ['chart-legend-position', 'Chart.legendPosition — not in the installed chart spec'],
  ['chart-value-format', 'Chart.valueFormat — not in the installed chart spec'],
  ['drawing-rotated-labels', 'Drawing label rotation — not in the installed drawing spec'],
  [
    'grid-bound-sort',
    'DataGrid declarative sort (sortStateKey / defaultSort / column sortable) — not in the installed grid spec',
  ],
  [
    'grid-declared-edit',
    'DataGrid declared edit (editStateKey / column editable) — not in the installed grid spec',
  ],
  [
    'grid-paged',
    'DataGrid declarative paging (pageSize / pageStateKey) — not in the installed grid spec',
  ],
  [
    'grid-paged-sorted',
    'DataGrid declarative paging + sort state — not in the installed grid spec',
  ],
  ['grid-sort-state-key', 'DataGrid.sortStateKey — not in the installed grid spec'],
  ['icon-1', 'the Icon node kind — not in the installed node vocabulary'],
  ['link-protected-1', 'Link.protection — not in the installed link spec'],
  [
    'metric-duration-1',
    'Duration / RelativeTime cell formats — not in the installed format vocabulary',
  ],
  ['table-sortable-1', 'staticRows sortable / defaultSort — not in the installed grid spec'],
]);

/** Whether the INSTALLED packages can express this wire at all: the strict
 *  decoder accepts it and the encoder reproduces it byte-identically. */
const packageCanExpress = (wire: string): boolean => {
  try {
    const decoded = decodeNode(wire);
    return decoded.ok && encodeNode(decoded.value) === wire;
  } catch {
    return false;
  }
};

const fixtures = nodeFixtures.map((f) => {
  const wire = readFileSync(resolve(corpusDir, f.inputFile), 'utf8').trim();
  return { ...f, wire, packageGap: !packageCanExpress(wire) };
});

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

describe('TS projection conformance (Node corpus)', () => {
  it('the corpus is present and non-trivial', () => {
    expect(nodeFixtures.length).toBeGreaterThanOrEqual(70);
  });

  it('every package-vocabulary skip is pinned by name, and no pinned skip is stale', () => {
    const detected = fixtures
      .filter((f) => f.packageGap)
      .map((f) => f.id)
      .sort();
    const pinned = [...PACKAGE_GAP_REASONS.keys()].sort();

    expect(
      detected,
      'the fixtures the installed @fuaran-ui/* packages cannot express must exactly match ' +
        'the pinned PACKAGE_GAP_REASONS inventory — add a new corpus fixture deliberately ' +
        'with its missing wire feature named, or remove an entry a package update has closed',
    ).toEqual(pinned);
  });

  for (const f of fixtures) {
    const gapReason = f.packageGap ? PACKAGE_GAP_REASONS.get(f.id) : undefined;

    it.skipIf(f.packageGap)(
      `${f.id} round-trips byte-identically${gapReason ? ` (package gap: ${gapReason})` : ''}`,
      () => {
        const expr = projectTypeScriptExpr(f.wire) as string;
        const reconstructed = evalExpr(expr);
        const reEncoded = encodeNode(reconstructed);

        expect(reEncoded, `projected TS source for ${f.id} must re-encode byte-identically`).toBe(
          f.wire,
        );
      },
    );
  }
});
