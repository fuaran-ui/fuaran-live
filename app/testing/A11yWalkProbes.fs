module Fuaran.Live.Testing.A11yWalkProbes

// ============================================================================
//  TEST-ONLY. The accessibility lens's flat diagnostic surface (flag summaries, aria, quick fixes).
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fable.Core
open Feliz
open Fuaran.UI.Types

module Introspect = Fuaran.UI.Ops.Introspect
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module Aria = Fuaran.UI.Renderer.Accessibility
module Resolver = Fuaran.UI.Renderer.BindingResolver
module Validate = Fuaran.UI.PreEmitValidate
module Defaults = Fuaran.UI.Defaults

open Fuaran.Live
open Fuaran.Live.A11yWalk

// ─── flat diagnostic surface (cross-boundary friendly) ───────────────────────
//
// F# lists, records and DUs are awkward to assert on across the Fable boundary,
// so — exactly as the Phase 710 cursor helpers and `PropertyEditor`'s flat
// surface do — the same values are projected to plain strings and arrays. These
// are the headless test surface AND a host-agnostic description of the audit.

/// Every finding as `"<nodeId>|<code>|<severity>|<fixPath>"`, walk order.
/// `fixPath` is empty for an unfixable finding.
let flagSummary (root: Node<obj>) : string array =
  treeFlags root
  |> List.map (fun f ->
    f.NodeId
    + "|"
    + f.Code
    + "|"
    + severityTag f.Severity
    + "|"
    + (f.Fix |> Option.defaultValue ""))
  |> Array.ofList

/// The findings against one node, addressed by its plain id string.
let flagsAt (root: Node<obj>) (nodeId: string) : string array =
  match Introspect.findNode (NodeId nodeId) root with
  | None -> [||]
  | Some node ->
    nodeFlags root node
    |> List.map (fun f -> f.Code + "|" + severityTag f.Severity + "|" + (f.Fix |> Option.defaultValue ""))
    |> Array.ofList

/// The emitted `aria-*` for one node, addressed by its plain id string.
let ariaAt (root: Node<obj>) (nodeId: string) : string array =
  match Introspect.findNode (NodeId nodeId) root with
  | None -> [||]
  | Some node -> ariaSummary node

/// `nextFlaggedId` / `prevFlaggedId` projected to "" for "no further flag", so
/// a test can drive the flags-only walk without an option across the boundary.
let nextFlagText (root: Node<obj>) (fromId: string) : string =
  nextFlaggedId root fromId |> Option.defaultValue ""

let prevFlagText (root: Node<obj>) (fromId: string) : string =
  prevFlaggedId root fromId |> Option.defaultValue ""

/// The quick-fix, addressed by plain strings: commit `raw` to the field the
/// flag `code` on node `nodeId` points at. Routed through
/// `PropertyEditorProbes.commitAt`, so this is the SAME op path as any other edit —
/// validator-gated, recorded, undoable. Refuses rather than inventing a path
/// when the flag has no fix.
let quickFixAt
  (session: Session.SessionState)
  (nodeId: string)
  (code: string)
  (raw: string)
  : {| Ok: bool
       Error: string
       Next: Session.SessionState |}
  =
  let refused message =
    {| Ok = false
       Error = message
       Next = session |}

  match session.Tree with
  | None -> refused "there is no tree to audit"
  | Some root ->
    match Introspect.findNode (NodeId nodeId) root with
    | None -> refused ("no node with id '" + nodeId + "'")
    | Some node ->
      match nodeFlags root node |> List.tryFind (fun f -> f.Code = code) with
      | None -> refused ("no '" + code + "' finding on node '" + nodeId + "'")
      | Some flag ->
        match flag.Fix with
        | None -> refused flag.Unfixable
        | Some path -> PropertyEditorProbes.commitAt session nodeId path raw

/// The total number of findings (a node may carry several).
let flagCount (root: Node<obj>) : int = List.length (treeFlags root)
