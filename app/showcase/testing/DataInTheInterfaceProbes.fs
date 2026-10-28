module Fuaran.Showcase.Testing.DataInTheInterfaceProbes

// ============================================================================
//  TEST-ONLY. The headless surface of the three "data in the interface" pages
//  (Phase 1887): Linked Brushing, the Pivot Sandbox and the Typed Refusal.
//
//  Imported by test/dataInTheInterface.test.ts from the Fable output and by
//  nothing in the app. Every tree crosses as the canonical wire JSON the real
//  encoder produced, and every resolution is the shipped resolver's own, so the
//  suite checks what the pages render from rather than a restatement of it.
// ============================================================================

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module LB = Fuaran.Showcase.LinkedBrushing
module PS = Fuaran.Showcase.PivotSandbox
module TR = Fuaran.Showcase.TypedRefusal

/// The row source a chart or grid node declares.
let private sourceOf (node: Node<obj>) : Binding<Row seq> option =
  match node.Kind with
  | NodeKind.Chart spec -> Some spec.Source
  | NodeKind.DataGrid g -> Some g.Source
  | _ -> None

/// One resolved row as `k=v` pairs in key order, rows joined by `;`.
let private flatRows (rows: Row seq) : string =
  rows
  |> Seq.map (fun r ->
    r
    |> Map.toList
    |> List.map (fun (k, v) -> k + "=" + string v)
    |> String.concat ",")
  |> String.concat ";"

/// `ok:<rows>` or `error:<message>` for a node's source under some sources.
let private resolveNode (sources: BindingSources) (node: Node<obj>) : string =
  match sourceOf node with
  | None -> "error:not a data-bearing node"
  | Some source ->
    match BindingResolver.resolve<Row seq> sources source with
    | BindingResolver.Resolved rows -> "ok:" + flatRows rows
    | BindingResolver.Errored m -> "error:" + m
    | BindingResolver.NotResolved -> "error:not resolved"
    | BindingResolver.I18nUnresolved k -> "error:i18n " + k

// ─── Linked Brushing ─────────────────────────────────────────────────────────

let lbWire: string = CJson.encodeNode LB.dashboard

let lbViewWires: string array =
  [| LB.regionChart; LB.productChart; LB.quarterChart; LB.detailGrid |]
  |> Array.map CJson.encodeNode

let private lbView (viewId: string) : Node<obj> option =
  [ LB.regionChart; LB.productChart; LB.quarterChart; LB.detailGrid ]
  |> List.tryFind (fun n -> n.Id = viewId)

/// A view's rows when the region chart has published `region` and the product
/// chart `product` — an empty string meaning that chart has published nothing.
/// The published value is a whole row, exactly as the chart's click writes it.
let lbRows (viewId: string) (region: string) (product: string) : string =
  let published (chartId: string) (field: string) (value: string) =
    if value = "" then
      []
    else
      [ NodeId chartId, box (Map.ofList [ field, box value; "revenue", box 0.0 ]) ]

  let sources =
    { BindingResolver.empty with
        Selections =
          Map.ofList (
            published LB.RegionChartId "region" region
            @ published LB.ProductChartId "product" product
          ) }

  match lbView viewId with
  | Some node -> resolveNode sources node
  | None -> "error:unknown view " + viewId

/// The row count of the inline table.
let lbTableRows: int = Table.rowCount LB.table

// ─── the Pivot Sandbox ───────────────────────────────────────────────────────

let psPaletteIds: string array =
  PS.palette |> List.map (fun s -> s.Id) |> Array.ofList

let psRunawayLength: int = PS.runawayLength

let psStepLimit: int = BindingResolver.TransformBudget.defaults.MaxSteps

let psRefusalMarker: string = BindingResolver.TransformBudget.RefusalMarker

let psWire (ids: string array) : string =
  CJson.encodeNode (PS.sandboxGrid (PS.pipelineOf (List.ofArray ids)))

let psStepCount (ids: string array) : int =
  PS.pipelineOf (List.ofArray ids) |> List.length

/// The grid's source as the renderer resolves it: `ok:<rows>` or `error:<message>`.
let psResolve (ids: string array) : string =
  resolveNode BindingResolver.empty (PS.sandboxGrid (PS.pipelineOf (List.ofArray ids)))

/// The labels of the grid's declared columns, comma-joined.
let psColumns (ids: string array) : string =
  match (PS.sandboxGrid (PS.pipelineOf (List.ofArray ids))).Kind with
  | NodeKind.DataGrid g -> g.Columns |> List.map (fun c -> c.Label) |> String.concat ","
  | _ -> ""

// ─── the Typed Refusal ───────────────────────────────────────────────────────

let private trBinding (x: string) (y: string) (margin: bool) (byRegion: bool) (named: bool) : TR.ChartBinding =
  { TR.initial with
      XField = x
      YField = y
      Margin = margin
      ByRegion = byRegion
      Source = if named then TR.Source.Named else TR.Source.Inline }

let trWire (x: string) (y: string) (margin: bool) (byRegion: bool) (named: bool) : string =
  CJson.encodeNode (TR.chartNode (trBinding x y margin byRegion named))

/// `<grade>|<code@path,…>|<name:type,…>` for the chart's check, where grade is
/// `checked` or `unchecked`, and a type the typer could not decide reads `?`.
let trCheck (x: string) (y: string) (margin: bool) (byRegion: bool) (named: bool) : string =
  match TR.checkOf (trBinding x y margin byRegion named) with
  | None -> "none"
  | Some c ->
    let grade =
      match c.Grade with
      | PreEmitValidate.BindingGrade.Checked -> "checked"
      | PreEmitValidate.BindingGrade.Unchecked _ -> "unchecked"

    let findings =
      TR.refusals c |> List.map (fun d -> d.Code + "@" + d.Path) |> String.concat ","

    let produced =
      c.Produced
      |> List.map (fun p -> p.Name + ":" + defaultArg p.Type "?")
      |> String.concat ","

    grade + "|" + findings + "|" + produced

/// The chart's own source resolved by the renderer, for the cases the report admits.
let trResolve (x: string) (y: string) (margin: bool) (byRegion: bool) : string =
  resolveNode BindingResolver.empty (TR.chartNode (trBinding x y margin byRegion false))
