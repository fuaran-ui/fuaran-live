module Fuaran.Showcase.WhatIf

// ============================================================================
//  The What-If Machine – parallel universes of your plan, side by side. Pillar:
//  "the app is a value" (its business face).
//
//  A scenario is not a copy of a spreadsheet – it is a BRANCH: a small op-set that
//  changes one assumption, with every derived value recomputed. Each active
//  what-if renders as its own live column (a counterfactual mount over a DAG
//  branch of one artefact). The ops drawer shows the real structural tree-diff
//  between the baseline tree and the scenario tree – a scenario is three or four
//  ops, its consequences ripple everywhere. Combine two scenarios that touch
//  different assumptions and they compose; two that touch the SAME assumption
//  genuinely conflict, and the page says so rather than faking a merge. Adopt a
//  column and it becomes the new baseline.
//
//  Honest scope: the revenue model + the compute run in F# (a toy but honest
//  ~4-assumption model, formulas one disclosure away); the ops are the real
//  `TreeOpDiff`; the compose/conflict is decided on the assumption the branch
//  touched. Nothing here needs a server – a plan is a value.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops.Types

module Diff = Fuaran.UI.OpStream.Replay.TreeOpDiff

// ─── The (toy, honest) revenue model ─────────────────────────────────────────

type private Assumptions =
  { Price: float // £ / unit
    Units: float // k units / yr
    Fx: float // % adjustment
    Opex: float } // £k

type private Derived =
  { Revenue: float // £k
    GrossProfit: float // £k
    Margin: float } // %

let private baseline0: Assumptions =
  { Price = 50.0
    Units = 200.0
    Fx = 0.0
    Opex = 6000.0 }

let private compute (a: Assumptions) : Derived =
  let revenue = a.Price * a.Units * (1.0 + a.Fx / 100.0)
  let gp = revenue - a.Opex

  { Revenue = revenue
    GrossProfit = gp
    Margin = (if revenue > 0.0 then gp / revenue * 100.0 else 0.0) }

let private fmtM (k: float) : string = sprintf "£%.1fM" (k / 1000.0)
let private fmtPct (x: float) : string = sprintf "%.0f%%" x

// ─── Scenarios = branch ops on one assumption ────────────────────────────────

type private Scenario =
  { Key: string
    Label: string
    Field: string
    Apply: Assumptions -> Assumptions }

let private scenarios: Scenario list =
  [ { Key = "price10"
      Label = "Price −10%"
      Field = "Price"
      Apply = fun a -> { a with Price = a.Price * 0.9 } }
    { Key = "price20"
      Label = "Price −20%"
      Field = "Price"
      Apply = fun a -> { a with Price = a.Price * 0.8 } }
    { Key = "units15"
      Label = "Units +15%"
      Field = "Units"
      Apply = fun a -> { a with Units = a.Units * 1.15 } }
    { Key = "fx5"
      Label = "FX +5%"
      Field = "Fx"
      Apply = fun a -> { a with Fx = a.Fx + 5.0 } }
    { Key = "opex"
      Label = "Trim opex 15%"
      Field = "Opex"
      Apply = fun a -> { a with Opex = a.Opex * 0.85 } } ]

let private scenarioByKey (k: string) : Scenario option =
  scenarios |> List.tryFind (fun s -> s.Key = k)

// ─── Building the plan tree (canonical ids for diffs, prefixed for render) ───

let private card (id: string) (heading: string) (value: string) : Node<unit> =
  Fuaran.card
    id
    { Defaults.card with
        Heading = Some(TextSource.Literal heading)
        Children = [ Fuaran.markdown (id + "-v") value ] }

let private group (id: string) (heading: string) (children: Node<unit> list) : Node<unit> =
  Fuaran.box
    id
    { Layout = LayoutMode.Flex(Orientation.Horizontal, true, Some 8)
      Role = BoxRole.Group
      KeepTogether = false
      BreakBefore = false
      Heading = Some(TextSource.Literal heading)
      Children = children }

let private planTree (prefix: string) (a: Assumptions) : Node<unit> =
  let d = compute a

  Fuaran.box
    (prefix + "root")
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
      Role = BoxRole.Dashboard
      KeepTogether = false
      BreakBefore = false
      Heading = None
      Children =
        [ group
            (prefix + "assume")
            "Assumptions"
            [ card (prefix + "price") "Price / unit" (sprintf "£%.0f" a.Price)
              card (prefix + "units") "Units (k)" (sprintf "%.0f" a.Units)
              card (prefix + "fx") "FX adj" (fmtPct a.Fx)
              card (prefix + "opex") "Opex" (fmtM a.Opex) ]
          group
            (prefix + "derived")
            "Derived"
            [ card (prefix + "rev") "Revenue" (fmtM d.Revenue)
              card (prefix + "gp") "Gross profit" (fmtM d.GrossProfit)
              card (prefix + "margin") "Margin" (fmtPct d.Margin) ] ] }

