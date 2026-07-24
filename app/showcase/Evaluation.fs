module Fuaran.Showcase.Evaluation

// ============================================================================
//  Evaluation results – a live dashboard over the published evaluation feed.
//
//  How reliably does the Fuaran orchestrator turn natural-language prompts into
//  correct, conformant UI? This page reads that answer LIVE from a published
//  static results feed (./eval/results.json – overridable via the
//  `fuaran-eval-feed` localStorage key), polling it client-side. No server we
//  run, nothing hand-entered: when a new run publishes, the dashboard updates
//  itself. The dashboard body is a Fuaran tree (the site is exhibit zero).
//
//  Honest-status rule (mirrors the conformance panel): when no run has published
//  yet, or the feed can't be read, the page says so plainly – it never
//  fabricates a score. "Read the numbers yourself, from the public source" beats
//  "trust the demo". The feed carries a link to the public source of the numbers.
// ============================================================================

open Fable.Core
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── the published feed shape (see public/eval/README.md) ────────────────────

type Provider =
  { Name: string
    Model: string
    Passed: int
    Total: int
    PassRate: float
    MeanScore: float }

type Category =
  { Name: string
    Passed: int
    Total: int
    PassRate: float }

type Summary =
  { TotalPrompts: int
    Passed: int
    PassRate: float
    ProvidersEvaluated: int
    AdversarialPassRate: float }

type Results =
  { Generated: string
    Commit: string
    SourceUrl: string
    SuiteVersion: string
    Summary: Summary
    Providers: Provider list
    Categories: Category list }

/// The feed state. `Awaiting` is the honest leg – no run has published to the
/// feed yet; `Failed` is the other honest leg – the feed is absent/unparseable.
/// Neither ever shows a number.
[<RequireQualifiedAccess>]
type FeedState =
  | Loading
  | Awaiting of string
  | Published of Results
  | Failed of string

// ─── interop: fetch + tolerant JSON read + polling clock ─────────────────────

[<Emit("fetch($0, {cache:'no-store'}).then(function(r){ if(!r.ok) throw new Error('HTTP '+r.status); return r.text(); }).then($1).catch(function(e){ $2(String(e&&e.message?e.message:e)); })")>]
let private fetchInto (url: string) (onText: string -> unit) (onErr: string -> unit) : unit = jsNative

[<Emit("(function(){ try { return JSON.parse($0); } catch(e){ return null; } })()")>]
let private tryParseJson (s: string) : obj = jsNative

[<Emit("($0 == null ? null : $0[$1])")>]
let private field (o: obj) (k: string) : obj = jsNative

[<Emit("(function(){ var v = ($0==null?null:$0[$1]); return Array.isArray(v)?v:[]; })()")>]
let private fieldArr (o: obj) (k: string) : obj[] = jsNative

[<Emit("(function(){ try { var o = localStorage.getItem('fuaran-eval-feed'); if (o) return o; } catch(e){} return './eval/results.json'; })()")>]
let private feedUrl () : string = jsNative

[<Emit("new Date().toLocaleTimeString()")>]
let private nowTime () : string = jsNative

[<Emit("setInterval($0, $1)")>]
let private setInterval (cb: unit -> unit) (ms: int) : int = jsNative

[<Emit("clearInterval($0)")>]
let private clearInterval (id: int) : unit = jsNative

let private gStr (o: obj) (k: string) : string =
  let v = field o k
  if isNull (box v) then "" else string v

let private gFloat (o: obj) (k: string) : float =
  let v = field o k
  if isNull (box v) then 0.0 else unbox<float> v

let private gInt (o: obj) (k: string) : int = int (gFloat o k)

let private parseProvider (o: obj) : Provider =
  { Name = gStr o "name"
    Model = gStr o "model"
    Passed = gInt o "passed"
    Total = gInt o "total"
    PassRate = gFloat o "passRate"
    MeanScore = gFloat o "meanScore" }

let private parseCategory (o: obj) : Category =
  { Name = gStr o "name"
    Passed = gInt o "passed"
    Total = gInt o "total"
    PassRate = gFloat o "passRate" }

let private parseFeed (raw: string) : FeedState =
  let o = tryParseJson raw

  if isNull (box o) then
    FeedState.Failed "the published feed could not be parsed"
  else
    let status = gStr o "status"

    if status = "" || status = "pending" then
      let m = gStr o "message"

      FeedState.Awaiting(
        if m = "" then
          "The evaluation suite has not published a run to this feed yet."
        else
          m
      )
    else
      let s = field o "summary"

      let summary =
        { TotalPrompts = gInt s "totalPrompts"
          Passed = gInt s "passed"
          PassRate = gFloat s "passRate"
          ProvidersEvaluated = gInt s "providersEvaluated"
          AdversarialPassRate = gFloat s "adversarialPassRate" }

      FeedState.Published
        { Generated = gStr o "generatedAt"
          Commit = gStr o "commit"
          SourceUrl = gStr o "sourceUrl"
          SuiteVersion = gStr o "suiteVersion"
          Summary = summary
          Providers = [ for p in fieldArr o "providers" -> parseProvider p ]
          Categories = [ for c in fieldArr o "categories" -> parseCategory c ] }

