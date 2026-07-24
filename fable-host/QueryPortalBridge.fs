module Fuaran.Live.QueryPortalBridge

// ============================================================================
//  Fable export bridge – exposes the shipped `Fuaran.UI` typed-data core
//  (QueryBinding / RetrievalSource) to the fuaran-live TypeScript shell as
//  callable JS functions.
//
//  The hybrid architecture: the TS driver edges (DuckDB-WASM / serverless HTTP /
//  retrieval) fetch rows and produce a result SCHEMA; this bridge runs the
//  shipped `Fuaran.UI.QueryBinding.check` so a type-mismatched dashboard is
//  rejected BEFORE render – the "the result schema *is* the UI contract" gate,
//  executed by the F# core, NOT reimplemented in TS (no second type system to
//  drift). One implementation of the relation; two callers (F# tests + TS shell).
//
//  Boundary discipline: string/JSON in, plain JS object out. The check needs
//  only the schema (name + ColumnType pairs) – NO row data crosses here, which
//  is also exactly the schema-only privacy path. Schema JSON shape:
//    [ { "name": "revenue", "type": "float" }, … ]   (type ∈ the 6 ColumnType tags)
//  Dashboard: a canonical Fuaran wire-format JSON string (the same the LLM emits
//  and the renderer consumes).
//
//  This file carries no `Program.run` – it is a pure function module, so
//  importing its Fable output runs no Elmish boot (unlike Host.fs).
// ============================================================================

open Fable.Core.JsInterop
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

/// Parse the schema JSON array into a `Fuaran.Core.Schema`, reusing the
/// Fable-clean `Fuaran.Core.Wire` JSON parser. A malformed schema is a typed
/// error string (default-deny – a bad schema is a defect, never a guess).
let private parseSchema (json: string) : Result<Schema, string> =
  match Json.parse json with
  | Error m -> Error("schema is not valid JSON: " + m)
  | Ok(JArr entries) ->
    let rec go acc remaining =
      match remaining with
      | [] -> Ok(List.rev acc)
      | JObj fields :: rest ->
        let field k =
          fields |> List.tryPick (fun (fk, fv) -> if fk = k then Some fv else None)

        match field "name", field "type" with
        | Some(JStr name), Some(JStr ty) ->
          match ColumnType.ofTag ty with
          | Some ct -> go ((name, ct) :: acc) rest
          | None -> Error("unknown column type '" + ty + "'")
        | _ -> Error "each schema entry needs a string 'name' and 'type'"
      | _ -> Error "each schema entry must be a JSON object"

    go [] entries
  | Ok _ -> Error "schema must be a JSON array"

/// Project one typed defect to a JS-friendly object (the §4d AI-recovery shape).
let private defectToJs (d: QueryBinding.Defect) : obj =
  createObj
    [ "code" ==> d.Code
      "nodeId" ==> d.NodeId
      "column" ==> d.Column
      "sink" ==> QueryBinding.BindingSinkClass.label d.Sink
      "message" ==> d.Message
      "availableFields" ==> (d.AvailableFields |> List.toArray)
      "suggestion"
      ==> (match d.Suggestion with
           | Some s -> box s
           | None -> null) ]

/// Type-check a dashboard (canonical wire JSON) against a resolved result schema
/// (JSON array). Returns `{ ok, defects, error? }`:
///   `ok`      – true when every query-bound sink types against the schema;
///   `defects` – the typed FUARAN066 / FUARAN067 defects (empty when `ok`);
///   `error`   – a schema-parse / wire-decode failure (also `ok: false`).
/// The "can't render wrong" gate: the TS shell renders only when `ok` is true.
let checkDashboard (schemaJson: string, dashboardWireJson: string) : obj =
  match parseSchema schemaJson with
  | Error e -> createObj [ "ok" ==> false; "error" ==> e; "defects" ==> ([||]: obj[]) ]
  | Ok schema ->
    match JsonDecode.decodeNode dashboardWireJson with
    | Error de ->
      createObj
        [ "ok" ==> false
          "error" ==> (de.Code + ": " + de.Message)
          "defects" ==> ([||]: obj[]) ]
    | Ok node ->
      // `decodeNode` yields a `WireTree`; the schema-check reads structure only, so
      // reify to the raw `Node<obj>` `QueryBinding.check` walks.
      let defects = QueryBinding.check schema (WireTree.reify node)

      createObj
        [ "ok" ==> List.isEmpty defects
          "defects" ==> (defects |> List.map defectToJs |> List.toArray) ]

/// The canonical retrieval-hit schema (Phase 325) as a JSON-friendly
/// `{ name, type }[]` – so the TS retrieval surface types a case-history
/// dashboard against the SAME `RetrievalSource.hitSchema` the F# core defines
/// (one contract for the relational and retrieval planes alike).
let retrievalHitSchema () : obj =
  RetrievalSource.hitSchema
  |> List.map (fun (name, ct) -> createObj [ "name" ==> name; "type" ==> ColumnType.tag ct ])
  |> List.toArray
  |> box

/// Project a `Fuaran.Core.Schema` to the JS `{ name, type }[]` shape.
let private schemaToJs (schema: Schema) : obj =
  schema
  |> List.map (fun (name, ct) -> createObj [ "name" ==> name; "type" ==> ColumnType.tag ct ])
  |> List.toArray
  |> box

