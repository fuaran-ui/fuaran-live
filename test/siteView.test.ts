// The Navigator's site view — a page set of named trees, switching, the
// cross-page "Move to page…" verb, paired undo, and the module-state guard
// rail — exercised headlessly over the Fable output. The module lives in
// app/navigator/SiteView.fs and compiles to app/output/navigator/SiteView.js;
// this drives it in node via vitest, following the structural-edit suite's
// pattern (flat string/array projections across the Fable boundary; opaque
// Site/Session values threaded back in; every fixture fed through the REAL
// strict decoder).
//
// The suite's central claims:
//
//  1. A page set is ordinary sessions, plural — each page keeps its OWN op
//     history across switches, byte-for-byte, chain intact.
//  2. The cross-page move is composed entirely from shipped single-tree
//     primitives: the destination receives one placed insert (ids remapped
//     only on collision), the source one RemoveNode — and BOTH legs carry the
//     same correlation-bearing actor in their own hash-chained records.
//  3. Paired undo reverts both legs as one action (replay-based, so it is
//     honestly refused once either tree has moved on), restoring both pages
//     byte-identically.
//  4. Moving a subtree that READS module state surfaces a typed warning naming
//     the keys — state does not travel with structure.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { describe, it, expect } from 'vitest';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { empty, ingestResult } from '../app/output/Session.js';
import {
  loadResult,
  pageNames,
  activePage,
  shelfSessionOf,
  switchResult,
  moveResult,
  undoMoveResult,
  canUndoMove,
  noticeKind,
  noticeLine,
  stateKeysAt,
  // @ts-expect-error untyped Fable output
} from '../app/output/navigator/SiteView.js';
import {
  canonicalTree,
  originIds,
  logKinds,
  appliedOps,
  verifyResult,
  // @ts-expect-error untyped Fable output
} from '../app/output/navigator/OpLog.js';
import { decodeNode, encodeNode } from '@fuaran-ui/ops';

// ─── fixtures ────────────────────────────────────────────────────────────────

// Home: root ▸ (a, card ▸ (x, y), c) — the structural suite's shape, so a
// moved subtree has depth and siblings on both sides.
const homeTree =
  '{"id":"home-root","kind":{"$type":"Box","children":[' +
  '{"id":"a","kind":{"$type":"Markdown","text":"A"}},' +
  '{"id":"card","kind":{"$type":"Box","children":[' +
  '{"id":"x","kind":{"$type":"Markdown","text":"X"}},' +
  '{"id":"y","kind":{"$type":"Markdown","text":"Y"}}],' +
  '"layout":{"$type":"Auto"},"role":"Card"}},' +
  '{"id":"c","kind":{"$type":"Markdown","text":"C"}}],' +
  '"layout":{"$type":"Auto"},"role":"Dashboard"}}';

// About: root ▸ (x) — deliberately carries an id ("x") that collides with a
// node inside Home's card, so a move exercises the collision remap.
const aboutTree =
  '{"id":"about-root","kind":{"$type":"Box","children":[' +
  '{"id":"x","kind":{"$type":"Markdown","text":"About X"}}],' +
  '"layout":{"$type":"Auto"},"role":"Card"}}';

// Form: root ▸ panel ▸ button whose `disabled` is a $state read — the guard
// rail's subject (the binding shape is a corpus fixture's).
const formTree =
  '{"id":"form-root","kind":{"$type":"Box","children":[' +
  '{"id":"panel","kind":{"$type":"Box","children":[' +
  '{"id":"btn","kind":{"$type":"Button",' +
  '"disabled":{"$type":"State","defaultValue":false,"key":"loading"},' +
  '"icon":"refresh","label":"Refresh","onClick":{"$type":"Chain","ops":[]},"variant":"Primary"}}],' +
  '"layout":{"$type":"Auto"},"role":"Card"}}],' +
  '"layout":{"$type":"Auto"},"role":"Dashboard"}}';

/** A page-set bundle over named tree documents. */
function bundle(pages: Array<[string, string]>): string {
  return JSON.stringify({
    $pages: 'fuaran-page-set',
    version: 1,
    pages: pages.map(([name, tree]) => ({ name, tree: JSON.parse(tree) })),
  });
}

