# Dual render-host mode (Phase 85)

fuaran-live's headline demonstration of the **implementation-independent contract**: the
same emitted wire-format JSON rendered, side by side, through **two independent conformant
hosts** — the TypeScript `@fuaran-ui/renderer` and the F# `Fuaran.UI.Renderer`
(Fable-compiled to JavaScript). One prompt, one wire format, identical DOM.

This is the _behavioural_ parity claim, distinct from [Phase 84](../../roadmap/phases/84-fuaran-live-source-projection-inspector.md)'s _notational_
parity (the F#/TS source-projection inspector). Phase 84 shows the two languages produce
the same tree on paper; Phase 85 shows the two renderers produce the same DOM in the
browser.

## How it works

```
                    ┌──────────────────────── preview pane (PreviewPane.tsx) ────────────┐
   wire JSON  ──►   │  host-switch.ts bridge   ──postMessage──►  ┌── ts-host.html  (iframe)│
 (encodeNode of     │  (toggle picks the                        │     @fuaran-ui/renderer  │
  the folded tree)  │   visible iframe)        ──postMessage──►  └── fable-host.html(iframe)│
                    │                                                 Fuaran.UI.Renderer    │
                    └────────────────────────────────────────────────(Fable-compiled)──────┘
```

- **Isolation is pragmatic.** Each host runs in its own iframe so the two bundler graphs
  stay disjoint — the Fable output never has to merge into the main Vite app graph. The
  shell posts the same wire JSON to both over `postMessage`; the toggle is an instant
  visibility flip, never a re-decode.
- **The protocol** is `src/hosts/protocol.ts` (parent ⇆ host messages). The Fable host
  (`fable-host/Host.fs`) hard-codes the identical string contract — F# cannot import the TS
  module, so the two are kept in sync by hand.
- **No effect ports are wired** in the host iframes: a decoded tree's actions are inert
  (the wire form carries no live closures), so both hosts render with a no-op dispatch.
  Parity is asserted over structure + class vocabulary + ARIA, which is exactly what is
  contract-locked.
- **Theme application.** The TS `<FuaranRenderer theme=…>` wraps the tree in
  `<div class="fuaran-root" style="--fuaran-…">`. The Fable host (`Host.fs`) mirrors that
  wrapper — a `fuaran-root` div carrying the theme `<style>` + the rendered tree — so the
  two hosts produce structurally identical DOM at the root, not just inside it.

## In-app parity pane

Beyond the offline Playwright gate, the dual-host claim is surfaced **live in the app**
as the **Dual-host wire parity** pane (`app/App.fs`, gated behind `VITE_DUAL_HOST`). It
embeds both render-host iframes side by side, posts the current tree's canonical wire
JSON to each, and shows a verdict banner. Each host now reports its **canonical
re-encoding** (`encode(decode(wire))`) alongside the render ack (the `canonical` field
added to the `fuaran:rendered` message in `src/hosts/protocol.ts`, mirrored by the F#
host in `fable-host/Host.fs`); the pane asserts the two hosts' re-encodings are
**byte-identical** to each other and to the expected canonical bytes — the wire-parity
contract made visible, not just the rendered DOM. A green banner means two independent
conformant hosts decoded and re-encoded the same wire to the same bytes.

### Corpus demo inputs (+ the §16 normalisation demo)

The pane's **Input** picker drives both hosts with real
[`wire-format-fixtures/`](../../wire-format-fixtures/) conformance files — inlined at
build time via Vite `?raw` imports in `src/hosts/parityFixtures.ts` (the same
sibling-checkout posture as the `@fuaran-ui/*` link bridge) — alongside the session's
own tree:

- **Canonical `nodes/` fixtures** (composite page, card + metric, ranged form, table):
  the file is its own canonical bytes, so a green verdict is the plain round-trip claim —
  `encode(decode(wire))` byte-equal to the input on both hosts.
- **§16 `lenient-accept` shorthands** (bare-string `Button.label` / `Callout.body`): the
  pane posts the **shorthand** input and shows the before/after — the bare-string wire
  actually sent, and the verbose canonical form both hosts re-encode it to. The verdict
  distinguishes this green from the round-trip green ("§16 shorthand normalised to
  identical canonical bytes"): per [`WIRE_FORMAT.md`](../../fuaran-dotnet/docs/WIRE_FORMAT.md)
  §16 a conformant host MUST accept the shorthand and normalise it, so
  `canonical ≠ input` here is the claim working, not a divergence.

Selecting a fixture overrides the session tree as the parity drive; loading a gallery
example or generating a UI re-points the pane at the session tree.

## Feature flag

The whole dual-host path is gated behind the `VITE_DUAL_HOST` build/runtime flag so the
[Phase 83](../../roadmap/phases/83-fuaran-live-client-only-byok-authoring-demo.md) MVP ships
without the Fable toolchain in the critical path.

| `VITE_DUAL_HOST`    | Preview pane                | Build inputs                                      | Fable toolchain               |
| ------------------- | --------------------------- | ------------------------------------------------- | ----------------------------- |
| unset (MVP default) | single in-process TS render | `index.html` only                                 | **not needed**                |
| `1` / `true`        | dual-host iframes + toggle  | `index.html` + `ts-host.html` + `fable-host.html` | **required** (`dotnet fable`) |

## Two-toolchain build

The dual build runs `dotnet fable` (F# → JS) and `vite build` (TS) into one static `dist/`.

```powershell
# one-shot production build (Fable + Vite), emits a static dist/
pnpm run build:dual          # = pnpm run fable && tsc --noEmit && cross-env VITE_DUAL_HOST=1 vite build

# just the Fable compile (writes fable-host/output/, gitignored)
pnpm run fable               # = dotnet fable fable-host --outDir fable-host/output
pnpm run fable:watch         # incremental, for dev

# dev (Fable watcher + Vite dev server + browser) — the launcher wires both
.\dev-scripts\launch-dual-host.ps1
```

`dotnet tool restore` (Fable 5 + Fantomas) is required once per checkout; the launcher runs
it. The Fable host is consumed by **direct ProjectReference into the sibling `../fuaran-dotnet`
checkout** (the same workspace-checkout posture the conformance harness + the `@fuaran-ui/*`
link bridge use during the bootstrap window) — see `fable-host/FableHost.fsproj`.

The static `dist/` still deploys to any plain static host: the iframes are same-origin
relative pages (`base: './'`), and the CSP carries `frame-src 'self'` for them.

## Parity regression gate

`tests/parity/parity.spec.ts` (Playwright) is the regression guard over the
[Phase 77](../../roadmap/phases/77-fuaran-renderer.md) reference-CSS byte-copy + parity-locked
class vocabulary, exercised on real `wire-format-fixtures/` corpus trees. For each fixture in
the primitive matrix it posts the wire JSON to both host pages and asserts their rendered
DOM is identical — tag nesting + sorted CSS classes + node ids + ARIA/role + text. It also
runs the §16 `lenient-accept` pairs through both hosts: each must accept the shorthand,
re-encode it to the expected file's exact canonical bytes, and render the same DOM. Any
divergence fails CI.

```powershell
pnpm run parity              # build:dual, then run the Playwright gate
```

The CI workflow (`.github/workflows/parity.yml`) assembles the workspace layout (workspace +
fuaran + fuaran-ts + fuaran-live), builds the dual artefact, and runs the gate.

## Known parity gaps

Per the wire-format forward-coupling rule ([`WIRE_FORMAT.md`](../../../fuaran-dotnet/docs/WIRE_FORMAT.md) §11),
a cross-tier divergence is fixed in the F# **and** TS tiers together, in one change-set — never
patched on one side from the consumer. fuaran-live is where the gate **detects** divergence; the
fix lands in `fuaran` + `fuaran-ts`.

_None currently open._

- **`form-1` Choice with an `<opaque>` options-source — resolved (workspace Phase 131).** The
  `form-1` fixture encodes a `Choice` whose options binding round-tripped to the `<opaque>`
  sentinel (a non-array `Static` options list the encoder could not serialise). The two renderers
  previously diverged — the F# renderer emitted one `<option><opaque></option>`, the TS renderer
  none. Phase 131 settled the cross-host contract: an opaque/non-array options source renders **no
  concrete options** on every conformant host (recorded in [`WIRE_FORMAT.md`](../../../fuaran-dotnet/docs/WIRE_FORMAT.md) §5).
  The F# renderer now strips the decoder's opaque placeholder; the TS renderer already did so via
  its `asArray` coercion. `form-1` is now in the green parity matrix.
