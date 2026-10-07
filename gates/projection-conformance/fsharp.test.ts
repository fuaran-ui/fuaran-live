// Codegen-conformance — F# arm (one generated file, one compile, one process).
//
// For every Node fixture in the workspace wire-format-fixtures/ corpus:
//   1. project the canonical wire JSON to `Fuaran.UI` smart-constructor source
//      via the F#/Fable projector (app/Projection.fs, Fable-compiled to
//      app/output/Projection.js);
//   2. write EVERY projection into ONE generated F# file, COMPILE IT ONCE
//      against the pinned `Fuaran.UI` package, and EXECUTE it — so each fixture
//      is both type-checked by the real compiler and run against the real
//      assembly;
//   3. re-encode via `CanonicalJson.encodeNode` and assert the JSON is
//      byte-identical to the fixture.
//
// The two sibling arms' shape, with one structural difference the host forces:
// the TypeScript arm evaluates its source in-process and the Python arm execs it
// in one interpreter, but F# has no interpreter in this toolchain worth using —
// `dotnet fsi`'s `#r "nuget:"` resolution is slower than a project restore and
// gives the harness no control over the diagnostics it must silence. So this arm
// COMPILES, and the compile is the point rather than a cost: it is the only arm
// whose host checks the emission's TYPES as well as its bytes. A projected record
// that omits a field, or names a union case with the wrong arity, fails here
// before it can produce any bytes at all — which is a class of drift the other
// two arms structurally cannot see.
//
// Why ONE file for the whole corpus, and not one per fixture: a per-fixture
// compile is 221 restores and 221 compiles, which is minutes rather than seconds,
// and it buys nothing — a diagnostic is attributed back to its fixture by LINE
// (`fixturesInErrors` below), which is what the compiler actually reports.
//
// The generated project lives in a GITIGNORED scratch directory. That is
// deliberate: a gate run must leave the tree clean, or the fold's gate-record
// reuse sees `tree-dirty` and pays for a second full run.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/,
// and a .NET SDK on PATH (the same one the Fable compile already needed).

import { spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

import { beforeAll, describe, expect, it } from 'vitest';

// Fable-generated JS — no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { projectFSharpExpr } from '../../app/output/Projection.js';

import { entriesFor, registerQuarantineChecks, type ConstructVerdict } from './quarantine';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '../..');
const corpusDir = resolve(repoRoot, '../wire-format-fixtures');

/** The gitignored scratch root the generated project is written into. */
const scratchDir = resolve(here, '.fsharp-exec');

/**
 * The `Fuaran.UI.*` version the generated project restores. It must be the
 * version `app/FuaranLive.fsproj` pins: the whole claim is that the projection
 * executes against the surface the app itself compiles against, and a harness
 * that silently restored a different one would be measuring something else. Read
 * from that fsproj rather than restated, so the pin moves in one place.
 */
const pinnedTierVersion = ((): string => {
  const fsproj = readFileSync(resolve(repoRoot, 'app/FuaranLive.fsproj'), 'utf8');
  const m = /<PackageReference\s+Include="Fuaran\.UI"\s+Version="([^"]+)"/.exec(fsproj);
  if (m === null) throw new Error('app/FuaranLive.fsproj declares no Fuaran.UI PackageReference');
  return m[1]!;
})();

interface ManifestEntry {
  readonly id: string;
  readonly kind: string;
  readonly inputFile: string;
}

const manifest = JSON.parse(readFileSync(resolve(corpusDir, 'manifest.json'), 'utf8')) as {
  fixtures: ManifestEntry[];
};

const nodeFixtures = manifest.fixtures.filter((f) => f.kind === 'node-round-trip');

const wireOfFixture = (f: ManifestEntry): string =>
  readFileSync(resolve(corpusDir, f.inputFile), 'utf8').trim();

const fixtureById = new Map(nodeFixtures.map((f) => [f.id, f]));
const wireOf = (id: string): string => wireOfFixture(fixtureById.get(id)!);

