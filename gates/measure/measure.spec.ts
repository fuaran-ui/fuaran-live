// Phase 1628 — the render-latency capture, and the check that it measured
// something.
//
// The harness itself is F#/Fable (`app/measure/`); this is its driver. It opens
// the measure page in a real browser, hands the capture the machine identity
// only the driver can see, and receives the perf-baseline artefact the gate
// consumes.
//
// THE SPEC IS A PROBE CHECK, NOT A LATENCY GATE. It never asserts a threshold
// in milliseconds — a wall-clock bound would fail on a loaded CI runner and say
// nothing about the code. What it asserts is that the instrument WORKS:
//
//   1. every declared metric carries a finite, positive number — a harness that
//      silently produced nulls would emit an artefact the gate reads as "not
//      captured", which looks like nothing happening rather than like a break;
//   2. full-render is never below time-to-commit, which is true by construction
//      and so is a check on the harness's own arithmetic;
//   3. the numbers SCALE WITH TREE SIZE. This is the assertion that the capture
//      is measuring rendering at all. A harness accidentally timing an empty
//      container, a cached root or a scheduling call returns numbers that do
//      not care how many nodes it was given; 97 nodes must cost materially more
//      than 5, or the reading is not a reading.
//
// Set MEASURE_OUT to write the artefact to a file (that is how the committed
// baseline and the run-to-run spread samples are produced); MEASURE_REPEATS and
// MEASURE_WARMUPS override the sample counts.

import { mkdirSync, writeFileSync } from 'node:fs';
import { cpus, release, type as osType } from 'node:os';
import { dirname, resolve } from 'node:path';

import { test, expect } from '@playwright/test';

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
  captured_at_utc: string;
  runtime: { dotnet: string; os: string; cpu: string };
  metrics: Metric[];
}

/** The machine identity the browser cannot see. Folded into `runtime.cpu`
 *  beside the browser engine by the harness — see `Timing.browserId`. */
function machine(): { cpu: string; os: string } {
  const c = cpus();
  const model = c.length > 0 ? (c[0]?.model ?? 'unknown CPU') : 'unknown CPU';
  return {
    cpu: `${model.trim()}, ${c.length}lp`,
    os: `${osType()} ${release()}`,
  };
}

function valueOf(artifact: Artifact, id: string): number {
  const m = artifact.metrics.find((x) => x.id === id);
  expect(m, `metric ${id} is absent from the captured artefact`).toBeDefined();
  expect(m?.value, `metric ${id} has no captured value`).not.toBeNull();
  return m?.value as number;
}

test('render-latency capture produces a usable baseline artefact', async ({ page }) => {
  const failures: string[] = [];
  page.on('pageerror', (e) => failures.push(String(e)));

  await page.goto('/measure.html');
  await page.waitForFunction(
    () => typeof (window as never as Record<string, unknown>).__fuaranMeasure !== 'undefined',
  );

  const { cpu, os } = machine();
  const repeats = Number(process.env.MEASURE_REPEATS ?? '25');
  const warmups = Number(process.env.MEASURE_WARMUPS ?? '3');

  // The capture's own failure is reported as TEXT, not as a thrown page error.
  // A production bundle is minified, so an exception crossing the evaluate
  // boundary arrives as its mangled constructor name (`ft`) and nothing else —
  // which is the least useful possible report of, say, a corpus document that
  // has fallen out of step with the wire format.
  const result = await page.evaluate(
    (opts) => {
      try {
        return {
          ok: true,
          text: (
            window as never as {
              __fuaranMeasure: { capture: (o: string) => string };
            }
          ).__fuaranMeasure.capture(opts),
        };
      } catch (e) {
        // An F#/Fable exception is not an `Error` instance, so `String(e)`
        // yields `[object Object]`. Read the message off whatever shape
        // arrives, and fall back to the serialised object rather than to a
        // sentence that says nothing.
        const box = e as { name?: string; message?: string; Message?: string } | null;
        const msg = box?.message ?? box?.Message ?? (typeof e === 'string' ? e : JSON.stringify(e));
        return { ok: false, text: `${box?.name ?? 'error'}: ${msg}` };
      }
    },
    JSON.stringify({ repeats, warmups, machineCpu: cpu, machineOs: os }),
  );

  expect(result.ok, `the capture threw: ${result.text}`).toBe(true);
  expect(failures, `the measure page raised errors: ${failures.join('; ')}`).toEqual([]);

  const json = result.text;
  const artifact = JSON.parse(json) as Artifact;

  // (1) The artefact is captured, stamped, and complete.
  expect(artifact.artifact).toBe('render-latency');
  expect(artifact.status).toBe('captured');
  expect(artifact.runtime.cpu).not.toBe('');
  // The browser engine and version are stamped beside the machine, so a run in
  // a different engine is as un-comparable to the gate as one on a different
  // machine — which it is.
  expect(artifact.runtime.cpu).not.toBe(cpu);
  expect(artifact.metrics.length).toBeGreaterThanOrEqual(8);

  for (const m of artifact.metrics) {
    expect(m.value, `${m.id} has no value`).not.toBeNull();
    expect(Number.isFinite(m.value), `${m.id} is not finite`).toBe(true);
    expect(m.value as number, `${m.id} is not positive`).toBeGreaterThan(0);
    expect(m.unit).toBe('ms');
  }

  // (2) Commit cannot exceed commit-plus-layout.
  for (const label of ['Small', 'Medium', 'Large', 'DeepNested']) {
    expect(
      valueOf(artifact, `render.full.${label}.ms`),
      `render.full.${label} is below render.ttfp.${label}`,
    ).toBeGreaterThanOrEqual(valueOf(artifact, `render.ttfp.${label}.ms`));
  }

  // (3) The reading responds to the thing it claims to measure. 97 nodes
  //     against 5 is a ~19x node-count range; requiring only 2x leaves ample
  //     room for a noisy runner while still failing a harness that is timing
  //     something other than the tree it was handed.
  const small = valueOf(artifact, 'render.full.Small.ms');
  const large = valueOf(artifact, 'render.full.Large.ms');
  expect(
    large / small,
    `render.full does not scale with tree size (Small ${small}ms, Large ${large}ms) — the capture is not measuring the tree`,
  ).toBeGreaterThan(2);

  const out = process.env.MEASURE_OUT;
  if (out !== undefined && out !== '') {
    const path = resolve(out);
    mkdirSync(dirname(path), { recursive: true });
    writeFileSync(path, json, 'utf8');
    console.log(`[measure] wrote ${path}`);
  }

  for (const m of artifact.metrics) {
    console.log(`[measure] ${m.id.padEnd(30)} ${(m.value as number).toFixed(3)} ${m.unit}`);
  }
});
