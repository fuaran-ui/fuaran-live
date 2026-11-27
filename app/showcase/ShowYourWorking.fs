module Fuaran.Showcase.ShowYourWorking

// ============================================================================
//  Show Your Working — click a number and step through how it was computed
//  (Phase 2756).
//
//  The claim: a number on a Fuaran page can account for itself, because the
//  computation travelled with the view. Every figure on this dashboard is a
//  `Metric` whose value is a `Binding.Transform`: the twelve orders below,
//  embedded in the tree, and a pipeline of dataframe steps over them, both on
//  the wire. Nothing is fetched and nothing is queried; there is no other place
//  the number could have come from.
//
//  The stepped view is the language tier's derivation trace (`Derivation.trace`,
//  through the resolver's own sources: `BindingResolver.derivationTrace`). It
//  lists every step the binding carries by its wire name, the columns the static
//  schema walk derives after it, and the table after it, evaluated by the same
//  reference evaluator the renderer runs. The trace evaluates each step's PREFIX
//  of the pipeline, so its last table is the binding's value by construction;
//  test/showYourWorking.test.ts holds it to the number the renderer resolves for
//  the slot, figure by figure.
//
//  The headline figures are rendered by the shipped renderer with its own
//  derivation disclosure switched on (`RenderPolicy.DerivationTrace`), so the
//  "How this was computed" under each one is the renderer's, not this page's.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── the data ────────────────────────────────────────────────────────────────

/// Twelve orders: an id, a region, a product, the units ordered and the unit
/// price. Deterministic and inline; every figure on the page reads this table
/// and nothing else.
let orderRows: (string * string * string * int * float) list =
  [ "A-101", "North", "Tea", 24, 3.5
    "A-102", "North", "Coffee", 12, 4.25
    "A-103", "North", "Cocoa", 6, 5.0
    "A-104", "South", "Tea", 18, 3.5
    "A-105", "South", "Coffee", 30, 4.25
    "A-106", "South", "Cocoa", 9, 5.0
    "A-107", "East", "Tea", 40, 3.5
    "A-108", "East", "Coffee", 22, 4.25
    "A-109", "East", "Cocoa", 15, 5.0
    "A-110", "West", "Tea", 8, 3.5
    "A-111", "West", "Coffee", 16, 4.25
    "A-112", "West", "Cocoa", 20, 5.0 ]

let orders: Table =
  let col build name values =
    build name (Vector.ofList values) AllValid

  { Schema =
      [ Field.create "order" StringType
        Field.create "region" StringType
        Field.create "product" StringType
        Field.create "units" IntType
        Field.create "price" FloatType ]
    Columns =
      [ col Fuaran.Core.Column.ofStrs "order" [ for o, _, _, _, _ in orderRows -> o ]
        col Fuaran.Core.Column.ofStrs "region" [ for _, r, _, _, _ in orderRows -> r ]
        col Fuaran.Core.Column.ofStrs "product" [ for _, _, p, _, _ in orderRows -> p ]
        col Fuaran.Core.Column.ofInts "units" [ for _, _, _, u, _ in orderRows -> u ]
        col Fuaran.Core.Column.ofFloats "price" [ for _, _, _, _, p in orderRows -> p ] ] }

/// The regions, in the order the breakdown draws them.
let regions: string list = [ "North"; "South"; "East"; "West" ]

/// The units an order of at least this many counts as large.
let largeOrderUnits: int = 20

// ─── the pipelines ───────────────────────────────────────────────────────────

let private revenueStep: Transform =
  Derive("revenue", Binary(Mul, Cast(FloatType, Col "units"), Col "price"))

let private inRegion (region: string) : Transform =
  Filter(Binary(Eq, Col "region", Lit(Cell.Str region)))

let private total (name: string) (fn: AggFn) (column: string) : Transform =
  GroupBy([], [ { Name = name; Fn = fn; Of = column } ])

