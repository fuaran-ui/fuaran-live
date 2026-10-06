# The language-tier pin — what `ref:` means in the workflows, and how to bump it

Six workflows check the F# language tier out as a sibling
(`repository: fuaran-ui/fuaran-dotnet`, `path: fuaran-dotnet`) at an exact
`ref:`. This document is that pin's rationale and its bump procedure. It lives
here rather than in the workflows because it was duplicated verbatim across all
six, and a fifty-line comment copied six times drifts: on 2026-09-09 every copy
still opened by announcing a release tag of `v0.12.0` while the `ref:` beside it
read `v0.79.0`, five bumps later. One copy can be wrong; six copies are wrong in
different places.

Each workflow now carries a one-line pointer here and the `ref:` itself.

## One binding: `Directory.Build.props` (Phase 2079)

Every F# project here reads its package versions from the repository's
`Directory.Build.props`, which declares ONE version property per producing
repository: `FuaranUIVersion` (the whole `Fuaran.UI.*` family, including
`Fuaran.UI.AiWire` and the program loop's UI adapter `Fuaran.UI.Program`),
`FuaranCoreVersion` (`Fuaran.Core.*`), `FuaranProgramVersion`
(`Fuaran.Program.Runtime`), and one each for the Fable, Elmish, Elmish.React
and Feliz toolchain packages. No project file spells a number.
`test/restoreGraphPublic.test.ts` locks three things about it: every pin reads a
property that file declares, each producer family reads its own one property,
and the `ref:` every workflow checks the tier out at equals
`v$(FuaranUIVersion)`. That last lock is what makes this ONE binding: the
showcase's source-bound tier and the packaged tier the other two projects
compile are the same release, so `app/Projection.fs`, `app/shared/Brand.fs` and
`app/navigator/Cursor.fs` — compiled into both the playground and the showcase —
compile against one tier version.

### Why each project binds as it does

- **The playground (`app/FuaranLive.fsproj`) takes published packages**, since
  2026-08-26 (it began, like the others, as project references plus linked
  `Fuaran.UI.Ops` sources). It consumes the bounded program loop, which is
  distributed as a package declaring its own `Fuaran.UI.*` dependencies; a
  ProjectReference to `Fuaran.UI` substitutes that project for the package's
  dependency while Fable still extracts every other `Fuaran.UI.*` package's
  sources into `fable_modules/`, where `Fuaran.UI` is then out of scope and the
  compile dies in thousands of errors. Linked copies of Ops/OpStream sources
  compounded it with a second definition of the same modules. So the whole
  family became packages, at one version.
- **The parity render host (`fable-host/FableHost.fsproj`) takes published
  packages since Phase 2079.** From the first public release it took
  ProjectReferences plus four linked `Fuaran.UI.Ops` / `OpStream` source files
  (`Repair.fs`, `JsonDecode.fs`, `Introspect.fs`, `CanonicalJson.fs`), to keep
  its Fable graph minimal and in step with the source defining the contract.
  Every linked file's module ships Fable-source-packed in the published
  `Fuaran.UI.Ops` and `Fuaran.UI.OpStream.Abstractions` packages, and the
  linked set had to be extended by hand each time the decoder grew a
  dependency (twice: `Introspect.fs`, then `Repair.fs` at v0.90.0). In CI the
  checkout was pinned to the same release tag the packages carry, so the parity
  view compared the same contract either way; as packages, both sides of it —
  this host and the TypeScript host's `@fuaran-ui/*` packages — are what a
  stranger restores.
- **The showcase (`app/showcase/Showcase.fsproj`) still binds the tier by
  SOURCE**, through ProjectReferences into the sibling checkout this document's
  `ref:` names. Its Git for Interfaces page runs `Fuaran.UI.OpStream.Dag.Merge`
  (the structural 3-way merge engine) in the browser, and that package is
  published for .NET hosts only: it ships no `fable/` sources, by the language
  tier's own recorded decision (its Phase 2128), so Fable cannot compile it from
  the registry. The binding cannot be mixed either — one ProjectReference among
  packages is the substitution failure described for the playground above. The
  showcase therefore WAITS on Dag.Merge's Fable packaging, which the language
  tier has decided to ship (operator ruling, 2026-10-06): it moves to packages
  at the first release that carries the merge engine's `fable/` sources, and
  until then the `ref:` lock keeps it on the same release as everything else.

## What the pin is

**Only the SHOWCASE project checks this tier out.** The playground and the
parity host do not project-reference the language tier at all — they consume it
as published nuget.org packages at `FuaranUIVersion`. So this `ref:` decides
which tier contracts `app/showcase/` compiles against, and it is a pin in every
sense the package pins are, spelled as a git ref.

**It is a release TAG, and a tag is immutable**, so it is no less deliberate
than the bare commit SHA it replaced. It was a SHA from Phase 761 until v0.75.0,
for a reason that has since expired: the showcase had adopted contracts
postdating every released tag, so no tag could compile it.

**The corpus checkout beside it stays UNPINNED, deliberately** — that is the
shared conformance gate, and pinning it would let this repo certify against a
specification the format has moved past.

## Bumping it is a SOURCE-AFFECTING act

Not housekeeping. A tier release that widens a record breaks every full-literal
construction of it in `app/showcase/`, so the `ref:` and the source migration
land in ONE commit and cannot be split: the field assignment alone fails against
the old checkout, and the `ref:` alone fails once the field is required.

**To bump: raise `FuaranUIVersion` in `Directory.Build.props` and the `ref:` in
all six workflows together** (grep the ref), with the showcase migration and the
package-raise checklist below in the same commit. The restore-graph suite
refuses the commit that moves one without the other.

## Why the two siblings are pinned differently

The tier is PINNED and the corpus is NOT, and the asymmetry is deliberate.

The tier is consumed by project reference, so its source is part of the
showcase's compile. Floating it on a default branch meant any upstream push
could redden this repo with no commit here — which is exactly what happened on
2026-07-27, when a required `RenderContext` field landed upstream and all six
workflows failed on a CI-only commit. An exact ref turns that into a deliberate
bump.

The corpus is the shared conformance gate every host certifies against, and the
whole point of giving it one home is that all hosts see the same current
specification. Pinning it would reintroduce precisely the drift that split
bought out.

## Why a tag was not automatically the more rigorous choice

Worth keeping, because the intuition runs the other way. Phases 692–694
flattened `NodeKind` and reshaped the authoring surface (`TextSource`,
`NodeId`, `SemanticStyle`, `SelectOption`). This repo adopted that contract
while no release tag carried it — v0.6.0 was the newest and PREDATED the change
— so a tag pin named a contract the source no longer targeted, and every
workflow compiled the migrated app against the old types. The pin moved to a
commit SHA, which is at least as deliberate as a tag (a tag can be moved; a SHA
cannot), on the stated intent of returning to a tag once the tier cut a release
carrying the flattening. v0.12.0 was that release; the return was made there,
and its tree was one test-only commit ahead of the SHA the pin had named, so
the contract compiled against did not move at the switch.

## History — what each raise cost

- **v0.12.0** — the return to a tag, after the SHA era described above.
- **v0.75.0** — the release that let the pin return to a tag a second time,
  after the showcase had again adopted contracts postdating every released tag.
- **v0.78.0** (2026-09-08, with the package pins in `app/FuaranLive.fsproj` and
  `app/showcase/Showcase.fsproj`): widens `Action.Navigate` to a
  `(TextSource, NavigateTarget)` pair, adds a required `Node.visible`, adds
  `SwitchCase.when` while making its `match` optional, and adds
  `RenderContext.CustomHashFloor` — every one of which the showcase source
  names. It also carries the Fable dynamic-Huffman inflate fix the teleport
  receiver needs to accept a bundle any standard deflater produced, and the
  print-break, node-tooltip and upload-sink members.
- **v0.78.1** (same day) — not a widening. The renderer's resume path named a
  wire type its Fable-only arm never opened, so v0.78.0 built for .NET and
  failed to Fable-compile for every consumer, this repo included through both
  the packaged playground copy and the project-referenced showcase copy. The
  showcase checks the tier out, so the ref is what carries the fix here.
- **v0.79.0** (2026-09-09, Phase 1627) — the tier's first deliberately BREAKING
  release in some time: `RenderContext` gains a required `Csp` field, so every
  full-literal construction of it in `app/showcase/` stops compiling. That is
  the source-affecting cost this document warns about, arriving in full, which
  is why the four `Csp = Csp.Permissive` assignments landed in the SAME commit
  as the ref. `Permissive` is the tier's own canonical default and emits
  byte-identical output, so the showcase's rendered bytes did not move.
- **v0.80.0** (2026-09-10, Phase 1669) — **the cheapest raise this document
  records, and worth writing down for that reason.** Against v0.79.0 the tier's
  whole `src/` delta is one file: `ProviderCallTelemetry` gains an optional
  `Subject` member. Nothing under `app/showcase/` constructs that record — the
  grep is empty across `app/` and `test/` — so the record widening cannot reach
  this repo at all, and the raise cost ZERO source migration. That is the
  exception rather than the rule: a raise is a source-affecting act by default
  (v0.79.0 is the worked example), and the way to know which kind you have is to
  diff the tier's `src/` between the two tags and check whether the showcase
  names what moved. Do that BEFORE budgeting the raise, not after.

  It rides with the package-pin raise the showcase's conformance needed —
  `@fuaran-ui/ui` 0.20.0, `renderer` 0.22.0, `ops` 0.25.0, `schema` 0.21.0, and
  `fuaran-py` 0.5.0 — which is what carries the declared FileUpload upload
  ceilings into both conformance arms. The playground's packaged `Fuaran.UI.*`
  pin in `app/FuaranLive.fsproj` is a separate slot on its own rhythm and did
  not move here.

- **v0.90.0** (2026-10-03) — a ten-release jump, and source-affecting in three
  ways the showcase names. `Node` gains a required `Fallback` (the two
  hand-built `Node<unit>` literals in `Relay.fs` and `Rosetta.fs` set
  `Fallback = None`); `ModuleAffordance` gains `Scope` (`AgentReadable.fs` sets
  `None`, which is the page itself); and the dataframe surface — `DataFrame`,
  `ColExpr`, `Agg`, `Transform`, `DataFrameCodec` — moved out of `Fuaran.Core`
  into the `Fuaran.Compute.DataFrame` package, namespace `Fuaran.Compute`,
  reached through the tier's own reference (`open Fuaran.Compute` in
  `LivingSheet.fs`, `Charts.fs`, `PatternBank.fs`; the GroupBy aggregate list
  is annotated `Agg list` so its `{ Name; Fn; Of }` literals do not resolve to
  `FragmentSignature`). `Cell` gained a `Decimal` case, so `cellText` gains the
  arm. The tier pins `Fuaran.Core.*` at 0.34.0, so the showcase's two direct
  Core pins (`Function`, `Wire`) moved 0.21.0 → 0.34.0 in the same commit — a
  number left behind is an NU1605 downgrade, which kills the Fable cracker.
