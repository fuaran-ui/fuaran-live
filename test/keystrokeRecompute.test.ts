// Phase 2047 — the playground stops recomputing on every keystroke.
//
// A prompt keystroke re-renders the whole page view, and changes nothing but
// the prompt text. This probe renders, ten times over one fixed session, the
// three surfaces that used to re-derive their content on every one of those
// renders, exactly as the page composes them:
//
//   • the Source card (`ProjectionSync.SourceCard`, fed the session's wire),
//   • the refine loop beneath the Editor, after a refinement came back, so its
//     comparison readout is showing (`Refine.below`),
//   • the host-fidelity preview inside its collapsed "More tools" disclosure
//     (`Disclosure.ToolDetails`, as the page builds it).
//
// and COUNTS the work underneath them, by wrapping the functions that do it:
// canonical-wire encodes (`CanonicalJson.encodeNode`), source projections
// (`Projection.projectSpans`), tree diffs (`TreeDiff.diff`) and host-preview
// renders. Nothing in the app carries a counter; the wrapping is module mocks.
//
// The figures, from the same ten-keystroke probe on the same fixed tree:
//
//                          before 2047   after 2047
//   canonical-wire encodes        190            0
//   source projections             10            0
//   tree diffs                     30            0
//   host-preview renders           10            0
//
// "Before" was measured against the unmodified build (2026-10-06) with the old
// composition — the Source card taking the tree, the tool body as a plain child
// of `<details>`. Its 190 encodes are the Source card's one per render plus the
// host preview's per-node encodes; its 30 diffs are the comparison readout
// running the decode-and-diff three times per render.
//
// The falsifier is the second test: a real op applied to the session DOES
// re-derive — the wire, one projection, one diff — so the zeros above are a
// cache that follows the tree, not a cache that stopped looking at it.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it, vi } from 'vitest';

const out = resolve(__dirname, '../app/output');
const fableModules = resolve(out, 'fable_modules');

/** The versioned package folder Fable emitted for `id` (e.g. `<id>.0.91.0`). */
const pkg = (id: string): string => {
  const hit = readdirSync(fableModules).find((d) => d.startsWith(id + '.'));
  if (!hit) {
    throw new Error(`no Fable output folder for ${id} under ${fableModules}`);
  }
  return resolve(fableModules, hit);
};

const counts = { encode: 0, project: 0, diff: 0, hostPreview: 0 };
const reset = () => {
  counts.encode = 0;
  counts.project = 0;
  counts.diff = 0;
  counts.hostPreview = 0;
};

// Wrap the three functions that do the work. `doMock` (not `mock`) because the
// paths are resolved at run time — the package folders carry version numbers.
vi.doMock(resolve(pkg('Fuaran.UI.OpStream.Abstractions'), 'CanonicalJson.fs.js'), async (orig) => {
  const m: any = await orig();
  return { ...m, encodeNode: (...a: unknown[]) => (counts.encode++, m.encodeNode(...a)) };
});
vi.doMock(resolve(pkg('Fuaran.UI.OpStream.Replay'), 'TreeDiff.fs.js'), async (orig) => {
  const m: any = await orig();
  return {
    ...m,
    TreeDiffModule_diff: (...a: unknown[]) => (counts.diff++, m.TreeDiffModule_diff(...a)),
  };
});
vi.doMock(resolve(out, 'Projection.js'), async (orig) => {
  const m: any = await orig();
  return { ...m, projectSpans: (...a: unknown[]) => (counts.project++, m.projectSpans(...a)) };
});

const fence = (json: string) => '```json\n' + json + '\n```';

const tree = (heading: string) =>
  '{"id":"root","kind":{"$type":"Box","children":[' +
  '{"id":"h","kind":{"$type":"Heading","level":2,"text":"' +
  heading +
  '","variant":"Standard"}},' +
  '{"id":"b","kind":{"$type":"Badge","label":"New","variant":"Brand"}}' +
  '],"layout":{"$type":"Auto"},"role":"Dashboard"}}';

