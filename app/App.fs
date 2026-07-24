module Fuaran.Live.App

// ============================================================================
//  fuaran-live (F#) – browser entry. The entirely-F#/Fable rebuild (Phase 326),
//  unified into one conversation surface (Phase 328).
//
//  The whole playground is F#/Fable, no TypeScript shell: the Elmish loop owns
//  the BYOK closed loop and the chrome + panes render through the F#
//  `Fuaran.UI.Renderer` – the same renderer that draws the emitted tree. "Built
//  entirely in the language it promotes."
//
//  As of Phase 328 there is ONE interaction model. Every prompt runs the
//  emit → observe → repair self-debug loop (multi-provider via Phase 327's
//  shared `FuaranLive.AiWire` connectors), streaming model text + tool calls +
//  emissions + halts inline as a single transcript. A live token counter, a live
//  elapsed timer, and a Stop button are the surfaced controls; a halted/stopped
//  run is **resumable** (a follow-up prompt continues with the accumulated agent
//  context). A generous hidden budget stays as a runaway backstop but is no
//  longer user-facing. Panes: the unified Conversation, live preview, inspector,
//  examples & sharing, and compare (Fuaran-typed vs conventional JS). Effectful
//  seams (providers, key stores, clipboard) are the injected F# ports.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Browser
open Elmish
open Elmish.React
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Live.Ports

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module Elc = Fuaran.UI.OpStream.Abstractions.Elicitation

importSideEffects "@fuaran-ui/renderer/css"
// KaTeX stylesheet – the `Math` primitive's client-only enhancement
// (`MathEnhance.enhance`) renders into KaTeX markup that needs this CSS (Phase
// 294 adoption). The npm `katex` package ships it.
importSideEffects "katex/dist/katex.min.css"
importSideEffects "./app.css"
// The icon-contract glyph map — icons render as empty data-icon hooks; this
// maps the common names to currentColor SVG masks (see app/icon-glyphs.css).
importSideEffects "./icon-glyphs.css"

// Toggle the chrome-token class on <html> so the page background (outside the
// max-width shell) darkens too and the ported --fuaran-color-* chrome tokens
// flip – mirrors the showcase's applyDarkClass. Rendered trees re-colour via
// themeStyleElement; this covers the body + branded chrome.
[<Emit("(function(){ try { document.documentElement.classList.toggle('ds-dark', $0); } catch(e){} })()")>]
let private applyDarkClass (dark: bool) : unit = jsNative

// The brand palette + dark theme + persisted preference live in Brand.fs – the
// single shared home, so the chrome and every rendered node re-colour from one
// definition.

// ─── dual-host wire-parity pane (Phase 85 surfaced in-app) ───────────────────
//
// The parity pane embeds the two render-host iframes (ts-host.html /
// fable-host.html – built only under VITE_DUAL_HOST) and drives them with the
// current tree's canonical wire JSON. Each host renders it AND reports its
// `encode(decode(wire))`; the pane asserts the two are byte-identical (and equal
// to the input), making the implementation-independent wire contract visible.
// Gated behind the same VITE_DUAL_HOST flag as the host pages themselves.

let private dualHostEnabled: bool =
  let flag: string =
    emitJsExpr () "(import.meta.env && import.meta.env.VITE_DUAL_HOST) || ''"

  flag = "1" || flag = "true"

/// Post `{kind:"fuaran:render", wire}` to a host iframe by element id (no-op if
/// the frame is absent – e.g. VITE_DUAL_HOST off, so the pages were not built).
/// Done wholly in JS via Emit so no DOM element crosses the F#/obj boundary.
[<Emit("(function(){var f=document.getElementById($0);if(f&&f.contentWindow)f.contentWindow.postMessage({kind:'fuaran:render',wire:$1},'*');})()")>]
let private postWireToFrame (frameId: string) (wire: string) : unit = jsNative

[<Literal>]
let private tsFrameId = "fl-parity-ts-frame"

[<Literal>]
let private fsFrameId = "fl-parity-fs-frame"

/// One corpus demo input for the parity pane (see src/hosts/parityFixtures.ts,
/// which inlines the real wire-format-fixtures files at build time). `wire` is
/// posted to both hosts; `expected` is the canonical bytes each host's
/// re-encoding must equal. The two differ exactly for the WIRE_FORMAT §16
/// lenient-accept shorthands, where normalising is the point of the demo.
type ParityFixture =
  abstract id: string
  abstract label: string
  abstract lenient: bool
  abstract wire: string
  abstract expected: string
  abstract description: string

[<Import("parityFixtures", "../src/hosts/parityFixtures")>]
let private parityFixtures: ParityFixture array = jsNative

// ─── Model + Msg ─────────────────────────────────────────────────────────────

type ChatError = { Source: string; Message: string }

/// One render host's parity report: did it decode, and what canonical bytes did
/// it re-encode the decoded tree to (None when the decode failed).
type HostReport = { Ok: bool; Canonical: string option }

/// Which side of a cross-device pairing this window is (Phase 295 Stage 2).
[<RequireQualifiedAccess>]
type PairRole =
  | Presenter
  | Joiner

/// The cross-device pairing handshake state (Phase 295 Stage 2). `Role` is set
/// once the visitor opts in; the token fields carry the SDP offer/answer blobs
/// exchanged out-of-band (QR + copy-paste); `Status` mirrors the live WebRTC
/// connection. `Offer`/`Answer` are the tokens THIS side produced to hand over;
/// `OfferInput`/`AnswerInput` are the paste boxes for the token from the peer.
type PairState =
  { Role: PairRole option
    Offer: string
    OfferInput: string
    Answer: string
    AnswerInput: string
    Status: WebRtc.PeerState
    Error: string option }

let pairEmpty: PairState =
  { Role = None
    Offer = ""
    OfferInput = ""
    Answer = ""
    AnswerInput = ""
    Status = WebRtc.PeerState.New
    Error = None }

type Model =
  {
    Session: Session.SessionState
    Prompt: string
    /// Client-only voice input (Phase 401). `VoiceSupported` gates the mic (the
    /// Web Speech API is absent in some browsers); `Listening` is dictation in
    /// flight; `VoiceBase` is the prompt text captured when dictation began, so
    /// recognised speech is appended to what the visitor already typed.
    VoiceSupported: bool
    Listening: bool
    VoiceBase: string
    /// Serverless live-drive (Phase 295). `IsAudience` – this window booted as a
    /// live-drive audience (`?live=audience`): it renders received updates and
    /// hides the authoring chrome. `Presenting` – this (authoring) window is
    /// broadcasting its tree + ops to an audience window.
    IsAudience: bool
    Presenting: bool
    ProviderId: string
    KeyPresent: bool
    AgentRunning: bool
    Error: ChatError option
    Model: string
    Dark: bool
    Comparing: bool
    Compare: Compare.CompareResult option
    /// The streamed transcript of the unified loop – user prompts, model text,
    /// tool calls, emissions, halts – accumulated ACROSS runs (so a resumed
    /// conversation stays visible).
    AgentTimeline: Agent.TimelineEntry list
    /// The live panel store (Phase 466) – every panel the agent has streamed
    /// into the transcript, each its own hash-chained op-stream scope. Panels
    /// persist across runs so a later turn can extend an earlier panel by id.
    Panels: Panels.PanelStore
    /// The accumulated provider-message context, carried across runs so a
    /// follow-up prompt resumes with prior context (Phase 328 resumability).
    AgentMessages: AgentMessage list
    /// This run's token counts + latest call number (reset on each Send) – the
    /// per-run readout reports these against the hard budget (Phase 115). In/out
    /// are tracked separately because output tokens price ~5× input.
    RunTokensIn: int
    RunTokensOut: int
    RunIteration: int
    /// Cumulative token count across the whole session (every run).
    SessionTokens: int
    /// Indicative session spend in USD, accumulated per call at that call's
    /// model pricing; `SessionCostKnown` drops to false if any call ran a model
    /// the price table doesn't know (the readout then omits the session cost).
    SessionCostUsd: float
    SessionCostKnown: bool
    /// Elapsed seconds of the current / most-recent run (reset on each Send).
    Elapsed: int
    /// The selected tab of the source-projection Output box (Phase 329).
    OutputTab: Projection.Target
    /// Dual-host wire-parity reports (Phase 85 surfaced in-app): the latest
    /// `rendered` result from each host iframe. None until the host reports.
    ParityTs: HostReport option
    ParityFs: HostReport option
    /// The parity pane's selected demo input: a wire-format-fixtures corpus
    /// entry (canonical node or §16 lenient shorthand), or None for the
    /// current session tree.
    ParityFixture: ParityFixture option
    /// Cross-device WebRTC pairing handshake (Phase 295 Stage 2).
    Pair: PairState
    /// The live elicitations (Phase 465 §18 via the askUser tool): every
    /// question the agent has asked this conversation, keyed by elicitation
    /// id – pending ones render as live form rows in the transcript; resolved
    /// ones stay as frozen records with their typed outcome.
    Asks: Map<string, Ask.AskRecord>
  }

type Msg =
  | SetPrompt of string
  | SetKey of string
  | SelectProvider of string
  | Send
  | ToggleTheme
  | LoadTree of Node<obj>
  | Share
  | RunCompare
  | CompareDone of Compare.CompareResult
  | AgentEmit of Agent.TimelineEntry
  | AgentSession of Session.SessionState
  | AgentPanels of Panels.PanelStore
  | AgentDone of Agent.AgentRunResult
  | DemoSend
  | StopAgent
  | ClearConversation
  | Tick
  // The askUser elicitation surface (Phase 465 §18 in the live loop).
  | AskOpened of envelopeWire: string
  | AskResolved of elicitationId: string * outcome: ElicitationOutcome
  | SelectOutputTab of Projection.Target
  // Client-only voice input (Phase 401).
  | ToggleMic
  | VoiceUpdate of finalText: string * interimText: string
  | VoiceError of string
  | VoiceEnded
  // Serverless live-drive (Phase 295).
  | TogglePresent
  | LiveReceive of LiveDriveMessage
  // Cross-device WebRTC pairing (Phase 295 Stage 2).
  | StartPairing
  | JoinPairing
  | PairOfferReady of string
  | SetPairOfferInput of string
  | SubmitPairOffer
  | PairAnswerReady of string
  | SetPairAnswerInput of string
  | SubmitPairAnswer
  | PairStateChanged of WebRtc.PeerState
  | PairError of string
  | CancelPairing
  // Dual-host wire-parity pane (Phase 85 surfaced in-app).
  | RunParity
  | ParityReady of string
  | ParityRendered of host: string * ok: bool * canonical: string option
  | SelectParityFixture of string