/** Canonical bytes of a hand-written tree (decode → re-encode identity). */
function canon(wire: string): string {
  const decoded = decodeNode(wire);
  if (!decoded.ok) throw new Error(`fixture did not decode: ${wire}`);
  return encodeNode(decoded.value);
}

function loaded(pages: Array<[string, string]>) {
  const r = loadResult(bundle(pages));
  expect(r.Ok).toBe(true);
  return r;
}

/** An ordinary single-tree edit on a session (the closed loop's op ingest). */
function edit(session: unknown, opJson: string) {
  const r = ingestResult(session, opJson);
  expect(r.Ok).toBe(true);
  return r.Next;
}

const retitleA = '{"$type":"UpdateProp","target":"a","path":"text","value":"A2"}';

// ─── loading a page set ──────────────────────────────────────────────────────

describe('page-set bundle load', () => {
  it('loads named pages; the first is active, the rest are shelved with their trees intact', () => {
    const r = loaded([
      ['Home', homeTree],
      ['About', aboutTree],
      ['Form', formTree],
    ]);
    expect(Array.from(pageNames(r.Site))).toEqual(['Home', 'About', 'Form']);
    expect(activePage(r.Site)).toBe('Home');
    expect(canonicalTree(r.Session)).toBe(canon(homeTree));
    expect(canonicalTree(shelfSessionOf(r.Site, 'About'))).toBe(canon(aboutTree));
    expect(canonicalTree(shelfSessionOf(r.Site, 'Form'))).toBe(canon(formTree));
    expect(noticeKind(r.Site)).toBe('info');
  });

  it('each page starts as its own fresh base — no ops, replay verified', () => {
    const r = loaded([
      ['Home', homeTree],
      ['About', aboutTree],
    ]);
    expect(appliedOps(r.Session).length).toBe(0);
    const shelved = shelfSessionOf(r.Site, 'About');
    expect(appliedOps(shelved).length).toBe(0);
    expect(verifyResult(shelved)).toEqual({ ReplayOk: true, ChainOk: true, Steps: 0 });
  });

  it('refuses a document that is not a page-set bundle', () => {
    for (const bad of [
      homeTree, // a bare node, not a bundle
      '{"$pages":"fuaran-page-set","version":2,"pages":[{"name":"Home","tree":{}}]}', // wrong version
      '{"$pages":"fuaran-page-set","version":1,"pages":[]}', // no pages
      'not json at all',
    ]) {
      expect(loadResult(bad).Ok).toBe(false);
    }
  });

  it('refuses a duplicate page name, and names the page whose tree fails the strict decoder', () => {
    const dup = loadResult(
      bundle([
        ['Home', homeTree],
        ['Home', aboutTree],
      ]),
    );
    expect(dup.Ok).toBe(false);
    expect(dup.Error).toContain('Home');

    const undecodable = loadResult(
      JSON.stringify({
        $pages: 'fuaran-page-set',
        version: 1,
        pages: [
          { name: 'Home', tree: JSON.parse(homeTree) },
          { name: 'Broken', tree: { id: 'z', kind: { $type: 'NoSuchKind' } } },
        ],
      }),
    );
    expect(undecodable.Ok).toBe(false);
    expect(undecodable.Error).toContain('Broken');
  });
});

// ─── switching ───────────────────────────────────────────────────────────────

