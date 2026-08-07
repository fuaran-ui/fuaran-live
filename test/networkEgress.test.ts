// The key-egress guard — the test SECURITY.md's key-handling guarantees name.
//
// The claim being backed: the BYOK provider key reaches the provider's auth
// header and NOTHING else — no `localStorage` / `sessionStorage` / IndexedDB /
// cookie, no `console` or other log sink, no telemetry payload, no URL or query
// string, no request body, and no origin outside the provider allow-list.
//
// How it is backed rather than asserted. The suite installs an instrumented
// world — fake storage surfaces, a recording `document.cookie`, recording
// `indexedDB` / `sendBeacon` / `XMLHttpRequest` / `WebSocket` / `EventSource` /
// `Image` / `RTCPeerConnection`, a patched `console`, and a `fetch` that records
// the URL, the body and every non-auth header — then drives the REAL adapter
// through `egressProbeFlat` (a fresh memory-only key store, the real egress
// helper, the real `fetch` call) for every provider, on both the single-shot and
// the tool-use path, over the success branch AND every failure branch. Anything
// written anywhere becomes a "sighting"; a sighting containing the key fails.
//
// The failure branches carry most of the weight. A credential escapes through a
// MESSAGE far more plausibly than through a request: an error body from the
// provider and an exception from the transport are both strings this app did not
// write, and both flow into `ProviderError.Message` and thence to the UI and the
// warn port. So the surfaced message is asserted as carefully as the request is.
//
// Finally, the instrumentation is itself tested (the last describe). A guard
// that has silently stopped observing passes exactly like a guard with nothing
// to find, so a synthetic leaky client is run through the same harness and each
// leak class must be caught. That block is what keeps the rest honest.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { readFileSync } from 'node:fs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { PROVIDER_ORIGINS } from '../src/byok/origins';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
import {
  createKeyStore,
  egressProbeFlat,
  providerOriginsFlat,
  scrubOnUnload,
  // @ts-expect-error untyped Fable output
} from '../app/output/Byok.js';

// A sentinel with no substring in common with anything else the app emits, so a
// match is never a coincidence. Long enough to clear the redaction helper's
// minimum-length guard, exactly as a real credential is.
const KEY = 'sk-egress-sentinel-3f9a2c7e51b04d68af0c1e2b3d4f5a67';

/** The auth header each provider is permitted to put the key in — and the only
 *  place in the whole request it may appear. */
const AUTH_HEADER: Record<string, { name: string; value: string }> = {
  anthropic: { name: 'x-api-key', value: KEY },
  openai: { name: 'authorization', value: `Bearer ${KEY}` },
  gemini: { name: 'x-goog-api-key', value: KEY },
  kimi: { name: 'authorization', value: `Bearer ${KEY}` },
  xai: { name: 'authorization', value: `Bearer ${KEY}` },
};

const PROVIDERS = Object.keys(AUTH_HEADER);

/** A minimal successful response in each provider's own wire shape. */
const successBody: Record<string, string> = {
  anthropic: JSON.stringify({
    content: [{ type: 'text', text: 'rendered' }],
    stop_reason: 'end_turn',
    usage: { input_tokens: 1, output_tokens: 1 },
  }),
  openai: JSON.stringify({
    choices: [{ message: { content: 'rendered' }, finish_reason: 'stop' }],
    usage: { prompt_tokens: 1, completion_tokens: 1 },
  }),
  gemini: JSON.stringify({
    candidates: [{ content: { parts: [{ text: 'rendered' }] }, finishReason: 'STOP' }],
    usageMetadata: { promptTokenCount: 1, candidatesTokenCount: 1 },
  }),
  kimi: JSON.stringify({
    choices: [{ message: { content: 'rendered' }, finish_reason: 'stop' }],
    usage: { prompt_tokens: 1, completion_tokens: 1 },
  }),
  xai: JSON.stringify({
    choices: [{ message: { content: 'rendered' }, finish_reason: 'stop' }],
    usage: { prompt_tokens: 1, completion_tokens: 1 },
  }),
};

