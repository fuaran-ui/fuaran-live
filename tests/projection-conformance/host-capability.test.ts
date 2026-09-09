// The capability-manifest consumer, tested against the real corpus (Phase 1582).
//
// The Python arm's live path is the PRE-MANIFEST fallback — its pinned release
// publishes no manifest — so nothing there exercises the computed path today.
// That is exactly why these exist: a mechanism whose only proof is "it will work
// when the pin moves" is a mechanism nobody has run. Everything below runs the
// real derivation over the real fixture corpus, with synthetic manifests standing
// in for a host that has not shipped one yet.
//
// The centre of the file is the acceptance criterion of the phase, made
// executable: REMOVING A KIND FROM A MANIFEST TURNS EXACTLY ITS FIXTURES INTO
// EXPECTED-UNMODELLED, AND NOTHING ELSE. Stated the other way round, it is the
// property that makes a computed quarantine worth more than a hand-written one.

import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

import {
  MANIFEST_FORMAT,
  classify,
  computeExpectedUnmodelled,
  deriveTokens,
  isClaimed,
  parseManifest,
  resolveManifest,
  staleResiduals,
  type CapabilityManifest,
  type Idl,
} from './host-capability';

const here = dirname(fileURLToPath(import.meta.url));
const corpusDir = resolve(here, '../..', '../wire-format-fixtures');

const manifest = JSON.parse(readFileSync(resolve(corpusDir, 'manifest.json'), 'utf8')) as {
  fixtures: { id: string; kind: string; inputFile: string }[];
};
const nodeFixtures = manifest.fixtures.filter((f) => f.kind === 'node-round-trip');

const idlPath = resolve(corpusDir, 'idl.json');
const idl = existsSync(idlPath) ? (JSON.parse(readFileSync(idlPath, 'utf8')) as Idl) : undefined;

const tokensByFixture = new Map<string, ReadonlySet<string>>(
  idl === undefined
    ? []
    : nodeFixtures.map((f) => [
        f.id,
        deriveTokens(JSON.parse(readFileSync(resolve(corpusDir, f.inputFile), 'utf8')), idl),
      ]),
);

/** Every token the corpus exercises, whatever its family. */
const universe = new Set<string>([...tokensByFixture.values()].flatMap((s) => [...s]));

/**
 * A synthetic host that models EVERYTHING the corpus exercises, with every family
 * covered and no scope narrowing. The baseline against which a removal is a
 * measurement rather than a guess: its expected-unmodelled set must be empty, and
 * if it is not, every later assertion here is measuring the wrong thing.
 */
const omniscient = (tokens: Iterable<string>): CapabilityManifest => ({
  $manifest: MANIFEST_FORMAT,
  host: 'synthetic',
  hostVersion: '1.0.0',
  corpusAuthority: null,
  generator: 'tests/projection-conformance/host-capability.test.ts',
  families: {
    kinds: { covered: true },
    kindFields: { covered: true },
    unionCases: { covered: true },
    caseFields: { covered: true },
    recordFields: { covered: true },
    hostedCases: { covered: true },
  },
  tokens: [...tokens].sort(),
});

describe('construct-token grammar (WIRE_FORMAT §27.2)', () => {
  it('tells the six shapes apart by the alternation of case', () => {
    expect(classify('Kind.Drawing')).toEqual({ family: 'kinds' });
    expect(classify('Kind.Chart.annotations')).toEqual({ family: 'kindFields' });
    expect(classify('Binding.Expr')).toEqual({ family: 'unionCases', scopeKey: 'Binding' });
    expect(classify('Binding.Transform.params')).toEqual({
      family: 'caseFields',
      scopeKey: 'Binding',
    });
    expect(classify('Accessibility.liveRegion')).toEqual({
      family: 'recordFields',
      scopeKey: 'Accessibility',
    });
    expect(classify('Binding.Transform.source.State')).toEqual({
      family: 'hostedCases',
      scopeKey: 'Binding.Transform.source',
    });
    // A hosted payload's own vocabulary need not be PascalCase — the compute
    // transforms spell theirs camelCase — and the shape is still unambiguous,
    // because a hosted token is one segment longer than the field it extends.
    expect(classify('Binding.Transform.pipeline.groupBy')).toEqual({
      family: 'hostedCases',
      scopeKey: 'Binding.Transform.pipeline',
    });
    expect(classify('Accessibility.role.Button')).toEqual({
      family: 'hostedCases',
      scopeKey: 'Accessibility.role',
    });
  });

  it('refuses what it cannot place', () => {
    expect(classify('Badge')).toBeUndefined();
    expect(classify('a.b.c.d.e')).toBeUndefined();
    expect(classify('Binding.')).toBeUndefined();
  });
});

