module Fuaran.Live.ProjectionSync

// ============================================================================
//  Navigator ⇄ source-projection sync (Phase 714; recomposed 2026-07-30 as the
//  unified Source card).
//
//  Walk the tree in the Editor; watch the corresponding construct light up in
//  whichever representation the Source card has open — the canonical wire JSON
//  or any of the nine host languages. That is a feature no other UI system can
//  show: the same node, as wire bytes or as TypeScript, Python or F# source —
//  because in Fuaran the tree IS the artefact, and each language is a
//  projection of it rather than the thing itself.
//
//  Two things make it honest rather than decorative:
//
//   • **One projector, not two.** The card calls `Projection.projectSpans`, the
//     same walk `Projection.projectTo` runs; the span map falls out of the walk
//     itself (invisible sentinels the generators emit and the entry points
//     strip). There is no second, "highlight-aware" projector that could drift
//     from the real one.
//
//   • **Nearest-enclosing resolution.** A language does not project every node:
//     the illustrative walkers fold a node in a `state` slot into its parent's
//     construct, and no source-level range corresponds to it. Rather than
//     showing nothing, the pane highlights the closest ANCESTOR the language
//     does project, and says so. "Nothing is highlighted" and "this construct
//     contains your node" are different facts and are reported differently.
//     The wire JSON tab always resolves exactly — every node projects to wire.
//
//  The cursor arrives by SUBSCRIPTION (`Navigator.subscribeCursor`), not by
//  prop: the Navigator owns the walk, this card only watches it. Everything
//  else is derived from the session tree on each render, so an applied op —
//  from the property panel, a model re-emission, a replayed permalink — re-runs
//  the projection and lands the highlight on the focused node's NEW span with
//  no edit path of its own. Scrolling uses `block: 'nearest'`, so a highlight
//  already visible does not move at all; scroll context survives the edit.
//
//  History: until the 2026-07-30 workspace recomposition this module rendered
//  three fixed language panes BESIDE the walk, inside the Navigator disclosure
//  (`beside` / `SyncPane`). The Source card supersedes that — and the separate
//  Inspector (wire JSON) and Output (source projection) boxes — with one tabbed
//  pane in the right column, permanently visible next to the live preview.
// ============================================================================

open Fable.Core
open Feliz
open Fuaran.UI.Types

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

/// The host's effect seam, bound to the browser implementation exactly as the
/// Navigator binds it — the Copy button is the only effect this card performs.
let private effects = Byok.browserEffectPorts

// ─── keeping the highlight in view ───────────────────────────────────────────

/// Bring the pane's highlight into view WITHIN its own `.fl-ps-code` scroll box
/// only. Deliberately NOT `scrollIntoView`: that scrolls every scrollable
/// ancestor including the viewport, so each cursor step in the Editor yanked
/// the whole page toward the Source card (reported 2026-07-30). A highlight
/// already on screen does not move — "re-derive the projections without losing
/// scroll context" — and the page never moves at all. No smooth behaviour: an
/// edit should land, not animate.
[<Emit("""(function(){
  try {
    var hits = document.querySelectorAll('.fl-ps-hit');
    for (var i = 0; i < hits.length; i++) {
      var el = hits[i];
      var sc = el.closest('.fl-ps-code');
      if (!sc) { continue; }
      var cr = sc.getBoundingClientRect();
      var er = el.getBoundingClientRect();
      if (er.top < cr.top) { sc.scrollTop += er.top - cr.top; }
      else if (er.bottom > cr.bottom) { sc.scrollTop += Math.min(er.bottom - cr.bottom, er.top - cr.top); }
      if (er.left < cr.left) { sc.scrollLeft += er.left - cr.left; }
      else if (er.left > cr.right) { sc.scrollLeft += er.left - cr.right; }
    }
    return hits.length;
  } catch (e) { return 0; }
})()""")>]
let private scrollHitsIntoView () : int = jsNative

// ─── one representation pane ─────────────────────────────────────────────────

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
    | None when List.isEmpty idPath -> "walk the tree in the Editor to highlight the matching construct here"
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
                  Html.span [ prop.className "fl-ps-where"; prop.text status ]
                  Html.button
                    [ prop.className "fl-ps-copy"
                      prop.title "Copy this representation to the clipboard"
                      prop.text "Copy"
                      prop.onClick (fun _ -> effects.WriteToClipboard projected.Text) ] ] ]
          Html.pre
            [ prop.className "fl-code fl-ps-code"
              prop.custom ("data-fuaran-projection", Projection.languageTag target)
              prop.children code ] ] ]

// ─── the unified Source card ─────────────────────────────────────────────────

/// The first tab is the wire itself, not a language projection of it — name it
/// what it is. The remaining labels are the shared Output-box labels verbatim.
let private tabLabel (target: Projection.Target) (label: string) : string =
  match target with
  | Projection.Target.Json -> "Wire JSON"
  | _ -> label

/// The right column's "the tree, as text" card: the canonical wire JSON or one
/// of the nine host-language projections, one tab at a time, cursor-synced to
/// the Editor's walk. Supersedes the Inspector and Output boxes.
[<ReactComponent>]
let SourceCard
  (tree: Node<obj> option)
  (active: Projection.Target)
  (onSelect: Projection.Target -> unit)
  : ReactElement =
  let idPath, setIdPath = React.useState ([||]: string array)

  // Subscribe once, and hand React the unsubscribe thunk as the effect's
  // cleanup. The Navigator replays the current path on subscribe, so a card
  // mounted part-way through a walk starts in step rather than blank. (The
  // annotation picks the `unit -> (unit -> unit)` overload; `useEffectOnce` has
  // four, and the return type is the only thing that separates them.)
  let subscribe () : unit -> unit = Navigator.subscribeCursor setIdPath

  React.useEffectOnce subscribe

  // After every render — the projection has just been re-derived, so this is
  // also the edit-sync path: a tree change re-renders, the spans move, and the
  // highlight is brought back into view where it now sits.
  React.useEffect (fun () -> scrollHitsIntoView () |> ignore)

  let tabs =
    Html.div
      [ prop.className "fl-output-tabs"
        prop.role "tablist"
        prop.ariaLabel "Source representation"
        prop.children
          [ for t, label in Projection.targets do
              let selected = t = active

              Html.button
                [ prop.key label
                  prop.role "tab"
                  prop.ariaSelected selected
                  prop.className (
                    if selected then
                      "fl-output-tab fl-output-tab-active"
                    else
                      "fl-output-tab"
                  )
                  prop.text (tabLabel t label)
                  prop.onClick (fun _ -> onSelect t) ] ] ]

  let body =
    match tree with
    | None ->
      Html.div
        [ prop.className "fl-empty fl-ps-empty"
          prop.text
            "Build a UI – its canonical wire JSON plus illustrative builder source in all nine host languages appears here, and walking the tree in the Editor highlights the matching construct." ]
    | Some root ->
      let wire = Canon.encodeNode root

      let label =
        Projection.targets
        |> List.pick (fun (t, l) -> if t = active then Some(tabLabel t l) else None)

      paneFor active label wire (List.ofArray idPath)

  Html.div [ prop.className "fl-source"; prop.children [ tabs; body ] ]
