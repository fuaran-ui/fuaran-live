module Fuaran.Live.Demo

// ============================================================================
//  The scripted panel-turns demo (Phase 466) – watch an agent stream live
//  panels with NO key and NO network.
//
//  A canned `IAgenticProvider` drives the REAL `Agent.runAgentLoop` – the same
//  loop a live provider drives – through an audit scenario: it opens a live
//  progress panel, opens a findings panel, grows the findings row by row with
//  TreeOps, and then reaches BACK to the progress panel by id to mark the
//  audit complete (the "a later turn may address a prior panel" beat). Every
//  emission is authored with the typed API and encoded through the canonical
//  encoder, so the script exercises the exact wire path a real model uses –
//  nothing here bypasses the decode → apply → chain machinery.
//
//  A short pause before each scripted turn makes the streaming visible; the
//  provider reports no token usage (there is none – no model was called), so
//  the session tallies stay honest.
// ============================================================================

open Fable.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Live.Ports

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module Elc = Fuaran.UI.OpStream.Abstractions.Elicitation

let prompt =
  "Audit the sample dashboard design and stream your progress and findings as live panels."

// ─── the emitted artefacts (typed → canonical bytes) ─────────────────────────

let private progressTree: Node<obj> =
  Fuaran.card
    "ap-root"
    { Defaults.card with
        Heading = Some(TextSource.Literal "Audit progress")
        Children =
          [ Fuaran.progress
              "ap-bar"
              { Defaults.progress with
                  Fraction = Binding.Static(Some 0.1)
                  Label = Some(TextSource.Literal "Scanning the design")
                  Tone = ToneVariant.Brand }
            Fuaran.markdown "ap-status" "_Scanning 1 of 3 sections…_" ] }

let private findingsTree: Node<obj> =
  Fuaran.card
    "df-root"
    { Defaults.card with
        Heading = Some(TextSource.Literal "Findings")
        Children = [ Fuaran.markdown "df-intro" "Issues found while auditing – this table grows as I work:" ] }

let private statusKind (text: string) : NodeKind<obj> = (Fuaran.markdown "ap-status" text).Kind

let private progressAt (fraction: float) (status: string) : TreeOp<obj> =
  TreeOp.Batch
    [ TreeOp.ReplaceBinding(NodeId "ap-bar", "Fraction", Binding.Static(Some(box fraction)))
      TreeOp.EditNode(NodeId "ap-status", statusKind status) ]

let private findingRow (n: int) (text: string) : TreeOp<obj> =
  // 0.4.0: InsertChild appends. `n` survives as the row id only — the table
  // grows in order, so appending lands each row exactly where the index did.
  TreeOp.InsertChild(NodeId "df-root", Fuaran.markdown (sprintf "df-row-%d" n) text)

// ─── the typed question (Phase 465 §18 – the askUser beat) ───────────────────
//
// Mid-run the scripted agent asks WHERE to focus a follow-up pass – as a real
// elicitation envelope through the shipped codec, so the demo exercises the
// exact ask → typed-answer path a live model uses. The final turn then READS
// the threaded outcome and answers accordingly: proof the typed answer
// actually reached the agent, not theatre.

let private askSections: SelectOption list =
  [ { Value = "layout"
      Label = "Layout & typography" }
    { Value = "data-bindings"
      Label = "Data bindings" }
    { Value = "empty-states"
      Label = "Empty states" } ]

let private askTree: Node<obj> =
  Fuaran.stack
    "da-root"
    { Defaults.stack with
        Children =
          [ Fuaran.markdown
              "da-why"
              "One decision before I wrap up: **which section should the follow-up pass deep-dive?** Pick one – my next audit round budgets its time by your answer."
            Fuaran.select
              "da-section"
              { Defaults.select with
                  Label = TextSource.Literal "Deep-dive section"
                  Source = Binding.Static(Some askSections)
                  Value = Binding.State("da-section-value", Some "data-bindings") } ] }

let private askEnvelopeWire: string =
  let envelope: ElicitationEnvelope =
    { ElicitationId = "demo-deep-dive"
      Tree = askTree
      Contract =
        { Fields =
            [ { Name = "section"
                NodeId = NodeId "da-section"
                StateKey = "da-section-value"
                Space = Fuaran.Core.Enum [ "layout"; "data-bindings"; "empty-states" ]
                Required = true } ] }
      TimeoutMs = None
      Default = Some(Map [ "section", AnswerValue.Str "data-bindings" ]) }

  match Elc.encodeEnvelope envelope with
  | Ok wire -> wire
  | Error e -> failwith ("demo ask envelope failed its own encode: " + e.Code)

[<Fable.Core.Emit("JSON.parse($0)")>]
let private jsonParse (json: string) : obj = jsNative

/// The label of a section value, for the closing turn's text.
let private sectionLabel (value: string) : string =
  askSections
  |> List.tryPick (fun o -> if o.Value = value then Some o.Label else None)
  |> Option.defaultValue value

