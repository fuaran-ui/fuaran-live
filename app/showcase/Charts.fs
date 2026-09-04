module Fuaran.Showcase.Charts

// ============================================================================
//  Chart-as-data – a LIVE dataframe that is data, not a PNG. Pillar: "the app
//  is a value".
//
//  The whole page is data end-to-end, computed live in the browser:
//
//    • The source DataFrame is an editable Fuaran `DataGrid` node – not a
//      hand-rolled HTML table. Its cells are typed `Column.editable`; editing
//      one dispatches a real `Action`, so the grid IS the app's data surface.
//    • The chart's `Source` is a `Binding.Transform` – an embedded
//      `Fuaran.Core.DataSource` + a declarative pipeline (the real dataframe
//      algebra) that RIDES THE WIRE. The renderer evaluates it client-side with
//      the shipped `Fuaran.Core.DataFrame` reference evaluator (Fable → JS), so
//      the figure's *data derivation* is itself canonical wire data – no server,
//      no charting library. Toggle the "variance" derived column and the pipeline
//      grows a step; the chart re-plots it live.
//    • Every edit is folded into a real hash-chained op-stream (the shipped
//      `HashChain.computeHash`): an append-only provenance log of the typed
//      operations between chart versions – the figure is notarised AND diffable,
//      and the log proves it. The chain is an unkeyed digest, so it detects
//      corruption and casual alteration; it is not tamper evidence, and the
//      honesty panel says so.
//
//  Nothing here needs a server. The wire drawer shows the genuine canonical JSON
//  – the chart node now carries its source table AND its transform pipeline, so
//  "your figure is data" is literal: the data, its derivation, and the picture
//  are one portable value that renders byte-identically on every host.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Ops.Types

module Diff = Fuaran.UI.OpStream.Replay.TreeOpDiff

// ─── The page message + the editable source rows ────────────────────────────

/// The one message the editable DataGrid dispatches – a single cell edit,
/// addressed by quarter + column. `dispatch` folds it back into React state,
/// which re-derives the whole page (grid, transform, chart, op-stream).
type private Msg = SetCell of quarter: string * col: string * value: float

type private Row =
  { Quarter: string
    Revenue: float
    Target: float }

let private initialRows: Row list =
  [ { Quarter = "Q1"
      Revenue = 120.0
      Target = 100.0 }
    { Quarter = "Q2"
      Revenue = 150.0
      Target = 140.0 }
    { Quarter = "Q3"
      Revenue = 90.0
      Target = 130.0 }
    { Quarter = "Q4"
      Revenue = 175.0
      Target = 160.0 } ]

/// The editable rows as a real `Fuaran.Core.Table` (columnar, null-aware) – the
/// embedded source the chart's `Binding.Transform` computes over.
let private sourceTable (rows: Row list) : Table =
  { Schema = [ "quarter", StringType; "revenue", FloatType; "target", FloatType ]
    Columns =
      [ { Name = "quarter"
          Type = StringType
          Cells = rows |> List.map (fun r -> Cell.Str r.Quarter) }
        { Name = "revenue"
          Type = FloatType
          Cells = rows |> List.map (fun r -> Cell.Float r.Revenue) }
        { Name = "target"
          Type = FloatType
          Cells = rows |> List.map (fun r -> Cell.Float r.Target) } ] }

// ─── The declarative transform (the real dataframe algebra, on the wire) ─────

/// variance = revenue − target – a derived column expressed as data (a
/// `ColExpr`), not code. When plotted, the pipeline carries this step.
let private varianceExpr: ColExpr = Binary(Sub, Col "revenue", Col "target")

/// The pipeline: a single toggleable `Derive` step. Empty ⇒ the chart plots the
/// raw source columns; on ⇒ `variance` becomes a plottable series computed by
/// the evaluator at render time. A param-free pipeline is byte-identical to the
/// no-op shape, so an unused transform costs nothing on the wire.
let private buildPipeline (variance: bool) : Fuaran.Core.Transform list =
  [ if variance then
      Derive("variance", varianceExpr) ]

/// The chart title, derived from the selected series – e.g. all →
/// "Quarterly Revenue vs Target vs Variance"; revenue only → "Quarterly Revenue".
let private titleFor (yFields: string list) : string =
  let cap (s: string) =
    if s = "" then
      s
    else
      string (System.Char.ToUpper s.[0]) + s.Substring 1

  match yFields |> List.map cap with
  | [] -> ""
  | names -> "Quarterly " + String.concat " vs " names

