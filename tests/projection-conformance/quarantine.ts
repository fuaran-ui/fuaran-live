// The projection-conformance QUARANTINE — one table for both arms (Phase 1584).
//
// Until this phase the two arms kept two tables in two files, and the fields did
// not even mean the same thing: `typescript.test.ts` held a bare `Set` of ids,
// while `python.test.ts` held a map whose `arm` named the REPOSITORY that owns a
// cause. So the one question a reader actually asks — "is this fixture
// quarantined on one arm and not the other, and why?" — could only be answered by
// diffing two files, and the word `arm` answered it differently in each.
//
// One table, keyed by fixture id, resolves both. `arm` now names the CONFORMANCE
// ARM (`typescript` | `python`); `class` names which repository owns the cause
// (`host` | `projector` | `both`) — the field the Python arm used to call `arm`.
// A fixture is a ROW, and a row carries one entry per arm that quarantines it, so
// a per-arm mismatch is visible in the row rather than in a diff.
//
// Everything else about an entry is Phase 1578's and is unchanged: the
// machine-readable `construct` token beside the human `reason`, and the two
// probes that falsify the claim rather than merely watching the outcome. What
// this phase changed is that they run PER ARM from here, once implemented, rather
// than once in the Python file.
//
// ── THE TOKEN GRAMMAR ────────────────────────────────────────────────────────
//
// A `construct` is a path into the arm's own pinned HOST, resolved by that arm's
// resolver — `resolve_construct` in `python_exec.py` for the Python arm, and
// `resolveTypeScriptConstruct` below for the TypeScript one. The two hosts are
// different surfaces, so the two grammars are near-siblings rather than one:
//
//   PYTHON (resolved against the installed interpreter)
//     `t.Drawing`                  a symbol the module must export. A module
//                                  prefix is the projector's own namespace (`t`,
//                                  `cp`, `binding`, …); a bare name resolves in `t`.
//     `Binding.Query`              a CASE of a union alias.
//     `Chart.annotations`          a FIELD of a record.
//
//   TYPESCRIPT (resolved in-process against the pinned `@fuaran-ui/*` packages)
//     `fuaran.embed`               a member of a factory namespace the evaluated
//                                  source binds. A bare name resolves in `fuaran`.
//     `ops.encodeNode`             the same, over the encoder module.
//
//   There was a third Python spelling, `optional:Owner.field` ("the record can
//   OMIT this slot"), and it is RETIRED — see lesson 4 below. BOTH arms refuse a
//   token carrying that prefix, by name, so a stale entry reads as the retired
//   grammar it is. A token an arm cannot resolve is an `{ error }`, which the
//   "every construct token resolves" check turns into a loud failure — never a
//   silent hold. That refusal is the whole reason the fourth pass's probes work
//   at all.
//
// ── WHAT THE MEASUREMENT PASSES ESTABLISHED ──────────────────────────────────
//
// Six dated re-measurements (2026-09-02 through 2026-09-07, across fuaran-py
// 0.0.1 · 0.0.6 · 0.1.0 · 0.2.0 · 0.3.0) used to sit here as six chronological
// blocks, each restating its own counts. The counts are generated now —
// `quarantineSummary()`, rendered into the census test's name — so what is kept
// is the part a count cannot carry: what each pass LEARNED, with the instance
// that taught it. Ordered by lesson, not by date.
//
//   1. A QUARANTINE IS EXACTLY AS HONEST AS ITS LAST MEASUREMENT. Twelve entries
//      blamed a host that had carried `Column.field_name` since 0.0.6; six more
//      blamed a release for a construct the projector had simply never emitted.
//      Both were found only by re-running the fixture, which is why Phase 1578
//      made every reason carry a falsifier. A reason nobody re-runs decays into
//      a claim.
//
//   2. A HOST RELEASE USUALLY MOVES THE WORK RATHER THAN REMOVING IT — except
//      where the projector was already emitting the construct. 0.2.0 cleared
//      thirty-eight entries and NOT ONE of them cleared on the pin raise alone;
//      0.3.0 cleared twelve on the pin raise alone. The discriminator is not the
//      release: it is whether `app/Projection.fs` already spelled the construct.
//      For `Drawing` / `Fact` / `Mount` it did, via `pyGenericNode`'s
//      `t.<WireTag>(<wire keys snake-cased>)` fallback, so the day the host grew
//      the class the fallback's guess became correct.
//
//   3. A FALLBACK THAT GUESSES CORRECTLY IS NOT CONFORMANCE. Those twelve
//      fixtures passed while the fallback filled the host's records with RAW
//      DICTS the host's lowering passes straight through — so the arm would have
//      gone on reporting green had 0.3.0 modelled `Drawing` and none of its nine
//      shapes. Passing fixtures were re-taught through the real constructors for
//      that reason. The fallback's own limit is visible in the one id it could
//      not carry: `mount-2` read the wire tag `Str` as a class name and reached
//      for a `t.Str` that has never existed in any release.
//
//   4. A PROBE CAN OUTLIVE ITS CAUSE TOO — AND THE FIX WAS TO RETIRE IT.
//      `optional:` asked whether a field admits `None`; the moment a host turns a
//      closure sentinel into a BOOL FLAG the field exists and cannot be `None`,
//      so the probe kept answering "the host cannot omit this" while the flag
//      omitted the wire key perfectly well. Two entries held vacuously that way
//      at 0.2.0 (`multiselect-chip-list-param`, `composite-tabs-panels`) and were
//      caught by the round-trip half, exactly as the pre-1578 passes were. The
//      token is GONE as of 2026-09-09: the closure-sentinel family it served was
//      emptied at 0.2.0 (family (iii) below), no entry has used it since, and
//      teaching it to read a `bool`-typed handler field as omittable-by-flag
//      would have been designing a falsifier for a case with no entry. An entry
//      that needs this ground names the CONSTRUCT — the record, the field, the
//      union case — which is what every standing entry already does and what the
//      round-trip half falsifies directly. Both resolvers refuse the prefix by
//      name, so the retirement is enforced rather than remembered.
//
//   5. AN ENTRY THAT NAMES ONLY THE HALF THAT IS SOMEONE ELSE'S IS HOW THE
//      PROJECTOR'S OWN LAG GOES UNRECORDED. That is what `class: 'both'` exists
//      for — three grid ids whose `Binding.Query` source was host lag AND whose
//      `exportable` / `keepRowsTogether` / `repeatHeader` were slots the host
//      modelled and the projector did not emit. Phase 1581 emitted them, so the
//      class is EMPTY; it is kept because the shape recurs on every release that
//      closes one half of a two-cause entry.
//
//   6. THE FAMILIES OF CAUSE this quarantine has met, as history rather than as
//      state: (i) no typed node kind — emptied at 0.3.0 by `Drawing` / `Fact` /
//      `Mount`; (ii) no typed binding or action — now holding only `Binding.Expr`,
//      which Phase 1580 declined with its reasons; (iii) a hardcoded closure
//      sentinel — emptied at 0.2.0, which made every handler an omittable flag
//      and every control's `value` optional; (iv) a record narrower than the wire
//      — `TransformBinding.source`, still a bare `DataSource`; (v) an encoder that
//      disagrees with the wire — §2 rule 2 orders object keys by UTF-16 code
//      unit, and the writer sorted by code point until 0.1.0. Families (i), (iii)
//      and (v) are empty. Read the list as the history it is and the map below as
//      the state.
//
// The TypeScript arm's own history is the mirror image and is shorter: its set
// held 47 ids on 2026-08-29 under two causes (package drift, 2 ids; projector
// vocabulary lag, 45), and has been EMPTY since 2026-08-30 because every
// subsequent shortfall — 48 ids, then 11 more — was TAUGHT rather than listed.
// Two of those were missing KINDS (`Embed`, `Tree`) rather than missing slots, so
// they failed as a `TypeError` from the generic sketch's two-argument call rather
// than as a byte difference; a `Cannot read properties of undefined` from that arm
// reads like a harness fault and is not one. Keeping the set empty is the
// preference, not an accident: a list is easier to append to than an emitter is to
// extend, and the previous list decayed for eight days proving it.

