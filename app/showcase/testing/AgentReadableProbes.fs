module Fuaran.Showcase.Testing.AgentReadableProbes

// ============================================================================
//  TEST-ONLY. The agent-readable page's annotation set as one canonical JSON document.
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fable.Core
open Fuaran.Live.Interop
open Fable.Core.JsInterop
open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Affordances
open Fuaran.Showcase.AgentReadable

/// The whole annotation set as one canonical JSON document. Exported so the
/// repository's own test suite can certify the payloads this page hangs on its
/// controls – the shapes are a contract a reader relies on, so they are pinned
/// rather than trusted.
let annotationsJson: string =
  Canon.render (
    JArr
      [ for a in annotations ->
          JObj
            [ "field", JStr a.FieldId
              "attributes", JObj [ for name, value in a.Attributes -> name, JStr value ] ] ]
  )

