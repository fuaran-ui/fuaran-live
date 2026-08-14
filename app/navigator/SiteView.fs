module Fuaran.Live.SiteView

// ============================================================================
//  The Navigator's site view — several named pages, one walk.
//
//  A "site" here is a page set: named trees, each with its OWN session (its own
//  op history, snapshots and attributed record), of which exactly one is ACTIVE
//  at a time. The active page's session is the app's ordinary live session —
//  the single source of truth every other pane already renders — and the other
//  pages sit on a SHELF, untouched, histories intact. Switching pages swaps
//  which session is live; nothing is copied and no history is reset, so the op
//  count you left a page at is the op count you come back to.
//
//  ── How a page set enters ───────────────────────────────────────────────────
//
//  The same way everything else enters this playground: as wire JSON, pasted.
//  The bundle is a self-describing envelope over ordinary canonical Node
//  documents — the multi-page reading of the single-tree load the examples
//  gallery already performs (each page is decoded by the REAL strict decoder
//  and rebased into a fresh session, exactly as a loaded example is):
//
//      { "$pages": "fuaran-page-set", "version": 1,
//        "pages": [ { "name": "Home", "tree": { …canonical Node… } }, … ] }
//
//  The marker is deliberately not `$type` — that sigil is the wire format's own
//  node/op discriminator, and an envelope wearing it would invite a decoder to
//  read this file as a tree (the same reasoning as the op-log export's `$log`).
//
//  ── The cross-page move ─────────────────────────────────────────────────────
//
//  "Move this to page B" is composed entirely from shipped single-tree
//  primitives — no new op kind, no cross-tree semantics anywhere an apply
//  engine can see. The focused subtree is lifted from the active tree
//  (`Introspect.findNode` — a node IS its subtree), the packaged paste places
//  it at the end of page B's root (remapping any id that collides with one
//  already there), and an ordinary `RemoveNode` takes it off page A. Each leg
//  is one validator-gated op through the navigator's one commit gate, recorded
//  against its own tree's stream.
//
//  What makes the two legs ONE editor action is the correlation annotation:
//  both are recorded against the same actor, `Human "navigator:move:<corr>"`,
//  so the pairing lives INSIDE each stream's hash-chained record (re-attributing
//  a leg breaks its chain) rather than in view state beside it. Undoing the
//  move reads that annotation back: when both legs are still the newest applied
//  op of their trees, one click replays both trees back one op — the same
//  replay-based undo the single-tree history row uses, run twice. When either
//  tree has moved on, the paired undo honestly refuses (replay undoes from the
//  top or not at all) and the per-tree histories remain individually steppable.
//
//  ── The guard rail ──────────────────────────────────────────────────────────
//
//  Structure travels; module state does not. A subtree that READS `$state`
//  keys (`Binding.State`, found by the shipped tree-wide binding walk) will
//  land on a page whose state bag never heard of them, so the move surfaces a
//  TYPED warning naming the keys — advisory, not a refusal: the reads fall
//  back to their declared defaults, which is legal and sometimes wanted.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

module Introspect = Fuaran.UI.Ops.Introspect
module Decode = Fuaran.UI.Ops.JsonDecode
module BindingWalk = Fuaran.UI.BindingWalk

// ─── the model ───────────────────────────────────────────────────────────────

/// What the site bar has to say about the last site action. A DU rather than a
/// string because the state-reads case is a CLAIM (these keys will not travel)
/// the tests assert on by shape, not by sentence.
[<RequireQualifiedAccess>]
type Notice =
  /// Something happened; here is what.
  | Info of text: string
  /// The action was refused; the site (and every session) is unchanged.
  | Refused of message: string
  /// The move happened, AND the moved subtree reads module state — the typed
  /// guard-rail warning: state does not travel with structure, so on the
  /// destination page these reads fall back to their declared defaults.
  | MovedWithStateReads of keys: string list * toPage: string

