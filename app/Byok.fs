module Fuaran.Live.Byok

// ============================================================================
//  BYOK providers + effect ports – Phase 327: the portable AI-connector
//  wire layer, vendored in-repo as `FuaranLive.AiWire` (Phase 576).
//
//  The providers no longer hand-roll their request/response JSON via raw
//  `createObj` / dynamic `?` interop. Every request body is built as a
//  `FuaranLive.AiWire` `JsonValue` and serialized by the canonical byte-stable
//  `JsonHost.serialize`; every response is parsed by the host-bridged
//  `JsonHost.parse` and navigated with the total (never-throwing) `JsonValue`
//  accessors – the SAME portable model a .NET server host runs over. The
//  bytes travel through the shared `IHttpTransport` egress seam, implemented
//  here once as a browser `fetch` transport: the key rides ONLY a header to the
//  one provider origin (the CSP `connect-src` allow-list pins them), read
//  per call (never captured), held memory-only (never persisted).
//
//  All providers implement `SendAgentic` (multi-turn tool-use), so the
//  self-debug loop is multi-provider – Claude, GPT, Gemini, Kimi, and Grok
//  alike. The
//  ordered `AgentContentBlock` model (Ports.fs) is fuaran-live's richer
//  projection over the shared flat `AIProviderResponse`; the per-provider
//  block↔wire translation lives here (Anthropic content blocks; OpenAI
//  `tool_calls` + `tool`-role messages; Gemini name-keyed `functionCall` /
//  `functionResponse` parts).
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open FuaranLive.AiWire
open Fuaran.Live.Ports

// ─── memory-only key store (port of byok/keyStore.ts) ────────────────────────

type KeyStore =
  abstract member Get: unit -> string option
  abstract member Set: string -> unit
  abstract member Clear: unit -> unit
  abstract member Has: unit -> bool

/// A memory-only BYOK key holder – never persisted to disk. The heap is gone on
/// tab close; `Clear` makes the "cleared" guarantee explicit.
let createKeyStore () : KeyStore =
  let mutable inMemory: string option = None

  { new KeyStore with
      member _.Get() = inMemory

      member _.Set(k) =
        inMemory <- (if k = "" then None else Some k)

      member _.Clear() = inMemory <- None
      member _.Has() = inMemory.IsSome }

// ─── browser effect ports (port of browserEffects.ts) ────────────────────────

/// The browser implementation of the `EffectPorts` seam. `Download` builds an
/// in-memory object URL – no file touches disk except where the user's own save
/// dialog puts it. Implemented via direct JS interop so the module needs no
/// `Browser.*` binding surface.
let browserEffectPorts: EffectPorts =
  { new EffectPorts with
      member _.WriteToClipboard(text) =
        emitJsStatement text "navigator.clipboard && navigator.clipboard.writeText($0)"

      member _.Download(filename, contents, mime) =
        emitJsStatement
          (contents, mime, filename)
          "const __blob = new Blob([$0], { type: $1 });
                 const __url = URL.createObjectURL(__blob);
                 const __a = document.createElement('a');
                 __a.href = __url; __a.download = $2; __a.click();
                 URL.revokeObjectURL(__url);"

      member _.Warn(message) =
        emitJsStatement message "console.warn('[fuaran-live]', $0)"

      member _.Notify(channel, payload) =
        emitJsStatement (channel, payload) "console.info('[fuaran-live] notify', $0, $1)" }

// ─── provider egress origins + default models ────────────────────────────────
//
// One direct browser `fetch` per provider, no SDK. The user's key rides ONLY a
// header to that provider's single origin (the CSP `connect-src` allow-list pins
// the origins). Mirrored in src/byok/origins.ts, which vite.config.ts
// imports to build the CSP.

[<Literal>]
let ANTHROPIC_ORIGIN = "https://api.anthropic.com"

[<Literal>]
let OPENAI_ORIGIN = "https://api.openai.com"

[<Literal>]
let GEMINI_ORIGIN = "https://generativelanguage.googleapis.com"

[<Literal>]
let KIMI_ORIGIN = "https://api.moonshot.ai"

[<Literal>]
let XAI_ORIGIN = "https://api.x.ai"

// One curated model per provider (operator decision 2026-07-15; reframed
// 2026-07-30): the picker offers exactly the model the evaluation measures as
// the best default for wire emission – increasingly a high-capability mid-tier
// model at a low reasoning setting rather than the frontier flagship – so the
// choice is WHOSE key, not which model.
// Anthropic rides Opus 4.8 until Fable 5 is permanently available (swap this
// literal when it is); OpenAI is GPT-5.6 Sol (GA 2026-07-09); Google is Gemini
// 3.1 Pro (`gemini-3.1-pro-preview`, Google's frontier coding/agentic model).
[<Literal>]
let DEFAULT_CLAUDE_MODEL = "claude-opus-4-8"

[<Literal>]
let DEFAULT_OPENAI_MODEL = "gpt-5.6-sol"

[<Literal>]
let DEFAULT_GEMINI_MODEL = "gemini-3.1-pro-preview"

// Moonshot is Kimi K3 – the flagship (1M context), the eval's fourth-family
// probe (pilot 5 addendum, 2026-07-21): base-40 one-shot 84.2% at low effort.
[<Literal>]
let DEFAULT_KIMI_MODEL = "kimi-k3"

// xAI is Grok 4.5 – the eval's fifth family (smoke 2026-07-28; the 729
// publication cohort ran it at provider-default posture: 12/12 decode-gate
// parse across three repeats, 100% judged-correct on the fuaran arm).
[<Literal>]
let DEFAULT_GROK_MODEL = "grok-4.5"

// ─── measured reasoning postures ─────────────────────────────────────────────
//
// Each provider call carries the reasoning-effort posture the published Fuaran
// evaluation measured as the recommended default for wire-format authoring
// (see the evaluation-results page + methodology on the showcase): the Claude
// and GPT families run LOW effort – one-shot quality holds within a couple of
// points of default at a fraction of the deliberation cost – while Gemini
// deliberately stays at its provider default: reducing its thinking measurably
// harms output quality on this task family, so no thinking config is sent.
// These are defaults, not caps – the emitted trees are identical in kind;
// only hidden deliberation (and therefore cost + latency) changes.

let private anthropicPostureFields: (string * JsonValue) list =
  [ "thinking", jobj [ "type", jstr "adaptive" ]
    "output_config", jobj [ "effort", jstr "low" ] ]

