module Fuaran.Showcase.Catalog

// ============================================================================
//  The Catalogue — a carousel from one number (Phase 1122).
//  Pillar: "intent, not implementation".
//
//  `autoAdvanceMs` is the ONE thing this carousel puts on the wire, and it says
//  only "this switch is meant to move on its own, this often". Everything else
//  — which gesture advances it, which key does, how far a finger must travel
//  before it counts, what pauses the timer and what stops it for good — is the
//  renderer's, under the affordance-to-op charter. No event name, no threshold
//  and no gesture reaches the vocabulary, so a document says WHAT the switch
//  does and never HOW a reader takes it over.
//
//  A DURATION RATHER THAN A FLAG, and `None` is the only spelling of "does not
//  advance". "Advances" with no interval is not renderable, and two hosts
//  inventing a period is precisely the divergence a wire format exists to
//  prevent. A non-positive value is refused at decode rather than read as off,
//  because absence already means off.
//
//  WCAG 2.2.2 IS THE DESIGN, NOT A DECORATION. Content that moves by itself for
//  more than five seconds must be pausable, stoppable or hideable by the reader,
//  and three obligations follow — the asymmetry between the second and the third
//  being the whole rule:
//
//   * PAUSE while pointing, reading or touching. Hover, focus-within and a held
//     touch each suspend the timer and each release it again. A courtesy: the
//     reader has not asked for anything, so nothing is decided.
//   * STOP PERMANENTLY on interaction. A reader who swipes, presses an arrow key
//     or clicks inside the stage has taken control, and a carousel that resumed
//     afterwards would drag them off whatever they chose to look at. The stop is
//     a one-way latch for the life of the mount: no resume path, no timeout back
//     to running, no resume-after-inactivity heuristic.
//   * NEVER START under `prefers-reduced-motion: reduce`. In the renderer rather
//     than the stylesheet, because a stylesheet can suppress the TRANSITION and
//     cannot suppress the ADVANCE — the content would still change under the
//     reader, silently, which is the harm the preference is about.
//
//  The state read-out below is `data-fuaran-switch-state`, taken from the live
//  DOM. It has four values and never three: `inert` is a switch that never had a
//  timer, `stopped` is one the reader turned off, and collapsing them would make
//  a stationary carousel unable to say which it was — exactly the distinction an
//  audit needs.
// ============================================================================

open Fable.Core
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

/// Read the stage's own state token out of the DOM. The renderer writes it from
/// render state, so this is what the mechanism believes rather than what this
/// page hoped.
[<Emit("(function(){ var el=document.querySelector('.ct-stage [data-fuaran-switch-state]'); return el ? String(el.getAttribute('data-fuaran-switch-state')) : 'not mounted'; })()")>]
let private readStageState () : string = jsNative

[<Emit("(function(){ try { return window.matchMedia('(prefers-reduced-motion: reduce)').matches; } catch(e) { return false; } })()")>]
let private prefersReducedMotion () : bool = jsNative

let private kSlide = "catalog.slide"

// ─── the slides ──────────────────────────────────────────────────────────────

let private slide (id: string) (heading: string) (blurb: string) (price: string) (badge: string) : Node<obj> =
  Fuaran.box
    id
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 10)
      Role = BoxRole.Card
      KeepTogether = false
      BreakBefore = false
      Heading = Some(TextSource.Literal heading)
      Children =
        [ Fuaran.badge
            (id + "-badge")
            { Label = TextSource.Literal badge
              Variant = BadgeVariant.Info }
          Fuaran.markdown (id + "-blurb") blurb
          Fuaran.factSpec
            (id + "-price")
            { Defaults.fact with
                Label = TextSource.Literal "From"
                Value = TextSource.Literal price
                Emphasis = true } ] }