/**
 * The generated project. `Fuaran.UI.OpStream.Abstractions` is what carries the
 * canonical encoder (`CanonicalJson.encodeNode`), and it pulls the rest of the
 * graph; both pins move together with the app's.
 *
 * The `NoWarn` list is not laziness. `FS0044` is `Action.Dispatch`'s own
 * deprecation, which the projection is REQUIRED to emit when a fixture carries
 * one; the rest are warnings a machine-generated file provokes structurally
 * (shadowing, indentation) and which say nothing about the emission's
 * correctness. Errors are untouched, and errors are what this arm reads.
 */
const projectFile = (name: string): string => `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <WarningLevel>0</WarningLevel>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);FS0044;FS0049;FS0064;FS0193;FS3370</NoWarn>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
    <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup><Compile Include="${name}" /></ItemGroup>
  <ItemGroup>
    <PackageReference Include="Fuaran.UI" Version="${pinnedTierVersion}" />
    <PackageReference Include="Fuaran.UI.OpStream.Abstractions" Version="${pinnedTierVersion}" />
  </ItemGroup>
</Project>
`;

/**
 * `open Fuaran.UI.Generated` comes LAST, and the order is load-bearing:
 * `Fuaran.Core` and the generated UI model both declare a `DeterminismSource`
 * and a `HostEffect`, and the record fields that take them are the generated
 * ones. Opening Core last made two fixtures fail to compile against types whose
 * names matched perfectly.
 */
const PRELUDE = `module FsProjectionConformance

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Generated
open Fuaran.UI.OpStream.Abstractions
`;

interface Emission {
  readonly id: string;
  readonly source: string;
}

/** Compile + run a generated program; the whole result of one `dotnet run`. */
interface RunResult {
  readonly status: number;
  readonly stdout: string;
  readonly errors: readonly string[];
}

const dotnetAvailable = (): boolean =>
  spawnSync('dotnet', ['--version'], { encoding: 'utf8' }).status === 0;

const compileAndRun = (dir: string, program: string): RunResult => {
  rmSync(dir, { recursive: true, force: true });
  mkdirSync(dir, { recursive: true });
  writeFileSync(resolve(dir, 'Program.fs'), program, 'utf8');
  writeFileSync(resolve(dir, 'FsProjection.fsproj'), projectFile('Program.fs'), 'utf8');

  const r = spawnSync(
    'dotnet',
    ['run', '--project', resolve(dir, 'FsProjection.fsproj'), '-v', 'q', '--nologo'],
    { encoding: 'utf8', maxBuffer: 256 * 1024 * 1024 },
  );

  const errors = `${r.stdout ?? ''}\n${r.stderr ?? ''}`
    .split('\n')
    .filter((l) => /: error /.test(l))
    .map((l) => l.trim());

  return { status: r.status ?? 1, stdout: r.stdout ?? '', errors };
};

// ── the round trip ───────────────────────────────────────────────────────────

const emissions: Emission[] = [];
/** Fixtures whose PROJECTION threw — a projector fault, not a host one. */
const emitFailures: string[] = [];
let run: RunResult | undefined;
/** The re-encoded canonical JSON per fixture id, as the program printed it. */
const encoded = new Map<string, string>();
let blockedReason: string | undefined;
/** Which lines of the generated file each fixture's emission occupies. */
let caseLines: { readonly id: string; readonly from: number; readonly to: number }[] = [];

/** The `1`-based line a diagnostic points at, or `undefined`. */
const lineOfDiagnostic = (diagnostic: string): number | undefined => {
  const m = /\.fs\((\d+),\d+\)/.exec(diagnostic);
  return m === null ? undefined : Number(m[1]);
};

/**
 * Which fixtures the diagnostics are about, attributed BY LINE. Matching on the
 * `case_N` binding name was the first attempt and it does not work: the compiler
 * reports `Program.fs(1455,50): error …`, and only a diagnostic about the binding
 * ITSELF ever repeats its name.
 */
