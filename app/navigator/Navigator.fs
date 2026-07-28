module Fuaran.Live.Navigator

// ============================================================================
//  The Navigator — a keyboard-driven cursor over the session's live tree.
//
//  The playground already shows you the tree three ways: rendered (the live
//  preview), as canonical wire JSON (the inspector), and as builder source (the
//  output projection). The Navigator adds the fourth: a WALK. Next/prev step the
//  tree in DFS pre-order, parent/first-child move vertically, a breadcrumb shows
//  the path from the root, and the node the cursor is on is outlined in place in
//  the rendered preview via its `data-fuaran-node-id` attribute.
//
//  The cursor is a PROJECTION over the session tree, never a copy of it — the
//  session stays the single source of truth, and every derived value (the DFS
//  order, the path, the node card) is recomputed from the current tree on each
//  render.
//
//  The card is no longer read-only: it carries a schema-DERIVED property panel
//  (`PropertyEditor`), and committing a field emits a `TreeOp` through the
//  public apply engine, validator-gated before the session sees it. Nothing in
//  this module edits a tree by hand — the only way a byte moves is an op. The
//  panel's whole field set comes from the introspection + schema surfaces, so a
//  `NodeKind` added to the language is editable here with no edit to this file.
//
//  The cursor is ID-ADDRESSED THROUGHOUT, never positional. Its whole state is a
//  path of `NodeId`s from the root to the focused node; there is no index, no
//  DFS ordinal, and no captured `Node` anywhere in it. That is what makes it
//  survive a tree replacement: after a `ReplaceRoot` or an op batch, the stored
//  path is RE-RESOLVED against the new tree (`reresolve`), and a focused node
//  that no longer exists falls back to the deepest surviving ancestor on its
//  path — root in the worst case. A positional cursor would silently land on a
//  different node instead, which is exactly the failure the identity rule exists
//  to prevent.
// ============================================================================

open Fable.Core
open Feliz
open Fuaran.UI.Types

module Introspect = Fuaran.UI.Ops.Introspect
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── the cursor model (pure — no DOM, no React) ──────────────────────────────

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
let rec private pathsFrom (trail: NodeId list) (node: Node<obj>) : NodeId list list =
  let here = trail @ [ node.Id ]
  here :: (Introspect.descendantNodes node |> List.collect (pathsFrom here))

/// Every id-path in the tree, DFS pre-order, root first.
let allPaths (root: Node<obj>) : NodeId list list = pathsFrom [] root

/// The cursor sitting on the tree's root.
let atRoot (root: Node<obj>) : NavCursor = { Path = [ root.Id ] }

/// The canonical path to `id` in `root`, or `None` when the id is absent.
let pathTo (root: Node<obj>) (id: NodeId) : NodeId list option =
  allPaths root |> List.tryFind (fun p -> List.tryLast p = Some id)

/// Re-resolve a (possibly stale) cursor against the CURRENT tree — the identity
/// rule made operational. The deepest id on the stored path that still exists
/// anywhere in the new tree wins, and its canonical path in the new tree is
/// recomputed; when nothing on the path survives, the cursor falls back to the
/// root. Total: always returns a cursor addressing a node that is really there.
let reresolve (root: Node<obj>) (stored: NavCursor option) : NavCursor =
  match stored with
  | None -> atRoot root
  | Some cursor ->
    cursor.Path
    |> List.rev
    |> List.tryPick (pathTo root)
    |> Option.map (fun p -> { Path = p })
    |> Option.defaultValue (atRoot root)

/// The node a (re-resolved) cursor is focused on.
let focusedNode (root: Node<obj>) (cursor: NavCursor) : Node<obj> option =
  focusedId cursor |> Option.bind (fun id -> Introspect.findNode id root)

/// The nodes on the cursor's path, root → focused — the breadcrumb's data.
/// Ids that have vanished are dropped rather than rendered as dead segments.
let breadcrumb (root: Node<obj>) (cursor: NavCursor) : Node<obj> list =
  cursor.Path |> List.choose (fun id -> Introspect.findNode id root)