let private gbp: CellFormat = CellFormat.Currency("GBP", Some 2, None)
let private whole: CellFormat = CellFormat.Number(Some 0, None)

/// One number on the page: where it sits, what it is called, how it is
/// formatted, and the pipeline that computes it from the orders.
type Figure =
  {
    Id: string
    Label: string
    /// `headline` for the four figures across the top, `cell` for the breakdown.
    Group: string
    Format: CellFormat
    Pipeline: Transform list
  }

let private headline (id: string) (label: string) (format: CellFormat) (pipeline: Transform list) : Figure =
  { Id = id
    Label = label
    Group = "headline"
    Format = format
    Pipeline = pipeline }

let private cell (region: string) (measure: string) : Figure =
  let pipeline, format =
    match measure with
    | "units" -> [ inRegion region; total "units" AggFn.Sum "units" ], whole
    | _ -> [ revenueStep; inRegion region; total "revenue" AggFn.Sum "revenue" ], gbp

  { Id = region.ToLowerInvariant() + "-" + measure
    Label = region + " " + measure
    Group = "cell"
    Format = format
    Pipeline = pipeline }

/// Every number on the page, headline figures first, then the breakdown by row.
let figures: Figure list =
  [ headline "revenue" "Revenue" gbp [ revenueStep; total "revenue" AggFn.Sum "revenue" ]
    headline
      "large-orders"
      (sprintf "Orders of %d units or more" largeOrderUnits)
      whole
      [ Filter(Binary(Ge, Col "units", Lit(Cell.Int largeOrderUnits)))
        total "orders" AggFn.Count "order" ]
    headline "average-order" "Average order value" gbp [ revenueStep; total "average" AggFn.Mean "revenue" ]
    headline
      "best-region"
      "Best region's revenue"
      gbp
      [ revenueStep
        GroupBy(
          [ "region" ],
          [ { Name = "revenue"
              Fn = AggFn.Sum
              Of = "revenue" } ]
        )
        Sort [ Slot.Lit "revenue", Desc ]
        Limit(Slot.Lit 1, Slot.Lit 0)
        Project [ "revenue", "revenue" ] ]
    for region in regions do
      cell region "units"
      cell region "revenue" ]

let figureById (id: string) : Figure option =
  figures |> List.tryFind (fun f -> f.Id = id)

/// The figure's value binding: the orders embedded in the tree, and its pipeline.
let binding (figure: Figure) : Binding<float> =
  Binding.Transform(TransformSource.Data(DataSource.Embedded orders), figure.Pipeline, None)

/// The figure as the node the page renders: a `Metric` over its binding.
let node (figure: Figure) : Node<obj> =
  Fuaran.metric
    ("syw-" + figure.Id)
    { Defaults.metric with
        Label = TextSource.Literal figure.Label
        Value = binding figure
        Format = figure.Format }

// ─── what the page reads from the tier ───────────────────────────────────────

/// The value the renderer resolves for the figure's slot: the number on screen.
let screenValue (figure: Figure) : Result<float, string> =
  match BindingResolver.resolveScalarFloat BindingResolver.empty (binding figure) with
  | BindingResolver.Resolved v -> Ok v
  | BindingResolver.Errored m -> Error m
  | BindingResolver.NotResolved -> Error "the value did not resolve"
  | BindingResolver.I18nUnresolved key -> Error("an unresolved message key: " + key)

/// The number as the page shows it, through the renderer's own number format.
let screenText (figure: Figure) : string =
  match screenValue figure with
  | Ok v -> Formatting.formatNumber figure.Format v
  | Error m -> m

/// How many rows of each table the stepped view and the disclosure show.
let rowLimit: int = 12

/// How the figure was computed: the trace, with the table after every step.
let trace (figure: Figure) : Result<Derivation.DerivationTrace, Derivation.DerivationRefusal> =
  BindingResolver.derivationTrace (Some rowLimit) BindingResolver.empty (binding figure)

