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
 * `pip install fuaran-py==0.1.0`); FUARAN_PY_PYTHON overrides it for CI, where
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
// 2026-09-07 against 0.0.6, and again the same day against **0.1.0**, the
// version CI now pins — see the re-measurement notes below the family list,
// which are where the record of what moved lives.
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
// `arm` says which repository owns the cause, and `both` is not a hedge — it is
// the three grid ids whose `Binding.Query` source is host lag AND whose
// `exportable` / `keepRowsTogether` / `repeatHeader` are slots 0.1.0 models and
// this projector does not emit. The counts by arm are `QUARANTINE_CENSUS`,
// asserted against the map's own tally below, so no prose in this file states a
// number the map can contradict.
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
  // 1 — no typed node kind.
  ['mount-1', { construct: 't.Mount', arm: 'host', reason: 'no t.Mount' }],
  ['mount-2', { construct: 't.Mount', arm: 'host', reason: 'no t.Mount' }],
  ['fact-1', { construct: 't.Fact', arm: 'host', reason: 'no t.Fact' }],
  ['now-environment-binding', { construct: 't.Fact', arm: 'host', reason: 'no t.Fact' }],
  ['master-detail-multi-field', { construct: 't.Fact', arm: 'host', reason: 'no t.Fact' }],
  ['master-detail-preselected', { construct: 't.Fact', arm: 'host', reason: 'no t.Fact' }],
  [
    'master-detail-preselected-second-row',
    { construct: 't.Fact', arm: 'host', reason: 'no t.Fact' },
  ],
  ['drawing-1', { construct: 't.Drawing', arm: 'host', reason: 'no t.Drawing' }],
  ['drawing-empty', { construct: 't.Drawing', arm: 'host', reason: 'no t.Drawing' }],
  ['drawing-nonfinite-sentinels', { construct: 't.Drawing', arm: 'host', reason: 'no t.Drawing' }],
  ['drawing-rotated-labels', { construct: 't.Drawing', arm: 'host', reason: 'no t.Drawing' }],
  ['drawing-tipped-shapes', { construct: 't.Drawing', arm: 'host', reason: 'no t.Drawing' }],

  // 2 — no typed binding / action case.
  ['query-dependson', { construct: 'Binding.Query', arm: 'host', reason: 'no Binding.Query' }],
  ['metric-invoke', { construct: 'Binding.Invoke', arm: 'host', reason: 'no Binding.Invoke' }],
  [
    'image-caption-i18n-1',
    { construct: 'TextSource.I18n', arm: 'host', reason: 'no TextSource.I18n' },
  ],
  ['btn-invoke', { construct: 'Action.Invoke', arm: 'host', reason: 'no Action.Invoke' }],
  ['btn-json-payloads', { construct: 'Action.AiTool', arm: 'host', reason: 'no Action.AiTool' }],
  ['call-into', { construct: 'Action.Call', arm: 'host', reason: 'no Action.Call' }],

  // 3 — a slot the record cannot omit: either no field at all (the `to_wire`
  // writes a CLOSURE sentinel) or a field that cannot be None. `optional:` is
  // the falsifier for both.
  [
    'form-declarative',
    {
      construct: 'optional:TextField.on_change',
      arm: 'host',
      reason: 'TextField hardcodes onChange',
    },
  ],
  [
    'form-declarative-minimal',
    {
      construct: 'optional:TextField.on_change',
      arm: 'host',
      reason: 'TextField hardcodes onChange',
    },
  ],
  [
    'form-field-rules',
    {
      construct: 'optional:TextField.on_change',
      arm: 'host',
      reason: 'TextField hardcodes onChange',
    },
  ],
  [
    'composite-tabs-panels',
    {
      construct: 'optional:Tabs.on_select',
      arm: 'host',
      reason: 'Tabs hardcodes onSelect; TextField hardcodes onChange',
    },
  ],
  [
    'form-toggle',
    {
      construct: 'optional:CheckboxField.on_toggle',
      arm: 'host',
      reason: 'CheckboxField hardcodes onToggle',
    },
  ],
  [
    'form-date-range',
    {
      construct: 'optional:DateRangeField.on_change',
      arm: 'host',
      reason: 'DateRangeField hardcodes onChange',
    },
  ],
  [
    'filters-declarative',
    {
      construct: 'optional:TextFilter.on_change',
      arm: 'host',
      reason: 'TextFilter hardcodes onChange',
    },
  ],
  [
    'filters-date-range',
    {
      construct: 'optional:DateRangeField.on_change',
      arm: 'host',
      reason: 'DateRangeField hardcodes onChange',
    },
  ],
  [
    'frag-stdlib-filter-bar',
    {
      construct: 'optional:TextFilter.on_change',
      arm: 'host',
      reason: 'TextFilter hardcodes onChange',
    },
  ],
  [
    'filterable-static-dashboard',
    {
      construct: 'optional:ChoiceFilter.on_change',
      arm: 'host',
      reason: 'ChoiceFilter hardcodes onChange',
    },
  ],
  [
    'multiselect-chip-list-param',
    { construct: 'optional:Select.on_change', arm: 'host', reason: 'Select hardcodes onChange' },
  ],
  [
    'controls-declarative',
    { construct: 'optional:Tabs.on_select', arm: 'host', reason: 'Tabs hardcodes onSelect' },
  ],
  [
    'controls-closure',
    { construct: 'Tabs.on_select_tag', arm: 'host', reason: 'Tabs has no onSelectTag' },
  ],
  [
    'grid-bound-sort',
    {
      construct: 'DataGrid.sort_state_key',
      arm: 'host',
      reason: 'DataGrid has no sortStateKey / defaultSort; Column has no sortable',
    },
  ],
  [
    'grid-declared-edit',
    {
      construct: 'DataGrid.edit_state_key',
      arm: 'host',
      reason: 'DataGrid has no editStateKey; Column has no editable; source is a Binding.Query',
    },
  ],
  [
    'grid-paged',
    {
      construct: 'DataGrid.page_size',
      arm: 'host',
      reason: 'DataGrid has no pageSize / pageStateKey',
    },
  ],
  [
    'grid-paged-sorted',
    {
      construct: 'DataGrid.page_size',
      arm: 'host',
      reason: 'DataGrid has no pageSize / pageStateKey / sortStateKey',
    },
  ],
  [
    'grid-reorderable',
    {
      construct: 'DataGrid.edit_state_key',
      arm: 'host',
      reason: 'DataGrid has no editStateKey / reorderable',
    },
  ],
  [
    'grid-sort-state-key',
    { construct: 'DataGrid.sort_state_key', arm: 'host', reason: 'DataGrid has no sortStateKey' },
  ],
  [
    'shared-source-seeded-pair',
    {
      construct: 'cp.TransformSource',
      arm: 'host',
      reason: 'TransformBinding.source is a bare DataSource — a State-bound source has no spelling',
    },
  ],

  // 4 — a record narrower than the wire.
  [
    'chart-axis-titles',
    { construct: 'Chart.x_title', arm: 'host', reason: 'Chart has no subtitle / xTitle / yTitle' },
  ],
  [
    'chart-data-labels',
    { construct: 'Chart.data_labels', arm: 'host', reason: 'Chart has no dataLabels' },
  ],
  [
    'chart-legend-position',
    { construct: 'Chart.legend_position', arm: 'host', reason: 'Chart has no legendPosition' },
  ],
  ['chart-temporal-x', { construct: 'Chart.x_scale', arm: 'host', reason: 'Chart has no xScale' }],
  [
    'chart-value-format',
    { construct: 'Chart.value_format', arm: 'host', reason: 'Chart has no valueFormat' },
  ],
  [
    'badge-transform-live',
    {
      construct: 'cp.TransformSource',
      arm: 'host',
      reason: 'TransformBinding.source is a bare DataSource — a State-bound source has no spelling',
    },
  ],
  [
    'link-protected-1',
    { construct: 'Link.protection', arm: 'host', reason: 'Link has no protection' },
  ],
  [
    'table-sortable-1',
    { construct: 'Table.sortable', arm: 'host', reason: 'Table has no sortable / defaultSort' },
  ],
  [
    'transfer-board',
    { construct: 'DataGrid.reorderable', arm: 'host', reason: 'DataGrid has no reorderable' },
  ],
  [
    'chart-annotation-bands',
    { construct: 'Chart.annotations', arm: 'host', reason: 'Chart has no annotations' },
  ],
  [
    'chart-annotation-events',
    { construct: 'Chart.annotations', arm: 'host', reason: 'Chart has no annotations' },
  ],
  [
    'chart-annotations',
    { construct: 'Chart.annotations', arm: 'host', reason: 'Chart has no annotations' },
  ],
  [
    'tooltip-button-1',
    { construct: 'UiNode.tooltip', arm: 'host', reason: 'UiNode has no tooltip' },
  ],
  [
    'tooltip-icon-button-1',
    { construct: 'UiNode.tooltip', arm: 'host', reason: 'UiNode has no tooltip' },
  ],
  [
    'tooltip-metric-1',
    { construct: 'UiNode.tooltip', arm: 'host', reason: 'UiNode has no tooltip' },
  ],

  // 3 (continued) — the same unomittable slot, in the controls added since 0.0.1.
  // Each of the four new field records writes `onChange` and `value` into the
  // wire unconditionally, so a canonical minimal control cannot be reached.
  [
    'filters-rating-colour',
    {
      construct: 'optional:RatingField.on_change',
      arm: 'host',
      reason: 'RatingField / ColorField hardcode onChange and value',
    },
  ],
  [
    'filters-tokens',
    {
      construct: 'optional:TokensField.on_change',
      arm: 'host',
      reason: 'TokensField hardcodes onChange and value',
    },
  ],
  [
    'form-combobox-freetext',
    {
      construct: 'optional:ComboboxField.on_change',
      arm: 'host',
      reason: 'ComboboxField hardcodes onChange',
    },
  ],
  [
    'form-rating-halves',
    {
      construct: 'optional:RatingField.on_change',
      arm: 'host',
      reason: 'RatingField hardcodes onChange',
    },
  ],
  [
    'form-tokens-freetext',
    {
      construct: 'optional:TokensField.on_change',
      arm: 'host',
      reason: 'TokensField hardcodes onChange and value',
    },
  ],
  [
    'popover-anchored-1',
    { construct: 'optional:Modal.on_dismiss', arm: 'host', reason: 'Modal hardcodes onDismiss' },
  ],
  [
    'popover-open-1',
    { construct: 'optional:Modal.on_dismiss', arm: 'host', reason: 'Modal hardcodes onDismiss' },
  ],

  // 2 (continued) — `Binding` is Static | State | Filter | Selection | Now |
  // FormatBinding | Local, so a `Query`-sourced control or grid has no spelling.
  // The three `arm: 'both'` grid ids additionally name the DataGrid slot 0.1.0
  // models and this projector does not emit — the half that is fixable HERE, and
  // that would go unrecorded if the Query source were the only cause named.
  ['form-combobox-query', { construct: 'Binding.Query', arm: 'host', reason: 'no Binding.Query' }],
  ['form-tokens-query', { construct: 'Binding.Query', arm: 'host', reason: 'no Binding.Query' }],
  [
    'grid-exportable-1',
    {
      construct: 'Binding.Query',
      arm: 'both',
      projectorConstruct: 'DataGrid.exportable',
      reason: 'no Binding.Query; and the projector emits no exportable',
    },
  ],
  [
    'grid-keep-rows-together-1',
    {
      construct: 'Binding.Query',
      arm: 'both',
      projectorConstruct: 'DataGrid.keep_rows_together',
      reason: 'no Binding.Query; and the projector emits no keep_rows_together',
    },
  ],
  [
    'grid-repeat-header-1',
    {
      construct: 'Binding.Query',
      arm: 'both',
      projectorConstruct: 'DataGrid.repeat_header',
      reason: 'no Binding.Query; and the projector emits no repeat_header',
    },
  ],

  // 1 (continued).
  ['now-grain', { construct: 't.Fact', arm: 'host', reason: 'no t.Fact' }],

  // Family 5 — the encoder's key order — is EMPTY as of the 0.1.0 pin raise:
  // `custom-nonascii-keys` was its only member and 0.1.0 sorts by UTF-16 code
  // unit. The family stays named in the list above because the SHAPE recurs (an
  // encoder that disagrees with the wire is not a modelling gap and has a
  // different remedy), not because anything is currently in it.

  // ── What survives the 2026-09-07 second pass, re-measured against 0.1.0.
  // Every id below fails on a construct 0.1.0 does not model ANYWHERE; the ones
  // whose reason used to name a construct the release has since grown are
  // rewritten to name what they now actually fail on, which is in each case a
  // SECOND cause that was always there behind the first.
  //
  // 1 (continued) — no typed binding case.
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
        'SwitchCase.when is modelled from 0.1.0; the predicate is a Binding.Expr, which is not',
    },
  ],
  [
    'node-visible',
    {
      construct: 'Binding.Expr',
      arm: 'host',
      reason:
        'UiNode.visible is modelled from 0.1.0; two predicates are Binding.Expr / Binding.Query, which are not',
    },
  ],

  // 2 (continued) — no typed action case.
  [
    'action-confirm',
    {
      construct: 'Action.Call',
      arm: 'host',
      reason:
        'Action.Confirm is modelled from 0.1.0; the confirmed branch is an Action.Call, which is not',
    },
  ],

  // 3 (continued) — the unomittable slot, on the buffer rather than a control.
  // `Local` writes `onCommit` unconditionally and carries neither `codec` nor
  // `commitTo`, and the wire refuses a document carrying both commit spellings,
  // so the declarative buffer has no reachable shape at all.
  [
    'form-local-declared',
    {
      construct: 'optional:Local.on_commit',
      arm: 'host',
      reason: 'Local hardcodes onCommit — no codec / commitTo',
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
const QUARANTINE_CENSUS = { host: 69, projector: 0, both: 3 } as const;

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
    // `python -m venv .venv && .venv/…/pip install fuaran-py==0.1.0`, or point
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
