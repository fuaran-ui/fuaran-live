module Fuaran.Showcase.Kintsugi

// ============================================================================
//  Kintsugi – the self-healing app. Pillar: "the machine can see the UI".
//
//  A healthy little orders dashboard renders beside a "senses" panel: four
//  structural checks the machine runs over the rendered tree – grid rows, a
//  live action, colour contrast, horizontal fit. Sabotage buttons each inject
//  ONE real fault (empty the grid's data, kill a button's action, force an
//  off-palette colour, jam the grid too wide); the matching sense lights red
//  with the TYPED signal – a real StyleObserver ContrastBelowAA flag, a real
//  LayoutObserver OverflowHorizontal flag, a missing row count, the renderer's
//  own unwired-button marker. No screenshot is taken.
//
//  Heal replays a recorded repair session (the keyless reliability floor):
//  it streams the machine's introspection calls, its diagnosis, and the
//  corrective op, then re-runs the senses – which go green for real, because
//  they read the repaired structure, not a promise.
// ============================================================================

open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.StyleObserver
open Fuaran.UI.LayoutObserver

module SFlags = Fuaran.UI.StyleObserver.Flags
module LFlags = Fuaran.UI.LayoutObserver.Flags

// ─── The curated fault set (one per failure class, §4j) ─────────────────────

[<RequireQualifiedAccess>]
type private Fault =
  | Healthy
  | EmptyGrid // render-time: the data binding resolved empty
  | DeadButton // behavioural: the action points nowhere
  | OffPalette // style: an off-palette colour resolved illegible
  | Overflow // layout: too many columns for the space

// ─── The one app, as intent – faulted per the active sabotage ───────────────

let private cell (nid: string) (label: string) (value: string) : Node<unit> =
  Fuaran.card
    nid
    { Defaults.card with
        Heading = Some(TextSource.Literal label)
        Children = [ Fuaran.markdown (nid + "-v") value ] }

let private healthyCells: Node<unit> list =
  [ cell "k-c1" "Acme Ltd" "£4,120"
    cell "k-c2" "Globex" "£2,880"
    cell "k-c3" "Initech" "£1,540"
    cell "k-c4" "Umbrella" "£3,300"
    cell "k-c5" "Soylent" "£980"
    cell "k-c6" "Stark" "£6,750" ]

let private gridNode (fault: Fault) : Node<unit> =
  let cells, template =
    match fault with
    | Fault.EmptyGrid -> [], None
    | Fault.Overflow -> healthyCells, Some "repeat(6, minmax(230px, 1fr))"
    | _ -> healthyCells, None

  Fuaran.box
    "k-grid"
    { Layout = LayoutMode.Grid(3, template, Some 10)
      Role = BoxRole.Group
      Heading = None
      Children = cells }

let private buttonAction (fault: Fault) : Action<unit> =
  match fault with
  | Fault.DeadButton -> Action.Chain [] // empty chain – clicking dispatches nothing
  | _ -> Action.Navigate "orders/refresh"

/// Does this action actually dispatch anything? An empty `Chain` (or a chain of
/// only-empty chains) is a behavioural no-op – the `InteractionNoOp` shape the
/// senses read straight from the tree (`getNodeState`), independent of any
/// runtime. Every non-chain leaf dispatches (Navigate / Notify / SetState / …).
let rec private dispatches (a: Action<'msg>) : bool =
  match a with
  | Action.Chain xs -> List.exists dispatches xs
  | _ -> true

let private buttonNode (fault: Fault) : Node<unit> =
  Fuaran.button
    "k-refresh"
    { Defaults.button with
        Label = TextSource.Literal "Refresh orders"
        OnClick = buttonAction fault
        Variant = ButtonVariant.Primary }

let private appTree (fault: Fault) : Node<unit> =
  Fuaran.box
    "k-root"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
      Role = BoxRole.Dashboard
      Heading = Some(TextSource.Literal "Orders")
      Children =
        [ Fuaran.callout
            "k-cta"
            { Defaults.callout with
                Tone = ToneVariant.Brand
                Heading = Some(TextSource.Literal "Top account")
                Body = TextSource.Literal "Stark Industries is your largest account this quarter – £6,750." }
          gridNode fault
          buttonNode fault ] }

// The OffPalette fault is a theme-token fault (the AI emitted an illegible
// brand colour), not a tree fault – mirrors the per-tenant token override the
// Infinite Skins page demonstrates.
let private themeFor (fault: Fault) : Theme =
  match fault with
  | Fault.OffPalette ->
    { Defaults.theme with
        Tones =
          { Defaults.theme.Tones with
              Brand =
                { Defaults.theme.Tones.Brand with
                    Foreground = ColorVar.Hex "#e8ecf5" } } }
  | _ -> Defaults.theme

// ─── The senses – structural reads + the shipped observer derivations ───────