- **v0.91.0** (2026-10-04, Phase 1882) — a raise of the second, cheap kind,
  found by the diff the v0.80.0 entry asks for. Against v0.90.0 the tier's
  library delta the showcase can reach is `Fuaran.UI.Ops/JsonDecode.fs`,
  `Fuaran.UI/PreEmitValidate.fs` (the expression bound now counts a rounding
  scale, so a document near `MaxExprNodes` that v0.90.0 accepted may be refused
  — the release is classed breaking for that BEHAVIOUR, and no showcase page
  authors a document near the bound) and the re-synced embedded renderer
  bundle; the rest is the new `Fuaran.UI.Program*` adapter packages, which no
  showcase project references. No showcase record widened and the tier still
  declares `Fuaran.Core.*` 0.34.0, so the two direct Core pins did not move.
  The one source edit rides for completeness rather than necessity: the
  `SlotTree` arm in `TypedQuestion.fs`'s `spaceText`, whose absence has been an
  incomplete-match warning since Core 0.31.0 grew the tree space. It rides with
  the `@fuaran-ui/*` 0.29.0 set (`ops` 0.29.0, `schema` 0.25.0, `ui` 0.23.0,
  `renderer` 0.26.0) and `fuaran-ui` 0.8.0, the releases carrying Phases 1810,
  1811, 1812 and 1892 — which is what cleared the projection-conformance
  quarantine's TypeScript entries and its four Python 1812 / 1892 entries.

