module Fuaran.Showcase.Navigator

// ============================================================================
//  The Navigator – edit a running app through its own wire format.
//  Pillar: "the machine can see the UI".
//
//  Every other page here shows you that a Fuaran app IS data. This one lets you
//  move around inside one and change it, and shows you the change as the bytes
//  it actually is. Walk the tree with a cursor, retitle a button, resize a
//  heading, undo – and beside each action, the `TreeOp` it emitted, in canonical
//  wire JSON. What you did, as data.
//
//  Three things are worth saying plainly, because they are the whole point:
//
//    1. **A walk emits nothing.** The cursor is a PROJECTION over the tree – a
//       path of ids, re-resolved against the current tree on every render. It
//       holds no index, no ordinal, and no captured node, so it survives an edit
//       that reshapes the tree around it. Moving the cursor is not a mutation
//       and there is no op to show for it; the page says so rather than
//       inventing one.
//    2. **An edit is an op, and only an op.** Nothing on this page edits a tree
//       by hand. Each edit builds a `TreeOp`, hands it to the SHIPPED apply
//       engine (`Fuaran.UI.Ops.Apply`), and takes the result – so the bytes you
//       are shown are the bytes that moved.
//    3. **Undo is not an op either.** The log is the truth; undo REPLAYS the log
//       from the base tree up to an earlier position. There is no inverse
//       operation to compose and nothing to get wrong – the same reason the
//       playground's op log can scrub.
//
//  Honest scope: this page composes the shipped substrate – the traversal
//  surface (`Introspect`), the apply engine, the canonical encoder, and the
//  source projector – on a CANNED tree, with no key and no session. The
//  playground's Navigator adds what genuinely needs a live session: a
//  schema-DERIVED property panel over every field of every kind, the structural
//  insert/move/remove palette, the accessibility walk, and re-prompting from the
//  cursor. Those are one click away at the bottom of this page; nothing here
//  imitates them.
//
//  The WALK, though, is not imitated either – it is the same code. `Cursor` is
//  the session-free module the playground's Navigator tab also drives, linked
//  into this project because it depends on the typed tree and the traversal
//  surface and on nothing else. This page carried a hand-mirrored copy of those
//  semantics until Phase 744; there is now one definition, so the two entries
//  cannot disagree about what "next" means.
// ============================================================================

open Fable.Core
open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops.Types

module Introspect = Fuaran.UI.Ops.Introspect
module Apply = Fuaran.UI.Ops.Apply
module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module Projection = Fuaran.Live.Projection
module Cursor = Fuaran.Live.Cursor

// ─── the canned tree (19 nodes across the common kinds) ──────────────────────

let private lit (s: string) : TextSource = TextSource.Literal s

let private kpi (id: string) (label: string) (value: float) (fmt: CellFormat) (tone: ToneVariant) : Node<unit> =
  Fuaran.metric
    id
    { Defaults.metric with
        Label = lit label
        Value = Binding.Static(Some value)
        Format = fmt
        Tone = tone }

let private detailCard (id: string) (heading: string) (body: string) : Node<unit> =
  Fuaran.card
    id
    { Defaults.card with
        Heading = Some(lit heading)
        Children = [ Fuaran.markdown (id + "-body") body ] }

