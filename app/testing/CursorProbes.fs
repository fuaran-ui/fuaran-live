module Fuaran.Live.Testing.CursorProbes

// ============================================================================
//  TEST-ONLY. The cursor's flat diagnostic surface (the walk as plain strings).
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fuaran.UI.Types
module Introspect = Fuaran.UI.Ops.Introspect

open Fuaran.Live
open Fuaran.Live.Cursor

/// Every node id in the tree, DFS pre-order — the walk, as plain strings.
let walkIds (root: Node<'Msg>) : string array =
  allPaths root |> List.choose List.tryLast |> List.map idText |> Array.ofList