// Effect seams (the injected F# ports). One memory-only key store per provider
// (so switching keeps each key independently), and one provider adapter per id –
// each reads its own store's key per call (never captured, never persisted).
let private keyStores: Map<string, Byok.KeyStore> =
  Byok.providers |> List.map (fun p -> p.Id, Byok.createKeyStore ()) |> Map.ofList

let private providersById: Map<string, IAIProvider> =
  Byok.providers
  |> List.map (fun p -> p.Id, p.Create(fun () -> (Map.find p.Id keyStores).Get()))
  |> Map.ofList

// The agentic (tool-use) providers. As of Phase 327 ALL THREE are agentic (each
// descriptor carries a `CreateAgentic` factory over the shared `FuaranLive.AiWire`
// connectors), so the unified loop runs on any selected provider. Each reads its
// own key store per call, exactly like the single-shot adapters.
let private agenticById: Map<string, IAgenticProvider> =
  Byok.providers
  |> List.choose (fun p ->
    p.CreateAgentic
    |> Option.map (fun mk -> p.Id, mk (fun () -> (Map.find p.Id keyStores).Get())))
  |> Map.ofList

let private keyStoreFor (id: string) : Byok.KeyStore = Map.find id keyStores
let private providerFor (id: string) : IAIProvider = Map.find id providersById
let private effects = Byok.browserEffectPorts

// The self-debug loop's preview-DOM reader (browser-only – getBoundingClientRect
// over the `.fl-preview` pane) + the polled abort flag the Stop button sets. The
// loop depends only on the `DomReader` interface + the flag getter, so the same
// loop runs headlessly in tests against `Agent.nullDomReader` + a never-abort flag.
let private domReader = Agent.createBrowserDomReader ".fl-preview"
let private agentAbort = ref false

// The in-flight elicitation's continuation (Phase 465 §18): the loop suspends
// inside `Async.FromContinuations` while the transcript's ask row awaits the
// human; resolving calls this with the outcome's canonical wire. A module ref
// (like the abort flag / voice handle) so it never enters the model. At most
// one ask is pending at a time – the loop dispatches tool calls sequentially.
let private pendingAskResolve: (string -> unit) option ref = ref None

[<Literal>]
let private AgentMaxTokensPerCall = 4096

/// Start the unified self-debug loop as an Elmish effect: each timeline entry +
/// session advance + completion dispatches back into the model, so the loop
/// streams into the UI as it runs. `priorMessages` resumes a halted run with its
/// accumulated context. The loop itself is the injected-ports core in `Agent.fs`;
/// this only wires its sinks to `dispatch`. The budget is the hidden backstop
/// (`Agent.defaultBudget`), not a user control.
let private startAgentCmd
  (provider: IAgenticProvider)
  (session: Session.SessionState)
  (panels: Panels.PanelStore)
  (priorMessages: AgentMessage list)
  (prompt: string)
  (model: string)
  : Cmd<Msg> =
  Cmd.ofEffect (fun dispatch ->
    agentAbort.Value <- false

    let deps: Agent.AgentLoopDeps =
      { Provider = provider
        Dom = domReader
        Model = model
        MaxTokens = AgentMaxTokensPerCall
        Budget = Agent.defaultBudget
        Emit = fun e -> dispatch (AgentEmit e)
        OnSession = fun s -> dispatch (AgentSession s)
        OnPanels = fun p -> dispatch (AgentPanels p)
        ShouldAbort = fun () -> agentAbort.Value
        WaitForPaint = Agent.waitForPaint
        Now = Agent.nowMs
        SystemSuffix = ""
        // The askUser seam: suspend the loop on a continuation, mount the ask
        // row (AskOpened), and resume when the row resolves (AskResolved →
        // the stored continuation fires with the outcome wire).
        AskUser =
          fun envelopeWire ->
            Async.FromContinuations(fun (ok, _, _) ->
              pendingAskResolve.Value <- Some ok
              dispatch (AskOpened envelopeWire)) }

    Async.StartImmediate(
      async {
        let! result = Agent.runAgentLoop session panels priorMessages prompt deps
        dispatch (AgentDone result)
      }
    ))

/// A one-shot ~1s tick: while a run is in flight the `Tick` handler advances the
/// elapsed timer and re-issues this, giving a live clock without a subscription.
let private tickCmd: Cmd<Msg> =
  Cmd.ofEffect (fun dispatch ->
    Async.StartImmediate(
      async {
        do! Async.Sleep 1000
        dispatch Tick
      }
    ))

// ─── client-only developer-content enhancers (Phase 294) ─────────────────────
//
// `CodeBlock` and `Math` render deterministic markup; their interactive layer
// (a working copy button; KaTeX-rendered equations) is a post-hydration,
// client-only concern the host wires – exactly as the renderer documents. Both
// are dogfooded by the Output box + the gallery docs page.

/// The code text for a click on a `.fuaran-codeblock-copy` button (the sibling
/// `<code>` under the enclosing `.fuaran-codeblock`), or `null` for a click
/// elsewhere. The DOM walk is one Emit so it is robust across Fable bindings;
/// the actual copy goes through the injected clipboard effect (the §4l seam).
[<Emit("""(function(ev){
  var el = ev && ev.target;
  if (!el || !el.closest) return null;
  var btn = el.closest('.fuaran-codeblock-copy');
  if (!btn) return null;
  var box = btn.closest('.fuaran-codeblock');
  if (!box) return null;
  var code = box.querySelector('.fuaran-codeblock-code');
  return code ? code.textContent : null;
})($0)""")>]
let private codeTextForClick (ev: obj) : string = jsNative

/// One delegated document click listener that copies any CodeBlock's source
/// through the clipboard effect. Registered once at boot, so every CodeBlock in
/// the app (the Output box, an emitted code sample) has a working copy button.
let private copyEnhanceCmd: Cmd<Msg> =
  Cmd.ofEffect (fun _ ->
    Browser.Dom.document.addEventListener (
      "click",
      fun ev ->
        let text = codeTextForClick (box ev)

        if not (isNull (box text)) then
          effects.WriteToClipboard text
    ))

/// A KaTeX enhancement pass – upgrades the deterministic `Math` source fallback
/// to rendered math (idempotent, guarded). A macrotask (`setTimeout 0`) defers
/// past React's synchronous commit so the freshly-rendered tree is in the DOM;
/// unlike `requestAnimationFrame` it still fires in a backgrounded tab. Scheduled
/// after a tree change.
let private mathEnhanceCmd: Cmd<Msg> =
  Cmd.ofEffect (fun _ -> window.setTimeout ((fun () -> MathEnhance.enhanceDocument ()), 0) |> ignore)

// ─── client-only voice input (Phase 401) ─────────────────────────────────────
//
// The live recognition handle lives in a module-level ref (like `agentAbort`),
// so it never enters the model. Dictation streams transcripts back through
// `dispatch`; a committed transcript then runs the shipped Send loop unchanged.

let private voiceHandle: obj option ref = ref None

/// Start dictation, wiring the Web Speech callbacks to `dispatch`. The demo sends
/// audio nowhere itself; recognition is the browser's own.
let private startVoiceCmd: Cmd<Msg> =
  Cmd.ofEffect (fun dispatch ->
    let handle =
      Voice.start
        (System.Action<string, string>(fun final interim -> dispatch (VoiceUpdate(final, interim))))
        (System.Action<string>(fun err -> dispatch (VoiceError err)))
        (System.Action(fun () -> dispatch VoiceEnded))

    voiceHandle.Value <- Some handle)

/// Stop the live recognition (if any).
let private stopVoiceCmd: Cmd<Msg> =
  Cmd.ofEffect (fun _ ->
    match voiceHandle.Value with
    | Some h ->
      Voice.stop h
      voiceHandle.Value <- None
    | None -> ())

// ─── serverless live-drive (Phase 295) ───────────────────────────────────────
//
// The live channel lives in a module ref (like the voice handle / abort flag),
// so it never enters the model. On the presenting side it is opened by
// `TogglePresent`; on the audience side by `init` (which subscribes).

let private liveChannel: ILiveDriveChannel option ref = ref None

// Stage 2 (Phase 295) – the cross-device WebRTC transport. A separate channel ref
// so a presenter can drive a same-machine audience window (BroadcastChannel) AND a
// paired device (WebRTC) at once; a delta pushes to whichever channels are open.
// The offerer/answerer handles live here too (like the voice handle / abort flag),
// never in the model.
let private pairChannel: ILiveDriveChannel option ref = ref None
let private webrtcOfferer: WebRtc.IWebRtcOfferer option ref = ref None
let private webrtcAnswerer: WebRtc.IWebRtcAnswerer option ref = ref None

/// Push a computed present→audience delta over every open live-drive channel – the
/// same-machine `BroadcastChannel` and/or the cross-device WebRTC data channel
/// (no-op when there is nothing to send or no channel is open).
let private presentDeltaCmd (msgs: LiveDriveMessage list) : Cmd<Msg> =
  if List.isEmpty msgs then
    Cmd.none
  else
    Cmd.ofEffect (fun _ ->
      let send (r: ILiveDriveChannel option ref) =
        match r.Value with
        | Some ch -> msgs |> List.iter ch.Send
        | None -> ()

      send liveChannel
      send pairChannel)

/// Begin presenting: open the channel + an audience window, then (after the
/// audience has had a moment to subscribe) send the current full tree as the
/// initial sync. Subsequent changes ride `presentDeltaCmd` from the update loop.
let private startPresentCmd (session: Session.SessionState) : Cmd<Msg> =
  Cmd.ofEffect (fun _ ->
    let ch = Live.createBroadcastChannel ignore
    liveChannel.Value <- Some ch
    Live.openAudienceWindow ()

    Async.StartImmediate(
      async {
        do! Async.Sleep 600

        match session.Tree with
        | Some _ -> ch.Send(LiveDriveMessage.FullTree(Session.treeJson session))
        | None -> ()
      }
    ))

/// Stop presenting: close + drop the channel.
let private stopPresentCmd: Cmd<Msg> =
  Cmd.ofEffect (fun _ ->
    match liveChannel.Value with
    | Some ch ->
      ch.Close()
      liveChannel.Value <- None
    | None -> ())

/// Audience-side subscription: open the channel wired to dispatch each received
/// message into the loop. Registered once at boot when in audience mode.
let private audienceSubscribeCmd: Cmd<Msg> =
  Cmd.ofEffect (fun dispatch ->
    let ch = Live.createBroadcastChannel (fun m -> dispatch (LiveReceive m))
    liveChannel.Value <- Some ch)

