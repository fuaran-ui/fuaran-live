// The tier this gate certifies, the corpus it certifies against, and the AHEAD grade between them
// (WIRE_FORMAT.md 11.3, Phase 2301).
//
// This repository certifies an UNPINNED corpus against PINNED host releases (docs/tier-pin.md): the
// projector and all three arms compile against the `Fuaran.UI` release `app/FuaranLive.fsproj` pins.
// A corpus fixture carries `since`, the reference tier release that first specifies it. A fixture
// whose `since` post-dates the pinned tier is AHEAD: the pinned release cannot know it, so it is not
// run, and it is registered as a SKIPPED test whose title names it and the two versions — counted by
// scripts/conformance-report.mjs, never silently passed and never red. Every fixture at or below the
// tier must pass. A fixture with no `since` (a corpus older than the field) is never AHEAD.

import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '../..');

/** The corpus FUARAN_WIRE_FIXTURES names (a set-but-missing path is refused), else the sibling. */
export const corpusDir = ((): string => {
  const named = (process.env.FUARAN_WIRE_FIXTURES ?? '').trim();
  if (named === '') return resolve(repoRoot, '../wire-format-fixtures');
  if (!existsSync(resolve(named, 'manifest.json')))
    throw new Error(
      `FUARAN_WIRE_FIXTURES names ${named}, which holds no manifest.json — refusing to fall back`,
    );
  return resolve(named);
})();

/**
 * The `Fuaran.UI.*` release this gate compiles against: the version `app/FuaranLive.fsproj` pins,
 * read rather than restated so the pin moves in one place.
 */
export const pinnedTierVersion = ((): string => {
  const fsproj = readFileSync(resolve(repoRoot, 'app/FuaranLive.fsproj'), 'utf8');
  const m = /<PackageReference\s+Include="Fuaran\.UI"\s+Version="([^"]+)"/.exec(fsproj);
  if (m === null) throw new Error('app/FuaranLive.fsproj declares no Fuaran.UI PackageReference');
  return m[1]!;
})();

export interface ManifestEntry {
  readonly id: string;
  readonly kind: string;
  readonly inputFile: string;
  readonly since?: string;
}

const parts = (v: string): number[] | null => {
  const m = /^(\d+)\.(\d+)\.(\d+)/.exec(v);
  return m === null ? null : [Number(m[1]), Number(m[2]), Number(m[3])];
};

/** True when `since` names a release later than `tier`; false when either is unreadable. */
export const isAhead = (since: string | undefined, tier: string): boolean => {
  const s = since === undefined ? null : parts(since);
  const t = parts(tier);
  if (s === null || t === null) return false;
  for (let i = 0; i < 3; i++) if (s[i] !== t[i]) return s[i]! > t[i]!;
  return false;
};

/** The title an AHEAD fixture's skipped test carries; the report counts by its marker. */
export const AHEAD_MARKER = 'AHEAD of tier';
export const aheadTitle = (f: ManifestEntry): string =>
  `${f.id} ${AHEAD_MARKER} ${pinnedTierVersion} (since ${f.since})`;

/** The corpus's node round-trip fixtures, split into the ones this tier certifies and the AHEAD. */
export const nodeFixtures = ((): { certified: ManifestEntry[]; ahead: ManifestEntry[] } => {
  const manifest = JSON.parse(readFileSync(resolve(corpusDir, 'manifest.json'), 'utf8')) as {
    fixtures: ManifestEntry[];
  };
  const nodes = manifest.fixtures.filter((f) => f.kind === 'node-round-trip');
  return {
    certified: nodes.filter((f) => !isAhead(f.since, pinnedTierVersion)),
    ahead: nodes.filter((f) => isAhead(f.since, pinnedTierVersion)),
  };
})();