/// The closing text, tailored to the threaded outcome the loop handed back –
/// the "the agent actually read your typed answer" beat.
let private closingText (messages: AgentMessage list) : string =
  let outcome =
    messages
    |> List.rev
    |> List.collect _.Content
    |> List.tryPick (fun b ->
      match b with
      | AgentContentBlock.ToolResult(_, content, _) -> Elc.decodeOutcome content |> Result.toOption
      | _ -> None)

  let ending =
    match outcome with
    | Some { Outcome = ElicitationOutcome.Answered answer } ->
      match Map.tryFind "section" answer with
      | Some(AnswerValue.Str s) ->
        sprintf
          "You chose **%s** – a typed, contract-checked value I read by name, no prose parsed. The follow-up pass will deep-dive there."
          (sectionLabel s)
      | _ -> "Your typed answer arrived – the follow-up pass will use it."
    | Some { Outcome = ElicitationOutcome.Declined } ->
      "You declined – fair enough. I'll pick the deep-dive section myself next round (data bindings, since it had the gravest finding)."
    | Some { Outcome = ElicitationOutcome.TimedOut } ->
      "The question timed out, so I'll proceed with my declared default: data bindings."
    | _ -> "No answer arrived, so I'll proceed with my default: data bindings."

  ending
  + "\n\nThat's the whole run: two live panels streamed into our conversation, each with its own hash-chained op stream, and one typed question answered through a validated contract. The panels are still live – and a later turn could keep growing that findings table by id."

// ─── the script ──────────────────────────────────────────────────────────────

let private fence (envelope: string) : string = "\n\n```json\n" + envelope + "\n```"

let private panelTreeEmission (panelId: string) (title: string) (tree: Node<obj>) : string =
  fence (
    "{\"$panel\":\""
    + panelId
    + "\",\"title\":\""
    + title
    + "\",\"tree\":"
    + Canon.encodeNode tree
    + "}"
  )

let private panelOpEmission (panelId: string) (op: TreeOp<obj>) : string =
  fence ("{\"$panel\":\"" + panelId + "\",\"op\":" + Canon.encodeOp op + "}")

// A mid-run turn carries a (real) getRuntimeErrors verification call – both
// the loop's continuation driver (a turn with no tool calls ends the loop) and
// the honest agent-mode rhythm: emit, then check the emission took. The final
// turn calls no tools, ending the run.
let private turn (n: int) (text: string) : AgentOutcome =
  AgentOutcome.Ok(
    [ AgentContentBlock.Text text
      AgentContentBlock.ToolUse(sprintf "demo-tu-%d" n, "getRuntimeErrors", Fable.Core.JsInterop.createObj []) ],
    AgentStopReason.ToolUse,
    None
  )

let private lastTurn (text: string) : AgentOutcome =
  AgentOutcome.Ok([ AgentContentBlock.Text text ], AgentStopReason.EndTurn, None)

let private script: AgentOutcome list =
  [ turn
      1
      ("Starting the audit. First, a live progress panel – it will keep filling in as I work."
       + panelTreeEmission "audit-progress" "Audit progress" progressTree)
    turn
      2
      ("Layout and typography checked. Updating the progress panel in place – same panel, one op."
       + panelOpEmission "audit-progress" (progressAt 0.45 "_Checked 2 of 3 sections – data bindings next…_"))
    turn
      3
      ("I have findings to report. Opening a second panel for them – it starts near-empty and grows."
       + panelTreeEmission "findings" "Findings" findingsTree)
    turn
      4
      ("First finding."
       + panelOpEmission
           "findings"
           (findingRow 1 "**1. Revenue metric double-counts refunds** – the source binding sums gross, not net."))
    turn
      5
      ("Second finding."
       + panelOpEmission
           "findings"
           (findingRow 2 "**2. The orders table has no empty state** – an `OnEmpty` slot would prevent a blank pane."))
    turn
      6
      ("Audit done – reaching back to the FIRST panel by its id to close it out."
       + panelOpEmission "audit-progress" (progressAt 1.0 "_Audit complete – 3 of 3 sections checked._"))
    // The askUser beat: a REAL elicitation envelope through the real loop –
    // the run now waits on the visitor's typed answer (or Decline).
    AgentOutcome.Ok(
      [ AgentContentBlock.Text "Before I close out, I need one decision from you – asking as a typed form, not prose."
        AgentContentBlock.ToolUse("demo-ask", "askUser", jsonParse askEnvelopeWire) ],
      AgentStopReason.ToolUse,
      None
    ) ]

/// A monotonic clock the pacing sleep rides on; injected nowhere – this is a
/// UI demo, not a test surface.
let private paceMs = 650

/// A fresh scripted provider (one per demo run – the call counter is the
/// script cursor). Each `SendAgentic` call returns the next turn after a short
/// pause (so the transcript visibly streams). The turns up to the ask are
/// static; the CLOSING turn is built per-call from the accumulated messages,
/// so it genuinely reads the typed outcome the loop threaded back.
/// `Send` (single-shot) is unused by the unified loop.
let createProvider () : IAgenticProvider =
  let calls = ref 0

  { new IAgenticProvider with
      member _.Id = "demo"
      member _.Label = "Scripted demo"
      member _.DefaultModel = "scripted-demo"

      member _.Send(_request) =
        async { return ProviderOutcome.Ok("", None) }

      member _.SendAgentic(request) =
        async {
          do! Async.Sleep paceMs
          let i = calls.Value
          calls.Value <- i + 1

          if i < List.length script then
            return List.item i script
          else
            // Past the scripted turns: close with text tailored to the
            // outcome that arrived (no tools – this ends the run).
            return lastTurn (closingText request.Messages)
        } }