/// The app the cursor walks. Deliberately ordinary — a release-readiness board
/// of the sort anyone has seen — so that what is remarkable is the WALK, not the
/// app. Eighteen nodes across seven kinds, deep enough that the cursor's
/// parent/first-child moves have somewhere to go.
let private baseTree: Node<unit> =
  Fuaran.box
    "nv-root"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 12)
      Role = BoxRole.Dashboard
      Heading = None
      Children =
        [ Fuaran.heading
            "nv-title"
            { Defaults.heading with
                Level = 3
                Text = lit "Release readiness — build 4120" }
          Fuaran.markdown
            "nv-intro"
            "Cut from `main` this morning. Sign-off needs coverage above 80% and no open blockers."
          Fuaran.box
            "nv-kpis"
            { Layout = LayoutMode.Grid(3, None, Some 10)
              Role = BoxRole.Group
              Heading = None
              Children =
                [ kpi "nv-kpi-coverage" "Coverage" 0.842 (CellFormat.Percent(Some 1)) ToneVariant.Success
                  kpi "nv-kpi-open" "Open blockers" 2.0 CellFormat.None ToneVariant.Warning
                  kpi "nv-kpi-latency" "p95 latency (ms)" 184.0 CellFormat.None ToneVariant.Default ] }
          Fuaran.callout
            "nv-risk"
            { Defaults.callout with
                Tone = ToneVariant.Warning
                Heading = Some(lit "Two blockers still open")
                Body = lit "Both sit in the importer. Neither is a regression; both are new-feature gaps." }
          Fuaran.box
            "nv-detail"
            { Layout = LayoutMode.Flex(Orientation.Horizontal, true, Some 10)
              Role = BoxRole.Group
              Heading = None
              Children =
                [ detailCard "nv-card-tests" "Tests" "1,284 passing · 0 failing · 3 skipped."
                  detailCard "nv-card-docs" "Docs" "Changelog written. Migration note still a stub." ] }
          Fuaran.box
            "nv-facts"
            { Layout = LayoutMode.Flex(Orientation.Horizontal, true, Some 10)
              Role = BoxRole.Group
              Heading = None
              Children =
                [ Fuaran.fact "nv-fact-branch" "Branch" "release/4120"
                  Fuaran.fact "nv-fact-window" "Window" "Thu 14:00–16:00 UTC" ] }
          Fuaran.progress
            "nv-progress"
            { Defaults.progress with
                Fraction = Binding.Static(Some 0.72)
                Label = Some(lit "Sign-off checklist")
                Tone = ToneVariant.Brand }
          Fuaran.button
            "nv-cta"
            { Defaults.button with
                Label = lit "Request sign-off"
                Variant = ButtonVariant.Primary } ] }

// ─── the cursor ──────────────────────────────────────────────────────────────
//
// There is nothing here, and that is the point. The cursor — the path-of-ids
// state, the DFS next/prev, parent/first-child, the end-stop rule and the
// re-resolution fallback — is `Fuaran.Live.Cursor`, aliased as `Cursor` above
// and linked into this project. This page used to restate all of it; see that
// module's header for the semantics, which are now written down exactly once.

// ─── the op log (the log is the truth; undo replays a prefix) ────────────────

/// The two edits the guided sequence performs, as the ops they are. Both are
/// `UpdateProp` — the same op the playground's property panel emits when you
/// commit a field — addressed by id and carrying a canonical wire value.
let private labelOp: TreeOp<unit> =
  TreeOp.UpdateProp(NodeId "nv-cta", "Label", PropValue.Wire(JStr "Ship build 4120"))

let private sizeOp: TreeOp<unit> =
  TreeOp.UpdateProp(NodeId "nv-title", "Level", PropValue.Wire(JInt 1))

/// Fold the SHIPPED apply engine over a prefix of the log. This is the whole of
/// undo: there is no inverse op to compose, so there is nothing to get wrong.
let private treeAfter (ops: TreeOp<unit> list) : Node<unit> =
  (baseTree, ops)
  ||> List.fold (fun tree op ->
    match Apply.apply op tree with
    | Ok next -> next
    | Error _ -> tree)

// ─── the guided sequence ─────────────────────────────────────────────────────

/// What a step DOES to the log, and what it therefore has to show. `Move` is the
/// honest odd one out: it changes the cursor and emits nothing.
type private StepAction =
  | Move of (Node<unit> -> Cursor.NavCursor -> Cursor.NavCursor)
  | Emit of TreeOp<unit>
  | Undo
  | Project

