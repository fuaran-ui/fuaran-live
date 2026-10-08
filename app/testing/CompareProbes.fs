module Fuaran.Live.Testing.CompareProbes

// ============================================================================
//  TEST-ONLY. The comparison's flat validity check.
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fuaran.Live.Ports
open Fuaran.Live
open Fuaran.Live.Compare

/// Is `text` a valid Fuaran emission? Reuses the app's own loop – it decodes +
/// applies into a typed tree, or it is a typed, named failure. (Exposed flat for
/// the comparison test.)
let fuaranValidates (text: string) : bool =
  match Session.ingest Session.empty text with
  | Session.Ingested _ -> true
  | Session.IngestFailed _ -> false
