module Fuaran.Showcase.EvalCard

// ============================================================================
//  The live evaluation-results card – honest states, canonical numbers at
//  fuaran-ui.io/evaluation.
//
//  The evaluation feed (public/eval/results.json) is published by the
//  measurement harness; this card renders ONLY what the feed reports, in the
//  Conformance panel's honest-status idiom: grey while pending or unavailable,
//  never a fabricated figure. The full, citable results – provider
//  head-to-head, compactness, provenance – live on the documentation site;
//  this card is the compact live pointer, not the record.
// ============================================================================

open Fable.Core
open Elmish
open Fuaran.UI
open Fuaran.UI.Types

/// The published feed's headline slice (the full schema is documented beside
/// the feed file; the card reads only the summary + provenance it shows).
type Headline =
  { GeneratedAt: string
    PassRate: float
    PassRateCi95: float
    TotalPrompts: int
    ProvidersEvaluated: int }

[<RequireQualifiedAccess>]
type CardState =
  | Loading
  | Awaiting
  | Ready of Headline
  | Stale of string

let feedUrl = "./eval/results.json"

[<Literal>]
let ResultsUrl = "https://fuaran-ui.io/evaluation"

[<Emit("fetch($0).then(function(r){ if (!r.ok) throw new Error('HTTP ' + r.status); return r.text(); }).then($1).catch(function(e){ $2(String(e && e.message ? e.message : e)); })")>]
let private fetchInto (url: string) (onText: string -> unit) (onErr: string -> unit) : unit = jsNative

[<Emit("(function(){ try { return JSON.parse($0); } catch (e) { return null; } })()")>]
let private tryParseJson (s: string) : obj = jsNative

[<Emit("($0 == null ? null : $0[$1])")>]
let private field (o: obj) (k: string) : obj = jsNative

let private parseFeed (raw: string) : CardState =
  let o = tryParseJson raw

  if isNull (box o) then
    CardState.Stale "the published feed could not be parsed"
  else
    let status =
      let v = field o "status"
      if isNull (box v) then "" else string v

    if status <> "published" then
      CardState.Awaiting
    else
      let summary = field o "summary"

      if isNull (box summary) then
        CardState.Stale "the published feed carries no summary"
      else
        let num (k: string) : float =
          let v = field summary k
          if isNull (box v) then 0.0 else unbox<float> v

        let gen =
          let v = field o "generatedAt"
          if isNull (box v) then "" else string v

        CardState.Ready
          { GeneratedAt = gen
            PassRate = num "passRate"
            PassRateCi95 = num "passRateCi95"
            TotalPrompts = int (num "totalPrompts")
            ProvidersEvaluated = int (num "providersEvaluated") }

/// Load the feed as an Elmish command. A missing feed is the honest awaiting
/// state, not an error.
let loadCmd (onResult: CardState -> 'Msg) : Cmd<'Msg> =
  Cmd.ofEffect (fun dispatch ->
    fetchInto feedUrl (parseFeed >> onResult >> dispatch) (fun _ -> dispatch (onResult CardState.Awaiting)))

/// The compact card: an honest-status callout plus the canonical-results link.
/// Green only when a genuine published run is being quoted.
let panel (state: CardState) : Node<'Msg> =
  let tone, heading, body =
    match state with
    | CardState.Loading -> ToneVariant.Subdued, "Evaluation – checking…", "Loading the published results feed."
    | CardState.Awaiting ->
      ToneVariant.Subdued,
      "Evaluation – awaiting the first published run",
      "The feed carries the honest pending seed; numbers appear here when a full evaluation cohort publishes. Nothing is ever hand-typed."
    | CardState.Stale reason ->
      ToneVariant.Subdued,
      "Evaluation – status unavailable",
      sprintf "Showing grey rather than a fake figure: %s." reason
    | CardState.Ready h ->
      ToneVariant.Success,
      "Evaluation – published",
      sprintf
        "Pass rate %.1f%% ±%.1f pp (95%% CI) over %d judged runs · %d providers · %s."
        (h.PassRate * 100.0)
        (h.PassRateCi95 * 100.0)
        h.TotalPrompts
        h.ProvidersEvaluated
        h.GeneratedAt

  Fuaran.card
    "ds-eval-card"
    { Defaults.card with
        Children =
          [ Fuaran.callout
              "ds-eval-status"
              { Defaults.callout with
                  Tone = tone
                  Heading = Some(TextSource.Literal heading)
                  Body = TextSource.Literal body }
            Fuaran.markdown "ds-eval-link" (sprintf "[Full results, methodology, and provenance ↗](%s)" ResultsUrl) ] }
