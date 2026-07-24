// Rosetta parity lock – every encoder the /demo/rosetta page runs must emit the
// reference canonical bytes for the exemplar tree. The pinned literal below IS
// the real Fuaran.UI canonical encoder's output for the default holes (verified
// against the Fable-compiled reference on 2026-07-23); when a canonical-format
// rev lands, this pin moves in the same change-set as rosetta-hosts.ts and the
// wasm artefacts – a failing pin here means the live byte-parity strip on
// /demo/rosetta is showing "diverges", which is exactly the regression this test
// exists to catch.
//
// Three of the page's five Tier-1 encoders are locked here headlessly:
//   • the independent TypeScript encoder (in-process);
//   • the Rust host – the committed fuaran-rs wasm32 artefact, instantiated in
//     Node and driven through its `fuaran_rosetta_encode` C-ABI export;
//   • the Go host – the committed fuaran-go js/wasm artefact, instantiated in
//     Node via the Go toolchain's `wasm_exec.js` glue.
// The F# encoder is the reference the pin is taken from (its own build gate
// covers it). The Python host (public/rosetta/py-host.py) runs in Pyodide in the
// browser and is exercised MANUALLY on the page – there is no headless Pyodide
// leg here; keep its tree builder in lockstep with this pin when editing either.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { encodeWireTs } from '../app/showcase/rosetta-hosts';

const defaultHoles = {
  labelA: 'Signups',
  valueA: 1280,
  labelB: 'Revenue',
  valueB: 42.5,
  labelC: 'Churn %',
  valueC: 12.4,
};

const referenceWire =
  '{"id":"rosetta-root","kind":{"$type":"Box","children":[{"id":"rosetta-strip",' +
  '"kind":{"$type":"Box","children":[{"id":"rosetta-m-a","kind":{"$type":"Metric",' +
  '"label":"Signups","value":{"$type":"Static","value":1280}}},{"id":"rosetta-m-b",' +
  '"kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":42.5}}},' +
  '{"id":"rosetta-m-c","kind":{"$type":"Metric","label":"Churn %","value":' +
  '{"$type":"Static","value":12.4}}}],"layout":{"$type":"Flex","direction":"Horizontal",' +
  '"wrap":true},"role":"Group"}}],"heading":"Revenue snapshot","layout":' +
  '{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Dashboard"}}';

const rosettaDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'public', 'rosetta');

describe('rosetta byte-parity', () => {
  it('the independent TS encoder emits the reference canonical bytes', () => {
    expect(encodeWireTs(defaultHoles)).toBe(referenceWire);
  });

  it('the committed Rust wasm artefact emits the reference canonical bytes', async () => {
    const bytes = readFileSync(join(rosettaDir, 'rosetta-rs.wasm'));
    const { instance } = await WebAssembly.instantiate(bytes, {});
    const x = instance.exports as unknown as {
      memory: WebAssembly.Memory;
      fuaran_alloc(len: number): number;
      fuaran_dealloc(ptr: number, len: number): void;
      fuaran_rosetta_encode(ptr: number, len: number): bigint;
    };
    const input = new TextEncoder().encode(JSON.stringify(defaultHoles));
    const inPtr = x.fuaran_alloc(input.length);
    new Uint8Array(x.memory.buffer).set(input, inPtr);
    const packed = BigInt.asUintN(64, x.fuaran_rosetta_encode(inPtr, input.length));
    x.fuaran_dealloc(inPtr, input.length);
    const outPtr = Number(packed >> 32n);
    const outLen = Number(packed & 0xffffffffn);
    const out = new Uint8Array(x.memory.buffer).slice(outPtr, outPtr + outLen);
    x.fuaran_dealloc(outPtr, outLen);
    expect(new TextDecoder().decode(out)).toBe(referenceWire);
  });

  it('the committed Go wasm artefact emits the reference canonical bytes', async () => {
    // The Go module needs the toolchain's wasm_exec.js glue (also committed).
    const glue = readFileSync(join(rosettaDir, 'wasm_exec.js'), 'utf8');
    const sandbox = globalThis as unknown as {
      Go?: new () => {
        importObject: WebAssembly.Imports;
        run(i: WebAssembly.Instance): Promise<void>;
      };
      fuaranGoRosettaEncode?: (holesJSON: string) => string;
    };
    // eslint-disable-next-line no-eval
    (0, eval)(glue); // defines globalThis.Go
    const GoCtor = sandbox.Go;
    if (!GoCtor) throw new Error('wasm_exec.js did not define Go');
    const go = new GoCtor();
    const bytes = readFileSync(join(rosettaDir, 'rosetta-go.wasm'));
    const { instance } = await WebAssembly.instantiate(bytes, go.importObject);
    // main registers the global then blocks on select{}; do not await run.
    void go.run(instance);
    for (let i = 0; i < 200 && typeof sandbox.fuaranGoRosettaEncode !== 'function'; i++) {
      await new Promise((r) => setTimeout(r, 10));
    }
    const encode = sandbox.fuaranGoRosettaEncode;
    if (typeof encode !== 'function') throw new Error('Go host did not register its encoder');
    expect(encode(JSON.stringify(defaultHoles))).toBe(referenceWire);
  });
});
