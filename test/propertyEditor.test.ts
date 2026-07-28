// Phase 711 – the Navigator's schema-derived property editor, exercised
// headlessly over the Fable output. The module lives in
// app/navigator/PropertyEditor.fs and compiles to
// app/output/navigator/PropertyEditor.js; this drives it in node via vitest,
// following the Phase 710 pattern (flat string/array projections across the
// Fable boundary, every fixture fed through the REAL strict decoder via the
// session's own ingest path, so nothing here is a hand-waved shape).
//
// The suite's claim is the one the phase rests on: the panel is DERIVED, not
// written. Two independent guards say so — a source lock asserting the module
// names no kind-specific field, and a behavioural sweep asserting kinds the
// module has never heard of still get editable fields. Everything else checks
// that a committed field becomes the right op, that the op goes through the
// public apply engine, and that a refused edit changes nothing at all.
//
// Requires `pnpm run fable:app` (or `dotnet fable app --outDir app/output`).

import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { empty, ingestResult, treeJson } from '../app/output/Session.js';
import {
  // @ts-expect-error untyped Fable output
  fieldSummary,
  // @ts-expect-error untyped Fable output
  editablePaths,
  // @ts-expect-error untyped Fable output
  readOnlyReason,
  // @ts-expect-error untyped Fable output
  choiceOptions,
  // @ts-expect-error untyped Fable output
  commitAt,
  // @ts-expect-error untyped Fable output
  opLog,
} from '../app/output/navigator/PropertyEditor.js';

// ─── fixtures ────────────────────────────────────────────────────────────────

/** Wrap one or more kind bodies as children of a Box root. */
const treeOf = (...children: string[]) =>
  '{"id":"root","kind":{"$type":"Box","children":[' +
  children.join(',') +
  '],"layout":{"$type":"Auto"},"role":"Dashboard"}}';

const heading =
  '{"id":"h","kind":{"$type":"Heading","level":2,"text":"Quarterly review","variant":"Standard"}}';
const metric =
  '{"id":"m","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":42}}}';
const badge = '{"id":"b","kind":{"$type":"Badge","label":"New","variant":"Brand"}}';

/** Ingest a tree and hand back the session (throwing loudly on a decode fail). */
function session(wire: string) {
  const r = ingestResult(empty, wire);
  if (!r.Ok) {
    throw new Error(`fixture did not decode: ${r.Error}\n${wire}`);
  }
  return r.Next;
}

/** The summary row for one field of one node, or undefined. */
function rowOf(s: any, nodeId: string, path: string): string | undefined {
  const node = findNodeJson(s, nodeId);
  return (fieldSummary(node) as string[]).find((r) => r.split('|')[1] === path);
}

// The flat surfaces are addressed by node id, so tests never need to hold a
// decoded `Node` — except `fieldSummary` / `editablePaths`, which take one.
// Reach it the same way the navigator does: through the introspection surface.
// @ts-expect-error untyped Fable output
import { findNode } from '../app/output/fuaran-dotnet/src/Fuaran.UI.Ops/Introspect.js';
// @ts-expect-error untyped Fable output
import { NodeId } from '../app/output/fuaran-dotnet/src/Fuaran.UI/Types.js';

function findNodeJson(s: any, nodeId: string) {
  const found = findNode(new NodeId(nodeId), s.Tree);
  if (found == null) {
    throw new Error(`no node '${nodeId}' in the fixture tree`);
  }
  return found;
}

// ─── the derivation ──────────────────────────────────────────────────────────

describe('field derivation is schema-driven', () => {
  it('gives a Heading its schema shapes: integer level, text, enum variant', () => {
    const s = session(treeOf(heading));
    const node = findNodeJson(s, 'h');
    const rows = fieldSummary(node) as string[];

    expect(rows).toContain('Properties|Level|integer|2');
    expect(rows).toContain('Properties|Text|text|Quarterly review');
    expect(rows).toContain('Properties|Variant|choice|Standard');
  });

  it("offers the schema's own enum cases as the choice options", () => {
    const s = session(treeOf(heading));
    const node = findNodeJson(s, 'h');

    // Exactly the HeadingVariant vocabulary, in schema order — not a list
    // written here or in the module.
    expect(Array.from(choiceOptions(node, 'Variant'))).toEqual([
      'Standard',
      'Eyebrow',
      'Caption',
      'Lead',
    ]);
  });

  it('derives the semantic-style block for every kind, from the SemanticStyle schema', () => {
    const s = session(treeOf(heading, metric, badge));

    for (const id of ['h', 'm', 'b']) {
      const rows = fieldSummary(findNodeJson(s, id)) as string[];
      const style = rows.filter((r) => r.startsWith('Style|')).map((r) => r.split('|')[1]);
      expect(style.sort()).toEqual(['emphasis', 'role', 'tone', 'voice', 'weight']);
      // Every style token is a bounded enum, so every one is a select.
      expect(
        rows.filter((r) => r.startsWith('Style|')).every((r) => r.split('|')[2] === 'choice'),
      ).toBe(true);
    }
  });

  it('keeps a bound value read-only rather than letting text clobber the binding', () => {
    const s = session(treeOf(metric));
    const node = findNodeJson(s, 'm');

    // Metric.Value is a Binding — an object on the wire, so the union rule
    // refuses to treat it as text.
    expect(rowOf(s, 'm', 'Value')!.split('|')[2]).toBe('readonly');
    expect(readOnlyReason(node, 'Value')).toMatch(/bound/i);
    // Metric.Label is a TextSource currently holding a bare literal, so the
    // SAME union rule makes it editable. One rule, both outcomes.
    expect(rowOf(s, 'm', 'Label')!.split('|')[2]).toBe('text');
  });

  it('names the structural ops for Children instead of pretending it is a field', () => {
    const s = session(treeOf(heading));
    const node = findNodeJson(s, 'root');

    expect(rowOf(s, 'root', 'Children')!.split('|')[2]).toBe('readonly');
    expect(readOnlyReason(node, 'Children')).toMatch(/InsertChild/);
  });
});