// Total lookups over the two tables above. A missing entry is a mistake in this
// file, not a soft-fail: a provider added to the registry with no canned body or
// declared auth header must stop the suite loudly rather than quietly skip the
// provider that was just introduced.
function bodyFor(provider: string): string {
  const body = successBody[provider];
  if (body === undefined) throw new Error(`no canned success body for provider '${provider}'`);
  return body;
}

function authFor(provider: string): { name: string; value: string } {
  const header = AUTH_HEADER[provider];
  if (header === undefined) throw new Error(`no auth header declared for provider '${provider}'`);
  return header;
}

// ─── the instrumented world ──────────────────────────────────────────────────

type Sighting = { where: string; text: string };
type FetchCall = { url: string; init: Record<string, unknown> };

/** Everything written to any surface that is not the sanctioned auth header. */
let sightings: Sighting[] = [];
/** Every `fetch` the run made — URL + init, verbatim. */
let fetchCalls: FetchCall[] = [];
/** Every attempt to open a network channel by any means OTHER than `fetch`. */
let otherNetworkAttempts: string[] = [];
/** What the stubbed `fetch` should do next. */
let responder: (url: string, init: Record<string, unknown>) => { status: number; body: string };

function show(value: unknown): string {
  if (typeof value === 'string') return value;
  try {
    return JSON.stringify(value) ?? String(value);
  } catch {
    return Object.prototype.toString.call(value);
  }
}

function record(where: string, ...values: unknown[]): void {
  sightings.push({ where, text: values.map(show).join(' ') });
}

/** A proxy that records every property write and every call, at any depth — so
 *  `indexedDB.open(x).transaction(y).objectStore(z).put(v)` is caught whole,
 *  without this test having to model IndexedDB. */
function recordingProxy(where: string, onCall?: (where: string) => void): unknown {
  const inert = function () {};
  return new Proxy(inert, {
    get(_target, prop) {
      if (prop === 'then' || prop === 'toJSON') return undefined;
      if (prop === 'toString' || prop === Symbol.toPrimitive) return () => `[${where}]`;
      return recordingProxy(`${where}.${String(prop)}`, onCall);
    },
    set(_target, prop, value) {
      record(`${where}.${String(prop)}`, value);
      return true;
    },
    apply(_target, _thisArg, args) {
      onCall?.(where);
      record(`${where}()`, ...args);
      return recordingProxy(`${where}()`, onCall);
    },
    construct(_target, args): object {
      onCall?.(`new ${where}`);
      record(`new ${where}()`, ...args);
      return recordingProxy(`new ${where}()`, onCall) as object;
    },
  });
}

/** A Storage-shaped fake: reads work, every write is recorded. */
function recordingStorage(where: string) {
  const map = new Map<string, string>();
  return {
    getItem: (k: string) => map.get(k) ?? null,
    setItem: (k: string, v: string) => {
      record(`${where}.setItem`, k, v);
      map.set(k, v);
    },
    removeItem: (k: string) => void map.delete(k),
    clear: () => map.clear(),
    key: (i: number) => [...map.keys()][i] ?? null,
    get length() {
      return map.size;
    },
  };
}

const CONSOLE_METHODS = ['log', 'info', 'warn', 'error', 'debug', 'trace', 'dir'] as const;
const INSTALLED = [
  'localStorage',
  'sessionStorage',
  'indexedDB',
  'document',
  'navigator',
  'XMLHttpRequest',
  'WebSocket',
  'EventSource',
  'Image',
  'RTCPeerConnection',
  'fetch',
  'window',
] as const;

// Saved as DESCRIPTORS, not values: several of these (`navigator` in Node 22)
// are accessor properties on the global object, so plain assignment throws and
// plain restoration would silently turn an accessor into a data property.
const savedGlobals = new Map<string, PropertyDescriptor | undefined>();
const savedConsole = new Map<string, unknown>();
/** Listeners registered on the fake `window` — the `pagehide` scrub lands here. */
let windowListeners: Array<[string, () => void]> = [];