/// The trace's canonical text (the tier's WIRE_FORMAT §35 form).
let traceWire (figure: Figure) : string =
  match trace figure with
  | Ok t -> Derivation.encode t
  | Error refusal -> Derivation.DerivationRefusal.describe refusal

/// The orders as the canonical bytes they are embedded as.
let ordersWire: string =
  Canonical.write Rendering.CrossHost (ColumnCodec.encodeJson (DataSource.Embedded orders))

// ─── the empty state ─────────────────────────────────────────────────────────

let private headlineCount =
  figures |> List.filter (fun f -> f.Group = "headline") |> List.length

let private cellCount =
  figures |> List.filter (fun f -> f.Group = "cell") |> List.length

/// What the derivation panel says before the first click.
let emptyStateHeading: string = "Pick any number above"

let emptyStateBody: string =
  sprintf
    "Nothing is selected yet. Each of the %d headline figures and the %d cells of the breakdown is a pipeline over the %d orders on the right. Choose one and this panel walks its steps in order: what each step is called on the wire, the columns it leaves, and the table after it, down to the one cell that is the number you clicked."
    headlineCount
    cellCount
    (List.length orderRows)

let emptyWireNote: string =
  "Before you choose a number, this panel shows the only data the page holds: the twelve orders, as the canonical bytes every pipeline reads. Choose a number and it shows that figure's whole node, data and pipeline together."

// ─── drawing a stage ─────────────────────────────────────────────────────────

let private cellText (c: Cell) : string =
  match BindingResolver.cellToText c with
  | Ok text -> text
  | Error _ -> ""

let private schemaColumns (schema: SchemaKnowledge) : ReactElement =
  Html.ul
    [ prop.className "syw-columns"
      prop.children
        [ for c in SchemaWalk.columns schema ->
            Html.li
              [ prop.className "syw-column"
                prop.children
                  [ Html.code [ prop.text c.Name ]
                    match c.Type with
                    | Some t -> Html.span [ prop.className "syw-column-type"; prop.text (ColumnType.tag t) ]
                    | None -> Html.none ] ] ] ]

let private stageTable (table: Derivation.StageTable) : ReactElement =
  let names = Table.columnNames table.Rows

  let columns = names |> List.choose (fun n -> Table.tryColumn n table.Rows)

  let shown = Table.rowCount table.Rows

  Html.div
    [ prop.className "syw-table-wrap"
      prop.children
        [ Html.p
            [ prop.className "syw-rowcount"
              prop.text (
                if shown < table.RowCount then
                  sprintf "%d rows; the first %d shown" table.RowCount shown
                elif table.RowCount = 1 then
                  "1 row"
                else
                  sprintf "%d rows" table.RowCount
              ) ]
          Html.table
            [ prop.className "syw-table"
              prop.children
                [ Html.thead [ Html.tr [ for n in names -> Html.th [ prop.scope "col"; prop.text n ] ] ]
                  Html.tbody
                    [ for i in 0 .. shown - 1 ->
                        Html.tr
                          [ for col in columns -> Html.td [ prop.text (cellText (Fuaran.Core.Column.cell i col)) ] ] ] ] ] ] ]

let private stageBody (stage: Derivation.DerivationStage) : ReactElement list =
  [ Html.h5 [ prop.className "syw-subhead"; prop.text "Columns after it" ]
    schemaColumns stage.Schema
    Html.h5 [ prop.className "syw-subhead"; prop.text "The table after it" ]
    match stage.Table with
    | Some(Ok table) -> stageTable table
    | Some(Error refusal) -> Html.p [ prop.className "syw-refusal"; prop.text refusal ]
    | None -> Html.none ]

/// One entry of the step list: index 0 is the input, index `i` the `i`th step.
let private stageName (t: Derivation.DerivationTrace) (index: int) : string =
  if index = 0 then
    "input"
  else
    let step = t.Steps[index - 1]

    match step.Status with
    | Derivation.StepStatus.Applied -> step.Verb
    | Derivation.StepStatus.Pruned _ -> step.Verb + " (not run)"

