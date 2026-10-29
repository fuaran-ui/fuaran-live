module Fuaran.Showcase.PivotSandbox

// ============================================================================
//  The Pivot Sandbox — build a pipeline, watch it run, watch a runaway refused
//  (Phase 1887).
//
//  The visitor composes a dataframe pipeline out of steps: a derived column, a
//  filter, a running total, a group-by, a pivot, a sort. Nothing they compose is
//  code. Each step is a `Fuaran.Compute.Transform` value, the pipeline is a list
//  of them inside the grid's `Binding.Transform`, and the renderer evaluates it
//  with the shipped dataframe evaluator, in the browser, on every change.
//
//  A tree that declares its own pipeline decides how much work the reader's
//  machine does, so the renderer meters every evaluation against a budget
//  counted in what the tree DECLARES, rows in and steps (Phase 1532). The
//  "runaway" step is what a looping generator emits: seventy more steps on the
//  same table. The renderer refuses it before evaluating anything, and the
//  refusal is drawn in the grid's own error slot with the limit it overran.
//
//  The table is sixteen rows on purpose. Joins, wide group-bys, pivots and
//  windows at scale are slow in a browser until the evaluator's gathering work
//  lands; a sandbox that showed those shapes at scale would be demonstrating
//  something this page cannot honestly promise.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module Budget = BindingResolver.TransformBudget

// ─── the table ───────────────────────────────────────────────────────────────

let private regions = [ "North", 120; "South", 95; "East", 140; "West", 80 ]
let private quarters = [ "Q1", 80; "Q2", 100; "Q3", 110; "Q4", 140 ]

/// Sixteen rows: region x quarter, units (integer arithmetic) and revenue at a
/// flat price. Deterministic and inline.
let table: Table =
  let rows =
    [ for region, scale in regions do
        for quarter, season in quarters do
          let units = scale * season / 100
          region, quarter, units, float units * 3.5 ]

  let col build name values =
    build name (Vector.ofList values) AllValid

  { Schema =
      [ Field.create "region" StringType
        Field.create "quarter" StringType
        Field.create "units" IntType
        Field.create "revenue" FloatType ]
    Columns =
      [ col Fuaran.Core.Column.ofStrs "region" [ for r, _, _, _ in rows -> r ]
        col Fuaran.Core.Column.ofStrs "quarter" [ for _, q, _, _ in rows -> q ]
        col Fuaran.Core.Column.ofInts "units" [ for _, _, u, _ in rows -> u ]
        col Fuaran.Core.Column.ofFloats "revenue" [ for _, _, _, v in rows -> v ] ] }

// ─── the steps a visitor can add ─────────────────────────────────────────────

/// One entry in the step palette: an id, what the button says, and the
/// `Transform` steps it appends — one each, except the runaway.
type Step =
  { Id: string
    Label: string
    Steps: Transform list }

/// How many steps the runaway appends. Over the shipped step limit on purpose,
/// whatever that limit is: the number is read from the budget, not restated.
let runawayLength: int = Budget.defaults.MaxSteps + 6

let palette: Step list =
  [ { Id = "price"
      Label = "Derive price = revenue ÷ units"
      Steps = [ Derive("price", Binary(Div, Col "revenue", Cast(FloatType, Col "units"))) ] }
    { Id = "large"
      Label = "Keep revenue over 400"
      Steps = [ Filter(Binary(Gt, Col "revenue", Lit(Cell.Float 400.0))) ] }
    { Id = "running"
      Label = "Window: running revenue per region"
      Steps =
        [ Window
            { PartitionBy = [ "region" ]
              OrderBy = [ "quarter", Asc ]
              Fn = CumulSum
              Of = "revenue"
              As = "running" } ] }
    { Id = "by-region"
      Label = "Group by region: totals"
      Steps =
        [ GroupBy(
            [ "region" ],
            [ { Name = "revenue"
                Fn = AggFn.Sum
                Of = "revenue" }
              { Name = "units"
                Fn = AggFn.Sum
                Of = "units" } ]
          ) ] }
    { Id = "pivot"
      Label = "Pivot: quarters across, revenue summed"
      Steps =
        [ Pivot
            { Index = [ "region" ]
              On = "quarter"
              Values = "revenue"
              Agg = AggFn.Sum } ] }
    { Id = "sort"
      Label = "Sort by revenue, highest first"
      Steps = [ Sort [ Slot.Lit "revenue", Desc ] ] }
    { Id = "runaway"
      Label = sprintf "A runaway: %d generated steps" runawayLength
      Steps = [ for i in 1..runawayLength -> Derive(sprintf "copy_%d" i, Col "units") ] } ]