/// The page set. The ACTIVE page's session is deliberately NOT here — it is the
/// app's live session, the single source of truth every pane renders — so the
/// shelf holds every page EXCEPT the active one and there is exactly one copy
/// of each page's history at all times.
type Site =
  {
    /// The name of the page whose session is live.
    Active: string
    /// Every page name, in bundle order (the active one included).
    Names: string list
    /// The inactive pages' sessions, by name — histories intact.
    Shelf: Map<string, Session.SessionState>
    /// Correlation ids of cross-page moves, newest first — the RECENCY order
    /// paired undo pops in. The pairing itself lives in the sessions' own
    /// attributed records (the actor annotation); this list only remembers
    /// which move was last.
    MoveStack: string list
    /// What the site bar reports about the last action.
    Notice: Notice option
  }

// ─── the bundle codec ────────────────────────────────────────────────────────

/// The bundle envelope's document-kind marker (see the module header for why it
/// is not `$type`).
[<Literal>]
let bundleMarker = "fuaran-page-set"

/// The bundle envelope's format version. Bump on shape change, so a reader can
/// reject an unrecognised document rather than mis-parse it.
[<Literal>]
let bundleVersion = 1

/// The envelope walk, as inline JS (the same leaf-string idiom `Session` uses):
/// `[name, treeJson]` pairs when the document is a well-shaped bundle, `null`
/// otherwise. Each page's tree is handed on as its own JSON text for the REAL
/// strict decoder — nothing here interprets a node.
[<Emit("""(function(text){
  try {
    var p = JSON.parse(text);
    if (!p || p.$pages !== 'fuaran-page-set' || p.version !== 1 || !Array.isArray(p.pages) || p.pages.length === 0) { return null; }
    var out = [];
    for (var i = 0; i < p.pages.length; i++) {
      var pg = p.pages[i];
      if (!pg || typeof pg.name !== 'string' || pg.name.trim() === '' || pg.tree == null) { return null; }
      out.push([pg.name.trim(), JSON.stringify(pg.tree)]);
    }
    return out;
  } catch (e) { return null; }
})($0)""")>]
let private parseBundleRaw (text: string) : (string * string) array = jsNative

let private isNullObj (o: obj) : bool = emitJsExpr o "$0 == null"

/// Parse a page-set bundle: the envelope walk above, then every page's tree
/// through the real strict decoder. All-or-nothing — one undecodable page
/// refuses the whole bundle, so a site can never load half-real.
let loadPages (bundleJson: string) : Result<(string * Node<obj>) list, string> =
  let raw = parseBundleRaw bundleJson

  if isNullObj (box raw) then
    Error(
      "not a page-set bundle — expected {\"$pages\":\""
      + bundleMarker
      + "\",\"version\":1,\"pages\":[{\"name\":…,\"tree\":…},…]} with at least one page"
    )
  else
    let names = raw |> Array.map fst

    match names |> Array.countBy id |> Array.tryFind (fun (_, n) -> n > 1) with
    | Some(dup, _) -> Error("page name '" + dup + "' appears more than once")
    | None ->
      raw
      |> Array.fold
        (fun acc (name, treeJson) ->
          acc
          |> Result.bind (fun pages ->
            match Decode.decodeNode treeJson with
            | Error e -> Error("page '" + name + "': " + e.Message)
            | Ok wire -> Ok(pages @ [ name, WireTree.reify wire ])))
        (Ok [])

/// A fresh session over one page's tree — the multi-page reading of what
/// loading a single example already does (`Session.rebase`: new base, empty op
/// record, one snapshot).
let private sessionOf (tree: Node<obj>) : Session.SessionState = Session.rebase tree Session.empty

/// A site over decoded pages: the first page is active (its session is
/// returned to become the app's live one), the rest go to the shelf.
let ofPages (pages: (string * Node<obj>) list) : Site * Session.SessionState =
  let firstName, firstTree = List.head pages

  { Active = firstName
    Names = pages |> List.map fst
    Shelf =
      pages
      |> List.tail
      |> List.map (fun (name, tree) -> name, sessionOf tree)
      |> Map.ofList
    MoveStack = []
    Notice = Some(Notice.Info(sprintf "%d pages loaded — '%s' is active" (List.length pages) firstName)) },
  sessionOf firstTree