describe('manifest decoding (WIRE_FORMAT §27.3)', () => {
  const valid = omniscient(['Kind.Badge']);

  it('refuses an unrecognised $manifest outright', () => {
    // Rather than reading the members it happens to know: a later format may mean
    // something different by the same field names, and a consumer that reads it
    // anyway has turned the version marker into decoration.
    const result = parseManifest({ ...valid, $manifest: 'fuaran.host-capability/2' });
    expect('error' in result && result.error).toContain('fuaran.host-capability/2');
  });

  it('refuses a document missing the members a claim rests on', () => {
    for (const key of ['host', 'hostVersion', 'generator', 'families', 'tokens'] as const) {
      const partial = { ...valid } as Record<string, unknown>;
      delete partial[key];
      expect('error' in parseManifest(partial), `a manifest with no ${key} must be refused`).toBe(
        true,
      );
    }
  });

  it('round-trips a well-formed document', () => {
    const parsed = parseManifest(JSON.parse(JSON.stringify(valid)));
    expect('error' in parsed).toBe(false);
    expect((parsed as CapabilityManifest).tokens).toEqual(['Kind.Badge']);
  });
});

describe('the version binding (WIRE_FORMAT §27.4 rule 5)', () => {
  it('refuses a manifest that describes a different release, and names both', () => {
    // The live shape of this: the arm executes a pinned PyPI release while the
    // sibling working tree is ahead of it. The useful sentence is never "no
    // manifest" — it is which two versions disagreed.
    const resolution = resolveManifest({
      host: 'fuaran-py',
      hostVersion: '0.4.0',
      capabilityManifest: { ...omniscient([]), host: 'fuaran-py', hostVersion: '0.5.0' },
    });
    expect(resolution.mode).toBe('fallback');
    if (resolution.mode !== 'fallback') return;
    expect(resolution.reason).toContain('0.5.0');
    expect(resolution.reason).toContain('0.4.0');
  });

  it('binds a manifest that describes the executing release', () => {
    const resolution = resolveManifest({
      host: 'fuaran-py',
      hostVersion: '0.4.0',
      capabilityManifest: { ...omniscient([]), host: 'fuaran-py', hostVersion: '0.4.0' },
    });
    expect(resolution.mode).toBe('computed');
  });

  it('falls back with the host’s own reason when no manifest is published', () => {
    const resolution = resolveManifest({
      host: 'fuaran-py',
      hostVersion: '0.4.0',
      capabilityManifest: null,
      capabilityError: "ImportError: cannot import name 'host_capability'",
    });
    expect(resolution.mode).toBe('fallback');
    if (resolution.mode !== 'fallback') return;
    expect(resolution.reason).toContain('0.4.0');
    expect(resolution.reason).toContain('host_capability');
  });

  it('refuses a manifest it cannot bind to any observed version', () => {
    const resolution = resolveManifest({ capabilityManifest: omniscient([]) });
    expect(resolution.mode).toBe('fallback');
  });
});

