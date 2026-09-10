# Source-projection fidelity decision

_Phase 84 — the `JSON | TS | F# | Python` source-projection inspector_
_(Python added by `fuaran#281`; dropped by the Phase 326 TS-shell deletion;_
_restored natively in F#/Fable by Phase 329; C# added by Phase 362, VB by Phase 363;_
_TypeScript leg re-verified by the Phase 329 follow-on; Python verified by `fuaran#1142`;_
_**F# verified by `fuaran#1657`**)._

The Output box renders the live Fuaran UI tree in several notations. The `JSON`
tab is the exact canonical wire form; the `TypeScript`, `Python`, `F#`, `C#`, and
`VB` tabs are **generated source projections** — the canonical wire tree walked
and emitted as `@fuaran-ui/ui`, `fuaran_py.ui`, `Fuaran.UI` smart-constructor,
`Fuaran.UI.CSharp` static-factory, and `Fuaran.UI.VisualBasic` XML-literal source.
Fidelity is **per-leg**: the `TypeScript`, `Python` and `F#` columns are
**verified byte-round-trips** (below); the `C#` / `VB` columns remain demo-grade /
illustrative, with the verified round-trip for those two the deferred follow-on.

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
(`python -m venv .venv` then `pip install fuaran-py==0.4.0`, or point
`FUARAN_PY_PYTHON` at an interpreter that already has it). It **fails** rather
than skips when the host is absent: an arm that goes green without its oracle is
worse than no arm.

The claim is deliberately scoped, and the scope is the interesting part. Where
the TypeScript leg can always fall back to a typed in-memory object literal,
Python has **no such escape hatch**: `encode` calls `.to_wire()` on the root, and
the structural `fuaran_py.model.Obj` has no such method, so a construct the typed
authoring model does not carry has no spelling at all. Measured against
`fuaran-py` 0.4.0 — the release the three workflows pin, and the one the standing
entries were last re-run against — the remaining set falls in two families:

- **No typed binding case** — `Binding.Expr`. The union runs Static, State,
  Filter, Selection, Now, FormatBinding, Local, Query, Invoke — and no further, so
  a predicate binding has no spelling in any slot that takes one.
- **A record narrower than the wire** — `TransformBinding.source` is a bare
  `DataSource` rather than the wire's `TransformSource`, so a `State`- or
  `Live`-bound source has no spelling.

Two families that held most of this set are now EMPTY, and each is kept named
because its shape recurs and its remedy is not the others'. The **hardcoded
closure sentinel**, which held every canonical minimal control, emptied at 0.2.0:
that release made every handler an omittable flag and every control's `value`
optional, and the projector reads both off the wire. **No typed node kind**
emptied at 0.3.0, which grew `Drawing`, `Fact` and `Mount` — along with the
`Query` and `Invoke` bindings, `TextSource.I18n` and the `Call` / `AiTool` /
`Invoke` actions, which between them emptied most of the second family too.

The set's size is deliberately not quoted here. It is computed and printed by the
census test both arms register from the shared table, which is the only place it
cannot go stale — a count in prose is a claim nobody re-runs, and three of this
file's earlier counts had outlived their cause by the time anyone checked.

None of that is projector lag and none of it is fixable in this repo. Since Phase
1584 EVERY arm reads ONE quarantine table
([`tests/projection-conformance/quarantine.ts`](../tests/projection-conformance/quarantine.ts))
— three of them since `fuaran#1657` —
keyed by fixture id, each entry naming the `arm` it belongs to (`typescript` |
`python` | `fsharp`), the `construct` it needs **as a machine-readable token**, and
a `class` saying which repository owns the cause (`host` | `projector` | `both`). So a
fixture quarantined on one arm and not the other is a row with one empty cell
rather than two files to compare. Two probes keep every entry honest, and both
run per arm from that table: the arm fails if a quarantined fixture starts
round-tripping while still listed, and it fails again if the pinned host turns out
to MODEL the construct an entry blames it for — in which case the failure says
whether that is a stale entry to remove or projector lag to teach
`app/Projection.fs`. The set's size is not quoted here or anywhere else in prose;
it is computed by arm, and the whole cross-arm summary is rendered into the census
test's name, so either arm's output states it. Closing the set is a matter of
teaching `fuaran-py`, one named construct at a time. For those fixtures the
projection
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

## F# leg — VERIFIED byte-round-trip (`fuaran#1657`)

The `F#` column is emitted **per-kind against the real published `Fuaran.UI`
authoring surface** — the `Fuaran.<kind>` smart constructors, the `Node.with*` /
`Node.on*` trait modifiers, and the typed model (`Binding`, `Action`,
`TextSource`, the format and spec unions, the `*Spec` records) for everything
those reach into — and a third arm beside the TypeScript and Python ones keeps it
honest: for every Node fixture in the shared corpus it projects the wire JSON to
F# source, writes **every** projection into ONE generated file, **compiles it
once** against the pinned `Fuaran.UI` package, **executes** it, re-encodes via
`CanonicalJson.encodeNode`, and asserts byte-identity with the fixture. Run it
with `pnpm conformance`; the arm needs a .NET SDK, which the Fable compile that
produced the projector already needed. It **fails** rather than skips when the
toolchain is absent, on the Python arm's reasoning.

**Its quarantine is EMPTY, and the emptiness is the assertion** — the TypeScript
arm's posture, reached for a reason peculiar to this leg and worth stating plainly
because it is what makes the claim strong: **this leg's authoring surface IS the
wire model.** The published `Fuaran.UI.Generated` declares `Node<'Msg>`,
`NodeKind<'Msg>`, every `*Spec` record and every binding / action / format union
AND the canonical encoders over them, and `CanonicalJson.encodeNode` is
`Generated.encodeNode ∘ Introspect.canonicalForm`. So a construct the corpus
carries is, necessarily, a construct the package declares — there is no host-lag
family here at all, and every shortfall the build-up found was the projector's own
and was taught rather than listed. That is the difference from the Python leg,
whose typed model is a separate implementation that can simply not carry a case.

Two consequences follow, and the second is what makes this arm stronger than its
siblings:

- The emitter is **type-directed over a schema transcribed from that published
  model** rather than 2,000 lines of bespoke per-kind string building. Each kind
  is still emitted exactly — its own field list, its own presence rule (a member
  the wire always carries, an `option`, or the one value the encoder omits it at),
  its own closure placeholders, its own smart constructor — but what the wire
  contract shares between kinds, the emitter shares too. The tables can go stale
  when the pin moves; the arm is what makes that a red gate rather than a quietly
  wrong Output box, and `app/Projection.fs`'s own F#-leg header says so at the
  code.
- **The host checks the emission's TYPES as well as its bytes.** A projected
  record that omits a field, or a union case named with the wrong arity, fails at
  the compile before it can produce any bytes at all — a class of drift the
  in-process arms structurally cannot see. Six real defects were found that way
  during the build-up, and the one worth remembering is the sixth: six of the
  model's enums do not encode as their bare case name (`LiveRegionKind`,
  `TextDirection`, `SortDirection`, `TextFormat`, `CompareOp`,
  `LinkProtection` all emit lower-case tokens), and deriving the token instead of
  carrying it compiled perfectly while emitting `"polite"` where the fixture said
  `"assertive"`. Wrong in the direction that still builds is exactly what a byte
  comparison is for.

