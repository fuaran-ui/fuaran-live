module Fuaran.Showcase.Degradation

// ============================================================================
//  The Degradation Ladder – turn JavaScript off; nothing white-screens. Pillar:
//  "one wire, many worlds".
//
//  One content-rich exhibit (an equation, highlighted code, GFM markdown, an open
//  modal, a scroll container) is rendered at two fidelity rungs:
//
//   • Rung 1 – full fidelity: the shipped renderer's deterministic output PLUS a
//     client-only rich layer (dependency-free syntax highlighting – the exact
//     post-hydration enhancement the CodeBlock fidelity contract declares).
//   • Rung 2 – JavaScript OFF: the same rendered markup dropped into a genuinely
//     script-disabled sandboxed iframe (`sandbox` with no `allow-scripts`). No
//     enhancement can run, yet the equation, the code structure, the OPEN modal
//     (no-portal contract), and the scroll clipping all still render. The page
//     reads the iframe back to prove zero scripts and zero highlight spans reached
//     it – and everything else did.
//
//  A per-node fidelity legend names each fidelity-sensitive kind's three declared
//  tiers (source / fallback / rich). The wire drawer shows the canonical source –
//  the parity-clean data every host renders.
//
//  Honest scope: rungs 1 and 2 are real renders of the shipped fidelity contract;
//  the script-disabled iframe is genuinely script-disabled. The cross-host
//  byte-diff (F#/TS/Python) and the live CI-gate artifact are enforced in CI, not
//  client-side – the panel here states the contract and points at the gate rather
//  than faking a green (or red) run.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── The exhibit – one content-rich tree of fidelity-sensitive kinds ─────────

let private fsCode =
  "let hypotenuse (a: float) (b: float) =\n    sqrt (a * a + b * b)"

let private pyCode = "def hypotenuse(a, b):\n    return (a * a + b * b) ** 0.5"

let private scrollCard (i: int) : Node<unit> =
  Fuaran.card
    (sprintf "dl-sc-%d" i)
    { Defaults.card with
        Heading = Some(TextSource.Literal(sprintf "Frame %d" i))
        Children = [ Fuaran.markdown (sprintf "dl-sc-%d-v" i) (sprintf "row %d of the scroll region" i) ] }

let private exhibit: Node<unit> =
  Fuaran.box
    "dl-root"
    { Layout =
        BoxLayout.Flex
          { Direction = Vertical
            Wrap = false
            Gap = None }
      Role = BoxRole.Dashboard
      Heading = None
      Children =
        [ Fuaran.heading
            "dl-h"
            { Level = 3
              Text = TextSource.Literal "Sample card – a bit of everything"
              Variant = HeadingVariant.Standard }
          Fuaran.markdown
            "dl-md"
            "This card is **sample content** – a deliberate mix of the pieces that normally lean on JavaScript: a typeset equation, syntax-highlighted code in two languages, a modal dialog, and a clipped scroll region. It's here to be stress-tested, not read. Turn scripts off and watch each one survive."
          // In-subset — renders as native MathML: real superscripts with zero
          // JavaScript on the script-disabled rung (Phase 658).
          Fuaran.math "dl-eq" "a^2 + b^2 = c^2"
          // Out-of-subset (`\int`, `\,`) — deterministically falls back to the
          // raw LaTeX source span, so the source tier stays visible on the page.
          Fuaran.math "dl-eq2" "\\int_0^1 x^2 \\, dx"
          Fuaran.codeBlockSpec
            "dl-fs"
            { Defaults.codeBlock with
                Language = "fsharp"
                Code = fsCode
                LineNumbers = true
                HighlightLines = [ 2 ]
                Copyable = true }
          Fuaran.codeBlockSpec
            "dl-py"
            { Defaults.codeBlock with
                Language = "python"
                Code = pyCode
                LineNumbers = true
                HighlightLines = [ 2 ]
                Copyable = true }
          Fuaran.modal
            "dl-modal"
            { Defaults.modal with
                Open = Binding.Static true
                Heading = Some(TextSource.Literal "Note")
                Dismissable = true
                Children =
                  [ Fuaran.markdown "dl-modal-b" "A sample dialog – it renders open with scripts on **or** off." ] }
          Fuaran.scrollArea
            "dl-scroll"
            { Defaults.scrollArea with
                Orientation = ScrollOrientation.Vertical
                MaxHeight = Some 132
                Children = [ for i in 1..8 -> scrollCard i ] } ] }

