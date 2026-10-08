module Fuaran.Live.Testing.GalleryProbes

// ============================================================================
//  TEST-ONLY. The gallery's flat diagnostic surface (every example's wire and tags).
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

open Fuaran.Live
open Fuaran.Live.Gallery

/// The canonical wire JSON of each example, as a JS array – the cross-boundary
/// surface the gallery test uses to assert every example is valid + shareable.
let exampleWires (unit: unit) : string[] =
  examples |> List.map (fun e -> Canon.encodeNode e.Tree) |> List.toArray

/// The `(title, feature)` pairs, as a JS array of two-element arrays — the
/// cross-boundary surface the gallery test reads to assert every entry is
/// feature-tagged and that no two entries collide on a title.
let exampleTags (unit: unit) : string[][] =
  examples |> List.map (fun e -> [| e.Title; e.Feature |]) |> List.toArray
