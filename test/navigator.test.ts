// Phase 710 – the Navigator's cursor model, exercised headlessly over the Fable
// output. Phase 744 extracted the model out of app/navigator/Navigator.fs into
// the session-free app/navigator/Cursor.fs, so this suite now drives
// app/output/navigator/Cursor.js – the module that OWNS the walk, and the one
// both entries of the site link. The assertions are unchanged: the walk they
// describe did not move, only the file it lives in. (`propSummary` stays with
// the tab – it reaches for the session's pretty-printer – so it is still
// imported from Navigator.js.) The flat `walkIds` / `cursorIds` / `focusedText`
// helpers project the F# list + NodeId DU results to assertable plain values
// across the Fable boundary – the same idiom `Session.ingestResult` uses for
// the closed loop.
//
// The point of the suite is the identity rule: the cursor is addressed BY ID, so
// a tree replacement must re-resolve it (or fall back to the nearest surviving
// ancestor) rather than silently landing on whatever node now sits at the old
// position. Every tree below is fed through the REAL strict decoder (via the
// session's own ingest path), so these fixtures are decodable canonical wire, not
// hand-waved shapes.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { describe, it, expect } from 'vitest';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { empty, ingestResult } from '../app/output/Session.js';
import {
  atRoot,
  next,
  prev,
  parent,
  firstChild,
  jumpToText,
  reresolve,
  position,
  walkIds,
  cursorIds,
  focusedText,
  focusedNode,
  // @ts-expect-error untyped Fable output
} from '../app/output/navigator/Cursor.js';
// @ts-expect-error untyped Fable output
import { propSummary } from '../app/output/navigator/Navigator.js';

// nav-root ▸ nav-card ▸ (nav-a, nav-b), then nav-c – five nodes, DFS pre-order.
const baseTree =
  '{"id":"nav-root","kind":{"$type":"Box","children":[' +
  '{"id":"nav-card","kind":{"$type":"Box","children":[' +
  '{"id":"nav-a","kind":{"$type":"Markdown","text":"A"}},' +
  '{"id":"nav-b","kind":{"$type":"Markdown","text":"B"}}],' +
  '"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Card"}},' +
  '{"id":"nav-c","kind":{"$type":"Markdown","text":"C"}}],' +
  '"layout":{"$type":"Auto"},"role":"Dashboard"}}';

// The same tree with nav-a gone and nav-c gone – nav-card survives.
const prunedTree =
  '{"id":"nav-root","kind":{"$type":"Box","children":[' +
  '{"id":"nav-card","kind":{"$type":"Box","children":[' +
  '{"id":"nav-b","kind":{"$type":"Markdown","text":"B"}}],' +
  '"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Card"}}],' +
  '"layout":{"$type":"Auto"},"role":"Dashboard"}}';

// A wholesale replacement sharing not one id with the base tree.
const foreignTree = '{"id":"other-root","kind":{"$type":"Markdown","text":"Z"}}';

const fence = (json: string) => '```json\n' + json + '\n```';

const treeOf = (json: string) => {
  const r = ingestResult(empty, fence(json));
  expect(r.Ok).toBe(true);
  return r.Next.Tree;
};

