module Fuaran.Showcase.Testing.UndoGridProbes

// ============================================================================
//  TEST-ONLY. The headless surface of the Undo Grid page (Phase 2395).
//
//  Imported by test/undoGrid.test.ts from the Fable output and by nothing in
//  the app. A session is a list of gesture strings run through the page's own
//  `step` from its own `initial`, so the suite drives exactly the model the page
//  renders:
//
//    edit:<sku>:<column>:<number>   a cell edit, as the grid's editor delivers it
//    edit:<sku>:<column>:"<text>"   a text value in a cell
//    undo / redo
// ============================================================================

open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven

module UG = Fuaran.Showcase.UndoGrid

let private gestureOf (text: string) : UG.Gesture =
  match text.Split(':') |> List.ofArray with
  | [ "undo" ] -> UG.Undo
  | [ "redo" ] -> UG.Redo
  | [ "edit"; sku; column; value ] when value.StartsWith "\"" ->
    UG.Edit(sku, column, UG.cellOfValue (CellValue.Text(value.Trim '"')))
  | [ "edit"; sku; column; value ] -> UG.Edit(sku, column, UG.cellOfValue (CellValue.Numeric(float value)))
  | _ -> failwith ("unknown gesture: " + text)

/// The model after a session, from the page's own initial state.
let private run (gestures: string array) : UG.Model =
  gestures |> Array.fold (fun m g -> UG.step (gestureOf g) m) UG.initial

/// A table as `k=v,…` rows in column order, joined by `;`.
let private flat (t: Table) : string =
  [ for i in 0 .. Table.rowCount t - 1 ->
      t.Columns
      |> List.map (fun c -> c.Name + "=" + UG.cellText (Fuaran.Core.Column.cell i c))
      |> String.concat "," ]
  |> String.concat ";"

/// The live table after the session.
let ugTable (gestures: string array) : string =
  flat (GridEditLog.table (run gestures).Log)

/// The table the starting rows replay to through the first `count` records.
let ugReplayTo (gestures: string array) (count: int) : string =
  match UG.replayTo count (run gestures) with
  | Ok t -> flat t
  | Error why -> "error:" + why

/// How many records the session appended.
let ugRecordCount (gestures: string array) : int =
  GridEditLog.records (run gestures).Log |> List.length

/// Each record's op on the columnar op codec's wire.
let ugRecordOps (gestures: string array) : string array =
  GridEditLog.records (run gestures).Log
  |> List.map (fun r -> ColumnOps.encode r.Op)
  |> Array.ofList

/// Each record's history line, as the page prints it.
let ugRecordLines (gestures: string array) : string array =
  GridEditLog.records (run gestures).Log
  |> List.map (fun r -> UG.describe r.Op)
  |> Array.ofList

/// `undo=<bool>,redo=<bool>` after the session.
let ugFlags (gestures: string array) : string =
  let log = (run gestures).Log

  "undo="
  + (if GridEditLog.canUndo log then "true" else "false")
  + ",redo="
  + if GridEditLog.canRedo log then "true" else "false"

/// The refusal the last gesture left, or "".
let ugRefusal (gestures: string array) : string = defaultArg (run gestures).Refusal ""

/// `intact`, or the index of the first broken record.
let ugIntegrity (gestures: string array) : string =
  match GridEditLog.verify (run gestures).Log with
  | None -> "intact"
  | Some broken -> "broken@" + string broken.Index

/// The last gesture's payload on the wire, or "".
let ugLastPayload (gestures: string array) : string =
  match (run gestures).Last with
  | Some edit -> GridEdit.encode edit
  | None -> ""

/// A payload decoded strictly by the published codec and re-encoded, or `error:<why>`.
let ugPayloadRoundTrip (payload: string) : string =
  match GridEdit.decode payload with
  | Ok edit -> GridEdit.encode edit
  | Error why -> "error:" + why

/// The grid's rows as the page hands them to the renderer, flat.
let ugGridRows (gestures: string array) : string =
  UG.rowsOf (GridEditLog.table (run gestures).Log)
  |> List.map (fun r ->
    r
    |> Map.toList
    |> List.map (fun (k, v) -> k + "=" + string v)
    |> String.concat ",")
  |> String.concat ";"
