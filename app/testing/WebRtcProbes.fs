module Fuaran.Live.Testing.WebRtcProbes

// ============================================================================
//  TEST-ONLY. The pairing signal codec's flat diagnostic surface.
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
open Fuaran.Live.WebRtc

/// The `kind` a signal token decodes to ("offer" / "answer"), or "" if invalid –
/// the codec projected to a flat string the headless tests assert on.
let signalKind (token: string) : string =
  match decodeSignal token with
  | Some(k, _) -> k
  | None -> ""

/// The SDP a signal token carries, or "" if invalid.
let signalSdp (token: string) : string =
  match decodeSignal token with
  | Some(_, sdp) -> sdp
  | None -> ""

/// Does a signal token survive a decode → re-encode round-trip byte-identically?
let signalRoundTrips (token: string) : bool =
  match decodeSignal token with
  | Some(k, sdp) -> encodeSignal k sdp = token
  | None -> false
