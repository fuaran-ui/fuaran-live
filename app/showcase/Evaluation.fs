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
  {
    Name: string
    Model: string
    /// Two-tier framing (feeds since 2026-07-30): "headline" = the provider's
    /// best-performing arm, "budget" = its cost-optimal posture. "" on a
    /// pre-tier feed — the card then renders untagged, never wrongly tagged.
    Tier: string
    /// The arm the publisher names as this provider's cost recommendation.
    /// `false` on a pre-tier feed, which is also the honest reading — an
    /// unnamed arm is not a pick.
    BudgetPick: bool
    Passed: int
    Total: int
    PassRate: float
    /// Absent on a feed whose scoring carries no 0–5 rubric mean — the pass-rate
    /// subtext then simply omits the clause, rather than reporting a mean of 0.
    MeanScore: float option
    /// Expected spend per correct artifact, on each published posture: a cold
    /// first call, and a turn inside a warm cached session. `None` when the
    /// feed does not carry the key — every pre-economics feed — and the tile
    /// then renders "—" rather than a fabricated zero.
    CostPerCorrectUsd: float option
    CostPerCorrectCachedUsd: float option
    /// Reasoning tokens over billed output. `ReasoningSplitSource` names how
    /// the split was obtained and rides WITH the number, never separately: a
    /// derived split is an estimate and has to say so where it is read.
    ReasoningShare: float option
    ReasoningSplitSource: string
  }

type Category =
  { Name: string
    Passed: int
    Total: int
    PassRate: float }

/// Every optional field here is optional on the wire too. A feed that measured
/// no adversarial track, no economics, or no reasoning split omits the key, and
/// the tile reads "—" — a missing measurement is never rendered as a zero.
type Summary =
  { TotalPrompts: int
    Passed: int
    PassRate: float
    ProvidersEvaluated: int
    AdversarialPassRate: float option
    CostPerCorrectUsd: float option
    CostPerCorrectCachedUsd: float option
    ReasoningShare: float option }

type SessionArm =
  { Condition: string
    ModelPin: string
    Cells: int
    UsdAtTurn3: float
    CellsAtTurn3: int
    UsdAtTurn5: float
    CellsAtTurn5: int
    IdentityMean: float
    IdentityCells: int }

/// Tier-B session-economics companion (README `sessionEconomics`) — Phase 806:
/// published since 2026-08-09, rendered at last. Denominators ride beside
/// every figure; a missing USD reads 0.0 and renders as unpriced.
type SessionEconomics =
  { Cohort: string
    Arms: SessionArm list
    Note: string }

type RecoveryArm =
  { Label: string
    Fired: int
    Recovered: int }

/// Tier-C repair-under-error companion (README `repairRecovery`). The note is
/// carried on the wire and rendered WITH the numbers — they are unciteable
/// without it (the rates are not a cross-condition ranking).
type RepairRecovery =
  { Cohort: string
    Conditions: RecoveryArm list
    Families: RecoveryArm list
    Note: string }

type Results =
  {
    Generated: string
    Commit: string
    SourceUrl: string
    SuiteVersion: string
    Summary: Summary
    Providers: Provider list
    Categories: Category list
    /// The two cost-per-correct prose strings, carried on the wire and rendered
    /// VERBATIM. The page never writes its own gloss on the number: how it was
    /// computed, and what it undercounts, are the publisher's claims to make.
    CostPerCorrectNote: string
    CostPerCorrectCaveat: string
    SessionEconomics: SessionEconomics option
    RepairRecovery: RepairRecovery option
  }

/// Which spend posture the cost-per-correct figures are read on. The feed
/// publishes both because they are different purchases — a cold first call
/// against a warm cached session — and neither one is "the" number.
[<RequireQualifiedAccess>]
type CostBasis =
  | Cold
  | Cached

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

/// The optional read. An absent key is `None`, NOT `0.0`: the publisher omits a
/// metric it did not measure, so collapsing the two would turn "unmeasured"
/// into a claim of zero — the one thing this page exists not to do.
let private gFloatOpt (o: obj) (k: string) : float option =
  let v = field o k

  if isNull (box v) then None else Some(unbox<float> v)

