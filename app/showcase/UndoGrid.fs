module Fuaran.Showcase.UndoGrid

// ============================================================================
//  The Undo Grid — edit a grid, undo, redo, and replay the history (Phase 2395).
//
//  The claim is that an edit is DATA. Each cell edit becomes one columnar op
//  (`ColumnOp.SetCell`, Phase 1891), and the published edit log
//  (`GridEditLog`) does the rest: undo applies the op's inverse, redo applies
//  the op again, and every op, undos included, is appended to a hash-chained
//  record that replays from the starting table to the live one. This page
//  writes no undo code, no history code and no replay code. Its one act of its
//  own is the translation of a cell gesture into the op that names that cell
//  (`opOf`, a few lines below).
//
//  Data is deterministic and inline: five rows of a seed stall's stock, in
//  integer units and integer pence. Nothing is fetched.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.ServerDriven

// ─── the inline table ────────────────────────────────────────────────────────

let private stockRows: (string * string * int * int) list =
  [ "S-01", "Tomato seeds", 40, 250
    "S-02", "Basil seeds", 25, 180
    "S-03", "Sweet pea seeds", 18, 320
    "S-04", "Runner bean seeds", 30, 275
    "S-05", "Sunflower seeds", 12, 199 ]

/// The table the log starts from, as a real columnar `Fuaran.Core.Table`.
let stock: Table =
  let col build name values =
    build name (Vector.ofList values) AllValid

  { Schema =
      [ Field.create "sku" StringType
        Field.create "item" StringType
        Field.create "qty" IntType
        Field.create "price" IntType ]
    Columns =
      [ col Fuaran.Core.Column.ofStrs "sku" [ for s, _, _, _ in stockRows -> s ]
        col Fuaran.Core.Column.ofStrs "item" [ for _, i, _, _ in stockRows -> i ]
        col Fuaran.Core.Column.ofInts "qty" [ for _, _, q, _ in stockRows -> q ]
        col Fuaran.Core.Column.ofInts "price" [ for _, _, _, p in stockRows -> p ] ] }

/// The State key the edits commit to, and the grid's declared row identity:
/// both travel in every edit payload.
[<Literal>]
let StockKey = "undo-grid.stock"

[<Literal>]
let RowKeyField = "sku"

// ─── the model: a log, the last payload, the last refusal ────────────────────

/// One reader gesture. `Edit` names the row by its key, as the grid does.
type Gesture =
  | Edit of sku: string * column: string * value: Cell
  | Undo
  | Redo

type Model =
  {
    /// The published edit log: the live version, the undo / redo stacks and the
    /// hash-chained record.
    Log: GridEditLog
    /// The wire payload of the last gesture that applied (an undo's payload
    /// carries the inverse op: an undo is an edit).
    Last: GridEdit option
    /// Why the last gesture did not apply, in the substrate's own terms.
    Refusal: string option
  }

let initial: Model =
  { Log = GridEditLog.create (Actor.Human "visitor") LiveTransformDefaults.EditHistory stock
    Last = None
    Refusal = None }

/// A cell as text: what a history line and the replay grid print.
let cellText (c: Cell) : string =
  match c with
  | Cell.Int i -> string i
  | Cell.Float f -> string f
  | Cell.Bool b -> if b then "true" else "false"
  | Cell.Str s -> s
  | Cell.Date d -> d
  | Cell.Timestamp t -> t
  | Cell.Decimal d -> d
  | Cell.Null -> "null"

/// The substrate's refusal, worded for the reader.
let private rejectionText (r: ColumnRejection) : string =
  match r with
  | NoSuchColumn(name, available) ->
    "there is no column '"
    + name
    + "' (the table has "
    + String.concat ", " available
    + ")"
  | DuplicateColumn name -> "a column named '" + name + "' already exists"
  | RowOutOfRange(row, count) -> "row " + string row + " is outside the table's " + string count + " rows"
  | CellTypeMismatch(column, expected, got) ->
    "column '" + column + "' holds " + expected + ", and the edit carried " + got
  | ColumnLengthMismatch(column, expected, got) ->
    "column '"
    + column
    + "' needs "
    + string expected
    + " values and the edit carried "
    + string got
  | RowShapeUnknownColumn(name, _) -> "a row names an unknown column '" + name + "'"
  | TransformRejected detail -> "the pipeline was refused: " + detail
  | NotInvertible op -> "the op " + op + " has no inverse"

let private refusalText (r: EditRefusal) : string =
  match r with
  | Refused rejection -> "Refused: " + rejectionText rejection + "."
  | NothingToUndo -> "Nothing to undo."
  | NothingToRedo -> "Nothing to redo."

/// The row a key names in the live table.
let private rowOf (sku: string) (t: Table) : int option =
  Table.tryColumn RowKeyField t
  |> Option.bind (fun keys ->
    List.init (Table.rowCount t) id
    |> List.tryFind (fun i -> Fuaran.Core.Column.cell i keys = Cell.Str sku))