/// The semantic `Chart` node. `Source` is a `Binding.Transform` – the embedded
/// source table + the declarative pipeline travel with the node, and the
/// renderer evaluates them client-side (Chart → rows → lowered Drawing → inline
/// SVG). No pre-computed rows, no charting library. The visible title rides the
/// wire (`ChartSpec.Title`) and lowers to a bigger, emphasised SVG label.
let private chartNode (kind: ChartKind) (yFields: string list) (variance: bool) (rows: Row list) : Node<Msg> =
  Fuaran.chart
    "sales-chart"
    { Defaults.chart<Msg> with
        Kind = kind
        Source =
          Binding.Transform(TransformSource.Data(DataSource.Embedded(sourceTable rows)), buildPipeline variance, None)
        XField = "quarter"
        YFields = yFields
        Title = Some(TextSource.Literal(titleFor yFields)) }

/// The editable source DataFrame as a first-party Fuaran `DataGrid` node: quarter
/// read-only, revenue + target as typed `Column.editable` cells that dispatch a
/// `SetCell` on change. The grid is data too – both panes are Fuaran nodes.
let private cellNum (v: CellValue) : float =
  match v with
  | CellValue.Numeric n -> n
  | _ -> 0.0

// fuaran#665 — column accessors and handlers read the projected wire `Row`
// (Map<string, obj>) by field name; the typed record survives only at the
// Source seam, projected through the REQUIRED `toRow` below.
let private rowText (field: string) (r: Fuaran.Core.Row) : string =
  defaultArg (Map.tryFind field r |> Option.map string) ""

let private rowFloat (field: string) (r: Fuaran.Core.Row) : float =
  match Map.tryFind field r with
  | Some v -> unbox<float> v
  | None -> 0.0

let private sourceGrid (rows: Row list) : Node<Msg> =
  Fuaran.grid
    "sales-source"
    (fun (r: Row) -> Map.ofList [ "quarter", box r.Quarter; "revenue", box r.Revenue; "target", box r.Target ])
    { Defaults.grid<Row, Msg> with
        Source = Binding.Static(Some(Seq.ofList rows))
        RowKey = rowText "quarter"
        Editable = true
        Columns =
          [ Column.text "quarter" (rowText "quarter")
            Column.numeric "revenue" (rowFloat "revenue")
            |> Column.editable (fun r v -> Action.dispatch (SetCell(rowText "quarter" r, "revenue", cellNum v)))
            Column.numeric "target" (rowFloat "target")
            |> Column.editable (fun r v -> Action.dispatch (SetCell(rowText "quarter" r, "target", cellNum v))) ] }

// ─── op-stream rendering (the hash-chained provenance of the edits) ──────────

/// The bare op-kind name (the typed operation's constructor), used to summarise
/// an edit's batch of ops honestly without surfacing each facet delta.
let private opKind (op: TreeOp<Msg>) : string =
  match op with
  | TreeOp.UpdateProp _ -> "UpdateProp"
  | TreeOp.ReplaceBinding _ -> "ReplaceBinding"
  | TreeOp.EditNode _ -> "EditNode"
  | TreeOp.UpdateStyle _ -> "UpdateStyle"
  | TreeOp.UpdateState _ -> "UpdateState"
  | TreeOp.InsertChild _ -> "InsertChild"
  | TreeOp.RemoveNode _ -> "RemoveNode"
  | TreeOp.MoveNode _ -> "MoveNode"
  | TreeOp.ReorderChildren _ -> "ReorderChildren"
  | TreeOp.ReplaceRoot _ -> "ReplaceRoot"
  | TreeOp.Batch _ -> "Batch"

let private shortHash (h: string) : string =
  if h.Length > 12 then h.Substring(0, 12) + "…" else h

/// One entry of the hash-chained op-stream we accumulate as edits happen.
type private ChainEntry =
  { Seq: int
    Summary: string
    Hash: string
    Prev: string }

/// A fixed timestamp so the chain is content-addressed (the hash is a pure
/// function of prev-hash + sequence + actor + op – edit the same way, get the
/// same chain), rather than wall-clock-dependent.
let private fixedTs =
  System.DateTimeOffset(2020, 1, 1, 0, 0, 0, System.TimeSpan.Zero)