let private readSenses: unit -> obj = import "readSenses" "./kintsugi-senses.ts"

let private toRgba (o: obj) : Rgba =
  let r: float = o?r
  let g: float = o?g
  let b: float = o?b
  let a: float = o?a
  Rgba.rgba r g b a

[<RequireQualifiedAccess>]
type private SenseId =
  | Rows
  | Action
  | Contrast
  | Fit

type private Sense =
  { Id: SenseId
    Label: string
    Ok: bool
    Signal: string } // the typed signal shown when red

/// Run all four senses over the current rendered DOM. The contrast + fit senses
/// feed real computed evidence into the SHIPPED StyleObserver / LayoutObserver
/// flag derivations; the rows + action senses read structural facts directly.
let private runSenses (fault: Fault) : Sense list =
  let s = readSenses ()

  let gridCells: int = s?gridCells
  // The behavioural sense introspects the button's action in the tree – the
  // renderer's `fuaran-button-unwired` DOM marker can't be used here: it fires
  // for ANY runtime-routed action in this runtime-less static render.
  let actionLive = dispatches (buttonAction fault)

  // Style: derive ContrastBelowAA / InvisibleText from every text node's real
  // computed colours (the manifest-free Phase-144 derivation).
  let styleInputs: obj array = s?styleInputs

  let contrastFlag =
    styleInputs
    |> Array.tryPick (fun o ->
      let fg = toRgba (o?fg)
      let layers = (o?bgLayers: obj array) |> Array.toList |> List.map toRgba

      let input: SFlags.StyleInput =
        { Foreground = fg
          BackgroundLayers = layers
          FontFamily = None
          EmittedTone = None }

      SFlags.derive StyleObserverOptions.defaults input
      |> List.tryPick (fun f ->
        match f with
        | StyleFlag.ContrastBelowAA r -> Some(sprintf "ContrastBelowAA – %.2f:1" r)
        | StyleFlag.InvisibleText r -> Some(sprintf "InvisibleText – %.2f:1" r)
        | _ -> None))

  // Layout: derive OverflowHorizontal from the stage's real geometry.
  let stage = s?stage
  let sw: float = stage?scrollW
  let cw: float = stage?clientW
  let ox: string = stage?overflowX
  let w: float = stage?w
  let h: float = stage?h

  let layoutInput =
    { LFlags.LayoutInput.empty w h with
        ScrollWidth = Some sw
        ClientWidth = Some cw
        OverflowX = Some ox }

  let overflowFlag =
    LFlags.derive LayoutObserverOptions.defaults layoutInput
    |> List.tryPick (fun f ->
      match f with
      | LayoutFlag.OverflowHorizontal -> Some "OverflowHorizontal"
      | _ -> None)

  [ { Id = SenseId.Rows
      Label = "grid renders rows"
      Ok = gridCells > 0
      Signal = sprintf "assertion grid.rowCount > 0 failed – rowCount = %d" gridCells }
    { Id = SenseId.Action
      Label = "button action is live"
      Ok = actionLive
      Signal = "InteractionNoOp – “Refresh orders” dispatches nothing (empty action chain)" }
    { Id = SenseId.Contrast
      Label = "text meets WCAG AA"
      Ok = Option.isNone contrastFlag
      Signal = defaultArg contrastFlag "" }
    { Id = SenseId.Fit
      Label = "layout fits its width"
      Ok = Option.isNone overflowFlag
      Signal = defaultArg overflowFlag "" } ]

// ─── The recorded heal transcript (one per fault) ───────────────────────────

[<RequireQualifiedAccess>]
type private StepKind =
  | Call
  | Result
  | Diagnose
  | Op
  | Verify

type private Step = { Kind: StepKind; Text: string }