// ─── switching ───────────────────────────────────────────────────────────────

/// Make `name` the active page: the current live session goes to the shelf
/// under the outgoing name, the named page's session comes off it — histories
/// untouched in both directions. `None` for an unknown name or the page that
/// is already active.
let switchPage (site: Site) (current: Session.SessionState) (name: string) : (Site * Session.SessionState) option =
  if name = site.Active then
    None
  else
    site.Shelf
    |> Map.tryFind name
    |> Option.map (fun next ->
      { site with
          Active = name
          Shelf = site.Shelf |> Map.remove name |> Map.add site.Active current
          Notice = None },
      next)

// ─── the guard rail ──────────────────────────────────────────────────────────

/// The distinct `$state` keys the subtree rooted at `nodeId` READS — via the
/// shipped tree-wide binding walk, so a binding position added to the language
/// is covered here with no edit to this file. Empty when the node is absent.
let stateKeys (root: Node<obj>) (nodeId: NodeId) : string list =
  match Introspect.findNode nodeId root with
  | None -> []
  | Some sub ->
    (BindingWalk.collect sub).Uses
    |> List.choose (fun use_ ->
      match use_.Use with
      | BindingWalk.BindingUse.State key -> Some key
      | _ -> None)
    |> List.distinct

// ─── the cross-page move ─────────────────────────────────────────────────────

/// The actor-id prefix that marks a log entry as a cross-page move leg; what
/// follows it is the correlation id both legs share.
[<Literal>]
let moveActorPrefix = "navigator:move:"

let private moveActor (corr: string) : Actor = Actor.Human(moveActorPrefix + corr)

/// The correlation id for a move: which node, to which page, at which point in
/// the source's history. Deterministic (the chain timestamp is fixed for the
/// same reason), and unique where it matters — among the entries that can
/// simultaneously be the newest applied op of two trees.
let private corrIdFor (nodeId: NodeId) (toPage: string) (current: Session.SessionState) : string =
  let (NodeId raw) = nodeId
  raw + ">" + toPage + "@" + string (List.length current.Log + 1)

/// Move the subtree rooted at `nodeId` from the active page's tree to the end
/// of `toPage`'s root — the paste leg first (page B refusing costs page A
/// nothing), then the remove leg, both recorded against the shared correlation
/// actor. Pure and all-or-nothing: a refusal on either leg returns `Error` and
/// no session anywhere has changed.
let movePage
  (site: Site)
  (current: Session.SessionState)
  (nodeId: NodeId)
  (toPage: string)
  : Result<Site * Session.SessionState, string> =
  match current.Tree with
  | None -> Error "there is no tree to move from"
  | Some root ->
    if toPage = site.Active then
      Error "that is the page it is already on"
    else
      match Map.tryFind toPage site.Shelf with
      | None -> Error("no page called '" + toPage + "'")
      | Some destSession ->
        match Introspect.findNode nodeId root with
        | None -> Error("no node '" + StructuralEdit.idText nodeId + "' in the tree")
        | Some sub ->
          if (Introspect.findParent nodeId root).IsNone then
            Error "the root node is the page itself — walk to something inside it"
          else
            match destSession.Tree with
            | None -> Error("page '" + toPage + "' has no tree to receive it")
            | Some destRoot ->
              let corr = corrIdFor nodeId toPage current
              let actor = moveActor corr

              let target =
                { ParentId = NodeId destRoot.Id
                  Placement = Placement.Last }

              match StructuralEdit.pasteFromAs actor destSession target sub with
              | PropertyEditor.Rejected message -> Error("page '" + toPage + "' refused it: " + message)
              | PropertyEditor.Committed destNext ->
                match StructuralEdit.removeAs actor current nodeId with
                | PropertyEditor.Rejected message -> Error message
                | PropertyEditor.Committed sourceNext ->
                  let keys = stateKeys root nodeId

                  let notice =
                    if List.isEmpty keys then
                      Notice.Info("moved #" + StructuralEdit.idText nodeId + " to '" + toPage + "'")
                    else
                      Notice.MovedWithStateReads(keys, toPage)

                  Ok(
                    { site with
                        Shelf = Map.add toPage destNext site.Shelf
                        MoveStack = corr :: site.MoveStack
                        Notice = Some notice },
                    sourceNext
                  )

