module Fuaran.Showcase.Testing.TimeMachineProbes

// ============================================================================
//  TEST-ONLY. The time-machine page's headless surface (frames, steps, forks and merges as flat strings).
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

module Apply = Fuaran.UI.Ops.Apply
module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson
open Fuaran.Showcase.TimeMachine

// ─── Headless surface – the verification gate drives these from vitest ──────
//
// Flat string projections across the Fable boundary (the Phase 710-713 pattern):
// every tree crosses as its canonical wire JSON, so the test compares bytes the
// real encoder produced and never a hand-waved shape.

/// The number of recorded turns (frames run 0..turnTotal).
let turnTotal: int = turnCount

/// Canonical JSON of trunk frame `n` – the tree the scrubber shows at turn n.
let frameJson (n: int) : string = CJson.encodeNode trunkFrames[n]

/// Frame `n` re-derived ONE step from frame n-1 through the apply engine – the
/// replay claim, checkable against `frameJson n` byte-for-byte.
let stepJson (n: int) : string =
  match Apply.apply (List.item (n - 1) turns).Op trunkFrames[n - 1] with
  | Ok t -> CJson.encodeNode t
  | Error e -> "error:" + describeError e

/// The fork branches on offer, by id.
let branchIds: string array = branches |> List.map (fun b -> b.Id) |> Array.ofList

/// `ok:<json>` for the branch's tree when forked at frame `k`, or `error:<msg>`
/// carrying the real apply error.
let forkJson (bid: string) (k: int) : string =
  match tryBranch bid with
  | None -> "error:unknown branch " + bid
  | Some b ->
    match branchTree b k with
    | Ok t -> "ok:" + CJson.encodeNode t
    | Error e -> "error:" + e

/// `merged:<json>` for a clean merge of the branch (forked at `k`) into the
/// trunk head, `conflict:<address,address,…>` naming the refused pairs' addresses,
/// or `error:<msg>` when the fork itself cannot apply or the fold faults otherwise.
let mergeJson (bid: string) (k: int) : string =
  match tryBranch bid with
  | None -> "error:unknown branch " + bid
  | Some b ->
    match mergeBranch b k with
    | Error e -> "error:" + e
    | Ok(Ok r) -> "merged:" + CJson.encodeNode r.Tree
    | Ok(Error(ReconcileFault.Refused cs)) -> "conflict:" + String.concat "," (conflictIds cs)
    | Ok(Error f) -> "error:" + describeError f

/// `merged:<json>` for the merge settled to a side (`trunk` or `branch`), or `error:<msg>`.
let mergeResolvedJson (bid: string) (k: int) (side: string) : string =
  match tryBranch bid with
  | None -> "error:unknown branch " + bid
  | Some b ->
    let s = if side = "trunk" then ReconcileSide.A else ReconcileSide.B

    match mergeBranchResolved s b k with
    | Error e -> "error:" + e
    | Ok(Ok r) -> "merged:" + CJson.encodeNode r.Tree
    | Ok(Error f) -> "error:" + describeError f

/// `agreements=<n>;interleaves=<n>;node=<64 hex>` for the merge settled to the trunk, or
/// `error:<msg>`. The node is the merge node's Core address.
let mergeReport (bid: string) (k: int) : string =
  match tryBranch bid with
  | None -> "error:unknown branch " + bid
  | Some b ->
    match mergeBranchResolved ReconcileSide.A b k with
    | Ok(Ok r) -> sprintf "agreements=%d;interleaves=%d;node=%s" r.Agreements r.Interleaves (mergeNodeId b k r)
    | Ok(Error f) -> "error:" + describeError f
    | Error e -> "error:" + e