// ─── moves ───────────────────────────────────────────────────────────────────
//
// **Ends STOP, they do not wrap.** `next` on the last node in DFS order and
// `prev` on the root are no-ops that return the cursor unchanged. Stopping was
// chosen over wrapping so that holding a key walks the tree exactly once and
// comes to rest at a knowable place; the tab's help hint says so.

let private step (delta: int) (root: Node<obj>) (cursor: NavCursor) : NavCursor =
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
let next (root: Node<obj>) (cursor: NavCursor) : NavCursor = step 1 root cursor

/// The previous node in DFS pre-order; unchanged at the root.
let prev (root: Node<obj>) (cursor: NavCursor) : NavCursor = step -1 root cursor

/// The focused node's parent; unchanged at the root.
let parent (_root: Node<obj>) (cursor: NavCursor) : NavCursor =
  if List.length cursor.Path <= 1 then
    cursor
  else
    { Path = cursor.Path |> List.truncate (List.length cursor.Path - 1) }

/// The focused node's first child (structural or non-list position); unchanged
/// at a leaf.
let firstChild (root: Node<obj>) (cursor: NavCursor) : NavCursor =
  match focusedNode root cursor with
  | None -> reresolve root (Some cursor)
  | Some node ->
    match Introspect.descendantNodes node with
    | [] -> cursor
    | child :: _ -> { Path = cursor.Path @ [ child.Id ] }

/// Jump the cursor to an id already on screen (a breadcrumb click). A missing
/// id leaves the cursor where it is.
let jumpTo (root: Node<obj>) (cursor: NavCursor) (id: NodeId) : NavCursor =
  match pathTo root id with
  | Some p -> { Path = p }
  | None -> cursor

/// The cursor's 1-based position in the DFS walk and the walk's length — the
/// "node 4 of 17" readout. Derived, never stored.
let position (root: Node<obj>) (cursor: NavCursor) : int * int =
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
let walkIds (root: Node<obj>) : string array =
  allPaths root |> List.choose List.tryLast |> List.map idText |> Array.ofList

/// The cursor's id-path, root → focused, as plain strings.
let cursorIds (cursor: NavCursor) : string array =
  cursor.Path |> List.map idText |> Array.ofList

/// The focused node's id as a plain string (`""` for an empty cursor).
let focusedText (cursor: NavCursor) : string =
  focusedId cursor |> Option.map idText |> Option.defaultValue ""

/// `jumpTo`, keyed by the plain id string — a `NodeId` is a single-case DU and
/// cannot be forged across the boundary by hand.
let jumpToText (root: Node<obj>) (cursor: NavCursor) (id: string) : NavCursor = jumpTo root cursor (NodeId id)

// ─── the read-only node card ─────────────────────────────────────────────────

/// The focused node stripped to ITSELF — children and the state-slot subtrees
/// removed — so the card's canonical-JSON summary describes one node rather
/// than dumping its whole subtree. Read-only: this shallow copy is never fed
/// back into the session.
let private shallow (node: Node<obj>) : Node<obj> =
  let kind = Introspect.withChildren node.Kind [] |> Option.defaultValue node.Kind

  { node with
      Kind = kind
      State =
        { node.State with
            OnLoading = None
            OnEmpty = None } }

/// The node's own properties as canonical wire JSON, pretty-printed — the
/// summary the card shows. The wire format is the honest description of a node,
/// so the card reports it rather than inventing a second vocabulary.
let propSummary (node: Node<obj>) : string =
  try
    Session.prettyJson (Canon.encodeNode (shallow node))
  with _ ->
    "– this node's properties could not be projected to wire JSON –"