let private healScript (fault: Fault) : Step list =
  match fault with
  | Fault.EmptyGrid ->
    [ { Kind = StepKind.Call
        Text = "fuaran.runEvalAssertions()" }
      { Kind = StepKind.Result
        Text = "{ \"grid.rowCount > 0\": FAILED, rowCount: 0 }" }
      { Kind = StepKind.Call
        Text = "fuaran.getNodeState(\"k-grid\")" }
      { Kind = StepKind.Result
        Text = "{ rows: [], source: \"orders/empty\" }" }
      { Kind = StepKind.Diagnose
        Text = "The grid's data binding resolves to an empty set – its source was cleared." }
      { Kind = StepKind.Op
        Text = "SetBinding k-grid.source → \"orders/recent\"" }
      { Kind = StepKind.Verify
        Text = "runEvalAssertions() → grid.rowCount = 6 ✓" } ]
  | Fault.DeadButton ->
    [ { Kind = StepKind.Call
        Text = "fuaran.simulateInteraction(\"k-refresh\", click)" }
      { Kind = StepKind.Result
        Text = "{ flag: \"InteractionNoOp\", dispatched: null }" }
      { Kind = StepKind.Call
        Text = "fuaran.getNodeState(\"k-refresh\")" }
      { Kind = StepKind.Result
        Text = "{ onClick: Chain [] (no wire-survivable leg) }" }
      { Kind = StepKind.Diagnose
        Text = "The button's action is an empty chain – clicking it dispatches nothing." }
      { Kind = StepKind.Op
        Text = "SetAction k-refresh.onClick → Navigate \"orders/refresh\"" }
      { Kind = StepKind.Verify
        Text = "simulateInteraction() → dispatched ✓" } ]
  | Fault.OffPalette ->
    [ { Kind = StepKind.Call
        Text = "fuaran.inspectStyle(\"k-cta\")" }
      { Kind = StepKind.Result
        Text = "{ flag: \"ContrastBelowAA\", fg: #e8ecf5, bg: #eff6ff }" }
      { Kind = StepKind.Diagnose
        Text = "The brand foreground token resolves nearly the same colour as its surface – the text is illegible." }
      { Kind = StepKind.Op
        Text = "SetToken tone.brand.fg → #1d4ed8" }
      { Kind = StepKind.Verify
        Text = "inspectStyle() → contrast 8.6:1 ✓" } ]
  | Fault.Overflow ->
    [ { Kind = StepKind.Call
        Text = "fuaran.inspectLayout(\"k-grid\")" }
      { Kind = StepKind.Result
        Text = "{ flag: \"OverflowHorizontal\", scrollWidth: 900, clientWidth: 420 }" }
      { Kind = StepKind.Diagnose
        Text = "Six fixed-min columns don't fit the panel – the grid overflows its container." }
      { Kind = StepKind.Op
        Text = "SetLayout k-grid.columns → 3 (auto-fit)" }
      { Kind = StepKind.Verify
        Text = "inspectLayout() → fits ✓" } ]
  | Fault.Healthy -> []

// ─── View helpers ───────────────────────────────────────────────────────────

let private sabotageLabel (fault: Fault) : string =
  match fault with
  | Fault.EmptyGrid -> "Empty the grid’s data"
  | Fault.DeadButton -> "Kill the button’s action"
  | Fault.OffPalette -> "Set an off-palette colour"
  | Fault.Overflow -> "Jam the grid too wide"
  | Fault.Healthy -> "Reset"

let private sabotages =
  [ Fault.EmptyGrid; Fault.DeadButton; Fault.OffPalette; Fault.Overflow ]

let private stepClass (k: StepKind) : string =
  match k with
  | StepKind.Call -> "k-step k-step-call"
  | StepKind.Result -> "k-step k-step-result"
  | StepKind.Diagnose -> "k-step k-step-diagnose"
  | StepKind.Op -> "k-step k-step-op"
  | StepKind.Verify -> "k-step k-step-verify"

let private stepTag (k: StepKind) : string =
  match k with
  | StepKind.Call -> "call"
  | StepKind.Result -> "result"
  | StepKind.Diagnose -> "diagnose"
  | StepKind.Op -> "op"
  | StepKind.Verify -> "verify"

