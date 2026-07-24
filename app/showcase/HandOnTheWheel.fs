module Fuaran.Showcase.HandOnTheWheel

// ============================================================================
//  Hand on the Wheel – a module declares which of its fields an agent may set,
//  by name and by typed space, and the agent drives them directly. Pillar:
//  "the machine can see the UI" (its actuation face – the read/write twin of
//  The Typed Question's read-only contract).
//
//  What the agent does here is two things, no pixels involved:
//    1. INSPECT – reads the module's declared control surface: a machine-
//       readable list of field → space, so it never has to guess from a
//       screenshot which knobs exist or what they accept.
//    2. SET FIELD – sets one declared field by name. A value inside the
//       field's declared space lands (the row + the live module pulse); a
//       value outside it is refused with the space it violated; a name the
//       module never declared is refused with the list of names that exist.
//
//  Honest scope: this page is a SCRIPTED, zero-egress illustration. The real
//  mechanism – the declared-field control surface an agent reads and drives –
//  is a separately-shipped substrate wired into the hands-on tooling; here the
//  "agent's" turns are a canned sequence so the shape is legible with no key
//  and no network. Every value shown is checked against its declared space by
//  the page itself, mirroring the real contract: in-space lands, out-of-space
//  is `decoder-rejected` with the range, an undeclared name is `unknown-field`.
//
//  The pulse is the genuine Apache-tier surface: each live-module card carries
//  a real `data-ai-name` attribute (the address the mechanism targets) and the
//  set field lights up via the renderer's `Motion.PulseDuringLoad` hook.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── The declared control surface (what the agent is allowed to drive) ───────

type private Space =
  | IntRange of int * int
  | OneOf of string list

type private FieldDecl =
  { Name: string
    Space: Space
    Initial: string }

/// The module under the wheel: a rollout console with three declared fields.
let private fields: FieldDecl list =
  [ { Name = "environment"
      Space = OneOf [ "staging"; "production" ]
      Initial = "staging" }
    { Name = "replicas"
      Space = IntRange(1, 20)
      Initial = "2" }
    { Name = "canaryShare"
      Space = IntRange(0, 100)
      Initial = "5" } ]

let private spaceText (s: Space) : string =
  match s with
  | IntRange(lo, hi) -> sprintf "integer %d–%d" lo hi
  | OneOf opts -> "one of " + String.concat " | " opts

let private inSpace (s: Space) (v: string) : bool =
  match s with
  | IntRange(lo, hi) ->
    match System.Int32.TryParse v with
    | true, n -> n >= lo && n <= hi
    | _ -> false
  | OneOf opts -> List.contains v opts

// ─── The agent's canned session (a fixed script – no model, no network) ──────

type private Call =
  | Inspect
  | SetField of field: string * value: string

let private script: Call list =
  [ Inspect
    SetField("environment", "production")
    SetField("replicas", "6")
    SetField("canaryShare", "25")
    SetField("canaryShare", "250") // outside 0–100 → decoder-rejected
    SetField("adminOverride", "grant") ] // never declared → unknown-field

/// One resolved turn: the call, its machine-readable status + detail, the field
/// that should pulse (Some only on a successful set), and the module snapshot
/// AFTER the turn.
type private Turn =
  { Call: Call
    Status: string
    Detail: string
    Pulse: string option
    Values: Map<string, string> }

let private snapshotJson (values: Map<string, string>) : string =
  let body =
    fields
    |> List.map (fun f -> sprintf "    \"%s\": \"%s\"" f.Name (Map.find f.Name values))
    |> String.concat ",\n"

  sprintf "{\n  \"moduleId\": \"rollout-console\",\n  \"fields\": {\n%s\n  }\n}" body