// ─── the editable property panel ─────────────────────────────────────────────
//
// Every row here is derived — see `PropertyEditor`. This component owns only two
// pieces of local state: the in-progress DRAFT text of the field being typed
// into, and the inline error of the last refused commit. Both are keyed by the
// field's op path and both are cleared when the cursor moves to another node, so
// a draft can never be committed against a node the user has since left.
//
// Commit points differ by control on purpose: a select or a checkbox commits on
// change (the value is already legal — the options came from the schema), while
// a text or number box commits on Enter or on blur (so a partially-typed value
// is not run through the validator on every keystroke).

/// The panel's own state key for a field.
let private draftKey (field: PropertyEditor.Field) : string = field.Group + "/" + field.Path

[<ReactComponent>]
let private PropertyPanel
  (session: Session.SessionState)
  (node: Node<obj>)
  (onEdit: Session.SessionState -> unit)
  : ReactElement =
  let drafts, setDrafts = React.useState (Map.empty: Map<string, string>)
  let failure, setFailure = React.useState (None: (string * string) option)

  let nodeKey = idText node.Id

  // A new focus means new fields: drop drafts + the inline error rather than
  // carry one node's half-typed value onto another's panel.
  React.useEffect (
    (fun () ->
      setDrafts Map.empty
      setFailure None),
    [| box nodeKey |]
  )

  let commit (field: PropertyEditor.Field) (raw: string) =
    match PropertyEditor.commit session node field raw with
    | PropertyEditor.Committed next ->
      // Drop the draft so the row re-reads its value from the applied tree —
      // what is on screen after a commit is the tree, not what was typed.
      setDrafts (Map.remove (draftKey field) drafts)
      setFailure None
      onEdit next
    | PropertyEditor.Rejected message -> setFailure (Some(draftKey field, message))

  let row (field: PropertyEditor.Field) =
    let key = draftKey field
    let draft = drafts |> Map.tryFind key |> Option.defaultValue field.Current

    let control =
      match field.Editor with
      | PropertyEditor.Editor.ReadOnly reason ->
        Html.div
          [ prop.className "fl-nav-field-ro"
            prop.title reason
            prop.children
              [ Html.code [ prop.className "fl-nav-field-value"; prop.text field.Current ]
                Html.span [ prop.className "fl-nav-field-why"; prop.text reason ] ] ]
      | PropertyEditor.Editor.Choice options ->
        Html.select
          [ prop.className "fl-nav-field-input"
            prop.value field.Current
            prop.onChange (fun (v: string) -> commit field v)
            prop.children [ for option in options -> Html.option [ prop.value option; prop.text option ] ] ]
      | PropertyEditor.Editor.Toggle ->
        Html.input
          [ prop.className "fl-nav-field-check"
            prop.type' "checkbox"
            prop.isChecked (field.Current = "true")
            prop.onChange (fun (b: bool) -> commit field (if b then "true" else "false")) ]
      | PropertyEditor.Editor.Text
      | PropertyEditor.Editor.Integer
      | PropertyEditor.Editor.Number ->
        let numeric = field.Editor <> PropertyEditor.Editor.Text

        Html.input
          [ prop.className "fl-nav-field-input"
            prop.type' (if numeric then "number" else "text")
            if field.Editor = PropertyEditor.Editor.Integer then
              prop.step 1
            prop.value draft
            prop.onChange (fun (v: string) -> setDrafts (Map.add key v drafts))
            prop.onBlur (fun _ ->
              if draft <> field.Current then
                commit field draft)
            prop.onKeyDown (fun ev ->
              if ev.key = "Enter" then
                ev.preventDefault ()
                commit field draft) ]

    let error =
      match failure with
      | Some(failedKey, message) when failedKey = key ->
        [ Html.p [ prop.className "fl-nav-field-error"; prop.role "alert"; prop.text message ] ]
      | _ -> []

    Html.div
      [ prop.key key
        prop.className "fl-nav-field"
        prop.children (
          [ Html.label [ prop.className "fl-nav-field-label"; prop.text field.Label ]
            control ]
          @ error
        ) ]

  let derived = PropertyEditor.fields node

  let group (name: string) =
    match derived |> List.filter (fun f -> f.Group = name) with
    | [] -> []
    | rows ->
      [ Html.div
          [ prop.key name
            prop.className "fl-nav-group"
            prop.children (
              Html.h4 [ prop.className "fl-nav-group-title"; prop.text name ]
              :: (rows |> List.map row)
            ) ] ]

  Html.div
    [ prop.className "fl-nav-edit"
      prop.children (group "Properties" @ group "Contained data" @ group "Style") ]

