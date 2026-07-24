module Fuaran.Live.Panels

// ============================================================================
//  Agent expression turns – live emitted panels (Phase 466).
//
//  An agent's turn can BE a working dashboard, findings table, plan tree, or
//  chart: real UI with real bindings, not a static image. The model streams a
//  Fuaran tree – and subsequent TreeOps – into a named PANEL that renders live
//  in the conversation transcript, stays interactive after the turn ends, and
//  keeps updating as the run progresses. A later turn may address a prior
//  panel by id with further ops ("see the table above" becomes a real
//  reference).
//
//  The emission envelope is one fenced JSON document carrying a `$panel`
//  marker (the main-preview emission dialect is unchanged – a bare Node or
//  TreeOp still targets the live preview):
//
//    { "$panel": "<panel id>", "title"?: "…", "tree": <Node> }   // open/replace
//    { "$panel": "<panel id>",                "op":   <TreeOp> } // extend
//
//  Each panel is its own op-stream scope: its ops are hash-chained under the
//  panel id through the shipped `HashChain.computeHash` (chain seed = the
//  content hash of the panel's initial tree), with a content-addressed fixed
//  timestamp so the chain is a pure function of prev-hash + sequence + actor +
//  op – replay it and you get the same hashes. `verify` refolds the panel from
//  its base tree + op stream through the real apply engine and recomputes the
//  chain, so "replay reconstructs any panel" is a checkable fact, not a claim.
//
//  Convergence with the elicitation envelope (the one-envelope rule): a panel
//  that ASKS – one whose interaction must resolve to a typed answer – is an
//  elicitation (the wire format's elicitation-envelope section), never a
//  panel variant. This envelope deliberately carries no answer contract, no
//  outcome, and no timeout; it is an expression channel only.
// ============================================================================

open Fable.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

module Decode = Fuaran.UI.Ops.JsonDecode
module ApplyEngine = Fuaran.UI.Ops.Apply
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── the panel store ─────────────────────────────────────────────────────────

/// One hash-chain entry: op `Seq` (1-based within the panel), the op's
/// top-level kind (display), the chain hash, its predecessor, and the actor
/// that authored it (kept so `verify` can recompute the hash bit-for-bit).
type ChainEntry =
  { Seq: int
    OpKind: string
    Hash: string
    Prev: string
    Author: Actor }

/// One live panel: the folded tree, the canonical bytes of its base (first)
/// tree, the base content hash (the chain seed), and the canonical op stream +
/// hash chain that grew it.
type Panel =
  { Id: string
    Title: string option
    Tree: Node<obj>
    BaseJson: string
    BaseHash: string
    OpsJson: string list
    Chain: ChainEntry list }

/// Every panel of the session, in first-appearance order.
type PanelStore =
  { Panels: Map<string, Panel>
    Order: string list }

let empty: PanelStore = { Panels = Map.empty; Order = [] }

let panelsInOrder (store: PanelStore) : Panel list =
  store.Order |> List.choose (fun id -> Map.tryFind id store.Panels)

/// The renderer scope a panel renders under – each panel keeps its reactive
/// `Binding.State` keys in its own isolated `StateStore.forScope` instance, so
/// two panels (or a panel and the main preview) can never collide on a key.
let scopeId (panelId: string) : string = "fl-panel-" + panelId

// ─── the emission envelope ───────────────────────────────────────────────────

[<RequireQualifiedAccess>]
type PanelPayloadKind =
  /// `tree` – open a new panel (or replace an existing one's tree wholesale).
  | Tree of json: string
  /// `op` – extend an existing panel with one TreeOp (or Batch).
  | Op of json: string
  /// The `$panel` marker was present but the envelope shape was wrong
  /// (`tree` and `op` both present, or both absent).
  | Malformed of message: string

type PanelPayload =
  { PanelId: string
    Title: string option
    Kind: PanelPayloadKind }

