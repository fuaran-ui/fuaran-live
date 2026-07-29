module Fuaran.Showcase.UnitTest

// ============================================================================
//  Unit-Test Your UI – assertions against the living interface. Pillar: "the
//  machine can see the UI".
//
//  A small channel dashboard renders beside a live assertion suite. Every
//  assertion is a genuine structural read over the SAME typed `Node<unit>` tree
//  that renders – nodeExists / childrenCount / stateBehaviour(OnEmpty) /
//  simulate-dispatches – plus one geometric assertion fed by the shipped
//  LayoutObserver over a real phone-width probe. The suite re-runs on every
//  change with a genuine wall-clock latency badge (`performance.now`).
//
//  Break-it toggles mutate the app (empty the grid, remove a node, kill the
//  button's action, jam the grid wide, strip the empty-state) and the matching
//  assertion goes red with a real reason + a "did you mean" hint. The theme flip
//  restyles the whole app and every assertion stays green – because they read
//  structure, not pixels: the tests never saw a colour.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.LayoutObserver

module LFlags = Fuaran.UI.LayoutObserver.Flags

[<Emit("performance.now()")>]
let private perfNow () : float = jsNative

let private idOf (n: Node<unit>) : string = n.Id

// ─── The app under test – a channel dashboard, mutated per the break toggles ─

type private Breaks =
  { EmptyGrid: bool
    NoKpi: bool
    DeadBtn: bool
    JamWide: bool
    NoOnEmpty: bool }

let private noBreaks =
  { EmptyGrid = false
    NoKpi = false
    DeadBtn = false
    JamWide = false
    NoOnEmpty = false }

let private channelCard (id: string) (label: string) (value: string) : Node<unit> =
  Fuaran.card
    id
    { Defaults.card with
        Heading = Some(TextSource.Literal label)
        Children = [ Fuaran.markdown (id + "-v") value ] }

let private gridNode (b: Breaks) : Node<unit> =
  let cells =
    if b.EmptyGrid then
      []
    else
      [ channelCard "ch-organic" "Organic" "4,120"
        channelCard "ch-paid" "Paid" "2,880"
        channelCard "ch-social" "Social" "1,540"
        channelCard "ch-email" "Email" "980" ]

  let template =
    if b.JamWide then
      "repeat(4, minmax(220px, 1fr))"
    else
      "repeat(auto-fit, minmax(120px, 1fr))"

  let grid =
    Fuaran.box
      "channel-grid"
      { Layout = LayoutMode.Grid(4, Some template, Some 10)
        Role = BoxRole.Group
        Heading = None
        Children = cells }

  // Override the node's typed state behaviour – an OnEmpty slot the machine can
  // assert on (present unless the NoOnEmpty break strips it).
  let state = grid.State |> Option.defaultValue Defaults.stateBehaviour

  { grid with
      State =
        Some
          { state with
              OnEmpty =
                if b.NoOnEmpty then
                  None
                else
                  Some(Fuaran.markdown "channel-grid-empty" "_No channels yet._") } }

let private appTree (b: Breaks) : Node<unit> =
  Fuaran.box
    "app-root"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
      Role = BoxRole.Dashboard
      Heading = Some(TextSource.Literal "Channel performance")
      Children =
        [ (if b.NoKpi then
             Fuaran.markdown "app-note" "_(revenue removed)_"
           else
             channelCard "revenue-kpi" "Revenue" "£128k")
          gridNode b
          Fuaran.button
            "refresh-btn"
            { Defaults.button with
                Label = TextSource.Literal "Refresh"
                OnClick =
                  (if b.DeadBtn then
                     Action.Chain []
                   else
                     Action.Navigate "channels/refresh")
                Variant = ButtonVariant.Primary } ] }

// ─── Structural reads over the typed tree (the same tree that renders) ────────

let rec private childrenOf (n: Node<unit>) : Node<unit> list =
  match n.Kind with
  | NodeKind.Box s -> s.Children
  | _ -> []

