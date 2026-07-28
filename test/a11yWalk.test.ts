// Phase 717 – the Navigator's accessibility walk mode, exercised headlessly
// over the Fable output. The lens lives in app/navigator/A11yWalk.fs and
// compiles to app/output/navigator/A11yWalk.js; this drives it in node via
// vitest, following the Phase 710/711/712 pattern — flat projections across the
// Fable boundary, every fixture fed through the REAL strict decoder via the
// session's own ingest path, so nothing here is a hand-waved shape.
//
// The suite's claims are the phase's acceptance criteria, plus the two guards
// that keep an audit honest:
//
//   1. A tree with seeded a11y defects shows the CORRECT flag count; the
//      flags-only walk visits EXACTLY the defective nodes; fixing each via the
//      quick-fix drops the count to zero.
//   2. Fixes are ordinary recorded ops — undo restores the pre-fix tree
//      byte-identically on the canonical wire, and the record attributes them
//      as human-navigator edits like any other hand edit.
//   3. NO FALSE POSITIVES. A node that declares its accessible name is not
//      flagged for an empty structural label; a well-formed node is silent.
//      An audit that cries wolf is worse than no audit, so this is asserted as
//      hard as the true-positive cases.
//   4. The re-derived interactive-kind set is PINNED against the language's own
//      source — every kind the lens calls interactive really carries a
//      non-`none` per-kind accessibility default upstream.
//
// Requires `pnpm run fable:app` (or `dotnet fable app --outDir app/output`).

import { describe, it, expect } from 'vitest';
import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
import {
  // @ts-expect-error untyped Fable output
  empty,
  // @ts-expect-error untyped Fable output
  ingestResult,
} from '../app/output/Session.js';
import {
  // @ts-expect-error untyped Fable output
  flagSummary,
  // @ts-expect-error untyped Fable output
  flaggedIds,
  // @ts-expect-error untyped Fable output
  flagCount,
  // @ts-expect-error untyped Fable output
  summary,
  // @ts-expect-error untyped Fable output
  flagsAt,
  // @ts-expect-error untyped Fable output
  ariaAt,
  // @ts-expect-error untyped Fable output
  declaredTrait,
  // @ts-expect-error untyped Fable output
  nextFlagText,
  // @ts-expect-error untyped Fable output
  prevFlagText,
  // @ts-expect-error untyped Fable output
  quickFixAt,
  // @ts-expect-error untyped Fable output
  interactiveKindNames,
} from '../app/output/navigator/A11yWalk.js';
import {
  // @ts-expect-error untyped Fable output
  undo,
  // @ts-expect-error untyped Fable output
  originKinds,
  // @ts-expect-error untyped Fable output
  originIds,
  // @ts-expect-error untyped Fable output
  logKinds,
  // @ts-expect-error untyped Fable output
  canonicalTree,
} from '../app/output/navigator/OpLog.js';
// @ts-expect-error untyped Fable output
import { findNode } from '../app/output/fuaran-dotnet/src/Fuaran.UI.Ops/Introspect.js';
// @ts-expect-error untyped Fable output
import { NodeId } from '../app/output/fuaran-dotnet/src/Fuaran.UI/Types.js';

// ─── fixtures ────────────────────────────────────────────────────────────────
//
// Canonical wire, decoded by the real strict decoder. Every defect below is a
// shape a model genuinely emits: the smart-constructor accessibility defaults
// are applied at CONSTRUCTION time, so a tree that arrived as JSON carries only
// the ARIA its JSON stated — which is usually none.

const treeOf = (...children: string[]) =>
  '{"id":"root","kind":{"$type":"Box","children":[' +
  children.join(',') +
  '],"layout":{"$type":"Auto"},"role":"Dashboard"}}';

/** DEFECT — an interactive button with nothing to name it. */
const btnNameless =
  '{"id":"btn-bad","kind":{"$type":"Button","label":"","onClick":{"$type":"Chain","ops":[]},"variant":"Primary"}}';

/** CLEAN — the same button, named by its text content. */
const btnNamed =
  '{"id":"btn-ok","kind":{"$type":"Button","label":"Save","onClick":{"$type":"Chain","ops":[]},"variant":"Primary"}}';