// ─── the "demonstrably derived" guards ───────────────────────────────────────

describe('the panel is derived, not hand-written per kind', () => {
  it('names no kind-specific spec field anywhere in the module', () => {
    const source = readFileSync(
      fileURLToPath(new URL('../app/navigator/PropertyEditor.fs', import.meta.url)),
      'utf8',
    );

    // Every one of these IS a derived, editable field on some kind. If the
    // panel were hand-written, at least one would have to appear here by name.
    // (`Children` is deliberately excluded: it is a genuine generic case — the
    // signpost to the structural ops — and the module says so out loud.)
    const kindSpecificFields = [
      'Copyable',
      'LineNumbers',
      'HighlightLines',
      'MaxHeight',
      'MaxWidth',
      'Dismissable',
      'Ordered',
      'Href',
      'Rel',
      'Zoom',
      'Stacked',
      'RowKeyField',
      'Subtext',
      'TrendFormat',
      'Indeterminate',
      'Caveat',
      'Accept',
      'SubmitLabel',
      'ActiveStep',
      'TemplateColumns',
    ];

    for (const field of kindSpecificFields) {
      expect(
        source,
        `PropertyEditor.fs must not name the kind-specific field '${field}'`,
      ).not.toContain(field);
    }
  });

  it('gives editable fields to kinds the module has never heard of', () => {
    // Every body below is a kind that appears nowhere in PropertyEditor.fs.
    const bodies: [string, string][] = [
      [
        'code',
        '{"id":"code","kind":{"$type":"CodeBlock","code":"let x = 1","copyable":true,"highlightLines":[],"language":"fsharp","lineNumbers":true}}',
      ],
      ['callout', '{"id":"callout","kind":{"$type":"Callout","body":"Mind the gap"}}'],
      ['skeleton', '{"id":"skeleton","kind":{"$type":"Skeleton","rows":3}}'],
      [
        'toast',
        '{"id":"toast","kind":{"$type":"Toast","message":"Saved","open":{"$type":"Static","value":true}}}',
      ],
      ['list', '{"id":"list","kind":{"$type":"List","items":["one","two"],"ordered":false}}'],
      ['markdown', '{"id":"markdown","kind":{"$type":"Markdown","text":"hello"}}'],
      ['math', '{"id":"math","kind":{"$type":"Math","display":"Block","source":"x^2"}}'],
      ['b', badge],
    ];

    for (const [id, body] of bodies) {
      const s = session(treeOf(body));
      const paths = Array.from(editablePaths(findNodeJson(s, id))) as string[];
      // At minimum the five style tokens; every one of these kinds also has
      // spec fields of its own.
      expect(paths.length, `${id} should derive editable fields`).toBeGreaterThan(5);
    }
  });
});

// ─── edit → op → apply → re-render ───────────────────────────────────────────