// ── WHAT PHASE 1582 CHANGED, AND WHAT IT DID NOT ─────────────────────────────
//
// A host that publishes a capability manifest (WIRE_FORMAT.md §27) lets the arm
// COMPUTE its expected-unmodelled set as corpus-minus-manifest, and this table
// stops being what decides membership. See `./host-capability.ts`.
//
// It is not deleted, for two reasons. The live Python arm executes a PINNED PyPI
// release that publishes no manifest, so the computed path is dark until that pin
// moves and this table is the whole answer until then. And even under a manifest,
// §27.4 rule 2 means a host that declines to claim a family says NOTHING about
// it — so a fixture whose only gap falls in an unclaimed family is neither
// computed-unmodelled nor honestly failable, and an entry may stand in for it.
//
// Such an entry declares `residual`: the family (and scope key) it relies on
// being unclaimed. `staleResiduals` fails it the moment the manifest DOES claim
// that family, which is what stops the residual quietly becoming this table again.

import { describe, expect, it } from 'vitest';

import type { TokenFamily } from './host-capability';

/** Which conformance arm quarantines the fixture. */
export type Arm = 'typescript' | 'python';

/** Which repository owns the cause. `both` additionally names a projector construct. */
export type CauseClass = 'host' | 'projector' | 'both';

