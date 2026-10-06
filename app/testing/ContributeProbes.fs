module Fuaran.Live.Testing.ContributeProbes

// ============================================================================
//  TEST-ONLY. The corpus sink's flat surfaces: the guard's findings, the origins,
//  the bundle build, the prepare path and the contribution probe.
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that `Contribute.fs` holds only what the playground runs.
// ============================================================================

open Fable.Core
open Fuaran.Live
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Live.Ports

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson
open Fuaran.Live.Contribute

// ─── flat surfaces for the headless suite ────────────────────────────────────
//
// F# `Result`s, DUs and lists are awkward across the Fable boundary, so — as
// `Session.ingestResult` and `Console.runLine` already do — the guard and the
// prepare path project to plain values.

/// `findings` as a plain array.
let findingsFlat (json: string) : string array = findings json |> Array.ofList

/// The provider origins as a plain array, so the test can assert this list, the
/// adapter registry's and the CSP module's are one set.
let providerOriginsFlat () : string array = providerOrigins |> Array.ofList

/// `build` from flat arguments.
let buildFlat (providerId: string) (modelId: string) (capturedAt: string) (session: Session.SessionState) : string =
  build
    { ProviderId = providerId
      ModelId = modelId
      CapturedAt = capturedAt }
    session

/// `prepare`, flattened: `Ok` plus the payload, or the refusal reason.
let prepareFlat
  (providerId: string)
  (modelId: string)
  (capturedAt: string)
  (session: Session.SessionState)
  : {| Ok: bool
       Reason: string
       Json: string |}
  =
  match
    prepare
      { ProviderId = providerId
        ModelId = modelId
        CapturedAt = capturedAt }
      session
  with
  | Ok(ContributionBundle.Verified json) ->
    {| Ok = true
       Reason = ""
       Json = json |}
  | Error reason ->
    {| Ok = false
       Reason = reason
       Json = "" |}

/// The WHOLE path a click takes — prepare, then post through a sink built for
/// `endpoint` — projected flat. This is what the guard test drives, so what it
/// exercises is the shipped sequence rather than a re-assembly of it: an
/// endpoint of `""` is the public build, and a refused prepare must never reach
/// the sink at all.
let contributeProbeFlat
  (endpoint: string)
  (providerId: string)
  (modelId: string)
  (capturedAt: string)
  (session: Session.SessionState)
  : JS.Promise<{| Outcome: string; Reason: string |}> =
  async {
    match
      prepare
        { ProviderId = providerId
          ModelId = modelId
          CapturedAt = capturedAt }
        session
    with
    | Error reason ->
      return
        {| Outcome = "refused"
           Reason = reason |}
    | Ok bundle ->
      let! outcome = (sinkTo endpoint).Post bundle

      return
        match outcome with
        | ContributionOutcome.Sent -> {| Outcome = "sent"; Reason = "" |}
        | ContributionOutcome.Refused reason ->
          {| Outcome = "refused"
             Reason = reason |}
        | ContributionOutcome.Failed reason ->
          {| Outcome = "failed"
             Reason = reason |}
  }
  |> Async.StartAsPromise