/** CLEAN — an empty structural label, but the trait declares the name. */
const btnAriaNamed =
  '{"id":"btn-aria","accessibility":{"label":{"$type":"Static","value":"Close"}},' +
  '"kind":{"$type":"Button","label":"","onClick":{"$type":"Chain","ops":[]},"variant":"Primary"}}';

/** DEFECT — a required text field left blank; the node renders empty. */
const headingBlank =
  '{"id":"head-bad","kind":{"$type":"Heading","level":2,"text":"","variant":"Standard"}}';

/** DEFECT — an accessibility reference naming a node that is not in the tree. */
const danglingRef =
  '{"id":"ref-bad","accessibility":{"labelledBy":"no-such-node"},' +
  '"kind":{"$type":"Markdown","text":"Body copy"}}';

/** CLEAN — a plain node with content and no ARIA claims at all. */
const plainProse = '{"id":"prose","kind":{"$type":"Markdown","text":"All well here."}}';

/** Ingest a tree and hand back the session (throwing loudly on a decode fail). */
function session(wire: string) {
  const r = ingestResult(empty, wire);
  if (!r.Ok) {
    throw new Error(`fixture did not decode: ${r.Error}\n${wire}`);
  }
  return r.Next;
}

/** One field of a `"a|b|c|d"` summary row, as a definite string. */
function part(row: string, index: number): string {
  return row.split('|')[index] ?? '';
}

/** The codes reported against one node, order-independent. */
function codesAt(s: any, nodeId: string): string[] {
  return (flagsAt(s.Tree, nodeId) as string[]).map((row) => part(row, 0)).sort();
}

describe('the accessibility lens flags what a screen reader would miss', () => {
  it('counts exactly the seeded defects and leaves clean nodes alone', () => {
    const s = session(
      treeOf(btnNameless, btnNamed, btnAriaNamed, headingBlank, danglingRef, plainProse),
    );

    // Three defective nodes among seven (root + six children).
    const [flagged, total] = summary(s.Tree);
    expect(total).toBe(7);
    expect(flagged).toBe(3);
    expect(Array.from(flaggedIds(s.Tree))).toEqual(['btn-bad', 'head-bad', 'ref-bad']);

    // Each defect is diagnosed as the RIGHT kind of defect, not merely counted.
    expect(codesAt(s, 'btn-bad')).toEqual(['A11Y-NAME']);
    expect(codesAt(s, 'head-bad')).toEqual(['A11Y-TEXT']);
    expect(codesAt(s, 'ref-bad')).toEqual(['A11Y-REF']);
  });

  it('does not cry wolf: a named node, an aria-named node and the root are silent', () => {
    const s = session(
      treeOf(btnNameless, btnNamed, btnAriaNamed, headingBlank, danglingRef, plainProse),
    );

    // The one that matters most: an EMPTY structural label is not a finding
    // when the trait declares the name, because aria-label wins over text
    // content in the accessible-name computation.
    expect(codesAt(s, 'btn-aria')).toEqual([]);
    expect(codesAt(s, 'btn-ok')).toEqual([]);
    expect(codesAt(s, 'prose')).toEqual([]);
    expect(codesAt(s, 'root')).toEqual([]);
  });

  it('reads the emitted aria from the renderer projection, not from a second opinion', () => {
    const s = session(treeOf(btnAriaNamed, danglingRef, plainProse));

    // The renderer emits label → labelledby → describedby → role → live →
    // hidden, in that order, and omits every absent one.
    expect(Array.from(ariaAt(s.Tree, 'btn-aria'))).toEqual(['aria-label=Close']);
    expect(Array.from(ariaAt(s.Tree, 'ref-bad'))).toEqual(['aria-labelledby=no-such-node']);

    // A node with no trait emits nothing at all — which is exactly why a
    // model-emitted tree needs auditing in the first place.
    expect(Array.from(ariaAt(s.Tree, 'prose'))).toEqual([]);
    expect(Array.from(declaredTrait(findNodeIn(s, 'prose')))).toEqual([]);
    expect(Array.from(declaredTrait(findNodeIn(s, 'ref-bad')))).toEqual(['labelledBy']);
  });

  it('is honest about the finding no op can fix', () => {
    const s = session(treeOf(danglingRef));

    // `UpdateProp` paths are rooted inside the kind spec, so nothing in the op
    // vocabulary reaches the accessibility trait. The lens offers no fix path
    // rather than one that would not work.
    const rows = flagsAt(s.Tree, 'ref-bad') as string[];
    expect(rows).toHaveLength(1);
    expect(part(rows[0] ?? '', 2)).toBe('');

    const refused = quickFixAt(s, 'ref-bad', 'A11Y-REF', 'anything');
    expect(refused.Ok).toBe(false);
    expect(refused.Error).toMatch(/no op reaches the accessibility trait/);
    expect(refused.Next).toBe(s);
  });
});