// ─── Stage 2 pairing commands (WebRTC) ────────────────────────────────────────

/// Presenter side: create the offerer, keep its transport as the pair channel,
/// and generate the offer token (ICE-complete) to hand to the other device.
let private startOffererCmd: Cmd<Msg> =
  Cmd.ofEffect (fun dispatch ->
    let offerer = WebRtc.createOfferer ignore (fun s -> dispatch (PairStateChanged s))

    webrtcOfferer.Value <- Some offerer
    pairChannel.Value <- Some offerer.Channel

    Async.StartImmediate(
      async {
        let! token = offerer.GenerateOffer()
        dispatch (PairOfferReady token)
      }
    ))

/// Presenter side: apply the answer token pasted back from the joiner.
let private acceptAnswerCmd (token: string) : Cmd<Msg> =
  Cmd.ofEffect (fun dispatch ->
    match webrtcOfferer.Value with
    | Some offerer ->
      if not (offerer.AcceptAnswer token) then
        dispatch (PairError "That isn't a valid answer code.")
    | None -> ())

/// Joiner side: build the answerer from the pasted offer token and generate the
/// answer token to hand back. A malformed offer dispatches an error.
let private startAnswererCmd (offerToken: string) : Cmd<Msg> =
  Cmd.ofEffect (fun dispatch ->
    match
      WebRtc.createAnswerer offerToken (fun m -> dispatch (LiveReceive m)) (fun s -> dispatch (PairStateChanged s))
    with
    | Some answerer ->
      webrtcAnswerer.Value <- Some answerer
      pairChannel.Value <- Some answerer.Channel

      Async.StartImmediate(
        async {
          let! token = answerer.GenerateAnswer()
          dispatch (PairAnswerReady token)
        }
      )
    | None -> dispatch (PairError "That isn't a valid pairing code."))

/// Presenter side: once the data channel opens, push the current full tree as the
/// initial sync (subsequent edits ride `presentDeltaCmd`).
let private pairSyncCmd (session: Session.SessionState) : Cmd<Msg> =
  match session.Tree with
  | Some _ ->
    Cmd.ofEffect (fun _ ->
      match pairChannel.Value with
      | Some ch -> ch.Send(LiveDriveMessage.FullTree(Session.treeJson session))
      | None -> ())
  | None -> Cmd.none

/// Tear the pairing down on either side: close the transport + drop every handle.
let private stopPairCmd: Cmd<Msg> =
  Cmd.ofEffect (fun _ ->
    match pairChannel.Value with
    | Some ch -> ch.Close()
    | None -> ()

    pairChannel.Value <- None
    webrtcOfferer.Value <- None
    webrtcAnswerer.Value <- None)

/// The parity pane's current drive: the wire posted to both hosts + the
/// canonical bytes each host's re-encoding must equal. A selected corpus
/// fixture wins over the session tree; for the session tree the two coincide
/// (its wire is already canonical), while a §16 lenient fixture posts the
/// shorthand and expects the normalised form.
let private parityInput (model: Model) : (string * string) option =
  match model.ParityFixture with
  | Some f -> Some(f.wire, f.expected)
  | None ->
    model.Session.Tree
    |> Option.map (fun t ->
      let wire = Canon.encodeNode t
      wire, wire)

/// Post a wire to both host iframes (drives a parity re-check). No-op when
/// there is nothing to post or the host pages were not built.
let private parityPostCmd (wire: string option) : Cmd<Msg> =
  match wire with
  | Some w ->
    Cmd.ofEffect (fun _ ->
      postWireToFrame tsFrameId w
      postWireToFrame fsFrameId w)
  | None -> Cmd.none

/// Register the window `message` listener the host iframes post their `ready` /
/// `rendered` acknowledgements to (mirrors the fable-host's own init listener).
/// Only wired when the dual-host pages exist.
let private parityListenCmd: Cmd<Msg> =
  if not dualHostEnabled then
    Cmd.none
  else
    Cmd.ofEffect (fun dispatch ->
      window.addEventListener (
        "message",
        fun ev ->
          let data = ev?data

          if not (isNull (box data)) then
            match data?kind with
            | "fuaran:ready" -> dispatch (ParityReady(string data?host))
            | "fuaran:rendered" ->
              let host = string data?host
              let ok: bool = unbox data?ok

              let canonical =
                let c = data?canonical
                if isNull (box c) then None else Some(string c)

              dispatch (ParityRendered(host, ok, canonical))
            | _ -> ()
      ))

let private init () : Model * Cmd<Msg> =
  // Restore a shared creation from the URL fragment on load (client-only – the
  // link IS the data). A fresh session otherwise.
  let session =
    match Permalink.fromHash () with
    | Some tree ->
      { Session.empty with
          Tree = Some tree
          Snapshots = [ tree ] }
    | None -> Session.empty

  let isAudience = Live.isAudience ()

  { Session = session
    Prompt = ""
    VoiceSupported = Voice.isSupported ()
    Listening = false
    VoiceBase = ""
    IsAudience = isAudience
    Presenting = false
    ProviderId = Byok.defaultProviderId
    KeyPresent = false
    AgentRunning = false
    Error = None
    Model = (Byok.descriptorFor Byok.defaultProviderId).DefaultModel
    Dark = Brand.initialDark ()
    Comparing = false
    Compare = None
    AgentTimeline = []
    Panels = Panels.empty
    AgentMessages = []
    RunTokensIn = 0
    RunTokensOut = 0
    RunIteration = 0
    SessionTokens = 0
    SessionCostUsd = 0.0
    SessionCostKnown = true
    Elapsed = 0
    OutputTab = Projection.Target.Json
    ParityTs = None
    ParityFs = None
    ParityFixture = None
    // A device that followed a `?live=pair` link lands straight in the joiner panel.
    Pair =
      (if WebRtc.isPairJoinRequested () then
         { pairEmpty with
             Role = Some PairRole.Joiner }
       else
         pairEmpty)
    Asks = Map.empty },
  // Register the client-only enhancers once; run an initial KaTeX pass in case a
  // permalink restored a tree carrying an equation. In audience mode also
  // subscribe to the live-drive channel.
  Cmd.batch
    [ parityListenCmd
      copyEnhanceCmd
      mathEnhanceCmd
      Cmd.ofEffect (fun _ -> applyDarkClass (Brand.initialDark ()))
      (if isAudience then audienceSubscribeCmd else Cmd.none) ]

// ─── update – the unified loop ───────────────────────────────────────────────