A kind outside the corpus-covered set is emitted as the structural sketch
`NodeKind.<WireTag>(…)` and so **names the absent case at the compiler** rather
than silently producing something that looks authored and is not — the F#
counterpart of the Python leg's `AttributeError`. Closure-valued fields are erased
to `"<closure>"` by the canonical encoder and project as placeholder lambdas that
are never invoked; and the smart constructors' per-kind ARIA defaults are cleared
or replaced with the wire's own `Accessibility` through `Node.withAccessibility`,
which is correct whatever the default is — so the ctor table records only THAT a
kind injects one, never which.

## C# / VB legs — demo-grade / illustrative

Per operator direction these columns are **illustrative** — "how it would look
written in C# / VB" — **not** a verified byte-round-trip:

- The tabs are an illustrative structural sketch: the projector walks the
  canonical wire tree (over the vendored `FuaranLive.AiWire` `JsonValue` model) and
  emits idiomatic builder source, covering the kinds the playground produces
  with a **generic fallback** for any uncovered kind (so it **never crashes**
  on a decodable tree). Closure-valued fields (handlers, query/selection
  accessors) are sketched, not reproduced. `C#` rides the generic per-language
  `LangSpec` walker (as `F#` did until `fuaran#1657`); **VB** has its own walker
  because its XML-literal shape (`<Kind attr="…">child</Kind>`) does not fit the
  builder-token model (Phase 363).

Headless coverage lives in [`test/projection.test.ts`](../test/projection.test.ts).

## Deferred follow-on — verify the remaining legs

`C#` and `VB` are what is left, and they wait on the same thing: an authoring
surface a harness can EXECUTE. `Fuaran.UI.CSharp` and `Fuaran.UI.VisualBasic` are
compiled veneers over the same `Fuaran.UI` model the F# arm now compiles against,
so the F# arm's shape is the one to copy when they are reached — a generated file,
one compile, one process — with two known differences to design for: the C#
veneer folds `Id` into its options object rather than taking it positionally, and
the VB dialect maps `$name` to a QUERY binding, so a state-bound control has no
VB spelling at all (the estate's own note on the veneer gaps). Neither is
projector lag.

The `Python` leg is done, with the scope its section above states: the remaining
work there is not in this repo but in `fuaran-py`, one named construct at a time,
and the shared quarantine table's `python`-arm entries are the list. The `F#`
leg is done outright — its host is the `Fuaran.UI` package, and there is no
second repository for its remaining work to be in.
