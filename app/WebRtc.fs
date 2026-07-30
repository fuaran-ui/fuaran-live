module Fuaran.Live.WebRtc

// ============================================================================
//  Serverless live-drive (Phase 295) – Stage 2: cross-device WebRTC P2P.
//
//  The drop-in second implementation of the `ILiveDriveChannel` seam (Ports.fs):
//  where Stage 1 (`Live.fs`) drives another *window on the same machine* over a
//  same-origin `BroadcastChannel`, this drives another *device* over a WebRTC
//  data channel – with STILL no server storing anything.
//
//  Signalling is manual + serverless. WebRTC needs the two peers to exchange an
//  SDP offer/answer, which normally rides a signalling server; here there is
//  none, so the two short SDP blobs are exchanged **out-of-band** – shown as a QR
//  code and as copy-paste text, carried across by the operator (scan / paste into
//  a chat). We gather ICE candidates to completion before handing over each blob
//  (non-trickle), so one blob per direction carries the whole handshake. A single
//  public STUN server is used for NAT traversal – handshake-only, no UI data ever
//  touches it (the documented, opt-in posture expansion; see SECURITY.md).
//
//  TRUST INVARIANT (identical to Stage 1, load-bearing): the ONLY thing the data
//  channel carries is a `LiveDriveMessage` – canonical wire JSON (a tree or a
//  TreeOp), the exact same data a shareable permalink already puts in the URL.
//  The BYOK key lives only in the prompting tab's key store and can never reach
//  this seam by construction: the DU has no case for it, and the signalling codec
//  round-trips only an SDP string. The channel envelope is reused verbatim from
//  `Live.fs`, so the two transports are byte-identical on the wire.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Fuaran.Live.Ports

/// A single public STUN server for NAT traversal. Handshake-only – it never sees
/// any UI data (that flows peer-to-peer over the data channel). This is the one
/// new network origin Stage 2 introduces; it is mirrored in the shipped CSP's
/// `connect-src` (vite.config.ts) and documented in SECURITY.md.
[<Literal>]
let stunServer = "stun:stun.l.google.com:19302"

/// The data-channel label – shared by both peers (must match).
[<Literal>]
let private channelLabel = "fuaran-live-drive"

/// Peer connection state surfaced to the UI, so the pairing panel can show
/// progress and handle teardown. Projected from `RTCPeerConnection.connectionState`.
[<RequireQualifiedAccess>]
type PeerState =
  | New
  | Connecting
  | Connected
  | Disconnected
  | Failed
  | Closed

let private peerStateOf (s: string) : PeerState =
  match s with
  | "connecting" -> PeerState.Connecting
  | "connected" -> PeerState.Connected
  | "disconnected" -> PeerState.Disconnected
  | "failed" -> PeerState.Failed
  | "closed" -> PeerState.Closed
  | _ -> PeerState.New

// ─── signalling codec (pure – headlessly testable) ───────────────────────────
//
// An SDP offer/answer is wrapped `{v,kind,sdp}` and base64'd into a single-line,
// copy-paste- and QR-friendly token. Decoding validates the envelope shape AND
// that the payload actually looks like SDP (`v=0`) – so a blob pasted into the
// wrong slot (an answer where an offer is expected, or arbitrary text) is
// rejected rather than fed to `setRemoteDescription`.

[<Emit("btoa(unescape(encodeURIComponent($0)))")>]
let private b64encode (s: string) : string = jsNative

[<Emit("decodeURIComponent(escape(atob($0)))")>]
let private b64decode (s: string) : string = jsNative

[<Emit("JSON.stringify({ v: 1, kind: $0, sdp: $1 })")>]
let private signalJson (kind: string) (sdp: string) : string = jsNative

/// Encode an SDP blob (`kind` = "offer" | "answer") to a transport token.
let encodeSignal (kind: string) (sdp: string) : string = b64encode (signalJson kind sdp)

/// Parse a `{kind,sdp}` back out of a token, or `null` for anything that is not a
/// well-formed signal (wrong version, unknown kind, or a payload that is not SDP).
[<Emit("""(function(raw){
  try {
    var m = JSON.parse(raw);
    if (!m || m.v !== 1) return null;
    if (m.kind !== 'offer' && m.kind !== 'answer') return null;
    if (typeof m.sdp !== 'string' || m.sdp.indexOf('v=0') === -1) return null;
    return { kind: m.kind, sdp: m.sdp };
  } catch (e) { return null; }
})($0)""")>]
let private parseSignal (json: string) : obj = jsNative