function define(name: string, value: unknown): void {
  Object.defineProperty(globalThis, name, { value, configurable: true, writable: true });
}

function installInstrumentedWorld(): void {
  sightings = [];
  fetchCalls = [];
  otherNetworkAttempts = [];
  windowListeners = [];
  responder = () => ({ status: 200, body: '{}' });

  for (const name of INSTALLED)
    savedGlobals.set(name, Object.getOwnPropertyDescriptor(globalThis, name));
  for (const name of CONSOLE_METHODS) savedConsole.set(name, (console as never)[name]);

  const networked = (where: string) => void otherNetworkAttempts.push(where);

  define('localStorage', recordingStorage('localStorage'));
  define('sessionStorage', recordingStorage('sessionStorage'));
  define('indexedDB', recordingProxy('indexedDB'));

  // A `document` whose `cookie` setter is recorded; everything else records too.
  let cookie = '';
  define(
    'document',
    new Proxy(
      {},
      {
        get: (_t, prop) =>
          prop === 'cookie' ? cookie : recordingProxy(`document.${String(prop)}`, networked),
        set: (_t, prop, value) => {
          record(`document.${String(prop)}`, value);
          if (prop === 'cookie') cookie = String(value);
          return true;
        },
      },
    ),
  );

  define(
    'navigator',
    new Proxy({}, { get: (_t, prop) => recordingProxy(`navigator.${String(prop)}`, networked) }),
  );
  for (const ctor of ['XMLHttpRequest', 'WebSocket', 'EventSource', 'Image', 'RTCPeerConnection'])
    define(ctor, recordingProxy(ctor, networked));

  define('window', {
    addEventListener: (type: string, handler: () => void) =>
      void windowListeners.push([type, handler]),
    removeEventListener: () => {},
  });

  for (const name of CONSOLE_METHODS)
    (console as never as Record<string, unknown>)[name] = (...args: unknown[]) =>
      record(`console.${name}`, ...args);

  define('fetch', async (url: unknown, init: Record<string, unknown>) => {
    const href = String(url);
    fetchCalls.push({ url: href, init: init ?? {} });
    record('fetch.url', href);
    record('fetch.body', (init?.body as string) ?? '');
    // Headers are recorded EXCEPT the provider's own auth header — that one is
    // the sanctioned channel, and is asserted separately (below) to prove the
    // probe really did carry the key rather than silently dropping it.
    for (const [name, value] of Object.entries((init?.headers as Record<string, string>) ?? {})) {
      const auth = Object.values(AUTH_HEADER).some((h) => h?.name === name.toLowerCase());
      if (!auth) record(`fetch.header.${name}`, value);
    }
    const { status, body } = responder(href, init ?? {});
    return { status, text: async () => body };
  });
}

function restoreWorld(): void {
  for (const name of INSTALLED) {
    const saved = savedGlobals.get(name);
    if (saved) Object.defineProperty(globalThis, name, saved);
    else delete (globalThis as unknown as Record<string, unknown>)[name];
  }
  for (const name of CONSOLE_METHODS)
    (console as never as Record<string, unknown>)[name] = savedConsole.get(name);
}

/** Every place the key was seen that is not the sanctioned auth header. */
function leaks(): Sighting[] {
  return sightings.filter((s) => s.text.includes(KEY));
}

function expectNoLeak(): void {
  expect(leaks().map((s) => `${s.where}: ${s.text}`)).toEqual([]);
}

/** The single request the run made. A missing one is a failure, not a skip. */
function onlyCall(): FetchCall {
  const call = fetchCalls[0];
  if (call === undefined) throw new Error('expected exactly one fetch, but none was made');
  return call;
}

/** The headers that one request carried, lower-cased. */
function requestAuthHeaders(): Record<string, string> {
  const headers = (onlyCall().init.headers ?? {}) as Record<string, string>;
  return Object.fromEntries(Object.entries(headers).map(([k, v]) => [k.toLowerCase(), v]));
}