let private cases: SwitchCase<obj> list =
  [ { Match = "loom"
      Child =
        slide
          "ct-loom"
          "The Harris Loom"
          "A single-width Hattersley, restored and running. Woven to order in lengths of nine yards; the warp is set once a season, so the colourway you choose is the colourway that season has."
          "£420"
          "Made to order" }
    { Match = "press"
      Child =
        slide
          "ct-press"
          "The Albion Press"
          "An 1858 hand press, cast iron, still on its original platen. Prints a forme up to crown folio. Sold with the frisket, the tympan and about forty pounds of type nobody has sorted."
          "£3,100"
          "One only" }
    { Match = "kiln"
      Child =
        slide
          "ct-kiln"
          "The Bottle Kiln"
          "Not for sale — the kiln is the reason for the rest of it. Fired twice a year to 1,280 degrees over three days, which is why the glaze is never quite the same twice and why we do not photograph it in advance."
          "—"
          "Not for sale" }
    { Match = "bench"
      Child =
        slide
          "ct-bench"
          "The Joiner's Bench"
          "European beech, eight feet, twin screws, dog holes on four-inch centres. Built from a pattern that has not changed since 1911 because nobody has yet suggested an improvement that survived a winter."
          "£1,850"
          "Two in stock" } ]

/// The carousel. One slot on the wire beyond the switch that was always here:
/// `autoAdvanceMs`. The cross-fade is a `Motion` token on the node — the
/// renderer needs no wrapper for it, because a switch whose case changes
/// REPLACES the child and the reference sheet's motion rule animates the
/// incoming one on its own.
let private carousel: Node<obj> =
  Fuaran.switch
    "ct-carousel"
    { Defaults.switch<obj> with
        On = Binding.State(kSlide, Some "loom")
        Cases = cases
        Default = slide "ct-default" "The workshop" "Pick a piece." "—" "Catalogue"
        AutoAdvanceMs = Some 4500 }
  |> Node.withMotion Motion.CrossFade

/// The same four slides with no interval declared. Identical bytes but for one
/// absent key, and identical DOM — which is the compatibility claim made
/// checkable rather than asserted.
let private still: Node<obj> =
  Fuaran.switch
    "ct-still"
    { Defaults.switch<obj> with
        On = Binding.State("catalog.still", Some "press")
        Cases = cases
        Default = slide "ct-still-default" "The workshop" "Pick a piece." "—" "Catalogue" }

let private wireMoving: string = CJson.encodeNode carousel
let private wireStill: string = CJson.encodeNode still

