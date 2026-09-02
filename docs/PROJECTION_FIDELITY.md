# Source-projection fidelity decision

_Phase 84 — the `JSON | TS | F# | Python` source-projection inspector_
_(Python added by `fuaran#281`; dropped by the Phase 326 TS-shell deletion;_
_restored natively in F#/Fable by Phase 329; C# added by Phase 362, VB by Phase 363;_
_TypeScript leg re-verified by the Phase 329 follow-on)._

The Output box renders the live Fuaran UI tree in several notations. The `JSON`
tab is the exact canonical wire form; the `TypeScript`, `Python`, `F#`, `C#`, and
`VB` tabs are **generated source projections** — the canonical wire tree walked
and emitted as `@fuaran-ui/ui`, `fuaran_py.ui`, `Fuaran.UI` smart-constructor,
`Fuaran.UI.CSharp` static-factory, and `Fuaran.UI.VisualBasic` XML-literal source.
Fidelity is **per-leg**: the `TypeScript` and `Python` columns are **verified
byte-round-trips** (below); the `F#` / `C#` / `VB` columns remain demo-grade /
illustrative, with the verified round-trip for those legs the deferred follow-on.

## History

Phase 84 shipped the inspector with the `TS` / `F#` projectors; `fuaran#281`
added the `Python` column. Those projectors lived in the TypeScript shell at
`src/inspector/projections/*` and carried a **verified byte-round-trip**
guarantee, enforced by a codegen-conformance harness over the
[`wire-format-fixtures/`](../../wire-format-fixtures/) corpus.

The **Phase 326** rebuild deleted the entire TypeScript shell (the playground is
now entirely F#/Fable), which removed those projectors and left the conformance
harness orphaned (it imported the deleted modules and was not run by the active
`test/**` vitest config). Phase 329 restored the Output box natively in F#/Fable
at [`app/Projection.fs`](../app/Projection.fs) — initially demo-grade across all
columns, per operator direction.

## TypeScript leg — VERIFIED byte-round-trip (Phase 329 follow-on)

The TS column is emitted **per-kind against the real `@fuaran-ui/ui` authoring
surface** (`fuaran.*` ctors, `binding.*` / `action.*` / `format.*` /
`formFieldKind.*` / `filterKind.*` builders), and a re-authored conformance
harness at [`tests/projection-conformance/`](../tests/projection-conformance/)
keeps it honest: for every Node fixture in the shared corpus it projects the
wire JSON to TS source, **executes** the generated source against the real
packages, re-encodes via the canonical encoder, and asserts the result is
**byte-identical** to the fixture. Run it with `pnpm conformance` (after
`pnpm run fable:app`); any drift between the projector and the `@fuaran-ui/ui`
contract fails the gate.

Two consequences shape the emitter (as for the pre-326 projectors):

- Closure-valued fields (handlers, query/selection accessors, parse/format) are
  erased to `"<closure>"` by the canonical encoder, so the projection emits
  structurally-correct placeholders (`() => action.chain([])`, `() => undefined`)
  — only the observable payload is projected faithfully.
- The smart constructors inject per-kind ARIA defaults the canonical minimal
  trees omit; the emitter pins the node's base traits (style / state /
  accessibility) back to the wire's exact values so the re-encode is byte-stable.

A kind outside the corpus-covered set falls back to the illustrative generic
sketch (never a crash); the verified claim covers exactly what the harness
executes.

## Python leg — VERIFIED byte-round-trip over the modelled set (`fuaran#1142`)

The `Python` column is emitted **per-kind against the real `fuaran_py` authoring
surface** — `fuaran.*` smart constructors, the `binding.*` / `action.*` /
`format.*` namespaces, and the typed model `fuaran_py.schema.types` (`t`) with
its compute layer (`cp`) for the records those namespaces do not reach — and a
second arm beside the TypeScript one keeps it honest: for every Node fixture in
the shared corpus it projects the wire JSON to Python source, **executes** the
generated source against the real host (every fixture in ONE CPython process),
re-encodes via `fuaran_py.ui.encode`, and asserts byte-identity with the fixture.
Run it with `pnpm conformance`; the arm needs a CPython carrying `fuaran-py`
(`python -m venv .venv` then `pip install fuaran-py==0.0.1`, or point
`FUARAN_PY_PYTHON` at an interpreter that already has it). It **fails** rather
than skips when the host is absent: an arm that goes green without its oracle is
worse than no arm.