// ─── rendered-node highlight (the only DOM touch in this module) ─────────────

/// Outline the rendered element carrying `nodeId` and scroll it into view,
/// clearing the outline from every other rendered node first. Scoped to the
/// live-preview container so a tree rendered elsewhere on the page (a transcript
/// panel) is never mistaken for the session tree. Returns whether an element was
/// found — an id with no rendered element is normal (a node inside a collapsed
/// disclosure, say), not an error.
// The id escaping is `split`/`join` rather than a regex `replace` deliberately:
// the replacement pattern would be `$&`, and `$` is the Emit macro's own
// placeholder sigil — an escape that reads fine and expands wrongly is not worth
// the two characters it saves.
[<Emit("""(function(id, scopeSel){
  try {
    var all = document.querySelectorAll('[data-fuaran-node-id]');
    for (var i = 0; i < all.length; i++) { all[i].classList.remove('fl-nav-focus'); }
    if (!id) { return false; }
    var scope = document.querySelector(scopeSel) || document;
    var esc = String(id).split('\\').join('\\\\').split('"').join('\\"');
    var el = scope.querySelector('[data-fuaran-node-id="' + esc + '"]');
    if (!el) { return false; }
    el.classList.add('fl-nav-focus');
    if (el.scrollIntoView) { el.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'smooth' }); }
    return true;
  } catch (e) { return false; }
})($0, $1)""")>]
let private highlight (nodeId: string) (scopeSelector: string) : bool = jsNative

/// The CSS selector of the live-preview container the highlight is scoped to.
let previewScope = ".fl-preview-root"

// ─── the tab view ────────────────────────────────────────────────────────────

let private helpHint =
  "↓/j next · ↑/k previous · ←/h parent · →/l first child · Home root. \
The walk is depth-first and stops at both ends — it does not wrap."

let private emptyState: ReactElement =
  Html.div
    [ prop.className "fl-empty fl-nav-empty"
      prop.children
        [ Html.p
            [ prop.text
                "No tree yet. Generate a UI (or load an example) and the navigator will walk it — every node in depth-first order, highlighted in the live preview as you go." ] ] ]