// ─── the dashboard body (a Fuaran tree – exhibit zero) ───────────────────────

let private renderNode (n: Node<'msg>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

let private headingNode (id: string) (level: int) (text: string) : Node<unit> =
  Fuaran.heading
    id
    { Level = level
      Text = TextSource.Literal text
      Variant = HeadingVariant.Standard }

let private metricNode
  (id: string)
  (label: string)
  (value: float)
  (fmt: CellFormat)
  (tone: ToneVariant)
  (subtext: string)
  : Node<unit> =
  Fuaran.metric
    id
    { Defaults.metric with
        Label = TextSource.Literal label
        Value = Binding.Static value
        Format = fmt
        Tone = tone
        Subtext =
          (if subtext = "" then
             None
           else
             Some(TextSource.Literal subtext)) }

let private toneFor (rate: float) : ToneVariant =
  if rate >= 0.9 then ToneVariant.Success
  elif rate >= 0.75 then ToneVariant.Default
  else ToneVariant.Warning

let private providerCard (i: int) (p: Provider) : Node<unit> =
  let metric =
    metricNode
      (sprintf "ev-pm-%d" i)
      "Pass rate"
      p.PassRate
      (CellFormat.Percent(Some 0))
      (toneFor p.PassRate)
      (sprintf "%d / %d prompts · mean %.1f / 5" p.Passed p.Total p.MeanScore)

  let children =
    if p.Model = "" then
      [ metric ]
    else
      [ metric; Fuaran.markdown (sprintf "ev-pmodel-%d" i) (sprintf "_%s_" p.Model) ]

  Fuaran.card
    (sprintf "ev-p-%d" i)
    { Defaults.card with
        Heading = Some(TextSource.Literal p.Name)
        Children = children }

let private dashboardTree (r: Results) : Node<unit> =
  let summaryGrid =
    Fuaran.gridLayout
      "ev-summary"
      { Defaults.gridLayout<unit> with
          Cols = 4
          Children =
            [ metricNode
                "ev-s-prompts"
                "Prompts evaluated"
                (float r.Summary.TotalPrompts)
                (CellFormat.Number(Some 0))
                ToneVariant.Brand
                "the fixed canonical set"
              metricNode
                "ev-s-pass"
                "Overall pass rate"
                r.Summary.PassRate
                (CellFormat.Percent(Some 0))
                ToneVariant.Brand
                (sprintf "%d / %d passed" r.Summary.Passed r.Summary.TotalPrompts)
              metricNode
                "ev-s-prov"
                "Providers"
                (float r.Summary.ProvidersEvaluated)
                (CellFormat.Number(Some 0))
                ToneVariant.Default
                "evaluated head-to-head"
              metricNode
                "ev-s-adv"
                "Adversarial pass rate"
                r.Summary.AdversarialPassRate
                (CellFormat.Percent(Some 0))
                (toneFor r.Summary.AdversarialPassRate)
                "held-out adversarial track" ] }

  let providerGrid =
    Fuaran.gridLayout
      "ev-prov"
      { Defaults.gridLayout<unit> with
          Cols = 3
          Children = [ for i, p in List.indexed r.Providers -> providerCard i p ] }

  let categoryGrid =
    Fuaran.gridLayout
      "ev-cats"
      { Defaults.gridLayout<unit> with
          Cols = 3
          Children =
            [ for i, c in List.indexed r.Categories ->
                metricNode
                  (sprintf "ev-c-%d" i)
                  c.Name
                  c.PassRate
                  (CellFormat.Percent(Some 0))
                  (toneFor c.PassRate)
                  (sprintf "%d / %d" c.Passed c.Total) ] }

  let mutable children = [ headingNode "ev-h-sum" 2 "Headline"; summaryGrid ]

  if not r.Providers.IsEmpty then
    children <- children @ [ headingNode "ev-h-prov" 2 "By provider"; providerGrid ]

  if not r.Categories.IsEmpty then
    children <- children @ [ headingNode "ev-h-cat" 2 "By category"; categoryGrid ]

  Fuaran.dashboard
    "ev-dash"
    { Defaults.dashboard with
        Children = children }

let private awaitingNode (message: string) : Node<unit> =
  Fuaran.dashboard
    "ev-awaiting"
    { Defaults.dashboard with
        Children =
          [ Fuaran.callout
              "ev-awaiting-callout"
              { Defaults.callout with
                  Tone = ToneVariant.Subdued
                  Heading = Some(TextSource.Literal "Awaiting the first published run")
                  Body = TextSource.Literal message }
            Fuaran.markdown
              "ev-awaiting-what"
              "When the evaluation suite publishes its first run to this feed, the numbers appear here automatically – an overall pass rate, a head-to-head per-provider breakdown, and per-category scores, each read live from the published results. Until then this page shows no figures rather than placeholder ones: the site never fabricates a signal it hasn't earned." ] }

let private failedNode (reason: string) : Node<unit> =
  Fuaran.callout
    "ev-failed"
    { Defaults.callout with
        Tone = ToneVariant.Subdued
        Heading = Some(TextSource.Literal "Results feed unavailable")
        Body = TextSource.Literal(sprintf "Showing this rather than fabricated numbers: %s." reason) }

// ─── page (Feliz chrome around the Fuaran dashboard) ─────────────────────────

let private shortSha (c: string) : string =
  if c = "" then "unknown" else c.Substring(0, min 7 c.Length)

let private sourceLink (url: string) : ReactElement =
  let href = if url = "" then "https://fuaran-ui.io" else url

  Html.a
    [ prop.className "ev-source"
      prop.href href
      prop.target "_blank"
      prop.rel "noreferrer"
      prop.text "Public source & methodology →" ]

[<ReactComponent>]
let private EvaluationView () : ReactElement =
  let state, setState = React.useState FeedState.Loading
  let lastChecked, setChecked = React.useState ""

  let load () =
    fetchInto
      (feedUrl ())
      (fun text ->
        setState (parseFeed text)
        setChecked (nowTime ()))
      (fun err ->
        setState (FeedState.Failed err)
        setChecked (nowTime ()))

  // Live feed: fetch on mount, then poll every 60s; clear the timer on unmount.
  React.useEffectOnce (fun () ->
    load ()
    let timer = setInterval load 60000

    { new System.IDisposable with
        member _.Dispose() = clearInterval timer })

  let liveStrip =
    Html.div
      [ prop.className "ev-strip"
        prop.children
          [ Html.span
              [ prop.className "ev-live"
                prop.children
                  [ Html.span [ prop.className "ev-live-dot" ]
                    Html.text (
                      if lastChecked = "" then
                        "Live feed · auto-refreshing"
                      else
                        sprintf "Live feed · auto-refreshing · checked %s" lastChecked
                    ) ] ]
            Html.button
              [ prop.className "ev-refresh"
                prop.text "Check now"
                prop.onClick (fun _ -> load ()) ] ] ]

  let provenance, source =
    match state with
    | FeedState.Published r ->
      let bits =
        [ if r.Generated <> "" then
            sprintf "as of %s" r.Generated
          sprintf "commit %s" (shortSha r.Commit)
          if r.SuiteVersion <> "" then
            sprintf "suite v%s" r.SuiteVersion ]

      Html.span [ prop.className "ev-prov"; prop.text (String.concat " · " bits) ], sourceLink r.SourceUrl
    | _ -> Html.none, sourceLink ""

  let body =
    match state with
    | FeedState.Loading -> renderNode (Fuaran.markdown "ev-loading" "_Reading the live results feed…_")
    | FeedState.Awaiting m -> renderNode (awaitingNode m)
    | FeedState.Failed reason -> renderNode (failedNode reason)
    | FeedState.Published r -> renderNode (dashboardTree r)

  Html.div
    [ prop.className "ev-page"
      prop.children
        [ Html.h1 [ prop.className "ev-title"; prop.text "Evaluation results" ]
          Html.p
            [ prop.className "ev-lede"
              prop.text
                "How reliably does the Fuaran orchestrator turn a natural-language prompt into correct, conformant UI? These figures come straight from the evaluation suite – a fixed, public set of prompts scored across providers – and this page reads them live from the published results feed. Nothing here is hand-typed; when a new run publishes, the dashboard updates itself." ]
          liveStrip
          Html.div [ prop.className "ev-meta"; prop.children [ provenance; source ] ]
          body
          Html.div
            [ prop.className "ev-honesty"
              prop.children
                [ Html.h3 [ prop.text "How honest is this?" ]
                  Html.ul
                    [ prop.children
                        [ Html.li
                            [ prop.text
                                "Every number is read live from the published results feed – a static JSON artefact fetched in your browser and polled every 60 seconds. Nothing on this page is hand-entered, and there is no server in the loop." ]
                          Html.li
                            [ prop.text
                                "Until a run publishes, the page shows no figures at all – not placeholders. If the feed can't be read it says so, in grey. It never dresses up a missing or failing result as a pass." ]
                          Html.li
                            [ prop.text
                                "The link above goes to the public source and methodology, so the numbers are reproducible rather than asserted." ]
                          Html.li
                            [ prop.text
                                "The dashboard you're reading is itself a Fuaran tree, drawn by the same renderer as every demo – the metrics, the per-provider cards, all of it is UI-as-data." ] ] ] ] ] ] ]

let page: ReactElement = EvaluationView()
