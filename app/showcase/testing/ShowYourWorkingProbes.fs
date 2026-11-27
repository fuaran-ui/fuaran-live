module Fuaran.Showcase.Testing.ShowYourWorkingProbes

// ============================================================================
//  TEST-ONLY. The headless surface of the Show Your Working page (Phase 2756).
//
//  Imported by test/showYourWorking.test.ts from the Fable output and by
//  nothing in the app. Each figure crosses as the canonical wire the real
//  encoder produced, the value the renderer resolves for its slot, and the
//  trace's canonical text, so the suite compares what the page shows with
//  what the trace ends on, from two different readers.
// ============================================================================

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Renderer

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module SYW = Fuaran.Showcase.ShowYourWorking

let private figure (id: string) : SYW.Figure =
  match SYW.figureById id with
  | Some f -> f
  | None -> failwithf "no figure %s" id

/// Every figure's id, in page order.
let figureIds: string array =
  SYW.figures |> List.map (fun f -> f.Id) |> Array.ofList

/// `headline` or `cell`.
let group (id: string) : string = (figure id).Group

/// The figure's node, as canonical wire.
let nodeWire (id: string) : string = CJson.encodeNode (SYW.node (figure id))

/// `ok:<number>` for the value the renderer resolves for the slot, else `error:<why>`.
let screenValue (id: string) : string =
  match SYW.screenValue (figure id) with
  | Ok v -> "ok:" + string v
  | Error m -> "error:" + m

/// The number as the page shows it.
let screenText (id: string) : string = SYW.screenText (figure id)

/// The figure's last table, read from the trace value: `rows=<n>;cols=<a,b>;cell=<text>`.
let traceFinal (id: string) : string =
  match SYW.trace (figure id) with
  | Error refusal -> "error:" + Derivation.DerivationRefusal.describe refusal
  | Ok t ->
    match Derivation.result t with
    | None -> "error:no tables"
    | Some(Error m) -> "error:" + m
    | Some(Ok table) ->
      let names = Table.columnNames table.Rows

      let cell =
        match names with
        | [ only ] ->
          match Table.tryColumn only table.Rows with
          | Some col when table.RowCount = 1 ->
            match BindingResolver.cellToFloat (Fuaran.Core.Column.cell 0 col) with
            | Ok v -> string v
            | Error m -> "error " + m
          | _ -> "none"
        | _ -> "none"

      sprintf "rows=%d;cols=%s;cell=%s" table.RowCount (String.concat "," names) cell

/// The last table's lone cell through the figure's own format.
let traceFinalText (id: string) : string =
  let f = figure id

  match SYW.trace f with
  | Ok t ->
    match Derivation.result t with
    | Some(Ok table) when table.RowCount = 1 ->
      match Table.columnNames table.Rows with
      | [ only ] ->
        match Table.tryColumn only table.Rows with
        | Some col ->
          match BindingResolver.cellToFloat (Fuaran.Core.Column.cell 0 col) with
          | Ok v -> Formatting.formatNumber f.Format v
          | Error m -> "error:" + m
        | None -> "error:no column"
      | _ -> "error:not one column"
    | _ -> "error:not one row"
  | Error refusal -> "error:" + Derivation.DerivationRefusal.describe refusal

/// The verbs the trace names, comma-separated.
let traceVerbs (id: string) : string =
  match SYW.trace (figure id) with
  | Ok t -> t.Steps |> List.map (fun s -> s.Verb) |> String.concat ","
  | Error _ -> ""

/// The trace's canonical text.
let traceWire (id: string) : string = SYW.traceWire (figure id)

/// The orders as embedded, canonical.
let ordersWire: string = SYW.ordersWire

/// The empty state's words.
let emptyStateHeading: string = SYW.emptyStateHeading
let emptyStateBody: string = SYW.emptyStateBody
let emptyWireNote: string = SYW.emptyWireNote

/// The threshold a large order meets.
let largeOrderUnits: int = SYW.largeOrderUnits