// ─── the page ────────────────────────────────────────────────────────────────

let private disclosurePolicy: RenderPolicy =
  { RenderPolicy.defaults with
      DerivationTrace = DerivationTracePolicy.Show rowLimit }

/// A headline figure, rendered by the shipped renderer with its derivation
/// disclosure on.
let private renderHeadline (figure: Figure) : ReactElement =
  let n = node figure

  Render.render
    { Render.RenderContext.create BindingResolver.empty ignore n with
        Runtime = Exhibit.runtime
        Policy = disclosurePolicy }
    n

[<ReactComponent>]
let private ShowYourWorkingView () : ReactElement =
  let selected, setSelected = React.useState (None: string option)
  let stage, setStage = React.useState 0

  let choose (id: string) =
    setSelected (Some id)
    setStage 0

  let chosen = selected |> Option.bind figureById

  let chooser (figure: Figure) (content: ReactElement list) (className: string) =
    let on = selected = Some figure.Id

    Html.button
      [ prop.className (if on then className + " syw-chosen" else className)
        prop.ariaPressed on
        prop.ariaLabel ("Show the working for " + figure.Label + ": " + screenText figure)
        prop.onClick (fun _ -> choose figure.Id)
        prop.children content ]

  let headlinePanel =
    Exhibit.panel
      "The dashboard"
      "Four figures over the same twelve orders. Each is a Metric whose value is a pipeline in the tree. Under each, the renderer's own disclosure lists the steps; the button above it opens the stepped view below."
      [ Html.div
          [ prop.className "syw-headlines"
            prop.children
              [ for f in figures |> List.filter (fun f -> f.Group = "headline") ->
                  Html.div
                    [ prop.className "syw-headline"
                      prop.children
                        [ Html.div [ prop.className "syw-stage"; prop.children [ renderHeadline f ] ]
                          chooser f [ Html.text "Show the working" ] "syw-choose" ] ] ] ] ]

  let breakdownPanel =
    let cellFor region measure =
      figures
      |> List.tryFind (fun f -> f.Id = (region: string).ToLowerInvariant() + "-" + measure)

    Exhibit.panel
      "By region"
      "Every cell is its own figure: the same orders, filtered to one region and totalled. Click a cell to see its working."
      [ Html.table
          [ prop.className "syw-breakdown"
            prop.children
              [ Html.thead
                  [ Html.tr
                      [ Html.th [ prop.scope "col"; prop.text "Region" ]
                        Html.th [ prop.scope "col"; prop.text "Units" ]
                        Html.th [ prop.scope "col"; prop.text "Revenue" ] ] ]
                Html.tbody
                  [ for region in regions ->
                      Html.tr
                        [ yield Html.th [ prop.scope "row"; prop.text region ]
                          for measure in [ "units"; "revenue" ] do
                            yield
                              Html.td
                                [ match cellFor region measure with
                                  | Some f -> chooser f [ Html.text (screenText f) ] "syw-cell"
                                  | None -> Html.none ] ] ] ] ] ]

  let derivationPanel =
    match chosen with
    | None ->
      Exhibit.panel
        "How it was computed"
        ""
        [ Html.div
            [ prop.className "syw-empty"
              prop.children
                [ Html.p [ prop.className "syw-empty-heading"; prop.text emptyStateHeading ]
                  Html.p [ prop.text emptyStateBody ] ] ] ]
    | Some figure ->
      match trace figure with
      | Error refusal ->
        Exhibit.panel
          ("How " + figure.Label + " was computed")
          ""
          [ Html.p
              [ prop.className "syw-refusal"
                prop.text (Derivation.DerivationRefusal.describe refusal) ] ]
      | Ok t ->
        let last = List.length t.Steps
        let at = min stage last

        let current = if at = 0 then t.Input else t.Steps[at - 1].Output

        let stageTitle =
          Html.h4
            [ prop.className "syw-stage-title"
              prop.text (
                if at = 0 then
                  "The input: the orders as embedded"
                else
                  sprintf "Step %d of %d: %s" at last (stageName t at)
              ) ]

        let stepWire =
          if at = 0 then
            Html.none
          else
            Html.pre
              [ prop.className "syw-step-wire"
                prop.children [ Html.code [ prop.text (Canonical.write Rendering.CrossHost t.Steps[at - 1].Wire) ] ] ]

        let resultLine =
          if at = last then
            Html.p
              [ prop.className "syw-result"
                prop.text ("The one cell left is the number on the page: " + screenText figure) ]
          else
            Html.none

        let atStart = at = 0
        let atEnd = at = last

        let nav =
          Html.div
            [ prop.className "syw-nav"
              prop.children
                [ Html.button
                    [ prop.className "syw-nav-button"
                      prop.disabled atStart
                      prop.text "Previous step"
                      prop.onClick (fun _ -> setStage (at - 1)) ]
                  Html.button
                    [ prop.className "syw-nav-button"
                      prop.disabled atEnd
                      prop.text "Next step"
                      prop.onClick (fun _ -> setStage (at + 1)) ] ] ]

        let stepList =
          Html.ol
            [ prop.className "syw-steps"
              prop.children
                [ for i in 0..last ->
                    Html.li
                      [ Html.button
                          [ prop.className (if i = at then "syw-step syw-step-on" else "syw-step")
                            prop.custom ("aria-current", (if i = at then "step" else "false"))
                            prop.onClick (fun _ -> setStage i)
                            prop.text (stageName t i) ] ] ] ]

        Exhibit.panel
          ("How " + figure.Label + " was computed")
          (sprintf
            "%d steps over the %d orders. Step through them, or pick one from the list."
            last
            (List.length orderRows))
          [ Html.div
              [ prop.className "syw-derivation"
                prop.children
                  [ stepList
                    Html.div
                      [ prop.className "syw-stage-view"
                        prop.children ([ stageTitle; stepWire ] @ stageBody current @ [ resultLine; nav ]) ] ] ] ]

  let wirePanel =
    match chosen with
    | None ->
      Exhibit.panel
        "The wire it was built from"
        emptyWireNote
        [ Html.pre
            [ prop.className "syw-wire"
              prop.children [ Html.code [ prop.text ordersWire ] ] ] ]
    | Some figure ->
      Exhibit.panel
        "The wire it was built from"
        (figure.Label
         + " is this node: the orders and the pipeline, in canonical bytes. There is no query anywhere else on the page.")
        [ Html.pre
            [ prop.className "syw-wire"
              prop.children [ Html.code [ prop.text (CJson.encodeNode (node figure)) ] ] ]
          Exhibit.wireDrawer "The derivation itself, as canonical bytes" (traceWire figure) ]

  Exhibit.shell
    "show-your-working"
    "Show Your Working"
    "Click any number on this dashboard and step through how it was computed: each step by name, the columns it leaves and the table after it. The computation travelled with the view, so the page can answer without asking anything."
    [ headlinePanel
      breakdownPanel
      Html.div [ prop.className "syw-split"; prop.children [ derivationPanel; wirePanel ] ] ]
    [ Exhibit.Claim.Verified
        "Every number is a Metric whose value is a pipeline over the twelve orders in its own node. The wire panel shows that node's bytes; nothing is fetched and no model is called."
      Exhibit.Claim.Verified
        "The stepped view is the language tier's derivation trace, evaluated in your browser by the same evaluator the renderer runs. Its last table is the figure's value, and the test suite holds it to the number the renderer resolves, figure by figure."
      Exhibit.Claim.Verified
        "The disclosure under each headline figure is the renderer's own, switched on through its render policy, not drawn by this page."
      Exhibit.Claim.Limit
        "The table is twelve rows on purpose, so every step's table fits on screen. A trace over a large table shows the first rows of each step and says how many it left out." ]

let page: ReactElement = ShowYourWorkingView()