let private openAiPostureFields: (string * JsonValue) list =
  [ "reasoning_effort", jstr "low" ]

// Kimi K3 rides the same OpenAI-compatible `reasoning_effort` dial, and the
// eval's fourth-family probe measured the SAME low-effort posture as the
// recommended default: the dial transfers (a 46× deliberation collapse vs the
// runaway provider default, judge parity both sides), landing base-40 one-shot
// at 84.2% with 189 reasoning tokens/cell on the hard stratum.
let private kimiPostureFields: (string * JsonValue) list =
  [ "reasoning_effort", jstr "low" ]

// Grok deliberately sends NO reasoning-effort field: like Gemini, its measured
// recommended posture is the provider default. The low-effort shakedown
// (2026-07-28) found @low induces repeatable deep-nesting JSON slips on
// emissions with large embedded data payloads (tier-a-005: 1/3 parse at low vs
// 3/3 at default); the 729 cohort ran @default and parsed 12/12.
let private grokPostureFields: (string * JsonValue) list = []

// ─── prompt caching (cost posture) ───────────────────────────────────────────
//
// The system prompt is the full drift-checked prompt pack (~15k tokens), so
// caching it per provider is what keeps a session's per-turn cost near the
// published evaluation's cached steady-state numbers:
// - Anthropic: EXPLICIT – the system block carries a `cache_control`
//   ephemeral breakpoint. The first turn writes the prefix to the provider
//   cache (a one-off surcharge on those tokens); every later turn within the
//   TTL reads it at a fraction of input price. GA API – no beta header.
// - OpenAI: AUTOMATIC for prompts over ~1k tokens; `prompt_cache_key` is sent
//   so the provider's cache routing keeps this app's identical prefix on warm
//   machines across turns.
// - Gemini: IMPLICIT caching is automatic on current models; the EXPLICIT
//   CachedContent API needs a server-side resource lifecycle (create / TTL /
//   storage billing) that does not fit a client-only BYOK page, so it is
//   deliberately not used.
// - Kimi (Moonshot): AUTOMATIC – repeated prefixes bill at the cached-input
//   rate with no request field to set; `prompt_cache_key` is an OpenAI-specific
//   routing hint and is not sent to Moonshot.
// - Grok (xAI): AUTOMATIC/IMPLICIT with no request field – the eval smoke
//   observed the cache warming ASYNCHRONOUSLY (the first turn bills near-full
//   input; turns minutes later hit ~15.7k cached tokens on the shared prefix).
//   `prompt_cache_key` is not sent.

/// The Anthropic `system` field as a content-block array with the ephemeral
/// cache breakpoint (a bare string cannot carry `cache_control`).
let private anthropicSystemJson (system: string) : JsonValue =
  jarr
    [ jobj
        [ "type", jstr "text"
          "text", jstr system
          "cache_control", jobj [ "type", jstr "ephemeral" ] ] ]

let private openAiCacheFields: (string * JsonValue) list =
  [ "prompt_cache_key", jstr "fuaran-live" ]

let private anthropicVersion = "2023-06-01"

// ─── indicative pricing (the Phase 115 cost readout) ─────────────────────────
//
// USD per million tokens for the default models, so the agent-mode status bar
// can show an approximate spend next to the raw token tally. Indicative only –
// the provider's own billing is authoritative – and keyed by exact model id, so
// an unknown model simply shows tokens without a cost figure.

let private modelPricing: (string * float * float) list =
  [ DEFAULT_CLAUDE_MODEL, 15.0, 75.0
    DEFAULT_OPENAI_MODEL, 5.0, 30.0
    DEFAULT_GEMINI_MODEL, 1.50, 9.0
    // Kimi K3 – confirmed 2026-07-16 from the official per-model pricing page
    // (platform.kimi.ai; single tier, 1M context).
    DEFAULT_KIMI_MODEL, 3.0, 15.0
    // Grok 4.5 – docs.x.ai/docs/models, retrieved 2026-07-30 (matches the
    // eval's Pricing.fs entry). Output figure is the billed total: xAI counts
    // reasoning tokens in the bill, and the usage normalisation below folds
    // them into OutputTokens so this rate applies to the right number.
    DEFAULT_GROK_MODEL, 2.0, 6.0 ]

/// Approximate USD cost of a token mix against the indicative price table, or
/// None for a model the table doesn't know (the readout then shows tokens only).
let estimateCostUsd (model: string) (inputTokens: int) (outputTokens: int) : float option =
  modelPricing
  |> List.tryFind (fun (m, _, _) -> m = model)
  |> Option.map (fun (_, inPerM, outPerM) -> (float inputTokens * inPerM + float outputTokens * outPerM) / 1_000_000.0)

// ─── the browser fetch IHttpTransport (the shared portable egress seam) ───────
//
// The one async boundary the wire mapping needs. A per-provider mapper builds a
// `JsonValue` body, serializes it, and hands a portable `HttpRequest` to this
// transport; the transport moves the bytes via `fetch` and hands back a buffered
// `HttpResponse`. The mapper never sees `fetch` – exactly the .NET host's
// `HttpClientTransport` split, re-expressed for the browser. A transport-level
// failure (network / CORS / DNS) rejects the promise, which surfaces to the
// caller's `try/with` as a `Network` error – mirroring the .NET host's throw.

[<Emit("fetch($0, $1)")>]
let private fetchApi (url: string) (init: obj) : JS.Promise<obj> = jsNative

let browserHttpTransport: IHttpTransport =
  { new IHttpTransport with
      member _.Send(request) =
        async {
          let headerObj =
            createObj (
              ("content-type", box "application/json")
              :: [ for k, v in request.Headers -> k, box v ]
            )

          let init = createObj [ "method" ==> request.Method; "headers" ==> headerObj ]

          match request.Body with
          | Some body -> init?body <- body
          | None -> ()

          let! response = fetchApi request.Url init |> Async.AwaitPromise
          let status: int = response?status
          let! bodyText = (response?text (): JS.Promise<string>) |> Async.AwaitPromise

          return
            { StatusCode = status
              Headers = []
              Body = bodyText }
        } }

