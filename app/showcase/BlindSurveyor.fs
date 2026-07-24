module Fuaran.Showcase.BlindSurveyor

// ============================================================================
//  The Blind Surveyor – layout is read, not looked at. Pillar: "the machine can
//  see the UI".
//
//  A four-tile dashboard renders inside a viewport rig (a width slider). Beside
//  it, a layout ledger shows each tracked node's DECLARED layout (the typed
//  fields from the tree – cols, min track, wrap) against its OBSERVED geometry
//  (scroll/client width + the shipped LayoutObserver's real `OverflowHorizontal`
//  derivation over the measured DOM). Drag the rig narrow and the observer
//  raises overflow – the visitor finds the break by reading, not looking.
//
//  The hero beat: an opaque shutter blacks out the preview. Measurement keeps
//  running (an overlay hides pixels; it does not remove the element from
//  layout), so the "inspect_layout" answer – the real scroll/client numbers and
//  the derived flag – is produced with the screen black. Then a blind fix swaps
//  the grid to a wrapping track template; the observed column goes green while
//  still shuttered; lifting the shutter only reveals what the data already knew.
// ============================================================================

open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.LayoutObserver

module LFlags = Fuaran.UI.LayoutObserver.Flags

// ─── The surveyed app – one overflow-prone grid, faulted per `patched` ─────────

let private brokenTemplate = "repeat(4, minmax(200px, 1fr))"
let private fixedTemplate = "repeat(auto-fit, minmax(150px, 1fr))"

let private tile (nid: string) (label: string) (value: string) : Node<unit> =
  Fuaran.card
    nid
    { Defaults.card with
        Heading = Some(TextSource.Literal label)
        Children = [ Fuaran.markdown (nid + "-v") value ] }

let private appTree (patched: bool) : Node<unit> =
  Fuaran.box
    "bs-root"
    { Layout =
        BoxLayout.Flex
          { Direction = Vertical
            Wrap = false
            Gap = None }
      Role = BoxRole.Dashboard
      Heading = Some(TextSource.Literal "Regional revenue")
      Children =
        [ Fuaran.callout
            "bs-note"
            { Defaults.callout with
                Tone = ToneVariant.Info
                Heading = None
                Body = TextSource.Literal "Four regional tiles. Drag the rig – the ledger reads the geometry." }
          Fuaran.box
            "bs-grid"
            { Layout =
                BoxLayout.Grid
                  { Cols = 4
                    TemplateColumns = Some(if patched then fixedTemplate else brokenTemplate)
                    Gap = Some 12 }
              Role = BoxRole.Group
              Heading = None
              Children =
                [ tile "bs-emea" "EMEA" "£5,900"
                  tile "bs-apac" "APAC" "£4,200"
                  tile "bs-amer" "Americas" "£6,750"
                  tile "bs-afr" "Africa" "£1,480" ] } ] }

// ─── The layout ledger – declared (from the tree) vs observed (measured) ─────

let private measureNodes: string[] -> obj =
  import "measureNodes" "./surveyor-measure.ts"

type private Observed =
  { Found: bool
    ScrollW: float
    ClientW: float
    Overflow: bool }

let private emptyObserved =
  { Found = false
    ScrollW = 0.0
    ClientW = 0.0
    Overflow = false }

/// Measure the grid node and run the SHIPPED LayoutObserver derivation over its
/// real geometry – the same `OverflowHorizontal` flag the orchestrator's
/// `inspect_layout` tool reads.
let private observeGrid () : Observed =
  let m = measureNodes [| "bs-grid" |]
  let g = m?("bs-grid")
  let found: bool = g?found

  if not found then
    emptyObserved
  else
    let scrollW: float = g?scrollW
    let clientW: float = g?clientW
    let ox: string = g?overflowX
    let w: float = g?w
    let h: float = g?h

    let input =
      { LFlags.LayoutInput.empty w h with
          ScrollWidth = Some scrollW
          ClientWidth = Some clientW
          OverflowX = Some ox }

    let overflow =
      LFlags.derive LayoutObserverOptions.defaults input
      |> List.exists (fun f ->
        match f with
        | LayoutFlag.OverflowHorizontal -> true
        | _ -> false)

    { Found = true
      ScrollW = scrollW
      ClientW = clientW
      Overflow = overflow }