let private update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
  match msg with
  | SetPrompt p -> { model with Prompt = p }, Cmd.none
  | ToggleTheme ->
    let dark = not model.Dark

    { model with Dark = dark },
    Cmd.ofEffect (fun _ ->
      Brand.persistDark dark
      applyDarkClass dark)
  | ToggleMic ->
    if not model.VoiceSupported then
      model, Cmd.none
    elif model.Listening then
      { model with Listening = false }, stopVoiceCmd
    else
      // Capture what the visitor already typed so speech is appended to it.
      { model with
          Listening = true
          VoiceBase = model.Prompt
          Error = None },
      startVoiceCmd
  | VoiceUpdate(final, interim) ->
    { model with
        Prompt = Voice.composePrompt model.VoiceBase final interim },
    Cmd.none
  | VoiceError code ->
    // "aborted" (user stop) / "no-speech" are benign – end quietly; surface only
    // genuine failures (permission denied, no mic, service unreachable).
    let benign = code = "aborted" || code = "no-speech"

    { model with
        Listening = false
        Error =
          if benign then
            model.Error
          else
            Some
              { Source = "voice"
                Message = Voice.friendlyError code } },
    stopVoiceCmd
  | VoiceEnded -> { model with Listening = false }, Cmd.none
  | TogglePresent ->
    if model.IsAudience then
      model, Cmd.none
    elif model.Presenting then
      { model with Presenting = false }, stopPresentCmd
    else
      { model with Presenting = true }, startPresentCmd model.Session
  | LiveReceive m ->
    // Audience side: apply the received tree/op through the same engine the
    // authoring loop uses (surgical for ops, wholesale for a full tree).
    let payload =
      match m with
      | LiveDriveMessage.FullTree j -> j
      | LiveDriveMessage.Op j -> j

    match Session.ingest model.Session payload with
    | Session.Ingested(_, next) -> { model with Session = next }, mathEnhanceCmd
    | Session.IngestFailed _ -> model, Cmd.none
  | StartPairing ->
    { model with
        Pair =
          { pairEmpty with
              Role = Some PairRole.Presenter
              Status = WebRtc.PeerState.Connecting } },
    startOffererCmd
  | JoinPairing ->
    { model with
        Pair =
          { pairEmpty with
              Role = Some PairRole.Joiner } },
    Cmd.none
  | PairOfferReady token ->
    { model with
        Pair = { model.Pair with Offer = token } },
    Cmd.none
  | SetPairOfferInput s ->
    { model with
        Pair = { model.Pair with OfferInput = s } },
    Cmd.none
  | SubmitPairOffer ->
    let token = model.Pair.OfferInput.Trim()

    if token = "" then
      model, Cmd.none
    else
      { model with
          Pair =
            { model.Pair with
                Status = WebRtc.PeerState.Connecting
                Error = None } },
      startAnswererCmd token
  | PairAnswerReady token ->
    { model with
        Pair = { model.Pair with Answer = token } },
    Cmd.none
  | SetPairAnswerInput s ->
    { model with
        Pair = { model.Pair with AnswerInput = s } },
    Cmd.none
  | SubmitPairAnswer ->
    let token = model.Pair.AnswerInput.Trim()

    if token = "" then
      model, Cmd.none
    else
      { model with
          Pair = { model.Pair with Error = None } },
      acceptAnswerCmd token
  | PairStateChanged st ->
    let model =
      { model with
          Pair = { model.Pair with Status = st } }

    // Presenter side: once the data channel connects, push the current tree as the
    // initial sync so the joiner starts from the live creation.
    match st, model.Pair.Role with
    | WebRtc.PeerState.Connected, Some PairRole.Presenter -> model, pairSyncCmd model.Session
    | _ -> model, Cmd.none
  | PairError msg ->
    { model with
        Pair =
          { model.Pair with
              Error = Some msg
              Status = WebRtc.PeerState.Failed } },
    Cmd.none
  | CancelPairing -> { model with Pair = pairEmpty }, stopPairCmd
  | LoadTree tree ->
    // Loading an example re-points the parity pane at the session tree (a
    // previously picked corpus fixture would otherwise keep overriding it).
    let prevSession = model.Session

    let model =
      { model with
          Error = None
          ParityTs = None
          ParityFs = None
          ParityFixture = None
          Session =
            { model.Session with
                Tree = Some tree
                Ops = []
                Snapshots = [ tree ] } }

    let delta =
      if model.Presenting || model.Pair.Status = WebRtc.PeerState.Connected then
        Live.liveDriveDelta prevSession model.Session
      else
        []

    model,
    Cmd.batch
      [ parityPostCmd (parityInput model |> Option.map fst)
        mathEnhanceCmd
        presentDeltaCmd delta ]
  | Share ->
    match model.Session.Tree with
    | Some tree ->
      effects.WriteToClipboard(Permalink.shareUrl tree)
      Permalink.writeToHash tree
      model, Cmd.none
    | None -> model, Cmd.none
  | RunCompare ->
    if model.Comparing || model.Prompt.Trim() = "" || not model.KeyPresent then
      model, Cmd.none
    else
      { model with
          Comparing = true
          Error = None },
      Cmd.OfAsync.perform
        (fun () -> Compare.compareEmissions (providerFor model.ProviderId) model.Model model.Prompt)
        ()
        CompareDone
  | CompareDone result ->
    { model with
        Comparing = false
        Compare = Some result },
    Cmd.none
  | SetKey k ->
    let store = keyStoreFor model.ProviderId
    store.Set k
    { model with KeyPresent = store.Has() }, Cmd.none
  | SelectProvider id ->
    if id = model.ProviderId then
      model, Cmd.none
    else
      let desc = Byok.descriptorFor id

      { model with
          ProviderId = desc.Id
          Model = desc.DefaultModel
          KeyPresent = (keyStoreFor desc.Id).Has()
          Error = None },
      Cmd.none
  | ClearConversation ->
    { model with
        Session = Session.empty
        Prompt = ""
        AgentTimeline = []
        Panels = Panels.empty
        Asks = Map.empty
        AgentMessages = []
        RunTokensIn = 0
        RunTokensOut = 0
        RunIteration = 0
        SessionTokens = 0
        SessionCostUsd = 0.0
        SessionCostKnown = true
        Elapsed = 0
        Error = None },
    Cmd.none
  | AgentEmit e ->
    // Accumulate the run + session tallies live as each model call reports its
    // usage; append the entry to the streamed transcript. Session cost is priced
    // per call at the model in effect for that call, so a provider switch
    // between runs never re-prices earlier spend.
    let model =
      match e with
      | Agent.TimelineEntry.ModelCall(_, iteration, usage) ->
        let tIn, tOut =
          match usage with
          | Some u -> u.InputTokens, u.OutputTokens
          | None -> 0, 0

        let callCost = Byok.estimateCostUsd model.Model tIn tOut

        { model with
            RunIteration = iteration
            RunTokensIn = model.RunTokensIn + tIn
            RunTokensOut = model.RunTokensOut + tOut
            SessionTokens = model.SessionTokens + tIn + tOut
            SessionCostUsd = model.SessionCostUsd + (callCost |> Option.defaultValue 0.0)
            SessionCostKnown = model.SessionCostKnown && callCost.IsSome }
      | _ -> model

    { model with
        AgentTimeline = model.AgentTimeline @ [ e ] },
    Cmd.none
  | AgentSession s ->
    // While presenting (same-machine window and/or a paired device), push the
    // delta (new ops, or a full-tree replacement) so the audience re-renders live.
    let delta =
      if model.Presenting || model.Pair.Status = WebRtc.PeerState.Connected then
        Live.liveDriveDelta model.Session s
      else
        []

    { model with Session = s }, Cmd.batch [ mathEnhanceCmd; presentDeltaCmd delta ]
  | AgentPanels store ->
    // A panel-turn emission advanced the panel store – the transcript's live
    // panel rows re-render from it. A panel may carry Math; re-enhance.
    { model with Panels = store }, mathEnhanceCmd
  | DemoSend ->
    // The keyless scripted demo (Phase 466): a canned provider drives the REAL
    // loop, streaming a progress panel + a growing findings table into the
    // transcript. No key, no network – a fresh provider per run so the script
    // starts from its first turn every time.
    if model.AgentRunning then
      model, Cmd.none
    else
      { model with
          AgentRunning = true
          Error = None
          RunTokensIn = 0
          RunTokensOut = 0
          RunIteration = 0
          Elapsed = 0 },
      Cmd.batch
        [ startAgentCmd (Demo.createProvider ()) model.Session model.Panels [] Demo.prompt "scripted-demo"
          tickCmd ]
  | AgentDone result ->
    { model with
        AgentRunning = false
        AgentMessages = result.FinalMessages
        Error =
          match result.ProviderError with
          | Some e ->
            Some
              { Source = "agent"
                Message = e.Message }
          | None -> model.Error },
    Cmd.none
  | AskOpened envelopeWire ->
    // The loop already decode-gated the envelope; a failure here is unreachable
    // in practice, but resolve-with-Declined keeps the loop suspension safe.
    (match Elc.decodeEnvelope envelopeWire with
     | Ok env ->
       { model with
           Asks =
             Map.add
               env.ElicitationId
               ({ Envelope = env
                  Wire = envelopeWire
                  Outcome = None }
               : Ask.AskRecord)
               model.Asks },
       Cmd.none
     | Error _ ->
       model,
       Cmd.ofEffect (fun _ ->
         match pendingAskResolve.Value with
         | Some resolve ->
           pendingAskResolve.Value <- None

           resolve (
             Elc.encodeOutcome
               { ElicitationId = "unknown"
                 Outcome = ElicitationOutcome.Declined }
           )
         | None -> ()))
  | AskResolved(elicitationId, outcome) ->
    match Map.tryFind elicitationId model.Asks with
    | Some record when record.Outcome.IsNone ->
      let wire =
        Elc.encodeOutcome
          { ElicitationId = elicitationId
            Outcome = outcome }

      { model with
          Asks =
            Map.add
              elicitationId
              { record with
                  Outcome = Some(Ask.outcomeKindName outcome, wire) }
              model.Asks },
      // Fire the stored continuation – the suspended loop resumes with the
      // typed outcome as the askUser tool result.
      Cmd.ofEffect (fun _ ->
        match pendingAskResolve.Value with
        | Some resolve ->
          pendingAskResolve.Value <- None
          resolve wire
        | None -> ())
    | _ -> model, Cmd.none
  | StopAgent ->
    agentAbort.Value <- true

    // A Stop while a question is pending resolves it as Declined, so the
    // suspended loop can wake, observe the abort flag, and halt cleanly.
    let pendingAsk =
      model.Asks
      |> Map.toList
      |> List.tryPick (fun (id, r) -> if r.Outcome.IsNone then Some id else None)

    match pendingAsk with
    | Some id when pendingAskResolve.Value.IsSome -> model, Cmd.ofMsg (AskResolved(id, ElicitationOutcome.Declined))
    | _ -> model, Cmd.none
  | Tick ->
    if model.AgentRunning then
      { model with
          Elapsed = model.Elapsed + 1 },
      tickCmd
    else
      model, Cmd.none
  | SelectOutputTab t -> { model with OutputTab = t }, Cmd.none
  | RunParity ->
    match parityInput model with
    | Some(wire, _) ->
      { model with
          ParityTs = None
          ParityFs = None },
      parityPostCmd (Some wire)
    | None -> model, Cmd.none
  | ParityReady host ->
    // A host iframe just mounted – feed it the current wire so it renders + reports.
    let frameId = if host = "ts" then tsFrameId else fsFrameId

    match parityInput model with
    | Some(wire, _) -> model, Cmd.ofEffect (fun _ -> postWireToFrame frameId wire)
    | None -> model, Cmd.none
  | ParityRendered(host, ok, canonical) ->
    let report = Some { Ok = ok; Canonical = canonical }

    (if host = "ts" then
       { model with ParityTs = report }
     else
       { model with ParityFs = report }),
    Cmd.none
  | SelectParityFixture id ->
    // "" (the "Current tree" option) finds no fixture and falls back to the
    // session tree; both host reports reset so the verdict re-derives.
    let model =
      { model with
          ParityFixture = parityFixtures |> Array.tryFind (fun f -> f.id = id)
          ParityTs = None
          ParityFs = None }

    model, parityPostCmd (parityInput model |> Option.map fst)
  | Send ->
    if model.AgentRunning || model.Prompt.Trim() = "" then
      model, Cmd.none
    else
      match Map.tryFind model.ProviderId agenticById with
      | Some prov when model.KeyPresent ->
        let prompt = model.Prompt

        { model with
            AgentRunning = true
            Error = None
            Prompt = ""
            RunTokensIn = 0
            RunTokensOut = 0
            RunIteration = 0
            Elapsed = 0 },
        // Resume from the accumulated context; do NOT clear the transcript or the
        // session token/cost tally – the conversation persists across runs. Only
        // the per-run counters reset (each Send is one budgeted run).
        Cmd.batch
          [ startAgentCmd prov model.Session model.Panels model.AgentMessages prompt model.Model
            tickCmd ]
      | _ ->
        { model with
            Error =
              Some
                { Source = "agent"
                  Message = "Set your API key to send." } },
        Cmd.none

// The chrome header is now the branded `topbar` (Feliz) defined near the view –
// brand mark + BYOK badge + provider/key controls + theme toggle.

// The showcase (the demo gallery) is a separate site entry / origin. Use a
// build-time VITE_SHOWCASE_ORIGIN when set (two-origin production), else fall
// back to the same-origin showcase.html (works in dev + a single-origin deploy).
// Mirrors how the showcase links back via VITE_PLAYGROUND_ORIGIN.
let private showcaseHref: string =
  let configured: string =
    emitJsExpr () "((import.meta.env && import.meta.env.VITE_SHOWCASE_ORIGIN) || '')"

  if configured = "" then "showcase.html" else configured

// A short project intro rendered as a Fuaran tree (exhibit zero – the playground
// chrome is Fuaran too). Mirrors the showcase landing's intro so a visitor who
// lands straight on the playground gets the same framing + the nine-host claim,
// plus a door across to the showcase (the reciprocal of the showcase's door here).
let private heroIntro: Node<unit> =
  Fuaran.dashboard
    "pg-hero-block"
    { Defaults.dashboard with
        Children =
          [ Fuaran.markdown
              "pg-hero-intro"
              "**Fuaran** is a language for generative UI – an interface is *data*, a typed tree with one canonical wire format rather than framework code. The same artefact renders across **nine host languages** (F#, C#, Visual Basic, TypeScript, Python, Go, Kotlin, Rust, and Swift), so what you build here is portable by construction."
            Fuaran.heading
              "pg-hero-title"
              { Level = 1
                Text = TextSource.Literal "See what UI-as-data can do."
                Variant = HeadingVariant.Standard }
            Fuaran.markdown
              "pg-hero-showcase"
              (sprintf
                "New here? Browse the **[showcase of what UI-as-data can do →](%s)** – a gallery of live demos, no key needed."
                showcaseHref) ] }