/// The Navigator tab. Holds only the id-path cursor; every other value on screen
/// is derived from the session's tree on each render, so a tree that changes
/// under the cursor re-resolves rather than going stale — including when the
/// change is one the panel below just committed. `onEdit` hands the post-op
/// session back to the host, which is what keeps the rendered view, the
/// inspector and every other tab on the same tree (one source of truth).
[<ReactComponent>]
let NavigatorPane (session: Session.SessionState) (onEdit: Session.SessionState -> unit) : ReactElement =
  let tree = session.Tree
  let stored, setStored = React.useState (None: NavCursor option)

  let cursor = tree |> Option.map (fun root -> reresolve root stored)

  // Re-apply the highlight after EVERY render (no dependency array): React owns
  // the preview's DOM and re-creates those elements on a tree change, which
  // would drop an imperatively-added class. Re-applying is idempotent — the
  // helper clears the outline from every other node first.
  React.useEffect (fun () ->
    let target =
      cursor |> Option.bind focusedId |> Option.map idText |> Option.defaultValue ""

    highlight target previewScope |> ignore)

  let move (f: Node<obj> -> NavCursor -> NavCursor) =
    match tree, cursor with
    | Some root, Some c -> setStored (Some(f root c))
    | _ -> ()

  let onKeyDown (ev: Browser.Types.KeyboardEvent) =
    let handled =
      match ev.key with
      | "ArrowDown"
      | "j" ->
        move next
        true
      | "ArrowUp"
      | "k" ->
        move prev
        true
      | "ArrowLeft"
      | "h" ->
        move parent
        true
      | "ArrowRight"
      | "l" ->
        move firstChild
        true
      | "Home" ->
        (match tree with
         | Some root -> setStored (Some(atRoot root))
         | None -> ())

        true
      | _ -> false

    if handled then
      ev.preventDefault ()

  let body =
    match tree, cursor with
    | Some root, Some c ->
      let here, total = position root c

      let crumbs =
        Html.nav
          [ prop.className "fl-nav-crumbs"
            prop.ariaLabel "Tree path"
            prop.children
              [ for node in breadcrumb root c do
                  Html.button
                    [ prop.key (idText node.Id)
                      prop.className (
                        if focusedId c = Some node.Id then
                          "fl-nav-crumb fl-nav-crumb-active"
                        else
                          "fl-nav-crumb"
                      )
                      prop.title (idText node.Id)
                      prop.text (Introspect.kindName node.Kind)
                      prop.onClick (fun _ -> setStored (Some(jumpTo root c node.Id))) ] ] ]

      let card =
        match focusedNode root c with
        | None -> Html.div [ prop.className "fl-empty"; prop.text "– the focused node has gone –" ]
        | Some node ->
          Html.div
            [ prop.className "fl-nav-card"
              prop.children
                [ Html.div
                    [ prop.className "fl-nav-card-head"
                      prop.children
                        [ Html.span [ prop.className "fl-nav-kind"; prop.text (Introspect.kindName node.Kind) ]
                          Html.code [ prop.className "fl-nav-id"; prop.text ("#" + idText node.Id) ]
                          Html.span
                            [ prop.className "fl-nav-count"
                              prop.text (sprintf "node %d of %d" here total) ] ] ]
                  Html.pre [ prop.className "fl-code fl-nav-props"; prop.text (propSummary node) ]
                  PropertyPanel session node onEdit ] ]

      Html.div
        [ prop.className "fl-nav-body"
          prop.children
            [ crumbs
              card
              Html.div
                [ prop.className "fl-nav-controls"
                  prop.children
                    [ Html.button
                        [ prop.className "fl-btn ghost"
                          prop.text "◂ Parent"
                          prop.onClick (fun _ -> move parent) ]
                      Html.button
                        [ prop.className "fl-btn ghost"
                          prop.text "▴ Previous"
                          prop.onClick (fun _ -> move prev) ]
                      Html.button
                        [ prop.className "fl-btn ghost"
                          prop.text "▾ Next"
                          prop.onClick (fun _ -> move next) ]
                      Html.button
                        [ prop.className "fl-btn ghost"
                          prop.text "First child ▸"
                          prop.onClick (fun _ -> move firstChild) ] ] ] ] ]
    | _ -> emptyState

  Html.div
    [ prop.className "fl-nav"
      prop.tabIndex 0
      prop.role "application"
      prop.ariaLabel "Tree navigator"
      prop.onKeyDown onKeyDown
      prop.children
        [ Html.p
            [ prop.className "fl-nav-intro"
              prop.text
                "Walk the tree the model emitted, and edit it. Click here, then use the keyboard — the node under the \
cursor is outlined in the live preview above, and its properties are editable in the card below. Every edit becomes a \
tree op, checked before it is applied." ]
          Html.p [ prop.className "fl-nav-help"; prop.text helpHint ]
          body ] ]

/// The tab's entry point — what the playground shell mounts.
let view (session: Session.SessionState) (onEdit: Session.SessionState -> unit) : ReactElement =
  NavigatorPane session onEdit
