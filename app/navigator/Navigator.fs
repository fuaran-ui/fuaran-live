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

/// The host's effect seam, bound to the browser implementation exactly as the
/// shell binds it. The export itself takes the `EffectPorts` INTERFACE
/// (`OpLog.download`), so the seam is intact and a different host swaps its own;
/// this binding only picks the default for the pane the browser mounts.
let private effects = Byok.browserEffectPorts

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

// ─── cursor-changed subscription (the Phase 714 hook) ────────────────────────
//
// The Navigator OWNS the cursor; panes outside it — the source-projection sync
// — need to follow it. Lifting the cursor into the app model would make every
// pane a party to the walk and would put a projection concern inside the
// cursor's state, so instead the pane PUBLISHES its id-path and interested
// views subscribe. One-way and read-only: a subscriber can watch the walk,
// never steer it, and the Navigator's own behaviour is unchanged whether anyone
// is listening or not.
//
// The three module-level mutables are a subscriber registry rather than state:
// the browser is single-threaded, the list is only appended to and filtered,
// and `lastPath` exists so a pane mounted mid-walk is handed the current
// position at once instead of waiting for the next keystroke.

let mutable private cursorSubs: (int * (string array -> unit)) list = []
let mutable private nextSubId = 0
let mutable private lastPath: string array = [||]

/// Subscribe to cursor moves; the returned thunk unsubscribes. The current path
/// is delivered immediately, so a late subscriber starts in step.
let subscribeCursor (f: string array -> unit) : unit -> unit =
  let id = nextSubId
  nextSubId <- nextSubId + 1
  cursorSubs <- cursorSubs @ [ id, f ]
  f lastPath
  fun () -> cursorSubs <- cursorSubs |> List.filter (fun (i, _) -> i <> id)

/// Publish the cursor's id-path. Idempotent: an unchanged path is swallowed, so
/// the after-every-render call below costs nothing when the cursor has not moved.
let private publishCursor (path: string array) : unit =
  if path <> lastPath then
    lastPath <- path

    for _, f in cursorSubs do
      f path

// ─── the tab view ────────────────────────────────────────────────────────────

let private helpHint =
  "↓/j next · ↑/k previous · ←/h parent · →/l first child · Home root · \
n insert · m pick up/drop · Alt+↑/↓ reorder · Delete remove (twice to confirm) · Esc cancel · \
Ctrl+Z undo · Ctrl+Shift+Z redo. \
The walk is depth-first and stops at both ends — it does not wrap."

