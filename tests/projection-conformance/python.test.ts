// Codegen-conformance — Python arm (single-process CPython executor).
//
// For every Node fixture in the workspace wire-format-fixtures/ corpus:
//   1. project the canonical wire JSON to `fuaran_py.ui` authoring source via
//      the F#/Fable projector (app/Projection.fs, Fable-compiled to
//      app/output/Projection.js);
//   2. EXECUTE the generated source against the real `fuaran_py` surface —
//      every fixture in ONE CPython process, not one spawn each;
//   3. re-encode via `fuaran_py.ui.encode` and assert the JSON is
//      byte-identical to the fixture.
//
// The sibling TypeScript arm's shape, with one structural difference forced by
// the host: `fuaran_py`'s `encode` calls `.to_wire()` on the root, and its
// structural `Obj` has no such method, so there is no escape hatch a construct
// outside the typed model can take. Where the TS leg can always emit a typed
// in-memory literal, the Python leg cannot — see PY_UNMODELLED below.
//
// Three slots typed as a raw wire `Value` (`UiNode.visible`, `SwitchCase.when`,
// `Navigate.route`) would accept a hand-built `Obj` and so would let the
// projector spell anything at all, which would dissolve this whole map. The
// projector's own rule — stated where it is enforced, in app/Projection.fs's
// Python-leg header — is to build every such value from a TYPED RECORD and lower
// it with that record's `to_wire`, so an absent case stays absent and this arm
// keeps measuring what it claims to measure.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/,
// and a CPython with `fuaran-py` installed (see resolvePython).

import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

import { beforeAll, describe, expect, it } from 'vitest';

// Fable-generated JS — no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { projectPythonExpr } from '../../app/output/Projection.js';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '../..');
const corpusDir = resolve(repoRoot, '../wire-format-fixtures');

interface ManifestEntry {
  readonly id: string;
  readonly kind: string;
  readonly inputFile: string;
}

const manifest = JSON.parse(readFileSync(resolve(corpusDir, 'manifest.json'), 'utf8')) as {
  fixtures: ManifestEntry[];
};

const nodeFixtures = manifest.fixtures.filter((f) => f.kind === 'node-round-trip');

/**
 * The interpreter to run the executor with. A repo-local `.venv` wins (the
 * documented local setup: `python -m venv .venv` then
 * `pip install fuaran-py==0.3.0`); FUARAN_PY_PYTHON overrides it for CI, where
 * the interpreter is whatever actions/setup-python provisioned.
 */
const resolvePython = (): string => {
  const override = process.env.FUARAN_PY_PYTHON;
  if (override) return override;

  const candidates = [
    resolve(repoRoot, '.venv/Scripts/python.exe'),
    resolve(repoRoot, '.venv/bin/python'),
  ];
  for (const c of candidates) if (existsSync(c)) return c;

  return process.platform === 'win32' ? 'python' : 'python3';
};