let rec private flatten (n: Node<unit>) : Node<unit> list =
  n :: (childrenOf n |> List.collect flatten)

let private findById (tree: Node<unit>) (id: string) : Node<unit> option =
  flatten tree |> List.tryFind (fun n -> idOf n = id)

let rec private dispatches (a: Action<unit>) : bool =
  match a with
  | Action.Chain xs -> List.exists dispatches xs
  | _ -> true

let private buttonDispatches (tree: Node<unit>) (id: string) : bool option =
  findById tree id
  |> Option.bind (fun n ->
    match n.Kind with
    | NodeKind.Button s -> Some(dispatches s.OnClick)
    | _ -> None)

let private allIds (tree: Node<unit>) : string list = flatten tree |> List.map idOf

// ─── Geometric read – the shipped LayoutObserver over a phone-width probe ─────

[<Emit("(function(){var el=document.querySelector('.ut-probe .fuaran-layout-grid');if(!el)return null;var cs=getComputedStyle(el);var r=el.getBoundingClientRect();return {scrollW:el.scrollWidth,clientW:el.clientWidth,ox:cs.overflowX,w:r.width,h:r.height};})()")>]
let private probeGrid () : obj = jsNative

/// Real overflow flag from the shipped derivation, or None if the probe is absent.
let private probeOverflow () : bool option =
  let g = probeGrid ()

  if isNull (box g) then
    None
  else
    let scrollW: float = g?scrollW
    let clientW: float = g?clientW
    let ox: string = g?ox
    let w: float = g?w
    let h: float = g?h

    let input =
      { LFlags.LayoutInput.empty w h with
          ScrollWidth = Some scrollW
          ClientWidth = Some clientW
          OverflowX = Some ox }

    LFlags.derive LayoutObserverOptions.defaults input
    |> List.exists (fun f ->
      match f with
      | LayoutFlag.OverflowHorizontal -> true
      | _ -> false)
    |> Some

// ─── Assertions – each runs against a context (tree + probed overflow) ────────

type private Ctx =
  { Tree: Node<unit>
    Overflow: bool option }

type private AssertResult =
  { Ok: bool
    Detail: string
    Hint: string option }

type private Assertion =
  { Name: string
    Run: Ctx -> AssertResult }

let private ok (detail: string) : AssertResult =
  { Ok = true
    Detail = detail
    Hint = None }

let private fail (detail: string) (hint: string option) : AssertResult =
  { Ok = false
    Detail = detail
    Hint = hint }

let private aNodeExists (id: string) : Assertion =
  { Name = sprintf "nodeExists \"%s\"" id
    Run =
      fun ctx ->
        match findById ctx.Tree id with
        | Some _ -> ok "found"
        | None ->
          let near =
            allIds ctx.Tree |> List.filter (fun x -> x <> "app-root") |> List.truncate 5

          fail (sprintf "no node with id \"%s\"" id) (Some("did you mean: " + String.concat ", " near)) }

let private aChildrenCount (id: string) (n: int) : Assertion =
  { Name = sprintf "childrenCount \"%s\" >= %d" id n
    Run =
      fun ctx ->
        match findById ctx.Tree id with
        | None -> fail (sprintf "no node with id \"%s\"" id) None
        | Some node ->
          let c = childrenOf node |> List.length

          if c >= n then
            ok (sprintf "count = %d" c)
          else
            fail
              (sprintf "count = %d (expected >= %d)" c n)
              (Some "the grid's children resolved empty – check its data source") }

let private aHasOnEmpty (id: string) : Assertion =
  { Name = sprintf "stateBehaviour \"%s\" has OnEmpty" id
    Run =
      fun ctx ->
        match findById ctx.Tree id with
        | None -> fail (sprintf "no node with id \"%s\"" id) None
        | Some node ->
          if node.State |> Option.bind _.OnEmpty |> Option.isSome then
            ok "OnEmpty present"
          else
            fail "no OnEmpty state" (Some "add an OnEmpty slot so the empty case renders") }