describe('switching the active page', () => {
  it("preserves each page's own op history across a round trip, chain intact", () => {
    const r = loaded([
      ['Home', homeTree],
      ['About', aboutTree],
    ]);
    // Edit Home (one recorded op), then walk away and back.
    const homeEdited = edit(r.Session, retitleA);
    expect(appliedOps(homeEdited).length).toBe(1);

    const toAbout = switchResult(r.Site, homeEdited, 'About');
    expect(toAbout.Ok).toBe(true);
    expect(activePage(toAbout.Site)).toBe('About');
    expect(canonicalTree(toAbout.Session)).toBe(canon(aboutTree));
    expect(appliedOps(toAbout.Session).length).toBe(0);

    const back = switchResult(toAbout.Site, toAbout.Session, 'Home');
    expect(back.Ok).toBe(true);
    expect(canonicalTree(back.Session)).toContain('"A2"');
    expect(appliedOps(back.Session).length).toBe(1);
    expect(verifyResult(back.Session)).toEqual({ ReplayOk: true, ChainOk: true, Steps: 1 });
  });

  it('refuses the already-active page and an unknown page, changing nothing', () => {
    const r = loaded([
      ['Home', homeTree],
      ['About', aboutTree],
    ]);
    expect(switchResult(r.Site, r.Session, 'Home').Ok).toBe(false);
    expect(switchResult(r.Site, r.Session, 'Nowhere').Ok).toBe(false);
  });
});

// ─── the cross-page move ─────────────────────────────────────────────────────

describe('move to page…', () => {
  it('lifts the subtree off the source and appends it to the destination root', () => {
    const r = loaded([
      ['Home', homeTree],
      ['About', aboutTree],
    ]);
    const mv = moveResult(r.Site, r.Session, 'card', 'About');
    expect(mv.Ok).toBe(true);

    // Source: the whole subtree is gone.
    const source = canonicalTree(mv.Session);
    expect(source).not.toContain('"card"');
    expect(source).not.toContain('"Y"');

    // Destination: the subtree arrived (last child of the root), content intact.
    const dest = canonicalTree(shelfSessionOf(mv.Site, 'About'));
    expect(dest).toContain('"card"');
    expect(dest).toContain('"Y"');
    expect(dest).toContain('"About X"');

    // The destination re-decodes through the strict decoder (no duplicate ids
    // survived the collision remap: About already had an "x").
    expect(decodeNode(dest).ok).toBe(true);
    // Exactly one literal id "x" remains — the moved copy was remapped.
    expect(dest.match(/"id":"x"/g)?.length ?? 0).toBe(1);
  });

  it('records one ordinary op per tree, both carrying the SAME correlation actor', () => {
    const r = loaded([
      ['Home', homeTree],
      ['About', aboutTree],
    ]);
    const mv = moveResult(r.Site, r.Session, 'card', 'About');
    expect(mv.Ok).toBe(true);

    const sourceIds = Array.from(originIds(mv.Session)) as string[];
    const destSession = shelfSessionOf(mv.Site, 'About');
    const destIds = Array.from(originIds(destSession)) as string[];

    const sourceActor = sourceIds[sourceIds.length - 1];
    const destActor = destIds[destIds.length - 1];
    expect(sourceActor.startsWith('navigator:move:')).toBe(true);
    expect(destActor).toBe(sourceActor); // the shared correlation annotation

    // Each leg is an ordinary single-tree op — no new op kind anywhere.
    expect(Array.from(logKinds(mv.Session)).pop()).toBe('RemoveNode');
    expect(Array.from(logKinds(destSession)).pop()).toBe('InsertChild');

    // Both streams replay and chain-verify after the move.
    expect(verifyResult(mv.Session)).toEqual({ ReplayOk: true, ChainOk: true, Steps: 1 });
    expect(verifyResult(destSession)).toEqual({ ReplayOk: true, ChainOk: true, Steps: 1 });
  });

  it('refuses the root, an unknown node, an unknown page, and the active page itself', () => {
    const r = loaded([
      ['Home', homeTree],
      ['About', aboutTree],
    ]);
    expect(moveResult(r.Site, r.Session, 'home-root', 'About').Ok).toBe(false);
    expect(moveResult(r.Site, r.Session, 'nope', 'About').Ok).toBe(false);
    expect(moveResult(r.Site, r.Session, 'card', 'Nowhere').Ok).toBe(false);
    expect(moveResult(r.Site, r.Session, 'card', 'Home').Ok).toBe(false);
    // A refusal surfaces as the site's notice and changes no session.
    const refused = moveResult(r.Site, r.Session, 'card', 'Nowhere');
    expect(noticeKind(refused.Site)).toBe('refused');
    expect(canonicalTree(refused.Session)).toBe(canon(homeTree));
  });
});