## The package pins — the same release, by package

Everything above is about the `ref:` in the six workflows. The playground and
the parity host consume the same tier as published nuget.org packages, at
`FuaranUIVersion`. Until Phase 2079 that was a SEPARATE slot on its own rhythm
(the two were unequal from the ref's v0.80.0 against a 0.79.0 package family,
and raised separately again on 2026-09-22), and `Projection.fs` was compiled
against two tier versions as a result. They are now one number, held equal by
the restore-graph suite, so a raise is one act with both costs: the showcase's
source migration (above) and the package checklist below.

**What a package raise touches**, all in one commit:

1. `FuaranUIVersion` in `Directory.Build.props`, and the six workflow `ref:`s;
2. **`FuaranCoreVersion` beside it** — read the new tier's nuspec from the
   registry and match its declared Core version. These are DIRECT pins on
   packages the tier also depends on, so a number left behind does not hold
   Core back, it downgrades the tier's own dependency and emits NU1605, and one
   NU160x line on msbuild's stdout kills the Fable project cracker outright
   (an unpublished pin is NU1603, a warning followed by a silent substitution of
   the nearest higher version, with the same effect on the cracker);
3. **`FuaranProgramVersion`** when the new `Fuaran.UI.Program` adapter declares a
   higher floor on `Fuaran.Program.Runtime` (below it is NU1605);