[<Emit("(($0 == null ? null : $0[$1]) === true)")>]
let private gBool (o: obj) (k: string) : bool = jsNative

let private parseProvider (o: obj) : Provider =
  { Name = gStr o "name"
    Model = gStr o "model"
    Tier = gStr o "tier"
    BudgetPick = gBool o "budgetPick"
    Passed = gInt o "passed"
    Total = gInt o "total"
    PassRate = gFloat o "passRate"
    MeanScore = gFloatOpt o "meanScore"
    CostPerCorrectUsd = gFloatOpt o "costPerCorrectUsd"
    CostPerCorrectCachedUsd = gFloatOpt o "costPerCorrectCachedUsd"
    ReasoningShare = gFloatOpt o "reasoningShare"
    ReasoningSplitSource = gStr o "reasoningSplitSource" }

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
          AdversarialPassRate = gFloatOpt s "adversarialPassRate"
          CostPerCorrectUsd = gFloatOpt s "costPerCorrectUsd"
          CostPerCorrectCachedUsd = gFloatOpt s "costPerCorrectCachedUsd"
          ReasoningShare = gFloatOpt s "reasoningShare" }

      FeedState.Published
        { Generated = gStr o "generatedAt"
          Commit = gStr o "commit"
          SourceUrl = gStr o "sourceUrl"
          SuiteVersion = gStr o "suiteVersion"
          Summary = summary
          Providers = [ for p in fieldArr o "providers" -> parseProvider p ]
          Categories = [ for c in fieldArr o "categories" -> parseCategory c ]
          CostPerCorrectNote = gStr o "costPerCorrectNote"
          CostPerCorrectCaveat = gStr o "costPerCorrectCaveat"
          SessionEconomics =
            let se = field o "sessionEconomics"

            if isNull (box se) then
              None
            else
              let arm (a: obj) : SessionArm =
                { Condition = gStr a "condition"
                  ModelPin = gStr a "modelPin"
                  Cells = gInt a "cells"
                  UsdAtTurn3 = gFloat a "usdAtTurn3"
                  CellsAtTurn3 = gInt a "cellsAtTurn3"
                  UsdAtTurn5 = gFloat a "usdAtTurn5"
                  CellsAtTurn5 = gInt a "cellsAtTurn5"
                  IdentityMean = gFloat a "identityMean"
                  IdentityCells = gInt a "identityCells" }

              Some
                { Cohort = gStr se "cohort"
                  Arms = [ for a in fieldArr se "arms" -> arm a ]
                  Note = gStr se "note" }
          RepairRecovery =
            let rr = field o "repairRecovery"

            if isNull (box rr) then
              None
            else
              let arm (a: obj) : RecoveryArm =
                { Label = gStr a "label"
                  Fired = gInt a "fired"
                  Recovered = gInt a "recovered" }

              Some
                { Cohort = gStr rr "cohort"
                  Conditions = [ for a in fieldArr rr "conditions" -> arm a ]
                  Families = [ for a in fieldArr rr "families" -> arm a ]
                  Note = gStr rr "note" } }

// ─── the dashboard body (a Fuaran tree – exhibit zero) ───────────────────────

