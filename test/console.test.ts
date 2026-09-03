// Phase 94 — the Console: query and poke the live tree, in the page.
//
// The pane makes three claims on screen, and this suite is what makes each of
// them checkable rather than asserted:
//
//   1. It answers with the SHIPPED introspection surface. Every call the pane
//      accepts is dispatched onto the object `DebugGlobal.buildGlobalWith`
//      builds, so `getNodeState` here is the renderer's `getNodeState` and not
//      a second implementation written for a panel. The tests below read the
//      typed snapshot back and assert its shape.
//   2. An op issued from the console goes through the policy gate and then the
//      navigator's own edit gate, and the session is returned untouched unless
//      the op actually applied. The log keeps three outcomes apart, because
//      they say different things about the input: APPLIED, REFUSED (the gate or
//      the edit gate said no) and FAILED (the document never decoded).
//      "Nothing happened" and "this was refused" are different facts, and only
//      the second is a demonstration of the gate.
//   3. It is a read-and-poke affordance over the visitor's own ephemeral
//      session — no egress, no persistence, and it does NOT register
//      `window.__fuaran` (that global stays production-gated in the renderer;
//      defeating the gate to feed a panel would be the opposite of the
//      posture). The last describe is that claim, checked against the module's
//      own generated source as well as against the global.
//
// The input is a fixed call grammar rather than JavaScript, so the parser is
// worth pinning directly: anything it cannot parse is an error message, never
// something that runs.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { empty, ingestResult } from '../app/output/Session.js';
// @ts-expect-error untyped Fable output
import { accepted, examples, parseFlat, runLine } from '../app/output/Console.js';
// The session's op list is an F# list across the boundary; the navigator's own
// flat projection is the established way to count it from a test.
// @ts-expect-error untyped Fable output
import { opLog } from '../app/output/navigator/PropertyEditor.js';

// ─── fixtures ────────────────────────────────────────────────────────────────

// A small tree with one addressable Button and one bound Markdown, so the
// introspection answers below have something real to be about. Fed through the
// session's own ingest path, which is the REAL strict decoder — a fixture that
// drifted from the wire format fails here rather than passing as a test about
// something else.
const wire = JSON.stringify({
  id: 'root',
  kind: {
    $type: 'Box',
    children: [
      {
        id: 'submit-btn',
        kind: {
          $type: 'Button',
          label: 'Submit',
          onClick: { $type: 'SetState', key: 'sent', value: 'yes' },
          variant: 'Primary',
        },
      },
      {
        id: 'readout',
        kind: {
          $type: 'Markdown',
          text: {
            $type: 'Bound',
            binding: { $type: 'State', defaultValue: 'nothing yet', key: 'sent' },
          },
        },
      },
    ],
    layout: { $type: 'Auto' },
    role: 'Dashboard',
  },
});

function session() {
  const r = ingestResult(empty, wire);
  if (!r.Ok) {
    throw new Error(`fixture did not decode: ${r.Error}`);
  }
  return r.Next;
}

// `logFlat` projects oldest-first, and the FIRST row of a run is always the call
// itself — any telemetry the apply pipeline emitted follows it. (The pane renders
// the same log newest-first, which is a display order, not this one.)
function callRow(log: string[]): string {
  const row = log[0];
  if (row === undefined) {
    throw new Error('the run logged nothing at all');
  }
  return row;
}

/** The detail half of the call's own log row. */
const detailOf = (log: string[]) => callRow(log).split('|').slice(2).join('|');

/** The level half of the call's own log row. */
const levelOf = (log: string[]) => callRow(log).split('|')[0];

// ─── the parser: a call grammar, not JavaScript ──────────────────────────────

describe('the input is parsed, never evaluated', () => {
  it('accepts each verb of the closed set', () => {
    expect(parseFlat('getNodeState("submit-btn")')).toBe('ok|getNodeState|submit-btn');
    expect(parseFlat('getBindingValue("readout", "Text")')).toBe('ok|getBindingValue|readout|Text');
    expect(parseFlat('getRenderedDom("submit-btn")')).toBe('ok|getRenderedDom|submit-btn');
    expect(parseFlat('inspectTree()')).toBe('ok|inspectTree');
    expect(parseFlat('findNodes("Button")')).toBe('ok|findNodes|Button');
    expect(parseFlat('getAffordances()')).toBe('ok|getAffordances|');
    expect(parseFlat('treeRevision()')).toBe('ok|treeRevision');
    expect(parseFlat('help()')).toBe('ok|help');
  });

  it('tolerates the shapes a visitor actually types', () => {
    // The DevTools spelling, pasted in.
    expect(parseFlat('__fuaran.getNodeState("submit-btn")')).toBe('ok|getNodeState|submit-btn');
    expect(parseFlat('window.__fuaran.inspectTree()')).toBe('ok|inspectTree');
    // Single quotes, no quotes, stray whitespace, a bare nullary name.
    expect(parseFlat("getNodeState('submit-btn')")).toBe('ok|getNodeState|submit-btn');
    expect(parseFlat('  getNodeState( submit-btn ) ')).toBe('ok|getNodeState|submit-btn');
    expect(parseFlat('help')).toBe('ok|help');
  });

  it('keeps an op document whole rather than splitting it into arguments', () => {
    // The naive comma-split this guards against would tear the JSON in half and
    // then fail somewhere far less legible.
    const op = '{"$type":"UpdateProp","path":"Label","target":"submit-btn","value":"Send"}';
    expect(parseFlat(`apply(${op})`)).toBe(`ok|apply|${op}`);
  });

  it('refuses anything else by naming what it accepts', () => {
    const bad = parseFlat('fetch("https://example.com")');
    expect(bad.startsWith('error|')).toBe(true);
    expect(bad).toContain(accepted);

    // Not a call at all, and an unbalanced one.
    expect(parseFlat('while (true) {}').startsWith('error|')).toBe(true);
    expect(parseFlat('inspectTree(').startsWith('error|')).toBe(true);
    // Right verb, wrong arity — reported as arity, not as "unknown".
    expect(parseFlat('getBindingValue("readout")')).toContain('takes');
  });

  it('offers only examples it can itself parse', () => {
    for (const example of examples as string[]) {
      expect(parseFlat(example).startsWith('ok|')).toBe(true);
    }
  });
});

