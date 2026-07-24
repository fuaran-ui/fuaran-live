// Phase 324, tasks 5 + 6 – the ephemeral-by-construction credential store + the
// schema-only privacy config. Proves: memory + sessionStorage backings only (no
// disk path exists), purge clears exactly the portal's keys, and the privacy
// toggle threads into a QueryRequest's schemaOnly flag.

import { describe, it, expect } from 'vitest';
import {
  memoryStore,
  sessionStore,
  EphemeralCredentials,
  defaultPrivacy,
  withPrivacy,
  type SessionStorageLike,
} from '../src/query-portal/ephemeral';

/** A fake sessionStorage (no disk) that also holds an unrelated host key. */
function fakeSession(): SessionStorageLike {
  const map = new Map<string, string>();
  map.set('host:theme', 'dark'); // a non-portal key purge must NOT touch
  return {
    getItem: (k) => map.get(k) ?? null,
    setItem: (k, v) => void map.set(k, v),
    removeItem: (k) => void map.delete(k),
    get length() {
      return map.size;
    },
    key: (i) => [...map.keys()][i] ?? null,
  };
}

describe('memoryStore', () => {
  it('holds and purges values, reporting backing = memory', () => {
    const s = memoryStore();
    s.set('a', '1');
    expect(s.get('a')).toBe('1');
    expect(s.keysHeld()).toContain('a');
    s.purge();
    expect(s.get('a')).toBeNull();
    expect(s.backing).toBe('memory');
  });
});

describe('sessionStore', () => {
  it('namespaces keys and purges only the portal keys (leaves host keys intact)', () => {
    const backing = fakeSession();
    const s = sessionStore(backing);
    s.set('llm-key', 'sk-123');
    expect(s.get('llm-key')).toBe('sk-123');
    expect(s.keysHeld()).toEqual(['llm-key']);
    s.purge();
    expect(s.get('llm-key')).toBeNull();
    // The unrelated host key survives the purge.
    expect(backing.getItem('host:theme')).toBe('dark');
  });
});

describe('EphemeralCredentials', () => {
  it('holds the LLM key + DB token in memory and purges them', () => {
    const creds = new EphemeralCredentials();
    creds.setLlmKey('sk-abc');
    creds.setDbToken('ro-token');
    expect(creds.current()).toEqual({ llmKey: 'sk-abc', dbToken: 'ro-token' });
    expect(creds.report()).toEqual({ backing: 'memory', secretsHeld: 2, persistsToDisk: false });
    creds.purge();
    expect(creds.current()).toEqual({ llmKey: undefined, dbToken: undefined });
    expect(creds.report().secretsHeld).toBe(0);
  });

  it('always reports persistsToDisk: false, even on a sessionStorage backing', () => {
    const creds = new EphemeralCredentials(sessionStore(fakeSession()));
    creds.setLlmKey('sk-xyz');
    expect(creds.report().backing).toBe('sessionStorage');
    expect(creds.report().persistsToDisk).toBe(false);
  });
});

describe('schema-only privacy config', () => {
  it('defaults to full-fetch (schema-only opt-in)', () => {
    expect(defaultPrivacy.schemaOnly).toBe(false);
  });
  it('threads the toggle into a QueryRequest-shaped flag', () => {
    expect(withPrivacy('select 1', { schemaOnly: true })).toEqual({
      sql: 'select 1',
      schemaOnly: true,
    });
    expect(withPrivacy('select 1', defaultPrivacy)).toEqual({ sql: 'select 1', schemaOnly: false });
  });
});
