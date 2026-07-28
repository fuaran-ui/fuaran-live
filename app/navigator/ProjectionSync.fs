module Fuaran.Live.ProjectionSync

// ============================================================================
//  Navigator ⇄ source-projection sync (Phase 714).
//
//  Walk the tree on the left; watch the corresponding construct light up in
//  three authoring languages at once on the right. That is the whole feature,
//  and it is one no other UI system can show: the same node, simultaneously, as
//  TypeScript, Python and F# source — because in Fuaran the tree IS the
//  artefact, and each language is a projection of it rather than the thing
//  itself.
//
//  Two things make it honest rather than decorative:
//
//   • **One projector, not two.** The panes call `Projection.projectSpans`, the
//     same walk `Projection.projectTo` runs for the Output box; the span map
//     falls out of the walk itself (invisible sentinels the generators emit and
//     the entry points strip). There is no second, "highlight-aware" projector
//     that could drift from the real one — the text a pane shows is byte-for-
//     byte the text the Output box shows.
//
//   • **Nearest-enclosing resolution.** A language does not project every node:
//     the illustrative walkers fold a node in a `state` slot into its parent's
//     construct, and no source-level range corresponds to it. Rather than
//     showing nothing, the pane highlights the closest ANCESTOR the language
//     does project, and says so. "Nothing is highlighted" and "this construct
//     contains your node" are different facts and are reported differently.
//
//  The cursor arrives by SUBSCRIPTION (`Navigator.subscribeCursor`), not by
//  prop: the Navigator owns the walk, this pane only watches it. Everything
//  else is derived from the session tree on each render, so an applied op —
//  from the property panel, a model re-emission, a replayed permalink — re-runs
//  the projections and lands the highlight on the focused node's NEW span with
//  no edit path of its own. Scrolling uses `block: 'nearest'`, so a pane whose
//  highlight is already visible does not move at all; scroll context survives
//  the edit.
// ============================================================================

open Fable.Core
open Feliz
open Fuaran.UI.Types

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── which projections are on screen ─────────────────────────────────────────

/// The three the feature is FOR — "step through the UI, watch the code in three
/// languages". Every other target the Output box can project is one click away;
/// none of them is on by default, because four panes of source beside a tree
/// walk is a wall, not a demo.
let private defaultShown =
  [ Projection.Target.TypeScript
    Projection.Target.Python
    Projection.Target.FSharp ]

// ─── keeping the highlight in view ───────────────────────────────────────────

/// Scroll every pane's highlight into view within its own scroll box. `nearest`
/// on both axes is load-bearing: a highlight already on screen does not move,
/// which is what "re-derive the projections without losing scroll context"
/// means in practice. No smooth behaviour — an edit should land, not animate.
[<Emit("""(function(){
  try {
    var hits = document.querySelectorAll('.fl-ps-hit');
    for (var i = 0; i < hits.length; i++) {
      if (hits[i].scrollIntoView) { hits[i].scrollIntoView({ block: 'nearest', inline: 'nearest' }); }
    }
    return hits.length;
  } catch (e) { return 0; }
})()""")>]
let private scrollHitsIntoView () : int = jsNative

// ─── one language pane ───────────────────────────────────────────────────────

let private paneFor (target: Projection.Target) (label: string) (wire: string) (idPath: string list) : ReactElement =
  let projected = Projection.projectSpans target wire
  let focused = List.tryLast idPath
  let span = Projection.spanForPath projected idPath

  let exact =
    match span, focused with
    | Some s, Some f -> s.NodeId = f
    | _ -> false

  let status =
    match span with
    | None -> "not projected in this language"
    | Some s ->
      let first, last = Projection.lineRange projected.Text s

      let lines =
        if first = last then
          sprintf "line %d" first
        else
          sprintf "lines %d–%d" first last

      if exact then
        lines
      else
        sprintf "%s — enclosing #%s" lines s.NodeId

  let code =
    match span with
    | None -> [ Html.text projected.Text ]
    | Some s ->
      [ Html.text (projected.Text.Substring(0, s.Start))
        Html.mark
          [ prop.className (
              if exact then
                "fl-ps-hit"
              else
                "fl-ps-hit fl-ps-hit-enclosing"
            )
            prop.text (projected.Text.Substring(s.Start, s.Length)) ]
        Html.text (projected.Text.Substring(s.Start + s.Length)) ]

  Html.div
    [ prop.key label
      prop.className "fl-ps-pane"
      prop.children
        [ Html.div
            [ prop.className "fl-ps-pane-head"
              prop.children
                [ Html.span [ prop.className "fl-ps-lang"; prop.text label ]
                  Html.span [ prop.className "fl-ps-where"; prop.text status ] ] ]
          Html.pre
            [ prop.className "fl-code fl-ps-code"
              prop.custom ("data-fuaran-projection", Projection.languageTag target)
              prop.children code ] ] ]

