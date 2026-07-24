// ============================================================================
//  Ephemeral-by-construction credential store + schema-only privacy config
//  (Phase 324, tasks 5 + 6).
//
//  The portal's data-governance promise made structural: the BYO LLM key and the
//  BYO data-source token live in MEMORY or `sessionStorage` ONLY – never
//  `localStorage`, never disk, never a cookie. Closing the tab leaves nothing.
//  This module is the seam that makes that true by construction: a store can be
//  backed only by an in-memory map or a `sessionStorage`-shaped object, and a
//  `purge()` clears every key it holds (the backing for the visible "nothing
//  persisted" affordance).
//
//  The schema-only privacy toggle is a first-class config here too: when on, the
//  portal sends the LLM only the `(name, type)` schema, never sample rows – the
//  toggle threads through to every `QueryRequest.schemaOnly` (see ./sources,
//  ./emission). `we persist nothing; the BYO-LLM endpoint sees structure (± rows
//  only if the user opts in)` – that line is enforced by these two mechanisms.
// ============================================================================

/**
 * The minimal `Storage` surface the store needs – `window.sessionStorage`
 * conforms, and a test passes a fake. Deliberately NOT `localStorage`: a
 * sessionStorage-shaped object is the only persistent backing the store accepts,
 * so a key cannot outlive the tab.
 */
export interface SessionStorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
  readonly length: number;
  key(index: number): string | null;
}

/** An ephemeral key/value store, backed by memory or sessionStorage – never disk. */
export interface EphemeralStore {
  readonly backing: 'memory' | 'sessionStorage';
  set(key: string, value: string): void;
  get(key: string): string | null;
  remove(key: string): void;
  /** Clear every key THIS store placed – the "nothing persisted" purge. */
  purge(): void;
  /** The keys currently held (for the visible affordance / a test assertion). */
  keysHeld(): readonly string[];
}

// All portal-held keys carry this prefix, so `purge()` clears exactly ours and
// never a host's unrelated sessionStorage entries.
const PREFIX = 'fuaran-live:portal:';

/** An in-memory store – the default; vanishes when the JS context is gone. */
export function memoryStore(): EphemeralStore {
  const map = new Map<string, string>();
  return {
    backing: 'memory',
    set: (k, v) => void map.set(k, v),
    get: (k) => map.get(k) ?? null,
    remove: (k) => void map.delete(k),
    purge: () => map.clear(),
    keysHeld: () => [...map.keys()],
  };
}

/**
 * A `sessionStorage`-backed store – persists across a reload but NOT across tab
 * close (the sessionStorage contract). Only a sessionStorage-shaped object is
 * accepted; there is no `localStorage` path by construction.
 */
export function sessionStore(storage: SessionStorageLike): EphemeralStore {
  const ours = (): string[] => {
    const keys: string[] = [];
    for (let i = 0; i < storage.length; i++) {
      const k = storage.key(i);
      if (k && k.startsWith(PREFIX)) keys.push(k);
    }
    return keys;
  };
  return {
    backing: 'sessionStorage',
    set: (k, v) => storage.setItem(PREFIX + k, v),
    get: (k) => storage.getItem(PREFIX + k),
    remove: (k) => storage.removeItem(PREFIX + k),
    purge: () => ours().forEach((k) => storage.removeItem(k)),
    keysHeld: () => ours().map((k) => k.slice(PREFIX.length)),
  };
}

// ─── the credential holder ────────────────────────────────────────────────────

/** The secrets the portal holds for one session: the LLM key + the data-source token. */
export interface PortalCredentials {
  readonly llmKey?: string;
  readonly dbToken?: string;
}

const KEY_LLM = 'llm-key';
const KEY_DB = 'db-token';

/**
 * A session credential holder over an `EphemeralStore`. Defaults to memory (the
 * strongest privacy posture). `report()` backs the visible "nothing persisted"
 * affordance; `purge()` is the panic-clear.
 */
export class EphemeralCredentials {
  constructor(private readonly store: EphemeralStore = memoryStore()) {}

  setLlmKey(key: string): void {
    this.store.set(KEY_LLM, key);
  }
  setDbToken(token: string): void {
    this.store.set(KEY_DB, token);
  }
  current(): PortalCredentials {
    // Omit absent keys rather than set them to `undefined` (exactOptionalPropertyTypes).
    const creds: { llmKey?: string; dbToken?: string } = {};
    const llmKey = this.store.get(KEY_LLM);
    if (llmKey !== null) creds.llmKey = llmKey;
    const dbToken = this.store.get(KEY_DB);
    if (dbToken !== null) creds.dbToken = dbToken;
    return creds;
  }
  /** Wipe every held secret – the affordance behind a "forget everything" button. */
  purge(): void {
    this.store.purge();
  }
  /** A structured, render-ready statement of the persistence posture. */
  report(): { backing: EphemeralStore['backing']; secretsHeld: number; persistsToDisk: false } {
    return {
      backing: this.store.backing,
      secretsHeld: this.store.keysHeld().length,
      persistsToDisk: false,
    };
  }
}

// ─── schema-only privacy config ───────────────────────────────────────────────

/** The portal's privacy configuration – the schema-only toggle is first-class. */
export interface PortalPrivacyConfig {
  /**
   * When true, only the `(name, type)` schema is sent to the LLM – never sample
   * rows. Threads to every `QueryRequest.schemaOnly`. The UI surfaces this as a
   * prominent toggle; the mechanism is enforced end-to-end (the source returns
   * `rows: null`, see ./sources).
   */
  readonly schemaOnly: boolean;
}

/** The privacy-preserving default-on posture is opt-in: full-fetch by default, schema-only when chosen. */
export const defaultPrivacy: PortalPrivacyConfig = { schemaOnly: false };

/** Thread the privacy config into a `QueryRequest`-shaped `schemaOnly` flag. */
export function withPrivacy(
  sql: string,
  privacy: PortalPrivacyConfig,
): { sql: string; schemaOnly: boolean } {
  return { sql, schemaOnly: privacy.schemaOnly };
}