type private Step =
  {
    Title: string
    Say: string
    Action: StepAction
    /// The op count the log sits at once this step has run — so the page can
    /// derive the tree for any step without replaying the UI.
    LogAt: int
    /// The id the cursor should be on when the step runs.
    At: string
  }

let private steps: Step list =
  [ { Title = "Walk"
      Say =
        "Step the cursor through the tree in DFS pre-order. The cursor is a path of ids re-resolved against the current tree on every render — it holds no index and no captured node. Nothing is emitted, because nothing changed."
      Action = Move Cursor.next
      LogAt = 0
      At = "nv-root" }
    { Title = "Edit a label"
      Say =
        "Retitle the call-to-action. The panel does not reach into the tree — it builds an UpdateProp addressed by id, carrying the new value in canonical wire form, and hands it to the apply engine."
      Action = Emit labelOp
      LogAt = 1
      At = "nv-cta" }
    { Title = "Edit a size"
      Say =
        "Promote the heading from level 3 to level 1. Same op, different field: the value is an integer this time, coerced by the field's own spec rather than by anything this page knows."
      Action = Emit sizeOp
      LogAt = 2
      At = "nv-title" }
    { Title = "Undo"
      Say =
        "Drop back one position in the log. Undo is not an op — the tree is REPLAYED from the base up to the earlier position, so there is no inverse to compose and nothing to get wrong."
      Action = Undo
      LogAt = 1
      At = "nv-title" }
    { Title = "Projection sync"
      Say =
        "The same tree, projected as source in three host languages at once. Move the cursor and every pane follows it — the projections are re-derived from the current tree, and the focused node is highlighted in each."
      Action = Project
      LogAt = 1
      At = "nv-cta" } ]

let private stepCount = List.length steps

/// The whole log, in order. Undo/redo is a position in it, never an inverse op.
let private fullLog = [ labelOp; sizeOp ]

let private maxOps = List.length fullLog

/// The log truncated to `n` applied ops — the only thing the tree is derived
/// from, so a position is all the page ever has to hold.
let private logAfter (n: int) : TreeOp<unit> list =
  fullLog |> List.truncate (max 0 (min maxOps n))

// ─── highlight the focused node in the live preview ──────────────────────────

/// Outline the rendered element the cursor is on, via the `data-fuaran-node-id`
/// attribute the renderer already emits. Scoped to this page's preview so the
/// wire-JSON panel is never mistaken for the tree. An id with no rendered
/// element is normal, not an error.
[<Emit("""(function(id){
  try {
    var scope = document.querySelector('.nv-preview');
    if (!scope) { return false; }
    var all = scope.querySelectorAll('[data-fuaran-node-id]');
    for (var i = 0; i < all.length; i++) { all[i].classList.remove('nv-focus'); }
    if (!id) { return false; }
    var esc = String(id).split('\\').join('\\\\').split('"').join('\\"');
    var el = scope.querySelector('[data-fuaran-node-id="' + esc + '"]');
    if (!el) { return false; }
    el.classList.add('nv-focus');
    if (el.scrollIntoView) { el.scrollIntoView({ block: 'nearest', inline: 'nearest' }); }
    return true;
  } catch (e) { return false; }
})($0)""")>]
let private highlight (nodeId: string) : bool = jsNative

// ─── the three source projections ────────────────────────────────────────────

/// The three the feature is FOR — "walk the tree, watch the source in three
/// languages". The Rosetta page projects all nine; three is the number that
/// still reads as code beside a walk rather than as a wall.
let private shownTargets =
  [ Projection.Target.TypeScript, "TypeScript"
    Projection.Target.Python, "Python"
    Projection.Target.FSharp, "F#" ]

