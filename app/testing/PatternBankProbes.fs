module Fuaran.Live.Testing.PatternBankProbes

// ============================================================================
//  TEST-ONLY. The pattern bank's two seed catalogues, flattened so the vitest
//  suite can pin every id, hole and encoded tree to the bytes each entry of the
//  site shipped before the bank became one shared file (Phase 2081).
//
//  Compiled into the playground project so the suite can import it from the
//  Fable output, and imported by NOTHING in the app.
// ============================================================================

open Fuaran.Live
open Fuaran.Live.PatternBankEngine
open Fuaran.UI.OpStream.Abstractions

/// One pattern as `id \t title \t summary \t resultType \t holes \t tree \t
/// treeFilled`: the holes as `addr=name:kind`, the tree instantiated with no
/// values, and again with every hole filled (`X-<addr>`), so a builder that
/// honours or ignores hole values is pinned either way.
let private dump (p: Pattern<obj>) : string =
  let holes =
    p.Holes
    |> List.map (fun h -> h.Addr + "=" + h.Name + ":" + string h.Kind)
    |> String.concat ","

  let filled = p.Holes |> List.map (fun h -> h.Addr, "X-" + h.Addr) |> Map.ofList

  String.concat
    "\t"
    [ p.Id
      p.Title
      p.Summary
      p.ResultType
      holes
      CanonicalJson.encodeNode (instantiate p Map.empty)
      CanonicalJson.encodeNode (instantiate p filled) ]

let playgroundSeedsFlat () : string array =
  PatternBankSeeds.playground () |> List.map dump |> Array.ofList

let showcaseSeedsFlat () : string array =
  PatternBankSeeds.showcase () |> List.map dump |> Array.ofList
