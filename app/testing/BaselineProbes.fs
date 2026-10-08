module Fuaran.Live.Testing.BaselineProbes

// ============================================================================
//  TEST-ONLY. The measurement baseline's pending template.
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Fuaran.Live
open Fuaran.Live.Measure.Baseline

/// The pending template: every metric declared, every value null. Emitted by
/// nothing in the ordinary path — it exists so that "what does this artefact
/// look like before it is captured" has one answer, and so a harness that
/// produced no usable sample cannot present as a captured run.
let pendingTemplate () : string =
  json
    "pending"
    ""
    { Dotnet = ""; Os = ""; Cpu = "" }
    (catalogue ()
     |> List.map (fun (id, unit, note) ->
       { Id = id
         Value = None
         Unit = unit
         Note = note }))
