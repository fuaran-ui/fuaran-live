// Phase 1628 — the render-latency harness's pure half, checked without a browser.
//
// `Capture.fs` needs a DOM and is exercised by the Playwright spec in
// tests/measure/. Everything else — the corpus, the aggregation, the metric
// ids, the browser-identity reader and the artefact emitter — is deliberately
// free of the DOM so that most of the harness is covered by the ordinary unit
// suite, which runs on every push and needs no browser at all.
//
// Requires `pnpm run fable:app` (or `dotnet fable app --outDir app/output`).

import { describe, it, expect } from 'vitest';

// Fable-generated JS — no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { corpus, SpineDepth } from '../app/output/measure/Corpus.js';
// @ts-expect-error untyped Fable output
import { aggregate, browserId, ttfpMetric, fullMetric } from '../app/output/measure/Timing.js';
// @ts-expect-error untyped Fable output
import { catalogue, captured, pendingTemplate } from '../app/output/measure/Baseline.js';

interface CorpusTree {
  Label: string;
  NodeCount: number;
  Depth: number;
  Wire: string;
}

interface Metric {
  id: string;
  value: number | null;
  unit: string;
  note: string;
}

interface Artifact {
  schema_version: number;
  artifact: string;
  status: string;
  runtime: { dotnet: string; os: string; cpu: string };
  metrics: Metric[];
}

// `corpus` and `catalogue()` cross the Fable boundary as F# lists, which are
// iterable but are not arrays.
const trees = Array.from(corpus as Iterable<CorpusTree>);

// The F# list the aggregator takes crosses the Fable boundary as an iterable;
// a plain array satisfies it, which is how the other suites here feed one.
const agg = aggregate as (
  metric: string,
  values: number[],
) => {
  metric: string;
  Samples: number;
  Min: number;
  Mean: number;
  P50: number;
  P95: number;
  Max: number;
};

describe('the measurement corpus', () => {
  it('carries the four shapes the metric ids are keyed on', () => {
    expect(trees.map((t) => t.Label)).toEqual(['Small', 'Medium', 'Large', 'DeepNested']);
  });

  it('spans a wide node-count range, which is what makes a per-node regression visible', () => {
    const wide = trees.filter((t) => t.Depth === 2).map((t) => t.NodeCount);
    expect(wide).toEqual([5, 25, 97]);
  });

  // The bound the decoder enforces is 24; a corpus that crossed it would not
  // decode at all, and the capture would fail rather than measure. Phase 202's
  // corpus nested 32 deep, which is exactly the mistake this pins against.
  it('keeps the DeepNested spine inside the decoder MaxDepth = 24 bound', () => {
    const deep = trees.find((t) => t.Label === 'DeepNested');
    expect(deep?.Depth).toBe((SpineDepth as number) + 1);
    expect(deep?.Depth).toBeLessThan(24);
  });

  it('emits parseable canonical wire JSON for every tree', () => {
    for (const t of trees) {
      const doc = JSON.parse(t.Wire) as { id: string; kind: { $type: string } };
      // Every node id is prefixed with the corpus label, so a tree that turns
      // up in a decode failure or a DOM dump says which shape it came from.
      // The spine's root is `DeepNested-d0` rather than `DeepNested`, because
      // its levels are numbered from the outside in.
      expect(doc.id.startsWith(t.Label)).toBe(true);
      expect(doc.kind.$type).toBe('Box');
    }
  });
});

describe('aggregation', () => {
  it('summarises a sample set', () => {
    const s = agg('m', [3, 1, 2, 4]);
    expect(s.Samples).toBe(4);
    expect(s.Min).toBe(1);
    expect(s.Max).toBe(4);
    expect(s.Mean).toBe(2.5);
  });

  // An un-taken mark is not a zero. Folding a NaN in would poison the mean of
  // an otherwise good run and make a broken capture look merely slow.
  it('drops NaN and infinity rather than folding them in', () => {
    const s = agg('m', [1, Number.NaN, 2, Number.POSITIVE_INFINITY, 3]);
    expect(s.Samples).toBe(3);
    expect(s.Mean).toBe(2);
  });

  it('reports zero samples rather than a zero value when nothing is usable', () => {
    const s = agg('m', [Number.NaN]);
    expect(s.Samples).toBe(0);
    expect(Number.isNaN(s.Min)).toBe(true);
  });
});

describe('metric ids', () => {
  // These strings are the keys the consuming gate matches its budget rules
  // against. A change here is a change to what the gate recognises.
  it('are the prefixes the gate keys on', () => {
    expect(ttfpMetric('Large')).toBe('render.ttfp.Large.ms');
    expect(fullMetric('DeepNested')).toBe('render.full.DeepNested.ms');
  });

  it('are declared once per corpus label, in both phases', () => {
    const ids = Array.from(catalogue() as Iterable<[string, string, string]>).map((c) => c[0]);
    expect(ids.length).toBe(trees.length * 2);
    for (const t of trees) {
      expect(ids).toContain(`render.ttfp.${t.Label}.ms`);
      expect(ids).toContain(`render.full.${t.Label}.ms`);
    }
  });
});