let private projectionPane (target: Projection.Target) (label: string) (wire: string) (idPath: string list) =
  let projected = Projection.projectSpans target wire

  let head, hit, tail =
    match Projection.spanForPath projected idPath with
    | Some span ->
      projected.Text.Substring(0, span.Start),
      projected.Text.Substring(span.Start, span.Length),
      projected.Text.Substring(span.Start + span.Length)
    | None -> projected.Text, "", ""

  Html.div
    [ prop.className "nv-proj"
      prop.children
        [ Html.div [ prop.className "nv-proj-lang"; prop.text label ]
          Html.pre
            [ prop.className "nv-proj-code"
              prop.children
                [ Html.text head
                  if hit <> "" then
                    Html.mark [ prop.className "nv-proj-hit"; prop.text hit ]
                  Html.text tail ] ] ] ]

// ─── the page ────────────────────────────────────────────────────────────────

let private renderTree (n: Node<unit>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

[<ReactComponent>]
let private NavigatorView () : ReactElement =
  // `done_` is how many guided steps have run (0 = nothing yet); free play
  // begins once every step has. `logPos` is how many ops of the log are applied,
  // and it is held SEPARATELY on purpose: the guided sequence's positions are
  // not monotonic (it applies two ops then undoes one), so stepping the rail
  // backwards would not undo the log. Free play scrubs `logPos` directly.
  let done_, setDone = React.useState 0
  let logPos, setLogPos = React.useState 0
  let cursor, setCursor = React.useState (Cursor.atRoot baseTree)

  let ops = logAfter logPos
  let tree = treeAfter ops
  let live = Cursor.reresolve tree (Some cursor)

  let focus =
    Cursor.focusedId live |> Option.map Cursor.idText |> Option.defaultValue tree.Id

  let idx, total = Cursor.position tree live
  let freePlay = done_ >= stepCount

  // Keep the in-place outline on the node the cursor is on. Re-run whenever the
  // cursor or the tree moves — the rendered DOM is what carries the ids.
  React.useEffect ((fun () -> highlight focus |> ignore), [| box focus; box (List.length ops) |])

  let move (f: Node<unit> -> Cursor.NavCursor -> Cursor.NavCursor) : unit = setCursor (f tree live)

  let runStep () : unit =
    if done_ < stepCount then
      let s = steps[done_]

      let next =
        match s.Action with
        | Move f -> f tree live
        | _ -> Cursor.jumpToText tree live s.At

      setCursor next
      setLogPos s.LogAt
      setDone (done_ + 1)

  let reset () : unit =
    setDone 0
    setLogPos 0
    setCursor (Cursor.atRoot baseTree)

  // ── what the current (or last-run) step emitted ──
  let lastStep = if done_ = 0 then None else Some steps[done_ - 1]

  let emittedPanel =
    let body =
      match lastStep with
      | None ->
        Html.p
          [ prop.className "nv-emit-none"
            prop.text "Nothing yet — run the first step. The cursor starts on the root." ]
      | Some s ->
        match s.Action with
        | Move _ ->
          Html.p
            [ prop.className "nv-emit-none"
              prop.text
                "No op. A walk changes where you are looking, not what is there — the cursor is a projection over the tree, so there is nothing to emit and this panel says so rather than inventing something." ]
        | Undo ->
          Html.p
            [ prop.className "nv-emit-none"
              prop.text
                "No op. Undo replays the log from the base tree to the earlier position; the dropped op is simply not applied. The log is the truth, so undo needs no inverse." ]
        | Project ->
          Html.p
            [ prop.className "nv-emit-none"
              prop.text "No op. Projecting reads the tree; it never writes to it." ]
        | Emit op ->
          Html.pre
            [ prop.className "nv-emit-code"
              prop.children [ Html.code [ prop.text (CJson.encodeOp op) ] ] ]

    Html.div
      [ prop.className "nv-emit"
        prop.children
          [ Html.div [ prop.className "nv-emit-head"; prop.text "What you did, as data" ]
            body ] ]

  // ── the guided rail ──
  let rail =
    Html.ol
      [ prop.className "nv-rail"
        prop.children
          [ for i in 0 .. stepCount - 1 ->
              let s = steps[i]

              let state =
                if i < done_ then "nv-step nv-step-done"
                elif i = done_ then "nv-step nv-step-now"
                else "nv-step"

              Html.li
                [ prop.className state
                  prop.children
                    [ Html.div [ prop.className "nv-step-title"; prop.text (sprintf "%d. %s" (i + 1) s.Title) ]
                      Html.p [ prop.className "nv-step-say"; prop.text s.Say ] ] ] ] ]

  let controls =
    Html.div
      [ prop.className "nv-controls"
        prop.children
          [ if not freePlay then
              Html.button
                [ prop.className "nv-btn nv-btn-primary"
                  prop.onClick (fun _ -> runStep ())
                  prop.text (sprintf "Run step %d — %s" (done_ + 1) steps[done_].Title) ]
            else
              Html.span [ prop.className "nv-freeplay"; prop.text "Free play — the tree is yours." ]
            Html.button
              [ prop.className "nv-btn"
                prop.onClick (fun _ -> reset ())
                prop.text "Start over" ] ] ]

  // ── free-play controls: the walk, the two edits, undo ──
  let freeControls =
    Html.div
      [ prop.className "nv-free"
        prop.children
          [ Html.div
              [ prop.className "nv-free-group"
                prop.children
                  [ Html.span [ prop.className "nv-free-label"; prop.text "Walk" ]
                    Html.button
                      [ prop.className "nv-btn"
                        prop.onClick (fun _ -> move Cursor.prev)
                        prop.text "◀ prev" ]
                    Html.button
                      [ prop.className "nv-btn"
                        prop.onClick (fun _ -> move Cursor.next)
                        prop.text "next ▶" ]
                    Html.button
                      [ prop.className "nv-btn"
                        prop.onClick (fun _ -> move Cursor.parent)
                        prop.text "▲ parent" ]
                    Html.button
                      [ prop.className "nv-btn"
                        prop.onClick (fun _ -> move Cursor.firstChild)
                        prop.text "▼ first child" ] ] ]
            Html.div
              [ prop.className "nv-free-group"
                prop.children
                  [ Html.span [ prop.className "nv-free-label"; prop.text "Log" ]
                    Html.button
                      [ prop.className "nv-btn"
                        prop.disabled (logPos <= 0)
                        prop.onClick (fun _ -> setLogPos (max 0 (logPos - 1)))
                        prop.text "← undo" ]
                    Html.button
                      [ prop.className "nv-btn"
                        prop.disabled (logPos >= maxOps)
                        prop.onClick (fun _ -> setLogPos (min maxOps (logPos + 1)))
                        prop.text "redo →" ]
                    Html.span
                      [ prop.className "nv-free-count"
                        prop.text (
                          sprintf "%d op%s applied" (List.length ops) (if List.length ops = 1 then "" else "s")
                        ) ] ] ] ] ]

  // ── breadcrumb + position readout ──
  let breadcrumb =
    Html.div
      [ prop.className "nv-crumbs"
        // Concatenated rather than a comprehension with a trailing element: an
        // `Html.span` sitting after a `for … ->` in the same list literal parses
        // without complaint and renders nothing (caught in the browser, not by
        // the compiler).
        prop.children (
          [ for id in live.Path ->
              let text = Cursor.idText id

              Html.button
                [ prop.className (if text = focus then "nv-crumb nv-crumb-on" else "nv-crumb")
                  prop.onClick (fun _ -> setCursor (Cursor.jumpTo tree live id))
                  prop.text text ] ]
          @ [ Html.span [ prop.className "nv-pos"; prop.text (sprintf "node %d of %d" idx total) ] ]
        ) ]

  let nodePanel =
    let json =
      Cursor.focusedNode tree live
      |> Option.map CJson.encodeNode
      |> Option.defaultValue "{}"

    Html.div
      [ prop.className "nv-node"
        prop.children
          [ Html.div [ prop.className "nv-node-head"; prop.text "The focused node, as wire JSON" ]
            Html.pre
              [ prop.className "nv-node-code"
                prop.children [ Html.code [ prop.text json ] ] ] ] ]

  let projections =
    let wire = CJson.encodeNode tree

    Html.div
      [ prop.className "nv-projs"
        prop.children
          [ Html.div
              [ prop.className "nv-projs-head"
                prop.text "The same tree as source, in three host languages — following the cursor" ]
            Html.div
              [ prop.className "nv-projs-row"
                prop.children [ for t, label in shownTargets -> projectionPane t label wire (Cursor.pathText live) ] ] ] ]

  // `StepAction` carries a function in `Move`, so it has no structural equality —
  // ask by pattern, not by `=`.
  let isProject (s: Step) =
    match s.Action with
    | Project -> true
    | _ -> false

  let showProjections = freePlay || (lastStep |> Option.exists isProject)

  let handoff =
    Html.div
      [ prop.className "nv-handoff"
        prop.children
          [ Html.h2 [ prop.text "Now do it to an app you just made" ]
            Html.p
              [ prop.text
                  "This tree was canned so the page needs no key. The playground's Navigator is the same walk over an app a model just emitted for you — and it adds what genuinely needs a live session: a property panel DERIVED from the schema (every field of every kind, validator-gated on commit), a structural palette that inserts, moves and removes nodes, an accessibility walk, and re-prompting from wherever the cursor is." ]
            Html.a
              [ prop.className "nv-door"
                prop.href (Pillars.playgroundOrigin + "#navigator")
                prop.text "Open the Navigator in the playground →" ] ] ]

  let honesty =
    Html.details
      [ prop.className "nv-honesty"
        prop.children
          [ Html.summary [ prop.text "What is real here" ]
            Html.div
              [ prop.children
                  [ Html.ul
                      [ prop.children
                          [ Html.li
                              [ prop.text
                                  "The cursor, the two edits, undo and the projections all run the shipped substrate compiled into this page: the traversal surface for the walk, the apply engine for every edit, the canonical encoder for every byte shown, and the source projector for the three panes." ]
                            Html.li
                              [ prop.text
                                  "Nothing here edits a tree by hand. Both edits are UpdateProp ops addressed by id; the values are coerced by each field's own spec, not by this page." ]
                            Html.li
                              [ prop.text
                                  "Undo replays the log from the base tree rather than applying an inverse — which is why it cannot drift from the log it is scrubbing." ]
                            Html.li
                              [ prop.text
                                  "The tree is canned and the edit set is fixed at two, because this page takes no key and calls nothing. The schema-derived property panel over every field, the structural insert/move/remove palette and the accessibility walk are the playground's, and are not reproduced here." ]
                            Html.li
                              [ prop.children
                                  [ Html.text
                                      "Walking a structure a machine can read, and changing it by naming ids rather than pointing at pixels, is the "
                                    Html.a [ prop.href "#/pillar/machine"; prop.text "machine-can-see-the-UI" ]
                                    Html.text " story at its most direct." ] ] ] ] ] ] ] ]

  Html.div
    [ prop.className "nv-page"
      prop.children
        [ Html.h1 [ prop.className "nv-title"; prop.text "The Navigator" ]
          Html.p
            [ prop.className "nv-lede"
              prop.text
                "Edit a running app through its own wire format. Walk it with a cursor, retitle a button, resize a heading, undo — and watch each action turn into the operation it actually is, in canonical bytes. No key, no server: the tree is right here." ]
          rail
          controls
          Html.div
            [ prop.className "nv-stage"
              prop.children
                [ Html.div
                    [ prop.className "nv-left"
                      prop.children
                        [ breadcrumb
                          Html.div [ prop.className "nv-preview"; prop.children [ renderTree tree ] ]
                          if freePlay then
                            freeControls ] ]
                  Html.div [ prop.className "nv-right"; prop.children [ emittedPanel; nodePanel ] ] ] ]
          if showProjections then
            projections
          handoff
          honesty ] ]

let page: ReactElement = NavigatorView()