// ─── panes (Feliz, dispatching Msg; preview uses the F# renderer) ─────────────

let private paneCard (title: string) (body: ReactElement) : ReactElement =
  Html.div
    [ prop.className "fl-card"
      prop.children [ Html.h2 [ prop.className "fl-card-heading"; prop.text title ]; body ] ]

// ─── the unified conversation pane (the streamed self-debug loop) ─────────────

let private tlRow (key: int) (cls: string) (tag: string) (body: string) : ReactElement =
  Html.li
    [ prop.key key
      prop.className cls
      prop.children
        [ Html.span [ prop.className "fl-tl-tag"; prop.text tag ]
          Html.span [ prop.className "fl-tl-body"; prop.text body ] ] ]

// ─── live panel rows (Phase 466) ─────────────────────────────────────────────
//
// A panel row renders the panel's CURRENT tree from the store – so later ops
// update it in place – inside the panel's own isolated renderer scope
// (`StateStore.forScope`), which keeps every panel's reactive `Binding.State`
// keys off the default store and off every other panel. The row subscribes to
// its scope so post-turn interaction (tabs, filters – the write-back default)
// re-renders it with no host glue: the panel stays ALIVE after the run ends.

/// The panels' `Action.SetState` / write-back substrate: the browser runtime
/// routes scoped writes into the per-panel store the row subscribes to.
let private panelRuntime: Runtime.IFuaranRuntime = BrowserRuntime.create ()

[<ReactComponent>]
let private PanelRow (panel: Panels.Panel) : ReactElement =
  let tick, setTick = React.useState 0

  let verified, setVerified = React.useState (None: Panels.VerifyReport option)

  // Re-render on any write within this panel's scope (interaction liveness);
  // re-subscribing per tick keeps the closure fresh, mirroring useStateKeys.
  React.useEffect (
    ((fun () -> (StateStore.forScope (Panels.scopeId panel.Id)).Subscribe(fun () -> setTick (tick + 1)))
    : unit -> unit -> unit),
    [| box panel.Id; box tick |]
  )

  let head =
    match List.tryLast panel.Chain with
    | Some entry -> entry.Hash.Substring(0, 8) + "…"
    | None -> panel.BaseHash.Substring(0, 8) + "…"

  Html.div
    [ prop.className "fl-panel"
      prop.children
        [ Html.div
            [ prop.className "fl-panel-head"
              prop.children
                [ Html.span
                    [ prop.className "fl-panel-title"
                      prop.text (panel.Title |> Option.defaultValue panel.Id) ]
                  Html.code [ prop.className "fl-panel-id"; prop.text ("#" + panel.Id) ]
                  Html.span
                    [ prop.className "fl-panel-chain"
                      prop.title
                        "This panel is its own hash-chained op stream: chain seed = the content hash of its first tree; every op links to its predecessor."
                      prop.text (sprintf "%d op(s) · head %s" (List.length panel.Chain) head) ]
                  Html.button
                    [ prop.className "fl-panel-verify"
                      prop.title
                        "Refold the panel from its recorded base tree + op stream through the real apply engine, and recompute its hash chain – proving the rendered panel is exactly its op-stream scope."
                      prop.text "⟲ verify replay"
                      prop.onClick (fun _ -> setVerified (Some(Panels.verify panel))) ]
                  (match verified with
                   | None -> Html.none
                   | Some report ->
                     let ok = report.ReplayOk && report.ChainOk

                     Html.span
                       [ prop.className (
                           if ok then
                             "fl-panel-badge fl-panel-ok"
                           else
                             "fl-panel-badge fl-panel-bad"
                         )
                         prop.text (
                           if ok then
                             sprintf "✓ replayed %d op(s) – tree + chain match" report.Steps
                           else
                             sprintf
                               "✗ replay diverged (tree %s, chain %s)"
                               (if report.ReplayOk then "ok" else "differs")
                               (if report.ChainOk then "ok" else "differs")
                         ) ]) ] ]
          Html.div
            [ prop.className "fl-panel-body"
              prop.children
                [ Render.renderWithSourcesInScope
                    (Panels.scopeId panel.Id)
                    BindingResolver.empty
                    panelRuntime
                    ignore
                    panel.Tree ] ] ] ]

let private timelineEntryView
  (panels: Panels.PanelStore)
  (asks: Map<string, Ask.AskRecord>)
  (resolveAsk: string -> ElicitationOutcome -> unit)
  (entry: Agent.TimelineEntry)
  : ReactElement =
  let key = Agent.entryOrd entry

  match entry with
  | Agent.TimelineEntry.UserPrompt(_, text) -> tlRow key "fl-tl fl-tl-user" "you" text
  | Agent.TimelineEntry.ModelCall(_, iteration, usage) ->
    let body =
      match usage with
      | Some u -> sprintf "%d in / %d out tokens" u.InputTokens u.OutputTokens
      | None -> "model responded"

    tlRow key "fl-tl fl-tl-call" (sprintf "call #%d" iteration) body
  | Agent.TimelineEntry.ModelText(_, text) -> tlRow key "fl-tl fl-tl-text" "says" text
  | Agent.TimelineEntry.Emission(_, mode, ok, summary) ->
    tlRow
      key
      (if ok then
         "fl-tl fl-tl-emit"
       else
         "fl-tl fl-tl-emit fl-tl-bad")
      (if ok then "emit " + mode else "emit failed")
      summary
  | Agent.TimelineEntry.ToolCall(_, name, input, resultSummary, ok) ->
    Html.li
      [ prop.key key
        prop.className (
          if ok then
            "fl-tl fl-tl-tool"
          else
            "fl-tl fl-tl-tool fl-tl-bad"
        )
        prop.children
          [ Html.span [ prop.className "fl-tl-tag"; prop.text ("🔧 " + name) ]
            Html.details
              [ prop.className "fl-tl-details"
                prop.children
                  [ Html.summary [ prop.text resultSummary ]
                    Html.pre [ prop.className "fl-code"; prop.text (Agent.jsonPretty input) ] ] ] ] ]
  | Agent.TimelineEntry.PanelEmission(_, panelId, mode, ok, isNew, summary) ->
    if not ok then
      tlRow key "fl-tl fl-tl-emit fl-tl-bad" "panel failed" summary
    elif isNew then
      // The panel's first appearance mounts its LIVE row here: rendered from
      // the panel store, so every later op updates it in this same spot.
      match Map.tryFind panelId panels.Panels with
      | Some panel ->
        Html.li
          [ prop.key key
            prop.className "fl-tl fl-tl-panel"
            prop.children [ PanelRow panel ] ]
      | None -> tlRow key "fl-tl fl-tl-emit" ("panel " + panelId) summary
    else
      tlRow key "fl-tl fl-tl-emit" ("panel " + mode) summary
  | Agent.TimelineEntry.Ask(_, elicitationId, summary) ->
    // The ask's LIVE row mounts here (Phase 465 §18): the decoded question
    // tree rendered in its own state scope, with the host's answer gate –
    // exactly as a panel's first appearance mounts its live row.
    match Map.tryFind elicitationId asks with
    | Some record ->
      Html.li
        [ prop.key key
          prop.className "fl-tl fl-tl-ask"
          prop.children [ Ask.AskRow record (resolveAsk elicitationId) ] ]
    | None -> tlRow key "fl-tl fl-tl-ask" "ask" summary
  | Agent.TimelineEntry.Halt(_, reason, detail) ->
    let body =
      Agent.haltLabel reason
      + (match detail with
         | Some d -> " (" + d + ")"
         | None -> "")

    tlRow key "fl-tl fl-tl-halt" "halt" body

/// Compact token count for the status bar: 850 / 12.4k / 2.0M.
let private fmtTokens (n: int) : string =
  if n >= 1_000_000 then
    sprintf "%.1fM" (float n / 1_000_000.0)
  elif n >= 1_000 then
    sprintf "%.1fk" (float n / 1_000.0)
  else
    string n

/// An indicative dollar figure: "≈$0.12", or "<$0.01" for a nonzero sliver.
let private fmtUsd (c: float) : string =
  if c >= 0.005 then sprintf "≈$%.2f" c
  elif c > 0.0 then "<$0.01"
  else "≈$0.00"

/// The live telemetry status bar (Phase 328, budget-aware since Phase 115): the
/// current run's call count + token tally reported against the hard budget
/// backstop, an indicative run cost, the session total, the elapsed timer, and
/// the Stop button (visible only while a run is in flight). Rendered before the
/// first prompt too, so the caps are visible up front.
let private statusBar (model: Model) (dispatch: Msg -> unit) : ReactElement =
  let budget = Agent.defaultBudget
  let runTokens = model.RunTokensIn + model.RunTokensOut

  let runCost =
    match Byok.estimateCostUsd model.Model model.RunTokensIn model.RunTokensOut with
    | Some c when runTokens > 0 -> " · " + fmtUsd c
    | _ -> ""

  let sessionCost =
    if model.SessionCostKnown && model.SessionTokens > 0 then
      " · " + fmtUsd model.SessionCostUsd
    else
      ""

  let item (text: string) =
    Html.span [ prop.className "fl-status-item"; prop.text text ]

  Html.div
    [ prop.className "fl-status"
      prop.children
        [ item (sprintf "call %d/%d" model.RunIteration budget.MaxIterations)
          item (sprintf "run %s / %s tokens%s" (fmtTokens runTokens) (fmtTokens budget.MaxTokens) runCost)
          item (sprintf "session %s tokens%s" (fmtTokens model.SessionTokens) sessionCost)
          item (sprintf "%ds" model.Elapsed)
          (if model.AgentRunning then
             Html.span [ prop.className "fl-status-item fl-status-live"; prop.text "● running" ]
           else
             Html.none)
          (if model.AgentRunning then
             Html.button
               [ prop.className "fl-btn ghost fl-status-stop"
                 prop.text "◼ Stop"
                 prop.onClick (fun _ -> dispatch StopAgent) ]
           else
             Html.none) ] ]

let private transcriptView (model: Model) (dispatch: Msg -> unit) : ReactElement =
  if List.isEmpty model.AgentTimeline then
    Html.div
      [ prop.className "fl-empty"
        prop.text
          "Send a prompt – the model emits a UI, then inspects what actually rendered and repairs it. The conversation, tool calls, emissions, live panels, and typed questions stream here." ]
  else
    let resolveAsk (id: string) (outcome: ElicitationOutcome) : unit = dispatch (AskResolved(id, outcome))

    Html.ol
      [ prop.className "fl-timeline"
        prop.children [ for entry in model.AgentTimeline -> timelineEntryView model.Panels model.Asks resolveAsk entry ] ]