// ─── JsonValue ↔ JS-object bridges ───────────────────────────────────────────
//
// Tool-call arguments (`AgentContentBlock.ToolUse.input`) and tool/JSON-Schema
// definitions cross the dispatcher boundary as raw JS objects (the dispatcher
// reads `input.nodeId` via interop), so the two bridges convert between the
// portable `JsonValue` model the wire mapping speaks and the JS objects the
// loop holds. `objToJson` round-trips through the host JSON engine (the same
// engine `JsonHost.parse` bridges to) so any obj re-enters the canonical model.

let rec private jsonToObj (v: JsonValue) : obj =
  match v with
  | JNull -> null
  | JBool b -> box b
  | JNumber n -> box n
  | JString s -> box s
  | JArray items -> box (items |> List.map jsonToObj |> List.toArray)
  | JObject members -> createObj [ for k, value in members -> k, jsonToObj value ]

let private objToJson (o: obj) : JsonValue =
  match JsonHost.parse (JS.JSON.stringify o) with
  | Some v -> v
  | None -> JNull

// ─── shared egress + error mapping over the portable transport ───────────────

let private statusToKind (status: int) : ProviderErrorKind =
  if status = 401 || status = 403 then Auth
  elif status = 429 then RateLimit
  else ProviderFault

/// The vendor error message (`.error.message`) from a non-2xx body – Anthropic,
/// OpenAI, and Gemini all nest it there. Falls back to the bare status text.
let private vendorErrorMessage (body: string) : string option =
  JsonHost.parse body
  |> Option.bind (JsonValue.tryField "error")
  |> Option.bind (JsonValue.tryField "message")
  |> Option.bind JsonValue.asString

/// The single egress point: POST a `JsonValue` body to `url` with `headers` (the
/// key rides a header only), returning the parsed response `JsonValue` or a typed
/// `ProviderError`. Every provider call – single-shot and agentic – goes through
/// here, so the key-egress contract + the error classification live in one place.
let private postJson
  (url: string)
  (headers: (string * string) list)
  (body: JsonValue)
  : Async<Result<JsonValue, ProviderError>> =
  async {
    let request = HttpRequest.post url headers (JsonHost.serialize body)

    try
      let! response = browserHttpTransport.Send request

      if HttpResponse.isSuccess response then
        match JsonHost.parse response.Body with
        | Some json -> return Result.Ok json
        | None ->
          return
            Result.Error
              { Kind = ProviderFault
                Message = "The provider returned a non-JSON response."
                Status = Some response.StatusCode }
      else
        let detail =
          vendorErrorMessage response.Body
          |> Option.defaultValue (string response.StatusCode)

        return
          Result.Error
            { Kind = statusToKind response.StatusCode
              Message = detail
              Status = Some response.StatusCode }
    with ex ->
      return
        Result.Error
          { Kind = Network
            Message = ex.Message
            Status = None }
  }

/// Common no-key guard: read the key, else a `Config` error; on a key, run
/// `call`. Shared by the single-shot path; the agentic path inlines the same
/// guard so it can return an `AgentOutcome` error.
let private sendWith (getKey: unit -> string option) (call: string -> Async<ProviderOutcome>) : Async<ProviderOutcome> =
  async {
    match getKey () with
    | None
    | Some "" ->
      return
        ProviderOutcome.Error
          { Kind = Config
            Message = "No provider key set."
            Status = None }
    | Some key -> return! call key
  }

let private noKeyAgentError: AgentOutcome =
  AgentOutcome.Error
    { Kind = Config
      Message = "No provider key set."
      Status = None }

let private roleStr (mapAssistant: string) (r: ProviderRole) : string =
  match r with
  | User -> "user"
  | Assistant -> mapAssistant

/// A tool's JSON-Schema (built as a JS object in Agent.fs) re-entered into the
/// canonical model for byte-stable serialization alongside the rest of the body.
let private toolSchema (t: ToolDefinition) : JsonValue = objToJson t.InputSchema

// ─── shared agentic-block stop-reason vocabulary ─────────────────────────────

let private stopReasonOf (raw: string) : AgentStopReason =
  match raw with
  | "tool_use" -> AgentStopReason.ToolUse
  | "end_turn" -> AgentStopReason.EndTurn
  | "max_tokens" -> AgentStopReason.MaxTokens
  | _ -> AgentStopReason.Other

// ============================================================================
//  Anthropic (Messages API: system + ordered content blocks; x-api-key)
// ============================================================================

let private anthropicHeaders (key: string) =
  [ "x-api-key", key
    "anthropic-version", anthropicVersion
    "anthropic-dangerous-direct-browser-access", "true" ]

/// Pull `usage.{input_tokens,output_tokens}` when present (the loop's token
/// counter needs real counts; absence is tolerated – a turn still returns).
let private anthropicUsage (json: JsonValue) : ProviderUsage option =
  match JsonValue.tryField "usage" json with
  | Some usage ->
    match
      usage |> JsonValue.tryField "input_tokens" |> Option.bind JsonValue.asInt,
      usage |> JsonValue.tryField "output_tokens" |> Option.bind JsonValue.asInt
    with
    | Some i, Some o -> Some { InputTokens = i; OutputTokens = o }
    | _ -> None
  | None -> None

/// Join the response `content` array's text blocks (the single-shot emission).
let private anthropicText (json: JsonValue) : string =
  match json |> JsonValue.tryField "content" |> Option.bind JsonValue.asArray with
  | Some blocks ->
    blocks
    |> List.choose (fun b ->
      match b |> JsonValue.tryField "type" |> Option.bind JsonValue.asString with
      | Some "text" -> b |> JsonValue.tryField "text" |> Option.bind JsonValue.asString
      | _ -> None)
    |> String.concat ""
  | None -> ""

/// Parse the response `content` array into the portable ordered block shape
/// (text + tool_use – what an assistant turn can emit).
let private anthropicBlocks (json: JsonValue) : AgentContentBlock list =
  match json |> JsonValue.tryField "content" |> Option.bind JsonValue.asArray with
  | Some blocks ->
    blocks
    |> List.choose (fun b ->
      match b |> JsonValue.tryField "type" |> Option.bind JsonValue.asString with
      | Some "text" ->
        b
        |> JsonValue.tryField "text"
        |> Option.bind JsonValue.asString
        |> Option.map AgentContentBlock.Text
      | Some "tool_use" ->
        match
          b |> JsonValue.tryField "id" |> Option.bind JsonValue.asString,
          b |> JsonValue.tryField "name" |> Option.bind JsonValue.asString
        with
        | Some id, Some name ->
          let input =
            b
            |> JsonValue.tryField "input"
            |> Option.map jsonToObj
            |> Option.defaultValue (createObj [])

          Some(AgentContentBlock.ToolUse(id, name, input))
        | _ -> None
      | _ -> None)
  | None -> []

