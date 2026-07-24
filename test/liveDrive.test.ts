// Phase 295 – serverless live-drive (Stage 1), over the Fable output.
//
// The present → audience transport: a full-tree snapshot on (re)placement, then
// surgical TreeOps as the op-stream grows; the channel codec round-trips; and the
// trust invariant – the ONLY thing a live-drive message carries is canonical wire
// JSON (a tree or an op), never the BYOK key. Headless via vitest.

import { describe, it, expect } from 'vitest';

import {
  deltaEnvelopes,
  envelopeKind,
  envelopePayload,
  envelopeRoundTrips,
  // @ts-expect-error untyped Fable output (no .d.ts for app/output/Live.js)
} from '../app/output/Live.js';
// @ts-expect-error untyped Fable output
import { empty, ingestResult } from '../app/output/Session.js';

// A first emission: a full Dashboard tree (the decoder canonicalises the
// container to the Box kind + role="Dashboard").
const treeWire =
  '{"id":"root","kind":{"$type":"Dashboard","children":[{"id":"m","kind":{"$type":"Metric","format":{"$type":"Number","decimals":0},"label":"Count","tone":"Brand","value":{"$type":"Static","value":1}}}]}}';
// A follow-up op against that tree.
const opWire =
  '{"$type":"UpdateStyle","style":{"emphasis":"Loud","tone":"Success","weight":"Spacious"},"target":"m"}';

const sessionWith = (raw: string, from = empty) => {
  const r = ingestResult(from, raw);
  expect(r.Ok).toBe(true);
  return r.Next;
};

describe('Phase 295 – serverless live-drive (Stage 1)', () => {
  it('a first tree is broadcast as a full-tree snapshot', () => {
    const s = sessionWith(treeWire);
    const envs: string[] = deltaEnvelopes(empty, s);
    expect(envs).toHaveLength(1);
    expect(envelopeKind(envs[0]!)).toBe('tree');
    // the payload is the canonical tree wire (Dashboard → Box + role)
    expect(envelopePayload(envs[0]!)).toContain('"role":"Dashboard"');
  });

  it('a subsequent edit is broadcast as a surgical TreeOp (not a whole tree)', () => {
    const s1 = sessionWith(treeWire);
    const s2 = sessionWith(opWire, s1);
    const envs: string[] = deltaEnvelopes(s1, s2);
    expect(envs).toHaveLength(1);
    expect(envelopeKind(envs[0]!)).toBe('op');
    expect(envelopePayload(envs[0]!)).toContain('"$type":"UpdateStyle"');
  });

  it('no change produces no broadcast', () => {
    const s = sessionWith(treeWire);
    expect(deltaEnvelopes(s, s)).toHaveLength(0);
    expect(deltaEnvelopes(empty, empty)).toHaveLength(0);
  });

  it('the channel codec round-trips every message byte-identically', () => {
    const s1 = sessionWith(treeWire);
    const s2 = sessionWith(opWire, s1);
    for (const env of [...deltaEnvelopes(empty, s1), ...deltaEnvelopes(s1, s2)]) {
      expect(envelopeRoundTrips(env)).toBe(true);
    }
  });

  it('TRUST: a live-drive message carries ONLY {kind, payload} wire data – never a key', () => {
    // The audience is driven from a session that also holds (elsewhere) a BYOK
    // key; the channel envelope must expose nothing but the UI-as-data.
    const s = sessionWith(treeWire);
    const envs: string[] = deltaEnvelopes(empty, s);
    for (const env of envs) {
      const parsed = JSON.parse(env);
      expect(Object.keys(parsed).sort()).toEqual(['kind', 'payload']);
      // no key-ish field can ride along
      expect(env.toLowerCase()).not.toContain('apikey');
      expect(env.toLowerCase()).not.toContain('sk-');
    }
  });
});
