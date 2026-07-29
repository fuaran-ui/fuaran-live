module Fuaran.Showcase.Attesor

// ============================================================================
//  Attesor – the reverse of Rosetta. Pillar: "one wire, many worlds".
//
//  Rosetta shows nine languages CONVERGING on one wire; Attesor runs the arrow
//  the other way: paste canonical wire JSON at the top, and read it back as
//  idiomatic source in each of the nine host languages beneath – plus the
//  rendered app itself, because the wire IS an app, not a description of one.
//
//  The projector is the playground's own multi-language source projector
//  (`Fuaran.Live.Projection`, Phase 329 lineage), linked into this entry
//  together with the pure JSON model it walks (JsonValue/JsonHost – no
//  transport, no key machinery; the showcase's no-key-egress guarantee holds).
//
//  Honesty (mirrors docs/PROJECTION_FIDELITY.md): the TypeScript leg is a
//  verified byte-round-trip – the generated source is executed in the
//  conformance harness and asserted to rebuild identical bytes. The other legs
//  are idiomatic projections ("how it would look written in…"), not certified
//  compilers; the footer says so on the page.
//
//  Emitter-lock note: this page hand-authors NO wire. The exemplar is built
//  from the real `Fuaran.*` constructors and encoded by the real
//  `CanonicalJson` at runtime (canon-by-construction); pasted input is decoded
//  by the real strict-plus-lenient `Fuaran.UI.Ops.JsonDecode` and re-encoded
//  canonically before projection, so the projections always walk canonical
//  bytes and a decode reject keeps the last good wire (the site's
//  last-good-tree convention).
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module Decode = Fuaran.UI.Ops.JsonDecode
module Projection = Fuaran.Live.Projection

// ─── small JS interop ────────────────────────────────────────────────────────

[<Emit("JSON.stringify(JSON.parse($0), null, 2)")>]
let private prettyJson (s: string) : string = jsNative

// ─── The exemplar wire (canon-by-construction, never hand-authored) ──────────

let private metric (id: string) (label: string) (value: float) (tone: ToneVariant) : Node<obj> =
  Fuaran.metric
    id
    { Defaults.metric with
        Label = TextSource.Literal label
        Value = Binding.Static(Some value)
        Tone = tone }

let private exemplar: Node<obj> =
  Fuaran.box
    "at-root"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
      Role = BoxRole.Dashboard
      Heading = Some(TextSource.Literal "Team pulse")
      Children =
        [ Fuaran.box
            "at-strip"
            { Layout = LayoutMode.Flex(Orientation.Horizontal, true, Some 12)
              Role = BoxRole.Group
              Heading = None
              Children =
                [ metric "at-deploys" "Deploys this week" 27.0 ToneVariant.Brand
                  metric "at-incidents" "Open incidents" 0.0 ToneVariant.Success ] }
          Fuaran.markdown "at-note" "**Green** across all pipelines."
          Fuaran.callout
            "at-freeze"
            { Defaults.callout with
                Tone = ToneVariant.Info
                Heading = Some(TextSource.Literal "Heads-up")
                Body = TextSource.Literal "Release freeze starts Friday." }
          Fuaran.button
            "at-refresh"
            { Defaults.button with
                Label = TextSource.Literal "Refresh"
                OnClick = Action.Chain []
                Variant = ButtonVariant.Primary } ] }

let private exemplarWire: string = CJson.encodeNode exemplar

// ─── The page ────────────────────────────────────────────────────────────────