// Constructs the corpus reaches that `fuaran_py`'s typed authoring surface does
// not model, so the projection cannot be exact. Named, not counted: each entry
// says WHICH Python construct is absent, so the day fuaran-py grows it the entry
// is removable by search rather than by re-derivation.
//
// This is the shape the sibling TS arm's own note sanctions for a slot with no
// reachable constructor and no literal form — with the difference that in the TS
// tier that case is rare (an in-memory object literal is always available) and
// here it is structural: `encode` requires a `.to_wire()` root, so a kind or a
// binding the typed model omits has NO spelling at all.
//
// Originally dated 2026-09-02 against fuaran-py 0.0.1 (PyPI); RE-MEASURED
// 2026-09-07 against 0.0.6, then 0.1.0, and finally against **0.2.0**, the
// version the three workflows now pin — see the re-measurement notes below the
// family list, which are where the record of what moved lives. Each pass is kept
// with its own version rather than rewritten forward: what a release DID is the
// reusable part, and a narrative edited to match the present tense loses it.
//
// Every entry below was measured, not assumed: the projector emits the shape the
// typed model WOULD take, and the executor's failure — an `AttributeError` naming
// an absent class, or a byte difference in a slot the record cannot carry — is
// what identified the cause. The causes fall into five families:
//
//   1. NO TYPED KIND. `encode` requires a `.to_wire()` root, so a node kind the
//      model omits has no spelling at all: Mount, Fact, Drawing.
//   2. NO TYPED BINDING / ACTION. Query, I18n and Invoke bindings; Call, AiTool
//      and Invoke actions.
//   3. A HARDCODED CLOSURE SENTINEL. Several records emit `onChange` / `onToggle`
//      / `onSelect` / `value` unconditionally, so the canonical minimal control
//      (`{"$type":"Text"}`) and the declarative field-named grid column cannot be
//      reached — the slot is not optional in the record, and the encoder has
//      nothing to omit.
//   4. A NARROWER RECORD. `Chart` reaches eight slots fewer than the wire;
//      `TransformBinding.source` is a bare `DataSource` rather than the wire's
//      `TransformSource` DU, so a `State`- or `Live`-bound source has no
//      spelling; `DataGrid` carries none of the declarative sort / page /
//      edit-state slots and no `reorderable`; `Link` has no `protection`;
//      `Table` no `sortable` / `defaultSort`; `UiNode` no `tooltip`.
//   5. AN ENCODER THAT DISAGREES WITH THE WIRE. §2 rule 2 orders object keys by
//      UTF-16 CODE UNIT; the canonical writer up to 0.0.6 sorted by Python's
//      code-point order, which differs the moment a key is non-BMP. Not a
//      modelling gap, but it failed here for the same reason the rest do — the
//      pinned host could not produce the bytes — and it was fixable only in that
//      host, which 0.1.0 did. The family is currently EMPTY; it is kept named
//      because the shape recurs and its remedy is not the others'.
//
// The five families are the CAUSES this quarantine has met, dated 0.0.1, not a
// description of what is in the map now — families 1, 3 and 5 are empty as of
// the sixth pass below, and family 2 holds only the `Expr` binding. Each pass
// records what it measured against its own version; the map itself is the only
// present tense in this file.
//
// None of this is projector lag, and none of it is fixable in this repo — which
// is why these are named with their cause rather than left to fail. The day
// fuaran-py grows one of these constructs, the entries naming it are removable by
// search, and the test below FAILS if one starts round-tripping while still
// listed, so the set cannot quietly outlive its cause.
//
// That first clause is an INTENT rather than an invariant, and the 0.1.0 pass
// below is what proved the difference: six entries turned out to be projector lag
// wearing a host-lag reason, and they were found only because a stale reason was
// re-derived by running the fixture. A quarantine is exactly as honest as its last
// measurement, and a reason nobody re-runs decays into a claim.
//
// RE-MEASURED 2026-09-07 against fuaran-py **0.0.6** — the version CI pins, and
// five releases past the 0.0.1 the original note measured. Three things came out
// of that pass and each is worth stating, because the set is only as honest as
// its last measurement:
//
//   • THREE ENTRIES WERE REMOVED. 0.0.6 grew `Media.tracks` / `Media.transcript`,
//     so `media-audio-transcript-1`, `media-video-captions-1` and
//     `media-video-tracks-2` now round-trip once the projector emits them — which
//     it does. The self-clearing test below is what forced the removal rather
//     than merely inviting it.
//   • TWENTY-TWO ENTRIES WERE ADDED, all of them corpus growth this repo cannot
//     absorb: chart annotations, the node-level tooltip, `Format.Since`, the
//     anchored-popover pair, the grid print/transfer trio, the new field controls
//     and the non-BMP key ordering. Each was measured the same way — project,
//     execute, read the executor's `AttributeError` or the byte difference.
//   • SOME SURVIVING REASONS NAME A 0.0.1 CAUSE. `Column has no field` is the
//     clearest: 0.0.6 DOES carry `Column.field_name`, and those fixtures now fail
//     on a different slot of the same record. They stay because they still fail
//     — verified, not assumed — but their wording is older than their cause, and
//     re-deriving each is work this pass did not do.
//
// Every entry below was additionally checked against fuaran-py `origin/main`, so
// the release-lag half is separable: `Format.Since` and `Binding.Now.grain` are
// modelled on main since f473bd1, and the UTF-16 key ordering since 71225ed, so
// a host release plus a CI pin raise clears `format-since` and
// `custom-nonascii-keys` with no work here. Nothing else in the set is modelled
// anywhere yet.
//
// SECOND PASS, same day, against the same 0.0.6 — the corpus moved 27 commits
// under the first measurement and arrived carrying eleven more fixtures, all of
// them failing on BOTH arms. The TypeScript half was projector lag and was
// taught (see the sibling arm's note); the Python half is entirely host lag and
// is the block at the end of this map. What is worth recording is the SHAPE of
// that half, because it inverts the ratio above: nine of the eleven are already
// on fuaran-py main — `SwitchCase.when` and `UiNode.visible` since e956a1f,
// `Navigate` over a `TextSource` with a `target` since ec18597, `Action.Confirm`
// and `Action.Focus` since dd40204 — so a release past 0.0.6 and a CI pin raise
// clears them with no code written anywhere, exactly as `format-since` does.
// Only `Binding.Expr` (2 ids) is unmodelled on every branch: the Python
// `Binding` union is still `Static | State | Filter | Selection | Now |
// FormatBinding | Local`, and `Local` still emits `onCommit` unconditionally
// with no `codec` / `commitTo` beside it (1 id).
//
// One SECOND cause was removed rather than listed, and the distinction is the
// point of measuring twice: `form-local-declared` also carried a spurious
// `onChange` because the projector built `t.NumberField(value)` and took the
// record's `on_change=True` default. That record CAN say False — it is not a
// hardcoded sentinel like `TextField`'s — so the projector now reads the wire's
// key and the fixture fails on its one real cause. An entry naming two causes
// when one of them is ours is how a quarantine starts drifting away from what
// it describes.
//
// ── THIRD PASS, 2026-09-07, RE-MEASURED AGAINST fuaran-py **0.1.0** ──────────
//
// 0.1.0 is on PyPI and the CI pin was raised to it in all three workflows that
// install the host (ci / conformance / azure-static-web-apps-showcase — the
// conformance one had been left at 0.0.3 and was raised with them). THIRTEEN
// entries were removed, every one of them forced by the self-clearing test below
// rather than merely invited by it, and FIFTEEN surviving reasons were re-derived
// by execution. (The set's size is no longer stated here; it is computed — see
// the fourth pass below and `QUARANTINE_CENSUS`.)
//
// The thirteen split three ways, and the split is the finding:
//
//   • ONE cleared on the pin raise ALONE, with nothing written anywhere:
//     `custom-nonascii-keys`. 0.1.0's canonical writer sorts object keys by
//     UTF-16 code unit.
//   • SIX cleared once the PYTHON LEG OF THE PROJECTOR was taught the construct
//     0.1.0 had grown: `format-since` (`t.FmtSince`), `action-navigate-target`
//     and `action-navigate-bound` (`Navigate`'s `target`, and a route that is a
//     `TextSource`), `action-focus` (`t.Focus`), `action-confirm-cancel`
//     (`t.Confirm`) and `switch-predicate-only` (`SwitchCase.when`). This is the
//     half the previous pass could not predict and did not: it recorded these as
//     host lag, which they were, and the release turned them into PROJECTOR lag
//     rather than into passes. A quarantine entry that clears on a version bump
//     is the exception; the rule is that the bump moves the work rather than
//     removing it.
//   • SIX more were never host lag at all, and were found only because
//     re-deriving a stale reason means running the fixture. `Column.field_name`
//     and `DataGrid.row_key_field` have been modelled since 0.0.6 and the
//     projector emitted neither, so twelve entries carried the reason `Column
//     has no field` while failing on the projector; teaching the two slots (plus
//     `t.TonedPillColumnKind`, `cp.Param` and `TransformBinding.params`, each
//     modelled and each unemitted) cleared `grid-editable-state`,
//     `grid-field-named`, `grid-toned-pill`, `switch-on-selection`,
//     `scalar-transform-composition` and `grid-transform-param`.
//
// What the fifteen re-derived reasons say, in one sentence each: the seven
// surviving grid entries name the DataGrid / Column slots 0.1.0 genuinely lacks
// (`sortStateKey`, `defaultSort`, `pageSize`, `pageStateKey`, `editStateKey`,
// `reorderable`, per-column `sortable` / `editable`) instead of a `field` slot
// that exists; `badge-transform-live` and `shared-source-seeded-pair` name a
// STATE-bound transform source rather than the `Live` one the old wording
// claimed (the fixture's source is `{"$type":"State"}` — the name misled);
// `switch-predicate`, `node-visible` and `action-confirm` name the second cause
// that was always behind the first, each a construct 0.1.0 still does not model
// (`Binding.Expr`, `Binding.Query`, `Action.Call`); and three `grid-*` entries
// gain the projector-side half they also carry, so the next reader does not
// expect a `Binding.Query` fix alone to clear them.
//
// Nothing in the surviving set was then believed to be release lag: every entry
// was executed against 0.1.0 this pass and fails on a construct absent from it.
//
// ── FOURTH PASS, 2026-09-07 — EVERY REASON CARRIES A FALSIFIER ───────────────
//
// The three passes above share one weakness, and it is not any of the reasons
// they got wrong — it is that a wrong reason could only ever be found by a human
// re-deriving it. Twelve entries blamed a host that had carried
// `Column.field_name` since 0.0.6, and six more blamed a release for what the
// projector had never emitted. The self-clearing test below has always checked
// the OUTCOME (a quarantined fixture that starts round-tripping fails), and
// nothing at all checked the CLAIM.
//
// So each entry now carries a machine-readable `construct` beside the human
// sentence, and two probes falsify it. The prose reason stays, because a
// sentence is what a reader needs and a token is what a test needs.
//
// THE TOKEN GRAMMAR — a construct token is a HOST-MODEL PATH, resolved against
// the installed interpreter by `resolve_construct` in `python_exec.py`:
//
//   `t.Drawing`                  a symbol the module must export. Module prefixes
//                                are the projector's own namespace (`t`, `cp`,
//                                `binding`, …); bare names resolve in `t`.
//   `Binding.Query`              a CASE of a union alias — resolved from the
//                                union's own arguments, so the day the union
//                                grows the case the probe sees it.
//   `Chart.annotations`          a FIELD of a record.
//   `optional:Modal.on_dismiss`  the record can OMIT this slot. This is the
//                                closure-sentinel family's falsifier, and it
//                                covers both shapes that force a key into the
//                                wire: no field at all (`TextField`, whose
//                                `to_wire` writes `"onChange": CLOSURE`) and a
//                                field that cannot be None (`Modal.on_dismiss`,
//                                defaulted to an empty `Chain`). A token naming
//                                neither a field nor a wire key the record
//                                writes is REFUSED as a typo, not read as a
//                                verdict.
//
// THE TWO PROBES, and the failure each one exists to produce:
//
//   1. THE HOST PROBE. For an entry blaming the host, the pinned host must NOT
//      model the construct. If it does, the entry is a claim about a gap that
//      has been closed and the test fails by name — which is the mirror of the
//      self-clearing test, applied to the reason rather than to the outcome.
//   2. THE PROJECTOR PROBE. When the host DOES model it, the two causes are told
//      apart by the projected source: a construct the projector never emits is
//      PROJECTOR lag, and the failure names `app/Projection.fs` and asks for
//      `arm: 'projector'` — the 2026-09-07 `Column.field_name` finding, made
//      mechanical. A construct the projector DOES emit means the entry has
//      simply outlived its cause: remove it.
//
// The projector probe reads generated source, so what it looks for is the
// projector's own emission rule (stated in `app/Projection.fs`'s Python-leg
// header): every value is built from a TYPED RECORD, so a construct in the
// output appears either as `t.X(` / `cp.X(` or as a `snake_case=` keyword. It is
// a text probe and says so; both its directions FAIL rather than pass, so it can
// misdirect a message but never hold an entry it should have dropped.
//
// `arm` says which repository owns the cause, and `both` is not a hedge: it was
// the three grid ids whose `Binding.Query` source is host lag AND whose
// `exportable` / `keepRowsTogether` / `repeatHeader` were slots the host modelled
// and this projector did not emit. Phase 1581 emitted them, so `both` is EMPTY
// and those three are plain host lag — the arm is kept because the SHAPE recurs
// on every release that closes one half of a two-cause entry, and because an
// entry naming only the half that happens to be someone else's is how the
// projector's own lag went unrecorded for three passes. The counts by arm are
// `QUARANTINE_CENSUS`, asserted against the map's own tally below, so no prose in
// this file states a number the map can contradict.
// ── FIFTH PASS, 2026-09-07 — RE-MEASURED AGAINST fuaran-py **0.2.0** ────────
//
// 0.2.0 is on PyPI and the pin was raised to it in all three workflows. It is
// the release carrying Phases 1576 (handlers and values optional, plus
// `t.RangeField`), 1577 (the DataGrid / Chart / tooltip / Link / Table field
// widening) and 1585 (`Chart.stacked` omit-at-default) — so it is the largest
// single move this quarantine has ever been measured across, and the shape of
// the result is the fourth pass's thesis confirmed at scale.
//
// THIRTY-EIGHT ENTRIES WERE REMOVED, and **not one of them cleared on the pin
// raise alone**. Every single one needed the projector taught the construct the
// release had grown — the chart's eight new slots, the grid's declarative sort /
// page / edit trio and its five transfer-export-print flags, the per-column
// `sortable` / `editable`, the static table's `sortable` / `defaultSort`,
// `Link.protection`, `UiNode.tooltip`, the buffer's `commitTo` / `codec`, and,
// across the whole field vocabulary, a handler flag and an optional `value` read
// from the WIRE rather than taken from the record's default. The 0.1.0 pass
// observed that a bump "moves the work rather than removing it" and put six
// entries behind that claim; here it is thirty-eight out of thirty-eight.
//
// TWO ENTRIES WERE RE-CLASSED AFTER THEIR REASON WAS RE-DERIVED BY EXECUTION,
// and both had been masked by a token that could no longer falsify anything:
//
//   • `multiselect-chip-list-param` blamed `optional:Select.on_change`. In 0.2.0
//     that slot is a `bool` flag, and `optional:` asks whether a field admits
//     `None` — so the probe answered "the host cannot omit it" and the entry
//     held, VACUOUSLY, while the fixture actually failed on something else
//     entirely: the compute layer's `in` predicate, which `cp` has modelled all
//     along and `pyColExpr` had no arm for, so it fell through to the `col`
//     fallback and projected `cp.Col('')`. That is the identical trap the
//     projector's own `param` arm was written to close. Fixed in
//     `app/Projection.fs`; the entry is gone.
//   • `composite-tabs-panels` blamed `optional:Tabs.on_select` for the same
//     vacuous reason. Its real surviving cause is `Action.Call` on `onSubmit`,
//     which 0.2.0 still does not model. Re-tokened, not removed.
//
// THE LESSON, because it is a new one and it is about the falsifier rather than
// about any entry: an `optional:` token stops falsifying the moment the host
// turns that slot from a closure sentinel into a BOOL FLAG. The field then exists
// and cannot be `None`, so the probe keeps reporting "the host cannot omit this"
// — which is now false in the only sense that matters, since the flag omits the
// wire key perfectly well. The fourth pass built the probes to catch a reason
// that had outlived its cause, and this is a reason whose PROBE outlived its
// cause. Both entries were caught by the round-trip half rather than the claim
// half, exactly as the pre-1578 passes were. A follow-up worth filing: teach
// `optional:` to read a `bool`-typed handler field as omittable-by-flag.
//
// THREE `arm: 'both'` ENTRIES BECAME PLAIN HOST LAG. `grid-exportable-1`,
// `grid-keep-rows-together-1` and `grid-repeat-header-1` each named a
// `Binding.Query` source AND a DataGrid slot the projector did not emit; the
// second half is emitted from this phase, so only the Query source survives and
// the `both` arm is empty. `grid-declared-edit` and `tooltip-metric-1` were
// re-tokened the same way, each to the second cause that was always behind the
// first (`Binding.Query`, `TextSource.I18n`).
//
// WHAT SURVIVES IS ALL HOST LAG, AND ALL OF IT WAS EXECUTED THIS PASS — no entry
// below is held on a reason inherited from an earlier measurement. `Binding.Expr`
// is worth naming: this phase was scoped to land its predicate-slot emission, and
// it could not, because 0.2.0's `Binding` union is still
// `Static | State | Filter | Selection | Now | FormatBinding | Local`. The four
// ids that need it (`expr-scalar`, `expr-params-state-selection`,
// `switch-predicate`, `node-visible`) are host lag, not projector lag, and the
// probe agrees.
// ── SIXTH PASS, 2026-09-07 — RE-MEASURED AGAINST fuaran-py **0.3.0** ────────
//
// 0.3.0 is on PyPI and the pin was raised to it in all three workflows. It is
// the release carrying Phases 1579 (the Drawing / Fact / Mount node kinds) and
// 1580 (`Binding.Query`, `Binding.Invoke`, `Action.Call` / `Invoke` / `AiTool`,
// and `TextSource.I18n`). TWENTY-EIGHT ENTRIES WERE REMOVED and SIX survive, so
// what is left here is smaller than the set of causes the family list above
// enumerates — read that list as the history it is and this note as the state.
//
// THE SPLIT IS THE FINDING, AND IT INVERTS THE FIFTH PASS'S. Twelve entries
// cleared ON THE PIN RAISE ALONE — every `Drawing`, `Fact` and `Mount` id — where
// the 0.2.0 pass cleared none that way and concluded that a bump "moves the work
// rather than removing it". Both observations are right and the discriminator is
// not the release: it is whether the projector was ALREADY emitting the
// construct. It was, for all three kinds, because a kind with no constructor arm
// falls to `pyGenericNode`, which spells `t.<WireTag>(<wire keys snake-cased>)` —
// so the day the host grew the class, the fallback's guess became correct.
//
// THAT IS ALSO WHY TWELVE PASSING FIXTURES WERE NOT LEFT ALONE. The fallback
// reaches the host's records but fills their slots with RAW DICTS
// (`t.Drawing(view_box={'height': 100, …})`), and the host's lowering passes a
// dict straight through. So the arm would have gone on reporting green had 0.3.0
// modelled `Drawing` and none of its nine shapes — it would have been certifying
// a host gap as conformance, which is the exact failure the leg's header forbids
// for `UiNode.visible` and names as the reason the quarantine means anything.
// `app/Projection.fs` now emits `t.ViewBox` / `t.DrawPoint` / `t.DrawStyle`, the
// five curve commands, the nine shapes, `t.GuestChannel` and the `FragmentArg`
// vocabulary through the real `fuaran.drawing` / `.fact` / `.mount`
// constructors. The fallback's own limit is visible in the one id it could not
// carry: `mount-2` read the wire tag `Str` as a class name and reached for a
// `t.Str` that has never existed in any release.
//
// SIXTEEN CLEARED ONCE THE PROJECTOR WAS TAUGHT the constructs 0.3.0 grew —
// `t.Query`, `t.Invoke` / `t.InvokeArg`, `t.I18n`, `t.Call` with
// `t.IntoState` / `t.IntoQuery`, and `t.AiTool`. Two details are worth keeping:
// the `Call` target's Python case names deliberately differ from the wire tags
// they encode (`t.IntoState` writes `{"$type":"State"}`), because `State` and
// `Query` are already taken by the binding union in the same module; and
// `on_result` is read from the wire KEY's presence rather than left to the
// record's default, for the reason `pyHandler` states on the projector side.
//
// ONE EXECUTOR CHANGE WAS NEEDED and it is the only builtin the eval namespace
// admits: §7's non-finite sentinels ride the wire as the strings `"NaN"` /
// `"Infinity"` / `"-Infinity"` and are FLOATS in every typed slot that carries
// them, which Python spells only as `float('nan')` — there is no literal. The
// generic fallback never needed it because it passed the STRING through, which
// round-tripped by coincidence. `float` resolves to a number and to no part of
// the host surface, so it cannot stand in for a construct the model omits.
//
// WHAT SURVIVES IS ALL HOST LAG AND ALL OF IT WAS EXECUTED THIS PASS. Four ids
// need `Binding.Expr`, which 1580 declined with its reasons: 0.3.0's `Binding`
// union is `Static | State | Filter | Selection | Now | FormatBinding | Local |
// Query | Invoke`, and the probe agrees. Two need a `TransformSource`-typed
// `TransformBinding.source`, still a bare `DataSource` in the model. The
// `Binding.Local` declarative-buffer entry the fifth pass expected to survive is
// NOT here: it never had one, and this is the second pass to note its absence
// rather than a third to imply it.
//
// The `optional:` follow-up the fifth pass filed is still open and is still
// worth doing — no entry now standing uses that token, so the trap it names is
// dormant rather than fixed, and it will bite the next entry that reaches for it.