// ─── the page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private CatalogView () : ReactElement =
  StateStore.useStateKeys (Set.ofList [ kSlide; "catalog.still" ]) |> ignore
  let stageState, setStageState = React.useState "…"
  let reduced, setReduced = React.useState false

  React.useEffectOnce (fun () ->
    StateStore.set kSlide (box "loom")
    StateStore.set "catalog.still" (box "press")
    setReduced (prefersReducedMotion ()))

  // Poll the stage's own attribute. Polling rather than subscribing because the
  // attribute is the RENDERER's, written from its render state — this page is a
  // reader of the mechanism, not a participant in it.
  React.useEffect (
    (fun () ->
      let handle = JS.setInterval (fun () -> setStageState (readStageState ())) 400
      (fun () -> JS.clearInterval handle): unit -> unit),
    [||]
  )

  let current =
    match StateStore.get kSlide with
    | Some v -> unbox<string> v
    | None -> "—"

  let stateChip =
    let cls =
      match stageState with
      | "running" -> "ct-chip ct-chip-run"
      | "paused" -> "ct-chip ct-chip-pause"
      | "stopped" -> "ct-chip ct-chip-stop"
      | "inert" -> "ct-chip ct-chip-inert"
      | _ -> "ct-chip"

    Html.span [ prop.className cls; prop.text stageState ]

  let explain =
    match stageState with
    | "running" -> "The timer is live at the declared interval."
    | "paused" -> "Suspended — you are pointing at it, focused inside it, or holding it."
    | "stopped" ->
      "You took control. It will not start again for the life of this page, and there is deliberately no way back."
    | "inert" ->
      if reduced then
        "Your system asks for reduced motion, so no timer was ever started. The stylesheet could have stopped the fade; only the renderer can stop the advance."
      else
        "No timer was ever started."
    | _ -> "Waiting for the stage to mount."

  let stagePanel =
    Exhibit.panel
      "It moves on its own, until you touch it"
      "Wait and it advances every four and a half seconds. Hover it and the timer suspends. Swipe it, press an arrow key, or click inside it — and it stops for good, which is not the same thing and the read-out below says which."
      [ Html.div [ prop.className "ct-stage"; prop.children [ Exhibit.renderLive carousel ] ]
        Html.div
          [ prop.className "ct-bar"
            prop.children
              [ Html.div
                  [ prop.className "ct-bar-l"
                    prop.children [ Html.span [ prop.text "data-fuaran-switch-state" ]; stateChip ] ]
                Html.div [ prop.className "ct-bar-c"; prop.text ("showing: " + current) ] ] ]
        Html.p [ prop.className "ct-explain"; prop.text explain ] ]

  let point (text: string) = Html.li [ prop.text text ]

  let rulesPanel =
    Exhibit.panel
      "Three obligations, and the asymmetry between two of them"
      "Content that moves by itself for more than five seconds has to be pausable, stoppable or hideable by the reader. That is not a nicety bolted on afterwards; it is what decided the shape of this feature."
      [ Html.ul
          [ prop.className "px-points"
            prop.children
              [ point
                  "PAUSE is a courtesy and it is reversible. Pointing at a carousel, focusing inside it or holding a finger on it suspends the timer and releasing resumes it — the reader asked for nothing, so nothing was decided."
                point
                  "STOP is a decision and it is one-way. A swipe, an arrow key or a click inside the stage means the reader has taken control, and a carousel that resumed would drag them off whatever they chose to look at. There is no resume path and no inactivity heuristic — on purpose."
                point
                  "REDUCED MOTION means the timer never exists. It is in the renderer rather than the stylesheet because a stylesheet can suppress the fade and cannot suppress the advance: the content would still change under the reader, silently, which is the harm the preference is about."
                point
                  "The state token has four values, not three. Inert never had a timer; stopped had one and the reader ended it. Collapsing them would leave a stationary carousel unable to say which it was, which is exactly what an audit needs to know." ] ] ]

  let comparePanel =
    Exhibit.panel
      "The same four slides, with the number left out"
      "No timer, no stage wrapper, no state attribute — the DOM a switch has always produced. Every carousel authored before this release is unchanged in the type, on the wire and on the screen, and this is that claim made checkable rather than asserted."
      [ Exhibit.renderLive still
        Exhibit.wireDrawer "The moving one — one key more" wireMoving
        Exhibit.wireDrawer "The still one" wireStill ]

  Exhibit.shell
    "catalog"
    "The Catalogue"
    "A workshop catalogue that turns its own pages. One number on the wire says how often; everything about how you take it over — the swipe, the arrow keys, the pause, the stop — belongs to the renderer, and none of it reaches the document."
    [ stagePanel; rulesPanel; comparePanel ]
    [ Exhibit.Claim.Verified
        "The advance, the swipe, the arrow keys, the pause and the one-way stop are all the shipped renderer's. This page declares one integer and reads one attribute; it implements none of the behaviour."
      Exhibit.Claim.Verified
        "The state chip is data-fuaran-switch-state, read from the live DOM every 400ms. It is what the mechanism believes, so a regression would show here as a wrong word rather than as a page that merely looked similar."
      Exhibit.Claim.Verified
        "The comparison carousel differs from the moving one by exactly one absent key. Open both wires."
      Exhibit.Claim.Limit
        "If your system asks for reduced motion, the top carousel will read inert and never move. That is the feature working; there is no override on this page, because offering one would be this exhibit deciding it knew better than your settings."
      Exhibit.Claim.Limit
        "The workshop and its prices are invented. The catalogue shape is the honest place to want an auto-advancing switch, which is why the exhibit is one rather than four coloured rectangles." ]

let page: ReactElement = CatalogView()