let private conversationPane (model: Model) (dispatch: Msg -> unit) : ReactElement =
  let conversationEmpty =
    Option.isNone model.Session.Tree && List.isEmpty model.AgentTimeline

  Html.div
    [ prop.className "fl-chat"
      prop.children
        [ statusBar model dispatch
          Html.div
            [ prop.className "fl-transcript"
              prop.children [ transcriptView model dispatch ] ]
          Html.textarea
            [ prop.className "fl-prompt"
              prop.value model.Prompt
              prop.placeholder "Describe a UI to build (or an edit to make)…"
              prop.disabled model.AgentRunning
              prop.onChange (fun (v: string) -> dispatch (SetPrompt v)) ]
          Html.div
            [ prop.className "fl-send-row"
              prop.children
                [ Html.button
                    [ prop.className "fl-send"
                      prop.disabled (model.AgentRunning || not model.KeyPresent)
                      prop.text (if model.AgentRunning then "Running…" else "Send")
                      prop.onClick (fun _ -> dispatch Send) ]
                  // Client-only voice input (Phase 401) – shown only where the
                  // browser supports it; a graceful no-op otherwise.
                  (if model.VoiceSupported then
                     Html.button
                       [ prop.className (if model.Listening then "fl-mic fl-mic-live" else "fl-mic")
                         prop.disabled model.AgentRunning
                         prop.title
                           "Dictate your prompt – the demo sends audio nowhere; your browser does the speech recognition"
                         prop.text (
                           if model.Listening then
                             "● Listening… tap to stop"
                           else
                             "🎤 Speak"
                         )
                         prop.onClick (fun _ -> dispatch ToggleMic) ]
                   else
                     Html.none)
                  Html.button
                    [ prop.className "fl-btn ghost"
                      prop.disabled (model.AgentRunning || conversationEmpty)
                      prop.title
                        "Clear the conversation, the emitted UI, and the counters – start fresh. Your key and settings stay."
                      prop.text "Clear"
                      prop.onClick (fun _ -> dispatch ClearConversation) ]
                  (if model.AgentRunning then
                     Html.button
                       [ prop.className "fl-btn ghost"
                         prop.text "◼ Stop"
                         prop.onClick (fun _ -> dispatch StopAgent) ]
                   else
                     Html.none) ] ]
          (if model.VoiceSupported then
             Html.p
               [ prop.className "fl-voice-note"
                 prop.text (
                   if model.Listening then
                     "Listening… speak your prompt, then tap Send. Your audio never reaches our servers – there are none."
                   else
                     "Or tap Speak and say it – e.g. \"build me a dashboard with a revenue chart and a filter\". Audio stays in your browser."
                 ) ]
           else
             Html.none)
          // The keyless panel-turns demo (Phase 466): a scripted agent drives
          // the real loop, streaming live panels into this transcript.
          Html.div
            [ prop.className "fl-demo-row"
              prop.children
                [ Html.button
                    [ prop.className "fl-demo"
                      prop.disabled model.AgentRunning
                      prop.title
                        "A canned script drives the real agent loop – no key, no network – so you can watch panel turns stream: a live progress panel, a findings table growing row by row, a reach-back to an earlier panel by id, and a typed question you answer through a validated contract."
                      prop.text "▶ Watch a scripted agent stream live panels & ask you a typed question – no key needed"
                      prop.onClick (fun _ -> dispatch DemoSend) ] ] ] ] ]

let private previewPane (model: Model) : ReactElement =
  match model.Session.Tree with
  | None -> Html.div [ prop.className "fl-empty"; prop.text "Your generated UI renders here." ]
  | Some tree -> Render.renderWithSources BindingResolver.empty ignore tree

let private inspectorPane (model: Model) : ReactElement =
  let json = Session.treeJson model.Session

  Html.pre
    [ prop.className "fl-inspector"
      prop.text (if json = "" then "– no tree yet –" else json) ]

// ─── cross-device pairing panel (Phase 295 Stage 2) ──────────────────────────
//
// The serverless WebRTC handshake made visible: the presenter's offer and the
// joiner's answer are each shown as a QR code + a copy-paste token, carried
// across out-of-band (there is no signalling server). A single STUN server does
// NAT traversal; the UI data itself flows peer-to-peer over the data channel.

let private pairStatusText (s: WebRtc.PeerState) : string =
  match s with
  | WebRtc.PeerState.New -> ""
  | WebRtc.PeerState.Connecting -> "Connecting…"
  | WebRtc.PeerState.Connected -> "✓ Connected – driving live, peer-to-peer"
  | WebRtc.PeerState.Disconnected -> "Disconnected – the peer may have closed their tab"
  | WebRtc.PeerState.Failed -> "Connection failed – cancel and try again"
  | WebRtc.PeerState.Closed -> "Connection closed"

/// A generated handshake token shown as a scannable QR (when it fits one) plus a
/// read-only copy box – the thing the operator carries to the other device.
let private pairTokenShare (step: string) (token: string) : ReactElement =
  let qr = WebRtc.qrDataUrl token 4

  Html.div
    [ prop.className "fl-pair-token"
      prop.children
        [ Html.p [ prop.className "fl-pair-step"; prop.text step ]
          (if token = "" then
             Html.p [ prop.className "fl-pair-wait"; prop.text "Generating pairing code…" ]
           else
             Html.div
               [ prop.className "fl-pair-share"
                 prop.children
                   [ (if qr <> "" then
                        Html.img [ prop.className "fl-pair-qr"; prop.src qr; prop.alt "Pairing QR code" ]
                      else
                        Html.none)
                     Html.textarea
                       [ prop.className "fl-pair-code"
                         prop.readOnly true
                         prop.rows 3
                         prop.value token ]
                     Html.button
                       [ prop.className "fl-btn ghost"
                         prop.text "Copy code"
                         prop.onClick (fun _ -> effects.WriteToClipboard token) ] ] ]) ] ]

/// A paste box for the peer's token + the button that consumes it.
let private pairTokenPaste
  (step: string)
  (value: string)
  (placeholder: string)
  (btnLabel: string)
  (onInput: string -> unit)
  (onSubmit: unit -> unit)
  : ReactElement =
  Html.div
    [ prop.className "fl-pair-paste"
      prop.children
        [ Html.p [ prop.className "fl-pair-step"; prop.text step ]
          Html.textarea
            [ prop.className "fl-pair-code"
              prop.rows 3
              prop.placeholder placeholder
              prop.value value
              prop.onChange (fun (v: string) -> onInput v) ]
          Html.button
            [ prop.className "fl-btn"
              prop.disabled (value.Trim() = "")
              prop.text btnLabel
              prop.onClick (fun _ -> onSubmit ()) ] ] ]

let private pairPanel (model: Model) (dispatch: Msg -> unit) : ReactElement =
  let p = model.Pair

  let statusLine =
    let text = pairStatusText p.Status

    if text = "" then
      Html.none
    else
      Html.p
        [ prop.className (
            if p.Status = WebRtc.PeerState.Connected then
              "fl-pair-status fl-pair-status-ok"
            elif p.Status = WebRtc.PeerState.Failed then
              "fl-pair-status fl-pair-status-bad"
            else
              "fl-pair-status"
          )
          prop.text text ]

  let errorLine =
    match p.Error with
    | Some e -> Html.p [ prop.className "fl-pair-status fl-pair-status-bad"; prop.text e ]
    | None -> Html.none

  let cancelBtn =
    Html.button
      [ prop.className "fl-btn ghost fl-pair-cancel"
        prop.text (
          if p.Status = WebRtc.PeerState.Connected then
            "◼ End session"
          else
            "Cancel"
        )
        prop.onClick (fun _ -> dispatch CancelPairing) ]

  match p.Role with
  | None ->
    Html.div
      [ prop.className "fl-pair-entry"
        prop.children
          [ Html.div
              [ prop.className "fl-pair-entry-head"
                prop.children
                  [ Html.span
                      [ prop.className "fl-pair-entry-title"
                        prop.text "📱 Live-drive across devices" ]
                    Html.button
                      [ prop.className "fl-present"
                        prop.text "🔗 Pair a device"
                        prop.onClick (fun _ -> dispatch StartPairing) ]
                    Html.button
                      [ prop.className "fl-btn ghost"
                        prop.text "Join a paired session →"
                        prop.onClick (fun _ -> dispatch JoinPairing) ] ] ]
            Html.p
              [ prop.className "fl-livedrive-note"
                prop.text
                  "Drive a second device's UI live over a peer-to-peer WebRTC link – scan a QR to connect, no server, no account. The UI travels as data; a STUN server only helps the two find each other, and your API key never crosses the link." ] ] ]
  | Some PairRole.Presenter ->
    Html.div
      [ prop.className "fl-pair"
        prop.children
          [ Html.h3
              [ prop.className "fl-pair-title"
                prop.text "Pair a device – you are presenting" ]
            pairTokenShare
              "1. On the other device, open this site, tap “Join a paired session”, and scan or paste this code:"
              p.Offer
            pairTokenPaste
              "2. Paste the code the other device shows back, then connect:"
              p.AnswerInput
              "Paste the answer code from the other device…"
              "Connect"
              (SetPairAnswerInput >> dispatch)
              (fun () -> dispatch SubmitPairAnswer)
            statusLine
            errorLine
            cancelBtn ] ]
  | Some PairRole.Joiner ->
    Html.div
      [ prop.className "fl-pair"
        prop.children
          [ Html.h3 [ prop.className "fl-pair-title"; prop.text "Join a paired session" ]
            pairTokenPaste
              "1. Paste the pairing code from the presenting device:"
              p.OfferInput
              "Paste the pairing code…"
              "Generate answer"
              (SetPairOfferInput >> dispatch)
              (fun () -> dispatch SubmitPairOffer)
            (if p.Answer <> "" then
               pairTokenShare "2. Scan or paste this answer code back on the presenting device:" p.Answer
             else
               Html.none)
            statusLine
            errorLine
            cancelBtn ] ]