interface Quarantined {
  /** The host-model path this entry claims is absent — see the grammar above. */
  readonly construct: string;
  /** Which repository owns the cause. `both` additionally sets `projectorConstruct`. */
  readonly arm: 'host' | 'projector' | 'both';
  /** The sentence for the human. Free text; the `construct` is what the probes read. */
  readonly reason: string;
  /**
   * For `arm: 'both'` — the construct the host DOES model and `app/Projection.fs`
   * does not emit. Probed exactly as a `projector` entry's `construct` is.
   */
  readonly projectorConstruct?: string;
}

const PY_UNMODELLED = new Map<string, Quarantined>([
  // What survives the 2026-09-07 SIXTH pass, re-measured against 0.3.0. Six
  // entries, two causes, both of them host lag with the probe agreeing; the
  // twenty-eight the release and this phase cleared are gone from the map and
  // accounted for in the sixth-pass note above rather than commented out here.

  // 1 — no typed binding case. `Binding` is Static | State | Filter | Selection
  // | Now | FormatBinding | Local | Query | Invoke, so a predicate binding has
  // no spelling in any slot. 1580 declined to model `Expr` with its reasons, so
  // this is a standing gap rather than a release still in flight.
  ['expr-scalar', { construct: 'Binding.Expr', arm: 'host', reason: 'no Binding.Expr' }],
  [
    'expr-params-state-selection',
    { construct: 'Binding.Expr', arm: 'host', reason: 'no Binding.Expr' },
  ],
  [
    'switch-predicate',
    {
      construct: 'Binding.Expr',
      arm: 'host',
      reason:
        'SwitchCase.when is modelled from 0.1.0 and emitted; the predicate is a Binding.Expr, which is not modelled',
    },
  ],
  [
    'node-visible',
    {
      construct: 'Binding.Expr',
      arm: 'host',
      reason:
        'UiNode.visible is modelled from 0.1.0 and emitted, and its Binding.Query predicate is modelled from 0.3.0 and emitted; the remaining predicate is a Binding.Expr, which is not modelled',
    },
  ],

  // 2 — a record narrower than the wire. `TransformBinding.source` is a bare
  // `DataSource` rather than the wire's `TransformSource` DU, so a source that
  // is `{"$type":"State"}` has no spelling at all — the fixture reads as a
  // literal table where the wire names a state key.
  [
    'badge-transform-live',
    {
      construct: 'cp.TransformSource',
      arm: 'host',
      reason: 'TransformBinding.source is a bare DataSource — a State-bound source has no spelling',
    },
  ],
  [
    'shared-source-seeded-pair',
    {
      construct: 'cp.TransformSource',
      arm: 'host',
      reason: 'TransformBinding.source is a bare DataSource — a State-bound source has no spelling',
    },
  ],
]);

