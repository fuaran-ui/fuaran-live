// Phase 712 – the session op log: attributed recording, undo/redo by replay,
// and the exportable artefact. The modules live in app/Session.fs (the record)
// and app/navigator/OpLog.fs (the cursor, the replay, the export); this drives
// them headlessly in node via vitest over the Fable output, following the Phase
// 710/711 pattern — flat projections across the Fable boundary, every fixture
// fed through the REAL strict decoder via the session's own ingest path.
//
// The suite's claims are exactly the phase's acceptance criteria:
//
//   1. N edits then N undos leaves the tree BYTE-IDENTICAL on the canonical
//      wire to the state before those edits; redo restores it. Byte-identical
//      is the assertion — not "looks the same", not "same node count".
//   2. The exported log replays cleanly through the public apply engine to
//      reproduce the final tree.
//   3. Human-navigator ops are distinguishable from AI ops in the stream.
//
// Undo is deliberately checked against a canonical string captured BEFORE the
// edits rather than against the post-undo session's own opinion of itself: a
// bug that corrupted both the tree and the record consistently would pass the
// weaker check.
//
// Requires `pnpm run fable:app` (or `dotnet fable app --outDir app/output`).

import { describe, it, expect } from 'vitest';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
import {
  // @ts-expect-error untyped Fable output
  empty,
  // @ts-expect-error untyped Fable output
  ingestResult,
} from '../app/output/Session.js';
// @ts-expect-error untyped Fable output
import { commitAt } from '../app/output/navigator/PropertyEditor.js';
import {
  // @ts-expect-error untyped Fable output
  cursor,
  // @ts-expect-error untyped Fable output
  recorded,
  // @ts-expect-error untyped Fable output
  canUndo,
  // @ts-expect-error untyped Fable output
  canRedo,
  // @ts-expect-error untyped Fable output
  undoN,
  // @ts-expect-error untyped Fable output
  redoN,
  // @ts-expect-error untyped Fable output
  originKinds,
  // @ts-expect-error untyped Fable output
  originIds,
  // @ts-expect-error untyped Fable output
  logKinds,
  // @ts-expect-error untyped Fable output
  logHashes,
  // @ts-expect-error untyped Fable output
  appliedOps,
  // @ts-expect-error untyped Fable output
  snapshotCount,
  // @ts-expect-error untyped Fable output
  canonicalTree,
  // @ts-expect-error untyped Fable output
  exportJson,
  // @ts-expect-error untyped Fable output
  exportedTree,
  // @ts-expect-error untyped Fable output
  replayExport,
  // @ts-expect-error untyped Fable output
  verifyResult,
  // @ts-expect-error untyped Fable output
  exportFilename,
  // @ts-expect-error untyped Fable output
  download,
} from '../app/output/navigator/OpLog.js';

// ─── fixtures ────────────────────────────────────────────────────────────────

const baseTree =
  '{"id":"root","kind":{"$type":"Box","children":[' +
  '{"id":"h","kind":{"$type":"Heading","level":2,"text":"Quarterly review","variant":"Standard"}},' +
  '{"id":"b","kind":{"$type":"Badge","label":"New","variant":"Brand"}}' +
  '],"layout":{"$type":"Auto"},"role":"Dashboard"}}';

/**
 * A model emission carrying one TreeOp — the AI half of the stream. The shape
 * is the canonical one pinned by the shared conformance corpus
 * (wire-format-fixtures/ops/op-updateprop.json), so these fixtures go through
 * the same strict decoder a real emission does.
 */
const modelOp = (nodeId: string, path: string, value: string) =>
  '{"$type":"UpdateProp","path":"' + path + '","target":"' + nodeId + '","value":"' + value + '"}';

/** Ingest an emission, failing loudly rather than silently returning the input. */
function ingest(s: any, wire: string) {
  const r = ingestResult(s, wire);
  if (!r.Ok) {
    throw new Error(`fixture did not ingest: ${r.Error}\n${wire}`);
  }
  return r.Next;
}

/** A navigator property commit, failing loudly on a refusal. */
function edit(s: any, nodeId: string, path: string, raw: string) {
  const r = commitAt(s, nodeId, path, raw);
  if (!r.Ok) {
    throw new Error(`commit refused: ${r.Error}`);
  }
  return r.Next;
}

/** The base session: a decoded tree, no ops yet. */
const seeded = () => ingest(empty, baseTree);

// ─── recording + attribution ─────────────────────────────────────────────────

