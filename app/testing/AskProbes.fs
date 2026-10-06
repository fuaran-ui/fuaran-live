module Fuaran.Live.Testing.AskProbes

// ============================================================================
//  TEST-ONLY. The ask surface's flat answer builder.
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that `Ask.fs` holds only what the playground runs.
// ============================================================================

open Fable.Core
open Fuaran.Live
open Fable.Core.JsInterop
open Feliz
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module Decode = Fuaran.UI.Ops.JsonDecode
module Elc = Fuaran.UI.OpStream.Abstractions.Elicitation
open Fuaran.Live.Ask

/// Flat headless wrapper: `stateJson` is a plain `{"<stateKey>": <scalar>}`
/// object standing in for the scoped store; the contract comes from the
/// envelope wire. Returns the canonical answer object JSON ("" on bad input).
let buildAnswerJsonFlat (stateJson: string) (envelopeWire: string) : string =
  match Elc.decodeEnvelope envelopeWire with
  | Error _ -> ""
  | Ok env ->
    match Json.parse stateJson with
    | Ok(JObj fields) ->
      let getState (key: string) : obj option =
        fields
        |> List.tryPick (fun (k, v) ->
          if k <> key then
            None
          else
            match v with
            | JStr s -> Some(box s)
            | JInt i -> Some(box (float i))
            | JFloat f -> Some(box f)
            | JBool b -> Some(box b)
            | _ -> None)

      buildAnswerJson getState env.Contract
    | _ -> ""