/** The map's own tally, by arm — computed, and rendered into the census test's name. */
const tallyByArm = () => {
  const tally = { host: 0, projector: 0, both: 0 };
  for (const q of PY_UNMODELLED.values()) tally[q.arm] += 1;
  return tally;
};

/**
 * The quarantine's size, by which repository owns the cause. DECLARED here and
 * asserted against `tallyByArm()` below, so the one number a reader sees is the
 * one the map actually holds — the three passes above each carried counts in
 * prose, and prose cannot be wrong out loud.
 */
const QUARANTINE_CENSUS = { host: 6, projector: 0, both: 0 } as const;

/**
 * What the projector's generated source must contain for it to be EMITTING the
 * construct a token names.
 *
 * The projector's Python leg builds every value from a typed record (its own
 * rule — see `app/Projection.fs`'s Python-leg header), so a construct it emits
 * appears in exactly one of two shapes: a qualified constructor (`t.Drawing(`,
 * `cp.Param(`) for a class or union case, or a keyword argument (`field_name=`)
 * for a record slot. Anchoring on those two shapes rather than on the bare word
 * is what keeps a fixture id like `'drawing-1'` from reading as an emission of
 * `t.Drawing`.
 */
const emissionPattern = (construct: string): RegExp => {
  const leaf = construct
    .replace(/^optional:/, '')
    .split('.')
    .pop()!;
  return /^[a-z]/.test(leaf)
    ? new RegExp(String.raw`\b${leaf}\s*=`)
    : new RegExp(String.raw`\b[a-z]+\.${leaf}\b`);
};

