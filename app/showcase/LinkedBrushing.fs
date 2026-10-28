module Fuaran.Showcase.LinkedBrushing

// ============================================================================
//  Linked Brushing — click a bar, and every other view filters (Phase 1887).
//
//  The claim is that cross-filtering is DECLARED, not coded. Four views read one
//  inline table. Each view's data is a `Binding.Transform`: the table, a
//  pipeline, and named params. A `Filter` step compares a column against a
//  param, and the param reads `Binding.Selection` off the other chart, so a
//  click publishes a row and every reader re-derives. Nothing on this page
//  handles the click: the renderer's point-selection default publishes the
//  clicked row under the chart's own node id (Phase 933), and an UNBOUND param
//  prunes its filter step, so "nothing selected" means "every row".
//
//  The page code builds the tree once and renders it. Its only behaviour of its
//  own is the two "clear" buttons, which remove a published selection — the one
//  act the vocabulary leaves to the host.
//
//  Data is deterministic and inline: four regions, three products, four
//  quarters, forty-eight rows computed from three small factor tables. Nothing
//  is fetched.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── the inline table ────────────────────────────────────────────────────────

let private regions = [ "North", 120; "South", 95; "East", 140; "West", 80 ]
let private products = [ "Tea", 100; "Coffee", 160; "Cocoa", 70 ]
let private quarters = [ "Q1", 80; "Q2", 100; "Q3", 110; "Q4", 140 ]

/// One row per region x product x quarter. Units are the region's scale times
/// two percentages (product and season), in integer arithmetic so every host
/// and every reader gets the same number; revenue is units at a per-product
/// price.
let private facts: (string * string * string * int * float) list =
  [ for region, scale in regions do
      for product, mult in products do
        for quarter, season in quarters do
          let units = scale * mult * season / 10000

          let price =
            if product = "Coffee" then 4.0
            elif product = "Tea" then 3.0
            else 5.0

          region, product, quarter, units, float units * price ]

/// The inline table every view reads, as a real columnar `Fuaran.Core.Table`.
let table: Table =
  let col build name values =
    build name (Vector.ofList values) AllValid

  { Schema =
      [ Field.create "region" StringType
        Field.create "product" StringType
        Field.create "quarter" StringType
        Field.create "units" IntType
        Field.create "revenue" FloatType ]
    Columns =
      [ col Fuaran.Core.Column.ofStrs "region" [ for r, _, _, _, _ in facts -> r ]
        col Fuaran.Core.Column.ofStrs "product" [ for _, p, _, _, _ in facts -> p ]
        col Fuaran.Core.Column.ofStrs "quarter" [ for _, _, q, _, _ in facts -> q ]
        col Fuaran.Core.Column.ofInts "units" [ for _, _, _, u, _ in facts -> u ]
        col Fuaran.Core.Column.ofFloats "revenue" [ for _, _, _, _, v in facts -> v ] ] }

// ─── the declarations ────────────────────────────────────────────────────────

/// The two charts whose clicks publish a selection.
[<Literal>]
let RegionChartId = "lb-by-region"

[<Literal>]
let ProductChartId = "lb-by-product"

/// A filter step keyed on a param: `column = param`. Unbound, the renderer
/// prunes it, which is what makes "nothing selected" show every row.
let private matches (column: string) : Transform =
  Filter(Binary(Eq, Col column, Param column))

/// The param that reads one field of the row a chart published.
let private selected (chartId: string) (field: string) : TransformParam =
  { Name = field
    From = (binding.selectionField chartId field None: Binding<JVal>) }

let private totalsBy (keys: string list) : Transform =
  GroupBy(
    keys,
    [ { Name = "revenue"
        Fn = AggFn.Sum
        Of = "revenue" }
      { Name = "units"
        Fn = AggFn.Sum
        Of = "units" } ]
  )

/// The source every view declares: the inline table, its pipeline, its params.
let private view (pipeline: Transform list) (parameters: TransformParam list) : Binding<Row seq> =
  Binding.Transform(TransformSource.Data(DataSource.Embedded table), pipeline, Some parameters)

let private title (text: string) = Some(TextSource.Literal text)

/// Revenue by region — filtered by the PRODUCT the other chart published.
let regionChart: Node<obj> =
  Fuaran.chart
    RegionChartId
    { Defaults.chart<obj> with
        Kind = ChartKind.Bar
        Source =
          view
            [ matches "product"; totalsBy [ "region" ]; Sort [ Slot.Lit "region", Asc ] ]
            [ selected ProductChartId "product" ]
        XField = "region"
        YFields = [ "revenue" ]
        Title = title "Revenue by region" }

/// Revenue by product — filtered by the REGION the other chart published.
let productChart: Node<obj> =
  Fuaran.chart
    ProductChartId
    { Defaults.chart<obj> with
        Kind = ChartKind.Bar
        Source =
          view
            [ matches "region"; totalsBy [ "product" ]; Sort [ Slot.Lit "product", Asc ] ]
            [ selected RegionChartId "region" ]
        XField = "product"
        YFields = [ "revenue" ]
        Title = title "Revenue by product" }

/// Revenue by quarter — filtered by BOTH selections.
let quarterChart: Node<obj> =
  Fuaran.chart
    "lb-by-quarter"
    { Defaults.chart<obj> with
        Kind = ChartKind.Line
        Source =
          view
            [ matches "region"
              matches "product"
              totalsBy [ "quarter" ]
              Sort [ Slot.Lit "quarter", Asc ] ]
            [ selected RegionChartId "region"; selected ProductChartId "product" ]
        XField = "quarter"
        YFields = [ "revenue" ]
        Title = title "Revenue by quarter" }