let stepById (id: string) : Step option =
  palette |> List.tryFind (fun s -> s.Id = id)

/// The pipeline a sequence of palette ids builds, in order.
let pipelineOf (ids: string list) : Transform list =
  ids |> List.choose stepById |> List.collect (fun s -> s.Steps)

// ─── the result grid ─────────────────────────────────────────────────────────

/// The columns of the result. A pivot names its columns from the DATA, so no
/// static walk can know them; the page asks the same evaluator the renderer
/// runs, over the same sixteen rows, for the result's column names and types
/// only. The rows the grid shows are the renderer's own evaluation.
let private resultColumns (pipeline: Transform list) : ColumnErased<obj> list =
  match Budget.check Budget.defaults (Table.rowCount table) (List.length pipeline) with
  | Error _ -> []
  | Ok() ->
    match DataFrame.evalPipeline pipeline table with
    | Error _ -> []
    | Ok result ->
      result.Schema
      |> List.map (fun (field: Field) ->
        let name, ty = field.Name, field.Type
        let numeric = ty = IntType || ty = FloatType

        { Defaults.columnErased<obj> with
            Label = name
            Field = Some name
            Kind =
              if numeric then
                CellKindErased.Numeric
              else
                CellKindErased.Text
            Format =
              if ty = FloatType then
                CellFormat.Number(Some 2, None)
              else
                CellFormat.None })

/// What the grid draws when the renderer refuses its source. A budget refusal
/// and an evaluation error are told apart by the marker the budget's refusals
/// begin with — a contract the renderer names, not a phrase this page guesses.
let private refusal (payload: HostPrelude.ErrorPayload) : Node<obj> =
  let overBudget = payload.Message.StartsWith Budget.RefusalMarker

  Fuaran.callout
    "ps-refusal"
    { Defaults.callout with
        Tone =
          if overBudget then
            ToneVariant.Critical
          else
            ToneVariant.Warning
        Heading =
          Some(
            TextSource.Literal(
              if overBudget then
                "Refused before it ran: over the evaluation budget"
              else
                "Refused: this pipeline cannot run over this table"
            )
          )
        Body = TextSource.Literal payload.Message }

let private emptyResult: Node<obj> =
  Fuaran.callout
    "ps-empty"
    { Defaults.callout with
        Tone = ToneVariant.Info
        Heading = Some(TextSource.Literal "No rows")
        Body =
          TextSource.Literal
            "The pipeline ran and every row was filtered out. That is a result, not a failure: remove the filter, or add it later in the pipeline." }

/// The sandbox's one data-bearing node: a grid whose source is the table plus
/// the visitor's pipeline, both on the wire.
let sandboxGrid (pipeline: Transform list) : Node<obj> =
  let node =
    Fuaran.grid
      "ps-result"
      id
      { Defaults.grid<Row, obj> with
          Source = Binding.Transform(TransformSource.Data(DataSource.Embedded table), pipeline, None) }

  let node =
    match node.Kind with
    | NodeKind.DataGrid g ->
      { node with
          Kind =
            NodeKind.DataGrid
              { g with
                  RowKey = None
                  Columns = resultColumns pipeline } }
    | _ -> node

  node |> Node.onError refusal |> Node.onEmpty emptyResult