const fixturesInErrors = (errors: readonly string[]): string[] => {
  const hit = new Set<string>();
  for (const diagnostic of errors) {
    const line = lineOfDiagnostic(diagnostic);
    if (line === undefined) continue;
    for (const c of caseLines) if (line >= c.from && line <= c.to) hit.add(c.id);
  }
  return [...hit].sort();
};

beforeAll(() => {
  if (!dotnetAvailable()) {
    blockedReason = 'no `dotnet` on PATH — the F# arm cannot compile its generated program';
    return;
  }

  for (const f of nodeFixtures) {
    try {
      emissions.push({ id: f.id, source: projectFSharpExpr(wireOfFixture(f)) as string });
    } catch (e) {
      emitFailures.push(`${f.id}: ${String(e).slice(0, 200)}`);
    }
  }

  // Built line by line so every fixture's line RANGE is known: that is what
  // attributes a compiler diagnostic back to the fixture it is about.
  const preludeLines = PRELUDE.split('\n').length;
  const bodyLines: string[] = [];
  caseLines = [];

  emissions.forEach((e, i) => {
    const from = preludeLines + bodyLines.length + 1;
    bodyLines.push(`let case_${i} () : Node<obj> =`);
    for (const l of e.source.split('\n')) bodyLines.push(`  ${l}`);
    bodyLines.push('');
    caseLines.push({ id: e.id, from, to: preludeLines + bodyLines.length });
  });

  const body = bodyLines.join('\n');
  const table = emissions.map((e, i) => `    ${JSON.stringify(e.id)}, case_${i}`).join('\n');

  // One line per fixture, `id<TAB>json`. A throw is reported in the same shape
  // rather than taking the process down: one fixture that cannot BUILD its tree
  // must not cost the other 220 their measurement.
  run = compileAndRun(
    scratchDir,
    `${PRELUDE}
${body}
let cases: (string * (unit -> Node<obj>)) list =
  [
${table}
  ]

[<EntryPoint>]
let main _ =
  for (id, build) in cases do
    let line =
      try
        CanonicalJson.encodeNode (build ())
      with ex ->
        "THREW: " + ex.Message.Replace("\\n", " ")

    System.Console.Out.WriteLine(id + "\\t" + line)

  0
`,
  );

  for (const line of run.stdout.split('\n')) {
    const tab = line.indexOf('\t');
    if (tab > 0) encoded.set(line.slice(0, tab), line.slice(tab + 1).replace(/\r$/, ''));
  }
}, 600_000);

const fsQuarantine = entriesFor('fsharp');

// ── the Phase 1661 i18n-argument slot, against the PINNED tier ───────────────
//
// `TextSource.I18n.args` widened from `Map<string, JVal>` to
// `Map<string, Binding<JVal>>`. The pin this arm restores decides which
// spelling `app/Projection.fs` must emit, and the two are mutually exclusive —
// so when the pin moves past that change the generated program stops compiling,
// with an FS0001 attributed to whichever fixture happens to carry an i18n text
// source. The cover below turns that into a named failure carrying the remedy.

/** Every i18n argument bag a node fixture carries, as it rides the WIRE. */
const wireI18nBags = (v: unknown): Record<string, unknown>[] => {
  const found: Record<string, unknown>[] = [];
  const walk = (x: unknown): void => {
    if (Array.isArray(x)) {
      for (const e of x) walk(e);
      return;
    }
    if (x === null || typeof x !== 'object') return;
    const o = x as Record<string, unknown>;
    if (o['$type'] === 'I18n' && o['args'] !== null && typeof o['args'] === 'object') {
      const bag = o['args'] as Record<string, unknown>;
      if (Object.keys(bag).length > 0) found.push(bag);
    }
    for (const e of Object.values(o)) walk(e);
  };
  walk(v);
  return found;
};

