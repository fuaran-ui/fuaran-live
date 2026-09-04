module Fuaran.Showcase.GitForInterfaces

// ============================================================================
//  Git for Interfaces – two assistants edit one app on separate branches; their
//  work is combined by a real structural three-way merge. Pillar: "the app is a
//  value".
//
//  Because a Fuaran app is a value, it can be branched and merged. Both branches
//  fork from the same base; each is a list of real `TreeOp`s. The merge is the
//  **shipped language-tier engine** running in the browser: `TreeMerge.merge3Way`
//  (the facet-refined structural 3-way merge, fuaran#179) – made Fable-portable
//  by fuaran#501, so the same function the .NET host runs now executes here.
//
//   - Round 1 (clean): the two branches touch DISJOINT cells – Agent A deep-edits
//     two figures, Agent B adds sibling cards – so `merge3Way base A B` returns
//     `Ok mergedTree`: a genuine auto-merge, nothing to resolve.
//   - Round 2 (conflict): both assistants rewrite the SAME title, so `merge3Way`
//     returns `Error [MergeConflict …]` naming the contended `g-title:kind` cell
//     (detected by the engine's canonical-encoding comparison, three-up over
//     base/A/B). The visitor resolves it; human-primacy is the default because the
//     visitor's own title lives in the common ancestor both branches forked from –
//     `merge3WayLenient` (both sides Secondary) keeps that base value, so "yours"
//     wins unless the visitor hands the cell to an assistant.
//
//  Honest scope: the branch trees are authored with the shipped `Apply.apply`;
//  the MERGE (clean auto-merge, conflict detection, primacy default) is the real
//  `Fuaran.UI.OpStream.Dag.Merge.TreeMerge` engine, not a client-side re-creation.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Dag.Merge

module Apply = Fuaran.UI.Ops.Apply

let private nid (s: string) : NodeId = NodeId s
let private wireStr (s: string) : PropValue = PropValue.Wire(JStr s)

// ─── The base app (the common ancestor – the visitor's own app) ──────────────

let private card (id: string) (label: string) (value: string) : Node<unit> =
  Fuaran.card
    id
    { Defaults.card with
        Heading = Some(TextSource.Literal label)
        Children = [ Fuaran.markdown (id + "-v") value ] }

let private baseTree: Node<unit> =
  Fuaran.box
    "g-root"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
      Role = BoxRole.Dashboard
      KeepTogether = false
      BreakBefore = false
      Heading = None
      Children =
        [ Fuaran.heading
            "g-title"
            { Level = 3
              Text = TextSource.Literal "Q3 board review"
              Variant = HeadingVariant.Standard }
          card "g-rev" "Revenue" "£2,400,000"
          card "g-users" "Users" "18,204"
          Fuaran.callout
            "g-note"
            { Defaults.callout with
                Tone = ToneVariant.Subdued
                Heading = None
                Body = TextSource.Literal "Draft – for review." } ] }

// ─── Branch authoring (real ops) + the real merge engine ─────────────────────

let private applyAll (ops: TreeOp<unit> list) (tree: Node<unit>) : Node<unit> =
  (tree, ops)
  ||> List.fold (fun t op ->
    match Apply.apply op t with
    | Ok next -> next
    | Error _ -> t)

// ─── Scenario 1 – the clean merge (disjoint cells) ───────────────────────────

// Agent A ("make it executive"): formatting smartening of the SAME figure
// (£2,400,000 → £2.4M), never a change of fact. A deep edit of a leaf value,
// no change to g-root's child list.
let private cleanA: TreeOp<unit> list =
  [ TreeOp.UpdateProp(nid "g-rev-v", "Text", wireStr "£2.4M") ]

// Agent B ("add analytics"): appends new sibling cards AND smartens a
// DIFFERENT cell's formatting (18,204 → 18.2k) – both branches deep-edit,
// still fully disjoint from the leaf A touched.
let private cleanB: TreeOp<unit> list =
  [ TreeOp.UpdateProp(nid "g-users-v", "Text", wireStr "18.2k")
    TreeOp.InsertChild(nid "g-root", card "g-trend" "Trend" "▲ 12% MoM")
    TreeOp.InsertChild(nid "g-root", card "g-region" "Top region" "EMEA") ]

