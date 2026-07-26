module Fuaran.Showcase.App

// ============================================================================
//  The showcase (F#) – browser entry. The shared shell of the zero-egress half
//  of the site.
//
//  The whole site is F#/Fable, no TypeScript shell: the Elmish loop owns the
//  pillar navigation + per-page hash routing, and the chrome + pages render
//  through the F# `Fuaran.UI.Renderer` – the same renderer every showcase page
//  uses to draw the tree it stages. The site is exhibit zero for "UI as typed
//  data".
//
//  The shell: a thesis landing + pillar map, pillar navigation, per-page
//  routing with coming-soon stubs, the page-agnostic scripted-replay loader
//  seam (every page's keyless mode), the CI conformance panel (honest
//  staleness), and a shared footer (wire-JSON drawer + docs + playground exit).
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
open Fuaran.Showcase

module Brand = Fuaran.Live.Brand

// The canonical reference stylesheet, from the public renderer package (the
// same import the playground entry uses). The shell's own chrome styles ride
// app.css beside this file.
importSideEffects "@fuaran-ui/renderer/css"
// KaTeX stylesheet – the `Math` primitive's client-only enhancement
// (`MathEnhance.enhanceDocument`) typesets the deterministic `.fuaran-math`
// container (native MathML or source fallback; Phase 658) in place after render;
// bundled npm package, same-origin, no key/network egress. The Degradation
// Ladder's script-less iframe rungs are untouched by design – that contrast IS
// the demo (the no-JS rung now shows real MathML superscripts).
importSideEffects "katex/dist/katex.min.css"
importSideEffects "./app.css"
// The icon-contract glyph map, shared with the playground (one file, no copies).
importSideEffects "../icon-glyphs.css"

// ─── theme (light / dark) ─────────────────────────────────────────────────────
//  The showcase injects a Fuaran theme via `themeStyleElement`; swapping it is
//  the whole of dark mode for the RENDERED exhibits – one move re-colours every
//  page's tree. The palette + the persisted preference live in the shared
//  Brand module (one definition for both entries of the site). The chrome
//  (`--fuaran-color-*`) flips via a `.ds-dark` class in app.css.

// Toggle the chrome-token class on <html> so the body background (outside the
// max-width shell) darkens too – the component tokens flip via themeStyleElement.
[<Emit("(function(){ try { document.documentElement.classList.toggle('ds-dark', $0); } catch(e){} })()")>]
let private applyDarkClass (dark: bool) : unit = jsNative

type Model =
  { Route: Pages.Route
    Replay: Replay.ReplayState
    Conformance: Conformance.PanelState
    Dark: bool }

type Msg =
  | HashChanged of string
  | ArtefactResult of Replay.ReplayState
  | ConformanceResult of Conformance.PanelState
  | ToggleDark

/// The per-route side effects: a demo page with a wired recording kicks the
/// replay load; every route (re)checks the conformance report lazily via init.
let private routeCmd (route: Pages.Route) : Cmd<Msg> =
  match route with
  | Pages.DemoPage d ->
    match d.ReplayId with
    | Some rid ->
      Replay.loadCmd rid (fun json -> ArtefactResult(Replay.ReplayState.Loaded json)) (fun reason ->
        ArtefactResult(Replay.ReplayState.Missing reason))
    | None -> Cmd.ofMsg (ArtefactResult(Replay.ReplayState.Missing "no recording is wired to this page yet"))
  | _ -> Cmd.none

/// Install a debounced MutationObserver that re-runs the client-only KaTeX
/// pass (`MathEnhance.enhanceDocument` – idempotent, never part of the parity
/// output) whenever the DOM changes. An Elmish-timed pass is not enough here:
/// showcase pages hold LOCAL React state, and a page-local re-render restores
/// the deterministic raw-source spans without any Elmish message firing. The
/// observer settles because enhanced nodes are `data-fuaran-math-done`-marked –
/// the follow-up pass is a no-op and produces no further mutations.
[<Emit("(function(run){ var pending=false; var kick=function(){ if(pending) return; pending=true; window.setTimeout(function(){ pending=false; run(); }, 50); }; new MutationObserver(function(){ kick(); }).observe(document.body, {childList:true, subtree:true}); kick(); })($0)")>]
let private installMathObserver (run: unit -> unit) : unit = jsNative