let private renderNode (n: Node<'msg>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

let private headingNode (id: string) (level: int) (text: string) : Node<unit> =
  Fuaran.heading
    id
    { Level = level
      Text = TextSource.Literal text
      Variant = HeadingVariant.Standard }

/// The graceful-degradation tile. `None` leaves the value slot UNBOUND, which
/// the renderer draws as "—" — so a feed published before a metric existed
/// still renders its whole card, with the unmeasured tiles honestly blank.
let private metricOptNode
  (id: string)
  (label: string)
  (value: float option)
  (fmt: CellFormat)
  (tone: ToneVariant)
  (subtext: string)
  : Node<unit> =
  Fuaran.metric
    id
    { Defaults.metric with
        Label = TextSource.Literal label
        Value =
          (match value with
           | Some v -> Binding.Static(Some v)
           | None -> Defaults.metric.Value)
        Format = fmt
        Tone = tone
        Subtext =
          (if subtext = "" then
             None
           else
             Some(TextSource.Literal subtext)) }

let private metricNode
  (id: string)
  (label: string)
  (value: float)
  (fmt: CellFormat)
  (tone: ToneVariant)
  (subtext: string)
  : Node<unit> =
  metricOptNode id label (Some value) fmt tone subtext

let private toneFor (rate: float) : ToneVariant =
  if rate >= 0.9 then ToneVariant.Success
  elif rate >= 0.75 then ToneVariant.Default
  else ToneVariant.Warning

let private toneForOpt (rate: float option) : ToneVariant =
  match rate with
  | Some r -> toneFor r
  | None -> ToneVariant.Default

// ─── cost-per-correct: the basis, and the prose that must ride with it ───────

let private basisLabel (basis: CostBasis) : string =
  match basis with
  | CostBasis.Cold -> "cold-call"
  | CostBasis.Cached -> "cached-session"

let private costOn (basis: CostBasis) (cold: float option) (cached: float option) : float option =
  match basis with
  | CostBasis.Cold -> cold
  | CostBasis.Cached -> cached

/// The figure the reader did NOT select, named so both postures stay visible
/// without touching the toggle. An unpublished counterpart says so.
let private otherBasisText (basis: CostBasis) (cold: float option) (cached: float option) : string =
  let describe (label: string) (v: float option) =
    match v with
    | Some x -> sprintf "%s $%.4f" label x
    | None -> sprintf "%s unpublished" label

  match basis with
  | CostBasis.Cold -> describe "cached-session" cached
  | CostBasis.Cached -> describe "cold-call" cold

/// How the reasoning split was obtained. It is a fidelity caveat, not a label:
/// a derived split is an estimate, and the tile that shows the share says so.
let private splitSourceText (source: string) : string =
  match source with
  | "native" -> "split: provider-native"
  | "sdk" -> "split: SDK-reported"
  | "derived" -> "split: derived (estimated)"
  | "" -> "split source unreported"
  | other -> sprintf "split: %s" other

let private hasCostPerCorrect (r: Results) : bool =
  r.Summary.CostPerCorrectUsd.IsSome
  || r.Summary.CostPerCorrectCachedUsd.IsSome
  || r.Providers
     |> List.exists (fun p -> p.CostPerCorrectUsd.IsSome || p.CostPerCorrectCachedUsd.IsSome)

let private providerCard (basis: CostBasis) (i: int) (p: Provider) : Node<unit> =
  let metric =
    metricNode
      (sprintf "ev-pm-%d" i)
      "Pass rate"
      p.PassRate
      (CellFormat.Percent(Some 0))
      (toneFor p.PassRate)
      (sprintf
        "%d / %d prompts%s"
        p.Passed
        p.Total
        (match p.MeanScore with
         | Some m -> sprintf " · mean %.1f / 5" m
         | None -> ""))

  let modelLine =
    let tierNote =
      match p.Tier with
      | "headline" -> " · **best**"
      | "budget" -> " · budget posture"
      | _ -> ""

    sprintf "_%s_%s%s" p.Model tierNote (if p.BudgetPick then " · cost pick" else "")

  // The two economics tiles ride in every provider card, whether or not the
  // feed measured them: an absent figure is a visible "—", not a missing row.
  let costMetric =
    metricOptNode
      (sprintf "ev-pcpc-%d" i)
      "Cost per correct (USD)"
      (costOn basis p.CostPerCorrectUsd p.CostPerCorrectCachedUsd)
      (CellFormat.Number(Some 4))
      ToneVariant.Default
      (sprintf "%s basis · %s" (basisLabel basis) (otherBasisText basis p.CostPerCorrectUsd p.CostPerCorrectCachedUsd))

  let reasoningMetric =
    metricOptNode
      (sprintf "ev-prs-%d" i)
      "Reasoning share"
      p.ReasoningShare
      (CellFormat.Percent(Some 0))
      ToneVariant.Default
      (if p.ReasoningShare.IsSome then
         sprintf "of billed output · %s" (splitSourceText p.ReasoningSplitSource)
       else
         "no reasoning split published")

  let children =
    [ metric
      if p.Model <> "" then
        Fuaran.markdown (sprintf "ev-pmodel-%d" i) modelLine
      costMetric
      reasoningMetric ]

  Fuaran.card
    (sprintf "ev-p-%d" i)
    { Defaults.card with
        Heading = Some(TextSource.Literal p.Name)
        Children = children }

let private dashboardTree (basis: CostBasis) (r: Results) : Node<unit> =
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
              // The headline economic figure. Rendered at four decimals rather
              // than as a currency: a cost per correct artifact lives in tenths
              // of a cent, and the currency format's two digits would round the
              // cold/cached difference away entirely.
              metricOptNode
                "ev-s-cpc"
                "Cost per correct (USD)"
                (costOn basis r.Summary.CostPerCorrectUsd r.Summary.CostPerCorrectCachedUsd)
                (CellFormat.Number(Some 4))
                ToneVariant.Brand
                (sprintf
                  "%s basis · %s"
                  (basisLabel basis)
                  (otherBasisText basis r.Summary.CostPerCorrectUsd r.Summary.CostPerCorrectCachedUsd))
              metricOptNode
                "ev-s-reason"
                "Reasoning share"
                r.Summary.ReasoningShare
                (CellFormat.Percent(Some 0))
                ToneVariant.Default
                "reasoning tokens over billed output, pooled"
              metricNode
                "ev-s-prov"
                "Providers"
                (float r.Summary.ProvidersEvaluated)
                (CellFormat.Number(Some 0))
                ToneVariant.Default
                "evaluated head-to-head"
              metricOptNode
                "ev-s-adv"
                "Adversarial pass rate"
                r.Summary.AdversarialPassRate
                (CellFormat.Percent(Some 0))
                (toneForOpt r.Summary.AdversarialPassRate)
                "held-out adversarial track" ] }

  // The measurement note and the undercount caveat are the feed's own strings,
  // rendered verbatim beside the number they qualify — a cost figure read
  // without them is not the claim the publisher made.
  let costProse =
    if not (hasCostPerCorrect r) then
      []
    else
      [ if r.CostPerCorrectNote <> "" then
          Fuaran.callout
            "ev-cpc-note"
            { Defaults.callout with
                Tone = ToneVariant.Info
                Heading = Some(TextSource.Literal "How cost per correct is measured")
                Body = TextSource.Literal r.CostPerCorrectNote }
        if r.CostPerCorrectCaveat <> "" then
          Fuaran.markdown "ev-cpc-caveat" (sprintf "_Caveat: %s._" r.CostPerCorrectCaveat) ]

  let providerGrid =
    Fuaran.gridLayout
      "ev-prov"
      { Defaults.gridLayout<unit> with
          Cols = 3
          Children =
            [ for i, p in
                List.indexed (
                  r.Providers
                  |> List.sortBy (fun p -> (if p.Tier = "headline" then 0 else 1), -p.PassRate)
                ) -> providerCard basis i p ] }

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

  let mutable children =
    [ headingNode "ev-h-sum" 2 "Headline"; summaryGrid ] @ costProse

  if not r.Providers.IsEmpty then
    children <- children @ [ headingNode "ev-h-prov" 2 "By provider"; providerGrid ]

  if not r.Categories.IsEmpty then
    children <- children @ [ headingNode "ev-h-cat" 2 "By category"; categoryGrid ]

  match r.SessionEconomics with
  | None -> ()
  | Some se ->
    let armCard (i: int) (a: SessionArm) : Node<unit> =
      let turn5 = a.UsdAtTurn5 > 0.0

      metricNode
        (sprintf "ev-se-%d" i)
        (sprintf "%s · %s" a.Condition a.ModelPin)
        (if turn5 then a.UsdAtTurn5 else a.UsdAtTurn3)
        (CellFormat.Currency "USD")
        ToneVariant.Default
        (sprintf
          "turn 3: $%.3f (n=%d)%s · identity μ %.2f (n=%d)"
          a.UsdAtTurn3
          a.CellsAtTurn3
          (if turn5 then
             sprintf " · turn 5: $%.3f (n=%d)" a.UsdAtTurn5 a.CellsAtTurn5
           else
             "")
          a.IdentityMean
          a.IdentityCells)

    children <-
      children
      @ [ headingNode "ev-h-se" 2 "Session economics (Tier B, multi-turn)"
          Fuaran.callout
            "ev-se-note"
            { Defaults.callout with
                Tone = ToneVariant.Info
                Heading = Some(TextSource.Literal "How these are measured")
                Body = TextSource.Literal se.Note }
          Fuaran.gridLayout
            "ev-se-arms"
            { Defaults.gridLayout<unit> with
                Cols = 3
                Children =
                  [ for i, a in List.indexed (se.Arms |> List.sortBy (fun a -> a.Condition, a.ModelPin)) -> armCard i a ] } ]

  match r.RepairRecovery with
  | None -> ()
  | Some rr ->
    let recoveryMetric (prefix: string) (i: int) (a: RecoveryArm) : Node<unit> =
      let rate =
        if a.Fired > 0 then
          float a.Recovered / float a.Fired
        else
          0.0

      metricNode
        (sprintf "ev-rr-%s-%d" prefix i)
        a.Label
        rate
        (CellFormat.Percent(Some 0))
        (toneFor rate)
        (sprintf "%d / %d recovered given an induced failure" a.Recovered a.Fired)

    let condGrid =
      Fuaran.gridLayout
        "ev-rr-conds"
        { Defaults.gridLayout<unit> with
            Cols = 3
            Children = [ for i, a in List.indexed rr.Conditions -> recoveryMetric "c" i a ] }

    let famGrid =
      Fuaran.gridLayout
        "ev-rr-fams"
        { Defaults.gridLayout<unit> with
            Cols = 3
            Children = [ for i, a in List.indexed rr.Families -> recoveryMetric "f" i a ] }

    children <-
      children
      @ [ headingNode "ev-h-rr" 2 "Repair under error (Tier C)"
          Fuaran.callout
            "ev-rr-note"
            { Defaults.callout with
                Tone = ToneVariant.Info
                Heading = Some(TextSource.Literal "Read this before the numbers")
                Body = TextSource.Literal rr.Note }
          condGrid
          headingNode "ev-h-rr-fam" 3 "By model family"
          famGrid ]

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
  let basis, setBasis = React.useState CostBasis.Cold

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

  // The cost basis is the reader's choice, so it is chrome rather than tree:
  // the dashboard body renders through the no-dispatch resolver, and a control
  // that pretended to be part of the tree could not act. It appears only when
  // the published feed actually carries a cost-per-correct figure.
  let basisToggle =
    // A segmented control rather than two buttons carrying their state in a
    // ●/○ glyph: the two options are one exclusive choice, so the control
    // should show the choice space and mark the current option structurally.
    // aria-pressed stays the accessible state; the modifier class is the
    // visual one, matching the tablist convention elsewhere in the showcase.
    let basisOption (b: CostBasis) (label: string) =
      let isCurrent = basis = b

      Html.button
        [ prop.className (
            if isCurrent then
              "ev-basis-option ev-basis-option-active"
            else
              "ev-basis-option"
          )
          prop.ariaPressed isCurrent
          prop.text label
          prop.onClick (fun _ -> setBasis b) ]

    match state with
    | FeedState.Published r when hasCostPerCorrect r ->
      Html.span
        [ prop.className "ev-basis"
          prop.children
            [ Html.span [ prop.className "ev-basis-label"; prop.text "Cost basis" ]
              Html.span
                [ prop.className "ev-basis-group"
                  prop.role "group"
                  prop.ariaLabel "Cost basis"
                  prop.children
                    [ basisOption CostBasis.Cold "Cold-call"
                      basisOption CostBasis.Cached "Cached-session" ] ] ] ]
    | _ -> Html.none

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
            basisToggle
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
    | FeedState.Published r -> renderNode (dashboardTree basis r)

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
                                "Cost per correct is shown with the publisher's own measurement note and undercount caveat, rendered word for word from the feed. A metric the run did not measure shows an em dash, not a zero." ]
                          Html.li
                            [ prop.text
                                "The link above goes to the public source and methodology, so the numbers are reproducible rather than asserted." ]
                          Html.li
                            [ prop.text
                                "The dashboard you're reading is itself a Fuaran tree, drawn by the same renderer as every demo – the metrics, the per-provider cards, all of it is UI-as-data." ] ] ] ] ] ] ]

let page: ReactElement = EvaluationView()