// ─── paired undo ─────────────────────────────────────────────────────────────

/// The newest APPLIED entry of a session's record (the redo tail excluded — an
/// undone op is not the top of anything).
let private appliedTop (session: Session.SessionState) : Session.LogEntry option =
  session.Log |> List.truncate (List.length session.Ops) |> List.tryLast

/// A log entry's correlation id, when it is a cross-page move leg.
let private corrOf (entry: Session.LogEntry) : string option =
  match entry.Actor with
  | Human id when id.StartsWith moveActorPrefix -> Some(id.Substring moveActorPrefix.Length)
  | _ -> None

/// Every page (active included) whose NEWEST applied op is a leg of `corr`.
let private legsAtTop (site: Site) (current: Session.SessionState) (corr: string) : string list =
  (site.Active, current) :: Map.toList site.Shelf
  |> List.choose (fun (name, session) ->
    match appliedTop session |> Option.bind corrOf with
    | Some c when c = corr -> Some name
    | _ -> None)

/// Whether the LAST move can be undone as one action: both of its legs must
/// still be the newest applied op of their trees. Replay-based undo steps from
/// the top or not at all, so a page that has been edited since honestly
/// disables this — its own history row still steps everything individually.
let canUndoMove (site: Site) (current: Session.SessionState) : bool =
  match site.MoveStack with
  | corr :: _ -> legsAtTop site current corr |> List.length = 2
  | [] -> false

/// Undo the last move as ONE editor action: replay BOTH trees back one op (the
/// same `OpLog.undo` the single-tree history row uses, run once per leg —
/// page A's remove un-happens, page B's insert un-happens). `None` when the
/// pair is no longer at both tops.
let undoMove (site: Site) (current: Session.SessionState) : (Site * Session.SessionState) option =
  match site.MoveStack with
  | [] -> None
  | corr :: rest ->
    match legsAtTop site current corr with
    | legs when List.length legs = 2 ->
      let start =
        { site with
            MoveStack = rest
            Notice = Some(Notice.Info "move undone — both pages restored") },
        current

      legs
      |> List.fold
        (fun acc name ->
          acc
          |> Option.bind (fun ((siteAcc: Site), cur) ->
            if name = siteAcc.Active then
              OpLog.undo cur |> Option.map (fun next -> siteAcc, next)
            else
              siteAcc.Shelf
              |> Map.tryFind name
              |> Option.bind OpLog.undo
              |> Option.map (fun next ->
                { siteAcc with
                    Shelf = Map.add name next siteAcc.Shelf },
                cur)))
        (Some start)
    | _ -> None

// ─── the action step (what the app's update folds) ───────────────────────────

/// Everything the site view can ask of the app, as one DU — the app model
/// grows one field and one message case, and this module owns the semantics.
[<RequireQualifiedAccess>]
type Action =
  /// Load a pasted page-set bundle (replaces any current site AND the live
  /// session — a loaded page set is a new base, like a loaded example).
  | Load of bundleJson: string
  /// Make the named page active.
  | Switch of page: string
  /// Move the subtree rooted at `nodeId` from the active page to `page`.
  | Move of nodeId: string * page: string
  /// Undo the last cross-page move as one action (both legs).
  | UndoMove
  /// Clear the site bar's notice.
  | DismissNotice

type StepResult =
  {
    Site: Site option
    Session: Session.SessionState
    /// Whether `Session` differs from the input — the app propagates a changed
    /// session exactly as it propagates a navigator edit (live-drive included).
    SessionChanged: bool
  }