/// THE page's own code: the gesture names one cell, so it IS one `SetCell`.
let opOf (sku: string) (column: string) (value: Cell) (t: Table) : Result<ColumnOp, string> =
  match rowOf sku t with
  | Some row -> Ok(SetCell(column, row, value))
  | None -> Error("Refused: no row has " + RowKeyField + " '" + sku + "'.")

/// Apply one gesture. Undo and redo are the log's, not this page's.
let step (gesture: Gesture) (model: Model) : Model =
  let applied =
    match gesture with
    | Edit(sku, column, value) ->
      opOf sku column value (GridEditLog.table model.Log)
      |> Result.bind (fun op -> GridEditLog.apply op model.Log |> Result.mapError refusalText)
    | Undo -> GridEditLog.undo model.Log |> Result.mapError refusalText
    | Redo -> GridEditLog.redo model.Log |> Result.mapError refusalText

  match applied with
  | Ok(log, edit) ->
    { Log = log
      Last =
        Some
          { Key = StockKey
            RowKeyField = Some RowKeyField
            Op = edit.Op }
      Refusal = None }
  | Error why -> { model with Refusal = Some why }

/// The record replayed from the log's base through its first `count` ops.
let replayTo (count: int) (model: Model) : Result<Table, string> =
  GridEditLog.records model.Log
  |> List.truncate count
  |> OpStream.replay ColumnOps.streamWitness (GridEditLog.baseTable model.Log)
  |> Result.mapError (fun (i, r) -> "record " + string i + ": " + rejectionText r)

/// A grid value as the editor delivers it, as a cell. A whole number is an `Int`;
/// anything else goes to the log as it came, so a wrong type is refused by the
/// substrate rather than coerced here.
let cellOfValue (v: CellValue) : Cell =
  match v with
  | CellValue.Numeric n when n = floor n && abs n < 2147483647.0 -> Cell.Int(int n)
  | CellValue.Numeric n -> Cell.Float n
  | CellValue.Text s -> Cell.Str s
  | CellValue.Bool b -> Cell.Bool b
  | CellValue.Date d -> Cell.Str(string d)
  | CellValue.Decimal d -> Cell.Decimal d
  | CellValue.Empty -> Cell.Null

/// One history line: what the op did, in words.
let describe (op: ColumnOp) : string =
  match op with
  | SetCell(column, row, value) -> "set " + column + " of row " + string row + " to " + cellText value
  | other -> ColumnOps.encode other

// ─── the grids ───────────────────────────────────────────────────────────────

/// The table's rows as the grid reads them: numbers as floats, text as text.
let rowsOf (t: Table) : Row list =
  [ for i in 0 .. Table.rowCount t - 1 ->
      t.Columns
      |> List.map (fun c ->
        let value: obj =
          match Fuaran.Core.Column.cell i c with
          | Cell.Int n -> box (float n)
          | Cell.Float f -> box f
          | other -> box (cellText other)

        c.Name, value)
      |> Map.ofList ]

let private rowText (field: string) (r: Row) : string =
  Map.tryFind field r |> Option.map string |> Option.defaultValue ""

let private rowNumber (field: string) (r: Row) : float =
  match Map.tryFind field r with
  | Some(:? float as f) -> f
  | _ -> nan

let private numberColumn (label: string) (field: string) : Column<'Msg> =
  { Column.numeric label (rowNumber field) with
      Format = CellFormat.Number(Some 0, None) }