export const ARMS: readonly Arm[] = ['typescript', 'python'];

const CLASSES: readonly CauseClass[] = ['host', 'projector', 'both'];

export interface QuarantineEntry {
  /** The conformance arm this entry is about. */
  readonly arm: Arm;
  /** The host-model path this entry claims is absent — see the grammar above. */
  readonly construct: string;
  /** Which repository owns the cause. `both` additionally sets `projectorConstruct`. */
  readonly class: CauseClass;
  /** The sentence for the human. Free text; the `construct` is what the probes read. */
  readonly reason: string;
  /**
   * For `class: 'both'` — the construct the host DOES model and the projector
   * does not emit. Probed exactly as a `projector` entry's `construct` is.
   */
  readonly projectorConstruct?: string;
  /**
   * The §27 RESIDUAL declaration (Phase 1582): the token family — and, where the
   * family has one, the scope key — this entry relies on a host manifest NOT
   * claiming. An entry carrying it survives under a live manifest; one without it
   * is expected to be covered by the computed set, and is reported as stale when
   * it is not. Setting it is a claim about the MANIFEST's silence, not about the
   * host, and `staleResiduals` falsifies it the moment that silence ends.
   */
  readonly residual?: { readonly family: TokenFamily; readonly scopeKey?: string };
}

/**
 * The one table. Keyed by fixture id; one entry per arm that quarantines it, so a
 * fixture quarantined on one arm and not the other is a single row whose other
 * cell is simply absent.
 *
 * The state, as re-measured 2026-09-07 against fuaran-py 0.3.0 and re-run
 * unchanged 2026-09-09 against the pinned 0.4.0: six ids, two constructs, both
 * host lag with the probe agreeing, all on the Python arm. The TypeScript arm
 * holds none, and its emptiness is an assertion — every node fixture is required
 * to re-encode byte-identically there.
 */
export const QUARANTINE: ReadonlyMap<string, readonly QuarantineEntry[]> = new Map<
  string,
  readonly QuarantineEntry[]