async function harness() {
  const { createElement } = await import('react');
  const { renderToString } = await import('react-dom/server');
  // @ts-expect-error untyped Fable output
  const Sess: any = await import('../app/output/Session.js');
  // @ts-expect-error untyped Fable output
  const SP: any = await import('../app/output/testing/SessionProbes.js');
  // @ts-expect-error untyped Fable output
  const PE: any = await import('../app/output/testing/PropertyEditorProbes.js');
  // @ts-expect-error untyped Fable output
  const R: any = await import('../app/output/navigator/Refine.js');
  // @ts-expect-error untyped Fable output
  const PS: any = await import('../app/output/navigator/ProjectionSync.js');
  // @ts-expect-error untyped Fable output
  const HP: any = await import('../app/output/HostPreview.js');
  // @ts-expect-error untyped Fable output
  const D: any = await import('../app/output/Disclosure.js');
  // @ts-expect-error untyped Fable output
  const P: any = await import('../app/output/Projection.js');

  const ingest = (s: unknown, json: string) => {
    const r = SP.ingestResult(s, fence(json));
    if (!r.Ok) throw new Error(`fixture did not ingest: ${r.Error}`);
    return r.Next;
  };

  // Emitted, edited by hand, refined, re-emitted: the comparison readout shows.
  let session = ingest(Sess.empty, tree('Quarterly review'));
  session = PE.commitAt(session, 'h', 'Text', 'Q3 revenue').Next;
  const baseline = R.baselineOf(session, 'add a metric');
  session = ingest(session, tree('Q3 revenue (re-emitted)'));

  const active = P.targets.head[0];
  const CountedHostPreview = (props: { tree: unknown }) => {
    counts.hostPreview++;
    return HP.HostPreviewPane(props);
  };

  // One page render, composed as `App.view` composes these three surfaces.
  const render = (s: any, toolOpen = false) =>
    renderToString(
      createElement(
        'div',
        null,
        createElement(PS.SourceCard, {
          wire: Sess.canonicalWire(s),
          active,
          onSelect: () => {},
        }),
        R.below(createElement('span'), s, baseline, false, true, () => {}),
        createElement(D.ToolDetails, {
          title: 'Host fidelity',
          openByDefault: toolOpen,
          body: () => createElement(CountedHostPreview, { tree: s.Tree }),
        }),
      ),
    );

  return { render, session, ingest };
}

describe('a prompt keystroke recomputes nothing the tree did not change', () => {
  it('ten keystrokes on a fixed tree: zero encodes, projections, diffs and host-preview renders', async () => {
    const h = await harness();

    const first = h.render(h.session); // the render that follows the tree arriving
    reset();

    const renders: string[] = [];
    for (let i = 0; i < 10; i++) renders.push(h.render(h.session));

    expect(counts).toEqual({ encode: 0, project: 0, diff: 0, hostPreview: 0 });
    // And what is shown is unchanged: every keystroke's page is the first one's.
    for (const page of renders) expect(page).toBe(first);
    // The cached content is the real thing, not an empty pane.
    expect(first).toContain('data-fuaran-projection');
    expect(first).toContain('change(s) against the version you approved');
  }, 120_000);

  it('a real op DOES re-derive — once — so the cache follows the tree', async () => {
    const h = await harness();
    h.render(h.session);

    const edited = h.ingest(
      h.session,
      '{"$type":"UpdateProp","path":"Text","target":"h","value":"Renamed"}',
    );
    reset();
    const after = h.render(edited);

    // The wire is re-encoded for the new tree; the diff encodes the nodes it compares too, so
    // the count is the change's own work rather than exactly one.
    expect(counts.encode).toBeGreaterThan(0);
    expect(counts.project).toBe(1);
    expect(counts.diff).toBe(1);
    expect(after).toContain('Renamed');

    // …and the keystrokes after it are free again.
    reset();
    for (let i = 0; i < 10; i++) h.render(edited);
    expect(counts).toEqual({ encode: 0, project: 0, diff: 0, hostPreview: 0 });
  }, 120_000);

  it('an OPEN tool panel mounts its body; a collapsed one never builds it', async () => {
    const h = await harness();

    reset();
    const collapsed = h.render(h.session, false);
    expect(counts.hostPreview).toBe(0);
    expect(collapsed).not.toContain('pg-tool-body');

    const open = h.render(h.session, true);
    expect(counts.hostPreview).toBe(1);
    expect(open).toContain('pg-tool-body');
    expect(open).toMatch(/<details[^>]*open/);
  }, 120_000);
});