The claim is deliberately scoped, and the scope is the interesting part. Where
the TypeScript leg can always fall back to a typed in-memory object literal,
Python has **no such escape hatch**: `encode` calls `.to_wire()` on the root, and
the structural `fuaran_py.model.Obj` has no such method, so a construct the typed
authoring model does not carry has no spelling at all. Measured against
`fuaran-py` 0.0.1 (the newest published release, and checked against its
development tree too), that is **55 of the 161 node fixtures**, in four families:

- **No typed node kind** — `Mount`, `Fact`, `Drawing`.
- **No typed binding / action case** — the `Query`, `I18n` and `Invoke` bindings;
  the `Call`, `AiTool` and `Invoke` actions.
- **A hardcoded closure sentinel** — several records emit `onChange` /
  `onToggle` / `onSelect` / `value` unconditionally, so the canonical minimal
  control (`{"$type":"Text"}`) and the declarative field-named grid column are
  unreachable: the slot is not optional in the record, and the encoder has
  nothing to omit.
- **A record narrower than the wire** — `Chart` reaches six slots fewer;
  `TransformBinding` carries neither `params` nor a `Live` source; `DataGrid`
  carries none of the declarative sort / page / edit-state slots; `Link` has no
  `protection`, `Table` no `sortable` / `defaultSort`, `Media` no `tracks` /
  `transcript`.

None of that is projector lag and none of it is fixable in this repo. Each of
the 55 is listed **with the construct it needs** in the arm's `PY_UNMODELLED`
map, and the arm fails if one starts round-tripping while still listed — so the
set cannot quietly outlive its cause, and closing it is a matter of teaching
`fuaran-py`, one named construct at a time. For those fixtures the projection
still emits the shape the typed model _would_ take, so pasting it raises an
`AttributeError` naming the absent class rather than silently producing something
that looks authored and is not.

The same two consequences shape this emitter as the TypeScript one: closure-valued
fields are erased to `"<closure>"` by the canonical encoder and so project as
structural placeholders, and the constructors' per-kind ARIA defaults are pinned
back to the wire's exact values (through `UiNode.replace`, Python's spelling of
the TS leg's object spread). Note the ARIA table is the **Python constructors'**,
not the TypeScript one's — `scroll_area` carries `role=region` where its TS
counterpart carries none, and the static-rows `table` constructor carries none
where `grid` carries `region`.

## F# / C# / VB legs — demo-grade / illustrative

Per operator direction these columns are **illustrative** — "how it would look
written in F# / C# / VB" — **not** a verified byte-round-trip:

- The tabs are an illustrative structural sketch: the projector walks the
  canonical wire tree (over the vendored `FuaranLive.AiWire` `JsonValue` model) and
  emits idiomatic builder source, covering the kinds the playground produces
  with a **generic fallback** for any uncovered kind (so it **never crashes**
  on a decodable tree). Closure-valued fields (handlers, query/selection
  accessors) are sketched, not reproduced. F# / C# ride one generic per-language
  `LangSpec` walker; **VB** has its own walker because its XML-literal shape
  (`<Kind attr="…">child</Kind>`) does not fit the builder-token model
  (Phase 363).

Headless coverage lives in [`test/projection.test.ts`](../test/projection.test.ts).

## Deferred follow-on — verify the remaining legs

Restoring the **verified byte-round-trip** for the `F#` column — re-authoring its
conformance arm (execute the generated source against `Fuaran.UI` via the dotnet
toolchain, canonical-encode, assert byte-identity) and upgrading that leg of
`app/Projection.fs` from the generic walk to per-kind exact emission — remains
recorded as the roadmap TIDY-UP residue. `C#` / `VB` follow once their authoring
surfaces (`Fuaran.UI.CSharp` / `Fuaran.UI.VisualBasic`) are executable in a
harness. The `Python` leg is done, with the scope its section above states: the
remaining work there is not in this repo but in `fuaran-py`, one named construct
at a time, and the arm's `PY_UNMODELLED` map is the list.