/** An argument is the BINDING arm iff it is an object carrying `$type` (§5). */
const isWireBoundArg = (a: unknown): boolean =>
  a !== null && typeof a === 'object' && !Array.isArray(a) && '$type' in (a as object);

const i18nCarriers = nodeFixtures
  .map((f) => {
    const bags = wireI18nBags(JSON.parse(wireOfFixture(f)));
    const args = bags.flatMap((b) => Object.values(b));
    return {
      id: f.id,
      bare: args.some((a) => !isWireBoundArg(a)),
      bound: args.some(isWireBoundArg),
      any: args.length > 0,
    };
  })
  .filter((c) => c.any);

interface I18nArmVerdict {
  readonly spelling?: 'bare' | 'bound';
  readonly error?: string;
}

let i18nArmVerdict: I18nArmVerdict | undefined;

/**
 * Which argument spelling the PINNED `Fuaran.UI` accepts, asked of the compiler
 * rather than inferred from a version string. Two one-line alternatives in their
 * own modules, attributed by line exactly as `resolveFSharpConstructs` does;
 * exactly one must compile, and "both" or "neither" is a broken probe reported
 * as such rather than resolved into a verdict.
 */
const pinnedI18nArgumentArm = (): I18nArmVerdict => {
  if (i18nArmVerdict !== undefined) return i18nArmVerdict;
  if (!dotnetAvailable()) {
    i18nArmVerdict = { error: 'no `dotnet` on PATH' };
    return i18nArmVerdict;
  }

  const prelude = PRELUDE.replace('module FsProjectionConformance', 'module FsI18nArgProbe');
  const preludeLines = prelude.split('\n').length;
  const alternatives: { readonly spelling: 'bare' | 'bound'; readonly expr: string }[] = [
    { spelling: 'bare', expr: 'TextSource.I18n("k", Map.ofList [ ("n", JInt 1) ])' },
    {
      spelling: 'bound',
      // `Binding.Static of value: 'T option` — the option is the slot's own
      // structural absence, not part of what is being probed here.
      expr: 'TextSource.I18n("k", Map.ofList [ ("n", Binding.Static(Some(JInt 1))) ])',
    },
  ];

  const lines: string[] = [];
  const ranges: { spelling: 'bare' | 'bound'; line: number }[] = [];
  alternatives.forEach((a, i) => {
    lines.push(`module I18nProbe_${i} =`);
    lines.push(`  let _v: TextSource = ${a.expr}`);
    lines.push('');
    ranges.push({ spelling: a.spelling, line: preludeLines + lines.length - 1 });
  });

  const dir = resolve(here, '.fsharp-i18n-probe');
  rmSync(dir, { recursive: true, force: true });
  mkdirSync(dir, { recursive: true });
  writeFileSync(resolve(dir, 'Probe.fs'), `${prelude}\n${lines.join('\n')}\n`, 'utf8');
  writeFileSync(
    resolve(dir, 'FsI18nProbe.fsproj'),
    projectFile('Probe.fs').replace(
      '<OutputType>Exe</OutputType>',
      '<OutputType>Library</OutputType>',
    ),
    'utf8',
  );

  const r = spawnSync(
    'dotnet',
    ['build', resolve(dir, 'FsI18nProbe.fsproj'), '-v', 'q', '--nologo'],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 },
  );
  const errorLines = `${r.stdout ?? ''}\n${r.stderr ?? ''}`
    .split('\n')
    .filter((l) => /: error /.test(l))
    .map((l) => l.trim());

  // A diagnostic pointing outside both probes is a fault of the project — a
  // restore failure, say — and must not read as "neither spelling compiles".
  const probeLines = new Set(ranges.map((g) => g.line));
  const outside = errorLines.filter((l) => {
    const line = lineOfDiagnostic(l);
    return line === undefined || !probeLines.has(line);
  });
  if (outside.length > 0) {
    i18nArmVerdict = { error: `the probe project itself failed: ${outside[0]}` };
    return i18nArmVerdict;
  }

  const refusedLines = new Set(
    errorLines.map(lineOfDiagnostic).filter((n): n is number => n !== undefined),
  );
  const accepted = ranges.filter((g) => !refusedLines.has(g.line)).map((g) => g.spelling);
  i18nArmVerdict =
    accepted.length === 1
      ? { spelling: accepted[0]! }
      : {
          error:
            `${accepted.length} of the two i18n argument spellings compiled against ` +
            `Fuaran.UI ${pinnedTierVersion} (expected exactly one): ${accepted.join(', ') || 'none'}`,
        };
  return i18nArmVerdict;
};