/// Decode a transport token to `(kind, sdp)`, or `None` if malformed / not SDP.
let decodeSignal (token: string) : (string * string) option =
  let decoded =
    try
      b64decode token
    with _ ->
      ""

  if decoded = "" then
    None
  else
    let m = parseSignal decoded

    if isNull (box m) then
      None
    else
      Some(string m?kind, string m?sdp)

// ─── flat diagnostic surface (cross-boundary friendly – used by the tests) ────

/// The `kind` a signal token decodes to ("offer" / "answer"), or "" if invalid –
/// the codec projected to a flat string the headless tests assert on.
let signalKind (token: string) : string =
  match decodeSignal token with
  | Some(k, _) -> k
  | None -> ""

/// The SDP a signal token carries, or "" if invalid.
let signalSdp (token: string) : string =
  match decodeSignal token with
  | Some(_, sdp) -> sdp
  | None -> ""

/// Does a signal token survive a decode → re-encode round-trip byte-identically?
let signalRoundTrips (token: string) : bool =
  match decodeSignal token with
  | Some(k, sdp) -> encodeSignal k sdp = token
  | None -> false

// ─── the join link (2026-07-30) ──────────────────────────────────────────────
//
// The presenter's QR used to encode the RAW offer token, so scanning it with a
// phone camera showed a wall of base64 and opened nothing. It now encodes a
// JOIN LINK — this page's own URL with `?live=pair` (lands in the joiner panel)
// and the offer in the `#offer=` FRAGMENT, which the browser never sends to any
// server: scanning opens the playground on the phone and the answer code is
// generated automatically. The same link works pasted into a chat. The token
// is standard base64 (`+ / =`), all legal fragment bytes, so no re-encoding.

[<Emit("(window.location.origin + window.location.pathname)")>]
let private pageBase () : string = jsNative

/// The URL the presenter's QR (and copy box) carries.
let joinLinkFor (offerToken: string) : string =
  pageBase () + "?live=pair#offer=" + offerToken

/// The `#offer=<token>` a join link delivered to THIS window, or "" when the
/// page was opened without one.
[<Emit("(function(){ try { var m = /[#&]offer=([^&]+)/.exec(window.location.hash || ''); return m ? m[1] : ''; } catch (e) { return ''; } })()")>]
let private offerFragmentProbe () : string = jsNative

let pairOfferFromLink () : string = offerFragmentProbe ()

/// The offer token in whatever the joiner pasted — a bare token, or a full join
/// link (the presenter's share surface now hands out links, and either shape
/// should work in the paste box).
let offerTokenOf (input: string) : string =
  let trimmed = input.Trim()
  let i = trimmed.LastIndexOf "#offer="

  if i >= 0 then
    trimmed.Substring(i + "#offer=".Length)
  else
    trimmed

// ─── raw RTCPeerConnection interop (the only browser-touching surface) ────────

[<Emit("new RTCPeerConnection({ iceServers: [{ urls: $0 }] })")>]
let private newPeer (stun: string) : obj = jsNative

[<Emit("$0.createDataChannel($1, { ordered: true })")>]
let private newDataChannel (pc: obj) (label: string) : obj = jsNative

[<Emit("$0.createOffer()")>]
let private createOffer (pc: obj) : JS.Promise<obj> = jsNative

[<Emit("$0.createAnswer()")>]
let private createAnswer (pc: obj) : JS.Promise<obj> = jsNative

[<Emit("$0.setLocalDescription($1)")>]
let private setLocalDescription (pc: obj) (desc: obj) : JS.Promise<unit> = jsNative

[<Emit("$0.setRemoteDescription({ type: $1, sdp: $2 })")>]
let private setRemoteDescription (pc: obj) (kind: string) (sdp: string) : JS.Promise<unit> = jsNative

/// Fire-and-forget remote-description apply: used where there is nothing to await
/// (the offerer accepting the answer), routing a rejected promise to `onFail`.
[<Emit("$0.setRemoteDescription({ type: $1, sdp: $2 }).catch(function(){ $3(); })")>]
let private applyRemote (pc: obj) (kind: string) (sdp: string) (onFail: unit -> unit) : unit = jsNative

