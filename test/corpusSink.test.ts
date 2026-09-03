// Phase 86 — the opt-in anonymous session-corpus sink.
//
// The feature makes four claims, and this suite exists so each is checkable
// rather than asserted:
//
//   1. THE PUBLIC BUILD NEVER POSTS. No endpoint is configured, so nothing is
//      offered and nothing is sent — and the shipped CSP gains not one
//      character, which matters because a widened `connect-src` would widen
//      every visitor's egress surface whether or not anyone contributed.
//   2. THE BUNDLE IS KEY-BLIND. Not "scrubbed": the builder has no key store in
//      scope, and the type the sink accepts has no case that could hold a key.
//      That is `SECURITY.md` key-handling guarantee 5's stated condition on any
//      corpus feature, and it is checked here structurally as well as by
//      payload.
//   3. THE GUARD REFUSES. Every known provider key format and every provider
//      origin is planted in turn — in the tree, in an op, and in the metadata —
//      and each must stop the contribution DEAD: no fetch, nothing uploaded.
//      A leak is a hard failure, never a warning.
//   4. IT DOES NOT OVER-FIRE, and it really does send. A guard that fires on
//      ordinary prose gets switched off, and a "no leak found" that came from a
//      probe which never posted is the vacuous result this file must not
//      produce. Both directions are pinned.
//
// The transcript is the other half of claim 2 and is asserted directly: the
// visitor's prompts and the model's replies are NOT in the bundle, only their
// count. "Nothing is stored about you" is only true if the free text a person
// typed never leaves, so that sentence gets a test rather than a promise.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { PROVIDER_ORIGINS } from '../src/byok/origins';
import { corpusSinkConnectSrc, corpusSinkOrigin, CORPUS_SINK_ENV } from '../src/corpus/sink';

import { readFileSync } from 'node:fs';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { empty, ingestResult } from '../app/output/Session.js';
import {
  buildFlat,
  configured,
  contributeProbeFlat,
  findingsFlat,
  prepareFlat,
  providerOriginsFlat,
  sinkUrl,
  // @ts-expect-error untyped Fable output
} from '../app/output/Contribute.js';
// @ts-expect-error untyped Fable output
import { providerOriginsFlat as adapterOrigins } from '../app/output/Byok.js';

// ─── fixtures ────────────────────────────────────────────────────────────────

/** An endpoint that exists only in this file. No test ever reaches a network:
 *  `fetch` is replaced wholesale below and every call is recorded instead. */
const ENDPOINT = 'https://collector.invalid/ingest';

const AT = '2026-09-03T00:00:00Z';

/** A tree with a `text` slot to plant things in, fed through the session's own
 *  REAL strict decoder — a fixture that drifted from the wire format fails here
 *  rather than passing as a test about something else. */
function wireWith(note: string): string {
  return JSON.stringify({
    id: 'root',
    kind: {
      $type: 'Box',
      children: [
        {
          id: 'note',
          kind: {
            $type: 'Markdown',
            text: { $type: 'Bound', binding: { $type: 'State', defaultValue: note, key: 'note' } },
          },
        },
        {
          id: 'submit-btn',
          kind: {
            $type: 'Button',
            label: 'Submit',
            onClick: { $type: 'SetState', key: 'sent', value: 'yes' },
            variant: 'Primary',
          },
        },
      ],
      layout: { $type: 'Auto' },
      role: 'Dashboard',
    },
  });
}

function sessionWith(note: string): unknown {
  const r = ingestResult(empty, wireWith(note));
  if (!r.Ok) throw new Error(`fixture did not decode: ${r.Error}`);
  return r.Next;
}

/** The clean baseline every positive assertion runs against. */
function cleanSession(): unknown {
  return sessionWith('A quarterly readout.');
}

/** The clean baseline plus one applied op, so the bundle has an op stream with a
 *  chain link in it rather than an empty array. */
function sessionWithOp(value: string): unknown {
  const op = JSON.stringify({
    $type: 'UpdateProp',
    target: 'submit-btn',
    path: 'label',
    value,
  });
  const r = ingestResult(cleanSession(), op);
  if (!r.Ok) throw new Error(`op fixture did not apply: ${r.Error}`);
  return r.Next;
}

/** Realistic-length instances of every key format the five supported providers
 *  issue. Each is 40+ characters after its prefix, as a real credential is —
 *  the guard requires length precisely so it can tell a credential from a
 *  hyphenated word. */