describe('F# projection conformance (Node corpus)', () => {
  it('the corpus is present and non-trivial', () => {
    expect(nodeFixtures.length).toBeGreaterThanOrEqual(70);
  });

  it('the arm can run at all', () => {
    // It FAILS rather than skips when the toolchain is absent, on the Python
    // arm's reasoning: an arm that goes green without its oracle is worse than
    // no arm. The .NET SDK is not an optional dependency here — the Fable
    // compile that produced the projector already needed it.
    expect(blockedReason, blockedReason ?? '').toBeUndefined();
  });

  it('every node fixture PROJECTS', () => {
    expect(
      emitFailures,
      `app/Projection.fs threw while projecting:\n  ${emitFailures.join('\n  ')}`,
    ).toEqual([]);
  });

  it('the generated program COMPILES and runs', () => {
    if (blockedReason !== undefined) return;
    const named = fixturesInErrors(run!.errors);
    expect(
      run!.status,
      [
        'the generated F# program did not compile+run cleanly.',
        named.length > 0 ? `fixtures named by the diagnostics: ${named.join(', ')}` : '',
        `first diagnostics:\n  ${run!.errors.slice(0, 12).join('\n  ')}`,
        `generated program: ${resolve(scratchDir, 'Program.fs')}`,
      ]
        .filter((s) => s !== '')
        .join('\n'),
    ).toBe(0);
  });

  for (const f of nodeFixtures) {
    // A quarantined fixture's self-clearing check is registered from the shared
    // table below, so only the required byte round-trip is left here.
    if (fsQuarantine.has(f.id)) continue;

    it(`${f.id} round-trips byte-identically`, () => {
      if (blockedReason !== undefined) return;
      const got = encoded.get(f.id);
      expect(
        got,
        `no re-encode for '${f.id}' — the generated program printed no line for it`,
      ).toBeDefined();
      expect(got, `projected F# source for ${f.id} must re-encode byte-identically`).toBe(
        wireOf(f.id),
      );
    });
  }

  it('I18n arguments: the corpus exercises BOTH arms', () => {
    // The non-vacuity half of the cover below. `TextSource.I18n.args` widened in
    // Phase 1661 (`Map<string, JVal>` -> `Map<string, Binding<JVal>>`), and the
    // wire carries no tag saying which arm an argument is: an object with
    // `$type` is the binding arm, any other JSON value is the literal, and a
    // `Static` argument carrying a value encodes BARE (WIRE_FORMAT.md §5). That
    // bare spelling is what let the TypeScript arm's raw-JSON reading round-trip
    // by accident for a whole release, so a corpus reaching only one arm would
    // make the byte comparison here prove less than it looks.
    expect(
      i18nCarriers.filter((c) => c.bare).map((c) => c.id),
      'no node fixture carries a BARE i18n argument — the literal arm is unexercised',
    ).not.toHaveLength(0);
    expect(
      i18nCarriers.filter((c) => c.bound).map((c) => c.id),
      'no node fixture carries a `$type` i18n argument — the binding arm is unexercised',
    ).not.toHaveLength(0);
    expect(
      i18nCarriers.map((c) => c.id).filter((id) => fsQuarantine.has(id)),
      'quarantining an i18n carrier drops this slot out of the byte cover',
    ).toEqual([]);
  });

  it('I18n arguments: the projector emits the spelling the PINNED tier takes', () => {
    if (blockedReason !== undefined) return;

    // The arm's own claim is that the projection executes against the surface
    // the app compiles against, so "which spelling is right" is a question about
    // the PIN and not about the current corpus. It is asked of the compiler
    // rather than of a version string: exactly one of the two argument
    // spellings type-checks against `Fuaran.UI` at the pinned version, and a
    // probe that answers "both" or "neither" is a broken probe, reported as an
    // error rather than resolved into a verdict.
    const arm = pinnedI18nArgumentArm();
    expect(
      arm.error,
      `the pinned-tier i18n probe could not answer: ${arm.error ?? ''}`,
    ).toBeUndefined();

    // The projector agrees with that answer or the generated program does not
    // compile, and a type error is what the pin move actually produces. This
    // turns that FS0001 into the sentence a session needs.
    const named = new Set(fixturesInErrors(run!.errors));
    const broken = i18nCarriers.map((c) => c.id).filter((id) => named.has(id));
    expect(
      broken,
      arm.spelling === 'bound'
        ? `Fuaran.UI ${pinnedTierVersion} takes a Binding<JVal> i18n argument (Phase 1661), and ` +
            "these fixtures' emissions do not compile against it. `fsTextSource` in " +
            'app/Projection.fs still emits the bare `Map.ofList [ (name, JVal) ]` form; each ' +
            'argument must be wrapped as a binding — the same change the TypeScript arm took, ' +
            'where a `$type`-carrying argument projects as a binding and any other JSON value ' +
            'as `binding.static(...)`, whose `Static` arm the encoder puts back on the wire bare.'
        : `Fuaran.UI ${pinnedTierVersion} takes a bare JVal i18n argument (the pre-1661 shape), ` +
            "and these fixtures' emissions do not compile against it.",
    ).toEqual([]);

    // Said out loud, so a reader of a green run knows WHICH world it was green
    // in. The pin predates Phase 1661 as of fuaran#1695; when it moves, the
    // assertion above is what names the work.
    expect(['bare', 'bound']).toContain(arm.spelling);
  }, 600_000);

  it('the F# arm holds NO quarantine entries, and that is the assertion', () => {
    // The same posture as the TypeScript arm's, and reached for the same reason:
    // a list is easier to append to than an emitter is to extend, and the
    // TypeScript leg's own list decayed for eight days proving it. Every node
    // fixture above is REQUIRED to compile, execute and re-encode byte-identically.
    //
    // This arm was empty from its first run (fuaran#1657) rather than driven
    // there, and the reason is structural rather than lucky: its host is the
    // pinned `Fuaran.UI` package, whose model IS the wire model, so a construct
    // the corpus carries is a construct the package declares. Every shortfall the
    // build-up found was the projector's own and was TAUGHT.
    //
    // A future corpus addition that is neither taught nor listed therefore fails
    // as a byte difference (or a compile error naming its `case_N`) rather than
    // being absorbed. If one ever genuinely cannot be taught — the authoring
    // surface lacking the construct outright — it takes a dated
    // `{ arm: 'fsharp', … }` entry in ./quarantine.ts naming that construct, and
    // this assertion's count moves with it.
    expect([...fsQuarantine.keys()]).toEqual([]);
  });
});

