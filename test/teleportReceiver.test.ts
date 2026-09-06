// =============================================================================
//  The teleport receiver's decode → mount decision.
//
//  WHY THIS FILE EXISTS. The receiver (/receiver.html, HOST 2) exists to make
//  one claim checkable: a page that ships no application can be handed one as
//  bytes, verify it, and run it. The bytes carry a TREE and a state map, and
//  for a long time the receive path read only the state — rebuilding the
//  Teleport page's own signup exemplar from it and rendering that. For a
//  bundle this page itself produced the two are identical, so every in-page
//  round trip looked perfect; for a bundle from ANY other conformant host the
//  arriving app was decoded, digest-verified, its digest announced in the
//  banner — and then thrown away, with the exemplar left on screen. The claim
//  failed exactly where it mattered and nowhere it was being watched.
//
//  So the decision is factored out of the React component (`mountOf`, reported
//  headlessly by `mountReport`) and pinned here over real bundles minted by the
//  real codec: what the host mounts is the tree that ARRIVED.
//
//  GO-RED. Reverting `mountOf` to derive the tree from the arriving state — the
//  behaviour described above — fails the foreign-bundle assertions below while
//  leaving the exemplar ones green, which is precisely the asymmetry that let
//  the defect ship.
// =============================================================================

import { describe, expect, it } from 'vitest';

// @ts-expect-error untyped Fable output (no .d.ts is generated for it)
import { mountReport, sampleBundles, tamperReport } from '../app/showcase/output/Teleport.js';

interface Bundles {
  exemplar: string;
  exemplarTreeJson: string;
  foreign: string;
  foreignTreeJson: string;
}

interface Report {
  ok: boolean;
  digest?: string;
  foreign?: boolean;
  mountedTreeJson?: string;
  error?: string;
}

const bundles = sampleBundles as Bundles;
const report = (encoded: string): Report => mountReport(encoded) as Report;

describe('the teleport receiver mounts what arrives', () => {
  it('mints both sample bundles through the real codec', () => {
    // An empty string is this fixture's encode-failure sentinel; a real bundle
    // is prefixed `FT1.`. Guard it, or every assertion below degrades into a
    // decode-failure test that would pass for the wrong reason.
    expect(bundles.exemplar.startsWith('FT1.'), 'exemplar bundle encoded').toBe(true);
    expect(bundles.foreign.startsWith('FT1.'), 'foreign bundle encoded').toBe(true);
    expect(bundles.exemplarTreeJson).not.toBe(bundles.foreignTreeJson);
  });

  it("mounts a foreign host's tree verbatim, not this page's exemplar", () => {
    const r = report(bundles.foreign);
    expect(r.ok, r.error ?? 'decode failed').toBe(true);
    expect(r.mountedTreeJson).toBe(bundles.foreignTreeJson);
    expect(r.mountedTreeJson).not.toBe(bundles.exemplarTreeJson);
  });

  it('flags a foreign app as foreign, so the page does not claim to drive it', () => {
    expect(report(bundles.foreign).foreign).toBe(true);
  });

  it("mounts this page's own bundle as its own, not as foreign", () => {
    const r = report(bundles.exemplar);
    expect(r.ok, r.error ?? 'decode failed').toBe(true);
    expect(r.mountedTreeJson).toBe(bundles.exemplarTreeJson);
    expect(r.foreign).toBe(false);
  });

  it('announces the digest it verified', () => {
    const r = report(bundles.foreign);
    // A hex digest, and the same one the arrival banner renders the head of.
    expect(r.digest).toMatch(/^[0-9a-f]{16,}$/);
    expect(report(bundles.exemplar).digest).not.toBe(r.digest);
  });

  it('refuses a tampered bundle rather than mounting something subtly wrong', () => {
    // Flip one character inside the payload, leaving the format prefix intact.
    const i = bundles.foreign.length - 8;
    const c = bundles.foreign[i] === 'A' ? 'B' : 'A';
    const tampered = bundles.foreign.slice(0, i) + c + bundles.foreign.slice(i + 1);
    const r = report(tampered);
    expect(r.ok).toBe(false);
    expect(typeof r.error).toBe('string');
  });

  it('reports a decode failure instead of throwing', () => {
    expect(report('not a bundle at all').ok).toBe(false);
  });
});

// The "flip one byte" vignette has to stage against WHATEVER is mounted, now
// that what is mounted can be an app this page never authored. Its refusal
// must be the DIGEST check — a parse error would refuse for the wrong reason
// and quietly stop demonstrating integrity at all.
describe('the tamper vignette stages against any mounted app', () => {
  interface Tamper {
    staged: boolean;
    what?: string;
    refused?: boolean;
    digestMismatch?: boolean;
    refusal?: string;
    why?: string;
  }
  const tamper = (encoded: string): Tamper => tamperReport(encoded) as Tamper;

  it("refuses the tampered exemplar on the digest, via the page's readable copy", () => {
    const t = tamper(bundles.exemplar);
    expect(t.staged, t.why ?? 'could not stage').toBe(true);
    expect(t.what).toContain('acc0unt');
    expect(t.refused).toBe(true);
    expect(t.digestMismatch).toBe(true);
  });

  it("refuses a tampered foreign app on the digest too, via a node id's first letter", () => {
    const t = tamper(bundles.foreign);
    expect(t.staged, t.why ?? 'could not stage').toBe(true);
    expect(t.what).toContain('node id');
    expect(t.refused).toBe(true);
    expect(t.digestMismatch).toBe(true);
  });

  it('reports a bundle it cannot stage rather than pretending it did', () => {
    expect(tamper('not a bundle at all').staged).toBe(false);
  });
});