let private declaredText (patched: bool) : string =
  if patched then
    "grid · auto-fit · min 150px · wraps"
  else
    "grid · 4 fixed cols · min 200px"

// The recorded inspect_layout tool exchange shown during the blackout – the
// numbers are the live measured geometry, so the transcript is never stale.
let private inspectJson (o: Observed) : string =
  sprintf
    "_platform.ui.inspect_layout({ node: \"bs-grid\" })\n→ { \"overflow_horizontal\": %b,\n    \"scroll_width\": %.0f,\n    \"client_width\": %.0f }"
    o.Overflow
    o.ScrollW
    o.ClientW

// ─── The page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private BlindSurveyorView () : ReactElement =
  // The rig width (px), whether the blind fix has been applied, and the shutter.
  let rigWidth, setRigWidth = React.useState 960
  let patched, setPatched = React.useState false
  let blackout, setBlackout = React.useState false
  let observed, setObserved = React.useState emptyObserved

  // Re-measure after every render that changes the tree or the rig width. A
  // synchronous `getComputedStyle` read forces a style/layout recalc, so no
  // rAF/timer is needed (a rAF inside an effect no-ops in a throttled tab).
  React.useEffect ((fun () -> setObserved (observeGrid ())), [| box rigWidth; box patched |])

  let reset () : unit =
    setBlackout false
    setPatched false
    setRigWidth 960

  let stage =
    Html.div
      [ prop.className "bs-stage-wrap"
        prop.children
          [ Html.div
              [ prop.className "bs-stage"
                prop.style [ style.width (length.px rigWidth) ]
                prop.children [ Render.renderWithSources BindingResolver.empty ignore (appTree patched) ] ]
            (if blackout then
               Html.div
                 [ prop.className "bs-shutter"
                   prop.children
                     [ Html.span
                         [ prop.className "bs-shutter-label"
                           prop.text "preview shuttered – measurement continues" ] ] ]
             else
               Html.none) ] ]

  let rig =
    Html.div
      [ prop.className "bs-rig"
        prop.children
          [ Html.div
              [ prop.className "bs-rig-head"
                prop.children
                  [ Html.span [ prop.className "bs-rig-label"; prop.text "Viewport rig" ]
                    Html.span [ prop.className "bs-rig-width"; prop.text (sprintf "%d px" rigWidth) ] ] ]
            Html.input
              [ prop.className "bs-rig-slider"
                prop.type' "range"
                prop.min 320
                prop.max 1040
                prop.value rigWidth
                prop.onChange (fun (v: string) -> setRigWidth (int v)) ] ] ]

  let ledger =
    Html.div
      [ prop.className "bs-ledger"
        prop.children
          [ Html.div
              [ prop.className "bs-ledger-head"
                prop.children
                  [ Html.span
                      [ prop.className "bs-ledger-title"
                        prop.text "Layout ledger – read, not looked at" ] ] ]
            Html.div
              [ prop.className "bs-ledger-cols"
                prop.children
                  [ Html.span [ prop.className "bs-ledger-col-h"; prop.text "declared" ]
                    Html.span [ prop.className "bs-ledger-col-h"; prop.text "observed" ] ] ]
            Html.div
              [ prop.className (
                  if observed.Overflow then
                    "bs-row bs-row-bad"
                  else
                    "bs-row bs-row-ok"
                )
                prop.children
                  [ Html.span [ prop.className "bs-node-id"; prop.text "bs-grid" ]
                    Html.span [ prop.className "bs-declared"; prop.text (declaredText patched) ]
                    Html.span
                      [ prop.className "bs-observed"
                        prop.children
                          [ Html.span
                              [ prop.className "bs-measure"
                                prop.text (
                                  if observed.Found then
                                    sprintf "scroll %.0f · client %.0f" observed.ScrollW observed.ClientW
                                  else
                                    "–"
                                ) ]
                            (if observed.Overflow then
                               Html.code [ prop.className "bs-flag"; prop.text "OverflowHorizontal" ]
                             else
                               Html.span [ prop.className "bs-flag-ok"; prop.text "fits ✓" ]) ] ] ] ] ] ]

  let blindPanel =
    Html.div
      [ prop.className "bs-blind"
        prop.children
          [ Html.div
              [ prop.className "bs-blind-head"
                prop.text "Ask the machine – screen blacked out" ]
            (if blackout then
               Html.pre
                 [ prop.className "bs-inspect"
                   prop.children [ Html.code [ prop.text (inspectJson observed) ] ] ]
             else
               Html.p
                 [ prop.className "bs-blind-hint"
                   prop.text
                     "Shutter the preview, then ask whether it fits. The answer comes from the geometry, with no pixels on screen." ])
            (if blackout then
               Html.p
                 [ prop.className "bs-blind-verdict"
                   prop.text (
                     if observed.Overflow then
                       "“bs-grid overflows – its four fixed tracks need more width than the rig gives.” No pixels were consulted; there were none."
                     else
                       "“bs-grid fits at this width.” Answered from the measured geometry, screen still black."
                   ) ]
             else
               Html.none) ] ]

  let controls =
    Html.div
      [ prop.className "bs-controls"
        prop.children
          [ Html.button
              [ prop.className "bs-btn"
                prop.text (
                  if blackout then
                    "Lift the shutter"
                  else
                    "Black out the preview"
                )
                prop.onClick (fun _ -> setBlackout (not blackout)) ]
            Html.button
              [ prop.className "bs-btn"
                prop.disabled (patched || not observed.Overflow)
                prop.text "Apply the blind fix"
                prop.onClick (fun _ -> setPatched true) ]
            (if patched || rigWidth <> 960 || blackout then
               Html.button
                 [ prop.className "bs-btn bs-btn-ghost"
                   prop.text "Reset"
                   prop.onClick (fun _ -> reset ()) ]
             else
               Html.none) ] ]

  let honesty =
    Html.div
      [ prop.className "bs-honesty"
        prop.children
          [ Html.h3 [ prop.text "No pixels were consulted" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "The observed column is real: the node's scroll/client width is measured from the laid-out DOM and fed to the shipped LayoutObserver derivation, which returns a typed OverflowHorizontal flag – the same signal the orchestrator's inspect_layout tool reads." ]
                    Html.li
                      [ prop.text
                          "The blackout is an opaque overlay: it hides the pixels but leaves the element in layout, so measurement is identical shuttered or not. The answer during the blackout is computed from those live numbers, not a screenshot." ]
                    Html.li
                      [ prop.text
                          "The blind fix swaps the grid to a wrapping track template – a real layout change; the observed column goes green before the shutter lifts. On a phone-width browser the responsive renderer already wraps the grid, so the overflow is a desktop-rig demonstration of the observer, caught before any breakpoint." ]
                    Html.li
                      [ prop.children
                          [ Html.text "This is the layout half of the "
                            Html.a [ prop.href "#/pillar/machine"; prop.text "machine-can-see-the-UI" ]
                            Html.text " story – geometry as typed data on both ends." ] ] ] ] ] ]

  Html.div
    [ prop.className "bs-page"
      prop.children
        [ Html.h1 [ prop.className "bs-title"; prop.text "The Blind Surveyor" ]
          Html.p
            [ prop.className "bs-lede"
              prop.text
                "Black out the screen and ask whether the dashboard fits on a phone. It answers – naming the node that overflows – because layout in Fuaran is read, not looked at." ]
          Html.div
            [ prop.className "bs-split"
              prop.children
                [ Html.div
                    [ prop.className "bs-preview-col"
                      prop.children
                        [ Html.h3 [ prop.className "bs-col-title"; prop.text "The survey" ]
                          rig
                          stage ] ]
                  Html.div [ prop.className "bs-data-col"; prop.children [ ledger; blindPanel ] ] ] ]
          controls
          honesty ] ]

let page: ReactElement = BlindSurveyorView()