const PLANTED_KEYS: Record<string, string> = {
  anthropic: 'sk-ant-api03-Kd82jfLq0wXmZ1nR7vTyB4cPeH6sAu3G',
  'openai-project': 'sk-proj-9QmZ2xVt7Lb0Ry4KpNc8Ws1Ef6Ha3Ju5',
  'openai-legacy': 'sk-7Hb2Kq9Zx4Lp0Rm8Vn3Ct6Ye1Wa5Su7Jd0Gf',
  google: 'AIzaSyD3kL9pQm2Xv7Bn4Rt8Wc1Ye6Ha0Ju5Zs2',
  xai: 'xai-4Kd8Lq2wXmZ1nR7vTyB4cPeH6sAu3GfJ0d',
};

// ─── the instrumented fetch ──────────────────────────────────────────────────
//
// Every test in this file runs with `fetch` replaced. A contribution that was
// refused must leave `fetchCalls` empty, and one that was sent must leave
// exactly one entry — so "nothing was uploaded" is an observation rather than
// an inference.

type FetchCall = { url: string; init: Record<string, unknown> };
let fetchCalls: FetchCall[] = [];
let status = 202;
let throwOnFetch = false;

let savedFetch: PropertyDescriptor | undefined;

beforeEach(() => {
  fetchCalls = [];
  status = 202;
  throwOnFetch = false;
  savedFetch = Object.getOwnPropertyDescriptor(globalThis, 'fetch');
  Object.defineProperty(globalThis, 'fetch', {
    configurable: true,
    writable: true,
    value: async (url: unknown, init: Record<string, unknown>) => {
      fetchCalls.push({ url: String(url), init: init ?? {} });
      if (throwOnFetch) throw new Error('connect ECONNREFUSED');
      return { status, text: async () => '' };
    },
  });
});

afterEach(() => {
  if (savedFetch) Object.defineProperty(globalThis, 'fetch', savedFetch);
  else delete (globalThis as unknown as Record<string, unknown>).fetch;
});

/** The single request a sent contribution made. A missing one is a failure, not
 *  a skip: several assertions below would otherwise pass vacuously. */
function onlyCall(): FetchCall {
  const call = fetchCalls[0];
  if (call === undefined) throw new Error('expected exactly one POST, but none was made');
  return call;
}

// ─── 1. the public build ─────────────────────────────────────────────────────

describe('the public build ships with collection disabled', () => {
  it('no endpoint is configured in this build', () => {
    // vitest runs with no VITE_CORPUS_SINK, which is exactly the public build's
    // configuration — so this whole file runs against the shipped posture.
    expect(sinkUrl).toBe('');
    expect(configured).toBe(false);
  });

  it('an unconfigured sink refuses and opens no connection at all', async () => {
    const outcome = await contributeProbeFlat('', 'anthropic', 'model-x', AT, cleanSession());
    expect(outcome.Outcome).toBe('refused');
    expect(outcome.Reason).toMatch(/no collection endpoint/i);
    expect(fetchCalls).toEqual([]);
  });

  it('the CSP gains nothing when no sink is set', () => {
    // The exact call vite.config.ts makes, for the value the public build has.
    expect(corpusSinkConnectSrc(process.env[CORPUS_SINK_ENV])).toBe('');
    for (const unset of [undefined, null, '', '   ']) {
      expect(corpusSinkConnectSrc(unset)).toBe('');
      expect(corpusSinkOrigin(unset)).toBe('');
    }
  });

  it('a configured sink contributes exactly one origin, and only a usable one', () => {
    expect(corpusSinkOrigin('https://collector.example/ingest')).toBe('https://collector.example');
    expect(corpusSinkConnectSrc('https://collector.example/ingest')).toBe(
      ' https://collector.example',
    );
    // Loopback http is admitted (an operator's own machine); remote http and
    // every other scheme fail closed rather than opening a cleartext channel.
    expect(corpusSinkOrigin('http://localhost:9000/ingest')).toBe('http://localhost:9000');
    expect(corpusSinkOrigin('http://127.0.0.1:9000/ingest')).toBe('http://127.0.0.1:9000');
    for (const bad of [
      'http://collector.example/ingest',
      'ftp://collector.example/ingest',
      'javascript:alert(1)',
      'data:text/plain,x',
      '/ingest',
      'collector.example',
    ])
      expect(corpusSinkOrigin(bad)).toBe('');
  });
});

// ─── 2. key-blindness, structurally ──────────────────────────────────────────

