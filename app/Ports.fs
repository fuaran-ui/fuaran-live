module Fuaran.Live.Ports

// ============================================================================
//  Portability seams (F# port of the former src/ports/ports.ts) – Stage 2.
//
//  Everything in fuaran-live that touches the outside world – the LLM provider
//  call, the clipboard, a file download, a warning – is declared here as an
//  interface and injected, never reached for directly inside the Elmish loop or
//  the panes. The §4l down-shift discipline, now in F#: the browser `fetch`
//  implementations live in `Byok.fs`, but the same loop could be driven by a
//  server host implementing these same seams with no re-authoring.
// ============================================================================

/// Why a provider call failed. Mirrors the TS `ProviderErrorKind`.
type ProviderErrorKind =
  | Config
  | Auth
  | RateLimit
  | Network
  | ProviderFault

type ProviderError =
  { Kind: ProviderErrorKind
    Message: string
    Status: int option }

/// Real provider-reported token usage for one call (optional – a provider that
/// omits usage still returns a valid outcome).
type ProviderUsage = { InputTokens: int; OutputTokens: int }

type ProviderRole =
  | User
  | Assistant

type ProviderMessage = { Role: ProviderRole; Content: string }

type ProviderRequest =
  {
    /// The wire-format system prompt.
    System: string
    /// Conversation so far, oldest first; the latest user message carries the
    /// injected current-tree JSON (the closed loop).
    Messages: ProviderMessage list
    Model: string
    MaxTokens: int
  }

[<RequireQualifiedAccess>]
type ProviderOutcome =
  | Ok of text: string * usage: ProviderUsage option
  | Error of ProviderError

/// One LLM provider. The browser implementation (`Byok.createAnthropicProvider`)
/// calls the provider directly with the user's key; the turn loop only ever sees
/// this interface. Async-at-the-boundary so Elmish drives it with `Cmd.OfAsync`.
type IAIProvider =
  abstract member Id: string
  abstract member Label: string
  abstract member DefaultModel: string
  abstract member Send: ProviderRequest -> Async<ProviderOutcome>

/// Side-effecting host capabilities the shell + panes dispatch through. The
/// browser implementation is `Byok.browserEffectPorts`; a different host swaps
/// its own.
type EffectPorts =
  abstract member WriteToClipboard: string -> unit
  abstract member Download: filename: string * contents: string * mime: string -> unit
  abstract member Warn: string -> unit
  abstract member Notify: channel: string * payload: string -> unit

// ─── The serverless live-drive transport seam (Phase 295) ────────────────────
//
// The "live-drive" demo streams a creation from a *presenting* window/device to
// an *audience* one so the audience re-renders live, with NO server: prompt on A,
// the tree + each TreeOp flow peer-to-peer to B. The transport is abstracted so
// the same present/audience loop runs over any channel – Stage 1 is same-origin
// `BroadcastChannel` (zero new egress, the proving ground); Stage 2 is WebRTC P2P
// (deferred). Mirrors the `IAIProvider` / `EffectPorts` portability discipline.
//
// TRUST INVARIANT (load-bearing): the ONLY things a `LiveDriveMessage` can carry
// are canonical wire JSON – a full tree or a TreeOp, the exact same data a
// shareable permalink already puts in the URL. The BYOK key lives only in the
// key store in the prompting tab and can never reach this seam by construction:
// the DU has no case for it.

/// A message on the live-drive channel: a full tree snapshot, or one TreeOp to
/// apply surgically. Both are canonical wire JSON strings – never a key, never a
/// prompt, never anything but the UI-as-data.
[<RequireQualifiedAccess>]
type LiveDriveMessage =
  | FullTree of wireJson: string
  | Op of opJson: string

/// One live-drive transport. The presenting side `Send`s; the audience side
/// receives via the callback wired at construction. `BroadcastChannel` and (later)
/// WebRTC are two implementations of this one seam.
type ILiveDriveChannel =
  abstract member Send: LiveDriveMessage -> unit
  abstract member Close: unit -> unit

// ─── The opt-in corpus-sink seam (Phase 86) ──────────────────────────────────
//
// The playground can OFFER to contribute one session — the ops that built the
// tree, the tree itself, and which provider/model produced it — to a collection
// endpoint an operator configures. The public build configures none, so the
// offer is not reachable there at all.
//
// TRUST INVARIANT (load-bearing, and the same construction `LiveDriveMessage`
// above uses): the ONLY thing a `ContributionBundle` can carry is a document the
// builder has already produced and verified. The BYOK key cannot reach this
// seam, because the type has no case that could hold one and the module that
// builds the bundle (`Contribute.fs`) never opens a key store. SECURITY.md's
// key-handling guarantee 5 requires exactly this — "key-blind by construction"
// — of any corpus sink, and this is that requirement expressed as a type rather
// than as a promise.