[<Emit("($0.localDescription && $0.localDescription.sdp) || ''")>]
let private localSdp (pc: obj) : string = jsNative

/// Resolve once ICE gathering completes, so the local description carries every
/// candidate (non-trickle). A 3s safety timeout resolves with whatever candidates
/// we have – host candidates alone suffice on LAN / loopback, and a slow relay
/// candidate should not hang the handshake forever.
[<Emit("""(function(pc){
  return new Promise(function(resolve){
    if (pc.iceGatheringState === 'complete') { resolve(); return; }
    var done = false;
    function finish(){ if (done) return; done = true; pc.removeEventListener('icegatheringstatechange', check); resolve(); }
    function check(){ if (pc.iceGatheringState === 'complete') finish(); }
    pc.addEventListener('icegatheringstatechange', check);
    setTimeout(finish, 3000);
  });
})($0)""")>]
let private awaitIceComplete (pc: obj) : JS.Promise<unit> = jsNative

[<Emit("$0.onconnectionstatechange = function(){ $1($0.connectionState); }")>]
let private wireConnState (pc: obj) (onState: string -> unit) : unit = jsNative

[<Emit("$0.ondatachannel = function(ev){ $1(ev.channel); }")>]
let private onDataChannel (pc: obj) (cb: obj -> unit) : unit = jsNative

[<Emit("""(function(dc, onOpen, onMsg, onClose){
  dc.onopen = function(){ onOpen(); };
  dc.onmessage = function(ev){ onMsg(ev.data); };
  dc.onclose = function(){ onClose(); };
})($0, $1, $2, $3)""")>]
let private wireDataChannel (dc: obj) (onOpen: unit -> unit) (onMsg: string -> unit) (onClose: unit -> unit) : unit =
  jsNative

[<Emit("(function(dc){ try { return dc && dc.readyState === 'open'; } catch(e){ return false; } })($0)")>]
let private channelOpen (dc: obj) : bool = jsNative

[<Emit("$0.send($1)")>]
let private channelSend (dc: obj) (data: string) : unit = jsNative

[<Emit("(function(dc){ try { dc.close(); } catch(e){} })($0)")>]
let private channelClose (dc: obj) : unit = jsNative

[<Emit("(function(pc){ try { pc.close(); } catch(e){} })($0)")>]
let private peerClose (pc: obj) : unit = jsNative

// ─── the pairing peers ────────────────────────────────────────────────────────
//
// Both roles produce one `ILiveDriveChannel` (the transport the present/audience
// loop drives, exactly like Stage 1's `createBroadcastChannel`). The offerer's
// data channel is created up-front; the answerer's arrives via `ondatachannel`
// once the handshake lands, so its `Send` is a no-op until the channel is open.

/// The presenting side of a pair. `GenerateOffer` produces the token to hand to
/// the other device; `AcceptAnswer` consumes the token that comes back; `Channel`
/// is the live transport once connected.
type IWebRtcOfferer =
  abstract member GenerateOffer: unit -> Async<string>
  abstract member AcceptAnswer: string -> bool
  abstract member Channel: ILiveDriveChannel

/// The joining side of a pair. Constructed from the offer token; `GenerateAnswer`
/// produces the token to hand back to the presenter; `Channel` receives the
/// driven UI once connected.
type IWebRtcAnswerer =
  abstract member GenerateAnswer: unit -> Async<string>
  abstract member Channel: ILiveDriveChannel

/// Wire a data channel's inbound messages to `onMessage` (decoding the shared
/// `Live` envelope, so WebRTC and BroadcastChannel are identical on the wire).
let private wireInbound (dc: obj) (onMessage: LiveDriveMessage -> unit) (onState: PeerState -> unit) : unit =
  wireDataChannel
    dc
    (fun () -> onState PeerState.Connected)
    (fun raw ->
      match Live.decodeMessage raw with
      | Some m -> onMessage m
      | None -> ())
    (fun () -> onState PeerState.Closed)

