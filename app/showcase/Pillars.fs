module Fuaran.Showcase.Pillars

// ============================================================================
//  The four pillars the site groups its pages by.
//
//  The site's accumulating argument is that these are not N features but one
//  design decision – UI as typed data – paying out N times. The navigation
//  groups by pillar so that argument is legible. The pages themselves are listed
//  in Registry.fs, which is compiled after the page modules.
// ============================================================================

open Fable.Core.JsInterop

/// The playground lives on its own origin (D8/D10 – one repo, two origins).
/// Prefer the build-time setting; fall back to the canonical permalink domain
/// when unset (dev: both entries on one Vite server, so the domain is the honest
/// target for a real cross-door hop). Defined here rather than in `Pages` because
/// pages compiled ahead of `Pages` link across too — the Navigator page's handoff.
let playgroundOrigin: string =
  let configured: string =
    emitJsExpr () "((import.meta.env && import.meta.env.VITE_PLAYGROUND_ORIGIN) || '')"

  if configured = "" then
    "https://fuaran-ui.live"
  else
    configured

[<RequireQualifiedAccess>]
type Pillar =
  | Value
  | Wire
  | Machine
  | Intent

let allPillars = [ Pillar.Value; Pillar.Wire; Pillar.Machine; Pillar.Intent ]

let pillarSlug (p: Pillar) : string =
  match p with
  | Pillar.Value -> "value"
  | Pillar.Wire -> "wire"
  | Pillar.Machine -> "machine"
  | Pillar.Intent -> "intent"

let pillarBySlug (slug: string) : Pillar option =
  allPillars |> List.tryFind (fun p -> pillarSlug p = slug)

let pillarTitle (p: Pillar) : string =
  match p with
  | Pillar.Value -> "The app is a value"
  | Pillar.Wire -> "One wire, many worlds"
  | Pillar.Machine -> "The machine can see the UI"
  | Pillar.Intent -> "Intent, not implementation"

let pillarBlurb (p: Pillar) : string =
  match p with
  | Pillar.Value ->
    "History, branching, provenance, portability of the artefact – an app that is data can be scrubbed, forked, notarised, and teleported."
  | Pillar.Wire ->
    "The same bytes across languages, runtimes, and render targets – a single wire format, many conformant hosts."
  | Pillar.Machine ->
    "Introspection, assertions, self-repair, default-deny safety – the interface is structured data a machine can read, not pixels it must guess at."
  | Pillar.Intent ->
    "Semantic styling, observable accessibility, grammar-constrained emission – you declare intent; the substrate resolves the implementation."