// ─── paired undo ─────────────────────────────────────────────────────────────

describe('paired undo (one editor action, two trees)', () => {
  it('reverts both legs, restoring both pages byte-identically', () => {
    const r = loaded([
      ['Home', homeTree],
      ['About', aboutTree],
    ]);
    const mv = moveResult(r.Site, r.Session, 'card', 'About');
    expect(mv.Ok).toBe(true);
    expect(canUndoMove(mv.Site, mv.Session)).toBe(true);

    const undone = undoMoveResult(mv.Site, mv.Session);
    expect(undone.Ok).toBe(true);
    expect(canonicalTree(undone.Session)).toBe(canon(homeTree));
    expect(canonicalTree(shelfSessionOf(undone.Site, 'About'))).toBe(canon(aboutTree));
    expect(canUndoMove(undone.Site, undone.Session)).toBe(false);
  });

  it('still works after switching to a third page (neither leg is active)', () => {
    const r = loaded([
      ['Home', homeTree],
      ['About', aboutTree],
      ['Form', formTree],
    ]);
    const mv = moveResult(r.Site, r.Session, 'card', 'About');
    const onForm = switchResult(mv.Site, mv.Session, 'Form');
    expect(onForm.Ok).toBe(true);
    expect(canUndoMove(onForm.Site, onForm.Session)).toBe(true);

    const undone = undoMoveResult(onForm.Site, onForm.Session);
    expect(undone.Ok).toBe(true);
    expect(canonicalTree(shelfSessionOf(undone.Site, 'Home'))).toBe(canon(homeTree));
    expect(canonicalTree(shelfSessionOf(undone.Site, 'About'))).toBe(canon(aboutTree));
  });

  it('is honestly refused once either tree has moved on (replay undoes from the top or not at all)', () => {
    const r = loaded([
      ['Home', homeTree],
      ['About', aboutTree],
    ]);
    const mv = moveResult(r.Site, r.Session, 'card', 'About');
    expect(canUndoMove(mv.Site, mv.Session)).toBe(true);

    // An ordinary edit on the source page buries its leg.
    const buried = edit(mv.Session, retitleA);
    expect(canUndoMove(mv.Site, buried)).toBe(false);
    expect(undoMoveResult(mv.Site, buried).Ok).toBe(false);
  });
});

// ─── the guard rail ──────────────────────────────────────────────────────────

describe('module-state guard rail', () => {
  it('moving a subtree that reads $state surfaces the typed warning naming the keys', () => {
    const r = loaded([
      ['Form', formTree],
      ['About', aboutTree],
    ]);
    const mv = moveResult(r.Site, r.Session, 'panel', 'About');
    expect(mv.Ok).toBe(true); // advisory, not a refusal
    expect(Array.from(mv.WarnKeys)).toEqual(['loading']);
    expect(noticeKind(mv.Site)).toBe('state-warning');
    expect(noticeLine(mv.Site)).toContain('loading');
    expect(noticeLine(mv.Site)).toContain('does not travel');
  });

  it('a subtree with no state reads moves with a plain info notice', () => {
    const r = loaded([
      ['Home', homeTree],
      ['About', aboutTree],
    ]);
    const mv = moveResult(r.Site, r.Session, 'a', 'About');
    expect(mv.Ok).toBe(true);
    expect(Array.from(mv.WarnKeys)).toEqual([]);
    expect(noticeKind(mv.Site)).toBe('info');
  });

  it('exposes the pre-move advisory for the picker (keys per focused subtree)', () => {
    const r = loaded([['Form', formTree]]);
    expect(Array.from(stateKeysAt(r.Session, 'panel'))).toEqual(['loading']);
    expect(Array.from(stateKeysAt(r.Session, 'btn'))).toEqual(['loading']);
    expect(Array.from(stateKeysAt(r.Session, 'form-root'))).toEqual(['loading']);
    expect(Array.from(stateKeysAt(r.Session, 'nope'))).toEqual([]);
  });
});