let private exhibitJson = CJson.encodeNode exhibit

// ─── The fidelity legend – the declared three tiers per node kind ────────────

type private TierRow =
  { Kind: string
    Source: string
    Fallback: string
    Rich: string }

let private legend: TierRow list =
  [ { Kind = "Math"
      Source = "the LaTeX string on the wire"
      Fallback =
        "native MathML in-subset (real superscripts, no JS), else escaped source – deterministic, parity-pinned"
      Rich = "KaTeX typeset (client-only)" }
    { Kind = "CodeBlock"
      Source = "the code text + line/highlight semantics"
      Fallback = "structured <pre> with line numbers + highlight rows"
      Rich = "syntax colours + copy button (client-only)" }
    { Kind = "Markdown"
      Source = "the GFM markdown text"
      Fallback = "one deterministic GFM render, conformance-gated"
      Rich = "– (already deterministic)" }
    { Kind = "Modal"
      Source = "open flag + heading + children"
      Fallback = "inline render, no portal – displays open with JS off"
      Rich = "dismiss on × / backdrop (client-only)" }
    { Kind = "ScrollArea"
      Source = "orientation + max-height + children"
      Fallback = "CSS overflow clip – byte-identical across hosts"
      Rich = "– (pure CSS)" } ]

// ─── DOM helpers (the rich layer + the script-disabled iframe source) ────────

let private highlightRich: unit -> int = import "highlightRich" "./degradation.ts"
let private collectCss: unit -> string = import "collectCss" "./degradation.ts"
let private sourceHtml: unit -> string = import "sourceHtml" "./degradation.ts"
let private probeIframe: unit -> obj = import "probeIframe" "./degradation.ts"

type private Probe =
  { Math: int
    Msup: int
    Code: int
    Modal: int
    Scroll: int
    Scripts: int
    HighlightSpans: int }