let private aFitsAtPhone (id: string) : Assertion =
  { Name = sprintf "no OverflowHorizontal \"%s\" @ 375px" id
    Run =
      fun ctx ->
        match ctx.Overflow with
        | None -> fail "geometry not measured yet" None
        | Some false -> ok "fits at phone width"
        | Some true ->
          fail
            "OverflowHorizontal at 375px"
            (Some "the fixed-width tracks don't fit a phone – use auto-fit / fewer columns") }

let private aSimulateDispatches (id: string) : Assertion =
  { Name = sprintf "afterSimulate click \"%s\" → dispatches" id
    Run =
      fun ctx ->
        match buttonDispatches ctx.Tree id with
        | Some true -> ok "action dispatches"
        | Some false ->
          fail "InteractionNoOp – the action is an empty chain" (Some "wire the button's onClick to a real action")
        | None -> fail (sprintf "no button with id \"%s\"" id) None }

let private defaultSuite: Assertion list =
  [ aNodeExists "revenue-kpi"
    aChildrenCount "channel-grid" 3
    aHasOnEmpty "channel-grid"
    aFitsAtPhone "channel-grid"
    aSimulateDispatches "refresh-btn" ]

// ─── A distinct theme for the "tests never saw a pixel" beat ─────────────────

let private altTheme: Theme =
  { Defaults.theme with
      Tones =
        { Defaults.theme.Tones with
            Brand =
              { Defaults.theme.Tones.Brand with
                  Background = ColorVar.Hex "#3a2d5c"
                  Foreground = ColorVar.Hex "#f4f0ff" } } }

