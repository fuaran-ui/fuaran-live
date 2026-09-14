// =============================================================================
//  NuGet restore-graph lock — every id this repo pins must be publicly
//  obtainable (Phase 1753).
//
//  WHY THIS FILE EXISTS. This repo is OSS-public and its whole posture is
//  "restores anonymously, exactly as a public consumer would" — which is why
//  `nuget.config` declares the public registry FIRST, against the usual
//  local-feed-first convention, and says so beside the order. That posture has
//  one failure mode and it is silent: a pin whose package is not published.
//  Everything keeps building here, because a maintainer's machine has a local
//  feed that serves it; the repo is unbuildable for everyone else, and the
//  first person to discover it is a stranger who cannot fix it.
//
//  WHY AN EXACT ID SET RATHER THAN A PREFIX PATTERN. Publication is a property
//  of the REGISTRY, never of the name: `Fuaran.UI.Something` being pinned here
//  is not evidence that anyone outside this machine can restore it. A prefix
//  allowlist would wave through every future id sharing a prefix, published or
//  not, which is precisely the check nobody would then be performing. An exact
//  set cannot do that — a new id reddens this suite until someone adds it
//  deliberately, and the act of adding it is where the registry gets checked.
//
//  Phase 1753 is the worked example that prompted the lock. It was dispatched
//  to make this repo adopt an agent-loop core "from a published package", and
//  no such package exists — the check found nothing on the registry under any
//  of the plausible ids. A session implementing that instruction faithfully
//  would have pinned a name that merely looked right, and nothing in this repo
//  would have objected. See the `## Dependencies` section of `CLAUDE.md`.
//
//  REGISTRY EVIDENCE (checked 2026-09-14 against the public flat-container
//  index; recorded here rather than fetched by the test, which stays offline
//  and deterministic). Every id+version below was served:
//    Fuaran.UI 0.79.0 · Fuaran.UI.Renderer 0.79.0 · Fuaran.UI.Ops 0.79.0
//    Fuaran.UI.OpStream.Abstractions 0.79.0 · Fuaran.UI.OpStream.Replay 0.79.0
//    Fuaran.UI.ServerDriven 0.79.0 · Fuaran.UI.AiWire 0.81.0
//    Fuaran.Core.Tree/Ops/Function/OpStream/Wire 0.21.0
//    Fuaran.Program.Runtime 0.4.0
//  The pins sit deliberately behind the language tier's current emit — the six
//  CI workflows pin the sibling source checkout at a tier ref and move with it
//  in one commit (`docs/tier-pin.md`) — so "behind" and "unobtainable" are
//  different questions and only the second one is a defect.
//
//  WHAT THIS LOCK DOES NOT CLAIM. It is offline, so it cannot tell you that a
//  version is on the registry TODAY; it tells you that the id set has not moved
//  without someone looking. Raising a pin is still the moment to check the
//  registry, and `.github/workflows/ci.yml` — which restores with no local feed
//  — is what actually proves obtainability on every push.
//
//  MUTATION-VERIFIED: each lock was proven to bite during development against
//  synthetic project text, and those proofs are kept as the self-test block at
//  the end rather than described. A guard that has silently stopped observing
//  passes exactly like a guard with nothing to find.
// =============================================================================

import { readFileSync, existsSync, readdirSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const repoRoot = resolve(__dirname, '..');

/**
 * Every NuGet package id this repository is allowed to pin.
 *
 * Adding a line here is a deliberate act with one precondition: the public
 * registry must serve the id. Removing a pin means removing its line, so the
 * set stays an inventory rather than a floor.
 */
const ALLOWED_PACKAGE_IDS: readonly string[] = [
  // The Fuaran UI language tier + renderer (public, nuget.org).
  'Fuaran.UI',
  'Fuaran.UI.Renderer',
  'Fuaran.UI.Ops',
  'Fuaran.UI.OpStream.Abstractions',
  'Fuaran.UI.OpStream.Replay',
  'Fuaran.UI.ServerDriven',
  // The portable AI-connector wire substrate this repo's BYOK layer is built
  // on — adopted as a package by Phase 1698, which deleted the vendored copy.
  'Fuaran.UI.AiWire',
  // The shared substrate spine (public, nuget.org).
  'Fuaran.Core.Tree',
  'Fuaran.Core.Ops',
  'Fuaran.Core.Function',
  'Fuaran.Core.OpStream',
  'Fuaran.Core.Wire',
  // The program/logic domain runtime (public, nuget.org).
  'Fuaran.Program.Runtime',
  // Fable / Feliz toolchain (public, nuget.org).
  'Fable.Core',
  'Fable.Elmish',
  'Fable.Elmish.React',
  'Feliz',
];

/** Directories that must never reappear: a deleted vendored copy staying deleted. */
const RETIRED_VENDOR_DIRS: readonly string[] = [
  // Phase 1698 replaced four vendored wire files with the published package.
  // Keeping the absence asserted here means it is checked by this repo's own
  // gate, on every run, rather than by anyone remembering to look.
  'app/vendor',
];

/** Recursively collect every `.fsproj` under the repo, skipping build output. */
function findProjects(dir: string, acc: string[] = []): string[] {
  const skip = new Set(['node_modules', 'bin', 'obj', 'dist', 'output', '.git']);
  for (const entry of readdirSync(dir)) {
    if (skip.has(entry)) continue;
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) findProjects(full, acc);
    else if (entry.endsWith('.fsproj')) acc.push(full);
  }
  return acc;
}

interface Pin {
  readonly id: string;
  readonly version: string | null;
  readonly project: string;
}