let private cleanATree = applyAll cleanA baseTree
let private cleanBTree = applyAll cleanB baseTree

// THE REAL ENGINE: the facet-refined structural 3-way merge. Disjoint cells ⇒ Ok.
let private cleanMerge: Result<Node<unit>, MergeConflict list> =
  TreeMerge.merge3Way baseTree cleanATree cleanBTree

// ─── Scenario 2 – the conflict (both edit g-title) + a disjoint change each ──

let private conflictA: TreeOp<unit> list =
  [ TreeOp.UpdateProp(nid "g-title", "Text", wireStr "Executive summary")
    TreeOp.UpdateProp(nid "g-rev-v", "Text", wireStr "£2.4M") ]

let private conflictB: TreeOp<unit> list =
  [ TreeOp.UpdateProp(nid "g-title", "Text", wireStr "Analytics review")
    TreeOp.UpdateProp(nid "g-users-v", "Text", wireStr "18.2k")
    TreeOp.InsertChild(nid "g-root", card "g-trend" "Trend" "▲ 12% MoM") ]

let private conflictATree = applyAll conflictA baseTree
let private conflictBTree = applyAll conflictB baseTree

// THE REAL ENGINE: same call, but the two branches collide on g-title, so the
// engine returns Error carrying the contended-cell envelope(s).
let private conflictMerge: Result<Node<unit>, MergeConflict list> =
  TreeMerge.merge3Way baseTree conflictATree conflictBTree

let private conflicts: MergeConflict list =
  match conflictMerge with
  | Ok _ -> []
  | Error cs -> cs

// The human-primacy DEFAULT merged tree: the lenient 3-way merge (both sides
// Secondary) resolves the contended cell to the common ancestor – the visitor's
// own value – while still auto-merging the disjoint changes around it.
let private conflictBaseMerged: Node<unit> =
  TreeMerge.merge3WayLenient baseTree conflictATree conflictBTree

[<RequireQualifiedAccess>]
type private Resolution =
  | Yours
  | AgentA
  | AgentB

let private resolvedTitle (r: Resolution) : string =
  match r with
  | Resolution.Yours -> "Q3 board review"
  | Resolution.AgentA -> "Executive summary"
  | Resolution.AgentB -> "Analytics review"

// Resolution is a human choice OVER the engine's default: the lenient merge keeps
// the base title; handing the cell to an assistant applies their value to that one
// contended node, leaving every auto-merged cell untouched.
let private conflictMerged (r: Resolution) : Node<unit> =
  match r with
  | Resolution.Yours -> conflictBaseMerged
  | _ -> applyAll [ TreeOp.UpdateProp(nid "g-title", "Text", wireStr (resolvedTitle r)) ] conflictBaseMerged

// ─── View helpers ────────────────────────────────────────────────────────────

[<RequireQualifiedAccess>]
type private Scenario =
  | Clean
  | Conflict