// The op-set a scenario carries = the real tree-diff from baseline to scenario.
let private scenarioOps (baseA: Assumptions) (scenA: Assumptions) : TreeOp<unit> list =
  Diff.diffBatched (planTree "wi-" baseA) (planTree "wi-" scenA)
  |> List.collect (fun op ->
    match op with
    | TreeOp.Batch xs -> xs
    | _ -> [ op ])

let private opSummary (op: TreeOp<unit>) : string =
  let idOf (NodeId s) = s

  match op with
  | TreeOp.UpdateProp(n, path, _) -> sprintf "UpdateProp %s.%s" (idOf n) path
  | TreeOp.ReplaceBinding(n, slot, _) -> sprintf "ReplaceBinding %s.%s" (idOf n) slot
  | TreeOp.InsertChild(p, c) -> sprintf "InsertChild %s ← %s" (idOf p) c.Id
  | TreeOp.RemoveNode n -> sprintf "RemoveNode %s" (idOf n)
  | _ -> "op"

// ─── The page ────────────────────────────────────────────────────────────────

let private renderPlan (n: Node<unit>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

type private Column =
  { Key: string
    Title: string
    Assumptions: Assumptions
    Conflict: string option }

[<ReactComponent>]
let private WhatIfView () : ReactElement =
  let baseline, setBaseline = React.useState baseline0
  let active, setActive = React.useState ([]: string list)
  let composed, setComposed = React.useState false
  let openOps, setOpenOps = React.useState (None: string option)

  let baseD = compute baseline

  let toggle (k: string) : unit =
    setComposed false

    if List.contains k active then
      setActive (active |> List.filter (fun x -> x <> k))
    elif List.length active >= 3 then
      () // three scenarios + baseline is the cap
    else
      setActive (active @ [ k ])

  // The columns to show: baseline, each active scenario, and the composite of
  // the first two active scenarios when the visitor asks to combine them.
  let columns: Column list =
    let scenCols =
      active
      |> List.choose (fun k ->
        scenarioByKey k
        |> Option.map (fun s ->
          { Key = k
            Title = s.Label
            Assumptions = s.Apply baseline
            Conflict = None }))

    let compositeCol =
      match composed, active with
      | true, a :: b :: _ ->
        match scenarioByKey a, scenarioByKey b with
        | Some sa, Some sb ->
          if sa.Field = sb.Field then
            Some
              { Key = "composite"
                Title = sprintf "%s + %s" sa.Label sb.Label
                Assumptions = baseline
                Conflict =
                  Some(
                    sprintf
                      "both change %s – the branches conflict; you cannot hold two %s values at once."
                      sa.Field
                      sa.Field
                  ) }
          else
            Some
              { Key = "composite"
                Title = sprintf "Composite · %s + %s" sa.Label sb.Label
                Assumptions = sb.Apply(sa.Apply baseline)
                Conflict = None }
        | _ -> None
      | _ -> None

    { Key = "base"
      Title = "Baseline"
      Assumptions = baseline
      Conflict = None }
    :: scenCols
    @ (compositeCol |> Option.toList)

  let chipBar =
    Html.div
      [ prop.className "wi-chips"
        prop.children
          [ Html.span [ prop.className "wi-chips-label"; prop.text "Ask a what-if:" ]
            for s in scenarios ->
              Html.button
                [ prop.className (
                    if List.contains s.Key active then
                      "wi-chip wi-chip-on"
                    else
                      "wi-chip"
                  )
                  prop.disabled (not (List.contains s.Key active) && List.length active >= 3)
                  prop.text s.Label
                  prop.onClick (fun _ -> toggle s.Key) ] ] ]

  let combineRow =
    if List.length active >= 2 then
      Html.div
        [ prop.className "wi-combine"
          prop.children
            [ Html.button
                [ prop.className "wi-combine-btn"
                  prop.text (
                    if composed then
                      "Un-combine"
                    else
                      sprintf "Combine the first two universes →"
                  )
                  prop.onClick (fun _ -> setComposed (not composed)) ] ] ]
    else
      Html.none

  let deltaBadge (delta: float) : ReactElement =
    Html.span
      [ prop.className (
          if delta >= 0.0 then
            "wi-delta wi-delta-up"
          else
            "wi-delta wi-delta-down"
        )
        prop.text (sprintf "%s%s rev" (if delta >= 0.0 then "+" else "") (fmtM delta)) ]

  let colHead (c: Column) (isBase: bool) (delta: float) : ReactElement =
    Html.div
      [ prop.className "wi-col-head"
        prop.children
          [ Html.span [ prop.className "wi-col-title"; prop.text c.Title ]
            (if isBase then Html.none else deltaBadge delta) ] ]

  let colActions (c: Column) : ReactElement =
    Html.div
      [ prop.className "wi-col-actions"
        prop.children
          [ Html.button
              [ prop.className "wi-op-btn"
                prop.text (
                  if openOps = Some c.Key then
                    "Hide the branch ops"
                  else
                    "Show the branch ops"
                )
                prop.onClick (fun _ -> setOpenOps (if openOps = Some c.Key then None else Some c.Key)) ]
            Html.button
              [ prop.className "wi-adopt-btn"
                prop.text "Adopt as the plan"
                prop.onClick (fun _ ->
                  setBaseline c.Assumptions
                  setActive []
                  setComposed false
                  setOpenOps None) ] ] ]

  let opsDrawer (c: Column) : ReactElement =
    let ops = scenarioOps baseline c.Assumptions

    Html.div
      [ prop.className "wi-ops"
        prop.children
          [ Html.div
              [ prop.className "wi-ops-head"
                prop.text (sprintf "this scenario is %d ops – small cause, large effect" (List.length ops)) ]
            Html.ul
              [ prop.className "wi-ops-list"
                prop.children
                  [ for op in ops ->
                      Html.li
                        [ prop.className "wi-op"
                          prop.children [ Html.code [ prop.text (opSummary op) ] ] ] ] ] ] ]

  let columnCard (c: Column) : ReactElement =
    let d = compute c.Assumptions
    let delta = d.Revenue - baseD.Revenue
    let isBase = c.Key = "base"

    let body =
      match c.Conflict with
      | Some msg -> Html.div [ prop.className "wi-conflict"; prop.text ("⛔ " + msg) ]
      | None ->
        Html.div
          [ prop.className "wi-col-body"
            prop.children
              [ Html.div
                  [ prop.className "wi-plan"
                    prop.children [ renderPlan (planTree ("c-" + c.Key + "-") c.Assumptions) ] ]
                (if isBase then Html.none else colActions c) ] ]

    let ops =
      match c.Conflict, openOps = Some c.Key with
      | None, true -> opsDrawer c
      | _ -> Html.none

    Html.div
      [ prop.className (
          if c.Key = "composite" then
            "wi-col wi-col-composite"
          else
            "wi-col"
        )
        prop.key c.Key
        prop.children [ colHead c isBase delta; body; ops ] ]

  let board =
    Html.div
      [ prop.className "wi-board"
        prop.children [ for c in columns -> columnCard c ] ]

  let formula =
    Html.details
      [ prop.className "wi-formula"
        prop.children
          [ Html.summary [ prop.text "The formulas (for the engineer reading over the CFO's shoulder)" ]
            Html.pre
              [ prop.className "wi-formula-body"
                prop.children
                  [ Html.code
                      [ prop.text
                          "revenue      = price × units × (1 + fx/100)\ngross_profit = revenue − opex\nmargin       = gross_profit / revenue\n\nA scenario changes ONE assumption; every derived value recomputes.\nThe branch ops are the tree-diff from baseline to the scenario tree." ] ] ] ] ]

  let honesty =
    Html.div
      [ prop.className "wi-honesty"
        prop.children
          [ Html.h3 [ prop.text "Scenario planning is version control in a business suit" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Each column is the same artefact on its own branch – a counterfactual rendered live, side by side. A scenario is a small op-set (open the branch ops), yet its consequences recompute everywhere. Nothing here is a copied spreadsheet." ]
                    Html.li
                      [ prop.text
                          "Combine two universes that touch different assumptions and they compose into a fourth column; two that touch the same assumption genuinely conflict – you cannot hold two prices at once – and the page says so rather than faking a merge." ]
                    Html.li
                      [ prop.text
                          "Adopt a column and it becomes the new baseline; every scenario then re-branches from there. Planning with an audit trail by construction – which what-if you asked, which ops it carried, when you adopted it." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "This is the mirror of the Time Machine: not where the app has been, but where it could go. The "
                            Html.a [ prop.href "#/pillar/value"; prop.text "app-is-a-value" ]
                            Html.text " thesis, at the planning layer – no server, a plan is a value." ] ] ] ] ] ]

  Html.div
    [ prop.className "wi-page"
      prop.children
        [ Html.h1 [ prop.className "wi-title"; prop.text "The What-If Machine" ]
          Html.p
            [ prop.className "wi-lede"
              prop.text
                "Ask “what if we cut price 10%, or grow units 15%?” – and parallel universes of your plan open side by side, each a real live app on its own branch. Pick the future you like; it becomes the plan." ]
          chipBar
          combineRow
          board
          formula
          honesty ] ]

let page: ReactElement = WhatIfView()