/// Probe an extracted JSON document for the `$panel` envelope. Returns a flat
/// JS object (`null` when the document is not a panel envelope at all): the
/// inner tree/op is re-serialised so the standard decoders take it from here.
[<Emit("""(function(json){
  var p;
  try { p = JSON.parse(json); } catch(e){ return null; }
  if (p == null || typeof p !== 'object' || typeof p['$panel'] !== 'string' || p['$panel'] === '') return null;
  var title = (typeof p.title === 'string' && p.title !== '') ? p.title : null;
  var hasTree = (p.tree != null && typeof p.tree === 'object');
  var hasOp = (p.op != null && typeof p.op === 'object');
  if (hasTree === hasOp)
    return { panelId: p['$panel'], title: title, kind: 'malformed', payload: null };
  return { panelId: p['$panel'], title: title, kind: (hasTree ? 'tree' : 'op'),
           payload: JSON.stringify(hasTree ? p.tree : p.op) };
})($0)""")>]
let private probeEnvelope (json: string) : obj = jsNative

[<Emit("$0 == null")>]
let private isNullObj (o: obj) : bool = jsNative

[<Emit("$0[$1]")>]
let private field (o: obj) (name: string) : obj = jsNative

/// Parse a panel envelope out of an extracted emission document. `None` means
/// "not a panel emission" (route to the main-session ingest); `Some` with a
/// `Malformed` kind means the model addressed the panel channel but got the
/// envelope wrong – a typed, repairable failure.
let tryPayload (json: string) : PanelPayload option =
  let raw = probeEnvelope json

  if isNullObj raw then
    None
  else
    let panelId = unbox<string> (field raw "panelId")

    let title =
      let t = field raw "title"
      if isNullObj t then None else Some(unbox<string> t)

    let kind =
      match unbox<string> (field raw "kind") with
      | "tree" -> PanelPayloadKind.Tree(unbox<string> (field raw "payload"))
      | "op" -> PanelPayloadKind.Op(unbox<string> (field raw "payload"))
      | _ ->
        PanelPayloadKind.Malformed
          "A $panel envelope must carry exactly one of \"tree\" (open/replace the panel) or \"op\" (extend it)."

    Some
      { PanelId = panelId
        Title = title
        Kind = kind }

// ─── ingest – the per-panel scoped fold ──────────────────────────────────────

/// A fixed timestamp so each panel's chain is content-addressed – a pure
/// function of prev-hash + sequence + actor + op. Replaying the same ops
/// yields the same hashes (which is what makes `verify` meaningful), rather
/// than a wall-clock-dependent record.
let private fixedTs =
  System.DateTimeOffset(2020, 1, 1, 0, 0, 0, System.TimeSpan.Zero)

[<Emit("(function(j){ try { var t = JSON.parse(j).$type; return (typeof t === 'string') ? t : 'op'; } catch(e){ return 'op'; } })($0)")>]
let private opKindOf (canonJson: string) : string = jsNative

type PanelOutcome =
  /// The payload applied. `Mode` is "tree" / "op"; `IsNew` marks a panel's
  /// first appearance (the transcript renders its live row at that point).
  | PanelApplied of store: PanelStore * panel: Panel * mode: string * isNew: bool * summary: string
  | PanelFailed of Session.IngestError

let private chainHead (panel: Panel) : string =
  match List.tryLast panel.Chain with
  | Some e -> e.Hash
  | None -> panel.BaseHash