let private anthropicStop (json: JsonValue) : AgentStopReason =
  json
  |> JsonValue.tryField "stop_reason"
  |> Option.bind JsonValue.asString
  |> Option.map stopReasonOf
  |> Option.defaultValue AgentStopReason.Other

/// Map one ordered block back to the Anthropic wire shape.
let private anthropicBlockJson (b: AgentContentBlock) : JsonValue =
  match b with
  | AgentContentBlock.Text t -> jobj [ "type", jstr "text"; "text", jstr t ]
  | AgentContentBlock.ToolUse(id, name, input) ->
    jobj
      [ "type", jstr "tool_use"
        "id", jstr id
        "name", jstr name
        "input", objToJson input ]
  | AgentContentBlock.ToolResult(toolUseId, content, isError) ->
    jobj (
      [ "type", jstr "tool_result"
        "tool_use_id", jstr toolUseId
        "content", jstr content ]
      @ (if isError then [ "is_error", jbool true ] else [])
    )

let private anthropicMessageJson (m: AgentMessage) : JsonValue =
  jobj
    [ "role", jstr (roleStr "assistant" m.Role)
      "content", jarr (m.Content |> List.map anthropicBlockJson) ]

let private anthropicAgenticBody (request: AgentRequest) : JsonValue =
  jobj (
    [ "model", jstr request.Model
      "max_tokens", jint request.MaxTokens
      "system", anthropicSystemJson request.System
      "messages", jarr (request.Messages |> List.map anthropicMessageJson) ]
    @ anthropicPostureFields
    @ [ "tools",
        jarr (
          request.Tools
          |> List.map (fun t ->
            jobj
              [ "name", jstr t.Name
                "description", jstr t.Description
                "input_schema", toolSchema t ])
        ) ]
  )

let createAnthropicProvider (getKey: unit -> string option) : IAgenticProvider =
  { new IAgenticProvider with
      member _.Id = "anthropic"
      member _.Label = "Claude (Anthropic)"
      member _.DefaultModel = DEFAULT_CLAUDE_MODEL

      member _.Send(request) =
        sendWith getKey (fun key ->
          async {
            let messages =
              request.Messages
              |> List.map (fun m -> jobj [ "role", jstr (roleStr "assistant" m.Role); "content", jstr m.Content ])

            let body =
              jobj (
                [ "model", jstr request.Model
                  "max_tokens", jint request.MaxTokens
                  "system", anthropicSystemJson request.System
                  "messages", jarr messages ]
                @ anthropicPostureFields
              )

            match! postJson (ANTHROPIC_ORIGIN + "/v1/messages") (anthropicHeaders key) body with
            | Result.Error e -> return ProviderOutcome.Error e
            | Result.Ok json -> return ProviderOutcome.Ok(anthropicText json, anthropicUsage json)
          })

      member _.SendAgentic(request) =
        async {
          match getKey () with
          | None
          | Some "" -> return noKeyAgentError
          | Some key ->
            match!
              postJson (ANTHROPIC_ORIGIN + "/v1/messages") (anthropicHeaders key) (anthropicAgenticBody request)
            with
            | Result.Error e -> return AgentOutcome.Error e
            | Result.Ok json -> return AgentOutcome.Ok(anthropicBlocks json, anthropicStop json, anthropicUsage json)
        } }

// ============================================================================
//  The OpenAI-compatible seam (Chat Completions: system is a leading message;
//  Bearer auth; tool-use via `tools` + assistant `tool_calls` + `tool`-role
//  results). Several vendors serve this wire format at their own origin, so
//  ONE body builder + provider factory is parameterised by a vendor config –
//  OpenAI itself is the first instantiation, Moonshot (Kimi) the second.
//  Adding a vendor = a config + a registry descriptor + an origins.ts entry.
// ============================================================================

/// One OpenAI-compatible vendor.
type private OpenAiCompatibleConfig =
  {
    Id: string
    Label: string
    DefaultModel: string
    /// The single origin this vendor's key ever travels to.
    Origin: string
    /// The output-token cap's wire name: OpenAI's current reasoning models
    /// reject the legacy `max_tokens` ("use 'max_completion_tokens'");
    /// Moonshot documents `max_tokens`.
    MaxTokensField: string
    /// The measured posture + cache fields appended to every request body.
    ExtraFields: (string * JsonValue) list
    /// Whether `usage.completion_tokens` already includes reasoning tokens.
    /// OpenAI and Moonshot: yes. xAI: NO – reasoning tokens ride
    /// `completion_tokens_details.reasoning_tokens` OUTSIDE the completion
    /// count (observed 62 completion vs 359 reasoning on one cell), so the
    /// usage parse folds them back in or the cost readout under-bills.
    OutputTokensIncludeReasoning: bool
  }

let private openAiUsageWith (includeReasoning: bool) (json: JsonValue) : ProviderUsage option =
  match JsonValue.tryField "usage" json with
  | Some usage ->
    match
      usage |> JsonValue.tryField "prompt_tokens" |> Option.bind JsonValue.asInt,
      usage |> JsonValue.tryField "completion_tokens" |> Option.bind JsonValue.asInt
    with
    | Some i, Some o ->
      let reasoningOutside =
        if includeReasoning then
          0
        else
          usage
          |> JsonValue.tryField "completion_tokens_details"
          |> Option.bind (JsonValue.tryField "reasoning_tokens")
          |> Option.bind JsonValue.asInt
          |> Option.defaultValue 0

      Some
        { InputTokens = i
          OutputTokens = o + reasoningOutside }
    | _ -> None
  | None -> None

let private openAiUsage (json: JsonValue) : ProviderUsage option = openAiUsageWith true json

let private openAiFirstMessage (json: JsonValue) : JsonValue option =
  json
  |> JsonValue.tryField "choices"
  |> Option.bind (JsonValue.tryItem 0)
  |> Option.bind (JsonValue.tryField "message")

let private openAiText (json: JsonValue) : string =
  openAiFirstMessage json
  |> Option.bind (JsonValue.tryField "content")
  |> Option.bind JsonValue.asString
  |> Option.defaultValue ""