/// Fold one site action. Pure; a refused action changes no session, and every
/// refusal lands in the site's notice rather than being thrown or swallowed.
let step (action: Action) (site: Site option) (session: Session.SessionState) : StepResult =
  let unchanged =
    { Site = site
      Session = session
      SessionChanged = false }

  let withNotice (notice: Notice) =
    match site with
    | Some s ->
      { unchanged with
          Site = Some { s with Notice = Some notice } }
    | None -> unchanged

  match action, site with
  | Action.Load json, _ ->
    match loadPages json with
    | Ok pages ->
      let next, active = ofPages pages

      { Site = Some next
        Session = active
        SessionChanged = true }
    | Error message -> withNotice (Notice.Refused message)
  | Action.Switch name, Some s ->
    match switchPage s session name with
    | Some(next, active) ->
      { Site = Some next
        Session = active
        SessionChanged = true }
    | None -> unchanged
  | Action.Move(nodeId, page), Some s ->
    match movePage s session (NodeId nodeId) page with
    | Ok(next, active) ->
      { Site = Some next
        Session = active
        SessionChanged = true }
    | Error message -> withNotice (Notice.Refused message)
  | Action.UndoMove, Some s ->
    match undoMove s session with
    | Some(next, active) ->
      { Site = Some next
        Session = active
        SessionChanged = true }
    | None -> unchanged
  | Action.DismissNotice, Some s ->
    { unchanged with
        Site = Some { s with Notice = None } }
  | (Action.Switch _ | Action.Move _ | Action.UndoMove | Action.DismissNotice), None -> unchanged

// ─── the view ────────────────────────────────────────────────────────────────

/// A notice as a sentence (shared by the bar and the flat test surface, so what
/// the tests pin is what the person reads).
let noticeText (notice: Notice) : string =
  match notice with
  | Notice.Info text -> text
  | Notice.Refused message -> message
  | Notice.MovedWithStateReads(keys, toPage) ->
    "Moved to '"
    + toPage
    + "' — note: this subtree reads module state ("
    + String.concat ", " keys
    + "). State does not travel with structure, so on '"
    + toPage
    + "' those reads fall back to their declared defaults."

let private noticeRow (notice: Notice) (dispatch: Action -> unit) : ReactElement =
  let className, role =
    match notice with
    | Notice.Info _ -> "fl-nav-count", "status"
    | Notice.Refused _ -> "fl-nav-field-error", "alert"
    | Notice.MovedWithStateReads _ -> "fl-nav-held", "status"

  Html.p
    [ prop.className (className + " fl-site-notice")
      prop.role role
      prop.children
        [ Html.span [ prop.text (noticeText notice) ]
          Html.button
            [ prop.className "fl-btn ghost"
              prop.text "✕"
              prop.title "Dismiss"
              prop.onClick (fun _ -> dispatch Action.DismissNotice) ] ] ]