let private mathEnhanceCmd: Cmd<Msg> =
  Cmd.ofEffect (fun _ -> installMathObserver (fun () -> MathEnhance.enhanceDocument ()))

/// Register the browser `hashchange` listener as a one-shot effect (lives for the
/// app's lifetime – mirrors fuaran-live's window-listener command). Every hash
/// change re-derives the route.
let private hashListenerCmd: Cmd<Msg> =
  Cmd.ofEffect (fun dispatch ->
    window.addEventListener ("hashchange", (fun _ -> dispatch (HashChanged window.location.hash))))

let private init () : Model * Cmd<Msg> =
  let route = Pages.parseHash window.location.hash
  let dark = Brand.initialDark ()

  { Route = route
    Replay = Replay.ReplayState.Loading
    Conformance = Conformance.PanelState.Pending
    Dark = dark },
  Cmd.batch
    [ hashListenerCmd
      mathEnhanceCmd
      Conformance.loadCmd ConformanceResult
      routeCmd route
      Cmd.ofEffect (fun _ -> applyDarkClass dark) ]

let private update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
  match msg with
  | HashChanged hash ->
    let route = Pages.parseHash hash

    { model with
        Route = route
        Replay = Replay.ReplayState.Loading },
    routeCmd route
  | ArtefactResult r -> { model with Replay = r }, Cmd.none
  | ConformanceResult c -> { model with Conformance = c }, Cmd.none
  | ToggleDark ->
    let dark = not model.Dark

    { model with Dark = dark },
    Cmd.ofEffect (fun _ ->
      Brand.persistDark dark
      applyDarkClass dark)

let private renderNode (n: Node<'msg>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

let private view (model: Model) (dispatch: Msg -> unit) : ReactElement =
  Html.div
    [ prop.className "ds-shell"
      prop.children
        [ Render.themeStyleElement (Brand.theme model.Dark)
          Html.div
            [ prop.className "ds-topbar"
              prop.children
                [ Html.a
                    [ prop.className "ds-brand"
                      prop.href "#/"
                      prop.children
                        [ Html.img
                            [ prop.className "ds-brand-mark"
                              prop.src "brand/fuaran-mark.svg"
                              prop.alt "Fuaran" ]
                          Html.span [ prop.className "ds-brand-word"; prop.text "fuaran" ] ] ]
                  // The persistent zero-egress badge (D5). Present on every
                  // showcase route – the standing promise this whole origin
                  // keeps, distinct from the playground's BYOK egress.
                  Html.span
                    [ prop.className "ds-egress-badge"
                      prop.title
                        "Nothing leaves your browser – no server, no upload, no account, no key. Every page runs entirely client-side, and this origin's Content-Security-Policy permits no network calls beyond the site itself."
                      prop.children
                        [ Html.span [ prop.className "ds-egress-lock"; prop.text "🔒" ]
                          Html.span [ prop.className "ds-egress-text"; prop.text "Zero-egress" ] ] ]
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
                      prop.onClick (fun _ -> dispatch ToggleDark)
                      prop.text (if model.Dark then "☀" else "☾") ]
                  // The tagline sits on its own line beneath the logo (wraps via
                  // .ds-topbar flex-wrap) – mirrored from the docs-site masthead
                  // lockup; a change to either side re-applies to the other.
                  Html.p [ prop.className "ds-brand-tagline"; prop.text "The source for generative UI" ] ] ]
          Pages.pillarNav model.Route
          Html.main
            [ prop.className "ds-main"
              prop.children [ Pages.renderRoute model.Route model.Replay ] ]
          // Conformance only. The evaluation card left this strip deliberately:
          // conformance certifies the trees and code on the page you are looking
          // at, so it belongs beside every demo; evaluation measures how reliably
          // a MODEL emits conformant Fuaran, which is a language-adoption metric
          // whose home is the Evaluation page (and fuaran-ui.io, where the
          // methodology and provenance live). Repeating its headline under every
          // capability demo diluted it and taxed pages it said nothing about.
          Html.div
            [ prop.className "ds-status-strip"
              prop.children [ renderNode (Conformance.panel model.Conformance) ] ]
          Pages.footer (Pages.footerWire model.Route model.Replay) ] ]

Program.mkProgram init update view
|> Program.withReactSynchronous "fuaran-showcase-root"
|> Program.run