/// OpenAI's `finish_reason` → the portable stop vocabulary.
let private openAiStop (json: JsonValue) : AgentStopReason =
  match
    json
    |> JsonValue.tryField "choices"
    |> Option.bind (JsonValue.tryItem 0)
    |> Option.bind (JsonValue.tryField "finish_reason")
    |> Option.bind JsonValue.asString
  with
  | Some "tool_calls" -> AgentStopReason.ToolUse
  | Some "length" -> AgentStopReason.MaxTokens
  | _ -> AgentStopReason.EndTurn

/// Parse the assistant message into ordered blocks: a `Text` (when non-empty)
/// followed by one `ToolUse` per `tool_calls[]` entry (arguments are a JSON
/// string – parsed back to a JS object for the dispatcher).
let private openAiBlocks (json: JsonValue) : AgentContentBlock list =
  match openAiFirstMessage json with
  | None -> []
  | Some message ->
    let textBlock =
      match message |> JsonValue.tryField "content" |> Option.bind JsonValue.asString with
      | Some t when t <> "" -> [ AgentContentBlock.Text t ]
      | _ -> []

    let toolBlocks =
      match message |> JsonValue.tryField "tool_calls" |> Option.bind JsonValue.asArray with
      | Some calls ->
        calls
        |> List.choose (fun tc ->
          let id =
            tc
            |> JsonValue.tryField "id"
            |> Option.bind JsonValue.asString
            |> Option.defaultValue ""

          let fn = tc |> JsonValue.tryField "function"

          match fn |> Option.bind (JsonValue.tryField "name") |> Option.bind JsonValue.asString with
          | Some name ->
            let argsJson =
              fn
              |> Option.bind (JsonValue.tryField "arguments")
              |> Option.bind JsonValue.asString
              |> Option.defaultValue "{}"

            let input =
              JsonHost.parse argsJson
              |> Option.map jsonToObj
              |> Option.defaultValue (createObj [])

            Some(AgentContentBlock.ToolUse(id, name, input))
          | None -> None)
      | None -> []

    textBlock @ toolBlocks

/// Map one `AgentMessage` to OpenAI's flatter message model: an assistant turn
/// becomes one `assistant` message carrying its text + `tool_calls`; a user turn
/// carrying tool results becomes one `tool`-role message per result; plain user
/// text becomes a `user` message. Returns a list (one block-rich turn can fan
/// out to several OpenAI messages).
let private openAiMessages (m: AgentMessage) : JsonValue list =
  match m.Role with
  | Assistant ->
    let text =
      m.Content
      |> List.choose (function
        | AgentContentBlock.Text t -> Some t
        | _ -> None)
      |> String.concat "\n"

    let toolCalls =
      m.Content
      |> List.choose (function
        | AgentContentBlock.ToolUse(id, name, input) ->
          Some(
            jobj
              [ "id", jstr id
                "type", jstr "function"
                "function", jobj [ "name", jstr name; "arguments", jstr (JS.JSON.stringify input) ] ]
          )
        | _ -> None)

    let content = if text = "" then jnull else jstr text

    if List.isEmpty toolCalls then
      [ jobj [ "role", jstr "assistant"; "content", content ] ]
    else
      [ jobj [ "role", jstr "assistant"; "content", content; "tool_calls", jarr toolCalls ] ]
  | User ->
    let toolResults =
      m.Content
      |> List.choose (function
        | AgentContentBlock.ToolResult(toolUseId, content, _isError) ->
          Some(jobj [ "role", jstr "tool"; "tool_call_id", jstr toolUseId; "content", jstr content ])
        | _ -> None)

    let text =
      m.Content
      |> List.choose (function
        | AgentContentBlock.Text t -> Some t
        | _ -> None)
      |> String.concat "\n"

    let userText =
      if text = "" then
        []
      else
        [ jobj [ "role", jstr "user"; "content", jstr text ] ]

    toolResults @ userText

let private openAiCompatibleAgenticBody (cfg: OpenAiCompatibleConfig) (request: AgentRequest) : JsonValue =
  let systemMsg = jobj [ "role", jstr "system"; "content", jstr request.System ]
  let turns = request.Messages |> List.collect openAiMessages

  let tools =
    request.Tools
    |> List.map (fun t ->
      jobj
        [ "type", jstr "function"
          "function",
          jobj
            [ "name", jstr t.Name
              "description", jstr t.Description
              "parameters", toolSchema t ] ])

  jobj (
    [ "model", jstr request.Model
      cfg.MaxTokensField, jint request.MaxTokens
      "messages", jarr (systemMsg :: turns)
      "tools", jarr tools
      "tool_choice", jstr "auto" ]
    @ cfg.ExtraFields
  )

let private createOpenAiCompatibleProvider
  (cfg: OpenAiCompatibleConfig)
  (getKey: unit -> string option)
  : IAgenticProvider =
  { new IAgenticProvider with
      member _.Id = cfg.Id
      member _.Label = cfg.Label
      member _.DefaultModel = cfg.DefaultModel

      member _.Send(request) =
        sendWith getKey (fun key ->
          async {
            let systemMsg = jobj [ "role", jstr "system"; "content", jstr request.System ]

            let turns =
              request.Messages
              |> List.map (fun m -> jobj [ "role", jstr (roleStr "assistant" m.Role); "content", jstr m.Content ])

            let body =
              jobj (
                [ "model", jstr request.Model
                  cfg.MaxTokensField, jint request.MaxTokens
                  "messages", jarr (systemMsg :: turns) ]
                @ cfg.ExtraFields
              )

            match! postJson (cfg.Origin + "/v1/chat/completions") [ "authorization", "Bearer " + key ] body with
            | Result.Error e -> return ProviderOutcome.Error e
            | Result.Ok json ->
              return ProviderOutcome.Ok(openAiText json, openAiUsageWith cfg.OutputTokensIncludeReasoning json)
          })

      member _.SendAgentic(request) =
        async {
          match getKey () with
          | None
          | Some "" -> return noKeyAgentError
          | Some key ->
            match!
              postJson
                (cfg.Origin + "/v1/chat/completions")
                [ "authorization", "Bearer " + key ]
                (openAiCompatibleAgenticBody cfg request)
            with
            | Result.Error e -> return AgentOutcome.Error e
            | Result.Ok json ->
              return
                AgentOutcome.Ok(
                  openAiBlocks json,
                  openAiStop json,
                  openAiUsageWith cfg.OutputTokensIncludeReasoning json
                )
        } }