/// The site bar: the page tabs + paired undo when a set is loaded, and the
/// bundle paste box either way (loading a set is how a site starts).
[<ReactComponent>]
let SiteBar (site: Site option) (session: Session.SessionState) (dispatch: Action -> unit) : ReactElement =
  let draft, setDraft = React.useState ""
  let loadError, setLoadError = React.useState (None: string option)

  let doLoad () =
    match loadPages draft with
    | Error message -> setLoadError (Some message)
    | Ok _ ->
      setLoadError None
      setDraft ""
      dispatch (Action.Load draft)

  let loadBox =
    Html.details
      [ prop.className "fl-site-load"
        prop.children
          [ Html.summary
              [ prop.text (
                  match site with
                  | None -> "Work across pages — load a page set…"
                  | Some _ -> "Load a different page set…"
                ) ]
            Html.p
              [ prop.className "fl-nav-field-why"
                prop.text (
                  "Paste a bundle of named trees: {\"$pages\":\""
                  + bundleMarker
                  + "\",\"version\":1,\"pages\":[{\"name\":\"Home\",\"tree\":{…}},…]}. "
                  + "Each tree is ordinary canonical wire JSON; each page keeps its own op history."
                ) ]
            Html.textarea
              [ prop.className "fl-nav-field-input fl-site-paste"
                prop.ariaLabel "Page-set bundle JSON"
                prop.rows 4
                prop.value draft
                prop.onChange (fun (v: string) -> setDraft v) ]
            Html.button
              [ prop.className "fl-btn"
                prop.text "Load page set"
                prop.disabled (draft.Trim() = "")
                prop.onClick (fun _ -> doLoad ()) ]
            (match loadError with
             | Some message -> Html.p [ prop.className "fl-nav-field-error"; prop.role "alert"; prop.text message ]
             | None -> Html.none) ] ]

  match site with
  | None -> Html.div [ prop.className "fl-site"; prop.children [ loadBox ] ]
  | Some s ->
    let tabs =
      Html.div
        [ prop.className "fl-nav-controls fl-site-pages"
          prop.role "tablist"
          prop.ariaLabel "Site pages"
          prop.children
            [ for name in s.Names do
                // Bound to a name first: `(name = s.Active)` passed inline
                // parses as a NAMED ARGUMENT called `name`, not an equality.
                let isActive = name = s.Active

                Html.button
                  [ prop.key name
                    prop.className (if isActive then "fl-btn" else "fl-btn ghost")
                    prop.role "tab"
                    prop.ariaSelected isActive
                    prop.text name
                    prop.title ("Switch to '" + name + "' — its own op history is preserved")
                    prop.onClick (fun _ ->
                      if name <> s.Active then
                        dispatch (Action.Switch name)) ]
              Html.button
                [ prop.className "fl-btn ghost"
                  prop.text "↶ Undo move"
                  prop.disabled (not (canUndoMove s session))
                  prop.title
                    "Undo the last cross-page move as one action — both pages step back one op. Available while the move is still the newest op on both pages."
                  prop.onClick (fun _ -> dispatch Action.UndoMove) ] ] ]

    Html.div
      [ prop.className "fl-site"
        prop.children
          [ tabs
            (match s.Notice with
             | Some notice -> noticeRow notice dispatch
             | None -> Html.none)
            loadBox ] ]

/// The "Move to page…" row for the structural panel: pick a destination page
/// for the focused subtree. Advises about `$state` reads BEFORE the click (the
/// typed notice repeats it after). Rendered only when a site with somewhere to
/// move to is loaded and the focus is not the root.
[<ReactComponent>]
let MovePicker
  (site: Site option)
  (session: Session.SessionState)
  (focused: NodeId option)
  (dispatch: Action -> unit)
  : ReactElement =
  let sel, setSel = React.useState ""

  let choices =
    match site with
    | Some s -> s.Names |> List.filter (fun name -> name <> s.Active)
    | None -> []

  let movable =
    match session.Tree, focused with
    | Some root, Some id -> (Introspect.findParent id root).IsSome
    | _ -> false

  match site, choices with
  | Some _, _ :: _ ->
    let chosen = if List.contains sel choices then sel else List.head choices

    let advisory =
      match session.Tree, focused with
      | Some root, Some id ->
        match stateKeys root id with
        | [] -> Html.none
        | keys ->
          Html.span
            [ prop.className "fl-nav-field-why"
              prop.text (
                "reads module state ("
                + String.concat ", " keys
                + ") — state stays with this page"
              ) ]
      | _ -> Html.none

    Html.div
      [ prop.className "fl-nav-controls fl-site-move"
        prop.children
          [ Html.select
              [ prop.className "fl-nav-field-input"
                prop.ariaLabel "Destination page"
                prop.value chosen
                prop.onChange (fun (v: string) -> setSel v)
                prop.children [ for name in choices -> Html.option [ prop.value name; prop.text name ] ] ]
            Html.button
              [ prop.className "fl-btn ghost"
                prop.text "⇥ Move to page"
                prop.disabled (not movable)
                prop.title
                  "Lift this subtree off this page and append it to the destination page's root — two ordinary ops, one on each page, undoable as one action"
                prop.onClick (fun _ ->
                  match focused with
                  | Some id -> dispatch (Action.Move(StructuralEdit.idText id, chosen))
                  | None -> ()) ]
            advisory ] ]
  | _ -> Html.none

