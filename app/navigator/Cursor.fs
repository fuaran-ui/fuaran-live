module Fuaran.Live.Cursor

// ============================================================================
//  The cursor — THE definition of "the walk" over a Fuaran tree.
//
//  This module is the canonical statement of the walk's semantics, and it is
//  the only place in this repo they are written down. Both entries of the site
//  consume it: the playground's Navigator tab (a live session's tree) and the
//  showcase's Navigator page (a canned tree, no key, no session). Before this
//  module existed the showcase carried a hand-mirrored copy, because the tab it
//  mirrored could not be linked into a zero-egress artifact — two copies that
//  agreed only for as long as someone kept checking. One module owns the walk
//  now, so there is nothing left to drift.
//
//  THE SEMANTICS, stated once:
//
//   * A cursor is a PATH OF IDS from the root down to the focused node — ids
//     only. No index, no DFS ordinal, no captured `Node`. Everything else (the
//     walk order, the breadcrumb, the "node 4 of 17" readout) is DERIVED from
//     the current tree on demand, never stored.
//
//   * The walk order is DFS PRE-ORDER over `Introspect.descendantNodes` — the
//     traversal surface, so nodes held in non-list positions are walked too,
//     not just structural children.
//
//   * ENDS STOP, THEY DO NOT WRAP. `next` on the last node in DFS order and
//     `prev` on the root return the cursor unchanged. Stopping was chosen over
//     wrapping so that holding a key walks the tree exactly once and comes to
//     rest at a knowable place.
//
//   * `parent` at the root and `firstChild` at a leaf are likewise no-ops, not
//     errors and not wraps.
//
//   * RE-RESOLUTION is what makes the id addressing pay. Against a tree that
//     has changed underneath it, the deepest id on the stored path that still
//     exists anywhere wins, and its canonical path in the NEW tree is
//     recomputed; when nothing on the path survives, the cursor falls back to
//     the root. Total — it always returns a cursor addressing a node that is
//     really there. A positional cursor would silently land on a different
//     node instead, which is exactly the failure the identity rule exists to
//     prevent.
//
//  DEPENDENCIES: the typed tree and the traversal surface, and nothing else.
//  No session, no key store, no effect ports, no React, no DOM. That is the
//  structural property that lets the zero-egress showcase artifact link it —
//  keep it that way; anything a cursor move has to *tell* the outside world
//  belongs in the pane that owns the cursor, not here.
// ============================================================================

open Fuaran.UI.Types

module Introspect = Fuaran.UI.Ops.Introspect

/// A cursor over a Fuaran tree, addressed **by id**: the path of `NodeId`s from
/// the root down to the focused node (so `Path` is never empty for a live
/// cursor, and its last element is the focus). Ids only — no index, no ordinal,
/// no captured node.
type NavCursor = { Path: NodeId list }

/// The focused node's id.
let focusedId (cursor: NavCursor) : NodeId option = List.tryLast cursor.Path

/// The raw string of a `NodeId` (the wire spelling, and the value the renderer
/// emits as `data-fuaran-node-id`).
let idText (NodeId s) : string = s

