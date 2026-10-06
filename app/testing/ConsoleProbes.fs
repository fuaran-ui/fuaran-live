module Fuaran.Live.Testing.ConsoleProbes

// ============================================================================
//  TEST-ONLY. The console's flat surfaces: the parser, the log and one headless run.
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that `Console.fs` holds only what the playground runs.
// ============================================================================

open Fable.Core
open Fuaran.Live
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Telemetry.Abstractions

module Decode = Fuaran.UI.Ops.JsonDecode
open Fuaran.Live.Console

// ─── flat surfaces for the headless suite ────────────────────────────────────
//
// F# DUs and records are awkward to assert on across the Fable boundary, so —
// exactly as `Session.ingestResult` and the navigator's cursor helpers do — the
// parser and the log project to plain strings.

/// `parse` as `"ok|<case>|<args…>"` / `"error|<message>"`.
let parseFlat (line: string) : string =
  match parse line with
  | Error message -> "error|" + message
  | Ok query ->
    let case =
      match query with
      | Query.NodeState id -> "getNodeState|" + id
      | Query.BindingValue(id, slot) -> "getBindingValue|" + id + "|" + slot
      | Query.RenderedDom id -> "getRenderedDom|" + id
      | Query.InspectTree -> "inspectTree"
      | Query.FindNodes kind -> "findNodes|" + kind
      | Query.Affordances moduleId -> "getAffordances|" + Option.defaultValue "" moduleId
      | Query.TreeRevision -> "treeRevision"
      | Query.Apply opJson -> "apply|" + opJson
      | Query.Help -> "help"

    "ok|" + case

/// Every log entry as `"<level>|<head>|<detail>"`, oldest first.
let logFlat (state: State) : string array =
  state.Log
  |> List.rev
  |> List.map (fun e ->
    let level =
      match e.Level with
      | Level.Info -> "info"
      | Level.Refused -> "refused"
      | Level.Failed -> "failed"

    level + "|" + e.Head + "|" + e.Detail)
  |> Array.ofList

/// Run one line against a session — the headless entry point, projected flat in
/// the same shape as `Session.ingestResult` / `PropertyEditor.commitAt`.
/// `Applied` false leaves `Next` as the input session, so a test can assert the
/// tree really was untouched.
let runLine
  (session: Session.SessionState)
  (line: string)
  : {| Applied: bool
       Log: string array
       Next: Session.SessionState |}
  =
  let outcome = run { empty with Input = line } session

  {| Applied = outcome.Session.IsSome
     Log = logFlat outcome.State
     Next = Option.defaultValue session outcome.Session |}