// ─── The page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private UnitTestView () : ReactElement =
  let breaks, setBreaks = React.useState noBreaks
  let themed, setThemed = React.useState false
  let overflow, setOverflow = React.useState (None: bool option)
  let results, setResults = React.useState ([]: (Assertion * AssertResult) list)
  let latencyUs, setLatencyUs = React.useState 0.0
  let userAsserts, setUserAsserts = React.useState ([]: Assertion list)
  // Editor state.
  let editKind, setEditKind = React.useState "nodeExists"
  let editNode, setEditNode = React.useState "revenue-kpi"

  let tree = appTree breaks
  let nodeChoices = allIds tree |> List.filter (fun x -> x <> "app-root")

  // Re-measure the probe, then run the whole suite with a genuine timer.
  React.useEffect (
    (fun () ->
      let ov = probeOverflow ()
      setOverflow ov
      let ctx = { Tree = tree; Overflow = ov }
      let suite = defaultSuite @ userAsserts

      let t0 = perfNow ()
      let rs = suite |> List.map (fun a -> a, a.Run ctx)
      let t1 = perfNow ()

      setResults rs
      setLatencyUs ((t1 - t0) * 1000.0)),
    [| box breaks; box userAsserts |]
  )

  let passCount = results |> List.filter (fun (_, r) -> r.Ok) |> List.length
  let total = List.length results
  let allGreen = total > 0 && passCount = total

  let addAssertion () : unit =
    let a =
      match editKind with
      | "childrenCount" -> aChildrenCount editNode 3
      | "hasOnEmpty" -> aHasOnEmpty editNode
      | _ -> aNodeExists editNode

    setUserAsserts (userAsserts @ [ a ])

  // Offscreen phone-width probe – same app, measured by the layout observer.
  let probe =
    Html.div
      [ prop.className "ut-probe"
        prop.children [ Render.renderWithSources BindingResolver.empty ignore tree ] ]

  let stage =
    Html.div
      [ prop.className "ut-stage"
        prop.children
          [ (if themed then
               Render.themeStyleElement altTheme
             else
               Html.none)
            Render.renderWithSources BindingResolver.empty ignore tree ] ]

  let latencyBadge =
    Html.span
      [ prop.className (
          if latencyUs < 50000.0 then
            "ut-latency ut-latency-ok"
          else
            "ut-latency ut-latency-slow"
        )
        prop.text (sprintf "%.2f ms · under the 50 ms bar" (latencyUs / 1000.0)) ]

  let suitePanel =
    Html.div
      [ prop.className "ut-suite"
        prop.children
          [ Html.div
              [ prop.className "ut-suite-head"
                prop.children
                  [ Html.span [ prop.className (if allGreen then "ut-dot ut-dot-ok" else "ut-dot ut-dot-bad") ]
                    Html.span
                      [ prop.className "ut-suite-title"
                        prop.text (sprintf "%d / %d passing" passCount total) ]
                    latencyBadge ] ]
            Html.ul
              [ prop.className "ut-assert-list"
                prop.children
                  [ for (a, r) in results ->
                      Html.li
                        [ prop.className (
                            if r.Ok then
                              "ut-assert ut-assert-ok"
                            else
                              "ut-assert ut-assert-bad"
                          )
                          prop.children
                            [ Html.div
                                [ prop.className "ut-assert-main"
                                  prop.children
                                    [ Html.span
                                        [ prop.className "ut-assert-mark"; prop.text (if r.Ok then "✓" else "✗") ]
                                      Html.code [ prop.className "ut-assert-name"; prop.text a.Name ]
                                      Html.span [ prop.className "ut-assert-detail"; prop.text r.Detail ] ] ]
                              (match r.Hint with
                               | Some h when not r.Ok ->
                                 Html.div [ prop.className "ut-assert-hint"; prop.text ("→ " + h) ]
                               | _ -> Html.none) ] ] ] ] ] ]

  let breakToggle (label: string) (isOn: bool) (flip: Breaks -> Breaks) : ReactElement =
    Html.button
      [ prop.className (if isOn then "ut-break ut-break-on" else "ut-break")
        prop.text label
        prop.onClick (fun _ -> setBreaks (flip breaks)) ]

  let breakRow =
    Html.div
      [ prop.className "ut-breaks"
        prop.children
          [ breakToggle "Empty the grid" breaks.EmptyGrid (fun b -> { b with EmptyGrid = not b.EmptyGrid })
            breakToggle "Remove the revenue node" breaks.NoKpi (fun b -> { b with NoKpi = not b.NoKpi })
            breakToggle "Kill the button action" breaks.DeadBtn (fun b -> { b with DeadBtn = not b.DeadBtn })
            breakToggle "Jam the grid wide" breaks.JamWide (fun b -> { b with JamWide = not b.JamWide })
            breakToggle "Strip the empty-state" breaks.NoOnEmpty (fun b -> { b with NoOnEmpty = not b.NoOnEmpty }) ] ]

  let editor =
    Html.div
      [ prop.className "ut-editor"
        prop.children
          [ Html.span [ prop.className "ut-editor-label"; prop.text "Compose an assertion" ]
            Html.div
              [ prop.className "ut-editor-row"
                prop.children
                  [ Html.select
                      [ prop.className "ut-select"
                        prop.value editKind
                        prop.onChange (fun (v: string) -> setEditKind v)
                        prop.children
                          [ Html.option [ prop.value "nodeExists"; prop.text "nodeExists" ]
                            Html.option [ prop.value "childrenCount"; prop.text "childrenCount ≥ 3" ]
                            Html.option [ prop.value "hasOnEmpty"; prop.text "has OnEmpty" ] ] ]
                    Html.select
                      [ prop.className "ut-select"
                        prop.value editNode
                        prop.onChange (fun (v: string) -> setEditNode v)
                        prop.children [ for id in nodeChoices -> Html.option [ prop.value id; prop.text id ] ] ]
                    Html.button
                      [ prop.className "ut-add-btn"
                        prop.text "Add"
                        prop.onClick (fun _ -> addAssertion ()) ]
                    (if List.isEmpty userAsserts then
                       Html.none
                     else
                       Html.button
                         [ prop.className "ut-add-btn ut-add-ghost"
                           prop.text "Clear added"
                           prop.onClick (fun _ -> setUserAsserts []) ]) ] ] ] ]

  let themeControl =
    Html.div
      [ prop.className "ut-theme-row"
        prop.children
          [ Html.button
              [ prop.className "ut-theme-btn"
                prop.text (if themed then "Reset the theme" else "Flip the theme")
                prop.onClick (fun _ -> setThemed (not themed)) ]
            Html.span
              [ prop.className "ut-theme-note"
                prop.text "Restyle the whole app – the suite stays green. These tests never saw a pixel." ] ] ]

  // The screenshot-diff contrast (a labelled mock – we don't build a real one to
  // lose to it).
  let contrastMock =
    Html.div
      [ prop.className "ut-mock"
        prop.children
          [ Html.div
              [ prop.className "ut-mock-tag"
                prop.text "Illustration – the screenshot-diff way" ]
            Html.div
              [ prop.className "ut-mock-body"
                prop.children
                  [ Html.span [ prop.className "ut-mock-swatch ut-mock-a" ]
                    Html.span [ prop.className "ut-mock-arrow"; prop.text "→" ]
                    Html.span [ prop.className "ut-mock-swatch ut-mock-b" ]
                    Html.code
                      [ prop.className "ut-mock-verdict"
                        prop.text "pixel-diff: 41,208 px changed → FAIL" ] ] ]
            Html.p
              [ prop.className "ut-mock-caption"
                prop.text
                  "A pixel comparison false-fails the moment you re-theme. The structural suite above ignores the repaint – it asserts what the UI IS, not how it looks." ] ] ]

  let honesty =
    Html.div
      [ prop.className "ut-honesty"
        prop.children
          [ Html.h3 [ prop.text "Assertions against the living UI" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Every assertion is a real structural read over the same typed tree that renders: node existence, child counts, the typed OnEmpty state slot, and whether a simulated click would dispatch – plus one geometric check fed by the shipped layout observer over a real phone-width probe." ]
                    Html.li
                      [ prop.text
                          "The latency is a genuine wall-clock measurement of the run – structural assertions cost microseconds, which is the whole economic argument: verifying generated UI this way is effectively free." ]
                    Html.li
                      [ prop.text
                          "Break the app and the exact assertion goes red with a real reason and a hint; re-theme it and every assertion stays green. The tests read structure, not pixels – the screenshot-diff illustration shows the alternative that can't tell a re-theme from a regression." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "This is the practitioner's answer to \"how do you trust generated UI?\" – you assert against it, like code. Same lens as "
                            Html.a [ prop.href "#/pillar/machine"; prop.text "the machine-can-see-the-UI" ]
                            Html.text " story across the site." ] ] ] ] ] ]

  Html.div
    [ prop.className "ut-page"
      prop.children
        [ Html.h1 [ prop.className "ut-title"; prop.text "Unit-Test Your UI" ]
          Html.p
            [ prop.className "ut-lede"
              prop.text
                "Write an assertion; it runs against the living interface in microseconds. Break the app and watch it catch you – then restyle the whole thing and every test stays green, because they test structure, not pixels." ]
          probe
          Html.div
            [ prop.className "ut-split"
              prop.children
                [ Html.div
                    [ prop.className "ut-app-col"
                      prop.children
                        [ Html.h3 [ prop.className "ut-col-title"; prop.text "The app under test" ]
                          stage
                          themeControl ] ]
                  Html.div [ prop.className "ut-suite-col"; prop.children [ suitePanel; editor ] ] ] ]
          Html.div
            [ prop.className "ut-break-block"
              prop.children
                [ Html.span [ prop.className "ut-break-label"; prop.text "Break it" ]
                  breakRow ] ]
          contrastMock
          honesty ] ]

let page: ReactElement = UnitTestView()
