module Fuaran.Live.Live

// ============================================================================
//  Serverless live-drive (Phase 295) – Stage 1: same-origin present → audience.
//
//  A *presenting* window drives an *audience* window's UI live, with NO server:
//  the presenter's current tree + each subsequent TreeOp are pushed over a
//  `BroadcastChannel` (same-origin, zero new network egress), and the audience
//  re-renders each update through the shared apply engine – surgically for ops,
//  wholesale for a full tree. It doubles as a credible proof of the live-channel
//  / server-driven story: the architecture already anticipated live updates; this
//  implements the transport as a channel rather than a backend.
//
//  The transport is the `ILiveDriveChannel` seam (Ports.fs); Stage 2 (WebRTC P2P
//  across devices) is a drop-in second implementation of the same seam.
//
//  TRUST: a `LiveDriveMessage` is only ever canonical wire JSON (a tree or a
//  TreeOp) – the same UI-as-data a permalink already puts in the URL. The BYOK
//  key cannot reach this seam: the DU has no case for it, and the codec below
//  round-trips only the wire payload.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Fuaran.Live.Ports

[<Literal>]
let channelName = "fuaran-live-drive"

// ─── message codec (pure – headlessly testable) ──────────────────────────────

/// Wrap a kind + a raw JSON payload into the channel envelope `{kind,payload}`
/// (payload re-parsed so it rides as JSON, not a string). Emit so it is one
/// JSON round-trip, not hand-rolled string concatenation.
[<Emit("JSON.stringify({ kind: $0, payload: JSON.parse($1) })")>]
let private wrap (kind: string) (payloadJson: string) : string = jsNative

/// Parse an envelope back to `{ kind, payload }` (payload re-serialised to its
/// JSON string), or `null` for anything malformed.
[<Emit("""(function(raw){
  try { var m = JSON.parse(raw);
        if (!m || typeof m.kind !== 'string') return null;
        return { kind: m.kind, payload: JSON.stringify(m.payload) }; }
  catch (e) { return null; }
})($0)""")>]
let private unwrap (raw: string) : obj = jsNative

/// Serialise a live-drive message to a channel string.
let encodeMessage (msg: LiveDriveMessage) : string =
  match msg with
  | LiveDriveMessage.FullTree j -> wrap "tree" j
  | LiveDriveMessage.Op j -> wrap "op" j

/// Parse a channel string back to a message, or `None` if it is not a valid
/// live-drive envelope.
let decodeMessage (raw: string) : LiveDriveMessage option =
  let m = unwrap raw

  if isNull (box m) then
    None
  else
    let kind: string = m?kind
    let payload: string = m?payload

    match kind with
    | "tree" -> Some(LiveDriveMessage.FullTree payload)
    | "op" -> Some(LiveDriveMessage.Op payload)
    | _ -> None

// ─── present → audience delta ─────────────────────────────────────────────────

/// What the presenter should push given the session advanced from `previous` to
/// `next`: the newly-appended TreeOps (surgical) when the op-stream grew, else a
/// full-tree snapshot when the tree was (re)placed, else nothing. Pure – the
/// single place the present-side change detection lives, so it is unit-testable.
let liveDriveDelta (previous: Session.SessionState) (next: Session.SessionState) : LiveDriveMessage list =
  let prevN = List.length previous.Ops
  let nextN = List.length next.Ops

  if nextN > prevN then
    next.Ops |> List.skip prevN |> List.map LiveDriveMessage.Op
  else
    match next.Tree with
    | Some _ when Session.treeJson next <> Session.treeJson previous ->
      [ LiveDriveMessage.FullTree(Session.treeJson next) ]
    | _ -> []

// ─── BroadcastChannel transport (Stage 1) ─────────────────────────────────────

[<Emit("new BroadcastChannel($0)")>]
let private newBroadcastChannel (name: string) : obj = jsNative

/// A same-origin `BroadcastChannel` live-drive channel. `onMessage` fires for
/// each decoded message the audience receives (pass `ignore` on the presenting
/// side, which only sends).
let createBroadcastChannel (onMessage: LiveDriveMessage -> unit) : ILiveDriveChannel =
  let bc = newBroadcastChannel channelName

  bc?onmessage <-
    (fun (ev: obj) ->
      let raw: string = ev?data

      match decodeMessage raw with
      | Some m -> onMessage m
      | None -> ())

  { new ILiveDriveChannel with
      member _.Send(msg: LiveDriveMessage) : unit = bc?postMessage (encodeMessage msg)
      member _.Close() : unit = bc?close () }

// ─── audience-mode detection + window opening ─────────────────────────────────

// The raw probe is Emit-inlined (not exported); `isAudience` wraps it for a real
// callable function. An audience window boots with `?live=audience` in its query.
[<Emit("(typeof window !== 'undefined' && /(?:[?&])live=audience(?:&|$)/.test(window.location.search))")>]
let private audienceProbe () : bool = jsNative

/// Did this window boot as a live-drive audience (`?live=audience`)?
let isAudience () : bool = audienceProbe ()

/// Open an audience window at this page with `?live=audience` (a second tab the
/// presenter drives). No-op-safe headless.
[<Emit("{ if (typeof window !== 'undefined') window.open(window.location.pathname + '?live=audience', '_blank'); }")>]
let openAudienceWindow () : unit = jsNative

// ─── flat diagnostic surface (cross-boundary friendly – used by the tests) ────
//
// `liveDriveDelta` returns an F# list of a DU, both awkward to assert on from the
// TS side of the Fable boundary. These project the same logic to flat values.

/// The present→audience delta as encoded channel envelope strings (a JS array).
let deltaEnvelopes (previous: Session.SessionState) (next: Session.SessionState) : string[] =
  liveDriveDelta previous next |> List.map encodeMessage |> List.toArray

/// The kind an envelope decodes to ("tree" / "op"), or "" if it is not valid.
let envelopeKind (envelope: string) : string =
  match decodeMessage envelope with
  | Some(LiveDriveMessage.FullTree _) -> "tree"
  | Some(LiveDriveMessage.Op _) -> "op"
  | None -> ""

/// The wire payload an envelope carries (the tree/op JSON), or "" if invalid.
let envelopePayload (envelope: string) : string =
  match decodeMessage envelope with
  | Some(LiveDriveMessage.FullTree j) -> j
  | Some(LiveDriveMessage.Op j) -> j
  | None -> ""

/// Does an envelope survive a decode → re-encode round-trip byte-identically?
let envelopeRoundTrips (envelope: string) : bool =
  match decodeMessage envelope with
  | Some m -> encodeMessage m = envelope
  | None -> false