let private renderTree (n: Node<unit>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

/// `accent` is "" (neutral) or "a" / "b" – tints the tile to match the branch's
/// colour in the DAG diagram (slightly lighter than the diagram node fill).
let private appPaneAccented (accent: string) (title: string) (badge: string option) (tree: Node<unit>) : ReactElement =
  Html.div
    [ prop.className (
        match accent with
        | "" -> "gi-pane"
        | a -> "gi-pane gi-pane-" + a
      )
      prop.children
        [ Html.div
            [ prop.className "gi-pane-head"
              prop.children
                [ Html.span [ prop.className "gi-pane-title"; prop.text title ]
                  (match badge with
                   | Some b -> Html.span [ prop.className "gi-pane-badge"; prop.text b ]
                   | None -> Html.none) ] ]
          Html.div [ prop.className "gi-pane-app"; prop.children [ renderTree tree ] ] ] ]

/// The bottom node's three states: waiting for the combine, contested (the
/// engine returned a conflict the visitor has not resolved yet), or landed.
[<RequireQualifiedAccess>]
type private DagState =
  | Pending
  | Clash
  | Merged

// The one diamond – base forks to A and B, which merge back. The bottom node
// tracks DagState: dashed "?" while pending, a red dashed "!" with a
// "conflict" caption while contested, a green "✓" with a "merged" caption
// once landed. Edges are branch-coloured (A blue, B red) with arrowheads
// showing the direction of history; the two merge edges stay dashed only
// while pending. Arrowhead triangles are precomputed for the fixed geometry
// (Feliz's typed SVG API has no marker-ref attributes).
let private dagDiamond (state: DagState) : ReactElement =
  let merged = state <> DagState.Pending

  let edgeCls (branch: string) (pending: bool) =
    "gi-dag-edge gi-dag-edge-"
    + branch
    + (if pending then " gi-dag-pending" else "")

  let arrowCls (branch: string) (pending: bool) =
    "gi-dag-arrow-" + branch + (if pending then " gi-dag-pending" else "")

  Svg.svg
    [ svg.viewBox (0, 0, 220, 182)
      svg.className "gi-dag"
      svg.children
        [ // base → A (blue, solid)
          Svg.line
            [ svg.x1 99
              svg.y1 31
              svg.x2 53
              svg.y2 71
              svg.className (edgeCls "a" false) ]
          Svg.polygon
            [ svg.points [ 47.0, 76.0; 51.0, 68.4; 55.2, 73.2 ]
              svg.className (arrowCls "a" false) ]
          // base → B (red, solid)
          Svg.line
            [ svg.x1 121
              svg.y1 31
              svg.x2 167
              svg.y2 71
              svg.className (edgeCls "b" false) ]
          Svg.polygon
            [ svg.points [ 173.0, 76.0; 169.0, 68.4; 164.8, 73.2 ]
              svg.className (arrowCls "b" false) ]
          // A → merge (blue, dashed until the merge lands)
          Svg.line
            [ svg.x1 47
              svg.y1 94
              svg.x2 93
              svg.y2 134
              svg.className (edgeCls "a" (not merged)) ]
          Svg.polygon
            [ svg.points [ 99.0, 139.0; 90.8, 136.2; 95.0, 131.4 ]
              svg.className (arrowCls "a" (not merged)) ]
          // B → merge (red, dashed until the merge lands)
          Svg.line
            [ svg.x1 173
              svg.y1 94
              svg.x2 127
              svg.y2 134
              svg.className (edgeCls "b" (not merged)) ]
          Svg.polygon
            [ svg.points [ 121.0, 139.0; 129.2, 136.2; 125.0, 131.4 ]
              svg.className (arrowCls "b" (not merged)) ]
          // nodes
          Svg.circle [ svg.cx 110; svg.cy 22; svg.r 14; svg.className "gi-dag-node" ]
          Svg.circle [ svg.cx 36; svg.cy 85; svg.r 14; svg.className "gi-dag-node gi-dag-a" ]
          Svg.circle [ svg.cx 184; svg.cy 85; svg.r 14; svg.className "gi-dag-node gi-dag-b" ]
          Svg.circle
            [ svg.cx 110
              svg.cy 148
              svg.r 14
              svg.className (
                match state with
                | DagState.Pending -> "gi-dag-node gi-dag-pending-node"
                | DagState.Clash -> "gi-dag-node gi-dag-clash"
                | DagState.Merged -> "gi-dag-node gi-dag-on"
              ) ]
          // labels
          Svg.text
            [ svg.x 110
              svg.y 26
              svg.textAnchor.middle
              svg.className "gi-dag-label"
              svg.text "base" ]
          Svg.text
            [ svg.x 36
              svg.y 89
              svg.textAnchor.middle
              svg.className "gi-dag-label"
              svg.text "A" ]
          Svg.text
            [ svg.x 184
              svg.y 89
              svg.textAnchor.middle
              svg.className "gi-dag-label"
              svg.text "B" ]
          Svg.text
            [ svg.x 110
              svg.y 152
              svg.textAnchor.middle
              svg.className (
                match state with
                | DagState.Pending -> "gi-dag-label"
                | DagState.Clash -> "gi-dag-label gi-dag-bang"
                | DagState.Merged -> "gi-dag-label gi-dag-check"
              )
              svg.text (
                match state with
                | DagState.Pending -> "?"
                | DagState.Clash -> "!"
                | DagState.Merged -> "✓"
              ) ]
          (match state with
           | DagState.Pending -> Html.none
           | DagState.Clash ->
             Svg.text
               [ svg.x 110
                 svg.y 176
                 svg.textAnchor.middle
                 svg.className "gi-dag-label gi-dag-bang"
                 svg.text "conflict" ]
           | DagState.Merged ->
             Svg.text
               [ svg.x 110
                 svg.y 176
                 svg.textAnchor.middle
                 svg.className "gi-dag-label"
                 svg.text "merged" ]) ] ]

// ─── The page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private GitForInterfacesView () : ReactElement =
  let scenario, setScenario = React.useState Scenario.Clean
  let merged, setMerged = React.useState false
  let resolution, setResolution = React.useState (None: Resolution option)

  let switch (s: Scenario) : unit =
    setScenario s
    setMerged false
    setResolution None

  // Real detection: the engine flagged g-title as a contended cell.
  let titleConflict = conflicts |> List.exists (fun c -> c.NodeId = "g-title")

  let aTree, bTree =
    match scenario with
    | Scenario.Clean -> cleanATree, cleanBTree
    | Scenario.Conflict -> conflictATree, conflictBTree

  let scenarioTabs =
    Html.div
      [ prop.className "gi-tabs"
        prop.children
          [ Html.button
              [ prop.className (
                  if scenario = Scenario.Clean then
                    "gi-tab gi-tab-on"
                  else
                    "gi-tab"
                )
                prop.text "Round 1 · clean merge"
                prop.onClick (fun _ -> switch Scenario.Clean) ]
            Html.button
              [ prop.className (
                  if scenario = Scenario.Conflict then
                    "gi-tab gi-tab-on"
                  else
                    "gi-tab"
                )
                prop.text "Round 2 · a conflict"
                prop.onClick (fun _ -> switch Scenario.Conflict) ] ] ]

  let branchRow =
    Html.div
      [ prop.className "gi-branch-row"
        prop.children
          [ appPaneAccented "a" "Agent A · make it executive" (Some "branch A") aTree
            appPaneAccented "" "Base · your app" None baseTree
            appPaneAccented "b" "Agent B · add analytics" (Some "branch B") bTree ] ]

  let mergeControls =
    Html.div
      [ prop.className "gi-merge-controls"
        prop.children
          [ Html.button
              [ prop.className "gi-merge-btn"
                prop.disabled merged
                prop.text "Combine the two branches"
                prop.onClick (fun _ -> setMerged true) ]
            dagDiamond (
              if not merged then
                DagState.Pending
              elif scenario = Scenario.Conflict && titleConflict && resolution = None then
                DagState.Clash
              else
                DagState.Merged
            ) ] ]

  let cleanResult =
    match cleanMerge with
    | Ok mergedTree ->
      Html.div
        [ prop.className "gi-result"
          prop.children
            [ Html.div
                [ prop.className "gi-result-banner gi-result-ok"
                  prop.text
                    "The structural three-way merge returned a clean result – the branches touched different cells, so the engine auto-merged them with nothing to resolve." ]
              appPaneAccented "" "Merged · B's analytics inside A's tightening" (Some "auto-merged") mergedTree ] ]
    | Error _ ->
      Html.div
        [ prop.className "gi-result"
          prop.children
            [ Html.div
                [ prop.className "gi-result-banner gi-result-conflict"
                  prop.text "Unexpected conflict on the clean round." ] ] ]

  let conflictResult =
    let chosen = defaultArg resolution Resolution.Yours

    Html.div
      [ prop.className "gi-result"
        prop.children
          [ Html.div
              [ prop.className "gi-result-banner gi-result-conflict"
                prop.text (
                  if titleConflict then
                    "⚠ Merge conflict — the disjoint changes merged automatically, but both assistants rewrote the same title. The engine detected that collision, not a guess: the title's canonical encoding differs across base, A and B."
                  else
                    "No conflict detected."
                ) ]
            // The contended cell(s), straight from the engine's MergeConflict
            // report – real data, not page prose.
            (if titleConflict then
               Html.div
                 [ prop.className "gi-conflict-cells"
                   prop.children
                     [ for c in conflicts ->
                         Html.span
                           [ prop.className "gi-conflict-chip"
                             prop.text (sprintf "contended cell · %s : %s · %A" c.NodeId c.Facet c.Class) ] ] ]
             else
               Html.none)
            Html.div
              [ prop.className "gi-resolve"
                prop.children
                  [ Html.span [ prop.className "gi-resolve-label"; prop.text "Whose title wins?" ]
                    Html.div
                      [ prop.className "gi-resolve-opts"
                        prop.children
                          [ for r, label in
                              [ Resolution.Yours, "Keep yours · “Q3 board review”"
                                Resolution.AgentA, "Take Agent A · “Executive summary”"
                                Resolution.AgentB, "Take Agent B · “Analytics review”" ] ->
                              Html.button
                                [ prop.className (
                                    if chosen = r then
                                      "gi-resolve-opt gi-resolve-on"
                                    else
                                      "gi-resolve-opt"
                                  )
                                  prop.text label
                                  prop.onClick (fun _ -> setResolution (Some r)) ] ] ]
                    (if resolution = None then
                       Html.p
                         [ prop.className "gi-primacy-note"
                           prop.text
                             "Human-primacy is the default: the engine keeps your version (it lives in the common ancestor both assistants forked from) unless you hand the cell over." ]
                     else
                       Html.none) ] ]
            appPaneAccented "" "Merged · conflict resolved" (Some "resolved") (conflictMerged chosen) ] ]

  let resultBlock =
    if not merged then
      Html.none
    else
      match scenario with
      | Scenario.Clean -> cleanResult
      | Scenario.Conflict -> conflictResult

  let honesty =
    Html.div
      [ prop.className "gi-honesty"
        prop.children
          [ Html.h3 [ prop.text "A real merge, not a mock-up" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Both rounds run the shipped structural three-way merge from the language tier – the very same function the server-side host runs, compiled into this page. Nothing about the merge is staged." ]
                    Html.li
                      [ prop.text
                          "Round 1 returns a clean auto-merge because the branches changed different cells; the engine composes them into one tree. Round 2 returns a conflict because both assistants rewrote the same title – detected by comparing that node's canonical encoding across base, A and B, three-way." ]
                    Html.li
                      [ prop.text
                          "Human-primacy is the closing beat: your edit lives in the common ancestor both assistants forked from, so the engine keeps it by default on the one real conflict while the disjoint work merges around it. Your version survived two assistants." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "Nothing here exists in a React app – there is no value to branch. This is the version-control face of the "
                            Html.a [ prop.href "#/pillar/value"; prop.text "app-is-a-value" ]
                            Html.text " story." ] ] ] ] ] ]

  Html.div
    [ prop.className "gi-page"
      prop.children
        [ Html.h1 [ prop.className "gi-title"; prop.text "Git for Interfaces" ]
          Html.p
            [ prop.className "gi-lede"
              prop.text
                "Two assistants worked on the same app in parallel, on separate branches. Combine their work – a real structural merge lands both changes, and when they collide, you decide, with your own edit winning by default." ]
          scenarioTabs
          branchRow
          mergeControls
          resultBlock
          honesty ] ]

let page: ReactElement = GitForInterfacesView()