// ── the shared quarantine's F# arm ───────────────────────────────────────────

/**
 * The F# arm's host resolver — the sibling of `resolve_construct` in
 * `python_exec.py` and of `resolveTypeScriptConstruct`, and the odd one out:
 * there is no in-process surface to look a symbol up on, because this arm's host
 * is a compiled assembly. So the COMPILER answers, over a probe that mentions
 * each token in the shape its own grammar implies:
 *
 *   `Binding.Expr`      a union case      → a `match` arm mentioning it
 *   `MetricSpec.Trend`  a record field    → a field read off a typed lambda
 *   `Fuaran.metric`     a smart ctor      → a bare reference to the value
 *
 * Every token is compiled in its OWN module inside one file, so one refused
 * token does not hide the others' verdicts, and a diagnostic is attributed to
 * its token BY LINE — the compiler reports `Probe.fs(13,56): error …`, and the
 * module name appears only when the error happens to be about the module itself.
 *
 * It REFUSES what it cannot answer rather than guessing, exactly as the other
 * two resolvers do — a token that resolved to a shrug would hold an entry
 * vacuously, which is the failure Phase 1578 exists to make impossible.
 */
export const resolveFSharpConstructs = (
  tokens: readonly string[],
): Map<string, ConstructVerdict> => {
  const out = new Map<string, ConstructVerdict>();
  const probes: { readonly token: string; readonly body: string }[] = [];

  for (const token of tokens) {
    if (token.startsWith('optional:')) {
      out.set(token, { error: '`optional:` is a retired token — name the construct itself' });
      continue;
    }
    const parts = token.split('.');
    if (parts.length !== 2) {
      out.set(token, { error: `expected Owner.symbol, got ${parts.length} segment(s)` });
      continue;
    }
    const [owner, symbol] = [parts[0]!, parts[1]!];
    const body =
      /^[A-Z]/.test(owner) && /^[A-Z]/.test(symbol)
        ? // A union CASE or a record FIELD. One expression cannot be both, so the
          // probe is both, and the token resolves if EITHER compiles — which is why
          // each alternative gets its own line and its own verdict.
          [
            `  let _case (x: ${owner}<obj>) = match x with | ${owner}.${symbol} _ -> 1 | _ -> 0`,
            `  let _field (x: ${owner}) = x.${symbol}`,
          ]
        : // A smart constructor / builder value.
          [`  let _value = ${owner}.${symbol}`];
    probes.push({ token, body: body.join('\n') });
  }

  if (probes.length === 0) return out;
  if (!dotnetAvailable()) {
    for (const p of probes) out.set(p.token, { error: 'no `dotnet` on PATH' });
    return out;
  }

  // Line-ranged exactly as the round-trip program is, and for the same reason.
  const prelude = PRELUDE.replace('module FsProjectionConformance', 'module FsProbe');
  const lines: string[] = [];
  const ranges: { token: string; from: number; to: number; alternatives: number }[] = [];
  const preludeLines = prelude.split('\n').length;

  probes.forEach((p, i) => {
    const alternatives = p.body.split('\n');
    lines.push(`module Probe_${i} =`);
    const from = preludeLines + lines.length;
    for (const l of alternatives) lines.push(l);
    lines.push('');
    ranges.push({
      token: p.token,
      from,
      to: preludeLines + lines.length - 1,
      alternatives: alternatives.length,
    });
  });

  const dir = resolve(here, '.fsharp-probe');
  rmSync(dir, { recursive: true, force: true });
  mkdirSync(dir, { recursive: true });
  writeFileSync(resolve(dir, 'Probe.fs'), `${prelude}\n${lines.join('\n')}\n`, 'utf8');
  writeFileSync(
    resolve(dir, 'FsProbe.fsproj'),
    projectFile('Probe.fs').replace(
      '<OutputType>Exe</OutputType>',
      '<OutputType>Library</OutputType>',
    ),
    'utf8',
  );

  const r = spawnSync('dotnet', ['build', resolve(dir, 'FsProbe.fsproj'), '-v', 'q', '--nologo'], {
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  });
  const errorLines = `${r.stdout ?? ''}\n${r.stderr ?? ''}`
    .split('\n')
    .filter((l) => /: error /.test(l))
    .map((l) => l.trim());

  const refusedLines = new Set(
    errorLines.map(lineOfDiagnostic).filter((n): n is number => n !== undefined),
  );

  // A diagnostic pointing OUTSIDE every probe is a fault of the project rather
  // than a verdict about a token — a restore failure, say — and is reported as
  // an error rather than read as "every token is absent".
  const outside = errorLines.filter((l) => {
    const line = lineOfDiagnostic(l);
    return line === undefined || !ranges.some((g) => line >= g.from && line <= g.to);
  });

  for (const g of ranges) {
    if (outside.length > 0) {
      out.set(g.token, { error: `the probe project itself failed: ${outside[0]}` });
      continue;
    }
    // Refused on EVERY alternative ⇒ the surface does not carry it. Refused on
    // some but not all ⇒ it does, under the spelling that compiled.
    let refused = 0;
    for (let line = g.from; line <= g.to; line += 1) if (refusedLines.has(line)) refused += 1;

    out.set(
      g.token,
      refused < g.alternatives
        ? { models: true, detail: `${g.token} compiles against Fuaran.UI ${pinnedTierVersion}` }
        : {
            models: false,
            detail:
              errorLines.find((l) => {
                const line = lineOfDiagnostic(l);
                return line !== undefined && line >= g.from && line <= g.to;
              }) ?? 'refused on every spelling',
          },
    );
  }
  return out;
};