/// Fold the script into resolved turns, threading the live value map. The same
/// space check the real decoder runs decides ok / decoder-rejected here.
let private turns: Turn list =
  let initial = fields |> List.map (fun f -> f.Name, f.Initial) |> Map.ofList

  let step (values: Map<string, string>, acc: Turn list) (call: Call) =
    match call with
    | Inspect ->
      values,
      { Call = call
        Status = "inspected"
        Detail = snapshotJson values
        Pulse = None
        Values = values }
      :: acc
    | SetField(name, v) ->
      match fields |> List.tryFind (fun f -> f.Name = name) with
      | None ->
        let available = fields |> List.map (fun f -> f.Name) |> String.concat ", "

        values,
        { Call = call
          Status = "unknown-field"
          Detail = sprintf "no field named \"%s\" – the module declares: %s" name available
          Pulse = None
          Values = values }
        :: acc
      | Some d ->
        if inSpace d.Space v then
          let next = Map.add name v values

          next,
          { Call = call
            Status = "ok"
            Detail = sprintf "%s ← %s" name v
            Pulse = Some name
            Values = next }
          :: acc
        else
          values,
          { Call = call
            Status = "decoder-rejected"
            Detail = sprintf "value \"%s\" is outside the declared space (%s)" v (spaceText d.Space)
            Pulse = None
            Values = values }
          :: acc

  List.fold step (initial, []) script |> snd |> List.rev

// ─── Rendering a Fuaran node (exhibit zero – the live module is real data) ───

