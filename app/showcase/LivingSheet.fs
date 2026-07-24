module Fuaran.Showcase.LivingSheet

// ============================================================================
//  The Living Sheet – the formulas are on the wire. Pillar: "one wire, many
//  worlds".
//
//  Every number in the dashboard is computed live by a declarative transform
//  pipeline (derive a margin column → filter by a threshold → group by region
//  with aggregates) run by the REAL `Fuaran.Core.DataFrame` reference evaluator,
//  compiled to JavaScript via Fable. Edit a source cell or drag the threshold and
//  the whole dashboard recomputes in-browser – no server, no spreadsheet engine.
//
//  The kicker: the pipeline and its source are themselves canonical wire data.
//  "Show the wire" reveals the DataSource + Transform list as JSON – the
//  *computation* is data, not code. A generic dashboard ships a screenshot of a
//  number; a Fuaran app ships the number's derivation, portable and re-runnable.
//  The same evaluator is certified byte-identical across F# / TypeScript / Python
//  (the cross-host parity contract), so the compute travels with the app.
//
//  Honest scope (stated in the footer): the evaluator, the pipeline codec, and the
//  source codec are the real shipped `Fuaran.Core.DataFrame` / `.Column` surfaces
//  (FSharp.Core-only, Fable-clean) – this page runs them directly. The transform
//  algebra shown is the actual v1 verb set; the JSON in the drawer is the actual
//  canonical wire the codecs emit. Nothing here needs a server.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module DF = Fuaran.Core.DataFrame

[<Emit("Number($0)")>]
let private jsNumber (s: string) : float = jsNative

[<Emit("Number.isFinite($0)")>]
let private jsFinite (n: float) : bool = jsNative

[<Emit("$0.toLocaleString('en-GB',{maximumFractionDigits:0})")>]
let private locale (n: float) : string = jsNative