/**
 * One token at a time, over a memoised batch: the shared checks ask per token,
 * and one compile answers all of them. With the arm's quarantine EMPTY the only
 * caller is the resolver's own self-test below, so the ordinary gate pays for
 * one small build rather than none — the price of the arm's claim being
 * falsifiable at all.
 */
const resolverCache = new Map<string, ConstructVerdict>();

const fsHostModels = (token: string): ConstructVerdict => {
  const cached = resolverCache.get(token);
  if (cached !== undefined) return cached;
  for (const [t, v] of resolveFSharpConstructs([token])) resolverCache.set(t, v);
  return resolverCache.get(token) ?? { error: 'not probed' };
};

const fsProjected = (id: string): string | undefined => emissions.find((e) => e.id === id)?.source;

const fsRoundTrip = (id: string): { ok: boolean; encoded?: string } => {
  const got = encoded.get(id);
  if (got === undefined || got.startsWith('THREW: ')) return { ok: false };
  return { ok: true, encoded: got };
};

registerQuarantineChecks({
  arm: 'fsharp',
  fixtureIds: nodeFixtures.map((f) => f.id),
  wireOf,
  projectedSource: fsProjected,
  hostModels: fsHostModels,
  roundTrip: fsRoundTrip,
  blocked: () => blockedReason,
});

