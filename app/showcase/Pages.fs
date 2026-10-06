module Fuaran.Showcase.Pages

// ============================================================================
//  Routing + the per-route views + the shared footer.
//
//  Navigation is by hash route (static-hostable, no server rewrites): every link
//  is a real <a href="#/…">, and App.fs listens for `hashchange`. The page bodies
//  are Fuaran trees rendered through the F# renderer (the site is exhibit zero);
//  the surrounding chrome (nav, footer) is Feliz Html, the same mixed shape the
//  playground uses. Every page route resolves through the registry (Registry.fs):
//  the route names a page by its id, and the shell loads the page's chunk.
// ============================================================================

open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.Showcase

/// The playground lives on its own origin (D8/D10 – one repo, two origins). The
/// door + the footer exit both link here. Defined in `Pillars` since Phase 718 —
/// the Navigator page also links across, and it is compiled ahead of this module.
/// Re-exported here so the existing call sites read unchanged.
let playgroundOrigin: string = Pillars.playgroundOrigin

// ─── routing ──────────────────────────────────────────────────────────────

type Route =
  | Home
  | PillarPage of Pillars.Pillar
  /// A registry page, by id. The id rather than the entry, so a route stays a
  /// plain value the shell can compare.
  | DemoPage of string
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
      match Registry.pageById parts.[1] with
      | Some d -> DemoPage d.Id
      | None -> NotFound raw
    | _ -> NotFound raw

let homeHash = "#/"
let evaluationHash = "#/evaluation"
let contactHash = "#/contact"
let pillarHash (p: Pillars.Pillar) : string = "#/pillar/" + Pillars.pillarSlug p
let demoHash (d: Registry.Page) : string = "#/demo/" + d.Id

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
        [ Exhibit.renderStatic (
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
          Exhibit.renderStatic (
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
          Exhibit.renderStatic (
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
          Exhibit.renderStatic (
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
        [ Exhibit.renderStatic header
          Html.div
            [ prop.className "ds-pillar-grid"
              prop.children [ for p in Pillars.allPillars -> pillarCard p ] ]
          evaluationTile
          playgroundDoor
          docsDoor ] ]

// ─── pillar page (its index, derived from the registry) ─────────────────────

let private demoTeaser (d: Registry.Page) : ReactElement =
  Html.a
    [ prop.className "ds-demo-teaser"
      prop.href (demoHash d)
      prop.children
        [ Exhibit.renderStatic (
            Fuaran.card
              ("ds-dt-" + d.Id)
              { Defaults.card with
                  Heading = Some(TextSource.Literal d.Title)
                  Children = [ Fuaran.markdown ("ds-dtw-" + d.Id) d.Wow ] }
          ) ] ]

let pillarPage (p: Pillars.Pillar) : ReactElement =
  Html.div
    [ prop.className "ds-pillar-page"
      prop.children
        [ Exhibit.renderStatic (headingNode "ds-pp-title" 1 (Pillars.pillarTitle p))
          Exhibit.renderStatic (Fuaran.markdown "ds-pp-blurb" (Pillars.pillarBlurb p))
          Html.div
            [ prop.className "ds-demo-grid"
              prop.children [ for d in Registry.pagesInPillar p -> demoTeaser d ] ] ] ]

let notFoundPage (raw: string) : ReactElement =
  Html.div
    [ prop.className "ds-notfound"
      prop.children
        [ Exhibit.renderStatic (headingNode "ds-nf-title" 1 "Nothing here")
          Exhibit.renderStatic (
            Fuaran.markdown
              "ds-nf-body"
              (sprintf "No page matches `%s`. Head back to the [home page](%s)." raw homeHash)
          ) ] ]

// ─── lazily loaded pages ────────────────────────────────────────────────────

/// Where a lazily loaded page stands. A page is fetched the first time its
/// route is visited and kept for the rest of the visit.
[<RequireQualifiedAccess>]
type PageLoad =
  | Loading
  | Ready of ReactElement
  | Failed of string

/// The key a route's page is loaded under — None for the routes the shell
/// draws itself.
let pageKey (route: Route) : string option =
  match route with
  | DemoPage id -> Some id
  | EvaluationPage -> Some "evaluation"
  | ContactPage -> Some "contact"
  | Home
  | PillarPage _
  | NotFound _ -> None

/// The loader for a page key, from the registry.
let pageLoader (key: string) : (unit -> Fable.Core.JS.Promise<ReactElement>) option =
  match key with
  | "evaluation" -> Some Registry.loadEvaluation
  | "contact" -> Some Registry.loadContact
  | id -> Registry.pageById id |> Option.map (fun d -> d.Load)

let private loadingPage: ReactElement =
  Html.div
    [ prop.className "ds-page-loading"
      prop.ariaBusy true
      prop.text "Loading the page…" ]

let private failedPage (reason: string) : ReactElement =
  Exhibit.renderStatic (
    Fuaran.callout
      "ds-page-failed"
      { Defaults.callout with
          Tone = ToneVariant.Critical
          Heading = Some(TextSource.Literal "This page did not load")
          Body = TextSource.Literal("The page's code could not be fetched (" + reason + "). Reload to try again.") }
  )

let renderRoute (route: Route) (loads: Map<string, PageLoad>) : ReactElement =
  match route with
  | Home -> landing
  | PillarPage p -> pillarPage p
  | NotFound raw -> notFoundPage raw
  | DemoPage _
  | EvaluationPage
  | ContactPage ->
    match pageKey route |> Option.bind (fun k -> Map.tryFind k loads) with
    | Some(PageLoad.Ready page) -> page
    | Some(PageLoad.Failed reason) -> failedPage reason
    | Some PageLoad.Loading
    | None -> loadingPage

// ─── shared footer (docs + contact + playground exit) ───────────────────────

let footer: ReactElement =
  Html.footer
    [ prop.className "ds-footer"
      prop.children
        [ Html.div
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