// ─── The page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private ChartsView () : ReactElement =
  let rows, setRows = React.useState initialRows
  let kind, setKind = React.useState ChartKind.Bar
  let showRevenue, setShowRevenue = React.useState true
  let showTarget, setShowTarget = React.useState true
  let showVariance, setShowVariance = React.useState false
  let chain, setChain = React.useState ([]: ChainEntry list)
  let prevTree = React.useRef (None: Node<Msg> option)

  let yFields =
    [ if showRevenue then
        "revenue"
      if showTarget then
        "target"
      if showVariance then
        "variance" ]

  let hasSeries = not (List.isEmpty yFields)

  // The chart artefact + its canonical wire + content hash. All pure functions
  // of the controls + the editable data – the figure (and its derivation) IS
  // these bytes. `showVariance` drives BOTH the pipeline's Derive step and the
  // plotted series, so the two never disagree.
  let tree = chartNode kind yFields showVariance rows
  let wire = CanonicalJson.encodeNode tree
  let hash = Hashing.sha256Hex wire

  // The editable grid dispatches SetCell; fold it into the row state, which
  // re-derives everything downstream.
  let dispatch (msg: Msg) : unit =
    match msg with
    | SetCell(q, col, v) ->
      setRows (
        rows
        |> List.map (fun r ->
          if r.Quarter = q then
            match col with
            | "revenue" -> { r with Revenue = v }
            | "target" -> { r with Target = v }
            | _ -> r
          else
            r)
      )

  // On any change, derive the real structural tree-diff between the previously
  // rendered chart and the new one, and FOLD the whole batch into ONE
  // hash-chained op-stream entry – a chain-linked provenance record of the
  // edit (the shipped `HashChain.computeHash`, running under Fable in the
  // browser). One edit ⇒ one chained entry, hashing the batched typed ops; the
  // summary names the op kinds honestly rather than surfacing each facet delta.
  React.useEffect (
    (fun () ->
      match prevTree.current with
      | Some prev when hasSeries ->
        let ops =
          Diff.diffBatched prev tree
          |> List.collect (fun op ->
            match op with
            | TreeOp.Batch xs -> xs
            | _ -> [ op ])

        if not (List.isEmpty ops) then
          let prevHash =
            match List.tryLast chain with
            | Some e -> e.Hash
            | None -> ""

          let nextSeq =
            match List.tryLast chain with
            | Some e -> e.Seq + 1
            | None -> 1

          // Hash the edit's batch as one op (a real `TreeOp.Batch`), so
          // one edit is one notarised, chained entry.
          let batchOp =
            match ops with
            | [ single ] -> single
            | many -> TreeOp.Batch many

          let kinds = ops |> List.map opKind |> List.distinct |> String.concat ", "

          let summary = sprintf "%d typed op(s) → sales-chart · %s" (List.length ops) kinds

          let h =
            HashChain.computeHash prevHash batchOp nextSeq fixedTs (Actor.Human "you") None OpResultEnvelope.Success

          setChain (
            chain
            @ [ { Seq = nextSeq
                  Summary = summary
                  Hash = h
                  Prev = prevHash } ]
          )
      | _ -> ()

      if hasSeries then
        prevTree.current <- Some tree),
    [| box wire |]
  )

  // ── controls ──
  let kindButton (k: ChartKind) (label: string) =
    Html.button
      [ prop.className (if kind = k then "ch-kind ch-kind-on" else "ch-kind")
        prop.text label
        prop.onClick (fun _ -> setKind k) ]

  let seriesToggle (label: string) (on: bool) (set: bool -> unit) =
    Html.label
      [ prop.className "ch-series"
        prop.children
          [ Html.input
              [ prop.type' "checkbox"
                prop.isChecked on
                prop.onChange (fun (v: bool) -> set v) ]
            Html.span [ prop.text label ] ] ]

  // ── the editable source DataFrame (a Fuaran DataGrid, rendered live) ──
  let dataPane =
    Html.div
      [ prop.className "ch-source"
        prop.children [ Render.renderWithSources BindingResolver.empty dispatch (sourceGrid rows) ] ]

  let chartStage =
    if hasSeries then
      // The renderer resolves the Chart's Binding.Transform (evaluating the
      // dataframe pipeline in-browser), then lowers Chart → Drawing → SVG.
      Html.div
        [ prop.className "ch-stage"
          prop.children [ Render.renderWithSources BindingResolver.empty ignore tree ] ]
    else
      Html.div
        [ prop.className "ch-stage ch-stage-empty"
          prop.text "Select at least one series to plot." ]

  let controls =
    Html.div
      [ prop.className "ch-controls"
        prop.children
          [ Html.div
              [ prop.className "ch-control-group"
                prop.children
                  [ Html.span [ prop.className "ch-control-label"; prop.text "Chart" ]
                    kindButton ChartKind.Bar "Bar"
                    kindButton ChartKind.Line "Line" ] ]
            Html.div
              [ prop.className "ch-control-group"
                prop.children
                  [ Html.span [ prop.className "ch-control-label"; prop.text "Series" ]
                    seriesToggle "revenue" showRevenue setShowRevenue
                    seriesToggle "target" showTarget setShowTarget
                    seriesToggle "variance (derived)" showVariance setShowVariance ] ] ] ]

  let notarised =
    Html.div
      [ prop.className "ch-notarise"
        prop.children
          [ Html.span
              [ prop.className "ch-notarise-label"
                prop.text "Content hash (SHA-256 of the wire)" ]
            Html.code
              [ prop.className "ch-hash"
                prop.text (if hasSeries then shortHash hash else "–") ]
            Html.span
              [ prop.className "ch-notarise-note"
                prop.text
                  "The wire now carries the source table AND its transform pipeline – edit a cell and the hash changes, because the figure's data and its derivation are content-addressed." ] ] ]

  // ── the hash-chained op-stream (the provenance of every edit) ──
  let opStream =
    if List.isEmpty chain then
      Html.none
    else
      Html.div
        [ prop.className "ch-chain"
          prop.children
            [ Html.div
                [ prop.className "ch-chain-head"
                  prop.text (
                    sprintf
                      "Op-stream – %d hash-chained edit(s). Each entry links to the previous by hash, so a corrupted or altered entry breaks the chain instead of replaying quietly:"
                      (List.length chain)
                  ) ]
              Html.ul
                [ prop.className "ch-chain-list"
                  prop.children
                    [ for e in List.rev chain ->
                        Html.li
                          [ prop.className "ch-chain-entry"
                            prop.children
                              [ Html.span [ prop.className "ch-chain-seq"; prop.text (sprintf "#%d" e.Seq) ]
                                Html.code [ prop.className "ch-chain-op"; prop.text e.Summary ]
                                Html.code
                                  [ prop.className "ch-chain-hash"
                                    prop.text (
                                      (if e.Prev = "" then "∅" else shortHash e.Prev) + " → " + shortHash e.Hash
                                    ) ] ] ] ] ] ] ]

  let wireDrawer =
    Html.details
      [ prop.className "ch-wire"
        prop.children
          [ Html.summary
              [ prop.text
                  "The chart's wire JSON – the data, its transform pipeline, and the figure are one value; paste it into any Fuaran host" ]
            Html.pre [ prop.className "wire-json"; prop.children [ Html.code [ prop.text wire ] ] ] ] ]

  let honesty =
    Html.div
      [ prop.className "ch-honesty"
        prop.children
          [ Html.h3 [ prop.text "A live dataframe that is data" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "The source table is an editable, first-party Fuaran data grid – edit any revenue or target cell and it dispatches a typed action. The grid is a node, not hand-rolled HTML." ]
                    Html.li
                      [ prop.text
                          "The chart binds its data with a declarative transform pipeline that rides the wire. The renderer runs the real Fuaran dataframe evaluator in your browser (compiled to JavaScript), computes the rows – including the derived variance column when toggled – and renders them as inline SVG. No server, no charting library, no pre-baked numbers." ]
                    Html.li
                      [ prop.text
                          "Every edit is folded into a hash-chained op-stream: each entry links to the previous by hash, so a corrupted or altered entry breaks the chain. The chain is an unkeyed digest, which makes it corruption detection rather than tamper evidence – anyone who can write the store can recompute the hashes that follow their edit, and catching that needs signing, a separate seam that is not shipped. The figure is notarised (the content hash) and diffable (the typed ops) – and portable, because the same wire renders byte-for-byte on the TypeScript and Python hosts, server-side and headless." ]
                    Html.li
                      [ prop.children
                          [ Html.text "Author it from a notebook: the same shape a "
                            Html.a [ prop.href "#/demo/pandas"; prop.text "pandas DataFrame" ]
                            Html.text " emits – a table becomes a live, computed chart-as-data, not a rasterised PNG." ] ] ] ] ] ]

  Html.div
    [ prop.className "ch-page"
      prop.children
        [ Html.h1 [ prop.className "ch-title"; prop.text "Chart-as-data" ]
          Html.p
            [ prop.className "ch-lede"
              prop.text
                "A figure is usually a PNG – an opaque pixel dump. Here an editable dataframe is authored as a Fuaran chart whose data-binding is a transform pipeline on the wire, computed live in the browser and rendered as inline SVG with no charting library. Edit a cell: the figure, its hash, and its op-stream all follow." ]
          Html.div
            [ prop.className "ch-split"
              prop.children
                [ Html.div
                    [ prop.className "ch-data-pane"
                      prop.children
                        [ Html.span [ prop.className "ch-pane-tag"; prop.text "The DataFrame – edit any number" ]
                          dataPane ] ]
                  Html.div
                    [ prop.className "ch-render-pane"
                      prop.children
                        [ Html.span
                            [ prop.className "ch-pane-tag"
                              prop.text "The chart (computed from the wire, no charting lib)" ]
                          controls
                          chartStage ] ] ] ]
          notarised
          opStream
          wireDrawer
          honesty ] ]

let page: ReactElement = ChartsView()
