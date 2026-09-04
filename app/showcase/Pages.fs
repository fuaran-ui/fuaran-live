module Fuaran.Showcase.Pages

// ============================================================================
//  Routing + the per-route views + the shared footer.
//
//  Navigation is by hash route (static-hostable, no server rewrites): every link
//  is a real <a href="#/…">, and App.fs listens for `hashchange`. The page bodies
//  are Fuaran trees rendered through the F# renderer (the site is exhibit zero);
//  the surrounding chrome (nav, footer, the wire-JSON drawer) is Feliz Html, the
//  same mixed shape fuaran-live uses.
// ============================================================================

open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.Showcase

/// Render a Fuaran node. Navigation is via hash links, so the tree never
/// dispatches – `ignore` is the no-op message sink.
let private renderNode (n: Node<'msg>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

/// The playground lives on its own origin (D8/D10 – one repo, two origins). The
/// door + the footer exit both link here. Defined in `Pillars` since Phase 718 —
/// the Navigator page also links across, and it is compiled ahead of this module.
/// Re-exported here so the existing call sites read unchanged.
let playgroundOrigin: string = Pillars.playgroundOrigin

// ─── routing ──────────────────────────────────────────────────────────────

type Route =
  | Home
  | PillarPage of Pillars.Pillar
  | DemoPage of Pillars.Demo
  | EvaluationPage
  | ContactPage
  | NotFound of string

let parseHash (raw: string) : Route =
  let trimmed = if raw.StartsWith "#" then raw.Substring 1 else raw

  // A route may carry a page-data suffix after '?' – e.g. Teleport's
  // fragment-encoded bundle (#/demo/teleport?t=FT1…). It rides the FRAGMENT
  // (never the query string) so the payload is never transmitted to any
  // server; strip it before route matching – the page reads it itself.
  let pathOnly =
    match trimmed.IndexOf '?' with
    | -1 -> trimmed
    | i -> trimmed.Substring(0, i)

  let h = pathOnly.Trim('/')

  if h = "" then
    Home
  else
    let parts = h.Split('/')

    match parts.[0] with
    | "evaluation" -> EvaluationPage
    | "contact" -> ContactPage
    | "pillar" when parts.Length > 1 ->
      match Pillars.pillarBySlug parts.[1] with
      | Some p -> PillarPage p
      | None -> NotFound raw
    | "demo" when parts.Length > 1 ->
      match Pillars.demoByRoute parts.[1] with
      | Some d -> DemoPage d
      | None -> NotFound raw
    | _ -> NotFound raw

let homeHash = "#/"
let evaluationHash = "#/evaluation"
let contactHash = "#/contact"
let pillarHash (p: Pillars.Pillar) : string = "#/pillar/" + Pillars.pillarSlug p
let demoHash (d: Pillars.Demo) : string = "#/demo/" + d.Route

// ─── chrome (Feliz) ─────────────────────────────────────────────────────────

let private navLink (href: string) (label: string) (active: bool) : ReactElement =
  Html.a
    [ prop.className (
        if active then
          "ds-nav-link ds-nav-active"
        else
          "ds-nav-link"
      )
      prop.href href
      prop.text label ]

let pillarNav (current: Route) : ReactElement =
  let isPillar p =
    match current with
    | PillarPage x -> x = p
    | _ -> false

  Html.nav
    [ prop.className "ds-nav"
      prop.children
        [ navLink homeHash "Home" (current = Home)
          yield! [ for p in Pillars.allPillars -> navLink (pillarHash p) (Pillars.pillarTitle p) (isPillar p) ]
          navLink evaluationHash "Evaluation" (current = EvaluationPage) ] ]

// ─── landing (thesis + pillar map) ──────────────────────────────────────────

let private headingNode (id: string) (level: int) (text: string) : Node<unit> =
  Fuaran.heading
    id
    { Level = level
      Text = TextSource.Literal text
      Variant = HeadingVariant.Standard }

let private header: Node<unit> =
  Fuaran.dashboard
    "ds-header"
    { Defaults.dashboard with
        Children =
          [ Fuaran.markdown
              "ds-header-intro"
              "**Fuaran** is a language for generative UI – an interface is *data*, a typed tree with one canonical wire format rather than framework code. The same artefact renders across **nine host languages** (F#, C#, Visual Basic, TypeScript, Python, Go, Kotlin, Rust, and Swift), so what you see below is portable by construction."
            headingNode "ds-header-title" 1 "See what UI-as-data can do."
            Fuaran.markdown
              "ds-header-thesis"
              "An app that is **data** can be scrubbed, branched, notarised, teleported, queried, re-projected, audited, and asserted against – because it is not code, it is a **value**. These are not so many features; they are a single design decision paying out again and again. Each page stages exactly a single consequence, end to end."
            Fuaran.callout
              "ds-header-exhibit"
              { Defaults.callout with
                  Tone = ToneVariant.Brand
                  Heading = Some(TextSource.Literal "Exhibit zero – this site is a Fuaran app")
                  Body =
                    TextSource.Literal
                      "Every pixel here – this nav, these cards, this very callout, and each demo – is one app authored in F# and drawn by the exact Fuaran.UI.Renderer the demos showcase. There is no framework underneath. The site is not about UI-as-data; it is UI-as-data." } ] }

let private pillarCard (p: Pillars.Pillar) : ReactElement =
  Html.a
    [ prop.className "ds-pillar-card"
      prop.href (pillarHash p)
      prop.children
        [ renderNode (
            Fuaran.card
              ("ds-pc-" + Pillars.pillarSlug p)
              { Defaults.card with
                  Heading = Some(TextSource.Literal(Pillars.pillarTitle p))
                  Children = [ Fuaran.markdown ("ds-pcb-" + Pillars.pillarSlug p) (Pillars.pillarBlurb p) ] }
          ) ] ]

/// A distinct full-width tile under the pillar grid – Evaluation is a live
/// benchmark dashboard, a first-class destination but not a fifth pillar.
let private evaluationTile: ReactElement =
  Html.a
    [ prop.className "ds-eval-tile"
      prop.href evaluationHash
      prop.children
        [ Html.span
            [ prop.className "ds-eval-badge"
              prop.children [ Html.span [ prop.className "ds-eval-dot" ]; Html.text "Live" ] ]
          renderNode (
            Fuaran.card
              "ds-eval-card"
              { Defaults.card with
                  Heading = Some(TextSource.Literal "Evaluation results")
                  Children =
                    [ Fuaran.markdown
                        "ds-eval-blurb"
                        "How reliably an AI turns a natural-language prompt into correct, conformant UI – read **live** from the published evaluation feed, with a link to the public source. Nothing hand-typed." ] }
          ) ] ]

/// The door out to the playground (D5). The showcase is the zero-egress door
/// the visitor is already through; this states the *other* contract – BYOK
/// egress – in one line before they cross to the other origin.
let private playgroundDoor: ReactElement =
  Html.a
    [ prop.className "ds-playground-door"
      prop.href playgroundOrigin
      prop.children
        [ Html.span [ prop.className "ds-door-kicker"; prop.text "The hands-on door →" ]
          renderNode (
            Fuaran.card
              "ds-door-card"
              { Defaults.card with
                  Heading = Some(TextSource.Literal "Try it yourself – the playground")
                  Children =
                    [ Fuaran.markdown
                        "ds-door-blurb"
                        "Leave the zero-egress showcase for the hands-on half: **bring your own key** and prompt a model to emit live Fuaran UI. Your key is held in memory only and sent solely to your chosen provider – the one place on the site that talks to the outside world." ] }
          ) ] ]

/// The door to the documentation (the reciprocal of the docs site's "Demos"
/// footer link): the reference half of the triangle, for the visitor who has
/// seen the demos and wants to build.
let private docsDoor: ReactElement =
  Html.a
    [ prop.className "ds-playground-door"
      prop.href "https://fuaran-ui.io"
      prop.target "_blank"
      prop.rel "noreferrer"
      prop.children
        [ Html.span [ prop.className "ds-door-kicker"; prop.text "The reference door →" ]
          renderNode (
            Fuaran.card
              "ds-docs-door-card"
              { Defaults.card with
                  Heading = Some(TextSource.Literal "Read the docs – fuaran-ui.io")
                  Children =
                    [ Fuaran.markdown
                        "ds-docs-door-blurb"
                        "The language guide, the wire-format specification and conformance corpus, the live component reference, and get-started tracks for all nine host languages." ] }
          ) ] ]

let landing: ReactElement =
  Html.div
    [ prop.className "ds-landing"
      prop.children
        [ renderNode header
          Html.div
            [ prop.className "ds-pillar-grid"
              prop.children [ for p in Pillars.allPillars -> pillarCard p ] ]
          evaluationTile
          playgroundDoor
          docsDoor ] ]

// ─── pillar page (its demos) ────────────────────────────────────────────────

let private demoTeaser (d: Pillars.Demo) : ReactElement =
  let heading = if d.Live then d.Title else d.Title + " · coming soon"

  Html.a
    [ prop.className "ds-demo-teaser"
      prop.href (demoHash d)
      prop.children
        [ renderNode (
            Fuaran.card
              ("ds-dt-" + d.Id)
              { Defaults.card with
                  Heading = Some(TextSource.Literal heading)
                  Children = [ Fuaran.markdown ("ds-dtw-" + d.Id) d.Wow ] }
          ) ] ]

let pillarPage (p: Pillars.Pillar) : ReactElement =
  Html.div
    [ prop.className "ds-pillar-page"
      prop.children
        [ renderNode (headingNode "ds-pp-title" 1 (Pillars.pillarTitle p))
          renderNode (Fuaran.markdown "ds-pp-blurb" (Pillars.pillarBlurb p))
          Html.div
            [ prop.className "ds-demo-grid"
              prop.children [ for d in Pillars.demosInPillar p -> demoTeaser d ] ] ] ]

// ─── demo page (stub – consumes the replay-loader seam) ─────────────────────

let private replayView (d: Pillars.Demo) (replay: Replay.ReplayState) : ReactElement =
  match d.ReplayId with
  | None -> renderNode (Fuaran.markdown "ds-demo-noreplay" "_No recording is wired to this page yet._")
  | Some _ ->
    match replay with
    | Replay.ReplayState.Loading -> renderNode (Fuaran.markdown "ds-demo-loading" "_Loading the recorded replay…_")
    | Replay.ReplayState.Missing reason ->
      renderNode (
        Fuaran.callout
          "ds-demo-missing"
          { Defaults.callout with
              Tone = ToneVariant.Subdued
              Heading = Some(TextSource.Literal "Replay unavailable")
              Body = TextSource.Literal reason }
      )
    | Replay.ReplayState.Loaded json ->
      Html.div
        [ prop.className "ds-replay"
          prop.children
            [ renderNode (
                Fuaran.markdown
                  "ds-replay-note"
                  (sprintf
                    "Loaded a recorded artefact (%d bytes) through the shared replay-loader – **no API key, no setup.** That keyless mode is the site's reliability floor."
                    json.Length)
              )
              renderNode (Fuaran.codeBlock "ds-replay-json" "json" json) ] ]

let demoPage (d: Pillars.Demo) (replay: Replay.ReplayState) : ReactElement =
  let status =
    if d.Live then
      Html.none
    else
      renderNode (
        Fuaran.callout
          "ds-demo-status"
          { Defaults.callout with
              Tone = ToneVariant.Info
              Heading = Some(TextSource.Literal "This page is on its way")
              Body =
                TextSource.Literal
                  "The full demo lands soon. Below is the scripted replay it will play back – the same recorded artefact, with no key pasted." }
      )

  Html.div
    [ prop.className "ds-demo-page"
      prop.children
        [ renderNode (headingNode "ds-demo-title" 1 d.Title)
          renderNode (Fuaran.markdown "ds-demo-wow" d.Wow)
          status
          replayView d replay ] ]

let notFoundPage (raw: string) : ReactElement =
  Html.div
    [ prop.className "ds-notfound"
      prop.children
        [ renderNode (headingNode "ds-nf-title" 1 "Nothing here")
          renderNode (
            Fuaran.markdown
              "ds-nf-body"
              (sprintf "No page matches `%s`. Head back to the [home page](%s)." raw homeHash)
          ) ] ]

let renderRoute (route: Route) (replay: Replay.ReplayState) : ReactElement =
  match route with
  | Home -> landing
  | PillarPage p -> pillarPage p
  // Live demo pages own their whole body (their own interactive component);
  // the replay-stub path serves every page still on its way.
  | DemoPage d when d.Id = "rosetta" -> Rosetta.page
  | DemoPage d when d.Id = "attesor" -> Attesor.page
  | DemoPage d when d.Id = "versioning" -> WireVersioning.page
  | DemoPage d when d.Id = "teleport" -> Teleport.page
  | DemoPage d when d.Id = "infinite-skins" -> Skins.page
  | DemoPage d when d.Id = "every-screen" -> Responsive.page
  | DemoPage d when d.Id = "kintsugi" -> Kintsugi.page
  | DemoPage d when d.Id = "time-machine" -> TimeMachine.page
  | DemoPage d when d.Id = "blind-surveyor" -> BlindSurveyor.page
  | DemoPage d when d.Id = "notarised" -> Notarised.page
  | DemoPage d when d.Id = "unit-test" -> UnitTest.page
  | DemoPage d when d.Id = "git-for-interfaces" -> GitForInterfaces.page
  | DemoPage d when d.Id = "bouncer" -> Bouncer.page
  | DemoPage d when d.Id = "degradation" -> Degradation.page
  | DemoPage d when d.Id = "pandas" -> Pandas.page
  | DemoPage d when d.Id = "send-me" -> Send.page
  | DemoPage d when d.Id = "grep-apps" -> GrepApps.page
  | DemoPage d when d.Id = "what-if" -> WhatIf.page
  | DemoPage d when d.Id = "bazaar" -> Bazaar.page
  | DemoPage d when d.Id = "relay" -> Relay.page
  | DemoPage d when d.Id = "living-sheet" -> LivingSheet.page
  | DemoPage d when d.Id = "counterfactual" -> Counterfactual.page
  | DemoPage d when d.Id = "pattern-bank" -> PatternBank.page
  | DemoPage d when d.Id = "chart-as-data" -> Charts.page
  | DemoPage d when d.Id = "typed-question" -> TypedQuestion.page
  | DemoPage d when d.Id = "hand-on-the-wheel" -> HandOnTheWheel.page
  | DemoPage d when d.Id = "agent-readable" -> AgentReadable.page
  | DemoPage d when d.Id = "locale-lens" -> LocaleLens.page
  | DemoPage d when d.Id = "go-sessions" -> GoSessions.page
  | DemoPage d when d.Id = "navigator" -> Navigator.page
  // The platform-baseline exhibits (Phase 1129).
  | DemoPage d when d.Id = "briefing" -> Briefing.page
  | DemoPage d when d.Id = "embedded" -> Embedded.page
  | DemoPage d when d.Id = "situation-room" -> Situation.page
  | DemoPage d when d.Id = "intake" -> Intake.page
  | DemoPage d when d.Id = "bidi" -> Bidi.page
  | DemoPage d when d.Id = "invoice" -> Invoice.page
  | DemoPage d when d.Id = "roster" -> Roster.page
  | DemoPage d when d.Id = "catalog" -> Catalog.page
  | DemoPage d when d.Id = "outline" -> Outline.page
  | DemoPage d when d.Id = "handover" -> Handover.page
  | DemoPage d when d.Id = "attach" -> Attach.page
  | DemoPage d -> demoPage d replay
  | EvaluationPage -> Evaluation.page
  | ContactPage -> Contact.page
  | NotFound raw -> notFoundPage raw

// ─── shared footer (wire-JSON drawer + docs + fuaran-live exit) ─────────────

/// The wire JSON of "what you just saw" – the loaded replay artefact on a demo
/// page. None elsewhere (the drawer only appears once there is a tree to show).
let footerWire (route: Route) (replay: Replay.ReplayState) : string option =
  match route with
  | DemoPage _ ->
    match replay with
    | Replay.ReplayState.Loaded json -> Some json
    | _ -> None
  | _ -> None

let footer (wireJson: string option) : ReactElement =
  Html.footer
    [ prop.className "ds-footer"
      prop.children
        [ (match wireJson with
           | Some json ->
             Html.details
               [ prop.className "ds-wire-drawer"
                 prop.children
                   [ Html.summary [ prop.text "Wire JSON – what you just saw" ]
                     Html.pre [ prop.className "ds-wire-json"; prop.text json ] ] ]
           | None -> Html.none)
          Html.div
            [ prop.className "ds-footer-links"
              prop.children
                [ Html.a
                    [ prop.className "ds-footer-link"
                      prop.href "https://fuaran-ui.io"
                      prop.target "_blank"
                      prop.rel "noreferrer"
                      prop.text "Docs" ]
                  Html.a [ prop.className "ds-footer-link"; prop.href contactHash; prop.text "Contact" ]
                  Html.a
                    [ prop.className "ds-footer-link ds-footer-exit"
                      prop.href playgroundOrigin
                      prop.target "_blank"
                      prop.rel "noreferrer"
                      prop.text "Build your own → the playground" ] ] ]
          Html.p
            [ prop.className "ds-footer-credit"
              prop.text
                "Exhibit zero – the whole site is a Fuaran app: F#/Fable, drawn by the same Fuaran.UI.Renderer as every demo. No framework underneath." ] ] ]