interface ExecResult {
  readonly id: string;
  readonly ok: boolean;
  readonly encoded?: string;
  readonly error?: string;
}

/** One construct token, resolved against the INSTALLED host by the executor. */
type ConstructVerdict = { models: boolean; detail: string } | { error: string };

let executed: Map<string, ExecResult> = new Map();
let projected: Map<string, string> = new Map();
let constructs: Record<string, ConstructVerdict> = {};
let fatal: string | undefined;

beforeAll(() => {
  const cases = nodeFixtures.map((f) => ({
    id: f.id,
    expr: projectPythonExpr(readFileSync(resolve(corpusDir, f.inputFile), 'utf8').trim()) as string,
  }));
  projected = new Map(cases.map((c) => [c.id, c.expr]));

  // Every token any entry names, host- and projector-side alike, resolved in the
  // same process that executes the corpus — so the probe and the round-trip can
  // never disagree about which interpreter they measured.
  const tokens = [
    ...new Set(
      [...PY_UNMODELLED.values()].flatMap((q) =>
        q.projectorConstruct ? [q.construct, q.projectorConstruct] : [q.construct],
      ),
    ),
  ];

  const proc = spawnSync(resolvePython(), [resolve(here, 'python_exec.py')], {
    input: JSON.stringify({ cases, constructs: tokens }),
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  });

  if (proc.error) {
    fatal = `could not run the Python executor (${resolvePython()}): ${proc.error.message}`;
    return;
  }
  if (proc.status !== 0) {
    fatal = `the Python executor exited ${proc.status}:\n${proc.stderr}`;
    return;
  }

  const payload = JSON.parse(proc.stdout) as {
    fatal?: string;
    results?: ExecResult[];
    constructs?: Record<string, ConstructVerdict>;
  };
  if (payload.fatal) {
    fatal = payload.fatal;
    return;
  }

  executed = new Map((payload.results ?? []).map((r) => [r.id, r]));
  constructs = payload.constructs ?? {};
}, 300_000);

