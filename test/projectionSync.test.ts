// Phase 714 – the Navigator ⇄ source-projection sync, exercised headlessly over
// the Fable output. The feature is "walk the tree, watch the same construct
// light up in three languages at once"; everything that makes that true is pure
// and lives in app/Projection.fs, so it is testable here without a DOM:
//
//   • the id → span side map each language's projection now carries,
//   • nearest-enclosing resolution for a node a language does not project,
//   • and the two invariants the design rests on — the markers the generators
//     emit to build the map never reach a caller, and a tree whose own content
//     carries a marker character is refused a map rather than given a wrong one.
//
// The edit-sync leg drives a REAL applied op (an `UpdateProp` through the
// session's decode → apply → fold path, the same engine the property panel and
// a model emission both go through), not a hand-swapped tree: the point of the
// acceptance criterion is that an *op* moves the highlight, and a test that
// replaced the tree wholesale would not have shown that.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { describe, it, expect } from 'vitest';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// A NAMESPACE import, one line each: the missing-declaration error lands on the
// module specifier, so `@ts-expect-error` has to sit directly above the line
// carrying it. In a wrapped multi-name import the specifier sits on the closing
// `} from '...'` line, so the directive goes on the last line INSIDE the braces
// — one per IMPORT, never one per name (a per-name directive suppresses nothing
// and is itself reported unused, TS2578).
// @ts-expect-error untyped Fable output
import * as P from '../app/output/Projection.js';
// @ts-expect-error untyped Fable output
import * as Sess from '../app/output/Session.js';

// Every language the Output box projects. The sync pane defaults to three of
// them, but the side map is not allowed to be a three-language feature — a tab
// switched on has to highlight too.
const LANGS = [
  'json',
  'typescript',
  'python',
  'fsharp',
  'csharp',
  'vb',
  'go',
  'kotlin',
  'rust',
  'swift',
];

// nav-root ▸ nav-card ▸ (nav-a, nav-b) – the Phase 710 walk fixture, with
// distinguishable label text so a span can be checked for the RIGHT node.
const baseTree =
  '{"id":"nav-root","kind":{"$type":"Box","children":[' +
  '{"id":"nav-card","kind":{"$type":"Box","children":[' +
  '{"id":"nav-a","kind":{"$type":"Markdown","text":"Alpha"}},' +
  '{"id":"nav-b","kind":{"$type":"Markdown","text":"Bravo"}}],' +
  '"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Card"}}],' +
  '"layout":{"$type":"Auto"},"role":"Dashboard"}}';

// A node held in a `state` slot rather than as a structural child. The Navigator
// walks it (its cursor rides `descendantNodes`, not just children), but the
// illustrative per-language walkers project only the node's `kind` fields — so
// `spinner` has a construct in TypeScript and JSON and none in F# or Python.
// That asymmetry is the whole reason nearest-enclosing resolution exists.
const stateSlotTree =
  '{"id":"root","kind":{"$type":"Markdown","text":"Body"},' +
  '"state":{"onLoading":{"id":"spinner","kind":{"$type":"Markdown","text":"Loading"}}}}';

const fence = (json: string) => '```json\n' + json + '\n```';

/** Ingest a wire document, asserting it decoded, and hand back the session. */
const sessionOf = (json: string) => {
  const r = Sess.ingestResult(Sess.empty, fence(json));
  expect(r.Ok).toBe(true);
  return r.Next;
};