beforeEach(installInstrumentedWorld);
afterEach(restoreWorld);

// ─── the egress contract, per provider, per path, per branch ─────────────────

describe('the key reaches the provider auth header and nowhere else', () => {
  for (const provider of PROVIDERS) {
    for (const agentic of [false, true]) {
      const path = agentic ? 'tool-use' : 'single-shot';

      it(`${provider} (${path}): the successful call carries the key in one header only`, async () => {
        responder = () => ({ status: 200, body: bodyFor(provider) });
        const outcome = await egressProbeFlat(provider, KEY, agentic);

        expect(outcome.Kind).toBe('ok');
        expect(fetchCalls).toHaveLength(1);

        // The probe genuinely carried the key — otherwise "no leak" would be
        // vacuous, which is the way a guard like this most easily rots.
        const { name, value } = authFor(provider);
        expect(requestAuthHeaders()[name]).toBe(value);

        // …and it is in exactly one header, not several.
        const bearing = Object.entries(requestAuthHeaders()).filter(([, v]) => v.includes(KEY));
        expect(bearing).toHaveLength(1);

        expectNoLeak();
      });
    }
  }
});

describe('the request itself never carries the key outside the header', () => {
  for (const provider of PROVIDERS) {
    it(`${provider}: not in the URL, not in the query string, not in the body`, async () => {
      responder = () => ({ status: 200, body: bodyFor(provider) });
      await egressProbeFlat(provider, KEY, true);

      const { url, init } = onlyCall();
      expect(url).not.toContain(KEY);
      // No query string at all. Gemini in particular accepts `?key=`, and a key
      // in a URL is logged by every proxy and history store on the path.
      expect(new URL(url).search).toBe('');
      expect(String(init.body ?? '')).not.toContain(KEY);
    });
  }
});

describe('every request goes to the chosen provider origin and no other', () => {
  it('the fetched origins are exactly the registry origins', async () => {
    const seen: string[] = [];
    for (const provider of PROVIDERS) {
      responder = () => ({ status: 200, body: bodyFor(provider) });
      fetchCalls = [];
      await egressProbeFlat(provider, KEY, true);
      expect(fetchCalls).toHaveLength(1);
      seen.push(new URL(onlyCall().url).origin);
    }
    expect(new Set(seen)).toEqual(new Set(providerOriginsFlat()));
    for (const origin of seen) expect(PROVIDER_ORIGINS).toContain(origin);
  });

  // The CSP `connect-src` is generated from src/byok/origins.ts while the
  // adapters fetch their own literals in app/Byok.fs. Both files claim in a
  // comment that they cannot drift apart; this is what makes that true.
  it('the CSP origin list and the adapter origin list are the same set', () => {
    expect(new Set(providerOriginsFlat())).toEqual(new Set(PROVIDER_ORIGINS));
  });

  it('cross-origin redirects are refused rather than followed', async () => {
    // Browsers strip `Authorization` across an origin boundary but NOT custom
    // headers, so a 30x between two allowlisted providers would replay
    // `x-api-key` to an origin the user did not choose. `redirect: 'error'`
    // closes that; the CSP cannot, because both origins are allowlisted.
    responder = () => ({ status: 200, body: bodyFor('anthropic') });
    await egressProbeFlat('anthropic', KEY, false);
    expect(onlyCall().init.redirect).toBe('error');
  });
});

describe('no telemetry, beacon, or second channel exists at all', () => {
  for (const provider of PROVIDERS) {
    it(`${provider}: exactly one network call, and it is the provider fetch`, async () => {
      responder = () => ({ status: 200, body: bodyFor(provider) });
      await egressProbeFlat(provider, KEY, true);

      expect(fetchCalls).toHaveLength(1);
      // Not one beacon, socket, XHR, tracking pixel or peer connection was even
      // constructed — the "no analytics, no error-reporting beacon" guarantee
      // enforced structurally rather than by reading the dependency list.
      expect(otherNetworkAttempts).toEqual([]);
    });
  }
});

