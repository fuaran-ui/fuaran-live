module Fuaran.Live.Testing.RefineProbes

// ============================================================================
//  TEST-ONLY. The refine panel's flat diagnostic surface (change and retention lines as plain arrays).
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Replay

module Decode = Fuaran.UI.Ops.JsonDecode
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module Diff = Fuaran.UI.OpStream.Replay.TreeDiff

open Fuaran.Live
open Fuaran.Live.Refine

let changeLines (session: Session.SessionState) (baseline: Baseline option) : string array =
  changes session baseline |> List.map changeLine |> Array.ofList

/// The edited ids the re-emission left untouched.
let retainedIds (session: Session.SessionState) (baseline: Baseline option) : string array =
  retention session baseline |> List.filter snd |> List.map fst |> Array.ofList

/// The edited ids the re-emission changed anyway — the honest half.
let overwrittenIds (session: Session.SessionState) (baseline: Baseline option) : string array =
  retention session baseline
  |> List.filter (snd >> not)
  |> List.map fst
  |> Array.ofList