describe('the projection side map', () => {
  it('leaves the projected text byte-identical – the span sentinels never escape', () => {
    for (const lang of LANGS) {
      const out = P.projectByName(lang, baseTree);
      // U+0001..U+0003 are the marker characters. The plain path never marks
      // and the mapped path strips, so neither may leak one — and a leak would
      // corrupt every projection invisibly, hence the explicit assertion.
      expect(out, lang).not.toMatch(/[\u0001\u0002\u0003]/);
      expect(out.length, lang).toBeGreaterThan(0);
    }
  });

  it('maps every node of the tree, in every language', () => {
    for (const lang of LANGS) {
      expect(Array.from(P.spanIdsByName(lang, baseTree)), lang).toEqual([
        'nav-root',
        'nav-card',
        'nav-a',
        'nav-b',
      ]);
    }
  });

  it('a span covers its own node and nothing of its sibling', () => {
    for (const lang of LANGS) {
      const a = P.spanTextByName(lang, baseTree, 'nav-a');
      expect(a, lang).toContain('nav-a');
      expect(a, lang).toContain('Alpha');
      expect(a, lang).not.toContain('nav-b');
      expect(a, lang).not.toContain('Bravo');
    }
  });

  it("a parent's span encloses its children's", () => {
    for (const lang of LANGS) {
      const card = P.spanTextByName(lang, baseTree, 'nav-card');
      expect(card, lang).toContain('nav-a');
      expect(card, lang).toContain('nav-b');
      // …and is a genuine substring of the text, at the offsets reported.
      expect(P.projectByName(lang, baseTree), lang).toContain(card);
    }
  });

  it('refuses to map a tree whose own content carries a marker character', () => {
    // Not hypothetical: the corpus pins control-character escaping, and node
    // fixture `btn-json-payloads` carries a literal U+0001 inside a string
    // payload. So the marker scheme may not assume content is marker-free, and
    // the guard's answer is an empty map over byte-exact text — no highlight,
    // never a wrong one over mangled source. (Built by char code rather than
    // written as an escape, so the character in this fixture is unambiguous.)
    const marker = String.fromCharCode(1);
    // Via JSON.stringify: JSON forbids an unescaped control character, so the
    // document carries `\u0001` and the parser hands the projector the raw one.
    const hostile = JSON.stringify({
      id: 'root',
      kind: { $type: 'Markdown', text: 'before' + marker + 'after' },
    });

    for (const lang of LANGS) {
      if (lang === 'json') continue;
      // The content survives the projection untouched…
      expect(P.projectByName(lang, hostile), lang).toContain(marker);
      // …and no span is claimed over it.
      expect(Array.from(P.spanIdsByName(lang, hostile)), lang).toEqual([]);
    }

    // JSON is unaffected: its spans come from a brace scan over the rendered
    // text, which is indifferent to what the strings inside it contain (and the
    // host encoder escapes the control character there anyway).
    expect(Array.from(P.spanIdsByName('json', hostile))).toEqual(['root']);
  });

  it('an id the tree does not carry maps to nothing', () => {
    expect(P.spanTextByName('fsharp', baseTree, 'no-such-node')).toBe('');
    expect(P.spanPathTextByName('fsharp', baseTree, ['no-such-node'])).toBe('');
    expect(Array.from(P.spanPathLinesByName('fsharp', baseTree, ['no-such-node']))).toEqual([]);
  });

  it('reports a 1-based line range inside the projection', () => {
    const text: string = P.projectByName('python', baseTree);
    const range = Array.from(
      P.spanPathLinesByName('python', baseTree, ['nav-root', 'nav-card', 'nav-b']),
    ) as number[];

    expect(range).toHaveLength(2);
    const first = range[0]!;
    const last = range[1]!;

    expect(first).toBeGreaterThan(0);
    expect(last).toBeGreaterThanOrEqual(first);
    expect(last).toBeLessThanOrEqual(text.split('\n').length);
    // The reported range really is where the construct lives. (Not "the first
    // line contains the id" – Python opens `fuaran.markdown(` on one line and
    // carries the id on the next, which is exactly why the pane reports a RANGE
    // rather than a line.)
    const block = text
      .split('\n')
      .slice(first - 1, last)
      .join('\n');
    expect(block).toContain('nav-b');
    expect(block).toContain('Bravo');
  });
});