describe('every applied op is recorded with its origin', () => {
  it('starts with a base tree and an empty record', () => {
    const s = seeded();
    expect(cursor(s)).toBe(0);
    expect(recorded(s)).toBe(0);
    expect(canUndo(s)).toBe(false);
    expect(canRedo(s)).toBe(false);
  });

  it('distinguishes human-navigator ops from AI ops in the stream', () => {
    let s = seeded();
    s = ingest(s, modelOp('h', 'Text', 'Annual review')); // the model
    s = edit(s, 'b', 'Label', 'Updated'); // the human
    s = ingest(s, modelOp('h', 'Text', 'Annual summary')); // the model again

    expect(originKinds(s)).toEqual(['agent', 'human', 'agent']);
    // The attribution id names the SURFACE the edit came from, so a reader of
    // the exported log can tell a panel commit from a loop emission by id too.
    expect(originIds(s)[1]).toBe('navigator');
  });

  it('records the op kind and a distinct chain hash per entry', () => {
    let s = seeded();
    s = edit(s, 'h', 'Text', 'One');
    s = edit(s, 'h', 'Text', 'Two');

    expect(logKinds(s)).toEqual(['UpdateProp', 'UpdateProp']);

    const hashes = logHashes(s) as string[];
    expect(hashes).toHaveLength(2);
    expect(hashes[0]).toMatch(/^[0-9a-f]{64}$/);
    expect(hashes[1]).not.toBe(hashes[0]);
  });

  it('keeps the record in step with the applied ops', () => {
    let s = seeded();
    s = edit(s, 'h', 'Text', 'One');
    s = ingest(s, modelOp('b', 'Label', 'Two'));

    expect(cursor(s)).toBe(2);
    expect(recorded(s)).toBe(2);
    expect(appliedOps(s)).toHaveLength(2);
    // The invariant replay rests on: the base, plus one tree per applied op.
    expect(snapshotCount(s)).toBe(cursor(s) + 1);
  });

  it('resets the record when a full-tree emission replaces the base', () => {
    let s = seeded();
    s = edit(s, 'h', 'Text', 'One');
    expect(recorded(s)).toBe(1);

    // A new base: the old sequence cannot replay against it, so it is dropped
    // rather than carried across the discontinuity.
    s = ingest(s, baseTree);
    expect(recorded(s)).toBe(0);
    expect(cursor(s)).toBe(0);
  });
});

// ─── undo / redo by replay ───────────────────────────────────────────────────

describe('undo and redo are replay to a position', () => {
  it('N edits then N undos is byte-identical to the state before them', () => {
    let s = seeded();
    s = edit(s, 'h', 'Text', 'First change');

    // The canonical bytes we must land back on, captured before the run.
    const before = canonicalTree(s);
    const cursorBefore = cursor(s);

    s = edit(s, 'h', 'Level', '4');
    s = ingest(s, modelOp('b', 'Label', 'Shipped'));
    s = edit(s, 'h', 'Text', 'Third change');
    expect(canonicalTree(s)).not.toBe(before);

    const after = canonicalTree(s);

    s = undoN(s, 3);
    expect(cursor(s)).toBe(cursorBefore);
    expect(canonicalTree(s)).toBe(before);

    // …and redo puts every one of them back, byte-for-byte.
    s = redoN(s, 3);
    expect(canonicalTree(s)).toBe(after);
  });

  it('holds the undone ops as a redo tail rather than discarding them', () => {
    let s = seeded();
    s = edit(s, 'h', 'Text', 'One');
    s = edit(s, 'h', 'Text', 'Two');

    s = undoN(s, 2);
    expect(cursor(s)).toBe(0);
    expect(recorded(s)).toBe(2);
    expect(canUndo(s)).toBe(false);
    expect(canRedo(s)).toBe(true);
  });

  it('stops at the ends instead of running off them', () => {
    let s = seeded();
    s = edit(s, 'h', 'Text', 'One');

    s = undoN(s, 5);
    expect(cursor(s)).toBe(0);
    expect(canonicalTree(s)).toBe(canonicalTree(seeded()));

    s = redoN(s, 5);
    expect(cursor(s)).toBe(1);
  });

  it('truncates the redo tail when a new edit lands after an undo', () => {
    let s = seeded();
    s = edit(s, 'h', 'Text', 'One');
    s = edit(s, 'h', 'Text', 'Two');
    s = undoN(s, 1);
    expect(recorded(s)).toBe(2);

    // Linear history: the undone branch is abandoned, not forked.
    s = edit(s, 'b', 'Label', 'Elsewhere');
    expect(recorded(s)).toBe(2);
    expect(cursor(s)).toBe(2);
    expect(canRedo(s)).toBe(false);
    expect(logKinds(s)).toHaveLength(2);
  });

  it('truncates the tail for a model emission too, not just a human edit', () => {
    let s = seeded();
    s = edit(s, 'h', 'Text', 'One');
    s = edit(s, 'h', 'Text', 'Two');
    s = undoN(s, 2);

    s = ingest(s, modelOp('b', 'Label', 'Model wins the branch'));
    expect(recorded(s)).toBe(1);
    expect(originKinds(s)).toEqual(['agent']);
  });

  it('re-derives the tree by replay, so the record and the tree cannot drift', () => {
    let s = seeded();
    s = edit(s, 'h', 'Text', 'One');
    s = ingest(s, modelOp('b', 'Label', 'Two'));
    s = edit(s, 'h', 'Level', '3');

    const v = verifyResult(s);
    expect(v.ReplayOk).toBe(true);
    expect(v.ChainOk).toBe(true);
    expect(v.Steps).toBe(3);

    // …and at a rewound position the same two facts hold of the shorter prefix.
    const rewound = verifyResult(undoN(s, 2));
    expect(rewound.ReplayOk).toBe(true);
    expect(rewound.ChainOk).toBe(true);
    expect(rewound.Steps).toBe(1);
  });
});

