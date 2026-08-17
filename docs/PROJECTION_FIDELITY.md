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
Fidelity is **per-leg**: the `TypeScript` column is a **verified byte-round-trip**
(below); the `Python` / `F#` / `C#` / `VB` columns remain demo-grade /
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

One bound on that claim: the corpus is versioned with the language repos and can
run **ahead of the published `@fuaran-ui/*` packages**, and a fixture carrying
wire vocabulary the installed decoder/encoder does not know yet is unprojectable
by construction — the projection's fidelity ceiling is the installed packages'
vocabulary. The harness detects those fixtures mechanically (decode→encode
through the installed package is not the identity), skips them with the missing
wire feature named, and pins the skip set by name so it cannot drift silently:
a package update that closes a gap fails the pin test until the entry is removed
and the fixture rejoins the byte-round-trip gate.

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

## Python / F# / C# / VB legs — demo-grade / illustrative

Per operator direction these columns are **illustrative** — "how it would look
written in Python / F# / C# / VB" — **not** a verified byte-round-trip:

- The tabs are an illustrative structural sketch: the projector walks the
  canonical wire tree (over the vendored `FuaranLive.AiWire` `JsonValue` model) and
  emits idiomatic builder source, covering the kinds the playground produces
  with a **generic fallback** for any uncovered kind (so it **never crashes**
  on a decodable tree). Closure-valued fields (handlers, query/selection
  accessors) are sketched, not reproduced. Python / F# / C# ride one generic
  per-language `LangSpec` walker; **VB** has its own walker because its
  XML-literal shape (`<Kind attr="…">child</Kind>`) does not fit the
  builder-token model (Phase 363).

Headless coverage lives in [`test/projection.test.ts`](../test/projection.test.ts).

## Deferred follow-on — verify the remaining legs

Restoring the **verified byte-round-trip** for the `Python` and `F#` columns —
re-authoring their conformance arms (execute the generated source against
`fuaran_py.ui` via CPython / against `Fuaran.UI` via the dotnet toolchain,
`encode_node` / canonical-encode, assert byte-identity), and upgrading those
legs of `app/Projection.fs` from the generic walk to per-kind exact emission —
remains recorded as the roadmap TIDY-UP residue. `C#` / `VB` follow once their
authoring surfaces (`Fuaran.UI.CSharp` / `Fuaran.UI.VisualBasic`) are
executable in a harness.