describe('nearest-enclosing resolution', () => {
  it('resolves to the focused node where the language projects it', () => {
    // `python` joined this group at fuaran#1142: its per-kind emitter projects a
    // state-slot placeholder as a nested constructor call with a span of its own,
    // exactly as the TypeScript leg does, where the generic walker folded it into
    // the parent construct.
    for (const lang of ['json', 'typescript', 'python']) {
      expect(P.spanPathIdByName(lang, stateSlotTree, ['root', 'spinner']), lang).toBe('spinner');
      expect(P.spanPathTextByName(lang, stateSlotTree, ['root', 'spinner']), lang).toContain(
        'Loading',
      );
    }
  });

  it('falls back to the closest projected ancestor where it does not', () => {
    for (const lang of ['fsharp']) {
      // The state slot is folded into the parent construct, so `spinner` has no
      // span of its own here…
      expect(Array.from(P.spanIdsByName(lang, stateSlotTree)), lang).toEqual(['root']);
      expect(P.spanTextByName(lang, stateSlotTree, 'spinner'), lang).toBe('');
      // …and the cursor path resolves to the construct that contains it rather
      // than to nothing at all.
      expect(P.spanPathIdByName(lang, stateSlotTree, ['root', 'spinner']), lang).toBe('root');
      expect(P.spanPathTextByName(lang, stateSlotTree, ['root', 'spinner']), lang).toContain(
        'root',
      );
    }
  });
});

describe('edit sync – an applied op moves the highlight with the node', () => {
  // The op a property-panel label edit emits, and the shape a model emits
  // mid-conversation: one `UpdateProp` against a node's own field. (`path`
  // names the field, matching wire-format-fixtures/ops/op-updateprop.json.)
  const relabel = (id: string, text: string) =>
    '{"$type":"UpdateProp","path":"Text","target":"' +
    id +
    '","value":' +
    JSON.stringify(text) +
    '}';

  it('re-derives the projections and lands the span on the new text', () => {
    const before = sessionOf(baseTree);
    expect(P.spanTextByName('fsharp', Sess.treeJson(before), 'nav-a')).toContain('Alpha');

    const applied = Sess.ingestResult(before, fence(relabel('nav-a', 'Renamed')));
    expect(applied.Ok).toBe(true);
    // A real op through the real engine, not a swapped tree.
    expect(applied.Mode).toBe('op');

    const after = Sess.treeJson(applied.Next);

    for (const lang of LANGS) {
      const span = P.spanTextByName(lang, after, 'nav-a');
      // Same id, same cursor – the construct now reads the new label.
      expect(span, lang).toContain('nav-a');
      expect(span, lang).toContain('Renamed');
      expect(span, lang).not.toContain('Alpha');
      // The untouched sibling is untouched.
      expect(P.spanTextByName(lang, after, 'nav-b'), lang).toContain('Bravo');
    }
  });

  it('keeps every other node mapped after the edit', () => {
    const applied = Sess.ingestResult(sessionOf(baseTree), fence(relabel('nav-b', 'Charlie')));
    expect(applied.Ok).toBe(true);

    for (const lang of LANGS) {
      expect(Array.from(P.spanIdsByName(lang, Sess.treeJson(applied.Next))), lang).toEqual([
        'nav-root',
        'nav-card',
        'nav-a',
        'nav-b',
      ]);
    }
  });

  it('survives a node vanishing – the cursor path resolves to the surviving ancestor', () => {
    const removal = '{"$type":"RemoveNode","target":"nav-a"}';
    const applied = Sess.ingestResult(sessionOf(baseTree), fence(removal));
    expect(applied.Ok).toBe(true);

    const after = Sess.treeJson(applied.Next);

    for (const lang of LANGS) {
      // nav-a is gone from the projection entirely…
      expect(P.spanTextByName(lang, after, 'nav-a'), lang).toBe('');
      // …and a cursor still carrying it lands on its parent's construct, which
      // is the same answer the Navigator's own re-resolution gives.
      expect(P.spanPathIdByName(lang, after, ['nav-root', 'nav-card', 'nav-a']), lang).toBe(
        'nav-card',
      );
    }
  });
});