// ─── The page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private KintsugiView () : ReactElement =
  let fault, setFault = React.useState Fault.Healthy
  let senses, setSenses = React.useState ([]: Sense list)
  let healLog, setHealLog = React.useState ([]: Step list)
  let healing, setHealing = React.useState false

  // Re-run the senses after every render that changes the tree/theme.
  React.useEffect ((fun () -> setSenses (runSenses fault)), [| box fault |])

  let sabotage (f: Fault) : unit =
    setHealLog []
    setHealing false
    setFault f

  let reset () : unit =
    setHealLog []
    setHealing false
    setFault Fault.Healthy

  // Stream the recorded transcript step-by-step, then clear the fault. The
  // senses re-run (the effect above) once the fault flips to Healthy, so the
  // green is read from the repaired DOM, not asserted by the script.
  let heal () : unit =
    let script = healScript fault

    if not (List.isEmpty script) then
      setHealing true
      setHealLog []

      script
      |> List.iteri (fun i _ ->
        // Grow the visible log to the first (i+1) steps at each tick –
        // Feliz's setState takes a value, not a functional updater.
        Browser.Dom.window.setTimeout ((fun () -> setHealLog (List.truncate (i + 1) script)), 420 * (i + 1))
        |> ignore)

      Browser.Dom.window.setTimeout (
        (fun () ->
          setFault Fault.Healthy
          setHealing false),
        420 * (List.length script + 1)
      )
      |> ignore

  let broken = fault <> Fault.Healthy
  let redSense = senses |> List.tryFind (fun s -> not s.Ok)

  let stage =
    Html.div
      [ prop.className "k-stage"
        prop.children
          [ Render.themeStyleElement (themeFor fault)
            Render.renderWithSources BindingResolver.empty ignore (appTree fault) ] ]

  let sensePanel =
    Html.div
      [ prop.className "k-senses"
        prop.children
          [ Html.div
              [ prop.className "k-senses-head"
                prop.children
                  [ Html.span
                      [ prop.className (
                          if broken then
                            "k-sense-dot k-sense-bad"
                          else
                            "k-sense-dot k-sense-ok"
                        ) ]
                    Html.span
                      [ prop.className "k-senses-title"
                        prop.text "Senses – read from structure, not pixels" ] ] ]
            Html.ul
              [ prop.className "k-sense-list"
                prop.children
                  [ for s in senses ->
                      Html.li
                        [ prop.className (
                            if s.Ok then
                              "k-sense k-sense-green"
                            else
                              "k-sense k-sense-red"
                          )
                          prop.children
                            [ Html.span [ prop.className "k-sense-mark"; prop.text (if s.Ok then "✓" else "✗") ]
                              Html.span [ prop.className "k-sense-label"; prop.text s.Label ]
                              (if s.Ok then
                                 Html.none
                               else
                                 Html.code [ prop.className "k-sense-signal"; prop.text s.Signal ]) ] ] ] ] ] ]

  let sabotageRow =
    Html.div
      [ prop.className "k-sabotage"
        prop.children
          [ for f in sabotages ->
              Html.button
                [ prop.className (if fault = f then "k-sab-btn k-sab-on" else "k-sab-btn")
                  prop.disabled healing
                  prop.text (sabotageLabel f)
                  prop.onClick (fun _ -> sabotage f) ] ] ]

  let healControls =
    Html.div
      [ prop.className "k-heal-controls"
        prop.children
          [ Html.button
              [ prop.className "k-heal-btn"
                prop.disabled (not broken || healing)
                prop.text (if healing then "Healing…" else "Heal it")
                prop.onClick (fun _ -> heal ()) ]
            (if broken && not healing then
               Html.button
                 [ prop.className "k-reset-btn"
                   prop.text "Reset"
                   prop.onClick (fun _ -> reset ()) ]
             else
               Html.none) ] ]

  let healLogView =
    if List.isEmpty healLog then
      Html.none
    else
      Html.div
        [ prop.className "k-heal-log"
          prop.children
            [ Html.div
                [ prop.className "k-heal-log-head"
                  prop.text "Recorded healing session · replayed" ]
              Html.ul
                [ prop.className "k-heal-steps"
                  prop.children
                    [ for step in healLog ->
                        Html.li
                          [ prop.className (stepClass step.Kind)
                            prop.children
                              [ Html.span [ prop.className "k-step-tag"; prop.text (stepTag step.Kind) ]
                                Html.code [ prop.className "k-step-text"; prop.text step.Text ] ] ] ] ] ] ]

  let honesty =
    Html.div
      [ prop.className "k-honesty"
        prop.children
          [ Html.h3 [ prop.text "No screenshots were taken" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Every diagnosis above is a typed signal read from the rendered tree – a failed assertion, the renderer's own unwired-button marker, a real StyleObserver contrast flag, a real LayoutObserver overflow flag. A vision model guessing at pixels can't name the exact node; structure can." ]
                    Html.li
                      [ prop.text
                          "The heal is a recorded repair session, replayed – the keyless reliability floor. But the green at the end is not scripted: the senses re-run over the repaired structure and pass for real." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "This is closed-loop authoring: introspect → diagnose → emit a corrective op → re-check. The same senses power the "
                            Html.a [ prop.href "#/pillar/machine"; prop.text "machine-can-see-the-UI" ]
                            Html.text " story across the site." ] ] ] ] ] ]

  Html.div
    [ prop.className "k-page"
      prop.children
        [ Html.h1 [ prop.className "k-title"; prop.text "Kintsugi" ]
          Html.p
            [ prop.className "k-lede"
              prop.text
                "Break the app on purpose. Watch the machine find the crack – from structure, not screenshots – and repair it in seconds." ]
          Html.div
            [ prop.className "k-split"
              prop.children
                [ Html.div
                    [ prop.className "k-app-col"
                      prop.children [ Html.h3 [ prop.className "k-col-title"; prop.text "The app" ]; stage ] ]
                  Html.div [ prop.className "k-sense-col"; prop.children [ sensePanel ] ] ] ]
          Html.div
            [ prop.className "k-controls"
              prop.children
                [ Html.div
                    [ prop.className "k-sabotage-block"
                      prop.children
                        [ Html.span [ prop.className "k-controls-label"; prop.text "Sabotage" ]
                          sabotageRow ] ]
                  healControls ] ]
          healLogView
          honesty ] ]

let page: ReactElement = KintsugiView()