let private renderStatic (n: Node<'msg>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

// ─── The page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private HandOnTheWheelView () : ReactElement =
  let step, setStep = React.useState 0
  let lastIx = List.length turns - 1
  let current = List.item step turns
  let values = current.Values
  let activeField = current.Pulse
  let atStart = step = 0
  let atEnd = step >= lastIx

  // ── the declared control surface (a machine-readable field → space list) ──
  let controlSurface =
    Html.div
      [ prop.className "how-panel"
        prop.children
          [ Html.h3 [ prop.text "The declared control surface" ]
            Html.p
              [ prop.className "how-muted"
                prop.text
                  "What the agent reads before it touches anything: the fields this module declares an agent may set, each with the typed space it accepts. No screenshot, no guessing which knobs exist – the surface is data, addressed by name." ]
            Html.table
              [ prop.className "how-table"
                prop.children
                  [ Html.thead
                      [ Html.tr
                          [ Html.th [ prop.text "field" ]
                            Html.th [ prop.text "declared space" ]
                            Html.th [ prop.text "value now" ] ] ]
                    Html.tbody
                      [ for f in fields ->
                          let isActive = activeField = Some f.Name

                          Html.tr
                            [ prop.className (if isActive then "how-row how-row-active" else "how-row")
                              prop.custom ("data-ai-name", f.Name)
                              prop.children
                                [ Html.td [ Html.code [ prop.text f.Name ] ]
                                  Html.td [ prop.text (spaceText f.Space) ]
                                  Html.td [ Html.code [ prop.className "how-val"; prop.text (Map.find f.Name values) ] ] ] ] ] ] ] ] ]

  // ── the live module (real Fuaran nodes; the set field pulses) ──
  let moduleCard (f: FieldDecl) : ReactElement =
    let baseNode =
      Fuaran.card
        ("how-fld-" + f.Name)
        { Defaults.card with
            Heading = Some(TextSource.Literal f.Name)
            Children = [ Fuaran.markdown ("how-fldv-" + f.Name) (sprintf "**%s**" (Map.find f.Name values)) ] }
      |> Node.withExtraAttribute "data-ai-name" f.Name

    let node =
      if activeField = Some f.Name then
        { baseNode with
            Motion = Some Motion.PulseDuringLoad }
      else
        baseNode

    renderStatic node

  let liveModule =
    Html.div
      [ prop.className "how-panel"
        prop.children
          [ Html.h3 [ prop.text "The module, right now" ]
            Html.p
              [ prop.className "how-muted"
                prop.text
                  "The rollout console as it stands after the agent's turns so far – a real Fuaran tree, re-rendered each step. Each card carries a data-ai-name attribute: that is the address the agent set, and what lights up when it does." ]
            Html.div
              [ prop.className "how-module"
                prop.children [ for f in fields -> moduleCard f ] ] ] ]

  // ── the agent's session (a scripted inspect → set-field walkthrough) ──
  let turnView (t: Turn) : ReactElement =
    let head =
      match t.Call with
      | Inspect -> "inspect", "reads the declared fields – structure, not pixels"
      | SetField(f, v) -> "set_field", sprintf "%s = %s" f v

    let tool, note = head

    match t.Status with
    | "inspected" ->
      Html.div
        [ prop.className "how-turn"
          prop.children
            [ Html.div
                [ prop.className "how-turn-head"
                  prop.children
                    [ Html.span [ prop.className "how-tool"; prop.text tool ]
                      Html.span [ prop.className "how-turn-note"; prop.text note ] ] ]
              Html.pre [ prop.className "wire-json"; prop.text t.Detail ] ] ]
    | "ok" ->
      Html.div
        [ prop.className "how-turn how-turn-ok"
          prop.children
            [ Html.span [ prop.className "how-ok-mark"; prop.text "✓ set" ]
              Html.code [ prop.text t.Detail ] ] ]
    | status ->
      // decoder-rejected | unknown-field – the machine-readable refusal the
      // agent can read and self-correct from.
      Html.div
        [ prop.className "how-turn"
          prop.children
            [ Html.div
                [ prop.className "how-turn-head"
                  prop.children
                    [ Html.span [ prop.className "how-tool"; prop.text tool ]
                      Html.span [ prop.className "how-turn-note"; prop.text note ] ] ]
              Html.div
                [ prop.className "how-refusal"
                  prop.children
                    [ Html.code [ prop.className "how-refusal-code"; prop.text status ]
                      Html.span [ prop.className "how-refusal-detail"; prop.text t.Detail ] ] ] ] ]

  let session =
    Html.div
      [ prop.className "how-panel"
        prop.children
          [ Html.h3 [ prop.text "The agent's session" ]
            Html.p
              [ prop.className "how-muted"
                prop.text
                  "A scripted sequence of the agent's turns – inspect the surface, then drive fields by name. Advance it a turn at a time: watch a good value land, an out-of-range value get refused with the space it broke, and an undeclared name get refused with the names that exist." ]
            Html.div
              [ prop.className "how-controls"
                prop.children
                  [ Html.button
                      [ prop.className "how-btn how-btn-primary"
                        prop.disabled atEnd
                        prop.text "Advance a turn"
                        prop.onClick (fun _ -> setStep (min lastIx (step + 1))) ]
                    Html.button
                      [ prop.className "how-btn"
                        prop.disabled atStart
                        prop.text "Reset"
                        prop.onClick (fun _ -> setStep 0) ]
                    Html.span
                      [ prop.className "how-step-count"
                        prop.text (sprintf "turn %d / %d" (step + 1) (lastIx + 1)) ] ] ]
            Html.div
              [ prop.className "how-turns"
                prop.children [ for t in List.truncate (step + 1) turns -> turnView t ] ] ] ]

  let honesty =
    Html.div
      [ prop.className "how-honesty"
        prop.children
          [ Html.h3 [ prop.text "A scripted view of a real mechanism" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "The declared control surface is the point: a module says which fields an agent may set, each with a typed space. An agent reads that list and drives the fields by name – it never interprets a screenshot." ]
                    Html.li
                      [ prop.text
                          "Every value here is checked against its declared space, exactly as the real contract does: in-space lands, out-of-space is refused with the range, an undeclared name is refused with the names that exist. A refusal is machine-readable – the agent can read it and correct itself." ]
                    Html.li
                      [ prop.text
                          "Honest scope: this page's agent is a canned script so the shape is legible with zero egress – no key, no network. The live mechanism that lets a real agent read and drive a module's declared fields is a separately-shipped substrate wired into the hands-on tooling; here you are watching its shape, not a live agent." ]
                    Html.li
                      [ prop.text
                          "The pulse is genuine: each live-module card carries a real data-ai-name attribute – the address the mechanism targets – and the set field lights up through the renderer's motion hook. Inspect the element and the machine-addressable name is right there." ] ] ] ] ]

  Html.div
    [ prop.className "how-page"
      prop.children
        [ Html.h1 [ prop.className "how-title"; prop.text "Hand on the Wheel" ]
          Html.p
            [ prop.className "how-lede"
              prop.text
                "An agent shouldn't drive your interface by guessing at pixels. Here a module declares which of its fields an agent may set – by name, each with a typed space – and the agent reads that list, then turns the knobs directly. Good values land; bad ones are refused with the reason." ]
          controlSurface
          liveModule
          session
          honesty ] ]

let page: ReactElement = HandOnTheWheelView()
