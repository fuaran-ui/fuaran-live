// Phase 329 – the multi-language source-projection output box, exercised
// headlessly over the Fable output. Projection.fs walks the canonical wire tree
// (the vendored FuaranLive.AiWire JsonValue model) and emits builder source per
// language; `projectByName` is the flat surface. The TypeScript leg is a
// verified byte-round-trip (see tests/projection-conformance/ + the fidelity
// doc); the Python / F# / C# / VB legs are illustrative. The projector must
// never crash on any decodable tree (generic fallback for uncovered kinds) and
// produce recognizably per-language output.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { describe, it, expect } from 'vitest';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { projectByName } from '../app/output/Projection.js';

// wire-format-fixtures/nodes/metric-1 – the canonical flat shape with a Literal
// text source, a Static binding, and a nested $type-tagged format object.
const metricNode =
  '{"id":"metric-1","kind":{"$type":"Metric","emphasis":"Normal","format":{"$type":"Currency","code":"GBP"},"icon":"trending-up","label":{"$type":"Literal","text":"Revenue"},"source":{"$type":"Static","value":1234.5}}}';

// A small tree with a nested child, to exercise recursion.
const dashboardTree =
  '{"id":"root","kind":{"$type":"Dashboard","children":[{"id":"h","kind":{"$type":"Heading","level":1,"text":{"$type":"Literal","text":"Hi"}}}]}}';

describe('source projection – per-language builder source', () => {
  it('JSON tab pretty-prints the canonical tree', () => {
    const out = projectByName('json', metricNode);
    expect(out).toContain('"id": "metric-1"');
    expect(out).toContain('"$type": "Metric"');
    expect(out).toContain('\n'); // re-indented, not compact
  });

  it('TypeScript projects fuaran.* ctor, static binding, and literal text', () => {
    const out = projectByName('typescript', metricNode);
    expect(out).toContain('fuaran.metric(');
    expect(out).toContain("'metric-1'");
    expect(out).toContain('binding.static(1234.5)');
    expect(out).toContain("'Revenue'"); // label Literal → bare string
  });

  it('Python projects snake-cased kwargs + builders', () => {
    const out = projectByName('python', metricNode);
    expect(out).toContain('fuaran.metric(');
    expect(out).toContain("label='Revenue'");
    // `value=`, not the wire's `source=`: since fuaran#1142 the Python leg emits
    // per-kind against the real `fuaran_py.ui` constructor signatures rather than
    // re-spelling the wire's own field names through the generic walker.
    expect(out).toContain('value=binding.static(1234.5)');
  });

  it('F# projects smart-ctor + record with PascalCase fields', () => {
    const out = projectByName('fsharp', metricNode);
    expect(out).toContain('Fuaran.metric "metric-1"');
    expect(out).toContain('TextSource.Literal "Revenue"');
    expect(out).toContain('Binding.Static(1234.5)');
    expect(out).toContain('Defaults.metric');
  });

  it('C# projects the static-factory + options-object shape', () => {
    const out = projectByName('csharp', metricNode);
    expect(out).toContain('Metric(new() {'); // static factory + options object
    expect(out).toContain('Id = "metric-1"'); // id folded into the initializer
    expect(out).toContain('Label = "Revenue"'); // Literal → bare string, PascalCase member
    expect(out).toContain('Source = Binding.Static(1234.5)');
  });

  it('VB projects the XML-literal shape (attributes, format-*, $-bound elided)', () => {
    const out = projectByName('vb', metricNode);
    expect(out).toContain('<Metric id="metric-1"'); // element = kind, id attribute
    expect(out).toContain('label="Revenue"'); // Literal → attribute value
    expect(out).toContain('format-currency="GBP"'); // CellFormat → format-* attribute
    expect(out).toContain('source="1234.5"'); // Static binding → flattened scalar attr
  });

  it('recurses into children (Dashboard → Heading)', () => {
    const ts = projectByName('typescript', dashboardTree);
    expect(ts).toContain('fuaran.dashboard(');
    expect(ts).toContain('fuaran.heading(');
    expect(ts).toContain('children:');

    // VB nests the child as a real XML child element under the parent.
    const vb = projectByName('vb', dashboardTree);
    expect(vb).toContain('<Dashboard id="root">');
    expect(vb).toContain('<Heading id="h"');
    expect(vb).toContain('</Dashboard>');
  });

  it('never crashes on an uncovered kind – generic fallback', () => {
    const weird = '{"id":"x","kind":{"$type":"ZibbleWidget","foo":[1,2,{"bar":true}]}}';
    for (const lang of ['json', 'typescript', 'python', 'fsharp', 'csharp', 'vb']) {
      const out = projectByName(lang, weird);
      expect(typeof out).toBe('string');
      expect(out.length).toBeGreaterThan(0);
    }
    // The unknown kind still names a constructor (structural sketch).
    expect(projectByName('typescript', weird)).toContain('zibbleWidget');
    // VB names the unknown element too (structural sketch, never a throw).
    expect(projectByName('vb', weird)).toContain('<ZibbleWidget');
  });

  it('an empty / non-JSON input never throws', () => {
    expect(typeof projectByName('typescript', '')).toBe('string');
    expect(typeof projectByName('json', 'not json')).toBe('string');
  });
});
