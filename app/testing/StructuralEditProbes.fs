module Fuaran.Live.Testing.StructuralEditProbes

// ============================================================================
//  TEST-ONLY. The structural editor's flat diagnostic surface (kind palette, flat insert/remove/move/nudge).
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fable.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

module Introspect = Fuaran.UI.Ops.Introspect
module Decode = Fuaran.UI.Ops.JsonDecode

open Fuaran.Live
open Fuaran.Live.StructuralEdit

let private targetOf (parentId: string) (placement: string) : Target =
  { ParentId = NodeId parentId
    Placement = parsePlacement placement }

/// Every kind discriminator the canonical schema knows — the palette's whole
/// vocabulary source, before any per-destination filtering.
let schemaKinds () : string array = kindsOf (Agent.getKindSchema None)

/// The raw synthesised wire JSON for one kind, addressed by plain string — the
/// emitter lock's subject (see `defaultNodeWire`).
let defaultWireFor (session: Session.SessionState) (kind: string) : string =
  match session.Tree with
  | None -> ""
  | Some root -> defaultNodeWire root kind

/// A parent's structural child ids, as plain strings.
let childIds (session: Session.SessionState) (parentId: string) : string array =
  match session.Tree with
  | None -> [||]
  | Some root -> childIdsOf root (NodeId parentId) |> List.map idText |> Array.ofList

/// The kinds offered at a destination, as plain strings.
let paletteKinds (session: Session.SessionState) (parentId: string) (placement: string) : string array =
  palette session (targetOf parentId placement) |> List.map _.Kind |> Array.ofList

/// The default structural destination for a focused node, as
/// `"<parentId>|<placement>"` (`""` when there is nowhere to put anything).
let defaultTargetSpec (session: Session.SessionState) (focused: string) : string =
  match session.Tree with
  | None -> ""
  | Some root ->
    defaultTarget root (NodeId focused)
    |> Option.map (fun t -> idText t.ParentId + "|" + placementSpec t.Placement)
    |> Option.defaultValue ""

/// The id the cursor should fall back to once `nodeId` is removed (`""` for the
/// root, which cannot be removed).
let fallbackIdAfterRemove (session: Session.SessionState) (nodeId: string) : string =
  match session.Tree with
  | None -> ""
  | Some root ->
    fallbackAfterRemove root (NodeId nodeId)
    |> Option.map idText
    |> Option.defaultValue ""

/// The canonical JSON of the most recently APPLIED op (`""` when none) — the
/// surface the placement claim is asserted on: the shape of the op is the claim.
let lastOpJson (session: Session.SessionState) : string =
  session.Ops |> List.tryLast |> Option.defaultValue ""

let private outcome
  (session: Session.SessionState)
  (result: PropertyEditor.CommitOutcome)
  : {| Ok: bool
       Error: string
       Next: Session.SessionState |}
  =
  match result with
  | PropertyEditor.Committed next -> {| Ok = true; Error = ""; Next = next |}
  | PropertyEditor.Rejected message ->
    {| Ok = false
       Error = message
       Next = session |}

/// Insert a default node of `kind` at a destination, addressed by plain strings.
/// `Ok` false leaves `Next` as the input session, so a test can assert the tree
/// really was untouched.
let insertAt
  (session: Session.SessionState)
  (parentId: string)
  (placement: string)
  (kind: string)
  : {| Ok: bool
       Error: string
       Next: Session.SessionState |}
  =
  match session.Tree with
  | None -> outcome session (PropertyEditor.Rejected "there is no tree to edit")
  | Some root ->
    match defaultNodeFor root kind with
    | None ->
      outcome session (PropertyEditor.Rejected("no minimal default node could be synthesised for '" + kind + "'"))
    | Some child -> outcome session (insert session (targetOf parentId placement) child)

/// Remove a node, addressed by plain string.
let removeAt
  (session: Session.SessionState)
  (nodeId: string)
  : {| Ok: bool
       Error: string
       Next: Session.SessionState |}
  =
  outcome session (remove session (NodeId nodeId))

/// Move a node to a destination, addressed by plain strings.
let moveTo
  (session: Session.SessionState)
  (nodeId: string)
  (parentId: string)
  (placement: string)
  : {| Ok: bool
       Error: string
       Next: Session.SessionState |}
  =
  outcome session (move session (targetOf parentId placement) (NodeId nodeId))

/// Nudge a node among its siblings, addressed by plain string.
let nudgeAt
  (session: Session.SessionState)
  (nodeId: string)
  (delta: int)
  : {| Ok: bool
       Error: string
       Next: Session.SessionState |}
  =
  outcome session (nudge session (NodeId nodeId) delta)

/// Whether a node may legally be dropped under a parent, addressed by plain
/// strings — the pick-up/drop interaction's own guard, made assertable.
let canDropAt (session: Session.SessionState) (moved: string) (parentId: string) : bool =
  match session.Tree with
  | None -> false
  | Some root -> canDrop root (NodeId moved) (NodeId parentId)