// ─── introspection answers come from the shipped surface ─────────────────────

describe('the console answers about the tree on screen', () => {
  it('returns the typed snapshot for a node', () => {
    const r = runLine(session(), 'getNodeState("submit-btn")');
    const snapshot = JSON.parse(detailOf(r.Log));

    expect(snapshot.id).toBe('submit-btn');
    expect(snapshot.kind).toBe('Button');
    // A typed snapshot, not the DOM: the node's own structural fields.
    expect(Object.keys(snapshot)).toContain('bindings');
    expect(r.Applied).toBe(false);
  });

  it('finds nodes by kind, and reports an unknown id rather than throwing', () => {
    const found = JSON.parse(detailOf(runLine(session(), 'findNodes("Button")').Log));
    expect(found).toEqual(['submit-btn']);

    const missing = runLine(session(), 'getNodeState("no-such-node")');
    expect(detailOf(missing.Log)).toContain('not found');
    expect(missing.Applied).toBe(false);
  });

  it('resolves one binding slot, and help() comes back as prose', () => {
    const resolved = runLine(session(), 'getBindingValue("readout", "Text")');
    expect(levelOf(resolved.Log)).toBe('info');

    const help = runLine(session(), 'help()');
    expect(detailOf(help.Log)).toContain('getNodeState');
    // Prose, not a JSON string literal — the log shows a string as itself.
    expect(detailOf(help.Log).startsWith('"')).toBe(false);
  });

  it('says so when there is no tree yet', () => {
    const r = runLine(empty, 'inspectTree()');
    expect(levelOf(r.Log)).toBe('failed');
    expect(detailOf(r.Log)).toContain('no tree yet');
  });
});

// ─── apply: the gate, then the navigator's edit gate ─────────────────────────

describe('an op issued from the console goes through the gates', () => {
  it('applies a well-formed op and folds it into the session', () => {
    const before = session();
    const r = runLine(
      before,
      'apply({"$type":"UpdateProp","path":"Label","target":"submit-btn","value":"Send"})',
    );

    expect(r.Applied).toBe(true);
    // The session grew by exactly the one op, recorded in its own hash-chained
    // log — the console's apply folds through the session, it does not bypass it.
    expect(opLog(r.Next).length).toBe(opLog(before).length + 1);
    // The apply envelope, then the journal record of the permitted op.
    expect(r.Log.some((row: string) => row.includes('op applied'))).toBe(true);
  });

  it('reports a malformed op as a failure to decode, and changes nothing', () => {
    const before = session();
    const r = runLine(before, 'apply({"$type":"NotAnOp"})');

    expect(r.Applied).toBe(false);
    // A document that never decoded is a FAILURE, not a refusal — the log keeps
    // the two apart because they mean different things about the input.
    expect(levelOf(r.Log)).toBe('failed');
    expect(detailOf(r.Log)).toContain('decodeFailed');
    expect(opLog(r.Next).length).toBe(opLog(before).length);
  });

  it('refuses an op the apply engine rejects, and says why', () => {
    const before = session();
    const r = runLine(
      before,
      'apply({"$type":"UpdateProp","path":"Label","target":"no-such-node","value":"Send"})',
    );

    expect(r.Applied).toBe(false);
    expect(levelOf(r.Log)).toBe('refused');
    // A refusal carries a diagnostic; it is not silently dropped.
    expect(detailOf(r.Log)).toContain('rejected');
    expect(opLog(r.Next).length).toBe(opLog(before).length);
  });
});

// ─── posture: ephemeral, no egress, and the global stays gated ───────────────

describe('the console is a read-and-poke affordance over the visitor own session', () => {
  const source = readFileSync(
    fileURLToPath(new URL('../app/output/Console.js', import.meta.url)),
    'utf8',
  );

  it('is reading the right file — the pane drives the shipped surface', () => {
    // The positive control for the three negative scans below: a scan of the
    // wrong file, or of an empty one, passes them all vacuously. This asserts
    // the source really is the console module AND that the console's answers
    // come from the renderer's own introspection surface rather than a second
    // implementation written for the pane.
    expect(source).toContain('buildGlobalWith');
    expect(source.length).toBeGreaterThan(1000);
  });

  it('does not register window.__fuaran', () => {
    // The renderer publishes that global only under a DEBUG build with an
    // explicit host opt-in. Running the console must not be a back door around
    // that gate — the pane builds the surface object and keeps it.
    runLine(session(), 'inspectTree()');
    expect((globalThis as Record<string, unknown>).__fuaran).toBeUndefined();
    expect(source).not.toContain('globalThis.__fuaran =');
  });

  it('reaches no network and no storage', () => {
    for (const sink of [
      'fetch(',
      'XMLHttpRequest',
      'WebSocket',
      'EventSource',
      'sendBeacon',
      'localStorage',
      'sessionStorage',
      'indexedDB',
      'document.cookie',
    ]) {
      expect(source).not.toContain(sink);
    }
  });

  it('evaluates nothing', () => {
    // The whole reason the input is a parsed call grammar.
    expect(source).not.toContain('eval(');
    expect(source).not.toContain('new Function');
  });
});