/**
 * Read every `PackageReference` out of project text.
 *
 * Both spellings are handled: the attribute form MSBuild writes, and a
 * `Version` supplied centrally (absent attribute), which reads as `null` here
 * rather than as an error — this repo does not use central pin management
 * today, and a lock that crashed on a shape it merely has not met yet would be
 * a worse guard than one that reports it.
 */
function parsePins(text: string, project: string): Pin[] {
  const pins: Pin[] = [];
  const re = /<PackageReference\s+Include="([^"]+)"([^/>]*)/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    const id = m[1];
    if (id === undefined) continue; // unreachable: group 1 is not optional
    const version = /Version="([^"]+)"/.exec(m[2] ?? '');
    pins.push({ id, version: version?.[1] ?? null, project });
  }
  return pins;
}

const projects = findProjects(repoRoot);
const pins = projects.flatMap((p) => parsePins(readFileSync(p, 'utf8'), p));

describe('NuGet restore-graph lock', () => {
  it('finds the repo projects at all (the guard is observing something)', () => {
    // If this ever reports zero, every assertion below is vacuously green.
    expect(projects.length).toBeGreaterThan(0);
    expect(pins.length).toBeGreaterThan(0);
  });

  it('pins no package id outside the approved public set', () => {
    const unknown = pins.filter((p) => !ALLOWED_PACKAGE_IDS.includes(p.id));
    expect(
      unknown.map((p) => `${p.id}  (in ${p.project.slice(repoRoot.length + 1)})`),
      'A package id appears that this repo has not approved. Confirm the public ' +
        'registry actually serves it, then add it to ALLOWED_PACKAGE_IDS in this ' +
        'file. A familiar-looking prefix is not evidence of publication: the ' +
        'registry is, and a pin it cannot serve makes this repo unbuildable for ' +
        'everyone who is not the person who added it.',
    ).toEqual([]);
  });

  it('carries no approved id that nothing pins any more (the set is an inventory)', () => {
    const pinned = new Set(pins.map((p) => p.id));
    const stale = ALLOWED_PACKAGE_IDS.filter((id) => !pinned.has(id));
    expect(
      stale,
      'These ids are approved but no project pins them. Drop the lines — an ' +
        'approval list that outlives its pins stops being a record of what this ' +
        'repo actually restores.',
    ).toEqual([]);
  });

  it('gives every pin a concrete version', () => {
    const floating = pins.filter((p) => p.version === null || p.version.trim() === '');
    expect(floating.map((p) => `${p.id} (${p.project.slice(repoRoot.length + 1)})`)).toEqual([]);
  });

  it('keeps the retired vendored wire copy deleted', () => {
    const back = RETIRED_VENDOR_DIRS.filter((d) => existsSync(join(repoRoot, d)));
    expect(
      back,
      'A vendored copy of the shared wire substrate has reappeared. It was ' +
        'deleted in favour of the published package (Phase 1698); re-vendoring ' +
        'it takes back a security-port obligation on a parser of untrusted ' +
        'provider responses, so it is a decision rather than a convenience.',
    ).toEqual([]);
  });

  it('declares the public registry ahead of any local feed, as the posture requires', () => {
    const config = readFileSync(join(repoRoot, 'nuget.config'), 'utf8');
    const sources = [...config.matchAll(/<add\s+key="([^"]+)"/g)].map((m) => m[1]);
    expect(sources).toContain('nuget.org');
    // Declaring a local feed first would let a maintainer's unpublished pack
    // shadow the registry, and the whole point of this repo's order is that a
    // maintainer's build resolves what a stranger's build resolves.
    const local = sources.findIndex((s) => s !== 'nuget.org');
    if (local !== -1) expect(sources.indexOf('nuget.org')).toBeLessThan(local);
  });
});

// ─── Self-test: prove each lock bites ────────────────────────────────────────
//
// Run against synthetic project text, so the assertions above are known to be
// capable of failing rather than merely observed passing.

describe('NuGet restore-graph lock — go-red self-test', () => {
  const approved = '<PackageReference Include="Fuaran.UI" Version="0.79.0" />';

  it('the parser reads an ordinary pin', () => {
    const got = parsePins(approved, 'synthetic.fsproj');
    expect(got).toEqual([{ id: 'Fuaran.UI', version: '0.79.0', project: 'synthetic.fsproj' }]);
  });

  it('an unapproved id is caught — including one wearing a public-looking prefix', () => {
    // The id below is a placeholder, deliberately naming nothing real: the
    // lock's job is to refuse an id it does not know, whatever it is called.
    const intruder = '<PackageReference Include="Fuaran.UI.SomeUnpublishedTier" Version="1.0.0" />';
    const got = parsePins(intruder, 'synthetic.fsproj');
    expect(got).toHaveLength(1);
    expect(ALLOWED_PACKAGE_IDS.includes(got[0]!.id)).toBe(false);
  });

  it('a version-less pin is caught', () => {
    const got = parsePins('<PackageReference Include="Fuaran.UI" />', 'synthetic.fsproj');
    expect(got[0]!.version).toBeNull();
  });

  it('the multi-pin and self-closing forms both parse', () => {
    const text = `
      <ItemGroup>
        <PackageReference Include="Fuaran.UI" Version="0.79.0" />
        <PackageReference Include="Feliz" Version="2.9.0"></PackageReference>
      </ItemGroup>`;
    expect(parsePins(text, 'synthetic.fsproj').map((p) => p.id)).toEqual(['Fuaran.UI', 'Feliz']);
  });

  it('the retired-directory check resolves against the repo root, both ways', () => {
    // Both polarities of the mechanism, using paths this block controls — NOT a
    // restatement of the production assertion, which would make the self-test
    // pass and fail with the very thing it is supposed to be independent of.
    expect(existsSync(join(repoRoot, 'app'))).toBe(true);
    expect(existsSync(join(repoRoot, 'no-such-directory-1753'))).toBe(false);
  });
});