// ─── the failure branches (where a credential actually escapes) ──────────────

describe('the failure branches surface no key', () => {
  const failures: Array<{
    name: string;
    respond: () => { status: number; body: string };
    kind: string;
  }> = [
    {
      name: 'a 401 with the vendor error shape',
      respond: () => ({
        status: 401,
        body: JSON.stringify({ error: { message: 'invalid x-api-key' } }),
      }),
      kind: 'auth',
    },
    {
      name: 'a 429 rate limit',
      respond: () => ({
        status: 429,
        body: JSON.stringify({ error: { message: 'rate limit exceeded' } }),
      }),
      kind: 'rate-limit',
    },
    {
      name: 'a 500 with an HTML body (no vendor JSON at all)',
      respond: () => ({ status: 500, body: '<html><body>Bad gateway</body></html>' }),
      kind: 'provider-fault',
    },
    {
      name: 'a 200 whose body is not JSON',
      respond: () => ({ status: 200, body: 'not json at all' }),
      kind: 'provider-fault',
    },
  ];

  for (const provider of PROVIDERS) {
    for (const failure of failures) {
      for (const agentic of [false, true]) {
        it(`${provider} (${agentic ? 'tool-use' : 'single-shot'}): ${failure.name}`, async () => {
          responder = failure.respond;
          const outcome = await egressProbeFlat(provider, KEY, agentic);

          expect(outcome.Kind).toBe(failure.kind);
          expect(outcome.Message).not.toContain(KEY);
          expect(outcome.Text).not.toContain(KEY);
          expectNoLeak();
        });
      }
    }
  }

  it('a transport exception does not carry the key into the surfaced message', async () => {
    // The transport's exception text is a string this app did not author. Node
    // and browsers do not put request headers in it today — the guarantee must
    // not depend on that staying true.
    responder = () => {
      throw new Error(`connect ECONNREFUSED (sent x-api-key: ${KEY})`);
    };
    const outcome = await egressProbeFlat('anthropic', KEY, false);

    expect(outcome.Kind).toBe('network');
    expect(outcome.Message).not.toContain(KEY);
    expect(outcome.Message).toContain('[redacted]');
    expectNoLeak();
  });

  it('a provider that echoes the rejected credential has it redacted', async () => {
    // Several vendors quote the offending key back in `error.message`. Verbatim
    // surfacing would put it in the UI, the warn port, and any host log.
    for (const provider of PROVIDERS) {
      fetchCalls = [];
      sightings = [];
      responder = () => ({
        status: 401,
        body: JSON.stringify({
          error: { message: `Incorrect API key provided: ${KEY}. Check your dashboard.` },
        }),
      });
      const outcome = await egressProbeFlat(provider, KEY, false);

      expect(outcome.Kind).toBe('auth');
      expect(outcome.Message).not.toContain(KEY);
      expect(outcome.Message).toContain('[redacted]');
      expectNoLeak();
    }
  });

  it('no key is set at all: the call never leaves and the error names no secret', async () => {
    const outcome = await egressProbeFlat('anthropic', '', false);
    expect(outcome.Kind).toBe('config');
    expect(fetchCalls).toEqual([]);
    expectNoLeak();
  });
});

// ─── the key store's own lifecycle ──────────────────────────────────────────