>([
  // 1 — no typed binding case. Python's `Binding` is Static | State | Filter |
  // Selection | Now | FormatBinding | Local | Query | Invoke, so a predicate
  // binding has no spelling in any slot. Phase 1580 declined to model `Expr`
  // with its reasons, so this is a standing gap rather than a release in flight.
  [
    'expr-scalar',
    [{ arm: 'python', construct: 'Binding.Expr', class: 'host', reason: 'no Binding.Expr' }],
  ],
  [
    'expr-params-state-selection',
    [{ arm: 'python', construct: 'Binding.Expr', class: 'host', reason: 'no Binding.Expr' }],
  ],
  [
    'switch-predicate',
    [
      {
        arm: 'python',
        construct: 'Binding.Expr',
        class: 'host',
        reason:
          'SwitchCase.when is modelled from 0.1.0 and emitted; the predicate is a Binding.Expr, which is not modelled',
      },
    ],
  ],
  [
    'node-visible',
    [
      {
        arm: 'python',
        construct: 'Binding.Expr',
        class: 'host',
        reason:
          'UiNode.visible is modelled from 0.1.0 and emitted, and its Binding.Query predicate is modelled from 0.3.0 and emitted; the remaining predicate is a Binding.Expr, which is not modelled',
      },
    ],
  ],

  // 2 — a record narrower than the wire. `TransformBinding.source` is a bare
  // `DataSource` rather than the wire's `TransformSource` DU, so a source that
  // is `{"$type":"State"}` has no spelling at all — the fixture reads as a
  // literal table where the wire names a state key.
  [
    'badge-transform-live',
    [
      {
        arm: 'python',
        construct: 'cp.TransformSource',
        class: 'host',
        reason:
          'TransformBinding.source is a bare DataSource — a State-bound source has no spelling',
        // The gap is INSIDE a slot the corpus IDL types as `hosted`, so it lives in
        // the hostedCases family — which this host cannot derive at all (its compute
        // layer lowers through an isinstance ladder rather than per-record to_wire).
        // A manifest therefore says nothing here, and this entry is what stands in
        // its place until one does.
        residual: { family: 'hostedCases', scopeKey: 'Binding.Transform.source' },
      },
    ],
  ],
  [
    'shared-source-seeded-pair',
    [
      {
        arm: 'python',
        construct: 'cp.TransformSource',
        class: 'host',
        reason:
          'TransformBinding.source is a bare DataSource — a State-bound source has no spelling',
        // The gap is INSIDE a slot the corpus IDL types as `hosted`, so it lives in
        // the hostedCases family — which this host cannot derive at all (its compute
        // layer lowers through an isinstance ladder rather than per-record to_wire).
        // A manifest therefore says nothing here, and this entry is what stands in
        // its place until one does.
        residual: { family: 'hostedCases', scopeKey: 'Binding.Transform.source' },
      },
    ],
  ],
]);

/** Every entry this arm owns, keyed by fixture id. */
export const entriesFor = (arm: Arm): Map<string, QuarantineEntry> => {
  const out = new Map<string, QuarantineEntry>();
  for (const [id, entries] of QUARANTINE) {
    const mine = entries.filter((e) => e.arm === arm);
    if (mine.length > 1) throw new Error(`'${id}' carries ${mine.length} entries for arm '${arm}'`);
    if (mine.length === 1) out.set(id, mine[0]!);
  }
  return out;
};

export type ArmTally = Record<CauseClass, number>;

/** The table's own tally, by arm and by cause class. */
export const tally = (): Record<Arm, ArmTally> => {
  const out = { typescript: emptyTally(), python: emptyTally() };
  for (const entries of QUARANTINE.values()) for (const e of entries) out[e.arm][e.class] += 1;
  return out;
};

const emptyTally = (): ArmTally => ({ host: 0, projector: 0, both: 0 });

/**
 * The quarantine's size, by arm and by which repository owns the cause. DECLARED
 * here and asserted against `tally()` by each arm's census test, so the one
 * number a reader sees is the one the table actually holds. Six passes of prose
 * each carried their own counts; prose cannot be wrong out loud.
 */
export const QUARANTINE_CENSUS: Readonly<Record<Arm, ArmTally>> = {
  typescript: { host: 0, projector: 0, both: 0 },
  python: { host: 6, projector: 0, both: 0 },
};