/// A verified contribution payload: the canonical bytes of one
/// `session.fuaran.json`, after redaction and after the residue check passed.
/// Single-case on purpose — it is a *permission slip*, not a container. Nothing
/// but `Contribute.prepare` constructs one, so a value of this type is evidence
/// that the guard ran.
[<RequireQualifiedAccess>]
type ContributionBundle = Verified of json: string

/// Why a contribution did not land, or that it did. `Refused` is the guard's
/// verdict on the payload (never the network's); `Failed` is the transport.
[<RequireQualifiedAccess>]
type ContributionOutcome =
  | Sent
  | Refused of reason: string
  | Failed of reason: string

/// One collection endpoint. The browser implementation lives in `Contribute.fs`
/// — deliberately NOT in `Byok.fs`, which is the one module holding the key
/// stores: the POST code and the key are never in scope together, and a
/// source-level test asserts it.
type IContributionSink =
  /// The configured endpoint, for display. Empty when unconfigured — in which
  /// case nothing offers a contribution and `Post` is never reached.
  abstract member Endpoint: string
  abstract member Post: ContributionBundle -> Async<ContributionOutcome>

// ─── The agentic (tool-use) seam (Phase 327 – over `FuaranLive.AiWire`) ─────
//
// Agent mode (the self-debug loop) needs a richer call than `Send`: the model
// must be able to *call introspection tools* and receive their results over
// multiple turns. This is a strict superset of `Send` – the same provider, the
// same key, the same single origin – so it lives behind `IAgenticProvider`, an
// extension of `IAIProvider`. As of Phase 327 ALL THREE providers implement it.
//
// These types are fuaran-live's **ordered** projection over the shared portable
// connector contract in `FuaranLive.AiWire`. The shared
// `AIProviderResponse` is deliberately flat (`Content: string` + a separate
// `ToolCalls` list), which cannot represent the interleaving of model text and
// `tool_use` blocks a multi-turn agent turn carries; `AgentContentBlock list`
// preserves that order. The per-provider block↔wire translation (Anthropic
// content blocks; OpenAI `tool_calls`; Gemini name-keyed `functionCall`) lives
// in `Byok.fs` and runs over the shared `JsonValue` / `JsonHost` model + the
// shared `IHttpTransport` egress – so the wire substrate is the same source of
// truth the forge server host uses, even though the loop keeps the richer shape.
// (Collapsing onto the shared contract once it grows an ordered-block form is a
// deferred follow-on – see TIDY-UP.)

/// A tool the model may call. `InputSchema` is a JSON Schema object (a raw JS
/// value) passed verbatim to the provider.
type ToolDefinition =
  { Name: string
    Description: string
    InputSchema: obj }

/// One block of structured message content. Assistant turns may carry `Text` and
/// `ToolUse`; the loop replies with `ToolResult` blocks in a user turn. `Input`
/// (tool args) and tool results cross the boundary as raw JSON values (`obj`).
[<RequireQualifiedAccess>]
type AgentContentBlock =
  | Text of text: string
  | ToolUse of id: string * name: string * input: obj
  | ToolResult of toolUseId: string * content: string * isError: bool

type AgentMessage =
  { Role: ProviderRole
    Content: AgentContentBlock list }

type AgentRequest =
  { System: string
    Messages: AgentMessage list
    Model: string
    MaxTokens: int
    Tools: ToolDefinition list }

/// Why the model stopped this turn. `ToolUse` means it wants tool results back;
/// `EndTurn` means it is done; `MaxTokens` means the turn was truncated.
[<RequireQualifiedAccess>]
type AgentStopReason =
  | ToolUse
  | EndTurn
  | MaxTokens
  | Other

[<RequireQualifiedAccess>]
type AgentOutcome =
  | Ok of content: AgentContentBlock list * stopReason: AgentStopReason * usage: ProviderUsage option
  | Error of ProviderError

/// A provider that additionally supports the multi-turn tool-use loop. The
/// Anthropic BYOK provider implements it; the agent loop depends only on this
/// interface, so a server host could drive the identical loop.
type IAgenticProvider =
  inherit IAIProvider
  abstract member SendAgentic: AgentRequest -> Async<AgentOutcome>
