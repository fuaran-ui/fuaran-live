module Fuaran.Live.Testing.OpLogProbes

// ============================================================================
//  TEST-ONLY. The op log's flat diagnostic surface (origins, counters, n-step undo/redo, export replay).
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fable.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Live.Ports

module Decode = Fuaran.UI.Ops.JsonDecode
module ApplyEngine = Fuaran.UI.Ops.Apply
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

open Fuaran.Live
open Fuaran.Live.OpLog

// ─── flat diagnostic surface (cross-boundary friendly) ───────────────────────
//
// F# lists, records and DUs are awkward to assert on from the JS side of the
// Fable boundary, so — exactly as `Session.ingestResult` and the Phase 710/711
// helpers do — the same values are projected to plain arrays and flat records.
// These are the headless test surface AND a host-language-agnostic description
// of what the record holds.

/// `"human"` / `"agent"` per recorded op, redo tail included — the origin
/// marker, as the acceptance criterion asks it: are the two distinguishable?
let originKinds (session: Session.SessionState) : string array =
  session.Log
  |> List.map (fun e ->
    match e.Actor with
    | Human _ -> "human"
    | Agent _ -> "agent")
  |> Array.ofList

/// The attribution id per recorded op (`"navigator"` for a panel edit).
let originIds (session: Session.SessionState) : string array =
  session.Log |> List.map (fun e -> Actor.id e.Actor) |> Array.ofList

/// The op kind (`"UpdateProp"`, `"Batch"`, …) per recorded op.
let logKinds (session: Session.SessionState) : string array =
  session.Log |> List.map (fun e -> e.OpKind) |> Array.ofList

/// The chain hash per recorded op.
let logHashes (session: Session.SessionState) : string array =
  session.Log |> List.map (fun e -> e.Hash) |> Array.ofList

/// The applied ops as a plain array (an F# list is a linked structure across
/// the Fable boundary, so a `.length` on it reads as an object with no length).
let appliedOps (session: Session.SessionState) : string array = Array.ofList session.Ops

/// How many snapshots the session holds. The invariant replay rests on is
/// `snapshotCount = cursor + 1` — the base, plus one tree per applied op.
let snapshotCount (session: Session.SessionState) : int = List.length session.Snapshots

/// `undo` applied `n` times, stopping early if there is nothing left to undo.
let undoN (session: Session.SessionState) (n: int) : Session.SessionState =
  let mutable s = session

  for _ in 1..n do
    match undo s with
    | Some next -> s <- next
    | None -> ()

  s

/// `redo` applied `n` times, stopping early at the end of the redo tail.
let redoN (session: Session.SessionState) (n: int) : Session.SessionState =
  let mutable s = session

  for _ in 1..n do
    match redo s with
    | Some next -> s <- next
    | None -> ()

  s

/// `verify`, flattened for assertion across the boundary.
let verifyResult
  (session: Session.SessionState)
  : {| ReplayOk: bool
       ChainOk: bool
       Steps: int |}
  =
  let r = verify session

  {| ReplayOk = r.ReplayOk
     ChainOk = r.ChainOk
     Steps = r.Steps |}

/// Replay an EXPORTED document through the public decode + apply engines and
/// return the canonical JSON of the tree it reproduces (`""` when the document
/// is malformed or an op fails to apply). This is the acceptance criterion made
/// executable: compare it with the document's own `tree` field and the export
/// either reproduces the session or it does not.
[<Emit("(function(j){ try { var p = JSON.parse(j); return JSON.stringify(p.base); } catch(e){ return ''; } })($0)")>]
let private exportedBase (json: string) : string = jsNative

[<Emit("(function(j){ try { var p = JSON.parse(j); return (p.ops||[]).map(function(o){ return JSON.stringify(o.op); }); } catch(e){ return []; } })($0)")>]
let private exportedOps (json: string) : string array = jsNative

[<Emit("(function(j){ try { var p = JSON.parse(j); return JSON.stringify(p.tree); } catch(e){ return ''; } })($0)")>]
let private exportedTreeRaw (json: string) : string = jsNative

/// The tree the exported document CLAIMS is final, read back through the real
/// strict decoder and re-encoded canonically (`""` when absent or undecodable).
/// Normalising through the encoder is what makes a comparison with `replayExport`
/// a statement about the TREES rather than about JSON formatting.
let exportedTree (json: string) : string =
  let raw = exportedTreeRaw json

  if raw = "" || raw = "null" then
    ""
  else
    match Decode.decodeNode raw with
    | Error _ -> ""
    | Ok node -> Canon.encodeNode (WireTree.reify node)

let replayExport (json: string) : string =
  let baseJson = exportedBase json

  if baseJson = "" || baseJson = "null" then
    ""
  else
    match Decode.decodeNode baseJson with
    | Error _ -> ""
    | Ok node ->
      let folded =
        exportedOps json
        |> Array.fold
          (fun acc opJson ->
            acc
            |> Option.bind (fun tree ->
              match Decode.decodeOp opJson with
              | Error _ -> None
              | Ok op ->
                match ApplyEngine.apply op tree with
                | Error _ -> None
                | Ok next -> Some next))
          (Some(WireTree.reify node))

      match folded with
      | Some tree -> Canon.encodeNode tree
      | None -> ""