/**
 * THE generated summary — one line, computed from the table, rendered into BOTH
 * arms' census test names so either arm's output states the whole cross-arm
 * state. This is what replaced the six hand-written RE-MEASURED counts.
 */
export const quarantineSummary = (): string => {
  const t = tally();
  const perArm = ARMS.map((arm) => {
    const a = t[arm];
    const total = a.host + a.projector + a.both;
    if (total === 0) return `${arm} 0`;
    const byClass = CLASSES.filter((c) => a[c] > 0)
      .map((c) => `${c} ${a[c]}`)
      .join(', ');
    return `${arm} ${total} (${byClass})`;
  }).join(' · ');

  const counts = new Map<string, number>();
  for (const entries of QUARANTINE.values())
    for (const e of entries) {
      const key = `${e.construct} [${e.arm}]`;
      counts.set(key, (counts.get(key) ?? 0) + 1);
    }
  const constructs = [...counts]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([k, n]) => `${k}×${n}`)
    .join(', ');

  return constructs === '' ? perArm : `${perArm} — ${constructs}`;
};

/**
 * The table as rows, one per fixture, with a cell per arm — the rendering that
 * makes a per-arm mismatch readable without diffing two files. Used in failure
 * messages; the one-line `quarantineSummary()` is what every run prints.
 */
export const quarantineRows = (): string => {
  const width = Math.max(8, ...[...QUARANTINE.keys()].map((id) => id.length));
  const cell = (entries: readonly QuarantineEntry[], arm: Arm): string => {
    const e = entries.find((x) => x.arm === arm);
    return e === undefined ? '—' : `${e.class}: ${e.construct}`;
  };
  const header = `${'fixture'.padEnd(width)}  typescript                python`;
  const rows = [...QUARANTINE]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(
      ([id, entries]) =>
        `${id.padEnd(width)}  ${cell(entries, 'typescript').padEnd(24)}  ${cell(entries, 'python')}`,
    );
  return [header, ...rows].join('\n');
};

/** One construct token, resolved against an arm's pinned host. */
export type ConstructVerdict = { models: boolean; detail: string } | { error: string };

/**
 * What an arm's generated source must contain for it to be EMITTING the construct
 * a token names.
 *
 * The two projector legs spell differently, so the pattern is per arm. The Python
 * leg builds every value from a typed record (its own rule — see
 * `app/Projection.fs`'s Python-leg header), so a construct it emits appears as a
 * qualified constructor (`t.Drawing(`, `cp.Param(`) or as a snake_case keyword
 * argument (`field_name=`). The TypeScript leg calls camelCase factory members
 * (`fuaran.tree(`, `binding.static(`) or spells an object property (`target:`).
 * Anchoring on those shapes rather than on the bare word is what keeps a fixture
 * id like `'drawing-1'` from reading as an emission of `t.Drawing`.
 */
export const emissionPattern = (arm: Arm, construct: string): RegExp => {
  const leaf = construct.split('.').pop()!;
  const lowercase = /^[a-z]/.test(leaf);
  if (arm === 'python') {
    return lowercase
      ? new RegExp(String.raw`\b${leaf}\s*=`)
      : new RegExp(String.raw`\b[a-z]+\.${leaf}\b`);
  }
  return lowercase
    ? new RegExp(String.raw`(\b[A-Za-z]+\.${leaf}\s*\(|\b${leaf}\s*:)`)
    : new RegExp(String.raw`\b[A-Za-z]+\.${leaf}\b`);
};

/**
 * The TypeScript arm's host resolver — the sibling of `resolve_construct` in
 * `python_exec.py`, over the pinned `@fuaran-ui/*` surface the arm already
 * imports. `namespaces` is the same binding set the arm evaluates projected
 * source against, so the probe and the round-trip cannot disagree about which
 * surface they measured.
 *
 * It REFUSES what it cannot answer rather than guessing, because a token that
 * resolves to a shrug would hold an entry vacuously — the exact failure Phase
 * 1578 exists to make impossible.
 */