let private renderTree (n: Node<obj>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

/// The nine language tabs (the input box IS the JSON, so no JSON tab here).
let private languageTargets: (Projection.Target * string) list =
  Projection.targets |> List.filter (fun (t, _) -> t <> Projection.Target.Json)

[<ReactComponent>]
let private AttesorView () : ReactElement =
  let input, setInput = React.useState (prettyJson exemplarWire)
  // The last GOOD canonical wire + its decoded tree – a reject never clears it.
  let goodWire, setGoodWire = React.useState exemplarWire
  let goodTree, setGoodTree = React.useState exemplar
  let decodeErr, setDecodeErr = React.useState ""
  let target, setTarget = React.useState Projection.Target.FSharp

  let tryAdopt (text: string) : unit =
    setInput text

    match Decode.decodeNodeObj text with
    | Error e -> setDecodeErr (sprintf "%s at %s – %s (last good wire kept below)" e.Code e.Path e.Message)
    | Ok node ->
      setDecodeErr ""
      setGoodTree node
      // Re-encode canonically: lenient-accepted shorthand normalises, so the
      // projections always walk canonical bytes.
      setGoodWire (CJson.encodeNode node)

  let reset () = tryAdopt (prettyJson exemplarWire)

  let wireEditor =
    Html.div
      [ prop.className "at-wire-box"
        prop.children
          [ Html.div
              [ prop.className "at-wire-head"
                prop.children
                  [ Html.span
                      [ prop.className "at-wire-tag"
                        prop.text "The wire – canonical JSON (edit or paste your own)" ]
                    Html.button
                      [ prop.className "at-reset-btn"
                        prop.text "Reset the exemplar"
                        prop.onClick (fun _ -> reset ()) ] ] ]
            Html.textarea
              [ prop.className "at-wire"
                prop.value input
                prop.rows 16
                prop.onChange (fun (v: string) -> tryAdopt v) ]
            (if decodeErr <> "" then
               Html.div [ prop.className "at-err"; prop.children [ Html.code [ prop.text decodeErr ] ] ]
             else
               Html.none) ] ]

  let preview =
    Html.div
      [ prop.className "at-preview"
        prop.children
          [ Html.span
              [ prop.className "at-pane-tag"
                prop.text "what this wire IS – the rendered app" ]
            Html.div [ prop.className "at-preview-app"; prop.children [ renderTree goodTree ] ] ] ]

  let tabs =
    Html.div
      [ prop.className "at-tabs"
        prop.children
          [ for t, label in languageTargets ->
              Html.button
                [ prop.className (if target = t then "at-tab at-tab-on" else "at-tab")
                  prop.text label
                  prop.onClick (fun _ -> setTarget t) ] ] ]

  let projection =
    Html.pre
      [ prop.className "at-code"
        prop.children [ Html.code [ prop.text (Projection.projectTo target goodWire) ] ] ]

  let honesty =
    Html.div
      [ prop.className "at-honesty"
        prop.children
          [ Html.h3 [ prop.text "How honest is this?" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "The projector is the playground's real multi-language source projector walking the canonical bytes above – pure string generation, and it never crashes: an unknown shape falls through a generic path rather than throwing." ]
                    Html.li
                      [ prop.text
                          "Fidelity is per-leg: the TypeScript projection is a verified byte-round-trip (the generated source is executed in the conformance harness and asserted to rebuild identical bytes); the other legs are idiomatic projections – how the tree would look written in each language – not certified compilers." ]
                    Html.li
                      [ prop.text
                          "Pasted wire is decoded by the real decoder – lenient-accepted shorthand normalises to canonical bytes before projection, and a reject keeps the last good wire, never a blank page." ] ] ] ] ]

  Html.div
    [ prop.className "at-page"
      prop.children
        [ Html.h1
            [ prop.className "at-title"
              prop.text "Attesor – read the wire back, in nine languages" ]
          Html.p
            [ prop.className "at-lede"
              prop.text
                "Rosetta shows nine languages converging on one wire. Attesor runs the arrow the other way: paste a wire, and read it back as idiomatic source in every host language – plus the app it renders, because the wire is the app. This is portability made concrete: write a UI once, in any of the nine languages, and it ports to every other – the wire is the common form every host can read back as its own code." ]
          wireEditor
          preview
          tabs
          projection
          honesty ] ]

let page: ReactElement = AttesorView()