describe('browser identity', () => {
  // Order matters: every Chromium-family agent carries `Chrome/`, so the more
  // specific brands must be tested first or Edge reads as Chromium.
  it('reads the engine and version from real agent strings', () => {
    expect(
      browserId(
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/151.0.7922.34 Safari/537.36',
      ),
    ).toBe('HeadlessChrome 151.0.7922.34');
    expect(
      browserId(
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.2903.86',
      ),
    ).toBe('Edge 131.0.2903.86');
    expect(browserId('Mozilla/5.0 (Windows NT 10.0; rv:133.0) Gecko/20100101 Firefox/133.0')).toBe(
      'Firefox 133.0',
    );
    expect(
      browserId(
        'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.2 Safari/605.1.15',
      ),
    ).toBe('Safari 18.2');
  });

  // An unreadable agent must read as unknown, never as a match: the gate treats
  // "the artefact does not name its host" and "the hosts agree" very
  // differently, and only one of them licenses a wall-time comparison.
  it('reads an unrecognised agent as unknown rather than as anything', () => {
    expect(browserId('some-crawler/1.0')).toBe('unknown');
    expect(browserId('')).toBe('unknown');
  });
});

describe('the baseline artefact', () => {
  const stats = trees.flatMap((t) => [
    agg(ttfpMetric(t.Label), [0.5, 0.4, 0.6]),
    agg(fullMetric(t.Label), [1.5, 1.4, 1.6]),
  ]);

  it('emits the shared perf-baseline shape, stamped with the host', () => {
    const doc = JSON.parse(
      captured(stats, '2026-09-08T00:00:00.000Z', {
        Dotnet: 'n/a (browser host)',
        Os: 'Windows_NT 10',
        Cpu: 'some-cpu · HeadlessChrome 151.0',
      }) as string,
    ) as Artifact;

    expect(doc.schema_version).toBe(1);
    expect(doc.artifact).toBe('render-latency');
    expect(doc.status).toBe('captured');
    expect(doc.runtime.cpu).toContain('HeadlessChrome');
    expect(doc.metrics.length).toBe(trees.length * 2);
    for (const m of doc.metrics) {
      expect(m.unit).toBe('ms');
      expect(m.value).not.toBeNull();
      expect(m.note).not.toBe('');
    }
  });

  // The minimum, not the median — timing noise in a browser is one-sided.
  it('records the fastest batch', () => {
    const doc = JSON.parse(
      captured([agg('render.ttfp.Small.ms', [0.5, 0.4, 0.9])], '2026-09-08T00:00:00.000Z', {
        Dotnet: '',
        Os: '',
        Cpu: 'x',
      }) as string,
    ) as Artifact;
    expect(doc.metrics[0]?.value).toBe(0.4);
  });

  // A capture that produced no usable sample must not present as a captured
  // zero: a null value is what makes the whole artefact read as not-captured.
  it('emits null for a metric with no usable samples', () => {
    const doc = JSON.parse(
      captured([agg('render.ttfp.Small.ms', [Number.NaN])], '2026-09-08T00:00:00.000Z', {
        Dotnet: '',
        Os: '',
        Cpu: 'x',
      }) as string,
    ) as Artifact;
    expect(doc.metrics[0]?.value).toBeNull();
  });

  it('declares every metric in the pending template with no values', () => {
    const doc = JSON.parse(pendingTemplate() as string) as Artifact;
    expect(doc.status).toBe('pending');
    expect(doc.metrics.length).toBe(trees.length * 2);
    expect(doc.metrics.every((m) => m.value === null)).toBe(true);
  });
});

describe('the committed baseline', () => {
  it('is captured, host-stamped, and declares exactly the corpus it was measured over', async () => {
    const doc = (await import('../app/measure/render-latency-baseline.json')).default as Artifact;
    expect(doc.status).toBe('captured');
    // The host stamp carries the machine AND the browser engine — the gate
    // withholds a wall-time comparison across either.
    expect(doc.runtime.cpu).toContain('·');
    const ids = doc.metrics.map((m) => m.id);
    for (const t of trees) {
      expect(ids).toContain(`render.ttfp.${t.Label}.ms`);
      expect(ids).toContain(`render.full.${t.Label}.ms`);
    }
    for (const m of doc.metrics) {
      expect(m.value, `${m.id} is not captured`).not.toBeNull();
      expect(m.value as number).toBeGreaterThan(0);
    }
  });
});