/// Apply one panel payload to the store. Pure – never mutates, never throws;
/// every failure is the same typed `IngestError` shape the main session uses.
let ingest (store: PanelStore) (author: Actor) (payload: PanelPayload) : PanelOutcome =
  match payload.Kind with
  | PanelPayloadKind.Malformed message ->
    PanelFailed
      { Kind = "panel-envelope"
        Message = message }
  | PanelPayloadKind.Tree json ->
    match Decode.decodeNode json with
    | Error e ->
      PanelFailed
        { Kind = "panel-decode"
          Message = e.Message }
    | Ok node ->
      let tree = WireTree.reify node
      let baseJson = Canon.encodeNode tree
      let isNew = not (Map.containsKey payload.PanelId store.Panels)

      let title =
        match payload.Title with
        | Some t -> Some t
        | None ->
          // A replacement without a title keeps the panel's existing one.
          store.Panels |> Map.tryFind payload.PanelId |> Option.bind (fun p -> p.Title)

      let panel =
        { Id = payload.PanelId
          Title = title
          Tree = tree
          BaseJson = baseJson
          BaseHash = Fuaran.UI.Hashing.sha256Hex baseJson
          OpsJson = []
          Chain = [] }

      let next =
        { Panels = Map.add payload.PanelId panel store.Panels
          Order =
            if isNew then
              store.Order @ [ payload.PanelId ]
            else
              store.Order }

      PanelApplied(
        next,
        panel,
        "tree",
        isNew,
        (if isNew then
           "Opened panel \"" + payload.PanelId + "\"."
         else
           "Replaced panel \"" + payload.PanelId + "\"'s tree.")
      )
  | PanelPayloadKind.Op json ->
    match Map.tryFind payload.PanelId store.Panels with
    | None ->
      PanelFailed
        { Kind = "panel-unknown"
          Message =
            "The op addresses panel \""
            + payload.PanelId
            + "\", but no such panel exists yet. Open it first with a $panel tree emission." }
    | Some panel ->
      match Decode.decodeOp json with
      | Error e ->
        PanelFailed
          { Kind = "panel-decode"
            Message = e.Message }
      | Ok op ->
        match ApplyEngine.apply op panel.Tree with
        | Error e ->
          PanelFailed
            { Kind = "panel-apply"
              Message = e.Message }
        | Ok newTree ->
          let canonOp = Canon.encodeOp op
          let seq = List.length panel.Chain + 1
          let prev = chainHead panel

          let hash =
            HashChain.computeHash prev op seq fixedTs author None OpResultEnvelope.Success

          let entry =
            { Seq = seq
              OpKind = opKindOf canonOp
              Hash = hash
              Prev = prev
              Author = author }

          let updated =
            { panel with
                Tree = newTree
                OpsJson = panel.OpsJson @ [ canonOp ]
                Chain = panel.Chain @ [ entry ] }

          let next =
            { store with
                Panels = Map.add payload.PanelId updated store.Panels }

          PanelApplied(next, updated, "op", false, "Applied " + entry.OpKind + " to panel \"" + payload.PanelId + "\".")

// ─── replay verification – the panel is reconstructible from its stream ──────

type VerifyReport =
  {
    /// Refolding base tree + op stream through the apply engine reproduces the
    /// live tree, byte-for-byte on the canonical wire.
    ReplayOk: bool
    /// Recomputing every chain hash from the recorded ops reproduces the
    /// recorded chain.
    ChainOk: bool
    /// Ops replayed.
    Steps: int
  }

/// Reconstruct the panel from its recorded base tree + op stream and check
/// both the fold and the hash chain. This is the FGP-5 claim made checkable:
/// the panel IS its op-stream scope; the rendered pixels are a projection.
let verify (panel: Panel) : VerifyReport =
  let steps = List.length panel.OpsJson

  let refolded =
    match Decode.decodeNode panel.BaseJson with
    | Error _ -> None
    | Ok node ->
      panel.OpsJson
      |> List.fold
        (fun acc opJson ->
          acc
          |> Option.bind (fun tree ->
            match Decode.decodeOp opJson with
            | Error _ -> None
            | Ok op ->
              match ApplyEngine.apply op tree with
              | Error _ -> None
              | Ok next -> Some next))
        (Some(WireTree.reify node))

  let replayOk =
    match refolded with
    | Some tree -> Canon.encodeNode tree = Canon.encodeNode panel.Tree
    | None -> false

  let chainOk =
    let recomputed =
      panel.OpsJson
      |> List.mapi (fun i opJson -> i, opJson)
      |> List.fold
        (fun acc (i, opJson) ->
          acc
          |> Option.bind (fun (prev, oks) ->
            match List.tryItem i panel.Chain, Decode.decodeOp opJson with
            | Some entry, Ok op ->
              let h =
                HashChain.computeHash prev op (i + 1) fixedTs entry.Author None OpResultEnvelope.Success

              Some(h, oks && h = entry.Hash && entry.Prev = prev)
            | _ -> None))
        (Some(panel.BaseHash, true))

    match recomputed with
    | Some(_, ok) -> ok && List.length panel.Chain = steps
    | None -> steps = 0

  { ReplayOk = replayOk
    ChainOk = chainOk
    Steps = steps }