/// Create the presenting peer. `onMessage` fires for anything the other side
/// sends back (unused today – the presenter drives one-way); `onState` tracks the
/// connection lifecycle for the UI.
let createOfferer (onMessage: LiveDriveMessage -> unit) (onState: PeerState -> unit) : IWebRtcOfferer =
  let pc = newPeer stunServer
  let dc = newDataChannel pc channelLabel
  wireConnState pc (peerStateOf >> onState)
  wireInbound dc onMessage onState

  { new IWebRtcOfferer with
      member _.GenerateOffer() =
        async {
          let! offer = createOffer pc |> Async.AwaitPromise
          do! setLocalDescription pc offer |> Async.AwaitPromise
          do! awaitIceComplete pc |> Async.AwaitPromise
          return encodeSignal "offer" (localSdp pc)
        }

      member _.AcceptAnswer(token: string) : bool =
        match decodeSignal token with
        | Some("answer", sdp) ->
          // Fire the async apply; a bad SDP surfaces as a Failed connection state
          // via the promise catch, not here (the token shape already validated above).
          applyRemote pc "answer" sdp (fun () -> onState PeerState.Failed)
          true
        | _ -> false

      member _.Channel =
        { new ILiveDriveChannel with
            member _.Send(msg: LiveDriveMessage) : unit =
              if channelOpen dc then
                channelSend dc (Live.encodeMessage msg)

            member _.Close() : unit =
              channelClose dc
              peerClose pc } }

/// Create the joining peer from the presenter's offer token, or `None` if the
/// token is not a valid offer. The data channel arrives from the offerer, so it
/// is captured in a ref and the transport's `Send` waits for it.
let createAnswerer
  (offerToken: string)
  (onMessage: LiveDriveMessage -> unit)
  (onState: PeerState -> unit)
  : IWebRtcAnswerer option =
  match decodeSignal offerToken with
  | Some("offer", offerSdp) ->
    let pc = newPeer stunServer
    let inbound: obj option ref = ref None
    wireConnState pc (peerStateOf >> onState)

    onDataChannel pc (fun dc ->
      inbound.Value <- Some dc
      wireInbound dc onMessage onState)

    Some
      { new IWebRtcAnswerer with
          member _.GenerateAnswer() =
            async {
              do! setRemoteDescription pc "offer" offerSdp |> Async.AwaitPromise
              let! answer = createAnswer pc |> Async.AwaitPromise
              do! setLocalDescription pc answer |> Async.AwaitPromise
              do! awaitIceComplete pc |> Async.AwaitPromise
              return encodeSignal "answer" (localSdp pc)
            }

          member _.Channel =
            { new ILiveDriveChannel with
                member _.Send(msg: LiveDriveMessage) : unit =
                  match inbound.Value with
                  | Some dc when channelOpen dc -> channelSend dc (Live.encodeMessage msg)
                  | _ -> ()

                member _.Close() : unit =
                  match inbound.Value with
                  | Some dc -> channelClose dc
                  | None -> ()

                  peerClose pc } }
  | _ -> None

// ─── QR rendering (reuse the shipped `qrcode-generator`) ──────────────────────
//
// The same tiny, zero-network, pure-JS encoder the Teleport page uses. It emits a
// GIF `data:` URL (already allowed by `img-src data:`), so it works under the
// locked-down CSP. Error-correction "L" maximises capacity; a signal too large
// for even a version-40 QR returns "" and the panel falls back to the copy-paste
// token (the same graceful degradation `teleport-qr.ts` uses).

let private qrGen: obj = importDefault "qrcode-generator"

// ─── deep-link entry ──────────────────────────────────────────────────────────

/// Did this window boot with `?live=pair`? A joiner device follows a shared link
/// (or scans a QR of it) straight into the "join a paired session" panel.
[<Emit("(typeof window !== 'undefined' && /(?:[?&])live=pair(?:&|$)/.test(window.location.search))")>]
let private pairJoinProbe () : bool = jsNative

let isPairJoinRequested () : bool = pairJoinProbe ()

/// A GIF `data:` URL for `text` as a QR code, or "" if it will not fit one QR.
let qrDataUrl (text: string) (cellSize: int) : string =
  emitJsExpr
    (qrGen, text, cellSize)
    """(function(gen,t,c){ try { var q = gen(0,'L'); q.addData(t); q.make(); return q.createDataURL(c,4); } catch(e){ return ''; } })($0,$1,$2)"""
