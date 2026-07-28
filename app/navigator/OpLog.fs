module Fuaran.Live.OpLog

// ============================================================================
//  The session op log — undo/redo by REPLAY, and the exportable artefact.
//
//  Undo here is not a second mutation model. There are no inverse ops, no
//  "unUpdateProp", no shadow tree kept in step with the real one. There is one
//  mechanism and it is the one the language already has: a base tree plus an
//  ordered sequence of ops, replayed through the public apply engine. Undoing to
//  position `k` means "decode the base and apply the first `k` recorded ops";
//  redoing means applying one more. That is the same operation the closed loop
//  performs on every emission and the same one `Panels.verify` uses to prove a
//  panel is reconstructible — so undo is exercised by everything else in the app
//  rather than being a lightly-tested parallel path.
//
//  Why that matters beyond tidiness: an inverse-op scheme has to be RIGHT for
//  every op in the vocabulary, and it silently stops being right the moment a
//  `NodeKind` or a `TreeOp` case is added. Replay is closed under vocabulary
//  growth by construction — a new op replays because it applies.
//
//  ── The cursor ──────────────────────────────────────────────────────────────
//
//  The undo cursor is DERIVED, never stored: it is `List.length session.Ops` —
//  how many ops are currently folded into the tree. `session.Log` holds the full
//  recorded sequence INCLUDING the redo tail, so `Log` is as long as `Ops` when
//  nothing is undone and longer when something is. There is no third piece of
//  state that could disagree with the other two.
//
//  A new edit while undone truncates the tail (`Session.recordOp`), so the
//  history stays linear. Branching is deliberately out of scope: the estate has
//  a real DAG model for op streams, and a half-built one here would be worse
//  than an honest linear history.
//
//  ── The export ──────────────────────────────────────────────────────────────
//
//  `exportJson` emits one self-describing document: the base tree, the applied
//  ops with their attribution and chain hashes, and the final tree — every
//  embedded document written by the canonical encoders, so the file is canonical
//  wire JSON throughout rather than a pretty-printed approximation of it.
//
//  The redo tail is deliberately NOT exported. The claim the document makes is
//  "apply these ops to this base and you get this tree"; an undone op in the
//  list would falsify it. What is exported is what actually happened.
// ============================================================================

open Fable.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Live.Ports

module Decode = Fuaran.UI.Ops.JsonDecode
module ApplyEngine = Fuaran.UI.Ops.Apply
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── the cursor ──────────────────────────────────────────────────────────────

/// How many recorded ops are currently folded into the tree — the undo cursor.
/// Derived from the session, never stored beside it.
let cursor (session: Session.SessionState) : int = List.length session.Ops

/// The full recorded length, redo tail included.
let recorded (session: Session.SessionState) : int = List.length session.Log

/// Whether there is an applied op to undo.
let canUndo (session: Session.SessionState) : bool = cursor session > 0

/// Whether an undone op is waiting to be replayed forward.
let canRedo (session: Session.SessionState) : bool = recorded session > cursor session

// ─── replay ──────────────────────────────────────────────────────────────────

/// Rebuild the session at `position` — the base tree with the first `position`
/// recorded ops replayed through the public apply engine. `Log` is carried over
/// untouched (that is what keeps the redo tail alive), while `Ops`, `Snapshots`
/// and `Tree` are rebuilt from the replay rather than sliced out of the old
/// session: slicing would make the arrays agree with each other while agreeing
/// with nothing, whereas a replay that fails to reproduce the sequence returns
/// `None` and changes nothing.
///
/// `None` also covers a position outside `[0, recorded]` and a session with no
/// base. Pure; never throws.
let replayTo (session: Session.SessionState) (position: int) : Session.SessionState option =
  if position < 0 || position > recorded session then
    None
  else
    match Session.baseTree session with
    | None -> None
    | Some baseNode ->
      let planned = session.Log |> List.truncate position

      let folded =
        planned
        |> List.fold
          (fun acc (entry: Session.LogEntry) ->
            acc
            |> Option.bind (fun (tree, ops, snaps) ->
              match Decode.decodeOp entry.OpJson with
              | Error _ -> None
              | Ok op ->
                match ApplyEngine.apply op tree with
                | Error _ -> None
                | Ok next -> Some(next, ops @ [ entry.OpJson ], snaps @ [ next ])))
          (Some(baseNode, [], [ baseNode ]))

      folded
      |> Option.map (fun (tree, ops, snaps) ->
        { session with
            Tree = Some tree
            Ops = ops
            Snapshots = snaps })

/// Step the cursor back one op. `None` when there is nothing to undo (or the
/// replay could not be reproduced — the caller keeps the session it has).
let undo (session: Session.SessionState) : Session.SessionState option =
  if canUndo session then
    replayTo session (cursor session - 1)
  else
    None