describe('the builder is key-blind by construction', () => {
  const contributeSource = readFileSync(new URL('../app/Contribute.fs', import.meta.url), 'utf8');
  const byokSource = readFileSync(new URL('../app/Byok.fs', import.meta.url), 'utf8');

  it('the contributing module reaches for no key store and no auth header', () => {
    // An absence no exercised path could reveal, so it is checked at the source
    // — the same instrument `networkEgress.test.ts` uses for the same class of
    // claim about `Byok.fs`. Comment prose in this module DOES discuss keys, so
    // the check is against code tokens that would actually reach one.
    const code = contributeSource
      .split('\n')
      .filter((line) => !line.trimStart().startsWith('//'))
      .join('\n');
    for (const reach of [
      'keyStore',
      'KeyStore',
      'Byok.',
      'x-api-key',
      'x-goog-api-key',
      'Authorization',
      'Bearer',
    ])
      expect(code).not.toContain(reach);
  });

  it('the key-bearing module knows nothing of the sink', () => {
    for (const reach of ['Contribute', 'CORPUS_SINK', 'ContributionBundle'])
      expect(byokSource).not.toContain(reach);
  });

  it('the guard, the adapters and the CSP name one set of provider origins', () => {
    // A sixth provider added to the registry without being added to the guard's
    // list would otherwise slip past the origin half of the check silently.
    expect(new Set(providerOriginsFlat())).toEqual(new Set(PROVIDER_ORIGINS));
    expect(new Set(providerOriginsFlat())).toEqual(new Set(adapterOrigins()));
  });
});

// ─── 3. the guard refuses, per class, per position ───────────────────────────

describe('a planted credential stops the contribution dead', () => {
  for (const [provider, key] of Object.entries(PLANTED_KEYS)) {
    it(`${provider}: planted in the rendered tree`, async () => {
      const session = sessionWith(`My key is ${key} — remember it.`);

      const prepared = prepareFlat('anthropic', 'model-x', AT, session);
      expect(prepared.Ok).toBe(false);
      expect(prepared.Reason).toContain('key-shaped token');
      expect(prepared.Json).toBe('');

      const outcome = await contributeProbeFlat(ENDPOINT, 'anthropic', 'model-x', AT, session);
      expect(outcome.Outcome).toBe('refused');
      expect(fetchCalls).toEqual([]);
    });

    it(`${provider}: planted in an applied op`, async () => {
      const session = sessionWithOp(`Send ${key}`);
      const outcome = await contributeProbeFlat(ENDPOINT, 'anthropic', 'model-x', AT, session);
      expect(outcome.Outcome).toBe('refused');
      expect(outcome.Reason).toContain('key-shaped token');
      expect(fetchCalls).toEqual([]);
    });

    it(`${provider}: planted in the metadata`, async () => {
      // The metadata is caller-supplied, so it is scanned like everything else
      // rather than trusted for coming from inside the app.
      const outcome = await contributeProbeFlat(ENDPOINT, 'anthropic', key, AT, cleanSession());
      expect(outcome.Outcome).toBe('refused');
      expect(fetchCalls).toEqual([]);
    });
  }

  for (const origin of PROVIDER_ORIGINS) {
    it(`a provider endpoint URL (${origin}) is refused too`, async () => {
      const session = sessionWith(`See ${origin}/v1/messages for details.`);
      const outcome = await contributeProbeFlat(ENDPOINT, 'anthropic', 'model-x', AT, session);
      expect(outcome.Outcome).toBe('refused');
      expect(outcome.Reason).toContain(origin);
      expect(fetchCalls).toEqual([]);
    });
  }

  it('names every class it found, not merely the first', () => {
    const session = sessionWith(`${PLANTED_KEYS.anthropic} at ${PROVIDER_ORIGINS[0]}`);
    const found = findingsFlat(buildFlat('anthropic', 'model-x', AT, session));
    expect(found.length).toBeGreaterThan(1);
    expect(found.join(' ')).toContain('key-shaped token');
    expect(found.join(' ')).toContain(PROVIDER_ORIGINS[0]);
  });

  it('a refusal quotes no secret back', () => {
    const key = PLANTED_KEYS.anthropic;
    const prepared = prepareFlat('anthropic', 'model-x', AT, sessionWith(`key ${key}`));
    expect(prepared.Reason).not.toContain(key);
  });

  it('a session with no tree is refused (there is nothing to contribute)', async () => {
    const outcome = await contributeProbeFlat(ENDPOINT, 'anthropic', 'model-x', AT, empty);
    expect(outcome.Outcome).toBe('refused');
    expect(fetchCalls).toEqual([]);
  });
});

// ─── 4. it does not over-fire, and it really does send ───────────────────────