// The resolver above has NO live entries to exercise it — this arm's quarantine
// is empty and is meant to stay that way — so it is proved here instead, exactly
// as the TypeScript arm's is. A probe nothing runs is worth less than no probe,
// because it reads as coverage. All three answers, in one compile.
describe("the F# arm's construct resolver (fuaran#1657)", () => {
  it('reports what the pinned tier HAS, what it LACKS, and REFUSES what it cannot answer', () => {
    if (blockedReason !== undefined) return;

    const verdicts = resolveFSharpConstructs([
      'Binding.Static',
      'Binding.NoSuchCaseExists',
      'noSuchNamespace.thing.extra',
      'optional:Binding.Static',
    ]);

    const has = verdicts.get('Binding.Static')!;
    expect(has, `'Binding.Static' must resolve: ${JSON.stringify(has)}`).not.toHaveProperty(
      'error',
    );
    expect((has as { models: boolean }).models).toBe(true);

    const lacks = verdicts.get('Binding.NoSuchCaseExists')!;
    expect(lacks).not.toHaveProperty('error');
    expect((lacks as { models: boolean }).models).toBe(false);

    // An unanswerable token, and the RETIRED `optional:` prefix — refused by name
    // on this arm as on the other two, so a stale entry carrying the old grammar
    // fails loudly rather than holding vacuously.
    expect(verdicts.get('noSuchNamespace.thing.extra')).toHaveProperty('error');
    expect(verdicts.get('optional:Binding.Static')).toHaveProperty('error');
  }, 600_000);
});