/// Step the cursor forward one op into the redo tail.
let redo (session: Session.SessionState) : Session.SessionState option =
  if canRedo session then
    replayTo session (cursor session + 1)
  else
    None

// ─── verification (the FGP-5 claim, made checkable) ──────────────────────────

type VerifyReport =
  {
    /// Replaying the base tree through the applied ops reproduces the live
    /// tree, byte-for-byte on the canonical wire.
    ReplayOk: bool
    /// Recomputing every chain hash from the recorded ops reproduces the
    /// recorded chain — so no op was re-attributed or reordered after the fact.
    ChainOk: bool
    /// Ops replayed (the applied prefix, not the redo tail).
    Steps: int
  }

/// Reconstruct the session from its base + applied op record and check both the
/// fold and the hash chain. Mirrors `Panels.verify` one scope up: the session IS
/// its op stream, and the rendered pixels are a projection of it.
let verify (session: Session.SessionState) : VerifyReport =
  let applied = session.Log |> List.truncate (cursor session)
  let steps = List.length applied

  let replayOk =
    match replayTo session steps, session.Tree with
    | Some rebuilt, Some live ->
      match rebuilt.Tree with
      | Some t -> Canon.encodeNode t = Canon.encodeNode live
      | None -> false
    | _ -> steps = 0 && session.Tree.IsNone

  let chainOk =
    let recomputed =
      applied
      |> List.mapi (fun i entry -> i, entry)
      |> List.fold
        (fun acc (i, (entry: Session.LogEntry)) ->
          acc
          |> Option.bind (fun (prev, oks) ->
            match Decode.decodeOp entry.OpJson with
            | Error _ -> None
            | Ok op ->
              let h =
                HashChain.computeHash prev op (i + 1) Session.chainTimestamp entry.Actor None OpResultEnvelope.Success

              Some(h, oks && h = entry.Hash && entry.Prev = prev && entry.Seq = i + 1)))
        (Some(Session.baseHash session, true))

    match recomputed with
    | Some(_, ok) -> ok
    | None -> steps = 0

  { ReplayOk = replayOk
    ChainOk = chainOk
    Steps = steps }

// ─── export ──────────────────────────────────────────────────────────────────

/// Canonical JSON string escaping — mirrors `CanonicalJson`'s rule (only `"`,
/// `\` and control characters, control as `\u00xx`) so the envelope's own string
/// fields are written by the same rule as the documents embedded in it.
let private jstr (s: string) : string =
  let sb = System.Text.StringBuilder()
  sb.Append '"' |> ignore

  for ch in s do
    match ch with
    | '"' -> sb.Append "\\\"" |> ignore
    | '\\' -> sb.Append "\\\\" |> ignore
    | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
    | c -> sb.Append c |> ignore

  sb.Append '"' |> ignore
  sb.ToString()

/// The export envelope's format version. Bump it when the shape changes, so a
/// reader can reject an unrecognised document with a clear error rather than
/// mis-parsing it.
[<Literal>]
let exportVersion = 1

/// The marker naming the document kind. Deliberately not `$type`: that sigil is
/// the wire format's own node/op discriminator, and an envelope wearing it would
/// invite a decoder to try to read this file as a tree.
[<Literal>]
let exportMarker = "fuaran-session-op-log"

/// The session as one exportable document: the base tree, the applied op record
/// (each op with its origin and chain hash), and the final tree — all canonical
/// wire JSON. The redo tail is excluded; see the module header.
let exportJson (session: Session.SessionState) : string =
  let applied = session.Log |> List.truncate (cursor session)

  let ops =
    applied
    |> List.map (fun entry ->
      "{\"seq\":"
      + string entry.Seq
      + ",\"actor\":"
      + Actor.encode entry.Actor
      + ",\"prevHash\":"
      + jstr entry.Prev
      + ",\"hash\":"
      + jstr entry.Hash
      + ",\"op\":"
      + entry.OpJson
      + "}")
    |> String.concat ","

  let nodeOrNull (node: Node<obj> option) =
    match node with
    | Some n -> Canon.encodeNode n
    | None -> "null"

  "{\"$log\":"
  + jstr exportMarker
  + ",\"version\":"
  + string exportVersion
  + ",\"baseHash\":"
  + jstr (Session.baseHash session)
  + ",\"base\":"
  + nodeOrNull (Session.baseTree session)
  + ",\"ops\":["
  + ops
  + "],\"tree\":"
  + nodeOrNull session.Tree
  + "}"

/// The download's filename. Generic by intent — the public surface calls this
/// "the session op log" and nothing more.
let exportFilename = "fuaran-session-op-log.json"

/// Hand the exported document to the host's download seam. Takes the `EffectPorts`
/// INTERFACE, not a browser call: the export is host-swappable for the same
/// reason every other effect in this app is (§4l).
let download (ports: EffectPorts) (session: Session.SessionState) : unit =
  ports.Download(exportFilename, exportJson session, "application/json")

