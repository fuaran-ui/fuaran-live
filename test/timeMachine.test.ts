// Phase 296 – the Time Machine: exact replay, side-by-side forks, and the 3-way
// merge round-trip through the DAG, exercised headlessly over the Fable output.
//
// The page lives in app/showcase/TimeMachine.fs and compiles to
// app/showcase/output/TimeMachine.js; this drives its headless surface in node
// via vitest, following the Phase 710-713 pattern: every tree crosses the Fable
// boundary as the canonical wire JSON the REAL encoder produced, so each claim
// below is a byte comparison and never a hand-waved shape.
//
// Three claims, one per acceptance leg:
//   1. Replay is exact – frame n is what the shipped apply engine produces from
//      frame n-1 and op n, for every n, and every frame is distinct.
//   2. Forks are real – a branch forked at the head is a genuinely divergent
//      tree; two branches diverge from each other; forking too early is a real
//      typed apply error, not a blank stage.
//   3. The merge round-trips through the DAG – forked at the head the 3-way
//      merge composes to exactly the branch's tree (ancestor == ours, so theirs
//      wins outright); forked earlier the trunk's later edits meet the branch's
//      and the SAME engine names the contended node as a conflict, which the
//      lenient resolution settles to the ancestor's value.
//
// Requires `pnpm run fable:app` to have produced app/showcase/output/.

import { describe, expect, it } from 'vitest';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import * as TM from '../app/showcase/output/TimeMachine.js';

const total: number = TM.turnTotal;
const branches: string[] = Array.from(TM.branchIds as string[]);

const stripOk = (s: string): string => {
  expect(s.startsWith('ok:')).toBe(true);
  return s.slice('ok:'.length);
};

describe('Phase 296 – the scrubber replays the session exactly', () => {
  it('records a real multi-turn arc', () => {
    expect(total).toBeGreaterThanOrEqual(10);
  });

  it('every frame is the apply engine one step on from the previous frame', () => {
    for (let n = 1; n <= total; n++) {
      expect(TM.stepJson(n)).toBe(TM.frameJson(n));
    }
  });

  it('every frame is distinct – each op genuinely changes the tree', () => {
    const frames = new Set<string>();
    for (let n = 0; n <= total; n++) frames.add(TM.frameJson(n));
    expect(frames.size).toBe(total + 1);
  });

  it('frames are canonical wire JSON rooted at the same node', () => {
    for (let n = 0; n <= total; n++) {
      const tree = JSON.parse(TM.frameJson(n));
      expect(tree.id).toBe('tm-root');
    }
  });
});

describe('Phase 296 – branching renders ≥2 variants beside the trunk', () => {
  it('offers at least two fork branches', () => {
    expect(branches.length).toBeGreaterThanOrEqual(2);
  });

  it('each branch forked at the head is a genuinely divergent tree', () => {
    const head = TM.frameJson(total);
    const forks = branches.map((b) => stripOk(TM.forkJson(b, total)));
    for (const f of forks) expect(f).not.toBe(head);
    expect(new Set(forks).size).toBe(forks.length);
  });

  it('forking before the branch’s target nodes exist is a real typed apply error', () => {
    // The "exec" branch removes the accounts grid, which frame 0 does not hold yet.
    expect(TM.forkJson('exec', 0).startsWith('error:')).toBe(true);
  });

  it('an unknown branch id is refused, not defaulted', () => {
    expect(TM.forkJson('nope', total).startsWith('error:')).toBe(true);
    expect(TM.mergeJson('nope', total).startsWith('error:')).toBe(true);
  });
});

describe('Phase 296 – the merge round-trips through the DAG', () => {
  it('forked at the head, the 3-way merge composes to exactly the branch tree', () => {
    for (const b of branches) {
      const fork = stripOk(TM.forkJson(b, total));
      expect(TM.mergeJson(b, total)).toBe('merged:' + fork);
    }
  });

  it('forked earlier, the trunk’s later title edit and the branch’s collide as a named conflict', () => {
    // Both branches retitle the dashboard; the trunk retitles it on its last turn.
    for (const b of branches) {
      const out: string = TM.mergeJson(b, 5);
      expect(out.startsWith('conflict:')).toBe(true);
      expect(out.slice('conflict:'.length).split(',')).toContain('tm-title');
    }
  });

  it('the lenient resolution settles every conflict to the ancestor’s value', () => {
    const ancestorTitle = 'Sales overview (draft)';
    expect(TM.frameJson(5)).toContain(ancestorTitle);
    for (const b of branches) {
      const out: string = TM.mergeLenientJson(b, 5);
      expect(out.startsWith('merged:')).toBe(true);
      expect(out).toContain(ancestorTitle);
    }
  });

  it('a merge of a fork the engine refused is the same error, not a merged tree', () => {
    expect(TM.mergeJson('exec', 0).startsWith('error:')).toBe(true);
    expect(TM.mergeLenientJson('exec', 0).startsWith('error:')).toBe(true);
  });
});