export const resolveTypeScriptConstruct = (
  token: string,
  namespaces: Readonly<Record<string, unknown>>,
): ConstructVerdict => {
  // `optional:` was RETIRED from the grammar (see lesson 4 in the header): it
  // stopped discriminating the moment a host turned a closure sentinel into a
  // bool flag, and no entry has used it since. Refused by name on BOTH arms —
  // its Python sibling refuses it identically — so a stale entry carrying the
  // prefix reads as the retired grammar rather than as a missing symbol.
  if (token.startsWith('optional:'))
    return { error: '`optional:` is a retired token — name the construct itself' };
  const parts = token.split('.');
  if (parts.length > 2)
    return { error: `expected [namespace.]symbol, got ${parts.length} segments` };
  const [ns, symbol] = parts.length === 2 ? [parts[0]!, parts[1]!] : ['fuaran', parts[0]!];
  const surface = namespaces[ns];
  if (surface === undefined || surface === null || typeof surface !== 'object')
    return {
      error: `no such namespace '${ns}' (known: ${Object.keys(namespaces).sort().join(', ')})`,
    };
  const has = symbol in (surface as Record<string, unknown>);
  return {
    models: has,
    detail: has
      ? `${ns}.${symbol} is ${typeof (surface as Record<string, unknown>)[symbol]}`
      : `${ns} exports no '${symbol}'`,
  };
};

/** What one arm supplies so the shared checks can run against it. */
export interface ArmProbe {
  readonly arm: Arm;
  /** Every node-round-trip fixture id in the corpus. */
  readonly fixtureIds: readonly string[];
  /** The fixture's canonical wire bytes. */
  readonly wireOf: (id: string) => string;
  /** The arm's projected source for a fixture, or `undefined` when it has none. */
  readonly projectedSource: (id: string) => string | undefined;
  /** Whether this arm's PINNED host models the construct a token names. */
  readonly hostModels: (token: string) => ConstructVerdict | undefined;
  /** Does the fixture round-trip byte-identically on this arm, right now? */
  readonly roundTrip: (id: string) => { ok: boolean; encoded?: string };
  /**
   * A reason the arm could not run at all (a missing interpreter, say). Its own
   * suite fails loudly on that; the per-entry checks here then stand down rather
   * than adding noise to a failure already named.
   */
  readonly blocked?: () => string | undefined;
}

/**
 * Phase 1578's self-clearing and falsifier tests, run PER ARM from the shared
 * table (Phase 1584). One implementation, invoked once by each suite:
 *
 *   • the census — the declared constant against the table's own tally, with the
 *     generated cross-arm summary in the test's name;
 *   • every quarantined id names a real fixture;
 *   • every construct token resolves against this arm's pinned host;
 *   • per entry, the FALSIFIER — a `host` claim fails when the host models the
 *     construct, and says whether that is a stale entry or projector lag; a
 *     `projector` claim fails when the host does NOT model it, or when the
 *     projected source already emits it;
 *   • per entry, the SELF-CLEARING check — a quarantined fixture that starts
 *     round-tripping fails by name.
 */
