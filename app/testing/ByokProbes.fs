module Fuaran.Live.Testing.ByokProbes

// ============================================================================
//  TEST-ONLY. The provider adapters' flat surfaces: request bodies, response parses,
//  the registry's origins and models, and the network-egress probe.
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that `Byok.fs` holds only what the playground runs.
// ============================================================================

open Fable.Core
open Fuaran.Live
open Fable.Core.JsInterop
open Fuaran.UI.AiWire
open Fuaran.Live.Ports
open Fuaran.Live.Byok

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

/// The OpenAI/Moonshot usage reading (completion tokens already include
/// reasoning) – the configs carry it as `OutputTokensIncludeReasoning`.
let private openAiUsage (json: JsonValue) : ProviderUsage option = openAiUsageWith true json

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

/// Every provider origin the registry can egress to, flattened for the test
/// boundary. This is the mirror side of `src/byok/origins.ts` (the constants
/// vite.config.ts builds the CSP `connect-src` from): the egress test asserts
/// the two sets are equal, so "the policy and the egress code cannot drift
/// apart" is enforced rather than merely asserted in a comment.
let providerOriginsFlat () : string array =
  providers |> List.map _.Origin |> List.toArray

let private errorKindName (kind: ProviderErrorKind) : string =
  match kind with
  | Config -> "config"
  | Auth -> "auth"
  | RateLimit -> "rate-limit"
  | Network -> "network"
  | ProviderFault -> "provider-fault"

/// Drive ONE complete provider round trip for `providerId` — a fresh memory-only
/// key store holding `key`, the real adapter, the real egress helper, the real
/// `fetch` call — against whatever `fetch` the host has installed, and flatten
/// the outcome for the vitest boundary. `agentic` selects the tool-use path.
///
/// This is the surface `test/networkEgress.test.ts` drives: the test installs an
/// instrumented `fetch` plus fake storage/console/telemetry globals, runs the
/// probe over every provider and every failure branch, and asserts the key
/// reached the auth header and nowhere else. The failure branches matter most —
/// a credential escapes through a message far more plausibly than through a
/// request — so the returned `Message` is asserted as carefully as the request.
let egressProbeFlat
  (providerId: string)
  (key: string)
  (agentic: bool)
  : JS.Promise<
      {| Kind: string
         Message: string
         Text: string |}
     >
  =
  let store = createKeyStore ()
  store.Set key
  let descriptor = descriptorFor providerId
  let getKey () = store.Get()

  let flat
    kind
    message
    text
    : {| Kind: string
         Message: string
         Text: string |}
    =
    {| Kind = kind
       Message = message
       Text = text |}

  async {
    if agentic then
      match descriptor.CreateAgentic with
      | None -> return flat "unsupported" "" ""
      | Some mk ->
        let request: AgentRequest =
          { System = "sys"
            Model = descriptor.DefaultModel
            MaxTokens = 256
            Tools =
              [ { Name = "getNodeState"
                  Description = "read a node"
                  InputSchema = createObj [ "type" ==> "object" ] } ]
            Messages =
              [ { Role = User
                  Content = [ AgentContentBlock.Text "build it" ] } ] }

        match! (mk getKey).SendAgentic request with
        | AgentOutcome.Error e -> return flat (errorKindName e.Kind) e.Message ""
        | AgentOutcome.Ok(blocks, _, _) ->
          let text =
            blocks
            |> List.choose (function
              | AgentContentBlock.Text t -> Some t
              | _ -> None)
            |> String.concat ""

          return flat "ok" "" text
    else
      let request: ProviderRequest =
        { System = "sys"
          Model = descriptor.DefaultModel
          MaxTokens = 256
          Messages = [ { Role = User; Content = "build it" } ] }

      match! (descriptor.Create getKey).Send request with
      | ProviderOutcome.Error e -> return flat (errorKindName e.Kind) e.Message ""
      | ProviderOutcome.Ok(text, _) -> return flat "ok" "" text
  }
  |> Async.StartAsPromise

/// `estimateCostUsd` flattened for the vitest boundary: -1.0 ⇒ unknown model
/// (the readout shows tokens only).
let estimateCostUsdFlat (model: string) (inputTokens: int) (outputTokens: int) : float =
  estimateCostUsd model inputTokens outputTokens |> Option.defaultValue -1.0

/// The default model id per provider, flattened for the test boundary – every
/// one must have an entry in the indicative price table.
let defaultModelIdsFlat () : string array =
  providers |> List.map _.DefaultModel |> List.toArray