describe('the guard leaves ordinary content alone', () => {
  // Each of these contains a key PREFIX as a substring of ordinary English or
  // of an ordinary identifier. A guard that fired on them would be a guard
  // someone turned off, at which point it protects nothing.
  const innocuous = [
    'A risk-averse task-oriented brief.',
    'AIzawa plotted the attractor.',
    'The desk-bound sk-8 model, briefly.',
    'Ask-then-act, not act-then-ask.',
    'xai-lab is a name; so is sk-ips.',
  ];

  for (const note of innocuous) {
    it(`clean: ${note}`, () => {
      expect(findingsFlat(buildFlat('anthropic', 'model-x', AT, sessionWith(note)))).toEqual([]);
    });
  }

  it('a clean session is genuinely POSTed — one request, to the endpoint', async () => {
    const outcome = await contributeProbeFlat(
      ENDPOINT,
      'anthropic',
      'model-x',
      AT,
      sessionWithOp('Go'),
    );
    expect(outcome.Outcome).toBe('sent');
    expect(fetchCalls).toHaveLength(1);
    expect(onlyCall().url).toBe(ENDPOINT);
  });

  it('the request is anonymous: no credentials, no custom header, no redirect', async () => {
    await contributeProbeFlat(ENDPOINT, 'anthropic', 'model-x', AT, cleanSession());
    const { init } = onlyCall();
    expect(init.method).toBe('POST');
    // No cookie or stored credential rides along — an "anonymous" contribution
    // that carried the browser's credentials would not be one.
    expect(init.credentials).toBe('omit');
    expect(init.redirect).toBe('error');
    expect(Object.keys(init.headers as Record<string, string>)).toEqual(['content-type']);
  });

  it('a non-2xx and a transport fault both fail without claiming success', async () => {
    status = 500;
    const bad = await contributeProbeFlat(ENDPOINT, 'anthropic', 'model-x', AT, cleanSession());
    expect(bad.Outcome).toBe('failed');
    expect(bad.Reason).toContain('500');

    fetchCalls = [];
    throwOnFetch = true;
    const dead = await contributeProbeFlat(ENDPOINT, 'anthropic', 'model-x', AT, cleanSession());
    expect(dead.Outcome).toBe('failed');
  });
});

// ─── the bundle's own content contract ───────────────────────────────────────

describe('what the bundle carries, and what it deliberately does not', () => {
  it('carries the trees, the attributed op chain, and four metadata fields', () => {
    const bundle = JSON.parse(buildFlat('anthropic', 'model-x', AT, sessionWithOp('Go')));

    expect(bundle.kind).toBe('fuaran-live/session-corpus');
    expect(bundle.version).toBe(1);
    expect(bundle.capturedAt).toBe(AT);
    expect(bundle.provider).toBe('anthropic');
    expect(bundle.model).toBe('model-x');
    expect(typeof bundle.promptCount).toBe('number');

    // Both trees: without the base there is nothing for the ops to replay
    // against, and without the result there is nothing to check the replay
    // produced. Either alone makes the op stream uncheckable.
    expect(bundle.baseTree).toBeTruthy();
    expect(bundle.tree).toBeTruthy();

    expect(bundle.ops).toHaveLength(1);
    const op = bundle.ops[0];
    expect(op.seq).toBe(1);
    expect(op.kind).toBe('UpdateProp');
    expect(op.actor).toMatch(/^agent:/);
    expect(typeof op.hash).toBe('string');
    expect(op.hash.length).toBeGreaterThan(0);
    expect(typeof op.prev).toBe('string');
    // The op itself is embedded as a document, not as a string, so a collector
    // reads one JSON tree rather than parsing values out of it.
    expect(op.op.$type).toBe('UpdateProp');
  });

  it('does NOT carry the prompts, the replies, or any identifier', () => {
    // The whole of "nothing is stored about you": free text a person typed
    // never leaves, and only its COUNT does.
    const SENTINEL = 'MY-PRIVATE-BUSINESS-PLAN-9f2c';
    const withHistory = {
      ...(cleanSession() as Record<string, unknown>),
      History: [
        { Role: { tag: 0 }, Content: `build me a dashboard about ${SENTINEL}` },
        { Role: { tag: 1 }, Content: `here is ${SENTINEL}` },
      ],
    };

    const json = buildFlat('anthropic', 'model-x', AT, withHistory);
    expect(json).not.toContain(SENTINEL);

    const bundle = JSON.parse(json);
    // No account, no cookie, no visitor id, no session id: there are exactly
    // nine members and none of them is an identifier.
    expect(Object.keys(bundle).sort()).toEqual(
      [
        'baseTree',
        'capturedAt',
        'kind',
        'model',
        'ops',
        'promptCount',
        'provider',
        'tree',
        'version',
      ].sort(),
    );
  });
});