/// Every id-path in `root`'s subtree, in **DFS pre-order** — the canonical walk
/// order the cursor's next/prev step through. Built over
/// `Introspect.descendantNodes` (the traversal surface), so nodes held in
/// non-list positions are walked too, not just structural children.
let rec private pathsFrom (trail: NodeId list) (node: Node<'Msg>) : NodeId list list =
  let here = trail @ [ (NodeId node.Id) ]
  here :: (Introspect.descendantNodes node |> List.collect (pathsFrom here))

/// Every id-path in the tree, DFS pre-order, root first.
let allPaths (root: Node<'Msg>) : NodeId list list = pathsFrom [] root

/// The cursor sitting on the tree's root.
let atRoot (root: Node<'Msg>) : NavCursor = { Path = [ (NodeId root.Id) ] }

/// The canonical path to `id` in `root`, or `None` when the id is absent.
let pathTo (root: Node<'Msg>) (id: NodeId) : NodeId list option =
  allPaths root |> List.tryFind (fun p -> List.tryLast p = Some id)

/// Re-resolve a (possibly stale) cursor against the CURRENT tree — the identity
/// rule made operational. The deepest id on the stored path that still exists
/// anywhere in the new tree wins, and its canonical path in the new tree is
/// recomputed; when nothing on the path survives, the cursor falls back to the
/// root. Total: always returns a cursor addressing a node that is really there.
let reresolve (root: Node<'Msg>) (stored: NavCursor option) : NavCursor =
  match stored with
  | None -> atRoot root
  | Some cursor ->
    cursor.Path
    |> List.rev
    |> List.tryPick (pathTo root)
    |> Option.map (fun p -> { Path = p })
    |> Option.defaultValue (atRoot root)

/// The node a (re-resolved) cursor is focused on.
let focusedNode (root: Node<'Msg>) (cursor: NavCursor) : Node<'Msg> option =
  focusedId cursor |> Option.bind (fun id -> Introspect.findNode id root)

/// The nodes on the cursor's path, root → focused — the breadcrumb's data.
/// Ids that have vanished are dropped rather than rendered as dead segments.
let breadcrumb (root: Node<'Msg>) (cursor: NavCursor) : Node<'Msg> list =
  cursor.Path |> List.choose (fun id -> Introspect.findNode id root)

// ─── moves ───────────────────────────────────────────────────────────────────
//
// **Ends STOP, they do not wrap** — see the header. `next` on the last node in
// DFS order and `prev` on the root are no-ops that return the cursor unchanged.

let private step (delta: int) (root: Node<'Msg>) (cursor: NavCursor) : NavCursor =
  let paths = allPaths root

  match paths |> List.tryFindIndex (fun p -> p = cursor.Path) with
  | None -> reresolve root (Some cursor)
  | Some i ->
    let j = i + delta

    if j < 0 || j >= List.length paths then
      cursor
    else
      { Path = paths[j] }

/// The next node in DFS pre-order; unchanged at the end of the walk.
let next (root: Node<'Msg>) (cursor: NavCursor) : NavCursor = step 1 root cursor

/// The previous node in DFS pre-order; unchanged at the root.
let prev (root: Node<'Msg>) (cursor: NavCursor) : NavCursor = step -1 root cursor

/// The focused node's parent; unchanged at the root.
let parent (_root: Node<'Msg>) (cursor: NavCursor) : NavCursor =
  if List.length cursor.Path <= 1 then
    cursor
  else
    { Path = cursor.Path |> List.truncate (List.length cursor.Path - 1) }

/// The focused node's first child (structural or non-list position); unchanged
/// at a leaf.
let firstChild (root: Node<'Msg>) (cursor: NavCursor) : NavCursor =
  match focusedNode root cursor with
  | None -> reresolve root (Some cursor)
  | Some node ->
    match Introspect.descendantNodes node with
    | [] -> cursor
    | child :: _ -> { Path = cursor.Path @ [ (NodeId child.Id) ] }

/// Jump the cursor to an id already on screen (a breadcrumb click). A missing
/// id leaves the cursor where it is.
let jumpTo (root: Node<'Msg>) (cursor: NavCursor) (id: NodeId) : NavCursor =
  match pathTo root id with
  | Some p -> { Path = p }
  | None -> cursor

/// The cursor's 1-based position in the DFS walk and the walk's length — the
/// "node 4 of 17" readout. Derived, never stored.
let position (root: Node<'Msg>) (cursor: NavCursor) : int * int =
  let paths = allPaths root

  let idx =
    paths |> List.tryFindIndex (fun p -> p = cursor.Path) |> Option.defaultValue 0

  idx + 1, List.length paths

// ─── flat diagnostic surface (cross-boundary friendly) ───────────────────────
//
// `NodeId` is a single-case DU and the walk is an F# list — both awkward to
// assert on from the JS side of the Fable boundary. These project the same
// values to plain string arrays, exactly as `Session.ingestResult` does for the
// closed loop, so the cursor model is testable headlessly over the Fable output.

/// Every node id in the tree, DFS pre-order — the walk, as plain strings.
let walkIds (root: Node<'Msg>) : string array =
  allPaths root |> List.choose List.tryLast |> List.map idText |> Array.ofList

/// The cursor's id-path, root → focused, as plain strings.
let cursorIds (cursor: NavCursor) : string array =
  cursor.Path |> List.map idText |> Array.ofList

/// The cursor's id-path as a plain string LIST — what a consumer keyed on the
/// path (the source projections' span lookup) wants, without an array hop.
let pathText (cursor: NavCursor) : string list = cursor.Path |> List.map idText

/// The focused node's id as a plain string (`""` for an empty cursor).
let focusedText (cursor: NavCursor) : string =
  focusedId cursor |> Option.map idText |> Option.defaultValue ""

/// `jumpTo`, keyed by the plain id string — a `NodeId` is a single-case DU and
/// cannot be forged across the boundary by hand.
let jumpToText (root: Node<'Msg>) (cursor: NavCursor) (id: string) : NavCursor = jumpTo root cursor (NodeId id)