// ─── the pane ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let SyncPane (tree: Node<obj> option) : ReactElement =
  let idPath, setIdPath = React.useState ([||]: string array)
  // The UPDATER form, not the value form. React batches state writes, so two
  // toggles clicked inside one batch both read the same captured `shown` and the
  // first one's change is silently lost. Observed, not theorised: switching JSON
  // on and TypeScript off in one go dropped the JSON pane.
  let shown, updateShown = React.useStateWithUpdater defaultShown

  // Subscribe once, and hand React the unsubscribe thunk as the effect's
  // cleanup. The Navigator replays the current path on subscribe, so a pane
  // opened part-way through a walk starts in step rather than blank. (The
  // annotation picks the `unit -> (unit -> unit)` overload; `useEffectOnce` has
  // four, and the return type is the only thing that separates them.)
  let subscribe () : unit -> unit = Navigator.subscribeCursor setIdPath

  React.useEffectOnce subscribe

  // After every render — the projections have just been re-derived, so this is
  // also the edit-sync path: a tree change re-renders, the spans move, and the
  // highlight is brought back into view where it now sits.
  React.useEffect (fun () -> scrollHitsIntoView () |> ignore)

  let toggle (target: Projection.Target) =
    updateShown (fun prev ->
      if List.contains target prev then
        prev |> List.filter (fun t -> t <> target)
      else
        // Keep the Output box's tab order rather than append-on-click order, so
        // a pane always appears where the reader expects it.
        Projection.targets
        |> List.map fst
        |> List.filter (fun t -> t = target || List.contains t prev))

  let toggles =
    Html.div
      [ prop.className "fl-ps-toggles"
        prop.role "group"
        prop.ariaLabel "Source projections to show"
        prop.children
          [ for target, label in Projection.targets do
              let on = List.contains target shown

              Html.button
                [ prop.key label
                  prop.className (
                    if on then
                      "fl-ps-toggle fl-ps-toggle-on"
                    else
                      "fl-ps-toggle"
                  )
                  prop.ariaPressed on
                  prop.text label
                  prop.onClick (fun _ -> toggle target) ] ] ]

  let body =
    match tree with
    | None ->
      Html.div
        [ prop.className "fl-empty fl-ps-empty"
          prop.text
            "No tree yet. Generate a UI (or load an example), then walk it — the construct under the cursor is highlighted in every language you switch on here." ]
    | Some root ->
      let wire = Canon.encodeNode root
      let path = List.ofArray idPath

      if List.isEmpty shown then
        Html.div
          [ prop.className "fl-empty fl-ps-empty"
            prop.text "No projections shown. Switch one on above." ]
      else
        Html.div
          [ prop.className "fl-ps-panes"
            prop.children
              [ for target, label in Projection.targets do
                  if List.contains target shown then
                    paneFor target label wire path ] ]

  Html.div
    [ prop.className "fl-ps"
      prop.children
        [ Html.p
            [ prop.className "fl-ps-intro"
              prop.text
                "The same node, in every language at once. Walk the tree beside this and the matching construct is highlighted here; edit it and the source re-derives with the highlight still on it." ]
          toggles
          body ] ]

/// The Navigator tab as the playground mounts it: the walk on one side, the
/// live source projections on the other. Takes the Navigator's already-built
/// element rather than its inputs, so this composition is unaffected by what the
/// Navigator's own entry point happens to take.
let beside (navigator: ReactElement) (tree: Node<obj> option) : ReactElement =
  Html.div
    [ prop.className "fl-nav-tab"
      prop.children
        [ Html.div [ prop.className "fl-nav-tab-walk"; prop.children [ navigator ] ]
          SyncPane tree ] ]