describe.skipIf(idl === undefined)('corpus-minus-manifest (WIRE_FORMAT §27.4 rule 1)', () => {
  it('derives a non-trivial vocabulary from the corpus', () => {
    // Guards every measurement below: a walk that silently derived nothing would
    // make "removing a kind changes exactly its fixtures" pass vacuously.
    expect(nodeFixtures.length).toBeGreaterThanOrEqual(70);
    expect(universe.size).toBeGreaterThan(100);
    expect([...universe].filter((t) => t.startsWith('Kind.')).length).toBeGreaterThan(40);
  });

  it('a host that models everything the corpus exercises holds nothing aside', () => {
    expect([...computeExpectedUnmodelled(omniscient(universe), tokensByFixture).keys()]).toEqual(
      [],
    );
  });

  it('removing a KIND turns exactly that kind’s fixtures into expected-unmodelled', () => {
    // The phase's acceptance criterion, over the real corpus. "Exactly" is the
    // load-bearing word in both directions: no fixture using the kind escapes,
    // and no fixture that does not use it is caught.
    const removed = 'Kind.Badge';
    const usingBadge = [...tokensByFixture]
      .filter(([, tokens]) => tokens.has(removed))
      .map(([id]) => id)
      .sort();
    expect(usingBadge.length).toBeGreaterThan(0);

    const without = omniscient([...universe].filter((t) => t !== removed));
    const expected = computeExpectedUnmodelled(without, tokensByFixture);
    expect([...expected.keys()].sort()).toEqual(usingBadge);
    for (const missing of expected.values()) expect(missing).toEqual([removed]);
  });

  it('removing a UNION CASE turns exactly its fixtures into expected-unmodelled', () => {
    // The same property one family over — the shape that carries this host's real
    // standing gap, `Binding.Expr`.
    const removed = 'Binding.Static';
    const users = [...tokensByFixture]
      .filter(([, tokens]) => tokens.has(removed))
      .map(([id]) => id)
      .sort();
    expect(users.length).toBeGreaterThan(0);

    const without = omniscient([...universe].filter((t) => t !== removed));
    expect([...computeExpectedUnmodelled(without, tokensByFixture).keys()].sort()).toEqual(users);
  });

  it('silence in an UNCLAIMED family holds nothing aside (§27.4 rule 2)', () => {
    // The rule that makes "declare it not covered rather than guess" safe to obey.
    // A manifest that declares only kinds says nothing whatever about union cases
    // — so removing every one of them must change nothing at all.
    const kindsOnly: CapabilityManifest = {
      ...omniscient(
        [...universe].filter((t) => t.startsWith('Kind.') && t.split('.').length === 2),
      ),
      families: {
        kinds: { covered: true },
        unionCases: { covered: false, reason: 'this synthetic host declines to enumerate them' },
      },
    };
    expect([...computeExpectedUnmodelled(kindsOnly, tokensByFixture).keys()]).toEqual([]);
    expect(isClaimed(kindsOnly, 'Binding.Expr')).toBe(false);
    expect(isClaimed(kindsOnly, 'Kind.Badge')).toBe(true);
  });

  it('a family narrowed by scope claims only the keys it lists (§27.3)', () => {
    const scoped: CapabilityManifest = {
      ...omniscient([...universe].filter((t) => !t.startsWith('Binding.'))),
      families: {
        kinds: { covered: true },
        unionCases: { covered: true, scope: ['TextSource'], reason: 'only this one is derivable' },
      },
    };
    expect(isClaimed(scoped, 'Binding.Expr')).toBe(false);
    expect(isClaimed(scoped, 'TextSource.Bound')).toBe(true);
    // Every Binding token is now unclaimed, so dropping them all holds nothing
    // aside; the TextSource ones are claimed and present, so nothing there either.
    expect([...computeExpectedUnmodelled(scoped, tokensByFixture).keys()]).toEqual([]);
  });
});

describe('the declared residual (WIRE_FORMAT §27.4 rule 6)', () => {
  const residual = {
    id: 'badge-transform-live',
    family: 'hostedCases',
    scopeKey: 'Binding.Transform.source',
  } as const;

  it('stands while the family it names is unclaimed', () => {
    const declining = {
      ...omniscient([]),
      families: {
        kinds: { covered: true },
        hostedCases: { covered: false, reason: 'not derivable from this host’s model' },
      },
    } as CapabilityManifest;
    expect(staleResiduals(declining, [residual])).toEqual([]);
  });

  it('is reported stale the moment the manifest claims that family', () => {
    const claiming = {
      ...omniscient([]),
      families: {
        kinds: { covered: true },
        hostedCases: { covered: true, scope: ['Binding.Transform.source'] },
      },
    } as CapabilityManifest;
    const stale = staleResiduals(claiming, [residual]);
    expect(stale).toHaveLength(1);
    expect(stale[0]).toContain('badge-transform-live');
    expect(stale[0]).toContain('Binding.Transform.source');
  });

  it('is not reported stale by a claim at a DIFFERENT scope key', () => {
    // The narrowing has to bite per key, or a host that covered one hosted slot
    // would retire every residual standing on any of them.
    const elsewhere = {
      ...omniscient([]),
      families: {
        kinds: { covered: true },
        hostedCases: { covered: true, scope: ['Binding.Transform.pipeline'] },
      },
    } as CapabilityManifest;
    expect(staleResiduals(elsewhere, [residual])).toEqual([]);
  });
});