let private galleryPane (model: Model) (dispatch: Msg -> unit) : ReactElement =
  Html.div
    [ prop.className "fl-gallery"
      prop.children
        [ Html.p
            [ prop.className "fl-gallery-intro"
              prop.text "Load an example – no key needed – then edit it by prompt:" ]
          Html.div
            [ prop.className "fl-gallery-buttons"
              prop.children
                [ for ex in Gallery.examples ->
                    Html.button
                      [ prop.className "fl-example"
                        prop.text ex.Title
                        prop.onClick (fun _ -> dispatch (LoadTree ex.Tree)) ] ] ]
          // The Pattern Bank fast path – deterministic structural lookup (the real
          // Fuaran.Core findBySignature engine), no key, no model. Picking a match
          // loads it into the playground through the same LoadTree path as the
          // gallery, ready to edit by prompt.
          PatternBank.panel (fun tree -> dispatch (LoadTree tree))
          Html.button
            [ prop.className "fl-share"
              prop.disabled model.Session.Tree.IsNone
              prop.text "Copy share link"
              prop.onClick (fun _ -> dispatch Share) ]
          // Serverless live-drive (Phase 295) – drive another window's UI live,
          // no server: the tree + each edit stream over a same-origin channel and
          // re-render there. Stage 1 opens a second window on this machine.
          Html.div
            [ prop.className "fl-livedrive"
              prop.children
                [ Html.button
                    [ prop.className (
                        if model.Presenting then
                          "fl-present fl-present-live"
                        else
                          "fl-present"
                      )
                      prop.disabled model.Session.Tree.IsNone
                      prop.text (
                        if model.Presenting then
                          "◼ Stop presenting"
                        else
                          "▶ Present live to another window"
                      )
                      prop.onClick (fun _ -> dispatch TogglePresent) ]
                  Html.p
                    [ prop.className "fl-livedrive-note"
                      prop.text (
                        if model.Presenting then
                          "Live – a second window is mirroring this UI. Every prompt/edit streams to it as data; no server, and your key stays here in this tab."
                        else
                          "Opens a second window and drives its UI live from here – the UI travels as data over a same-origin channel, not pixels. Your API key never crosses it. To drive another DEVICE, use “Live-drive across devices” near the top."
                      ) ] ] ] ] ]

let private armView (label: string) (arm: Compare.ArmResult) : ReactElement =
  Html.div
    [ prop.className "fl-arm"
      prop.children
        [ Html.h3 [ prop.className "fl-arm-title"; prop.text label ]
          Html.div
            [ prop.className (if arm.Valid then "fl-valid" else "fl-invalid")
              prop.text ((if arm.Valid then "✓ " else "✗ ") + arm.ValidityReason) ]
          Html.pre [ prop.className "fl-arm-text"; prop.text arm.Text ] ] ]

let private comparePane (model: Model) (dispatch: Msg -> unit) : ReactElement =
  Html.div
    [ prop.className "fl-compare"
      prop.children
        [ Html.button
            [ prop.className "fl-compare-run"
              prop.disabled (model.Comparing || not model.KeyPresent)
              prop.text (
                if model.Comparing then
                  "Comparing…"
                else
                  "Compare on the current prompt"
              )
              prop.onClick (fun _ -> dispatch RunCompare) ]
          (match model.Compare with
           | Some r ->
             Html.div
               [ prop.className "fl-compare-results"
                 prop.children
                   [ armView "Fuaran (typed, verified)" r.Fuaran
                     armView "Conventional JS" r.Conventional ] ]
           | None ->
             Html.p
               [ prop.className "fl-compare-intro"
                 prop.text
                   "Runs the current prompt twice – typed Fuaran vs conventional HTML/JS – and checks each emission for validity. The typed arm is verifiably valid or rejected; the freeform arm only plausibly so." ]) ] ]

// ─── source-projection output box (Phase 329) ────────────────────────────────

let private outputPane (model: Model) (dispatch: Msg -> unit) : ReactElement =
  let wire = Session.treeJson model.Session
  let hasTree = model.Session.Tree.IsSome
  let content = Projection.projectTo model.OutputTab wire

  // Dogfood (Phase 294): the projected source is rendered through the `CodeBlock`
  // primitive – a real language tag, line numbers, and a copy button (wired by
  // the app-wide copy enhancer) – instead of an ad-hoc `<pre>`. The playground
  // that shows you Fuaran code is itself built with Fuaran's code primitive.
  let codeBlock: Node<obj> =
    Fuaran.codeBlockSpec
      "fl-output-code-block"
      { Defaults.codeBlock with
          Code = content
          Language = Projection.languageTag model.OutputTab
          LineNumbers = true
          Copyable = true }

  Html.div
    [ prop.className "fl-output"
      prop.children
        [ Html.div
            [ prop.className "fl-output-tabs"
              prop.children
                [ for t, label in Projection.targets ->
                    Html.button
                      [ prop.key label
                        prop.className (
                          if t = model.OutputTab then
                            "fl-output-tab fl-output-tab-active"
                          else
                            "fl-output-tab"
                        )
                        prop.text label
                        prop.onClick (fun _ -> dispatch (SelectOutputTab t)) ] ] ]
          (if not hasTree then
             Html.div
               [ prop.className "fl-empty"
                 prop.text
                   "Build a UI – its JSON plus illustrative builder source in all nine host languages (TypeScript, Python, F#, C#, VB, Go, Kotlin, Rust, Swift) appears here." ]
           else
             Html.div
               [ prop.className "fl-output-body"
                 prop.children [ Render.renderWithSources BindingResolver.empty ignore codeBlock ] ]) ] ]

// ─── dual-host wire-parity pane (Phase 85 surfaced in-app) ────────────────────

let private parityPane (model: Model) (dispatch: Msg -> unit) : ReactElement =
  let input = parityInput model

  let verdict =
    match input with
    | None ->
      Html.div
        [ prop.className "fl-parity-verdict fl-parity-pending"
          prop.text
            "Load an example, generate a UI, or pick a corpus fixture – both hosts render it side by side below." ]
    | Some(wire, expected) ->
      match model.ParityTs, model.ParityFs with
      | Some ts, Some fs ->
        if
          ts.Ok
          && fs.Ok
          && ts.Canonical.IsSome
          && ts.Canonical = fs.Canonical
          && ts.Canonical = Some expected
        then
          // Two distinct green claims: a canonical input round-trips to its own
          // bytes; a §16 shorthand input is NORMALISED – both hosts accept it
          // and re-encode to the same verbose canonical form.
          if wire = expected then
            Html.div
              [ prop.className "fl-parity-verdict fl-parity-ok"
                prop.text "✓ Both hosts agree – byte-identical canonical re-encoding" ]
          else
            Html.div
              [ prop.className "fl-parity-verdict fl-parity-ok"
                prop.text "✓ Both hosts agree – §16 shorthand normalised to identical canonical bytes" ]
        elif not ts.Ok || not fs.Ok then
          Html.div
            [ prop.className "fl-parity-verdict fl-parity-bad"
              prop.text (sprintf "✗ Decode failed on %s" (if not ts.Ok then "the TypeScript host" else "the F# host")) ]
        elif ts.Canonical = fs.Canonical then
          Html.div
            [ prop.className "fl-parity-verdict fl-parity-bad"
              prop.text "✗ Hosts agree with each other but diverge from the expected canonical bytes" ]
        else
          Html.div
            [ prop.className "fl-parity-verdict fl-parity-bad"
              prop.text "✗ Hosts diverge – canonical re-encodings differ" ]
      | _ ->
        Html.div
          [ prop.className "fl-parity-verdict fl-parity-pending"
            prop.text "Rendering in both hosts…" ]

  // Input picker: the session's own tree, or a real wire-format-fixtures corpus
  // entry (canonical nodes + §16 lenient-accept shorthands).
  let picker =
    Html.div
      [ prop.className "fl-parity-picker"
        prop.children
          [ Html.label [ prop.className "fl-parity-picker-label"; prop.text "Input:" ]
            Html.select
              [ prop.className "fl-parity-select"
                prop.value (model.ParityFixture |> Option.map (fun f -> f.id) |> Option.defaultValue "")
                prop.onChange (fun (v: string) -> dispatch (SelectParityFixture v))
                prop.children
                  [ yield Html.option [ prop.value ""; prop.text "Current tree (session)" ]
                    for f in parityFixtures -> Html.option [ prop.key f.id; prop.value f.id; prop.text f.label ] ] ] ] ]

  let wireBlock (title: string) (bytes: string) : ReactElement =
    Html.div
      [ prop.className "fl-parity-wire"
        prop.children
          [ Html.h4 [ prop.className "fl-parity-wire-title"; prop.text title ]
            Html.pre [ prop.className "fl-code fl-parity-wire-bytes"; prop.text bytes ] ] ]

  let fixtureDetail =
    match model.ParityFixture with
    | None -> Html.none
    | Some f ->
      Html.div
        [ prop.className "fl-parity-fixture"
          prop.children
            [ Html.p
                [ prop.className "fl-parity-desc"
                  prop.text (sprintf "%s (corpus fixture %s)" f.description f.id) ]
              (if f.lenient then
                 // The §16 demo's before/after: the shorthand actually posted to
                 // both hosts, and the canonical form both must re-encode it to.
                 Html.div
                   [ prop.className "fl-parity-wire-pair"
                     prop.children
                       [ wireBlock "Shorthand input (posted to both hosts)" f.wire
                         wireBlock "Canonical form (both hosts re-encode to)" f.expected ] ]
               else
                 Html.none) ] ]

  let hostFrame (title: string) (frameId: string) (src: string) : ReactElement =
    Html.div
      [ prop.className "fl-parity-host"
        prop.children
          [ Html.h3 [ prop.className "fl-parity-host-title"; prop.text title ]
            Html.iframe
              [ prop.id frameId
                prop.className "fl-parity-frame"
                prop.src src
                prop.title title ] ] ]

  Html.div
    [ prop.className "fl-parity"
      prop.children
        [ Html.p
            [ prop.className "fl-parity-intro"
              prop.text
                "The same wire JSON, decoded and rendered by two independent conformant hosts – the F# (Fable) Fuaran.UI.Renderer and the @fuaran-ui TypeScript renderer. Each re-encodes what it decoded; byte-identical output proves the wire format is implementation-independent. The corpus inputs are real wire-format-fixtures conformance files, including §16 lenient shorthands both hosts must normalise identically." ]
          picker
          fixtureDetail
          verdict
          Html.button
            [ prop.className "fl-btn ghost fl-parity-recheck"
              prop.disabled input.IsNone
              prop.text "Re-check parity"
              prop.onClick (fun _ -> dispatch RunParity) ]
          Html.div
            [ prop.className "fl-parity-hosts"
              prop.children
                [ hostFrame "F# host – Fuaran.UI.Renderer (Fable)" fsFrameId "fable-host.html"
                  hostFrame "TypeScript host – @fuaran-ui/renderer" tsFrameId "ts-host.html" ] ] ] ]