let private openAiConfig: OpenAiCompatibleConfig =
  { Id = "openai"
    Label = "GPT (OpenAI)"
    DefaultModel = DEFAULT_OPENAI_MODEL
    Origin = OPENAI_ORIGIN
    MaxTokensField = "max_completion_tokens"
    ExtraFields = openAiPostureFields @ openAiCacheFields
    OutputTokensIncludeReasoning = true }

/// Moonshot (Kimi) – OpenAI-compatible per its migration guide (endpoint + key
/// convention confirmed 2026-07-16 from platform.kimi.ai docs, via the eval
/// suite's seam). No `prompt_cache_key` (its caching is automatic with no
/// routing hint) and the documented `max_tokens` cap name.
let private kimiConfig: OpenAiCompatibleConfig =
  { Id = "kimi"
    Label = "Kimi (Moonshot)"
    DefaultModel = DEFAULT_KIMI_MODEL
    Origin = KIMI_ORIGIN
    MaxTokensField = "max_tokens"
    ExtraFields = kimiPostureFields
    OutputTokensIncludeReasoning = true }

/// xAI (Grok) – OpenAI-compatible chat completions. No posture fields (the
/// measured recommendation is the provider default – see grokPostureFields);
/// no cache fields (implicit caching, no request knob); and CRUCIALLY
/// `OutputTokensIncludeReasoning = false`: xAI bills reasoning tokens but
/// reports them OUTSIDE `completion_tokens`, so the usage parse folds
/// `completion_tokens_details.reasoning_tokens` back in to keep the cost
/// readout honest (the eval seam's same normalisation).
let private xaiConfig: OpenAiCompatibleConfig =
  { Id = "xai"
    Label = "Grok (xAI)"
    DefaultModel = DEFAULT_GROK_MODEL
    Origin = XAI_ORIGIN
    MaxTokensField = "max_tokens"
    ExtraFields = grokPostureFields
    OutputTokensIncludeReasoning = false }

let createOpenAIProvider: (unit -> string option) -> IAgenticProvider =
  createOpenAiCompatibleProvider openAiConfig

let createGrokProvider: (unit -> string option) -> IAgenticProvider =
  createOpenAiCompatibleProvider xaiConfig

let createKimiProvider: (unit -> string option) -> IAgenticProvider =
  createOpenAiCompatibleProvider kimiConfig

// ============================================================================
//  Gemini (generateContent: system_instruction; roles user|model; tool-use via
//  name-keyed `functionCall` / `functionResponse` parts – no tool-call id, so a
//  call's id IS its function name and a result keys back on that name)
// ============================================================================

let private geminiUsage (json: JsonValue) : ProviderUsage option =
  match JsonValue.tryField "usageMetadata" json with
  | Some usage ->
    match
      usage |> JsonValue.tryField "promptTokenCount" |> Option.bind JsonValue.asInt,
      usage
      |> JsonValue.tryField "candidatesTokenCount"
      |> Option.bind JsonValue.asInt
    with
    | Some i, Some o -> Some { InputTokens = i; OutputTokens = o }
    | _ -> None
  | None -> None

let private geminiParts (json: JsonValue) : JsonValue list =
  json
  |> JsonValue.tryField "candidates"
  |> Option.bind (JsonValue.tryItem 0)
  |> Option.bind (JsonValue.tryField "content")
  |> Option.bind (JsonValue.tryField "parts")
  |> Option.bind JsonValue.asArray
  |> Option.defaultValue []

let private geminiText (json: JsonValue) : string =
  geminiParts json
  |> List.choose (fun p -> p |> JsonValue.tryField "text" |> Option.bind JsonValue.asString)
  |> String.concat ""

/// Parse the candidate parts into ordered blocks: text parts join into one
/// `Text`; each `functionCall` part becomes a `ToolUse` whose id is the function
/// name (Gemini has no call id – the name is the key a `functionResponse` pairs
/// back on).
let private geminiBlocks (json: JsonValue) : AgentContentBlock list =
  let parts = geminiParts json

  let text =
    parts
    |> List.choose (fun p -> p |> JsonValue.tryField "text" |> Option.bind JsonValue.asString)
    |> String.concat ""

  let textBlock = if text = "" then [] else [ AgentContentBlock.Text text ]

  let callBlocks =
    parts
    |> List.choose (fun p ->
      match p |> JsonValue.tryField "functionCall" with
      | Some fc ->
        match fc |> JsonValue.tryField "name" |> Option.bind JsonValue.asString with
        | Some name ->
          let input =
            fc
            |> JsonValue.tryField "args"
            |> Option.map jsonToObj
            |> Option.defaultValue (createObj [])

          Some(AgentContentBlock.ToolUse(name, name, input))
        | None -> None
      | None -> None)

  textBlock @ callBlocks

/// Gemini's stop: any `functionCall` part means tool-use regardless of the enum;
/// else map the `finishReason` enum.
let private geminiStop (json: JsonValue) : AgentStopReason =
  let hasCall =
    geminiParts json
    |> List.exists (fun p -> (p |> JsonValue.tryField "functionCall").IsSome)

  if hasCall then
    AgentStopReason.ToolUse
  else
    match
      json
      |> JsonValue.tryField "candidates"
      |> Option.bind (JsonValue.tryItem 0)
      |> Option.bind (JsonValue.tryField "finishReason")
      |> Option.bind JsonValue.asString
    with
    | Some "MAX_TOKENS" -> AgentStopReason.MaxTokens
    | Some "STOP" -> AgentStopReason.EndTurn
    | _ -> AgentStopReason.Other

/// A tool result's string content as a Gemini `response` object – a parsed JSON
/// object passes through; anything else wraps as `{ result: … }`.
let private geminiResponseObj (content: string) : JsonValue =
  match JsonHost.parse content with
  | Some(JObject _ as o) -> o
  | Some v -> jobj [ "result", v ]
  | None -> jobj [ "result", jstr content ]