describe('the memory-only key store', () => {
  it('holds the key on the heap and touches no storage surface', () => {
    const store = createKeyStore();
    store.Set(KEY);
    expect(store.Has()).toBe(true);
    expect(store.Get()).toBe(KEY);
    store.Clear();
    expect(store.Has()).toBe(false);
    expect(store.Get()).toBeUndefined();
    expectNoLeak();
  });

  it('scrubOnUnload registers a pagehide scrub that drops the key', () => {
    const store = createKeyStore();
    store.Set(KEY);
    scrubOnUnload(() => store.Clear());

    // `pagehide`, not `unload`: it also fires on entry to the back/forward
    // cache, where the heap survives and `unload` never runs.
    const registered = windowListeners.filter(([type]) => type === 'pagehide');
    expect(registered).toHaveLength(1);

    const firePagehide = registered[0]?.[1];
    expect(firePagehide).toBeTypeOf('function');
    firePagehide?.();
    expect(store.Has()).toBe(false);
    expectNoLeak();
  });

  it('the key-bearing module references no storage global', () => {
    // Guarantee 1 states the store module reaches for no storage surface at
    // all. A source-level check is the only way to assert an absence that no
    // exercised path could reveal.
    const source = readFileSync(new URL('../app/Byok.fs', import.meta.url), 'utf8');
    for (const global of ['localStorage', 'sessionStorage', 'indexedDB', 'document.cookie'])
      expect(source).not.toContain(global);
  });
});

// ─── the instrumentation itself (the go-red guard) ──────────────────────────

describe('the instrumentation catches a leak (this suite’s own self-test)', () => {
  // A guard that has quietly stopped observing passes identically to a guard
  // with nothing to find. Each leak class below is run through the SAME harness
  // the real assertions use, so if a global stops being interceptable — a Node
  // change, a non-writable global, a renamed surface — this block goes red
  // rather than the whole suite going quietly, uselessly green.

  it('catches a write to localStorage', () => {
    (globalThis as never as { localStorage: Storage }).localStorage.setItem('byok', KEY);
    expect(leaks()).not.toEqual([]);
  });

  it('catches a write to sessionStorage', () => {
    (globalThis as never as { sessionStorage: Storage }).sessionStorage.setItem('byok', KEY);
    expect(leaks()).not.toEqual([]);
  });

  it('catches a cookie write', () => {
    (globalThis as never as { document: { cookie: string } }).document.cookie = `byok=${KEY}`;
    expect(leaks()).not.toEqual([]);
  });

  it('catches an IndexedDB put, however deeply nested', () => {
    const idb = (globalThis as never as { indexedDB: never }).indexedDB as never as {
      open: (n: string) => {
        transaction: (s: string) => { objectStore: (s: string) => { put: (v: string) => void } };
      };
    };
    idb.open('keys').transaction('keys').objectStore('keys').put(KEY);
    expect(leaks()).not.toEqual([]);
  });

  it('catches a console log', () => {
    console.warn('provider key', KEY);
    expect(leaks()).not.toEqual([]);
  });

  it('catches a key in a fetch URL or query string', async () => {
    responder = () => ({ status: 200, body: '{}' });
    await (globalThis as never as { fetch: typeof fetch }).fetch(
      `https://api.anthropic.com/v1/messages?key=${KEY}` as never,
      {} as never,
    );
    expect(leaks()).not.toEqual([]);
  });

  it('catches a key in a fetch body', async () => {
    responder = () => ({ status: 200, body: '{}' });
    await (globalThis as never as { fetch: typeof fetch }).fetch(
      'https://api.anthropic.com' as never,
      {
        body: JSON.stringify({ apiKey: KEY }),
      } as never,
    );
    expect(leaks()).not.toEqual([]);
  });

  it('catches a key in a non-auth header', async () => {
    responder = () => ({ status: 200, body: '{}' });
    await (globalThis as never as { fetch: typeof fetch }).fetch(
      'https://api.anthropic.com' as never,
      {
        headers: { 'x-debug-key': KEY },
      } as never,
    );
    expect(leaks()).not.toEqual([]);
  });

  it('catches a telemetry beacon and any second network channel', () => {
    (
      globalThis as never as { navigator: { sendBeacon: (u: string, b: string) => void } }
    ).navigator.sendBeacon('https://telemetry.example', KEY);
    expect(leaks()).not.toEqual([]);
    expect(otherNetworkAttempts).not.toEqual([]);
  });

  it('does NOT flag the sanctioned auth header (or the guard would be untrippable)', async () => {
    responder = () => ({ status: 200, body: bodyFor('anthropic') });
    await egressProbeFlat('anthropic', KEY, false);
    expect(leaks()).toEqual([]);
  });
});