describe('walking the flags', () => {
  it('steps flag order, not DFS order, and stops at both ends', () => {
    const s = session(
      treeOf(btnNameless, btnNamed, btnAriaNamed, headingBlank, danglingRef, plainProse),
    );

    // Forward from the root: the three defective nodes, then a stop.
    expect(nextFlagText(s.Tree, 'root')).toBe('btn-bad');
    expect(nextFlagText(s.Tree, 'btn-bad')).toBe('head-bad');
    expect(nextFlagText(s.Tree, 'head-bad')).toBe('ref-bad');
    expect(nextFlagText(s.Tree, 'ref-bad')).toBe('');

    // The clean nodes between them are skipped, which is the whole point.
    expect(nextFlagText(s.Tree, 'btn-ok')).toBe('head-bad');

    // Backward, symmetrically, stopping at the start rather than wrapping.
    expect(prevFlagText(s.Tree, 'ref-bad')).toBe('head-bad');
    expect(prevFlagText(s.Tree, 'head-bad')).toBe('btn-bad');
    expect(prevFlagText(s.Tree, 'btn-bad')).toBe('');
  });

  it('counts a multiply-flagged node as one stop on the walk', () => {
    // One node, two independent defects: a blank required text AND a dangling
    // reference. The walk must visit it once; the count of FINDINGS is still
    // two, and the two numbers are reported separately for that reason.
    const doubled =
      '{"id":"both","accessibility":{"describedBy":"ghost"},' +
      '"kind":{"$type":"Heading","level":1,"text":"","variant":"Standard"}}';
    const s = session(treeOf(doubled));

    expect(codesAt(s, 'both')).toEqual(['A11Y-REF', 'A11Y-TEXT']);
    expect(Array.from(flaggedIds(s.Tree))).toEqual(['both']);
    expect(summary(s.Tree)[0]).toBe(1);
    expect(flagCount(s.Tree)).toBe(2);
  });
});