let private stockGrid (id: string) (editable: bool) (columns: Column<'Msg> list) (t: Table) : Node<'Msg> =
  Fuaran.grid
    id
    (fun (r: Row) -> r)
    { Defaults.grid<Row, 'Msg> with
        Source = Binding.Static(Some(Seq.ofList (rowsOf t)))
        RowKey = rowText RowKeyField
        RowKeyField = Some RowKeyField
        Editable = editable
        Columns = columns }

/// The live grid: quantity and price are editable, and an edit dispatches the gesture.
let editGrid (t: Table) : Node<Gesture> =
  let editable (field: string) (col: Column<Gesture>) =
    col
    |> Column.editable (fun r v -> Action.dispatch (Edit(rowText RowKeyField r, field, cellOfValue v)))

  stockGrid
    "undo-grid-live"
    true
    [ Column.text "SKU" (rowText "sku")
      Column.text "Item" (rowText "item")
      numberColumn "Qty" "qty" |> editable "qty"
      numberColumn "Price (p)" "price" |> editable "price" ]
    t

/// The replayed table, read-only.
let replayGrid (t: Table) : Node<obj> =
  stockGrid
    "undo-grid-replay"
    false
    [ Column.text "SKU" (rowText "sku")
      Column.text "Item" (rowText "item")
      numberColumn "Qty" "qty"
      numberColumn "Price (p)" "price" ]
    t

// ─── the page ────────────────────────────────────────────────────────────────

let private shortHash (h: string) : string =
  if h.Length > 10 then h.Substring(0, 10) + "…" else h

[<ReactComponent>]
let private UndoGridView () : ReactElement =
  let model, dispatch = React.useReducer ((fun m g -> step g m), initial)
  let scrub, setScrub = React.useState (None: int option)

  let records = GridEditLog.records model.Log
  let count = List.length records
  let at = scrub |> Option.map (fun k -> min k count) |> Option.defaultValue count
  let live = GridEditLog.table model.Log

  let button (label: string) (enabled: bool) (gesture: Gesture) =
    Html.button
      [ prop.className "ug-action"
        prop.disabled (not enabled)
        prop.text label
        prop.onClick (fun _ -> dispatch gesture) ]

  let editPanel =
    Exhibit.panel
      "Edit, then undo"
      "Change a quantity or a price. Each change is recorded as one op that names the cell. Undo applies that op's inverse; redo applies the op again. Try a fractional quantity to see the table refuse it."
      [ Html.div
          [ prop.className "ug-actions"
            prop.children
              [ button "Undo" (GridEditLog.canUndo model.Log) Undo
                button "Redo" (GridEditLog.canRedo model.Log) Redo ] ]
        match model.Refusal with
        | Some why -> Html.p [ prop.className "ug-refusal"; prop.role "status"; prop.text why ]
        | None -> Html.none
        Html.div
          [ prop.className "ug-stage"
            prop.children [ Render.renderWithSources BindingResolver.empty dispatch (editGrid live) ] ]
        match model.Last with
        | Some edit -> Exhibit.wireDrawer "The last gesture, as the payload a host would send" (GridEdit.encode edit)
        | None -> Html.none ]

  let historyPanel =
    let line (r: OpRecord<ColumnOp>) =
      Html.li
        [ prop.className (
            if r.Seq < at then
              "ug-record"
            else
              "ug-record ug-record-after"
          )
          prop.children
            [ Html.span [ prop.className "ug-seq"; prop.text ("#" + string r.Seq) ]
              Html.span [ prop.className "ug-op"; prop.text (describe r.Op) ]
              Html.code [ prop.className "ug-hash"; prop.text (shortHash r.Hash) ] ] ]

    let integrity =
      match GridEditLog.verify model.Log with
      | None -> "Chain intact: every record's hash recomputes and links to the one before it."
      | Some broken -> "Chain broken at record " + string broken.Index + "."

    Exhibit.panel
      "The record"
      "Every op is appended, undos included: an undo does not delete a line, it adds the inverse. Each line carries a hash over the op and the line before it."
      [ if count = 0 then
          Html.p [ prop.className "ug-empty"; prop.text "No edits yet. Change a cell above." ]
        else
          Html.ol [ prop.className "ug-records"; prop.children (records |> List.map line) ]
        Html.p [ prop.className "ug-integrity"; prop.text integrity ] ]

  let replayPanel =
    let replayed = replayTo at model
    let noRecords = List.isEmpty records

    let verdict =
      match replayed with
      | Error why -> "Replay refused at " + why + "."
      | Ok t when at = count && rowsOf t = rowsOf live ->
        "Replaying all " + string count + " ops reproduces the live table."
      | Ok _ when at = count -> "Replaying every op did NOT reproduce the live table."
      | Ok _ -> "The table as it stood after op " + string at + " of " + string count + "."

    Exhibit.panel
      "Replay"
      "Move the slider to replay the record from the starting table. Nothing here was saved along the way: each table is rebuilt from the ops."
      [ Html.div
          [ prop.className "ug-scrub"
            prop.children
              [ Html.label
                  [ prop.htmlFor "ug-scrub"
                    prop.text ("Ops replayed: " + string at + " of " + string count) ]
                Html.input
                  [ prop.id "ug-scrub"
                    prop.type'.range
                    prop.min 0
                    prop.max count
                    prop.value at
                    prop.disabled noRecords
                    prop.onChange (fun (v: int) -> setScrub (if v >= count then None else Some v)) ] ] ]
        Html.p [ prop.className "ug-verdict"; prop.text verdict ]
        match replayed with
        | Ok t ->
          Html.div
            [ prop.className "ug-stage"
              prop.children [ Render.renderWithSources BindingResolver.empty ignore (replayGrid t) ] ]
        | Error _ -> Html.none ]

  Exhibit.shell
    "undo-grid"
    "The Undo Grid"
    "Edit a grid, undo it and replay the history. The page has no undo code: every edit is an op, and undo, redo and the replayable record come from the published edit log."
    [ editPanel; historyPanel; replayPanel ]
    [ Exhibit.Claim.Verified
        "Undo, redo, the hashed record and the replay are the published edit log's. The page's own code turns a cell gesture into the op that names that cell, and nothing else."
      Exhibit.Claim.Verified
        "The last gesture is shown as the edit payload a host would send: the State key, the grid's row-identity column and the op. An undo's payload carries the inverse op."
      Exhibit.Claim.Limit
        "The grid commits on every keystroke, so typing 120 over 40 records three edits (1, 12, 120). The record shows exactly what the grid sent."
      Exhibit.Claim.Limit
        "The edit cells are handlers in this page, so this grid does not survive the wire. The renderer commits a declared grid edit as whole rows, not as an op; turning that into an op would mean diffing rows, which is what this design avoids."
      Exhibit.Claim.Limit
        "The record is bounded at the log's default depth. Past it the oldest op folds into the starting table, which stays replayable from where the fold left it." ]

let page: ReactElement = UndoGridView()