describe('Python projection conformance (Node corpus)', () => {
  it('the Python executor ran', () => {
    // A hard failure, never a skip: a conformance arm that goes green without
    // its oracle is worse than no arm at all. Install the host with
    // `python -m venv .venv && .venv/…/pip install fuaran-py==0.3.0`, or point
    // FUARAN_PY_PYTHON at an interpreter that already has it.
    expect(fatal, fatal ?? '').toBeUndefined();
  });

  it('the corpus is present and non-trivial', () => {
    expect(nodeFixtures.length).toBeGreaterThanOrEqual(70);
  });

  it('every unmodelled id names a real fixture', () => {
    const ids = new Set(nodeFixtures.map((f) => f.id));
    for (const q of PY_UNMODELLED.keys()) {
      expect(ids.has(q), `unmodelled '${q}' is not in the corpus — remove it`).toBe(true);
    }
  });

  const census = tallyByArm();
  it(`the quarantine census — host ${census.host} / projector ${census.projector} / both ${census.both} — is the map's own tally`, () => {
    // Generated, not narrated: the counts are in this test's NAME, so every run
    // prints them, and the declared constant is what makes a drifted count fail
    // rather than merely read as out of date. The prose above quotes no total.
    expect(
      census,
      `QUARANTINE_CENSUS is stale — the map now tallies host ${census.host} / projector ${census.projector} / both ${census.both}`,
    ).toEqual({ ...QUARANTINE_CENSUS });
  });

  it('every construct token resolves against the pinned host', () => {
    // A token the executor cannot resolve is a claim about nothing. It is
    // reported here, once, rather than inside each entry probe — a misspelled
    // record name would otherwise read as "the host lacks it", which is exactly
    // the vacuous hold this phase exists to make impossible.
    const unresolved = Object.entries(constructs)
      .filter(([, v]) => 'error' in v)
      .map(([token, v]) => `${token}: ${(v as { error: string }).error}`);
    expect(unresolved, `unresolvable construct token(s):\n  ${unresolved.join('\n  ')}`).toEqual(
      [],
    );
  });

  for (const f of nodeFixtures) {
    const wireOf = () => readFileSync(resolve(corpusDir, f.inputFile), 'utf8').trim();
    const entry = PY_UNMODELLED.get(f.id);

    if (entry) {
      it(`${f.id} — the reason's construct is where it says it is (${entry.construct})`, () => {
        if (fatal) return; // the executor test above already fails, loudly

        /** Host lag: the pinned host must NOT model what the entry blames it for. */
        const hostSide = (token: string) => {
          const verdict = constructs[token];
          if (!verdict || 'error' in verdict) return; // reported by the token test above
          if (!verdict.models) return; // the entry holds

          const source = projected.get(f.id) ?? '';
          const emitted = emissionPattern(token).test(source);
          expect(
            verdict.models,
            emitted
              ? `'${f.id}' blames the host for '${token}', but the pinned host MODELS it (${verdict.detail}) and the projector already emits it — REMOVE the entry or re-derive its reason`
              : `'${f.id}' blames the host for '${token}', but the pinned host MODELS it (${verdict.detail}) and app/Projection.fs never emits it — this is PROJECTOR lag: re-class the entry as arm: 'projector' (or teach app/Projection.fs)`,
          ).toBe(false);
        };

        /** Projector lag: the host models it and app/Projection.fs must not emit it. */
        const projectorSide = (token: string) => {
          const verdict = constructs[token];
          if (!verdict || 'error' in verdict) return;
          expect(
            verdict.models,
            `'${f.id}' claims app/Projection.fs lags on '${token}', but the pinned host does not model it (${verdict.detail}) — this is HOST lag: re-class the entry as arm: 'host'`,
          ).toBe(true);
          const source = projected.get(f.id) ?? '';
          expect(
            emissionPattern(token).test(source),
            `'${f.id}' claims app/Projection.fs lags on '${token}', but the projected source already emits it — REMOVE the entry or re-derive its reason`,
          ).toBe(false);
        };

        if (entry.arm === 'host' || entry.arm === 'both') hostSide(entry.construct);
        if (entry.arm === 'projector') projectorSide(entry.construct);
        if (entry.arm === 'both') {
          expect(
            entry.projectorConstruct,
            `'${f.id}' is arm: 'both' and must name its projectorConstruct`,
          ).toBeDefined();
          projectorSide(entry.projectorConstruct!);
        }
      });

      it(`${f.id} is unmodelled by fuaran_py (${entry.reason})`, () => {
        const result = executed.get(f.id);
        if (!result?.ok) return; // still un-projectable — the entry holds
        expect(
          result.encoded,
          `'${f.id}' now round-trips — fuaran-py grew the construct; REMOVE it from PY_UNMODELLED`,
        ).not.toBe(wireOf());
      });
    } else {
      it(`${f.id} round-trips byte-identically`, () => {
        const result = executed.get(f.id);
        expect(result, `no executor result for '${f.id}'`).toBeDefined();
        expect(result!.ok, `projected Python for ${f.id} failed to execute: ${result!.error}`).toBe(
          true,
        );
        expect(
          result!.encoded,
          `projected Python source for ${f.id} must re-encode byte-identically`,
        ).toBe(wireOf());
      });
    }
  }
});