// ─── view ────────────────────────────────────────────────────────────────────

let private topbar (model: Model) (dispatch: Msg -> unit) : ReactElement =
  // Identical to the showcase top line – same logo (word "fuaran"), same tagline,
  // same theme toggle (the .ds-* classes + their CSS are copied into app.css) –
  // so the two entries of the site read as one product.
  Html.header
    [ prop.className "ds-topbar"
      prop.children
        [ Html.div
            [ prop.className "ds-brand"
              prop.children
                [ Html.img
                    [ prop.className "ds-brand-mark"
                      prop.src "brand/fuaran-mark.svg"
                      prop.alt "Fuaran" ]
                  Html.span [ prop.className "ds-brand-word"; prop.text "fuaran" ] ] ]
          Html.button
            [ prop.className "ds-theme-toggle"
              prop.title (
                if model.Dark then
                  "Switch to light theme"
                else
                  "Switch to dark theme"
              )
              prop.ariaLabel (
                if model.Dark then
                  "Switch to light theme"
                else
                  "Switch to dark theme"
              )
              prop.onClick (fun _ -> dispatch ToggleTheme)
              prop.text (if model.Dark then "☀" else "☾") ]
          // The tagline sits on its own line beneath the logo (wraps via
          // .ds-topbar flex-wrap) – mirrored from the docs-site masthead lockup;
          // a change to either side re-applies to the other.
          Html.p [ prop.className "ds-brand-tagline"; prop.text "The source for generative UI" ] ] ]

/// The BYOK connect row – provider + key + the in-tab-only badge. Lives beneath
/// the intro and above the workspace: pasting a key is a step you take before
/// building, not brand chrome, so it sits with the work rather than in the header.
let private connectRow (model: Model) (dispatch: Msg -> unit) : ReactElement =
  let providerLabel = (Byok.descriptorFor model.ProviderId).Label

  Html.div
    [ prop.className "pg-connect"
      prop.children
        [ Html.span
            [ prop.className "pg-byok-badge"
              prop.title
                "Bring your own key. Your key is held in this tab's memory only and sent solely to your chosen provider – never persisted, never to any other origin. Expand \"Your key & what a run costs\" below for the full posture."
              prop.children
                [ Html.span [ prop.text "🔒" ]
                  Html.span [ prop.className "pg-byok-text"; prop.text "BYOK – key in-tab only" ] ] ]
          // ONE combined model picker (operator decision 2026-07-15): a single
          // frontier model per provider – lower tiers emit measurably worse
          // wire JSON, so the only choice offered is WHOSE key you bring.
          // Selecting an entry selects its provider; the model follows
          // (SelectProvider sets Model = the provider's DefaultModel).
          Html.select
            [ prop.className "fl-provider"
              prop.value model.ProviderId
              prop.title
                "The frontier model your key runs – one per provider; lower tiers aren't offered because emission quality drops."
              prop.onChange (fun (v: string) -> dispatch (SelectProvider v))
              prop.children
                [ for p in Byok.providers ->
                    let modelLabel =
                      p.Models |> List.tryHead |> Option.map _.Label |> Option.defaultValue p.Label

                    Html.option [ prop.value p.Id; prop.text (modelLabel + " · " + p.Label) ] ] ]
          Html.input
            [ prop.className "fl-key"
              prop.type' "password"
              prop.placeholder (providerLabel + " key · in-tab only")
              prop.onChange (fun (v: string) -> dispatch (SetKey v)) ]
          // The frontier-only disclaimer: WHY lower tiers aren't offered. A
          // Fuaran emission is a strict structured-output task, so the model
          // quality difference is visible as refusals, not subtle degradation.
          Html.p
            [ prop.className "pg-connect-note"
              prop.children
                [ Html.strong [ prop.text "Why only frontier models? " ]
                  Html.span
                    [ prop.text
                        "A Fuaran emission must be exactly-valid typed wire JSON – the decoder refuses anything malformed rather than guessing at it. Frontier models get it right in a turn or two; lower tiers spend your tokens in repair loops, so we don't offer them." ] ] ] ] ]

// ─── trust + spend panel (Phase 115) ─────────────────────────────────────────
//
// The key-safety posture (memory-only key, single provider egress, CSP lock) is
// genuine but was invisible; and every prompt runs a multi-call loop on the
// visitor's own key. This panel makes both visible BEFORE the first prompt:
// where the key can and cannot go, and what a run can cost – with the hard
// budget backstop spelled out and the full threat model one click away.

[<Literal>]
let private SecurityDocUrl =
  "https://github.com/fuaran-ui/fuaran-live/blob/main/SECURITY.md"

let private trustPanel (model: Model) : ReactElement =
  let desc = Byok.descriptorFor model.ProviderId
  let budget = Agent.defaultBudget

  let li (text: string) = Html.li [ prop.text text ]

  let section (title: string) (items: string list) : ReactElement list =
    [ Html.h3 [ prop.text title ]
      Html.ul [ prop.children [ for text in items -> li text ] ] ]

  Html.details
    [ prop.className "fl-trust"
      prop.children
        [ Html.summary
            [ prop.className "fl-trust-summary"
              prop.text (
                sprintf
                  "🔒 Your key stays in this tab – memory only, sent solely to %s. Every run shows a live token/cost tally with hard caps."
                  desc.Label
              ) ]
          Html.div
            [ prop.className "fl-trust-body"
              prop.children
                [ yield!
                    section
                      "Where your key can go"
                      [ "Held in this tab's memory only – never written to localStorage, sessionStorage, IndexedDB, a cookie, or any other storage. Reloading or closing the tab forgets it."
                        sprintf
                          "Sent only as the auth header on the direct call to %s at %s – never in a URL, a request body, or a log."
                          desc.Label
                          desc.Origin
                        "The page's Content-Security-Policy allows network connections to the supported provider origins only – even a compromised dependency could not send your key anywhere else."
                        "No server, no account, no analytics: the app is static files, and your prompts and responses travel directly between your browser and the provider." ]
                  yield!
                    section
                      "What a run costs"
                      [ sprintf
                          "Each prompt runs a multi-call emit → observe → repair loop on your key – typically a handful of model calls, each capped at %s output tokens."
                          (fmtTokens AgentMaxTokensPerCall)
                        "The status bar shows a live call / token / approximate-cost tally while it runs; Stop halts the loop immediately, and a stopped run is resumable."
                        sprintf
                          "Hard backstop: the loop halts itself at %d calls, %s tokens, or %d minutes – whichever comes first."
                          budget.MaxIterations
                          (fmtTokens budget.MaxTokens)
                          (budget.MaxWallClockMs / 60_000) ]
                  Html.p
                    [ prop.className "fl-trust-note"
                      prop.text
                        "Cost figures are indicative, computed from published per-token prices for the default models – your provider's own billing is authoritative." ]
                  Html.a
                    [ prop.className "fl-trust-link"
                      prop.href SecurityDocUrl
                      prop.target "_blank"
                      prop.rel "noreferrer"
                      prop.text "Read the full threat model (SECURITY.md)" ] ] ] ] ]

/// The audience view (Phase 295) – a window booted with `?live=audience`. It
/// only renders the preview, live-driven from the presenting window over the
/// same-origin channel; there is no authoring chrome and no key input here.
let private audienceView (model: Model) : ReactElement =
  Html.div
    [ prop.className (
        if model.Dark then
          "fl-shell fl-dark fl-audience"
        else
          "fl-shell fl-audience"
      )
      prop.children
        [ Render.themeStyleElement (Brand.theme model.Dark)
          Html.div
            [ prop.className "fl-audience-banner"
              prop.text
                "🔴 Live audience – this screen is being driven from another window. No server: the UI arrives as data and re-renders live." ]
          paneCard "Live preview" (previewPane model) ] ]

/// A collapsible "tool" panel – progressive disclosure for the secondary
/// features so the core prompt→preview workspace stays front-and-centre.
let private toolDetails (title: string) (openByDefault: bool) (body: ReactElement) : ReactElement =
  Html.details
    [ prop.className "pg-tool"
      if openByDefault then
        prop.custom ("open", true)
      prop.children
        [ Html.summary [ prop.text title ]
          Html.div [ prop.className "pg-tool-body"; prop.children [ body ] ] ] ]

let private view (model: Model) (dispatch: Msg -> unit) : ReactElement =
  if model.IsAudience then
    audienceView model
  else
    let noTree = Option.isNone model.Session.Tree

    Html.div
      [ prop.className (
          if model.Dark then
            "fl-shell fl-dark pg-shell"
          else
            "fl-shell pg-shell"
        )
        prop.children
          [ Render.themeStyleElement (Brand.theme model.Dark)
            topbar model dispatch
            Html.div
              [ prop.className "pg-hero"
                prop.children [ Render.renderWithSources BindingResolver.empty ignore heroIntro ] ]
            connectRow model dispatch
            trustPanel model
            // Cross-device live-drive (Phase 295 Stage 2) – lifted to the top so a
            // joiner arriving fresh (or via a ?live=pair link) finds it at once.
            Html.div [ prop.className "pg-pairing"; prop.children [ pairPanel model dispatch ] ]
            // Primary workspace – compose on the left, live preview on the right;
            // every secondary feature is progressive-disclosure below.
            Html.div
              [ prop.className "pg-workspace"
                prop.children
                  [ Html.div
                      [ prop.className "pg-col"
                        prop.children [ paneCard "Conversation" (conversationPane model dispatch) ] ]
                    Html.div
                      [ prop.className "pg-col"
                        prop.children
                          [ paneCard "Live preview" (previewPane model)
                            toolDetails "Inspector – wire JSON" false (inspectorPane model)
                            toolDetails "Output – source projection" false (outputPane model dispatch) ] ] ] ]
            // Secondary tools – collapsed by default; Examples opens on first run.
            Html.section
              [ prop.className "pg-tools"
                prop.children
                  [ Html.h2 [ prop.className "pg-tools-title"; prop.text "More tools" ]
                    toolDetails "Examples & pattern bank" noTree (galleryPane model dispatch)
                    toolDetails "Compare: Fuaran vs conventional JS" false (comparePane model dispatch)
                    (if dualHostEnabled then
                       toolDetails "Dual-host wire parity" false (parityPane model dispatch)
                     else
                       Html.none) ] ]
            (match model.Error with
             | Some e -> Html.div [ prop.className "fl-error"; prop.text (e.Source + " error: " + e.Message) ]
             | None -> Html.none) ] ]

// ─── boot ──────────────────────────────────────────────────────────────────

Program.mkProgram init update view
|> Program.withReactSynchronous "fuaran-live-fs-root"
|> Program.run