// ─── flat diagnostic surface (cross-boundary friendly) ───────────────────────
//
// F# records, options and DUs are awkward to assert on from the JS side of the
// Fable boundary, so — exactly as the cursor / op-log / structural-edit helpers
// do — the same logic is projected to plain strings, arrays and flat records.
// `Site` and `SessionState` travel opaquely (the tests thread them back in);
// everything asserted on is flat.

let private noSite: Site =
  { Active = ""
    Names = []
    Shelf = Map.empty
    MoveStack = []
    Notice = None }

/// `loadPages` + `ofPages`, flattened: `Ok` false leaves `Site`/`Session` as
/// inert placeholders a test must not read.
let loadResult
  (bundleJson: string)
  : {| Ok: bool
       Error: string
       Site: Site
       Session: Session.SessionState |}
  =
  match loadPages bundleJson with
  | Ok pages ->
    let site, active = ofPages pages

    {| Ok = true
       Error = ""
       Site = site
       Session = active |}
  | Error message ->
    {| Ok = false
       Error = message
       Site = noSite
       Session = Session.empty |}

/// Every page name, in order.
let pageNames (site: Site) : string array = Array.ofList site.Names

/// The active page's name.
let activePage (site: Site) : string = site.Active

/// A SHELVED page's session (the active page's session is the live one the
/// caller already holds). `Session.empty` for an unknown/active name.
let shelfSessionOf (site: Site) (name: string) : Session.SessionState =
  site.Shelf |> Map.tryFind name |> Option.defaultValue Session.empty

/// `switchPage`, flattened. `Ok` false returns the inputs unchanged.
let switchResult
  (site: Site)
  (session: Session.SessionState)
  (name: string)
  : {| Ok: bool
       Site: Site
       Session: Session.SessionState |}
  =
  match switchPage site session name with
  | Some(next, active) ->
    {| Ok = true
       Site = next
       Session = active |}
  | None ->
    {| Ok = false
       Site = site
       Session = session |}

/// `movePage`, flattened, with the guard rail's keys surfaced as a plain array
/// (empty when the moved subtree reads no module state).
let moveResult
  (site: Site)
  (session: Session.SessionState)
  (nodeId: string)
  (page: string)
  : {| Ok: bool
       Error: string
       WarnKeys: string array
       Site: Site
       Session: Session.SessionState |}
  =
  match movePage site session (NodeId nodeId) page with
  | Ok(next, active) ->
    let warnKeys =
      match next.Notice with
      | Some(Notice.MovedWithStateReads(keys, _)) -> Array.ofList keys
      | _ -> [||]

    {| Ok = true
       Error = ""
       WarnKeys = warnKeys
       Site = next
       Session = active |}
  | Error message ->
    {| Ok = false
       Error = message
       WarnKeys = [||]
       // The same posture as `step`: a refusal lands in the notice, and no
       // session anywhere has changed.
       Site =
        { site with
            Notice = Some(Notice.Refused message) }
       Session = session |}

/// `undoMove`, flattened. `Ok` false returns the inputs unchanged.
let undoMoveResult
  (site: Site)
  (session: Session.SessionState)
  : {| Ok: bool
       Site: Site
       Session: Session.SessionState |}
  =
  match undoMove site session with
  | Some(next, active) ->
    {| Ok = true
       Site = next
       Session = active |}
  | None ->
    {| Ok = false
       Site = site
       Session = session |}

/// The notice's kind, as a plain tag (`""` when none) — the typed warning made
/// assertable by shape.
let noticeKind (site: Site) : string =
  match site.Notice with
  | None -> ""
  | Some(Notice.Info _) -> "info"
  | Some(Notice.Refused _) -> "refused"
  | Some(Notice.MovedWithStateReads _) -> "state-warning"

/// The notice's sentence (`""` when none).
let noticeLine (site: Site) : string =
  match site.Notice with
  | None -> ""
  | Some notice -> noticeText notice

/// The pre-move advisory, addressed by plain string: the `$state` keys the
/// subtree at `nodeId` reads.
let stateKeysAt (session: Session.SessionState) (nodeId: string) : string array =
  match session.Tree with
  | None -> [||]
  | Some root -> stateKeys root (NodeId nodeId) |> Array.ofList