/// Map one `AgentMessage` to a Gemini `content`. A turn carrying tool results
/// becomes a `user`-role content of `functionResponse` parts (Gemini has no
/// tool-role channel); a turn carrying tool calls becomes a `model`-role content
/// of optional text + `functionCall` parts; plain text rides its own role.
let private geminiContent (m: AgentMessage) : JsonValue =
  let role = roleStr "model" m.Role

  let toolResults =
    m.Content
    |> List.choose (function
      | AgentContentBlock.ToolResult(toolUseId, content, _isError) ->
        Some(jobj [ "functionResponse", jobj [ "name", jstr toolUseId; "response", geminiResponseObj content ] ])
      | _ -> None)

  if not (List.isEmpty toolResults) then
    jobj [ "role", jstr "user"; "parts", jarr toolResults ]
  else
    let textParts =
      m.Content
      |> List.choose (function
        | AgentContentBlock.Text t -> Some(jobj [ "text", jstr t ])
        | _ -> None)

    let callParts =
      m.Content
      |> List.choose (function
        | AgentContentBlock.ToolUse(_id, name, input) ->
          Some(jobj [ "functionCall", jobj [ "name", jstr name; "args", objToJson input ] ])
        | _ -> None)

    jobj [ "role", jstr role; "parts", jarr (textParts @ callParts) ]

let private geminiUrl (model: string) =
  GEMINI_ORIGIN + "/v1beta/models/" + model + ":generateContent"

/// Gemini's `function_declarations[].parameters` is a restricted OpenAPI
/// schema subset, not full JSON Schema: it rejects `additionalProperties`
/// (Anthropic and OpenAI both accept it) with "Unknown name" at decode time.
/// Strip that key recursively; the constraint it carried is advisory to the
/// model, so dropping it for this provider loses nothing the host enforces.
let rec private geminiSchemaValue (json: JsonValue) : JsonValue =
  match json with
  | JObject fields ->
    fields
    |> List.filter (fun (k, _) -> k <> "additionalProperties")
    |> List.map (fun (k, v) -> k, geminiSchemaValue v)
    |> JObject
  | JArray items -> JArray(items |> List.map geminiSchemaValue)
  | other -> other

// NOTE (measured posture): the Gemini bodies deliberately carry NO thinking
// config – the published evaluation measured reduced thinking as harmful to
// output quality on the Gemini family (its deliberation is content-load-
// bearing), so the provider default is the recommended posture here.
let private geminiAgenticBody (request: AgentRequest) : JsonValue =
  let functionDeclarations =
    request.Tools
    |> List.map (fun t ->
      jobj
        [ "name", jstr t.Name
          "description", jstr t.Description
          "parameters", geminiSchemaValue (toolSchema t) ])

  jobj
    [ "system_instruction", jobj [ "parts", jarr [ jobj [ "text", jstr request.System ] ] ]
      "contents", jarr (request.Messages |> List.map geminiContent)
      "tools", jarr [ jobj [ "functionDeclarations", jarr functionDeclarations ] ]
      "toolConfig", jobj [ "functionCallingConfig", jobj [ "mode", jstr "AUTO" ] ] ]

let createGeminiProvider (getKey: unit -> string option) : IAgenticProvider =
  { new IAgenticProvider with
      member _.Id = "gemini"
      member _.Label = "Gemini (Google)"
      member _.DefaultModel = DEFAULT_GEMINI_MODEL

      member _.Send(request) =
        sendWith getKey (fun key ->
          async {
            let contents =
              request.Messages
              |> List.map (fun m ->
                jobj
                  [ "role", jstr (roleStr "model" m.Role)
                    "parts", jarr [ jobj [ "text", jstr m.Content ] ] ])

            let body =
              jobj
                [ "system_instruction", jobj [ "parts", jarr [ jobj [ "text", jstr request.System ] ] ]
                  "contents", jarr contents ]

            // The model name rides the URL path; the key is on the x-goog-api-key
            // header (never the ?key= query param – a key in a URL is more loggable).
            match! postJson (geminiUrl request.Model) [ "x-goog-api-key", key ] body with
            | Result.Error e -> return ProviderOutcome.Error e
            | Result.Ok json -> return ProviderOutcome.Ok(geminiText json, geminiUsage json)
          })

      member _.SendAgentic(request) =
        async {
          match getKey () with
          | None
          | Some "" -> return noKeyAgentError
          | Some key ->
            match! postJson (geminiUrl request.Model) [ "x-goog-api-key", key ] (geminiAgenticBody request) with
            | Result.Error e -> return AgentOutcome.Error e
            | Result.Ok json -> return AgentOutcome.Ok(geminiBlocks json, geminiStop json, geminiUsage json)
        } }

// ─── provider registry (port of byok/registry.ts) ────────────────────────────

/// One selectable provider: its id, the label the picker shows, its default
/// model, a factory that builds the adapter against a per-provider key reader,
/// and the agentic factory. As of Phase 327 ALL providers are agentic
/// (tool-use via the shared `FuaranLive.AiWire` mappers), so each carries
/// `CreateAgentic = Some _` and can drive the self-debug loop.
/// A selectable model for a provider – the single evaluation-chosen default
/// today; a list so a future picker could offer measured alternates.
type ModelOption = { Id: string; Label: string }

type ProviderDescriptor =
  {
    Id: string
    Label: string
    DefaultModel: string
    /// The models offered in the picker for this provider, in display order.
    /// The selected model rides each agentic request; an unknown id still
    /// works – the cost readout just falls back to tokens only.
    Models: ModelOption list
    /// The single origin this provider's key is ever sent to (the trust panel
    /// shows it; the CSP `connect-src` allow-list enforces it).
    Origin: string
    Create: (unit -> string option) -> IAIProvider
    CreateAgentic: ((unit -> string option) -> IAgenticProvider) option
  }

