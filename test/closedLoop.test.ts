// Phase 326 (Stage 3) – the F#/Fable closed loop, exercised headlessly over the
// Fable output. The loop logic (decode → apply → fold + the current-tree injection)
// lives in app/Session.fs and compiles to app/output/Session.js; this drives it in
// node via vitest. The flat `ingestResult` / `lastMessageContent` helpers project
// the F# DU / list results to assertable plain values across the Fable boundary.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { describe, it, expect } from 'vitest';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { empty, ingestResult, treeJson, lastMessageContent } from '../app/output/Session.js';
// @ts-expect-error untyped Fable output
import { value as systemPrompt } from '../app/output/SystemPrompt.js';

// A corpus-valid full-tree emission (wire-format-fixtures/nodes/metric-1 – the
// canonical flat shape the F# decoder certifies against), fenced as a model emits it.
const metricNode =
  '{"id":"metric-1","kind":{"$type":"Metric","format":{"$type":"Currency","code":"GBP"},"icon":"trending-up","label":"Revenue","subtext":"vs last month","tone":"Brand","trend":{"$type":"Static","value":0.07},"trendFormat":{"$type":"Percent","decimals":1},"value":{"$type":"Static","value":1234.5}}}';
const treeEmission = 'Here is your metric.\n\n```json\n' + metricNode + '\n```';

// A follow-up TreeOp (wire-format-fixtures/ops/op-updatestyle) targeting metric-1.
const styleOp =
  '```json\n{"$type":"UpdateStyle","style":{"emphasis":"Loud","tone":"Success","weight":"Spacious"},"target":"metric-1"}\n```';

describe('the F#/Fable closed loop', () => {
  it('buildMessages injects nothing on the first turn (no tree yet)', () => {
    expect(lastMessageContent(empty, 'build a dashboard')).toBe('build a dashboard');
    expect(treeJson(empty)).toBe('');
  });

  it('a response with no JSON is a recoverable no-json failure (never throws)', () => {
    const r = ingestResult(empty, 'I cannot help with that.');
    expect(r.Ok).toBe(false);
    expect(r.Error).toBe('no-json');
  });

  it('a TreeOp before any tree is a no-tree failure', () => {
    const r = ingestResult(empty, styleOp);
    expect(r.Ok).toBe(false);
    expect(r.Error).toBe('no-tree');
  });

  it('a full-tree emission decodes, folds in, and round-trips through the wire codec', () => {
    const r = ingestResult(empty, treeEmission);
    expect(r.Ok).toBe(true);
    expect(r.Mode).toBe('tree');
    const json = treeJson(r.Next);
    expect(json).not.toBe('');
    expect(json).toContain('"metric-1"');
  });

  it('the closed loop injects the current tree JSON into the next turn', () => {
    const withTree = ingestResult(empty, treeEmission).Next;
    const msg = lastMessageContent(withTree, 'add a heading');
    expect(msg).toContain('canonical wire-format tree');
    expect(msg).toContain('"metric-1"');
    expect(msg).toContain('add a heading');
  });

  it('a follow-up TreeOp applies against the current tree (decode → apply → fold)', () => {
    const withTree = ingestResult(empty, treeEmission).Next;
    const r = ingestResult(withTree, styleOp);
    expect(r.Ok).toBe(true);
    expect(r.Mode).toBe('op');
    // the inserted markdown child is now in the folded tree
    expect(treeJson(r.Next)).toContain('"Success"');
  });

  it('a malformed wire document is a recoverable decode failure', () => {
    const r = ingestResult(empty, '```json\n{"id":"x","kind":{"$type":"Nonsense"}}\n```');
    expect(r.Ok).toBe(false);
    expect(r.Error).toBe('decode');
  });
});

describe('the system prompt teaches the canonical flat wire format', () => {
  // Guard against regressing to the stale verbose shape (Display/Layout category
  // wrappers + "spec" + "state"/"style") the F# decoder rejects.
  it('does not teach the stale verbose envelope', () => {
    expect(systemPrompt).not.toContain('"$type":"Display"');
    expect(systemPrompt).not.toContain('"$type":"Layout"');
    expect(systemPrompt).not.toContain('"spec":');
  });

  it("every fenced example in the prompt decodes/applies through the app's own loop", () => {
    // Pull each ```json … ``` block and ingest it (full trees against an empty
    // session; ops against a session seeded by the prior tree example). The
    // prompt now inlines the drift-checked prompt pack (fuaran/docs/prompt-pack)
    // whose examples are pretty-printed and whose prose mentions a fence marker
    // inline, so: fences are LINE-ANCHORED (an inline mention in prose is not a
    // block), and the op test tolerates `"$type": "…"` spacing. The op list is
    // the full TreeOp vocabulary – a pack example using any op must not be
    // misread as a tree.
    const blocks = [
      ...systemPrompt.matchAll(/(?:^|\n)[ \t]*```json[^\S\n]*\n([\s\S]*?)\n[ \t]*```/g),
    ].map((m: RegExpMatchArray) => (m[1] ?? '').trim());
    expect(blocks.length).toBeGreaterThanOrEqual(4);

    // Three example classes, three contracts. A full NODE (top-level "id" +
    // "kind") must decode through the loop. A TREEOP must apply cleanly
    // against at least one EARLIER tree example — the pack's op examples
    // target the tree they discuss, which is not necessarily the
    // immediately-preceding fence (op-replacebinding under "Editing an
    // existing tree" targets metric-1 from the opening example). Anything
    // else is a teaching FRAGMENT (a Binding / TextSource sub-shape shown in
    // prose) and must at least be valid JSON.
    const seenTrees: unknown[] = [];
    let fullTrees = 0;
    for (const block of blocks) {
      const isOp =
        /"\$type":\s*"(EditNode|UpdateProp|ReplaceBinding|UpdateStyle|UpdateState|InsertChild|RemoveNode|MoveNode|ReorderChildren|Batch)"/.test(
          block.slice(0, 200),
        );
      // A fragment may be a standalone value OR a field-in-context snippet
      // (`"value": { … }`), which parses only when brace-wrapped.
      let parsed: unknown;
      try {
        parsed = JSON.parse(block);
      } catch {
        try {
          parsed = JSON.parse(`{${block}}`);
        } catch {
          expect.fail(`example is not valid JSON (bare or brace-wrapped):\n${block}`);
        }
      }
      const isNode =
        !isOp &&
        typeof parsed === 'object' &&
        parsed !== null &&
        'id' in parsed &&
        'kind' in parsed;
      if (isNode) {
        const r = ingestResult(empty, block);
        expect(r.Ok, `example failed to decode: ${r.Error}\n${block}`).toBe(true);
        seenTrees.push(r.Next);
        fullTrees += 1;
      } else if (isOp) {
        const applies = seenTrees.some((s) => ingestResult(s, block).Ok);
        expect(applies, `op example applies against no earlier tree example:\n${block}`).toBe(true);
      }
    }
    expect(fullTrees).toBeGreaterThanOrEqual(4); // the teaching must stay example-driven
  });
});