/// A placement in words, for the placement select and the destination readout.
let private describePlacement (placement: StructuralEdit.Placement) : string =
  match placement with
  | StructuralEdit.Placement.Last -> "at the end"
  | StructuralEdit.Placement.Before anchor -> "before #" + idText anchor
  | StructuralEdit.Placement.After anchor -> "after #" + idText anchor

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

  // The structural editor (Phase 713). Five pieces of local state, and only one
  // of them survives a cursor move: `held` — carrying a node while you walk to
  // its new parent IS the pick-up/drop interaction, so it is cleared by a drop
  // or by Escape, never by the walk. The other four are reset the moment focus
  // lands elsewhere (the effect below), so an armed removal or a placement
  // naming a sibling of the node you just left can never be committed.
  let paletteOpen, setPaletteOpen = React.useState false
  let placementSel, setPlacementSel = React.useState ""
  let kindSel, setKindSel = React.useState ""
  let removeArmed, setRemoveArmed = React.useState false
  let structuralError, setStructuralError = React.useState (None: string option)
  let held, setHeld = React.useState (None: string option)

  let cursor = tree |> Option.map (fun root -> reresolve root stored)

  // Re-apply the highlight after EVERY render (no dependency array): React owns
  // the preview's DOM and re-creates those elements on a tree change, which
  // would drop an imperatively-added class. Re-applying is idempotent — the
  // helper clears the outline from every other node first.
  React.useEffect (fun () ->
    let target =
      cursor |> Option.bind focusedId |> Option.map idText |> Option.defaultValue ""

    highlight target previewScope |> ignore)

  // Phase 714 hook — publish the cursor's id-path so the source-projection panes
  // can follow the walk. Same no-dependency-array reasoning as the highlight
  // above: `cursor` is re-derived from the session's tree on every render, so an
  // edit that moves the focused node re-publishes the re-resolved path.
  // `publishCursor` swallows an unchanged path, so this does not churn.
  React.useEffect (fun () -> cursor |> Option.map cursorIds |> Option.defaultValue [||] |> publishCursor)

  // Phase 713 — a new focus means a new destination, so the placement anchor,
  // the chosen kind, the armed removal and the last refusal all go. Same
  // reasoning (and same shape) as the property panel's draft reset one level
  // down: state keyed to a node must not outlive the cursor's stay on it.
  let focusedKey = cursor |> Option.map focusedText |> Option.defaultValue ""

  React.useEffect (
    (fun () ->
      setPlacementSel ""
      setKindSel ""
      setRemoveArmed false
      setStructuralError None),
    [| box focusedKey |]
  )

  let move (f: Node<obj> -> NavCursor -> NavCursor) =
    match tree, cursor with
    | Some root, Some c -> setStored (Some(f root c))
    | _ -> ()

  // ── the structural destination ─────────────────────────────────────────────
  //
  // Where an insertion would land: inside the focused node when it takes
  // children, otherwise beside it under its parent — the reading of "insert
  // here" that is useful at a leaf, which is where the cursor mostly is. The
  // placement select overrides the second half of that answer; `placementSel`
  // empty means "whatever the cursor implies", so the readout stays live as the
  // walk moves rather than freezing on the first node visited.

  let insertTarget =
    match tree, cursor with
    | Some root, Some c ->
      focusedId c
      |> Option.bind (StructuralEdit.defaultTarget root)
      |> Option.map (fun target ->
        if placementSel = "" then
          target
        else
          { target with
              Placement = StructuralEdit.parsePlacement placementSel })
    | _ -> None

  // The palette runs a decode + apply + validate per candidate kind, so it is
  // computed only while the panel is open and memoised on everything it depends
  // on — the destination and the tree itself. The tree's canonical JSON is the
  // honest key: an edit anywhere can change what is legal here, and a cheaper
  // key (an op count, a child count) would be a guess about which edits matter.
  let paletteKey =
    match insertTarget with
    | Some target when paletteOpen ->
      idText target.ParentId
      + "|"
      + StructuralEdit.placementSpec target.Placement
      + "|"
      + OpLog.canonicalTree session
    | _ -> ""

  let paletteEntries =
    React.useMemo (
      (fun () ->
        match insertTarget with
        | Some target when paletteKey <> "" -> StructuralEdit.palette session target
        | _ -> []),
      [| box paletteKey |]
    )

  /// The kind the Insert button would use: the explicit choice while it is still
  /// on offer, else the first thing offered.
  let chosenKind =
    if paletteEntries |> List.exists (fun entry -> entry.Kind = kindSel) then
      kindSel
    else
      paletteEntries |> List.tryHead |> Option.map _.Kind |> Option.defaultValue ""

  // ── the one structural commit path ─────────────────────────────────────────
  //
  // Every structural action funnels through here: on acceptance the cursor is
  // landed DELIBERATELY (the caller says where) and the session is handed to
  // `onEdit` — the same channel a property commit and an undo use, so every
  // other pane follows a structural edit exactly as it follows any other. On
  // refusal nothing moves and the reason is shown inline; the session was never
  // mutated in the first place, so there is nothing to restore.
  let structural (landing: NodeId option) (result: PropertyEditor.CommitOutcome) =
    match result with
    | PropertyEditor.Committed next ->
      setStructuralError None
      setRemoveArmed false

      match landing, next.Tree with
      | Some id, Some root ->
        match pathTo root id with
        | Some path -> setStored (Some { Path = path })
        | None -> ()
      | _ -> ()

      onEdit next
    | PropertyEditor.Rejected message -> setStructuralError (Some message)

  let doInsert () =
    match insertTarget, paletteEntries |> List.tryFind (fun entry -> entry.Kind = chosenKind) with
    | Some target, Some entry -> structural (Some entry.Node.Id) (StructuralEdit.insert session target entry.Node)
    | _ -> ()

  let doRemove () =
    match tree, cursor |> Option.bind focusedId with
    | Some root, Some id ->
      // Computed BEFORE the op, while the node is still there to have
      // neighbours; applied after, so focus lands where the user was looking.
      let landing = StructuralEdit.fallbackAfterRemove root id
      structural landing (StructuralEdit.remove session id)
    | _ -> ()

  let doNudge (delta: int) =
    match cursor |> Option.bind focusedId with
    | Some id -> structural (Some id) (StructuralEdit.nudge session id delta)
    | None -> ()

  let doDrop () =
    match tree, held, cursor |> Option.bind focusedId with
    | Some root, Some heldId, Some focus ->
      let moved = NodeId heldId

      match StructuralEdit.defaultTarget root focus with
      | Some target when StructuralEdit.canDrop root moved target.ParentId ->
        setHeld None
        structural (Some moved) (StructuralEdit.move session target moved)
      | Some _ -> setStructuralError (Some "a node cannot move into itself or into something inside it")
      | None -> setStructuralError (Some "there is nowhere here to drop it")
    | _ -> ()

  /// Pick the focused node up, or — when something is already held — drop it
  /// here. One key, because it is one gesture.
  let doPickUpOrDrop () =
    match held, tree, cursor |> Option.bind focusedId with
    | Some _, _, _ -> doDrop ()
    | None, Some root, Some id ->
      if (Introspect.findParent id root).IsSome then
        setHeld (Some(idText id))
        setStructuralError None
      else
        setStructuralError (Some "the root node has nowhere to move to")
    | _ -> ()

  let doCancel () =
    setHeld None
    setRemoveArmed false
    setPaletteOpen false
    setStructuralError None

  // Undo/redo rebuild the session by REPLAY (see `OpLog`) and hand the result
  // back through `onEdit`, the same channel a property commit uses — so every
  // other pane follows a time-step exactly as it follows an edit, and the
  // cursor re-resolves against the rebuilt tree by the Phase 710 fallback rule
  // (`cursor` is re-derived from `session.Tree` on each render, so a node the
  // undo removed hands focus to its deepest surviving ancestor rather than to
  // whatever now sits at the old position).
  let stepHistory (f: Session.SessionState -> Session.SessionState option) = f session |> Option.iter onEdit

  let onKeyDown (ev: Browser.Types.KeyboardEvent) =
    let handled =
      // Modified keys first: the plain-letter moves below are single-key
      // bindings, so Ctrl+Z must be claimed before `ev.key` is matched bare.
      // Browsers report the letter case-shifted while Shift is held, hence both.
      match ev.key with
      | "z"
      | "Z" when ev.ctrlKey || ev.metaKey ->
        stepHistory (if ev.shiftKey then OpLog.redo else OpLog.undo)
        true
      | _ when ev.ctrlKey || ev.metaKey -> false
      // Phase 713 — Alt+↑/↓ reorder the focused node among its siblings. Alt is
      // claimed before the bare arrows below for the same reason Ctrl is: the
      // plain arrow is already a walk binding, and a guard that ran after it
      // would never be reached.
      | "ArrowUp" when ev.altKey ->
        doNudge -1
        true
      | "ArrowDown" when ev.altKey ->
        doNudge 1
        true
      | _ when ev.altKey -> false
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
      // Phase 713 — the structural gestures. `Delete` arms on the first press
      // and commits on the second: a subtree removal is the one edit here whose
      // undo, though real, costs the user a moment of alarm, and an in-UI
      // confirmation says so without reaching for a native dialog (which would
      // put a second browser dependency in a module whose only DOM touch is the
      // preview highlight).
      | "n"
      | "N" ->
        setPaletteOpen (not paletteOpen)
        setStructuralError None
        true
      | "m"
      | "M" ->
        doPickUpOrDrop ()
        true
      | "Delete"
      | "Backspace" ->
        if removeArmed then doRemove () else setRemoveArmed true
        true
      | "Escape" ->
        doCancel ()
        true
      | _ -> false

    if handled then
      ev.preventDefault ()

  // The history row: where the session stands in its recorded op sequence, the
  // two replay steps, and the export. The readout is deliberately explicit
  // ("3 of 5 ops") rather than a bare pair of arrows — the whole claim of this
  // tab is that the session IS its op stream, so saying where you are in that
  // stream is the honest thing to put on screen.
  let historyControls =
    let applied = OpLog.cursor session
    let total = OpLog.recorded session

    Html.div
      [ prop.className "fl-nav-controls"
        prop.children
          [ Html.span
              [ prop.className "fl-nav-count"
                prop.text (
                  if total = 0 then
                    "no ops recorded"
                  else
                    sprintf "op %d of %d" applied total
                ) ]
            Html.button
              [ prop.className "fl-btn ghost"
                prop.text "↶ Undo"
                prop.disabled (not (OpLog.canUndo session))
                prop.title "Ctrl+Z — replay the session to the previous op"
                prop.onClick (fun _ -> stepHistory OpLog.undo) ]
            Html.button
              [ prop.className "fl-btn ghost"
                prop.text "↷ Redo"
                prop.disabled (not (OpLog.canRedo session))
                prop.title "Ctrl+Shift+Z — replay one op forward"
                prop.onClick (fun _ -> stepHistory OpLog.redo) ]
            Html.button
              [ prop.className "fl-btn ghost"
                prop.text "Download session op log"
                prop.title
                  "The base tree, every applied op with its origin, and the final tree — as canonical wire JSON"
                prop.onClick (fun _ -> OpLog.download effects session) ] ] ]

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

      // ── the structural panel (Phase 713) ──────────────────────────────────
      //
      // Everything on it is derived: the destination from the cursor, the
      // placement options from the destination parent's actual children, and
      // the kind list from the schema (filtered to what would really be
      // accepted here). Nothing is enumerated by hand, so a kind added to the
      // language and a child added to the tree both show up with no edit here.
      let structuralPanel =
        let destination =
          match insertTarget with
          | None -> Html.span [ prop.className "fl-nav-count"; prop.text "nowhere to insert from here" ]
          | Some target ->
            Html.span
              [ prop.className "fl-nav-count"
                prop.text ("into #" + idText target.ParentId + ", " + describePlacement target.Placement) ]

        let placementSelect =
          match insertTarget with
          | None -> Html.none
          | Some target ->
            let options =
              ("last", "at the end")
              :: [ for kid in StructuralEdit.childIdsOf root target.ParentId do
                     let id = idText kid
                     yield "before:" + id, "before #" + id
                     yield "after:" + id, "after #" + id ]

            Html.select
              [ prop.className "fl-nav-field-input"
                prop.ariaLabel "Where the new node goes"
                prop.value (StructuralEdit.placementSpec target.Placement)
                prop.onChange (fun (v: string) -> setPlacementSel v)
                prop.children [ for value, label in options -> Html.option [ prop.value value; prop.text label ] ] ]

        let kindSelect =
          match paletteEntries with
          | [] ->
            Html.span
              [ prop.className "fl-nav-field-why"
                prop.text "nothing can be inserted here — this node holds no children" ]
          | entries ->
            Html.select
              [ prop.className "fl-nav-field-input"
                prop.ariaLabel "What to insert"
                prop.value chosenKind
                prop.onChange (fun (v: string) -> setKindSel v)
                prop.children [ for entry in entries -> Html.option [ prop.value entry.Kind; prop.text entry.Kind ] ] ]

        let paletteBlock =
          if not paletteOpen then
            Html.none
          else
            Html.div
              [ prop.className "fl-nav-palette"
                prop.children
                  [ destination
                    placementSelect
                    kindSelect
                    Html.button
                      [ prop.className "fl-btn"
                        prop.text "Insert"
                        prop.disabled (String.length chosenKind = 0)
                        prop.title
                          "Appends, or emits Batch [InsertChild, ReorderChildren] when the placement is not last"
                        prop.onClick (fun _ -> doInsert ()) ] ] ]

        let carrying =
          match held with
          | None -> Html.none
          | Some id ->
            Html.p
              [ prop.className "fl-nav-held"
                prop.role "status"
                prop.text (
                  "Carrying #"
                  + id
                  + " — walk to where it should go and press Drop (m). The move appends; a placement that is not last \
adds a ReorderChildren naming every sibling."
                ) ]

        let refusal =
          match structuralError with
          | None -> Html.none
          | Some message -> Html.p [ prop.className "fl-nav-field-error"; prop.role "alert"; prop.text message ]

        Html.div
          [ prop.className "fl-nav-structural"
            prop.children
              [ Html.div
                  [ prop.className "fl-nav-controls"
                    prop.children
                      [ Html.button
                          [ prop.className (if paletteOpen then "fl-btn" else "fl-btn ghost")
                            prop.text "＋ Insert…"
                            prop.title "n — choose a kind and a placement"
                            prop.onClick (fun _ ->
                              setPaletteOpen (not paletteOpen)
                              setStructuralError None) ]
                        Html.button
                          [ prop.className "fl-btn ghost"
                            prop.text (
                              match held with
                              | Some _ -> "▼ Drop here"
                              | None -> "✥ Pick up"
                            )
                            prop.title "m — pick the focused node up, then walk to its new parent and drop it"
                            prop.onClick (fun _ -> doPickUpOrDrop ()) ]
                        Html.button
                          [ prop.className "fl-btn ghost"
                            prop.text "⤒ Move up"
                            prop.title "Alt+↑ — ReorderChildren naming the full sibling order"
                            prop.onClick (fun _ -> doNudge -1) ]
                        Html.button
                          [ prop.className "fl-btn ghost"
                            prop.text "⤓ Move down"
                            prop.title "Alt+↓ — ReorderChildren naming the full sibling order"
                            prop.onClick (fun _ -> doNudge 1) ]
                        Html.button
                          [ prop.className (if removeArmed then "fl-btn" else "fl-btn ghost")
                            prop.text (if removeArmed then "Confirm remove" else "🗑 Remove")
                            prop.title "Delete — press twice; the whole subtree goes, and Ctrl+Z brings it back"
                            prop.onClick (fun _ -> if removeArmed then doRemove () else setRemoveArmed true) ]
                        (if removeArmed then
                           Html.button
                             [ prop.className "fl-btn ghost"
                               prop.text "Cancel"
                               prop.onClick (fun _ -> setRemoveArmed false) ]
                         else
                           Html.none) ] ]
                paletteBlock
                carrying
                refusal ] ]

      Html.div
        [ prop.className "fl-nav-body"
          prop.children
            [ crumbs
              card
              structuralPanel
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
                          prop.onClick (fun _ -> move firstChild) ] ] ]
              historyControls ] ]
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