export const registerQuarantineChecks = (probe: ArmProbe): void => {
  const { arm } = probe;
  const mine = entriesFor(arm);

  describe(`quarantine table — ${arm} arm (Phase 1584)`, () => {
    it(`quarantine: ${quarantineSummary()} — the ${arm} arm's declared census is the table's own tally`, () => {
      // Generated, not narrated: the counts are in this test's NAME, so every run
      // prints the WHOLE cross-arm state and neither file has to be read against
      // the other. The declared constant is what makes a drifted count fail
      // rather than merely read as out of date.
      expect(
        tally()[arm],
        `QUARANTINE_CENSUS.${arm} is stale — the table now holds:\n${quarantineRows()}`,
      ).toEqual({ ...QUARANTINE_CENSUS[arm] });
    });

    it(`every ${arm}-arm quarantined id names a real fixture`, () => {
      const ids = new Set(probe.fixtureIds);
      for (const id of mine.keys())
        expect(ids.has(id), `quarantined '${id}' is not in the corpus — remove it`).toBe(true);
    });

    it(`every ${arm}-arm construct token resolves against the pinned host`, () => {
      // A token the resolver cannot answer is a claim about nothing. Reported
      // here, once, rather than inside each entry probe — a misspelled record
      // name would otherwise read as "the host lacks it", which is exactly the
      // vacuous hold the falsifier exists to make impossible.
      if (probe.blocked?.() !== undefined) return; // the arm's own suite fails, loudly
      const unresolved: string[] = [];
      for (const e of mine.values())
        for (const token of e.projectorConstruct
          ? [e.construct, e.projectorConstruct]
          : [e.construct]) {
          const verdict = probe.hostModels(token);
          if (verdict === undefined) unresolved.push(`${token}: not probed`);
          else if ('error' in verdict) unresolved.push(`${token}: ${verdict.error}`);
        }
      expect(unresolved, `unresolvable construct token(s):\n  ${unresolved.join('\n  ')}`).toEqual(
        [],
      );
    });

    for (const [id, entry] of mine) {
      it(`${id} — the reason's construct is where it says it is (${entry.construct})`, () => {
        if (probe.blocked?.() !== undefined) return;

        /** Host lag: the pinned host must NOT model what the entry blames it for. */
        const hostSide = (token: string) => {
          const verdict = probe.hostModels(token);
          if (verdict === undefined || 'error' in verdict) return; // the token test reports it
          if (!verdict.models) return; // the entry holds

          const emitted = emissionPattern(arm, token).test(probe.projectedSource(id) ?? '');
          expect(
            verdict.models,
            emitted
              ? `'${id}' blames the ${arm} host for '${token}', but the pinned host MODELS it (${verdict.detail}) and the projector already emits it — REMOVE the entry or re-derive its reason`
              : `'${id}' blames the ${arm} host for '${token}', but the pinned host MODELS it (${verdict.detail}) and app/Projection.fs never emits it — this is PROJECTOR lag: re-class the entry as class: 'projector' (or teach app/Projection.fs)`,
          ).toBe(false);
        };

        /** Projector lag: the host models it and app/Projection.fs must not emit it. */
        const projectorSide = (token: string) => {
          const verdict = probe.hostModels(token);
          if (verdict === undefined || 'error' in verdict) return;
          expect(
            verdict.models,
            `'${id}' claims app/Projection.fs lags on '${token}', but the pinned ${arm} host does not model it (${verdict.detail}) — this is HOST lag: re-class the entry as class: 'host'`,
          ).toBe(true);
          expect(
            emissionPattern(arm, token).test(probe.projectedSource(id) ?? ''),
            `'${id}' claims app/Projection.fs lags on '${token}', but the projected source already emits it — REMOVE the entry or re-derive its reason`,
          ).toBe(false);
        };

        if (entry.class === 'host' || entry.class === 'both') hostSide(entry.construct);
        if (entry.class === 'projector') projectorSide(entry.construct);
        if (entry.class === 'both') {
          expect(
            entry.projectorConstruct,
            `'${id}' is class: 'both' and must name its projectorConstruct`,
          ).toBeDefined();
          projectorSide(entry.projectorConstruct!);
        }
      });

      it(`${id} is quarantined on the ${arm} arm (${entry.reason})`, () => {
        if (probe.blocked?.() !== undefined) return;
        const result = probe.roundTrip(id);
        if (!result.ok) return; // still un-projectable — the entry holds
        expect(
          result.encoded,
          `'${id}' now round-trips on the ${arm} arm — the ${entry.class === 'projector' ? 'projector learned it' : 'host grew the construct'}; REMOVE it from QUARANTINE`,
        ).not.toBe(probe.wireOf(id));
      });
    }
  });
};
