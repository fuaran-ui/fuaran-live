module Fuaran.Live.Testing.LiveProbes

// ============================================================================
//  TEST-ONLY. The live-drive channel's flat diagnostic surface (delta envelopes and their codec).
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Fuaran.Live.Ports
open Fuaran.Live
open Fuaran.Live.Live

/// The present→audience delta as encoded channel envelope strings (a JS array).
let deltaEnvelopes (previous: Session.SessionState) (next: Session.SessionState) : string[] =
  liveDriveDelta previous next |> List.map encodeMessage |> List.toArray

/// The kind an envelope decodes to ("tree" / "op"), or "" if it is not valid.
let envelopeKind (envelope: string) : string =
  match decodeMessage envelope with
  | Some(LiveDriveMessage.FullTree _) -> "tree"
  | Some(LiveDriveMessage.Op _) -> "op"
  | None -> ""

/// The wire payload an envelope carries (the tree/op JSON), or "" if invalid.
let envelopePayload (envelope: string) : string =
  match decodeMessage envelope with
  | Some(LiveDriveMessage.FullTree j) -> j
  | Some(LiveDriveMessage.Op j) -> j
  | None -> ""

/// Does an envelope survive a decode → re-encode round-trip byte-identically?
let envelopeRoundTrips (envelope: string) : bool =
  match decodeMessage envelope with
  | Some m -> encodeMessage m = envelope
  | None -> false