let private renderNode (n: Node<unit>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

// ─── The editable source (a tiny sales table) ────────────────────────────────

type private Row =
  { Region: string
    Product: string
    Units: int
    Revenue: float
    Cost: float }

let private initialRows: Row list =
  [ { Region = "North"
      Product = "Widget"
      Units = 120
      Revenue = 4800.0
      Cost = 2600.0 }
    { Region = "North"
      Product = "Gadget"
      Units = 80
      Revenue = 5200.0
      Cost = 3100.0 }
    { Region = "South"
      Product = "Widget"
      Units = 60
      Revenue = 2400.0
      Cost = 1300.0 }
    { Region = "South"
      Product = "Gadget"
      Units = 140
      Revenue = 9100.0
      Cost = 5400.0 }
    { Region = "East"
      Product = "Widget"
      Units = 90
      Revenue = 3600.0
      Cost = 1950.0 }
    { Region = "East"
      Product = "Gadget"
      Units = 50
      Revenue = 3250.0
      Cost = 1900.0 } ]

/// Build the real `Fuaran.Core.Table` (columnar, null-aware) from the editable rows.
let private sourceTable (rows: Row list) : Table =
  { Schema =
      [ "region", StringType
        "product", StringType
        "units", IntType
        "revenue", FloatType
        "cost", FloatType ]
    Columns =
      [ { Name = "region"
          Type = StringType
          Cells = rows |> List.map (fun r -> Cell.Str r.Region) }
        { Name = "product"
          Type = StringType
          Cells = rows |> List.map (fun r -> Cell.Str r.Product) }
        { Name = "units"
          Type = IntType
          Cells = rows |> List.map (fun r -> Cell.Int r.Units) }
        { Name = "revenue"
          Type = FloatType
          Cells = rows |> List.map (fun r -> Cell.Float r.Revenue) }
        { Name = "cost"
          Type = FloatType
          Cells = rows |> List.map (fun r -> Cell.Float r.Cost) } ] }

// ─── The declarative pipeline (the real Transform algebra) ───────────────────

/// margin = (revenue − cost) / revenue – a derived column, as data.
let private marginExpr: ColExpr =
  Binary(Div, Binary(Sub, Col "revenue", Col "cost"), Col "revenue")

/// The three toggleable steps. `Filter` compares `units` against the live
/// `minUnits` param (param binding – the reactive edge is derived from
/// the expression, never separately declared). `GroupBy`'s margin aggregate only
/// exists when the derive step is on (else the column is absent – honest).
let private buildPipeline (derive: bool) (filter: bool) (group: bool) : Transform list =
  [ if derive then
      Derive("margin", marginExpr)
    if filter then
      Filter(Binary(Ge, Col "units", Param "minUnits"))
    if group then
      let aggs =
        [ { Name = "revenue"
            Fn = Sum
            Of = "revenue" }
          { Name = "units"
            Fn = Sum
            Of = "units" } ]
        @ (if derive then
             [ { Name = "margin"
                 Fn = Mean
                 Of = "margin" } ]
           else
             [])

      GroupBy([ "region" ], aggs) ]

// ─── Reading a computed table back ───────────────────────────────────────────

let private colByName (t: Table) (name: string) : Column option =
  t.Columns |> List.tryFind (fun c -> c.Name = name)

let private rowCount (t: Table) : int =
  match t.Columns with
  | [] -> 0
  | c :: _ -> List.length c.Cells

let private cellAt (t: Table) (name: string) (i: int) : Cell =
  match colByName t name with
  | Some c when i < List.length c.Cells -> List.item i c.Cells
  | _ -> Cell.Null

let private cellFloat (c: Cell) : float =
  match c with
  | Cell.Float f -> f
  | Cell.Int i -> float i
  | _ -> 0.0

let private cellText (c: Cell) : string =
  match c with
  | Cell.Int i -> string i
  | Cell.Float f -> sprintf "%g" (System.Math.Round(f, 3))
  | Cell.Str s -> s
  | Cell.Bool b -> (if b then "true" else "false")
  | Cell.Date s
  | Cell.Timestamp s -> s
  | Cell.Null -> "–"

// ─── The payoff: the computed table rendered as a Fuaran dashboard ───────────

let private kpiCard (id: string) (label: string) (value: string) : Node<unit> =
  Fuaran.card
    id
    { Defaults.card with
        Heading = Some(TextSource.Literal label)
        Children = [ Fuaran.markdown (id + "-v") value ] }

let private dashboardNode (out: Table) : Node<unit> =
  let n = rowCount out
  let hasMargin = (colByName out "margin").IsSome

  Fuaran.box
    "ls-dash"
    { Layout =
        BoxLayout.Flex
          { Direction = Vertical
            Wrap = false
            Gap = Some 12 }
      Role = BoxRole.Dashboard
      Heading = Some(TextSource.Literal "Regional performance – computed live")
      Children =
        [ for i in 0 .. n - 1 do
            let region = cellText (cellAt out "region" i)
            let revenue = "£" + locale (cellFloat (cellAt out "revenue" i))
            let units = cellText (cellAt out "units" i)

            let cards =
              [ kpiCard (sprintf "ls-r%d-rev" i) "Revenue" revenue
                kpiCard (sprintf "ls-r%d-units" i) "Units" units ]
              @ (if hasMargin then
                   [ kpiCard
                       (sprintf "ls-r%d-margin" i)
                       "Avg margin"
                       (sprintf "%.1f%%" (cellFloat (cellAt out "margin" i) * 100.0)) ]
                 else
                   [])

            Fuaran.box
              (sprintf "ls-region-%d" i)
              { Layout =
                  BoxLayout.Flex
                    { Direction = Horizontal
                      Wrap = true
                      Gap = Some 10 }
                Role = BoxRole.Group
                Heading = Some(TextSource.Literal region)
                Children = cards } ] }

/// A generic HTML sheet of any computed table (used when the pipeline is not
/// grouped – the raw derived/filtered rows).
let private sheetTable (t: Table) : ReactElement =
  let names = t.Schema |> List.map fst
  let n = rowCount t

  Html.div
    [ prop.className "ls-sheet-wrap"
      prop.children
        [ Html.table
            [ prop.className "ls-sheet"
              prop.children
                [ Html.thead [ Html.tr [ prop.children [ for nm in names -> Html.th [ prop.text nm ] ] ] ]
                  Html.tbody
                    [ prop.children
                        [ for i in 0 .. n - 1 ->
                            Html.tr
                              [ prop.children [ for nm in names -> Html.td [ prop.text (cellText (cellAt t nm i)) ] ] ] ] ] ] ] ] ]

// ─── The page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private LivingSheetView () : ReactElement =
  let rows, setRows = React.useState initialRows
  let minUnits, setMinUnits = React.useState 0
  let derive, setDerive = React.useState true
  let filter, setFilter = React.useState true
  let group, setGroup = React.useState true
  let showWire, setShowWire = React.useState false

  let src = sourceTable rows
  let pipeline = buildPipeline derive filter group
  let env = Map [ "minUnits", Cell.Int minUnits ]
  let result = DF.evalPipelineInEnv env pipeline src

  // The wire – the actual canonical JSON the shipped codecs emit.
  let pipelineJson = DataFrameCodec.encodePipeline pipeline
  let sourceJson = ColumnCodec.encode (DataSource.Embedded src)

  // ── the editable source sheet ──────────────────────────────────────────
  let numCell (value: float) (onSet: float -> unit) : ReactElement =
    Html.input
      [ prop.className "ls-num"
        prop.type' "number"
        prop.value (sprintf "%g" value)
        prop.onChange (fun (v: string) ->
          let n = jsNumber v

          if jsFinite n then
            onSet n) ]

  let updateRow (i: int) (f: Row -> Row) : unit =
    setRows (rows |> List.mapi (fun j x -> if j = i then f x else x))

  let editRow (i: int) : ReactElement =
    let r = List.item i rows

    let cell (el: ReactElement) : ReactElement = Html.td [ prop.children [ el ] ]

    Html.tr
      [ prop.children
          [ Html.td [ prop.text r.Region ]
            Html.td [ prop.text r.Product ]
            cell (numCell (float r.Units) (fun v -> updateRow i (fun x -> { x with Units = int v })))
            cell (numCell r.Revenue (fun v -> updateRow i (fun x -> { x with Revenue = v })))
            cell (numCell r.Cost (fun v -> updateRow i (fun x -> { x with Cost = v }))) ] ]

  let headerRow: ReactElement =
    Html.thead
      [ Html.tr
          [ prop.children [ for h in [ "region"; "product"; "units"; "revenue"; "cost" ] -> Html.th [ prop.text h ] ] ] ]

  let sourceEditor =
    Html.div
      [ prop.className "ls-source"
        prop.children
          [ Html.div [ prop.className "ls-panel-h"; prop.text "Source data – edit any number" ]
            Html.div
              [ prop.className "ls-sheet-wrap"
                prop.children
                  [ Html.table
                      [ prop.className "ls-sheet ls-sheet-edit"
                        prop.children
                          [ headerRow
                            Html.tbody [ prop.children [ for i in 0 .. List.length rows - 1 -> editRow i ] ] ] ] ] ] ] ]

  // ── the pipeline panel (the formulas + toggles) ────────────────────────
  let stepRow (on: bool) (setOn: bool -> unit) (name: string) (formula: string) : ReactElement =
    Html.label
      [ prop.className (if on then "ls-step ls-step-on" else "ls-step")
        prop.children
          [ Html.input
              [ prop.type' "checkbox"
                prop.isChecked on
                prop.onChange (fun (c: bool) -> setOn c) ]
            Html.div
              [ prop.className "ls-step-body"
                prop.children
                  [ Html.span [ prop.className "ls-step-name"; prop.text name ]
                    Html.code [ prop.className "ls-step-formula"; prop.text formula ] ] ] ] ]

  let pipelinePanel =
    Html.div
      [ prop.className "ls-pipeline"
        prop.children
          [ Html.div [ prop.className "ls-panel-h"; prop.text "Transform pipeline – toggle a step" ]
            stepRow derive (fun v -> setDerive v) "derive" "margin = (revenue − cost) / revenue"
            stepRow filter (fun v -> setFilter v) "filter" "keep rows where units ≥ minUnits"
            stepRow group (fun v -> setGroup v) "groupBy" "region → Σ revenue, Σ units, mean margin"
            Html.div
              [ prop.className "ls-threshold"
                prop.children
                  [ Html.span
                      [ prop.className "ls-threshold-label"
                        prop.text (sprintf "minUnits param = %d" minUnits) ]
                    Html.input
                      [ prop.className "ls-slider"
                        prop.type' "range"
                        prop.min 0
                        prop.max 150
                        prop.step 5
                        prop.value minUnits
                        prop.disabled (not filter)
                        prop.onChange (fun (v: string) -> setMinUnits (int (jsNumber v))) ] ] ] ] ]

  // ── the result (Fuaran dashboard when grouped, else the raw sheet) ──────
  let resultView =
    match result with
    | Error e ->
      Html.div
        [ prop.className "ls-eval-error"
          prop.children
            [ Html.span [ prop.className "ls-eval-mark"; prop.text "EvalError" ]
              Html.code [ prop.text (sprintf "%A" e) ] ] ]
    | Ok out when group && (colByName out "region").IsSome ->
      Html.div
        [ prop.className "ls-dash-render"
          prop.children [ renderNode (dashboardNode out) ] ]
    | Ok out -> sheetTable out

  let resultPanel =
    Html.div
      [ prop.className "ls-result"
        prop.children
          [ Html.div
              [ prop.className "ls-panel-h"
                prop.text (
                  if group then
                    "Result – a Fuaran dashboard, rendered from the computed table"
                  else
                    "Result – the raw computed rows"
                ) ]
            resultView ] ]

  // ── the wire drawer ────────────────────────────────────────────────────
  let wireDrawer =
    Html.div
      [ prop.className "ls-wire"
        prop.children
          [ Html.button
              [ prop.className "ls-wire-toggle"
                prop.text (
                  if showWire then
                    "Hide the wire"
                  else
                    "Show the wire – the formulas are data"
                )
                prop.onClick (fun _ -> setShowWire (not showWire)) ]
            (if showWire then
               Html.div
                 [ prop.className "ls-wire-body"
                   prop.children
                     [ Html.div
                         [ prop.className "ls-wire-block"
                           prop.children
                             [ Html.span
                                 [ prop.className "ls-wire-tag"
                                   prop.text "the pipeline (Transform list) – canonical wire" ]
                               Html.pre
                                 [ prop.className "wire-json"
                                   prop.children [ Html.code [ prop.text pipelineJson ] ] ] ] ]
                       Html.div
                         [ prop.className "ls-wire-block"
                           prop.children
                             [ Html.span
                                 [ prop.className "ls-wire-tag"
                                   prop.text "the source (DataSource) – canonical wire" ]
                               Html.pre
                                 [ prop.className "wire-json"
                                   prop.children [ Html.code [ prop.text sourceJson ] ] ] ] ] ] ]
             else
               Html.none) ] ]

  let honesty =
    Html.div
      [ prop.className "ls-honesty"
        prop.children
          [ Html.h3 [ prop.text "How honest is this?" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Every number in the dashboard is computed by the real Fuaran.Core.DataFrame reference evaluator, compiled to JavaScript via Fable. Editing a source cell or the threshold re-runs the actual pipeline in your browser – there is no server and no separate spreadsheet engine." ]
                    Html.li
                      [ prop.text
                          "The pipeline you toggle is the actual serialisable Transform algebra – derive / filter / groupBy from the shipped v1 verb set. The threshold drives a real pipeline Param, whose reactive edge is derived from the expression, not separately wired." ]
                    Html.li
                      [ prop.text
                          "The JSON in the drawer is the genuine canonical wire the shipped codecs emit for the pipeline and its source – no hand-authored mock. The computation is data: it can be stored, diffed, teleported, and re-run, because it is not code." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "That same evaluator is certified byte-identical across F#, TypeScript, and Python – so the compute travels with the app, the "
                            Html.a [ prop.href "#/pillar/wire"; prop.text "one-wire-many-worlds" ]
                            Html.text " thesis extended from the view to the computation behind it." ] ] ] ] ] ]

  Html.div
    [ prop.className "ls-page"
      prop.children
        [ Html.h1 [ prop.className "ls-title"; prop.text "The Living Sheet" ]
          Html.p
            [ prop.className "ls-lede"
              prop.text
                "Every number here is computed live by a pipeline that is itself data on the wire. Edit an input and watch it recompute; open the wire and the formulas are right there in the JSON – the computation is a value, not code." ]
          Html.div [ prop.className "ls-grid"; prop.children [ sourceEditor; pipelinePanel ] ]
          resultPanel
          wireDrawer
          honesty ] ]

let page: ReactElement = LivingSheetView()