4. `app/output/fable_modules/<PackageId>.<Version>/` paths, which carry the
   version — `test/tierOutput.ts` (the one place the suite names them) and
   `scripts/fable-app.mjs`;
5. any declared tier lag the raise closes — see the canonical-form assertion in
   `test/permalinkGallery.test.ts`, whose normalisation is deleted rather than
   kept once the tier emits the canonical spelling;
6. the registry-evidence block in `test/restoreGraphPublic.test.ts`.

### History — the package family

- **0.79.0 → 0.85.0** (2026-09-22) — the release whose `SchemaGen` emits Phase
  1821's canonical dataframe spellings (`project.columns`, `TransformSortKey.column`;
  the old `cols` / `col` remain decode aliases). It took the F# arm of the
  projection-conformance gate from five failing transform fixtures to none, and
  closed the one-member normalisation `test/permalinkGallery.test.ts` had been
  carrying to keep its byte comparison honest about the lag. It also moved
  `Fuaran.Core.*` 0.21.0 → 0.28.0 per point 2 above. The `ref:` did **not**
  move with it: no showcase contract needed it, and a five-release source jump
  carries its own migration budget that this raise had no reason to spend.
- **0.85.0 → 0.86.0** (Phase 1915) — the floor the bounded program loop's UI
  adapter (`Fuaran.Program.UI` 0.6.0) declared on every member of the family,
  so a pin below it would have been NU1605 rather than a choice. The tier
  change a playground tree can observe: a state binding with NO declared
  default, at a slot nothing has written, resolves as UNRESOLVED where it
  resolved to the empty value — a tree that wants the empty value declares it
  as the binding's default. `Fuaran.Core.*` moved 0.28.0 → 0.32.0 with it.
- **0.86.0 → 0.91.0** (Phase 2016) — the release that first ships the loop's UI
  adapter INSIDE this family, as `Fuaran.UI.Program` (it was
  `Fuaran.Program.UI`, released with the loop's core up to 0.7.1), so the
  adapter moves with the family rather than with the loop; the core
  (`Fuaran.Program.Runtime`) stays on its own producer's line at 0.7.1, the
  adapter's floor. From 0.90.0 the dataframe pipeline types (`Transform`,
  `ColExpr`, `Slot` and the enums beside them) live in the `Fuaran.Compute`
  namespace; from 0.91.0 the expression bound counts a rounding scale as a
  node. `Fuaran.Core.*` moved 0.32.0 → 0.34.0.
- **The ceiling that twice held the family back is gone.** Before the loop's
  0.6.0 split, the tier widened a member of `Action` and the loop — a separate
  producer — still constructed the narrower form in its own Fable-packed
  sources, a compile error this repo could not spell around: first over
  `Action.WriteToClipboard` (held at 0.46.0 for a cycle, lifted at the loop's
  0.2.0), then over `Action.Navigate` (widened at 0.78.0, lifted at the loop's
  0.4.0). Since the adapter is a family member, built and released with it,
  that ceiling cannot arise between them.
- **`Fuaran.UI.AiWire` joins the family number** (Phase 2079). It was adopted as
  a package by Phase 1698, replacing four vendored files, and was pinned on its
  own number — 0.81.0 in the showcase and 0.85.0 in the playground — on the
  reading that it moves on its own line. It does not: the language tier releases
  it with every other member, at one version, and its source is byte-identical
  from v0.81.0 to v0.91.0, so unifying it at 0.91.0 changed no compiled code.
  The modules the playground does not use stay absent from the package for the
  reasons Phase 1676 recorded: the transport already classifies a rate limit
  itself, retrying automatically spends the READER'S OWN key, and an unused SSE
  parser is prompt-security surface nothing runs.
- **Why the playground pins `Fuaran.Core.*` at all.** Phase 379 linked
  `Fuaran.UI.Ops/Apply.fs` into the project, and the linked file needed the Core
  skeleton packages its own project carries. The link went when the project
  moved to packages; the direct Core pins stayed because the app's own sources
  open `Fuaran.Core` (the Pattern Bank's signature search and the op-stream
  chain among them) — and being direct, they must match the tier's declared
  Core version (point 2).
