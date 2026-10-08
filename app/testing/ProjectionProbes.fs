module Fuaran.Live.Testing.ProjectionProbes

// ============================================================================
//  TEST-ONLY. The projector's flat string surface (projection and spans by target name).
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fable.Core.JsInterop
open Fuaran.UI.AiWire
open Fuaran.Live
open Fuaran.Live.Projection

// ─── flat test surface (headless unit coverage) ──────────────────────────────

/// The `Target` a flat target name selects – the vitest surface's vocabulary.
let private targetNamed (targetName: string) : Target =
  match targetName with
  | "json" -> Target.Json
  | "typescript" -> Target.TypeScript
  | "python" -> Target.Python
  | "fsharp" -> Target.FSharp
  | "csharp" -> Target.CSharp
  | "vb" -> Target.VisualBasic
  | "vbfluent" -> Target.VisualBasicFluent
  | "go" -> Target.Go
  | "kotlin" -> Target.Kotlin
  | "rust" -> Target.Rust
  | "swift" -> Target.Swift
  | _ -> Target.Json

/// Project by target name ("json"/"typescript"/"python"/"fsharp"/"csharp"/"vb"/"vbfluent"/…)
/// – a flat string surface assertable from vitest over the Fable output, so the
/// projector's never-crash + per-language shape are testable headlessly.
let projectByName (targetName: string) (wireJson: string) : string =
  projectTo (targetNamed targetName) wireJson

/// Every node id this language's projection maps, in document order – the side
/// map, flattened for the Fable boundary.
let spanIdsByName (targetName: string) (wireJson: string) : string array =
  let projected = projectSpans (targetNamed targetName) wireJson

  projected.Spans
  |> List.sortBy (fun s -> s.Start)
  |> List.map (fun s -> s.NodeId)
  |> Array.ofList

/// The projected source a node id maps to, or `""` when this language does not
/// project the node at all.
let spanTextByName (targetName: string) (wireJson: string) (nodeId: string) : string =
  let projected = projectSpans (targetNamed targetName) wireJson

  match spanFor projected nodeId with
  | Some s -> projected.Text.Substring(s.Start, s.Length)
  | None -> ""

/// The projected source the nearest-enclosing resolution of a cursor id-path
/// (root → focused) lands on, or `""` when nothing on the path is projected.
let spanPathTextByName (targetName: string) (wireJson: string) (idPath: string array) : string =
  let projected = projectSpans (targetNamed targetName) wireJson

  match spanForPath projected (List.ofArray idPath) with
  | Some s -> projected.Text.Substring(s.Start, s.Length)
  | None -> ""

/// The id the nearest-enclosing resolution actually landed on – the focused node
/// when this language projects it, an ancestor when it does not, `""` when
/// nothing on the path is projected.
let spanPathIdByName (targetName: string) (wireJson: string) (idPath: string array) : string =
  let projected = projectSpans (targetNamed targetName) wireJson

  match spanForPath projected (List.ofArray idPath) with
  | Some s -> s.NodeId
  | None -> ""

/// The 1-based inclusive `[start; end]` line range of the nearest-enclosing
/// span, or an empty array when nothing on the path is projected.
let spanPathLinesByName (targetName: string) (wireJson: string) (idPath: string array) : int array =
  let projected = projectSpans (targetNamed targetName) wireJson

  match spanForPath projected (List.ofArray idPath) with
  | Some s ->
    let a, b = lineRange projected.Text s
    [| a; b |]
  | None -> [||]
