module Fuaran.Live.Compare

// ============================================================================
//  Comparison mode (F# port of src/compare) – Fuaran vs conventional JS.
//
//  For one prompt, run the SAME provider twice, swapping only the system prompt:
//  once with the Fuaran wire-format prompt (the app's path), once with a
//  conventional "emit raw HTML/JS" baseline. Both arms are single-turn (no tree
//  injection) so they are apples-to-apples. The Fuaran arm's emission is checked
//  by the app's OWN decode/apply loop (it either decodes into a typed tree or it
//  doesn't); the conventional arm gets a loose "looks like markup" check – the
//  point being that the typed arm is verifiably valid or rejected, the freeform
//  arm only plausibly so. Effects go only through the injected `IAIProvider`.
// ============================================================================

open Fuaran.Live.Ports

[<Literal>]
let private MaxTokens = 8192

/// The conventional-baseline system prompt: emit raw front-end code, no typed
/// contract. The contrast the comparison makes visible.
let conventionalPrompt =
  "You are a front-end engineer. Given a request, emit a single self-contained HTML document (inline CSS + JS) that implements the requested UI. Reply with exactly one fenced ```html code block and nothing else."

type ArmResult =
  { Ok: bool
    Text: string
    Valid: bool
    ValidityReason: string }

type CompareResult =
  { Prompt: string
    Fuaran: ArmResult
    Conventional: ArmResult }

/// Is `text` a valid Fuaran emission? Reuses the app's own loop – it decodes +
/// applies into a typed tree, or it is a typed, named failure. (Exposed flat for
/// the comparison test.)
let fuaranValidates (text: string) : bool =
  match Session.ingest Session.empty text with
  | Session.Ingested _ -> true
  | Session.IngestFailed _ -> false

let private fuaranValidity (text: string) : bool * string =
  match Session.ingest Session.empty text with
  | Session.Ingested(mode, _) -> true, "decoded + applied as a " + mode
  | Session.IngestFailed e -> false, "rejected: " + e.Kind

/// A loose "looks like front-end markup/code" check for the conventional arm –
/// deliberately permissive (the contrast is that the typed arm is *verifiably*
/// valid, the freeform arm only superficially so).
let private conventionalValidity (text: string) : bool * string =
  let looksLikeMarkup =
    text.Contains("<div")
    || text.Contains("<html")
    || text.Contains("</")
    || text.Contains("function")
    || text.Contains("<!DOCTYPE")

  if looksLikeMarkup then
    true, "looks like HTML/JS (not type-checked)"
  else
    false, "no recognisable markup"

let private runArm
  (provider: IAIProvider)
  (model: string)
  (system: string)
  (prompt: string)
  (validate: string -> bool * string)
  : Async<ArmResult> =
  async {
    let request =
      { System = system
        Messages = [ { Role = User; Content = prompt } ]
        Model = model
        MaxTokens = MaxTokens }

    match! provider.Send request with
    | ProviderOutcome.Error e ->
      return
        { Ok = false
          Text = ""
          Valid = false
          ValidityReason = e.Message }
    | ProviderOutcome.Ok(text, _) ->
      let valid, reason = validate text

      return
        { Ok = true
          Text = text
          Valid = valid
          ValidityReason = reason }
  }

/// Run both arms for `prompt` through `provider`.
let compareEmissions (provider: IAIProvider) (model: string) (prompt: string) : Async<CompareResult> =
  async {
    let! fuaran = runArm provider model SystemPrompt.value prompt fuaranValidity
    let! conventional = runArm provider model conventionalPrompt prompt conventionalValidity

    return
      { Prompt = prompt
        Fuaran = fuaran
        Conventional = conventional }
  }