// ─── the page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private PivotSandboxView () : ReactElement =
  let chosen, setChosen = React.useState ([]: string list)
  let pipeline = pipelineOf chosen
  let grid = sandboxGrid pipeline
  let wire = CJson.encodeNode grid

  let rows = Table.rowCount table
  let steps = List.length pipeline
  let verdict = Budget.check Budget.defaults rows steps

  let meter (label: string) (used: int64) (limit: int64) =
    let over = used > limit

    Html.div
      [ prop.className (if over then "ps-meter ps-meter-over" else "ps-meter")
        prop.children
          [ Html.span [ prop.className "ps-meter-label"; prop.text label ]
            Html.span [ prop.className "ps-meter-value"; prop.text (sprintf "%d of %d" used limit) ] ] ]

  let removeAt (index: int) =
    setChosen (chosen |> List.indexed |> List.filter (fun (i, _) -> i <> index) |> List.map snd)

  let paletteButtons =
    Html.div
      [ prop.className "ps-palette"
        prop.children
          [ for s in palette ->
              Html.button
                [ prop.className (
                    if s.Id = "runaway" then
                      "ps-add ps-add-runaway"
                    else
                      "ps-add"
                  )
                  prop.text ("+ " + s.Label)
                  prop.onClick (fun _ -> setChosen (chosen @ [ s.Id ])) ] ] ]

  let pipelineList =
    if List.isEmpty chosen then
      Html.p
        [ prop.className "ps-pipeline-empty"
          prop.text
            "No steps yet: the grid shows the sixteen source rows exactly as they sit in the wire. Add a step above." ]
    else
      Html.ol
        [ prop.className "ps-pipeline"
          prop.children
            [ for i, id in List.indexed chosen ->
                let label = stepById id |> Option.map (fun s -> s.Label) |> Option.defaultValue id

                Html.li
                  [ prop.className "ps-step"
                    prop.children
                      [ Html.span [ prop.text label ]
                        Html.button
                          [ prop.className "ps-remove"
                            prop.ariaLabel ("Remove step " + string (i + 1))
                            prop.text "Remove"
                            prop.onClick (fun _ -> removeAt i) ] ] ] ] ]

  let builderPanel =
    Exhibit.panel
      "Build a pipeline"
      "Each button appends a step. Order matters, as it does in any pipeline: a price derived after a group-by is computed from totals, and a pivot after a group-by has nothing left to spread."
      [ paletteButtons
        pipelineList
        Html.div
          [ prop.className "ps-actions"
            prop.children
              [ Html.button
                  [ prop.className "ps-reset"
                    prop.disabled (List.isEmpty chosen)
                    prop.text "Start again"
                    prop.onClick (fun _ -> setChosen []) ] ] ] ]

  let budgetPanel =
    Exhibit.panel
      "What the renderer will spend"
      "Counted in what the tree declares, not in seconds, so a refusal is the same on a phone and on a workstation, and an author can act on it."
      [ Html.div
          [ prop.className "ps-meters"
            prop.children
              [ meter "Rows in" (int64 rows) (int64 Budget.defaults.MaxRows)
                meter "Steps" (int64 steps) (int64 Budget.defaults.MaxSteps)
                meter "Rows × steps" (int64 rows * int64 steps) (int64 Budget.defaults.MaxRowSteps) ] ]
        Html.p
          [ prop.className (
              match verdict with
              | Ok() -> "ps-verdict"
              | Error _ -> "ps-verdict ps-verdict-over"
            )
            prop.text (
              match verdict with
              | Ok() -> "Within budget: the renderer will evaluate this pipeline."
              | Error why -> why
            ) ] ]

  let resultPanel =
    Exhibit.panel
      "The result"
      "Rendered by the shipped DataGrid from the Transform binding below. When the renderer refuses the source, the grid's own error slot says why."
      [ Html.div
          [ prop.className "ps-stage"
            prop.children [ Render.renderWithSources BindingResolver.empty ignore grid ] ]
        Exhibit.wireDrawer (sprintf "The grid's wire: sixteen rows and a %d-step pipeline, as data" steps) wire ]

  Exhibit.shell
    "pivot-sandbox"
    "The Pivot Sandbox"
    "Build a pivot, a derived column or a running total, and watch the renderer compute it. Then add a runaway and watch it refused before it runs. You never write code: every step is data in the tree."
    [ builderPanel; budgetPanel; resultPanel ]
    [ Exhibit.Claim.Verified
        "Every result is evaluated in your browser by the shipped dataframe evaluator, from the Transform in the wire drawer. No step runs a script."
      Exhibit.Claim.Verified
        "The budget limits on the meters are read from the renderer's own budget, and the refusal in the grid is the renderer's message, not this page's."
      Exhibit.Claim.Verified
        "The grid's columns are read from a second evaluation of the same pipeline over the same sixteen rows, because a pivot names its columns from the data. Its rows are the renderer's."
      Exhibit.Claim.Limit
        "The table is deliberately tiny. Joins, wide group-bys, pivots and windows over large tables are slow in a browser today, and this page does not show them at scale." ]

let page: ReactElement = PivotSandboxView()