// ─── flat diagnostic surface (cross-boundary friendly) ───────────────────────
//
// F# lists, records and DUs are awkward to assert on from the JS side of the
// Fable boundary, so — exactly as `Session.ingestResult` and the Phase 710/711
// helpers do — the same values are projected to plain arrays and flat records.
// These are the headless test surface AND a host-language-agnostic description
// of what the record holds.

/// `"human"` / `"agent"` per recorded op, redo tail included — the origin
/// marker, as the acceptance criterion asks it: are the two distinguishable?
let originKinds (session: Session.SessionState) : string array =
  session.Log
  |> List.map (fun e ->
    match e.Actor with
    | Human _ -> "human"
    | Agent _ -> "agent")
  |> Array.ofList

/// The attribution id per recorded op (`"navigator"` for a panel edit).
let originIds (session: Session.SessionState) : string array =
  session.Log |> List.map (fun e -> Actor.id e.Actor) |> Array.ofList

/// The op kind (`"UpdateProp"`, `"Batch"`, …) per recorded op.
let logKinds (session: Session.SessionState) : string array =
  session.Log |> List.map (fun e -> e.OpKind) |> Array.ofList

/// The chain hash per recorded op.
let logHashes (session: Session.SessionState) : string array =
  session.Log |> List.map (fun e -> e.Hash) |> Array.ofList

/// The applied ops as a plain array (an F# list is a linked structure across
/// the Fable boundary, so a `.length` on it reads as an object with no length).
let appliedOps (session: Session.SessionState) : string array = Array.ofList session.Ops

/// How many snapshots the session holds. The invariant replay rests on is
/// `snapshotCount = cursor + 1` — the base, plus one tree per applied op.
let snapshotCount (session: Session.SessionState) : int = List.length session.Snapshots

/// `undo` applied `n` times, stopping early if there is nothing left to undo.
let undoN (session: Session.SessionState) (n: int) : Session.SessionState =
  let mutable s = session

  for _ in 1..n do
    match undo s with
    | Some next -> s <- next
    | None -> ()

  s

/// `redo` applied `n` times, stopping early at the end of the redo tail.
let redoN (session: Session.SessionState) (n: int) : Session.SessionState =
  let mutable s = session

  for _ in 1..n do
    match redo s with
    | Some next -> s <- next
    | None -> ()

  s

/// `verify`, flattened for assertion across the boundary.
let verifyResult
  (session: Session.SessionState)
  : {| ReplayOk: bool
       ChainOk: bool
       Steps: int |}
  =
  let r = verify session

  {| ReplayOk = r.ReplayOk
     ChainOk = r.ChainOk
     Steps = r.Steps |}

/// Replay an EXPORTED document through the public decode + apply engines and
/// return the canonical JSON of the tree it reproduces (`""` when the document
/// is malformed or an op fails to apply). This is the acceptance criterion made
/// executable: compare it with the document's own `tree` field and the export
/// either reproduces the session or it does not.
[<Emit("(function(j){ try { var p = JSON.parse(j); return JSON.stringify(p.base); } catch(e){ return ''; } })($0)")>]
let private exportedBase (json: string) : string = jsNative

[<Emit("(function(j){ try { var p = JSON.parse(j); return (p.ops||[]).map(function(o){ return JSON.stringify(o.op); }); } catch(e){ return []; } })($0)")>]
let private exportedOps (json: string) : string array = jsNative

[<Emit("(function(j){ try { var p = JSON.parse(j); return JSON.stringify(p.tree); } catch(e){ return ''; } })($0)")>]
let private exportedTreeRaw (json: string) : string = jsNative

/// The tree the exported document CLAIMS is final, read back through the real
/// strict decoder and re-encoded canonically (`""` when absent or undecodable).
/// Normalising through the encoder is what makes a comparison with `replayExport`
/// a statement about the TREES rather than about JSON formatting.
let exportedTree (json: string) : string =
  let raw = exportedTreeRaw json

  if raw = "" || raw = "null" then
    ""
  else
    match Decode.decodeNode raw with
    | Error _ -> ""
    | Ok node -> Canon.encodeNode (WireTree.reify node)

/// The canonical wire JSON of the session's live tree (`""` when there is none).
let canonicalTree (session: Session.SessionState) : string =
  match session.Tree with
  | Some tree -> Canon.encodeNode tree
  | None -> ""

let replayExport (json: string) : string =
  let baseJson = exportedBase json

  if baseJson = "" || baseJson = "null" then
    ""
  else
    match Decode.decodeNode baseJson with
    | Error _ -> ""
    | Ok node ->
      let folded =
        exportedOps json
        |> Array.fold
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

      match folded with
      | Some tree -> Canon.encodeNode tree
      | None -> ""