// ─── the export ──────────────────────────────────────────────────────────────

describe('the exported log replays to reproduce the final tree', () => {
  it('replays cleanly through the public apply engine', () => {
    let s = seeded();
    s = edit(s, 'h', 'Text', 'Annual review');
    s = ingest(s, modelOp('b', 'Label', 'Shipped'));
    s = edit(s, 'h', 'Level', '3');

    const doc = exportJson(s);

    // The document's own base + ops, replayed by the real decode/apply engines,
    // reproduce the document's own final tree — and that tree is the session's.
    expect(replayExport(doc)).toBe(exportedTree(doc));
    expect(replayExport(doc)).toBe(canonicalTree(s));
  });

  it('is a self-describing document carrying base, ops and final tree', () => {
    let s = seeded();
    s = edit(s, 'h', 'Text', 'Annual review');

    const doc = JSON.parse(exportJson(s));
    expect(doc.$log).toBe('fuaran-session-op-log');
    expect(doc.version).toBe(1);
    expect(doc.baseHash).toMatch(/^[0-9a-f]{64}$/);
    expect(doc.base.id).toBe('root');
    expect(doc.tree.id).toBe('root');
    expect(doc.ops).toHaveLength(1);
  });

  it('carries the origin of every exported op', () => {
    let s = seeded();
    s = ingest(s, modelOp('h', 'Text', 'Model wrote this'));
    s = edit(s, 'b', 'Label', 'Human wrote this');

    const doc = JSON.parse(exportJson(s));
    expect(doc.ops.map((o: any) => o.actor.kind)).toEqual(['agent', 'human']);
    expect(doc.ops[1].actor.id).toBe('navigator');
    // Sequence + chain links are exported, so a reader can verify the order.
    expect(doc.ops.map((o: any) => o.seq)).toEqual([1, 2]);
    expect(doc.ops[1].prevHash).toBe(doc.ops[0].hash);
    expect(doc.ops[0].prevHash).toBe(doc.baseHash);
  });

  it('exports what happened, not what was undone', () => {
    let s = seeded();
    s = edit(s, 'h', 'Text', 'One');
    s = edit(s, 'h', 'Text', 'Two');
    s = undoN(s, 1);

    const doc = exportJson(s);
    const parsed = JSON.parse(doc);

    // The redo tail is excluded: the document claims "these ops build this
    // tree", and an undone op in the list would make that claim false.
    expect(parsed.ops).toHaveLength(1);
    expect(replayExport(doc)).toBe(canonicalTree(s));
  });

  it('exports an empty-but-valid document for a session with no ops', () => {
    const s = seeded();
    const doc = exportJson(s);
    expect(JSON.parse(doc).ops).toEqual([]);
    expect(replayExport(doc)).toBe(canonicalTree(s));
  });

  it('goes out through the injected effect seam, not a direct browser call', () => {
    let s = seeded();
    s = edit(s, 'h', 'Text', 'One');

    const calls: Array<[string, string, string]> = [];
    const ports = {
      WriteToClipboard: () => {},
      Download: (filename: string, contents: string, mime: string) => {
        calls.push([filename, contents, mime]);
      },
      Warn: () => {},
      Notify: () => {},
    };

    download(ports, s);
    expect(calls).toHaveLength(1);
    expect(calls[0][0]).toBe(exportFilename);
    expect(calls[0][2]).toBe('application/json');
    expect(replayExport(calls[0][1])).toBe(canonicalTree(s));
  });

  it('names the download generically', () => {
    // The public surface calls this "the session op log" and nothing more.
    expect(exportFilename).toBe('fuaran-session-op-log.json');
  });
});