describe('a committed field becomes an op through the public apply engine', () => {
  it('round-trips a label and a size on three different kinds', () => {
    let s = session(treeOf(heading, metric, badge));
    const opsBefore = (opLog(s) as string[]).length;

    // 1. Heading — a label (TextSource) and a size (the integer level).
    let r = commitAt(s, 'h', 'Text', 'Annual review');
    expect(r.Ok, r.Error).toBe(true);
    s = r.Next;
    r = commitAt(s, 'h', 'Level', '4');
    expect(r.Ok, r.Error).toBe(true);
    s = r.Next;

    // 2. Metric — a label, and a size expressed as the semantic weight token.
    r = commitAt(s, 'm', 'Label', 'Gross revenue');
    expect(r.Ok, r.Error).toBe(true);
    s = r.Next;
    r = commitAt(s, 'm', 'weight', 'Spacious');
    expect(r.Ok, r.Error).toBe(true);
    s = r.Next;

    // 3. Badge — a label and its bounded variant.
    r = commitAt(s, 'b', 'Label', 'Updated');
    expect(r.Ok, r.Error).toBe(true);
    s = r.Next;
    r = commitAt(s, 'b', 'Variant', 'Success');
    expect(r.Ok, r.Error).toBe(true);
    s = r.Next;

    // Every commit is one op, recorded on the session's op stream.
    expect((opLog(s) as string[]).length - opsBefore).toBe(6);

    // The tree really changed, and every edited node kept its id — the cursor
    // stays on the node it was editing.
    const json = treeJson(s);
    expect(json).toContain('Annual review');
    expect(json).toContain('Gross revenue');
    expect(json).toContain('Updated');
    expect(json).toContain('"level": 4');
    expect(json).toContain('Success');
    expect(json).toContain('Spacious');
    for (const id of ['h', 'm', 'b']) {
      expect(json).toContain(`"${id}"`);
    }
  });

  it('records the right op discriminator per route', () => {
    let s = session(treeOf(heading));

    const lastOp = (x: any) => {
      const ops = opLog(x) as string[];
      return ops[ops.length - 1];
    };

    s = commitAt(s, 'h', 'Level', '3').Next;
    expect(lastOp(s)).toContain('"$type":"UpdateProp"');

    s = commitAt(s, 'h', 'tone', 'Brand').Next;
    expect(lastOp(s)).toContain('"$type":"UpdateStyle"');
  });

  it('edits a contained-data position by index within the focused node', () => {
    const grid =
      '{"id":"g","kind":{"$type":"DataGrid","columns":[' +
      '{"kind":{"$type":"Text"},"label":"Channel","value":"<closure>"},' +
      '{"kind":{"$type":"Text"},"label":"Spend","value":"<closure>"}],' +
      '"rowKey":"<closure>","source":{"$type":"Static","value":"<opaque>"}}}';

    const s = session(treeOf(grid));

    // The `Columns[i].Label` pattern is expanded against the REAL column count
    // — two columns, two rows, positional per the 0.2.0 payload-collection rule.
    const paths = Array.from(editablePaths(findNodeJson(s, 'g'))) as string[];
    expect(paths).toContain('Columns[0].Label');
    expect(paths).toContain('Columns[1].Label');
    expect(paths).not.toContain('Columns[2].Label');

    const r = commitAt(s, 'g', 'Columns[1].Label', 'Total spend');
    expect(r.Ok, r.Error).toBe(true);
    expect(treeJson(r.Next)).toContain('Total spend');
    // Untouched sibling column.
    expect(treeJson(r.Next)).toContain('Channel');
  });
});

// ─── the gate ────────────────────────────────────────────────────────────────

describe('a refused edit applies nothing', () => {
  it('refuses a value the pre-emit validator rejects, and leaves the tree byte-identical', () => {
    // A two-series chart. Switching its bounded `Kind` to Pie is a perfectly
    // legal enum value and applies cleanly — and produces a chart the lowering
    // cannot honour (FUARAN088: pie needs exactly one series). The pre-emit
    // validator is the only thing standing between the user and that tree.
    const chart =
      '{"id":"c","kind":{"$type":"Chart","kind":"Line","source":{"$type":"Static","value":"<opaque>"},' +
      '"title":"Channel mix","xField":"month","yFields":["revenue","cost"]}}';

    const s = session(treeOf(chart));
    const before = treeJson(s);

    const r = commitAt(s, 'c', 'Kind', 'Pie');
    expect(r.Ok).toBe(false);
    expect(r.Error).toContain('FUARAN088');
    // Not "restored" — never applied. Same session object's tree, byte-identical.
    expect(treeJson(r.Next)).toBe(before);
    expect((opLog(r.Next) as string[]).length).toBe((opLog(s) as string[]).length);
  });

  it('refuses a value outside the field’s schema type', () => {
    const s = session(treeOf(heading));

    const bad = commitAt(s, 'h', 'Level', 'enormous');
    expect(bad.Ok).toBe(false);
    expect(bad.Error).toMatch(/not a number/);
    expect(treeJson(bad.Next)).toBe(treeJson(s));

    const fractional = commitAt(s, 'h', 'Level', '2.5');
    expect(fractional.Ok).toBe(false);
    expect(fractional.Error).toMatch(/whole number/);
  });

  it('refuses an enum case the schema does not list', () => {
    const s = session(treeOf(heading));

    const r = commitAt(s, 'h', 'Variant', 'Enormous');
    expect(r.Ok).toBe(false);
    expect(r.Error).toContain('Standard');
    expect(treeJson(r.Next)).toBe(treeJson(s));
  });

  it('refuses a commit against a read-only field', () => {
    const s = session(treeOf(metric));

    const r = commitAt(s, 'm', 'Value', '99');
    expect(r.Ok).toBe(false);
    expect(r.Error).toMatch(/read-only/);
    expect(treeJson(r.Next)).toBe(treeJson(s));
  });

  it('refuses an unknown node or an unknown field without throwing', () => {
    const s = session(treeOf(heading));

    expect(commitAt(s, 'nope', 'Level', '3').Ok).toBe(false);
    expect(commitAt(s, 'h', 'NoSuchField', 'x').Ok).toBe(false);
    expect(commitAt(empty, 'h', 'Level', '3').Ok).toBe(false);
  });
});