/// A grid column read by FIELD — a declaration that survives the wire, where a
/// closure accessor would not.
let private fieldColumn (label: string) (field: string) (kind: CellKindErased<obj>) (format: CellFormat) =
  { Defaults.columnErased<obj> with
      Label = label
      Field = Some field
      Kind = kind
      Format = format }

/// The detail grid — region x product totals, filtered by BOTH selections. It is
/// built through the typed facade and then given field-read columns, so every
/// column is a name on the wire rather than a function in this page.
let detailGrid: Node<obj> =
  let node =
    Fuaran.grid
      "lb-detail"
      id
      { Defaults.grid<Row, obj> with
          Source =
            view
              [ matches "region"
                matches "product"
                totalsBy [ "region"; "product" ]
                Sort [ Slot.Lit "region", Asc; Slot.Lit "product", Asc ] ]
              [ selected RegionChartId "region"; selected ProductChartId "product" ] }

  match node.Kind with
  | NodeKind.DataGrid g ->
    { node with
        Kind =
          NodeKind.DataGrid
            { g with
                RowKey = None
                Columns =
                  [ fieldColumn "Region" "region" CellKindErased.Text CellFormat.None
                    fieldColumn "Product" "product" CellKindErased.Text CellFormat.None
                    fieldColumn "Units" "units" CellKindErased.Numeric (CellFormat.Number(Some 0))
                    fieldColumn "Revenue" "revenue" CellKindErased.Numeric (CellFormat.Number(Some 0)) ] } }
  | _ -> node

/// The whole dashboard, one tree: what the wire drawer shows and the renderer draws.
let dashboard: Node<obj> =
  Fuaran.gridLayoutTemplated
    "lb-dashboard"
    "repeat(auto-fit, minmax(min(100%, 22rem), 1fr))"
    { Defaults.gridLayout<obj> with
        Children = [ regionChart; productChart; quarterChart; detailGrid ] }

let private wire: string = CJson.encodeNode dashboard

// ─── the page ────────────────────────────────────────────────────────────────

/// The field a chart's published row carries, if it has published one.
let private publishedField (chartId: string) (field: string) : string option =
  SelectionStore.get chartId
  |> Option.bind (fun row -> Map.tryFind field (unbox<Row> row))
  |> Option.map string

[<ReactComponent>]
let private LinkedBrushingView () : ReactElement =
  // Subscribe the chrome (the "selected" chips) to the same two producers the
  // tree reads, so the chips and the views move together.
  SelectionStore.useSelectionKeys (Set.ofList [ RegionChartId; ProductChartId ])
  |> ignore

  let region = publishedField RegionChartId "region"
  let product = publishedField ProductChartId "product"

  let chip (label: string) (value: string option) (chartId: string) =
    Html.div
      [ prop.className "lb-chip"
        prop.children
          [ Html.span [ prop.className "lb-chip-label"; prop.text label ]
            Html.span
              [ prop.className (
                  if value.IsSome then
                    "lb-chip-value"
                  else
                    "lb-chip-value lb-chip-none"
                )
                prop.text (defaultArg value "every row") ]
            Html.button
              [ prop.className "lb-clear"
                prop.disabled value.IsNone
                prop.text "Clear"
                prop.onClick (fun _ -> SelectionStore.clear chartId) ] ] ]

  let brushPanel =
    Exhibit.panel
      "Click a bar"
      "Click a region bar or a product bar. Each chart filters the other, and the quarter line and the detail grid follow both. Click another bar to move the selection; clear it to see every row again."
      [ Html.div
          [ prop.className "lb-chips"
            prop.children [ chip "Region" region RegionChartId; chip "Product" product ProductChartId ] ]
        Html.div
          [ prop.className "lb-stage"
            prop.children [ Render.renderStateReactive BindingResolver.empty ignore dashboard ] ] ]

  let point (text: string) = Html.li [ prop.text text ]

  let declaredPanel =
    Exhibit.panel
      "Where the wiring lives"
      "Open the wire. Each view's source is a Transform: the table, a pipeline, and params. A params entry reads a Selection on the other chart's node id, and a filter step compares a column with that param. That is the whole coordination."
      [ Html.ul
          [ prop.className "px-points"
            prop.children
              [ point
                  "A chart with no click handler of its own publishes the row you clicked under its own node id. That is the renderer's default, so a decoded chart from any emitter does it too."
                point
                  "A param that nothing has published to is unbound, and the renderer drops the filter step that reads it. So 'nothing selected' is not a special case anyone wrote: it is every row."
                point
                  "Each chart reads only the OTHER chart's selection, which is why clicking a region narrows the products without collapsing the region chart to one bar." ] ]
        Exhibit.wireDrawer "The dashboard's wire: four views, one table, two selections" wire ]

  Exhibit.shell
    "linked-brushing"
    "Linked Brushing"
    "Click a bar and every view filters. No view is told about another: each declares a filter on what the other chart publishes, and the renderer does the rest."
    [ brushPanel; declaredPanel ]
    [ Exhibit.Claim.Verified
        "Every number is computed in your browser by the shipped dataframe evaluator, from the forty-eight inline rows in the wire. Nothing is fetched and nothing is pre-aggregated."
      Exhibit.Claim.Verified
        "The page attaches no click handler to any chart. The selection you see in the chips is read back from the same store the views read."
      Exhibit.Claim.Limit
        "Clearing a selection is a host act: the vocabulary publishes a click but has no 'unselect' gesture, so the two Clear buttons are this page's own code."
      Exhibit.Claim.Limit
        "This is point selection, not a dragged range. A brushed range is not in the vocabulary yet, and the page does not imitate one." ]

let page: ReactElement = LinkedBrushingView()
