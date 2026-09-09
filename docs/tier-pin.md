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

## What the pin is

**Only the SHOWCASE project checks this tier out.** The playground project does
not project-reference the language tier at all — it consumes it as published
nuget.org packages at its own version pins in `app/FuaranLive.fsproj`. So this
`ref:` decides which tier contracts `app/showcase/` compiles against, and it is
a pin in every sense the package pins are, spelled as a git ref.

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

**To bump: raise the `ref:` in all six workflows together** (grep the ref), with
the showcase migration in the same commit.

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