describe('the quick-fix is an ordinary edit', () => {
  it('drops the count to zero as each fixable defect is fixed', () => {
    // Two fixable defects seeded; the unfixable class is exercised separately
    // above, because "drops to zero" can only be claimed of what an op reaches.
    let s = session(treeOf(btnNameless, headingBlank, btnNamed, plainProse));
    expect(summary(s.Tree)[0]).toBe(2);

    const first = quickFixAt(s, 'btn-bad', 'A11Y-NAME', 'Refresh');
    expect(first.Ok).toBe(true);
    s = first.Next;
    expect(summary(s.Tree)[0]).toBe(1);
    expect(Array.from(flaggedIds(s.Tree))).toEqual(['head-bad']);

    const second = quickFixAt(s, 'head-bad', 'A11Y-TEXT', 'Channel performance');
    expect(second.Ok).toBe(true);
    s = second.Next;

    // The audit passes: stepping the whole tree now finds nothing.
    expect(summary(s.Tree)[0]).toBe(0);
    expect(flagCount(s.Tree)).toBe(0);
    expect(Array.from(flagSummary(s.Tree))).toEqual([]);
    expect(nextFlagText(s.Tree, 'root')).toBe('');
  });

  it('really writes the value through the op path', () => {
    let s = session(treeOf(btnNameless));
    s = quickFixAt(s, 'btn-bad', 'A11Y-NAME', 'Refresh').Next;
    // Asserted on the CANONICAL wire, not the inspector's pretty-printed view:
    // the canonical form is the one the op log and the hash chain agree about.
    expect(canonicalTree(s)).toContain('"label":"Refresh"');
  });

  it('records each fix as an undoable human-navigator op', () => {
    const before = session(treeOf(btnNameless, headingBlank));
    const beforeWire = canonicalTree(before);

    let s = quickFixAt(before, 'btn-bad', 'A11Y-NAME', 'Refresh').Next;
    s = quickFixAt(s, 'head-bad', 'A11Y-TEXT', 'Channel performance').Next;

    // Ordinary ops in the ordinary stream — same shape a property-panel commit
    // produces, because it IS the same call.
    expect(Array.from(logKinds(s))).toEqual(['UpdateProp', 'UpdateProp']);
    expect(Array.from(originKinds(s))).toEqual(['human', 'human']);
    expect(Array.from(originIds(s))).toEqual(['navigator', 'navigator']);

    // Undo works because the op log replays, not because the lens kept a copy.
    // Asserted against the wire captured BEFORE the fixes: a bug corrupting the
    // tree and the record consistently would pass a weaker self-comparison.
    const undoneOnce = undo(s);
    const undoneTwice = undo(undoneOnce);
    expect(canonicalTree(undoneTwice)).toBe(beforeWire);

    // …and the defects are back, which is the audit's own view of the undo.
    expect(summary(undoneTwice.Tree)[0]).toBe(2);
  });

  it('refuses a fix whose flag is not on the node, changing nothing', () => {
    const s = session(treeOf(btnNamed));
    const refused = quickFixAt(s, 'btn-ok', 'A11Y-NAME', 'whatever');
    expect(refused.Ok).toBe(false);
    expect(refused.Next).toBe(s);
    expect(Array.from(logKinds(refused.Next))).toEqual([]);
  });
});

describe('the one re-derived table is pinned to the language', () => {
  // The lens re-derives which kinds are interactive because the language states
  // it one smart-constructor call site at a time rather than as a queryable
  // surface. The pin is ONE-DIRECTIONAL on purpose: every kind the lens calls
  // interactive must really carry a non-`none` default upstream (so the audit
  // cannot accuse a node of a defect the language does not recognise), but the
  // lens is NOT required to cover every such kind — an un-audited kind is a gap,
  // whereas a falsely-flagged one is a wrong answer.
  const languageSource = fileURLToPath(
    new URL('../../fuaran-dotnet/src/Fuaran.UI/Fuaran.fs', import.meta.url),
  );

  it('names only kinds the language gives a non-none accessibility default', () => {
    if (!existsSync(languageSource)) {
      // The sibling checkout is a build input, so this should not happen; skip
      // rather than fail, so a missing checkout never masquerades as drift.
      return;
    }
    const source = readFileSync(languageSource, 'utf8');

    for (const kind of interactiveKindNames as string[]) {
      const pairing = new RegExp(
        `NodeKind\\.${kind}\\([^)]*\\)\\)?\\s*\\n?\\s*Defaults\\.Accessibility\\.(\\w+)`,
      );
      const found = source.match(pairing);
      expect(found, `no Defaults.Accessibility pairing found for NodeKind.${kind}`).not.toBeNull();
      expect(found![1], `NodeKind.${kind} is paired with Defaults.Accessibility.none`).not.toBe(
        'none',
      );
    }
  });

  it('covers the four interactive kinds the lens claims', () => {
    expect(Array.from(interactiveKindNames).sort()).toEqual([
      'Button',
      'FileUpload',
      'Form',
      'Select',
    ]);
  });
});

// The flat surfaces are addressed by node id, so tests never need to hold a
// decoded `Node` — except `declaredTrait`, which takes one. Reach it the same
// way the navigator does: through the introspection surface.
function findNodeIn(s: any, nodeId: string) {
  const found = findNode(new NodeId(nodeId), s.Tree);
  if (found == null) {
    throw new Error(`no node '${nodeId}' in the fixture tree`);
  }
  return found;
}
