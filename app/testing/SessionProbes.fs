module Fuaran.Live.Testing.SessionProbes

// ============================================================================
//  TEST-ONLY. The session's flat diagnostic surface (ingest as a flat record, message lists, budget constants).
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Live.Ports

module Decode = Fuaran.UI.Ops.JsonDecode
module ApplyEngine = Fuaran.UI.Ops.Apply
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

open Fuaran.Live
open Fuaran.Live.Session

/// The budget constants as READABLE values. A `[<Literal>]` is inlined at every
/// use site and never reaches the module's exports, so a test that imported the
/// literal directly would silently receive `undefined` and assert nothing — the
/// budget check would pass whatever the budget was.
let correctionBudget: int = Session.correctionBudgetChars

/// EVERY message `buildMessages` produces, as a plain array — the accumulated
/// conversation as the provider would receive it. `lastMessageContent` answers
/// "what is injected"; this answers "what else is carried", which is the
/// question the refine loop turns on.
let allMessageContents (session: SessionState) (prompt: string) : string array =
  buildMessages session prompt |> List.map _.Content |> Array.ofList

/// `ingest`, projected to a flat record: `Ok` + the `Mode` ("tree" / "op") on
/// success, or `Error` (the failure kind) otherwise, with the resulting `Next`
/// session (the input session unchanged on failure).
let ingestResult
  (session: SessionState)
  (raw: string)
  : {| Ok: bool
       Mode: string
       Error: string
       Next: SessionState |}
  =
  match ingest session raw with
  | Ingested(mode, next) ->
    {| Ok = true
       Mode = mode
       Error = ""
       Next = next |}
  | IngestFailed e ->
    {| Ok = false
       Mode = ""
       Error = e.Kind
       Next = session |}