/// The providers offered in the picker, in display order. Claude is the default;
/// all are agentic. ONE curated model per provider (the operator decision
/// recorded at the DEFAULT_* literals) – the picker is a single combined
/// model-forward choice, so `Models` is a single entry each and the UI never
/// shows a separate tier dropdown.
let providers: ProviderDescriptor list =
  [ { Id = "anthropic"
      Label = "Claude (Anthropic)"
      DefaultModel = DEFAULT_CLAUDE_MODEL
      Models =
        [ { Id = DEFAULT_CLAUDE_MODEL
            Label = "Claude Opus 4.8" } ]
      Origin = ANTHROPIC_ORIGIN
      Create = (fun getKey -> createAnthropicProvider getKey :> IAIProvider)
      CreateAgentic = Some createAnthropicProvider }
    { Id = "openai"
      Label = "GPT (OpenAI)"
      DefaultModel = DEFAULT_OPENAI_MODEL
      Models =
        [ { Id = DEFAULT_OPENAI_MODEL
            Label = "GPT-5.6 Sol" } ]
      Origin = OPENAI_ORIGIN
      Create = (fun getKey -> createOpenAIProvider getKey :> IAIProvider)
      CreateAgentic = Some createOpenAIProvider }
    { Id = "gemini"
      Label = "Gemini (Google)"
      DefaultModel = DEFAULT_GEMINI_MODEL
      Models =
        [ { Id = DEFAULT_GEMINI_MODEL
            Label = "Gemini 3.1 Pro" } ]
      Origin = GEMINI_ORIGIN
      Create = (fun getKey -> createGeminiProvider getKey :> IAIProvider)
      CreateAgentic = Some createGeminiProvider }
    { Id = "kimi"
      Label = "Kimi (Moonshot)"
      DefaultModel = DEFAULT_KIMI_MODEL
      Models =
        [ { Id = DEFAULT_KIMI_MODEL
            Label = "Kimi K3" } ]
      Origin = KIMI_ORIGIN
      Create = (fun getKey -> createKimiProvider getKey :> IAIProvider)
      CreateAgentic = Some createKimiProvider }
    { Id = "xai"
      Label = "Grok (xAI)"
      DefaultModel = DEFAULT_GROK_MODEL
      Models =
        [ { Id = DEFAULT_GROK_MODEL
            Label = "Grok 4.5" } ]
      Origin = XAI_ORIGIN
      Create = (fun getKey -> createGrokProvider getKey :> IAIProvider)
      CreateAgentic = Some createGrokProvider } ]

[<Literal>]
let defaultProviderId = "anthropic"

/// Look up a descriptor by id, falling back to the default.
let descriptorFor (id: string) : ProviderDescriptor =
  providers
  |> List.tryFind (fun p -> p.Id = id)
  |> Option.defaultValue (List.head providers)

// ─── flat test surface (Fable smoke per provider – Phase 327) ────────────────
//
// The per-provider agentic request build + response parse are pure (they speak
// the shared `JsonValue` model, not `fetch`), so they're testable headlessly
// over the Fable output without a live LLM. These project the body builders +
// the response parsers to flat values (a serialized string / an anonymous
// record) assertable from vitest across the Fable boundary, exercising all
// three providers' block↔wire translation.

/// Build a representative agentic request body for `providerId` and return its
/// serialized JSON – a tool definition + a turn with text + a `tool_use` + a
/// following `tool_result`, so the request path for every block kind is covered.
let agenticRequestBodyFlat (providerId: string) : string =
  let request: AgentRequest =
    { System = "sys"
      Model = "m"
      MaxTokens = 1024
      Tools =
        [ { Name = "getNodeState"
            Description = "read a node"
            InputSchema =
              // `additionalProperties` is deliberately present: Anthropic and
              // the OpenAI-compatible vendors pass it through; Gemini's body
              // builder must STRIP it (its schema subset rejects the key) –
              // both behaviours are locked by the vitest suite.
              createObj
                [ "type" ==> "object"
                  "properties" ==> createObj [ "nodeId" ==> createObj [ "type" ==> "string" ] ]
                  "additionalProperties" ==> false ] } ]
      Messages =
        [ { Role = User
            Content = [ AgentContentBlock.Text "build it" ] }
          { Role = Assistant
            Content =
              [ AgentContentBlock.Text "inspecting"
                AgentContentBlock.ToolUse("tu-1", "getNodeState", createObj [ "nodeId" ==> "n1" ]) ] }
          { Role = User
            Content = [ AgentContentBlock.ToolResult("tu-1", "{\"found\":true}", false) ] } ] }

  match providerId with
  | "openai" -> JsonHost.serialize (openAiCompatibleAgenticBody openAiConfig request)
  | "kimi" -> JsonHost.serialize (openAiCompatibleAgenticBody kimiConfig request)
  | "xai" -> JsonHost.serialize (openAiCompatibleAgenticBody xaiConfig request)
  | "gemini" -> JsonHost.serialize (geminiAgenticBody request)
  | _ -> JsonHost.serialize (anthropicAgenticBody request)

/// Parse a canned agentic response for `providerId` to a flat shape: the count
/// of text + tool-use blocks, the first tool's name + a stable arg, the stop
/// reason (string), and the token totals.
let parseAgenticResponseFlat
  (providerId: string)
  (responseJson: string)
  : {| Blocks: int
       ToolUses: int
       FirstToolName: string
       StopReason: string
       InTokens: int
       OutTokens: int |}
  =
  let json = JsonHost.parse responseJson |> Option.defaultValue JNull

  let blocks, stop, usage =
    match providerId with
    | "openai"
    | "kimi" -> openAiBlocks json, openAiStop json, openAiUsage json
    | "xai" -> openAiBlocks json, openAiStop json, openAiUsageWith false json
    | "gemini" -> geminiBlocks json, geminiStop json, geminiUsage json
    | _ -> anthropicBlocks json, anthropicStop json, anthropicUsage json

  let toolUses =
    blocks
    |> List.choose (function
      | AgentContentBlock.ToolUse(_, name, _) -> Some name
      | _ -> None)

  let stopStr =
    match stop with
    | AgentStopReason.ToolUse -> "tool_use"
    | AgentStopReason.EndTurn -> "end_turn"
    | AgentStopReason.MaxTokens -> "max_tokens"
    | AgentStopReason.Other -> "other"

  {| Blocks = List.length blocks
     ToolUses = List.length toolUses
     FirstToolName = (toolUses |> List.tryHead |> Option.defaultValue "")
     StopReason = stopStr
     InTokens = (usage |> Option.map (fun u -> u.InputTokens) |> Option.defaultValue 0)
     OutTokens = (usage |> Option.map (fun u -> u.OutputTokens) |> Option.defaultValue 0) |}

/// `estimateCostUsd` flattened for the vitest boundary: -1.0 ⇒ unknown model
/// (the readout shows tokens only).
let estimateCostUsdFlat (model: string) (inputTokens: int) (outputTokens: int) : float =
  estimateCostUsd model inputTokens outputTokens |> Option.defaultValue -1.0

/// The default model id per provider, flattened for the test boundary – every
/// one must have an entry in the indicative price table.
let defaultModelIdsFlat () : string array =
  providers |> List.map _.DefaultModel |> List.toArray