let private renderTree (n: Node<unit>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

/// True when a click landed on the modal's dismiss button (×) or on the backdrop
/// itself – the demo's own runtime for the "dismiss (client-only)" rich tier,
/// since the exhibit renders through a no-op message sink.
[<Emit("(function(t){ try { if (!t) return false; if (t.closest && t.closest('.fuaran-modal-dismiss')) return true; if (t.classList && t.classList.contains('fuaran-modal-overlay')) return true; return false; } catch(e){ return false; } })($0)")>]
let private isDismissClick (target: obj) : bool = jsNative

// ─── The page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private DegradationView () : ReactElement =
  let srcDoc, setSrcDoc = React.useState ""
  let showWire, setShowWire = React.useState false
  let probe, setProbe = React.useState (None: Probe option)
  // Rung 1 is interactive like a real app: the dialog starts CLOSED, an "Open the
  // dialog" button opens it, and × / backdrop close it. Toggled by a class, not a
  // re-render, so the imperative highlight/copy layer survives. Rung 2 (the iframe,
  // no JS) renders the same markup with the dialog OPEN – the "renders without JS" proof.
  let modalOpen, setModalOpen = React.useState false

  let readProbe () : unit =
    let p = probeIframe ()

    setProbe (
      Some
        { Math = p?math
          Msup = p?msup
          Code = p?code
          Modal = p?modal
          Scroll = p?scroll
          Scripts = p?scripts
          HighlightSpans = p?highlightSpans }
    )

  // After the exhibit renders, apply the rich layer to rung 1 and build the
  // script-disabled iframe from the clean source markup + the real CSS.
  React.useEffect (
    (fun () ->
      highlightRich () |> ignore
      let css = collectCss ()
      let body = sourceHtml ()

      let doc =
        "<!doctype html><html><head><meta charset=\"utf-8\"><style>"
        + css
        + "\nbody{margin:0;padding:12px;background:#fff}</style></head><body>"
        + body
        + "</body></html>"

      setSrcDoc doc),
    [||]
  )

  let rung (n: int) (title: string) (sub: string) (body: ReactElement) : ReactElement =
    Html.div
      [ prop.className "dl-rung"
        prop.children
          [ Html.div
              [ prop.className "dl-rung-head"
                prop.children
                  [ Html.span [ prop.className "dl-rung-num"; prop.text (string n) ]
                    Html.div
                      [ prop.className "dl-rung-titles"
                        prop.children
                          [ Html.span [ prop.className "dl-rung-title"; prop.text title ]
                            Html.span [ prop.className "dl-rung-sub"; prop.text sub ] ] ] ] ]
            body ] ]

  let rung1 =
    rung
      1
      "Full fidelity"
      "the shipped render + a client-only rich layer (highlighting, copy, an interactive dialog)"
      (Html.div
        [ prop.className (
            if modalOpen then
              "dl-rung1 dl-stage"
            else
              "dl-rung1 dl-stage dl-modal-closed"
          )
          prop.onClick (fun e ->
            if isDismissClick e.target then
              setModalOpen false)
          prop.children
            [ (if modalOpen then
                 Html.none
               else
                 Html.button
                   [ prop.className "dl-reopen"
                     prop.text "Open the dialog"
                     prop.onClick (fun _ -> setModalOpen true) ])
              renderTree exhibit ] ])

  // The clean source render – off-screen; its markup feeds the iframe.
  let sourceRender =
    Html.div [ prop.className "dl-source"; prop.children [ renderTree exhibit ] ]

  let rung2 =
    rung
      2
      "JavaScript OFF"
      "the same markup in a genuinely script-disabled sandboxed iframe"
      (Html.div
        [ prop.className "dl-rung2 dl-stage"
          prop.children
            [ Html.iframe
                [ prop.className "dl-frame"
                  // `allow-same-origin` WITHOUT `allow-scripts` – scripts are
                  // genuinely disabled (JS off); same-origin only so the page
                  // can read the frame back to prove the fallback rendered.
                  // Adding `allow-scripts` is the one thing that would let JS
                  // run, and it is deliberately absent.
                  prop.custom ("sandbox", "allow-same-origin")
                  prop.custom ("srcDoc", srcDoc)
                  prop.onLoad (fun _ -> readProbe ())
                  prop.title "Rung 2 – the exhibit with scripts disabled" ]
              (match probe with
               | Some p ->
                 Html.div
                   [ prop.className "dl-probe"
                     prop.children
                       [ Html.span
                           [ prop.className (if p.Scripts = 0 then "dl-probe-ok" else "dl-probe-bad")
                             prop.text (sprintf "%d scripts ran" p.Scripts) ]
                         Html.span [ prop.className "dl-probe-item"; prop.text (sprintf "equation ✓ (%d)" p.Math) ]
                         Html.span
                           [ prop.className (if p.Msup > 0 then "dl-probe-item" else "dl-probe-bad")
                             prop.text (sprintf "MathML superscripts ✓ (%d ⟨msup⟩)" p.Msup) ]
                         Html.span [ prop.className "dl-probe-item"; prop.text (sprintf "code ✓ (%d)" p.Code) ]
                         Html.span [ prop.className "dl-probe-item"; prop.text (sprintf "modal ✓ (%d)" p.Modal) ]
                         Html.span [ prop.className "dl-probe-item"; prop.text (sprintf "scroll ✓ (%d)" p.Scroll) ]
                         Html.span
                           [ prop.className "dl-probe-item"
                             prop.text (sprintf "%d rich spans" p.HighlightSpans) ] ] ]
               | None -> Html.none) ] ])

  let ladder = Html.div [ prop.className "dl-ladder"; prop.children [ rung1; rung2 ] ]

  let legendPanel =
    Html.div
      [ prop.className "dl-legend"
        prop.children
          [ Html.h3
              [ prop.className "dl-legend-title"
                prop.text "The fidelity contract, per node kind" ]
            Html.div
              [ prop.className "dl-legend-grid"
                prop.children
                  [ Html.div [ prop.className "dl-legend-h"; prop.text "kind" ]
                    Html.div [ prop.className "dl-legend-h"; prop.text "source (wire)" ]
                    Html.div [ prop.className "dl-legend-h"; prop.text "fallback (parity-pinned)" ]
                    Html.div [ prop.className "dl-legend-h"; prop.text "rich (client-only)" ]
                    for r in legend do
                      Html.div [ prop.className "dl-legend-kind"; prop.text r.Kind ]
                      Html.div [ prop.className "dl-legend-cell dl-tier-source"; prop.text r.Source ]
                      Html.div [ prop.className "dl-legend-cell dl-tier-fallback"; prop.text r.Fallback ]
                      Html.div [ prop.className "dl-legend-cell dl-tier-rich"; prop.text r.Rich ] ] ] ] ]

  let wireDrawer =
    Html.div
      [ prop.className "dl-wire"
        prop.children
          [ Html.button
              [ prop.className "dl-wire-toggle"
                prop.text (
                  if showWire then
                    "Hide the wire source"
                  else
                    "Show the wire source – the bytes every host renders"
                )
                prop.onClick (fun _ -> setShowWire (not showWire)) ]
            (if showWire then
               Html.pre
                 [ prop.className "wire-json"
                   prop.children [ Html.code [ prop.text exhibitJson ] ] ]
             else
               Html.none) ] ]

  let honesty =
    Html.div
      [ prop.className "dl-honesty"
        prop.children
          [ Html.h3 [ prop.text "Descent with dignity – really" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Both rungs render the same tree through the shipped renderer. The fidelity contract puts everything deterministic on the wire, so the base render needs no JavaScript – the rich layer (highlighting, the copy button, opening and dismissing the dialog) is declared client-only, layered on top. That's why rung 1 lets you drive the dialog and rung 2 cannot." ]
                    Html.li
                      [ prop.text
                          "Rung 2 is genuinely script-disabled: the same markup is placed in a sandboxed iframe with no allow-scripts, so nothing can run. The equation still typesets with REAL superscripts – native MathML, laid out by the browser with zero JavaScript (an out-of-subset equation stays as its readable LaTeX source) – and the code structure, the dialog in its open state (no portal), and the scroll clipping all still render. The page reads the iframe back to confirm zero scripts and zero highlight spans reached it, that genuine ⟨msup⟩ elements are present, and everything else did too." ]
                    Html.li
                      [ prop.text
                          "The wire source is the parity-clean data every conformant host renders. The byte-for-byte cross-host agreement (F#, TypeScript, Python) and the break-the-contract red build are enforced by the conformance gate in CI – a real gate you can run, not a claim this page can prove client-side." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "No SPA can even state this contract, because nothing else separates what the artefact says from how a host renders it. Same "
                            Html.a [ prop.href "#/pillar/wire"; prop.text "one-wire-many-worlds" ]
                            Html.text " thesis, at the render layer." ] ] ] ] ] ]

  Html.div
    [ prop.className "dl-page"
      prop.children
        [ Html.h1 [ prop.className "dl-title"; prop.text "The Degradation Ladder" ]
          Html.p
            [ prop.className "dl-lede"
              prop.text
                "Turn JavaScript off. The equation still renders, the code still reads, the open dialog still shows – the app degrades like a photograph, not like a crash." ]
          ladder
          sourceRender
          legendPanel
          wireDrawer
          honesty ] ]

let page: ReactElement = DegradationView()
