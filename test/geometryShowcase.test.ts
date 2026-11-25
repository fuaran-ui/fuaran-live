// Phase 2851 – the geometry showcase page, exercised headlessly over the Fable output.
//
// The page lives in app/showcase/Geometry.fs and its headless surface in
// app/showcase/testing/GeometryProbes.fs. Every probe calls the page's own functions,
// so each claim below is about what the page computes and shows.
//
// Four legs, one per claim the page makes:
//   1. The published example: the package's law vector `Circle.through-general-1`,
//      recomputed from its published inputs, equals its published bits – checked
//      against an independent copy of those bits restated here, not only against the
//      page's own copy.
//   2. Moving a point across the circle flips the sign, and nowhere else.
//   3. The triangles and the cells are the side test applied to every triple: a known
//      configuration gives its known triangulation and cell.
//   4. The figure is canonical wire, and the page reaches nothing off the page.
// Then the publication-boundary sweep over the files this phase added.
//
// Requires `pnpm run fable:app` to have produced app/showcase/output/.

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import * as P from '../app/showcase/output/testing/GeometryProbes.js';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

// The published bits of `Circle.through-general-1`, as the package's conformance file
// (conformance/laws/geometric-algebra-laws.json, release 0.47.0) states them. Restated
// here independently of the page, so a typo copied into both would have to be made twice.
const published = [
  'bfa30f8bb05bf4a5',
  'bfa7436b2629c064',
  'bf72a7e28475b123',
  '3fc03b1c420b39aa',
  '3f8d388bd4ed4a5c',
  '3f5f21cd3ad42b58',
  '3f821fa9f81a79ed',
  'bf5ba3e1c0088025',
  'bf69bc8ed53470d0',
  '3f82b6e2757a2d2e',
];

const side = (xs: number[]): string => P.geoSide(Float64Array.from(xs));

describe('Phase 2851 – the published example is reproduced bit for bit', () => {
  it('computes the published bits from the published inputs', () => {
    expect(Array.from(P.geoExampleComputed() as string[])).toEqual(published);
  });

  it('states the published bits the package publishes', () => {
    expect(Array.from(P.geoExamplePublished() as string[])).toEqual(published);
  });

  it('writes and reads bit patterns as the law vectors do', () => {
    expect(P.geoBits(1.0)).toBe('3ff0000000000000');
    expect(P.geoBits(-0.0)).toBe('8000000000000000');
    expect(P.geoBits(-2.0)).toBe('c000000000000000');
    for (const hex of published) {
      expect(P.geoBits(P.geoOfBits(hex))).toBe(hex);
    }
  });
});

describe('Phase 2851 – moving a point across the circle flips the sign', () => {
  // The circle through (1,0), (0,1) and (-1,0) is the unit circle about the origin.
  const unit = [1, 0, 0, 1, -1, 0];

  it('reads inside, on and outside the unit circle', () => {
    expect(side([...unit, 0, 0.5])).toBe('Inside');
    expect(side([...unit, 0, -1])).toBe('On');
    expect(side([...unit, 0, 1.5])).toBe('Outside');
  });

  it('answers the same whatever order the circle points come in', () => {
    expect(side([-1, 0, 0, 1, 1, 0, 0, 0.5])).toBe('Inside');
    expect(side([-1, 0, 0, 1, 1, 0, 0, 1.5])).toBe('Outside');
  });

  it('flips once as D is dragged from inside the page circle to outside it', () => {
    const start = Array.from(P.geoInitialPoints() as Float64Array);
    const abc = start.slice(0, 6);
    const [dx, dy] = [start[6], start[7]];
    // D starts inside, as the page's copy says.
    expect(side([...abc, dx, dy])).toBe('Inside');

    // Drag D straight up the frame in small steps: the reading changes exactly once.
    const readings: string[] = [];
    for (let y = dy; y >= 1; y -= 0.5) readings.push(side([...abc, dx, y]));
    const changes = readings.filter((r, i) => i > 0 && r !== readings[i - 1]);
    expect(readings[0]).toBe('Inside');
    expect(readings[readings.length - 1]).toBe('Outside');
    expect(changes.length).toBe(1);
  });

  it('reads three points in a line as no circle', () => {
    expect(side([0, 0, 1, 1, 2, 2, 5, 0])).toBe('none');
  });
});