/// Marshal one JSON value into a `Cell` of the declared column type. A missing /
/// shape-mismatched value becomes `Null` (best-effort over already-fetched data –
/// the refinement is local compute, not a strict re-decode of untrusted wire).
let private cellOf (ty: ColumnType) (v: JVal option) : Cell =
  match ty, v with
  | IntType, Some(JInt i) -> Int i
  | FloatType, Some(JFloat f) -> Float f
  | FloatType, Some(JInt i) -> Float(float i)
  | BoolType, Some(JBool b) -> Bool b
  | StringType, Some(JStr s) -> Str s
  | DateType, Some(JStr s) -> Date s
  | TimestampType, Some(JStr s) -> Timestamp s
  | _ -> Null

/// Project a realised `Cell` back to a JS scalar (`Null` → JS `null`).
let private cellToJs (c: Cell) : obj =
  match c with
  | Int i -> box i
  | Float f -> box f
  | Bool b -> box b
  | Str s -> box s
  | Date s -> box s
  | Timestamp s -> box s
  | Null -> null

/// Build a column-oriented `Table` from a `Schema` + row-objects (the shape the TS
/// shell already holds – `{ col: value }[]`), so the bridge owns the row↔columnar
/// marshalling and the TS side passes the rows it has in hand.
let private tableOfRows (schema: Schema) (rows: (string * JVal) list list) : Table =
  let columns =
    schema
    |> List.map (fun (name, ty) ->
      let cells =
        rows
        |> List.map (fun fields ->
          cellOf ty (fields |> List.tryPick (fun (k, fv) -> if k = name then Some fv else None)))

      Column.create name ty cells)

  { Schema = schema; Columns = columns }

/// Project a `Table` back to row-objects (`{ col: value }[]`) for the TS renderer.
let private tableToRowsJs (t: Table) : obj =
  let n = Table.rowCount t

  [| for i in 0 .. n - 1 -> createObj [ for c in t.Columns -> c.Name ==> cellToJs (Column.cell i c) ] |]
  |> box

/// Apply a local refinement to ALREADY-FETCHED rows and re-type the dashboard –
/// the Phase 324 fast-path, run by the shipped `Fuaran.UI.QueryRefine.refineLocally`
/// (the pinned `Fuaran.Core.DataFrame` evaluator), NOT a TS re-implementation of
/// the algebra. A follow-on tweak ("sort descending", "filter region", "regroup by
/// month") thus costs ZERO re-query and ZERO LLM tokens by construction.
///
/// Inputs (all JSON strings; no closures cross):
///   `schemaJson`    – the in-hand result schema (`{ name, type }[]`);
///   `rowsJson`      – the already-fetched rows as `{ col: value }[]` (rows in hand,
///                     never re-fetched);
///   `pipelineJson`  – the refinement as a canonical `Transform[]` pipeline;
///   `dashboardJson` – the current dashboard as canonical wire JSON.
///
/// Returns `{ ok, schema?, rows?, defects?, error? }`:
///   `ok`      – true when the refinement evaluated AND the dashboard still types
///               against the refined schema (render the refined data locally);
///   `schema`  – the refined result schema (the new UI contract);
///   `rows`    – the refined rows as `{ col: value }[]`, so the TS shell re-renders
///               refined data with NO re-query and NO LLM token spend;
///   `defects` – the typed FUARAN066/067 defects when the refinement dropped /
///               re-typed a bound column (a refinement that would render wrong is a
///               typed defect, never a broken render – the 323 thread);
///   `error`   – a decode / evaluation failure (also `ok: false`; the caller's
///               signal to fall back to a fresh NL→query emission, the slow path).
let refineLocally (schemaJson: string, rowsJson: string, pipelineJson: string, dashboardJson: string) : obj =
  let fail (msg: string) =
    createObj [ "ok" ==> false; "error" ==> msg; "defects" ==> ([||]: obj[]) ]

  match parseSchema schemaJson with
  | Error e -> fail e
  | Ok schema ->
    let rows =
      match Json.parse rowsJson with
      | Ok(JArr entries) ->
        entries
        |> List.choose (fun e ->
          match e with
          | JObj fields -> Some fields
          | _ -> None)
        |> Ok
      | Ok _ -> Error "rows must be a JSON array of objects"
      | Error m -> Error("rows are not valid JSON: " + m)

    match rows with
    | Error e -> fail e
    | Ok rowFields ->
      match DataFrameCodec.decodePipeline pipelineJson with
      | Error e -> fail ("refinement pipeline is not valid: " + ColumnCodec.errorString e)
      | Ok pipeline ->
        match JsonDecode.decodeNode dashboardJson with
        | Error de -> fail (de.Code + ": " + de.Message)
        | Ok dashboard ->
          let table = tableOfRows schema rowFields

          match QueryRefine.refineLocally table pipeline (WireTree.reify dashboard) with
          | Ok(refined, _) ->
            createObj
              [ "ok" ==> true
                "schema" ==> schemaToJs refined.Schema
                "rows" ==> tableToRowsJs refined ]
          | Error(QueryRefine.RefineError.TypeMismatch defects) ->
            createObj
              [ "ok" ==> false
                "error"
                ==> QueryRefine.RefineError.message (QueryRefine.RefineError.TypeMismatch defects)
                "defects" ==> (defects |> List.map defectToJs |> List.toArray) ]
          | Error e -> fail (QueryRefine.RefineError.message e)