describe('the Navigator cursor model', () => {
  it('walks the tree in DFS pre-order, root first', () => {
    expect(Array.from(walkIds(treeOf(baseTree)))).toEqual([
      'nav-root',
      'nav-card',
      'nav-a',
      'nav-b',
      'nav-c',
    ]);
  });

  it('next steps through every node and STOPS at the end – it does not wrap', () => {
    const tree = treeOf(baseTree);
    let c = atRoot(tree);
    const visited = [focusedText(c)];

    for (let i = 0; i < 10; i++) {
      c = next(tree, c);
      visited.push(focusedText(c));
    }

    // Five distinct nodes, then the last one repeats – stopped, not wrapped.
    expect(visited.slice(0, 5)).toEqual(['nav-root', 'nav-card', 'nav-a', 'nav-b', 'nav-c']);
    expect(new Set(visited.slice(5)).size).toBe(1);
    expect(visited[10]).toBe('nav-c');
  });

  it('prev walks back and stops at the root', () => {
    const tree = treeOf(baseTree);
    let c = atRoot(tree);
    for (let i = 0; i < 4; i++) c = next(tree, c);
    expect(focusedText(c)).toBe('nav-c');

    for (let i = 0; i < 10; i++) c = prev(tree, c);
    expect(focusedText(c)).toBe('nav-root');
  });

  it('firstChild descends and parent climbs; both are no-ops at the ends', () => {
    const tree = treeOf(baseTree);
    const root = atRoot(tree);

    const card = firstChild(tree, root);
    expect(focusedText(card)).toBe('nav-card');

    const a = firstChild(tree, card);
    expect(focusedText(a)).toBe('nav-a');

    // nav-a is a leaf – firstChild leaves the cursor alone.
    expect(focusedText(firstChild(tree, a))).toBe('nav-a');

    expect(focusedText(parent(tree, a))).toBe('nav-card');
    expect(focusedText(parent(tree, parent(tree, a)))).toBe('nav-root');
    // The root has no parent – a no-op, never a crash or an empty path.
    expect(focusedText(parent(tree, root))).toBe('nav-root');
  });

  it('the cursor is an id-PATH, root → focused – never an index', () => {
    const tree = treeOf(baseTree);
    const a = firstChild(tree, firstChild(tree, atRoot(tree)));
    expect(Array.from(cursorIds(a))).toEqual(['nav-root', 'nav-card', 'nav-a']);
  });

  it('position reports the 1-based DFS ordinal and the walk length', () => {
    const tree = treeOf(baseTree);
    const c = next(tree, next(tree, atRoot(tree)));
    expect(Array.from(position(tree, c))).toEqual([3, 5]);
  });

  it('jumpTo moves to any id in the tree and ignores one that is absent', () => {
    const tree = treeOf(baseTree);
    const root = atRoot(tree);
    const jumped = jumpToText(tree, root, 'nav-b');
    expect(Array.from(cursorIds(jumped))).toEqual(['nav-root', 'nav-card', 'nav-b']);
    expect(focusedText(jumpToText(tree, jumped, 'no-such-node'))).toBe('nav-b');
  });

  // ─── the identity rule ─────────────────────────────────────────────────────

  it('re-resolves a surviving id across a whole-tree replacement', () => {
    const before = treeOf(baseTree);
    const after = treeOf(prunedTree);

    const onB = jumpToText(before, atRoot(before), 'nav-b');
    const resolved = reresolve(after, onB);

    // nav-b survived – the cursor is still on it, at its NEW path.
    expect(focusedText(resolved)).toBe('nav-b');
    expect(Array.from(cursorIds(resolved))).toEqual(['nav-root', 'nav-card', 'nav-b']);
  });

  it('falls back to the nearest surviving ancestor when the focused id vanishes', () => {
    const before = treeOf(baseTree);
    const after = treeOf(prunedTree);

    // nav-a is gone from the pruned tree; nav-card, its parent, is not.
    const onA = firstChild(before, firstChild(before, atRoot(before)));
    expect(focusedText(onA)).toBe('nav-a');

    const resolved = reresolve(after, onA);
    expect(focusedText(resolved)).toBe('nav-card');
  });

  it('falls back to the root when nothing on the path survives', () => {
    const before = treeOf(baseTree);
    const after = treeOf(foreignTree);

    const onA = firstChild(before, firstChild(before, atRoot(before)));
    expect(focusedText(reresolve(after, onA))).toBe('other-root');
  });

  it('a positional cursor would have landed elsewhere – the id one does not', () => {
    const before = treeOf(baseTree);
    const after = treeOf(prunedTree);

    // nav-c was DFS #5 in the base tree; the pruned tree has only 3 nodes, and
    // nothing sits at #5. An ordinal cursor has nowhere honest to go; the id
    // cursor climbs to the ancestor that is still there.
    const onC = jumpToText(before, atRoot(before), 'nav-c');
    expect(Array.from(position(before, onC))).toEqual([5, 5]);
    expect(Array.from(walkIds(after)).length).toBe(3);
    expect(focusedText(reresolve(after, onC))).toBe('nav-root');
  });

  it('re-resolving an absent stored cursor yields the root', () => {
    const tree = treeOf(baseTree);
    expect(focusedText(reresolve(tree, undefined))).toBe('nav-root');
  });

  // ─── the read-only node card ───────────────────────────────────────────────

  it('the node card summarises the node ITSELF, not its subtree', () => {
    const tree = treeOf(baseTree);
    const card = firstChild(tree, atRoot(tree));
    const summary = propSummary(focusedNode(tree, card));

    expect(summary).toContain('"nav-card"');
    expect(summary).toContain('"Box"');
    // The children are the walk's business, not the card's.
    expect(summary).not.toContain('nav-a');
    expect(summary).not.toContain('nav-b');
  });
});