describe('Phase 2851 – the triangles and cells are the side test over every triple', () => {
  // A square with its centre: the four triangles round the centre are the Delaunay
  // triangles (the square's own diagonals each have the centre inside their circle).
  const square = [10, 10, 30, 10, 30, 30, 10, 30, 20, 20];

  it('finds the four triangles round the centre', () => {
    const tris = Array.from(P.geoTriangles(Float64Array.from(square)) as string[]).sort();
    expect(tris).toEqual(['0,1,4', '0,3,4', '1,2,4', '2,3,4']);
  });

  it('gives the centre a four-cornered cell and every point a cell', () => {
    const sizes = Array.from(P.geoCellSizes(Float64Array.from(square)) as Int32Array);
    expect(sizes.length).toBe(5);
    expect(sizes[4]).toBe(4);
    for (const s of sizes) expect(s).toBeGreaterThanOrEqual(3);
  });

  it('gives each of the page’s seven starting points a cell', () => {
    const sizes = Array.from(P.geoCellSizes(P.geoInitialPoints()) as Int32Array);
    expect(sizes.length).toBe(7);
    for (const s of sizes) expect(s).toBeGreaterThanOrEqual(3);
  });
});

describe('Phase 2851 – the figure is canonical wire and the page stays on the page', () => {
  it('encodes the figure as a Drawing node of polygons, lines and circles', () => {
    const wire = P.geoFigureWire() as string;
    const parsed = JSON.parse(wire);
    expect(typeof parsed).toBe('object');
    expect(wire).toContain('Drawing');
    expect(wire).toContain('Polygon');
    expect(wire).toContain('Circle');
  });

  it('opens no connection', () => {
    const src = readFileSync(join(repoRoot, 'app/showcase/Geometry.fs'), 'utf8');
    expect(src).not.toMatch(/\bfetch\s*\(/);
    expect(src).not.toMatch(/XMLHttpRequest|WebSocket|EventSource|sendBeacon/);
  });
});

describe('Phase 2851 – the new files keep to the publication boundary', () => {
  // The same two halves as the Phase 2395 sweep: a SHAPE check that needs no list (no
  // machine path, no off-machine URL), and the maintainers' deny-list read from
  // FUARAN_DENY_LIST, reported by index so no failure prints a pattern.
  const files = [
    'app/showcase/Geometry.fs',
    'app/showcase/testing/GeometryProbes.fs',
    'test/geometryShowcase.test.ts',
  ];
  const text = files.map((f) => readFileSync(join(repoRoot, f), 'utf8')).join('\n');

  const denyPatterns = (process.env['FUARAN_DENY_LIST'] ?? '')
    .split(/[\r\n,]+/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith('#'));

  it('names no machine path and no off-machine URL', () => {
    expect(text.match(/[A-Za-z]:[\\/][A-Za-z0-9_.-]{2,}/g) ?? []).toEqual([]);
    expect(text.match(/\/(?:home|Users|root|mnt)\/[A-Za-z0-9_.-]{2,}/g) ?? []).toEqual([]);
    expect(text.match(/https?:\/\/[^"'\s\\]+/g) ?? []).toEqual([]);
  });

  it.skipIf(denyPatterns.length === 0)('carries nothing the configured deny-list names', () => {
    const lower = text.toLowerCase();
    const hits = denyPatterns
      .map((pattern, index) => ({ index, hit: new RegExp(pattern, 'i').test(lower) }))
      .filter((row) => row.hit)
      .map((row) => `deny-list entry #${row.index + 1}`);
    expect(hits).toEqual([]);
  });
});
