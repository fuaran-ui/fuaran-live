module Fuaran.Showcase.Testing.ScenarioDeskProbes

// ============================================================================
//  TEST-ONLY. The headless surface of the Scenario Desk page (Phase 2759).
//
//  Imported by test/scenarioDesk.test.ts from the Fable output and by nothing
//  in the app. The host's state crosses as JSON text (a state object and a
//  filter object, each key to its stored value), and everything the suite
//  compares crosses as canonical bytes the real encoders produced.
// ============================================================================

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module SD = Fuaran.Showcase.ScenarioDesk

let private tree: Node<obj> =
  match SD.desk with
  | Ok t -> t
  | Error e -> failwithf "the desk was refused: %s" e

/// `ok`, or the library's reason for refusing the desk.
let deskBuilt: string =
  match SD.desk with
  | Ok _ -> "ok"
  | Error e -> "error:" + e

/// The desk's tree, as canonical wire.
let deskWire: string = CJson.encodeNode tree

/// The parameters' filter keys, in panel order.
let parameterKeys: string array =
  SD.parameters |> List.map (fun p -> p.Key) |> Array.ofList

/// The sites that read `key`, sorted.
let readers (key: string) : string array =
  SD.readersOf tree key |> List.sort |> Array.ofList

/// The sites that read none of the parameters, sorted.
let noParameterReaders: string array =
  SD.readersOfNoParameter tree |> List.sort |> Array.ofList

/// The readers whose state access the walk cannot see.
let opaqueReaders: string array = SD.opaqueReaders tree |> Array.ofList

let private entries (json: string) : Map<string, obj> =
  match Canonical.read json with
  | Ok(JObj members) -> members |> List.map (fun (k, v) -> k, box v) |> Map.ofList
  | Ok _ -> failwith "expected a JSON object"
  | Error e -> failwithf "not JSON: %A" e

let private sources (state: string) (filters: string) : BindingSources =
  { BindingResolver.empty with
      State = entries state
      Filters = entries filters }

let private nodeById (id: string) : Node<obj> =
  match Fuaran.UI.Ops.Introspect.findNode (NodeId id) tree with
  | Some n -> n
  | None -> failwithf "no node %s" id

/// The value each value-bearing site resolves to under `filters`, as
/// `id=<canonical bytes or error>` lines in a stable order: the two totals,
/// the comparison grid and the table's grid.
let siteValues (filters: string) : string =
  let s = sources "{}" filters

  let value (id: string) =
    match (nodeById id).Kind with
    | NodeKind.Metric m ->
      match BindingResolver.resolveScalarFloat s m.Value with
      | BindingResolver.Resolved v -> string v
      | other -> sprintf "error:%A" other
    | NodeKind.DataGrid g ->
      match BindingResolver.resolveRowsWithFields s g.Source with
      | BindingResolver.Resolved rows, _ -> SD.rowsWire (Seq.toList rows)
      | other, _ -> sprintf "error:%A" other
    | _ -> "error:not a value site"

  [ "desk-total-a"; "desk-total-b"; "desk-comparison-grid"; "desk-lines-grid" ]
  |> List.map (fun id -> id + "=" + value id)
  |> String.concat "\n"

/// What the live table shows under `state` and `filters`, as canonical bytes.
let shownWire (state: string) (filters: string) : string =
  match SD.shownWire tree (sources state filters) with
  | Ok w -> w
  | Error e -> "error:" + e

/// The view the page would copy under `state` and `filters`.
let copiedView (state: string) (filters: string) : string =
  match SD.shownAndView tree (sources state filters) with
  | Ok(_, view) -> SD.copiedView view
  | Error e -> "error:" + e

/// A pasted view applied to the rows, as canonical bytes, else `error:<why>`.
let pastedWire (text: string) : string =
  match SD.applyPasted text with
  | Ok rows -> SD.rowsWire rows
  | Error e -> "error:" + e

/// The page's words.
let emptyReadoutHeading: string = SD.emptyReadoutHeading
let emptyReadoutBody: string = SD.emptyReadoutBody
let emptyPasteNote: string = SD.emptyPasteNote
let whatIfContrast: string = SD.whatIfContrast

/// The table's FIXED pipeline, whose filter still reads its chip's parameter.
let fixedPipelineWire: string =
  Fuaran.Compute.DataFrameCodec.encodePipeline (Fuaran.UI.Fragments.Stdlib.refinablePipeline SD.tableId SD.table)
