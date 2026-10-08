module Fuaran.Live.Testing.AgentProbes

// ============================================================================
//  TEST-ONLY. The agent loop's headless probes: the flat tool dispatch, the scripted
//  agentic providers, and one probe per loop behaviour the suite asserts.
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that `Agent.fs` holds only what the playground runs.
// ============================================================================

open Fable.Core
open Fuaran.Live
open Fable.Core.JsInterop
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Live.Ports

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module Schema = Fuaran.UI.Ops.SchemaGen
open Fuaran.Live.Agent

/// The stable string form of a halt reason (the flat test surface).
let haltReasonStr (reason: HaltReason) : string =
  match reason with
  | HaltReason.Completed -> "completed"
  | HaltReason.MaxIterations -> "max-iterations"
  | HaltReason.MaxTokens -> "max-tokens"
  | HaltReason.MaxWallClock -> "max-wall-clock"
  | HaltReason.Aborted -> "aborted"
  | HaltReason.ProviderError -> "provider-error"

/// The kind discriminator of an entry, as a string (the flat test surface).
let timelineKind (entry: TimelineEntry) : string =
  match entry with
  | TimelineEntry.UserPrompt _ -> "user-prompt"
  | TimelineEntry.ModelCall _ -> "model-call"
  | TimelineEntry.ModelText _ -> "model-text"
  | TimelineEntry.Emission _ -> "emission"
  | TimelineEntry.ToolCall _ -> "tool-call"
  | TimelineEntry.PanelEmission _ -> "panel-emission"
  | TimelineEntry.Ask _ -> "ask"
  | TimelineEntry.Halt _ -> "halt"

/// A headless `AskUser`: immediately resolves every question as `Declined` –
/// the honest no-surface answer (never a fabricated `Answered`).
let autoDeclineAskUser (envelopeJson: string) : Async<string> =
  async {
    let id =
      match Elicitation.decodeEnvelope envelopeJson with
      | Ok env -> env.ElicitationId
      | Error _ -> "unknown"

    return
      Elicitation.encodeOutcome
        { ElicitationId = id
          Outcome = ElicitationOutcome.Declined }
  }

// ─── flat test surface (cross-boundary friendly – used by the loop tests) ─────
//
// As in Session.fs, the F# DU / Async results are awkward to assert on from the
// vitest (TS-over-Fable-output) side. These helpers project the same logic to
// flat values (anonymous records / a Promise), so the pure tool dispatch and the
// full loop – driven by a MOCK agentic provider scripted in F# – are testable
// headlessly without a live LLM or a DOM.

/// `dispatchTool` over a default context (null DOM, no runtime errors), with the
/// tree JSON passed flat (`null` ⇒ no tree). Lets the test exercise the three
/// pure tools directly.
let dispatchToolFlat (treeJson: string) (name: string) (input: obj) : {| Ok: bool; Content: string |} =
  let ctx: ToolContext =
    { CurrentTreeJson = fun () -> Option.ofObj treeJson
      RuntimeErrors = fun () -> []
      Advisories = fun () -> []
      Dom = nullDomReader }

  let r = dispatchTool ctx name input
  {| Ok = r.Ok; Content = r.Content |}

/// A scripted mock agentic provider: returns the i-th canned outcome per call,
/// clamping to the last (so an over-budget loop keeps getting the final outcome).
let scriptedAgenticProvider (script: AgentOutcome list) : IAgenticProvider =
  let calls = ref 0

  { new IAgenticProvider with
      member _.SendAgentic(_request) =
        async {
          let i = calls.Value
          calls.Value <- i + 1
          let idx = if i < List.length script then i else List.length script - 1
          return List.item idx script
        } }

let private probeDeps
  (provider: IAgenticProvider)
  (budget: AgentBudget)
  (timeline: ResizeArray<TimelineEntry>)
  : AgentLoopDeps =
  { Provider = provider
    Dom = nullDomReader
    Model = "mock-model"
    MaxTokens = 1024
    Budget = budget
    Emit = timeline.Add
    OnSession = ignore
    OnPanels = ignore
    ShouldAbort = fun () -> false
    WaitForPaint = fun () -> async { return () }
    Now = nowMs
    SystemSuffix = ""
    AskUser = autoDeclineAskUser }

/// Drive the loop against a mock provider scripted `tool_use → end_turn`: turn 1
/// emits text + a tool call on `nodeId`; turn 2 ends the turn with no tools. The
/// session is seeded with `seedEmission` so the tool finds a real node. Proves
/// tool-running + result-threading (the 2nd provider call must carry the
/// tool_result) + the end_turn halt. Returns a flat result as a JS Promise.
let runLoopProbe
  (seedEmission: string)
  (toolName: string)
  (nodeId: string)
  : JS.Promise<
      {| HaltReason: string
         Iterations: int
         ToolCalls:
           {| Name: string
              Ok: bool
              ResultSummary: string |} array
         SecondTurnThreadedToolResult: bool
         TimelineKinds: string array |}
     >
  =
  let seeded =
    match Session.ingest Session.empty seedEmission with
    | Session.Ingested(_, s) -> s
    | Session.IngestFailed _ -> Session.empty

  let secondTurnThreaded = ref false
  let calls = ref 0

  let script =
    [ AgentOutcome.Ok(
        [ AgentContentBlock.Text "Let me inspect what rendered."
          AgentContentBlock.ToolUse("tu-1", toolName, createObj [ "nodeId" ==> nodeId ]) ],
        AgentStopReason.ToolUse,
        Some { InputTokens = 10; OutputTokens = 5 }
      )
      AgentOutcome.Ok(
        [ AgentContentBlock.Text "The UI looks correct." ],
        AgentStopReason.EndTurn,
        Some { InputTokens = 8; OutputTokens = 3 }
      ) ]

  let provider =
    { new IAgenticProvider with
        member _.SendAgentic(request) =
          async {
            let i = calls.Value
            calls.Value <- i + 1

            if i = 1 then
              secondTurnThreaded.Value <-
                request.Messages
                |> List.exists (fun m ->
                  m.Content
                  |> List.exists (fun b ->
                    match b with
                    | AgentContentBlock.ToolResult _ -> true
                    | _ -> false))

            let idx = if i < List.length script then i else List.length script - 1
            return List.item idx script
          } }

  let timeline = ResizeArray<TimelineEntry>()

  async {
    let! result = runAgentLoop seeded Panels.empty [] "Inspect the metric." (probeDeps provider defaultBudget timeline)

    let toolCalls =
      timeline
      |> Seq.choose (fun e ->
        match e with
        | TimelineEntry.ToolCall(_, name, _, summary, ok) ->
          Some
            {| Name = name
               Ok = ok
               ResultSummary = summary |}
        | _ -> None)
      |> Seq.toArray

    return
      {| HaltReason = haltReasonStr result.HaltReason
         Iterations = result.Iterations
         ToolCalls = toolCalls
         SecondTurnThreadedToolResult = secondTurnThreaded.Value
         TimelineKinds = timeline |> Seq.map timelineKind |> Seq.toArray |}
  }
  |> Async.StartAsPromise

/// Drive the loop against a provider that always wants more tools, with a small
/// iteration budget – proves the budget halts a runaway loop. Returns flat.
let runBudgetProbe
  (maxIterations: int)
  : JS.Promise<
      {| HaltReason: string
         Iterations: int |}
     >
  =
  let provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.ToolUse("tu", "getRuntimeErrors", createObj []) ],
          AgentStopReason.ToolUse,
          None
        ) ]

  let timeline = ResizeArray<TimelineEntry>()

  let deps =
    probeDeps
      provider
      { defaultBudget with
          MaxIterations = maxIterations }
      timeline

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "loop forever" deps

    return
      {| HaltReason = haltReasonStr result.HaltReason
         Iterations = result.Iterations |}
  }
  |> Async.StartAsPromise

/// Drive the loop where turn 1 emits a TreeOp with no tree yet (ingest fails) and
/// calls NO tools – the "emit-and-stop over a broken emission" case – then turn 2
/// emits a valid full tree, also no tools. Proves the loop does NOT halt as
/// Completed on the failed turn (it feeds the failure back so the model can
/// recover), and DOES complete once a good tree lands. Returns flat.
let runEmitRepairProbe
  ()
  : JS.Promise<
      {| HaltReason: string
         Iterations: int
         FinalTreeSet: bool
         FinalErrorCount: int |}
     >
  =
  let failingOp =
    "Editing the tree.\n\n```json\n{\"$type\":\"RemoveNode\",\"target\":\"ghost\"}\n```"

  let fullTree =
    "Here is the full tree.\n\n```json\n{\"id\":\"m\",\"kind\":{\"$type\":\"Markdown\",\"text\":{\"$type\":\"Literal\",\"text\":\"hi\"}}}\n```"

  let provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.Text failingOp ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        )
        AgentOutcome.Ok(
          [ AgentContentBlock.Text fullTree ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        ) ]

  let timeline = ResizeArray<TimelineEntry>()
  let lastSession = ref Session.empty

  let deps =
    { probeDeps provider defaultBudget timeline with
        OnSession = fun s -> lastSession.Value <- s }

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "build it" deps

    return
      {| HaltReason = haltReasonStr result.HaltReason
         Iterations = result.Iterations
         FinalTreeSet = lastSession.Value.Tree.IsSome
         FinalErrorCount = List.length result.RuntimeErrors |}
  }
  |> Async.StartAsPromise

// ─── Phase 664 probes – pre-emit advisories in the loop ──────────────────────

/// A valid grid emission that APPLIES but is inert-editable (FUARAN090):
/// `editable: true` over a Transform source.
let private inertEditableEmission =
  "Here is the grid.\n\n```json\n{\"id\":\"g\",\"kind\":{\"$type\":\"DataGrid\",\"editable\":true,\"rowKeyField\":\"q\",\"columns\":[{\"field\":\"q\",\"kind\":{\"$type\":\"Text\"},\"label\":\"Q\"}],\"source\":{\"$type\":\"Transform\",\"pipeline\":[],\"source\":{\"columns\":{\"q\":{\"validity\":[true],\"values\":[\"Q1\"]}},\"schema\":[{\"name\":\"q\",\"type\":\"string\"}]}}}}\n```"

/// The corrected shape – the same grid over a shared `$state` rows source
/// (the Phase 663 write-back floor); draws no advisories.
let private stateEditableEmission =
  "Corrected.\n\n```json\n{\"id\":\"g\",\"kind\":{\"$type\":\"DataGrid\",\"editable\":true,\"rowKeyField\":\"q\",\"columns\":[{\"field\":\"q\",\"kind\":{\"$type\":\"Text\"},\"label\":\"Q\"}],\"source\":{\"$type\":\"State\",\"key\":\"rows\",\"defaultValue\":[{\"q\":\"Q1\"}]}}}\n```"

/// Count the advisory feedback turns the loop injected (the no-tools leg).
let private advisoryFeedbackCount (msgs: AgentMessage list) : int =
  msgs
  |> List.filter (fun m ->
    m.Role = User
    && m.Content
       |> List.exists (fun b ->
         match b with
         | AgentContentBlock.Text t -> t.Contains "pre-emit advisories"
         | _ -> false))
  |> List.length

let private emissionSummaries (timeline: ResizeArray<TimelineEntry>) : string list =
  timeline
  |> Seq.choose (fun e ->
    match e with
    | TimelineEntry.Emission(_, _, _, summary) -> Some summary
    | _ -> None)
  |> List.ofSeq

/// Turn 1 emits an inert-editable grid (applies; FUARAN090) with no tools –
/// the loop must feed the advisory back rather than halting satisfied. Turn 2
/// emits the corrected `$state`-sourced grid – the loop completes with a clean
/// advisory slate. Returns flat.
let runAdvisoryProbe
  ()
  : JS.Promise<
      {| HaltReason: string
         Iterations: int
         FeedbackTurns: int
         FeedbackNamesFuaran090: bool
         EmissionSummaryNamesFuaran090: bool
         FinalAdvisoryCount: int |}
     >
  =
  let provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.Text inertEditableEmission ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        )
        AgentOutcome.Ok(
          [ AgentContentBlock.Text stateEditableEmission ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        ) ]

  let timeline = ResizeArray<TimelineEntry>()
  let lastSession = ref Session.empty

  let deps =
    { probeDeps provider defaultBudget timeline with
        OnSession = fun s -> lastSession.Value <- s }

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "build an editable grid" deps

    let feedbackTexts =
      result.FinalMessages
      |> List.collect (fun m ->
        m.Content
        |> List.choose (fun b ->
          match b with
          | AgentContentBlock.Text t when t.Contains "pre-emit advisories" -> Some t
          | _ -> None))

    return
      {| HaltReason = haltReasonStr result.HaltReason
         Iterations = result.Iterations
         FeedbackTurns = advisoryFeedbackCount result.FinalMessages
         FeedbackNamesFuaran090 = feedbackTexts |> List.exists _.Contains("FUARAN090")
         EmissionSummaryNamesFuaran090 = emissionSummaries timeline |> List.exists _.Contains("FUARAN090")
         FinalAdvisoryCount = List.length (Session.preEmitAdvisories lastSession.Value) |}
  }
  |> Async.StartAsPromise

/// Both turns emit the SAME inert-editable wire – the fingerprint guard must
/// feed the advisory set exactly once and then complete (warnings can never
/// burn the budget). Returns flat.
let runAdvisoryRepeatProbe
  ()
  : JS.Promise<
      {| HaltReason: string
         Iterations: int
         FeedbackTurns: int |}
     >
  =
  let provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.Text inertEditableEmission ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        )
        AgentOutcome.Ok(
          [ AgentContentBlock.Text inertEditableEmission ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        ) ]

  let timeline = ResizeArray<TimelineEntry>()

  let deps = probeDeps provider defaultBudget timeline

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "build an editable grid" deps

    return
      {| HaltReason = haltReasonStr result.HaltReason
         Iterations = result.Iterations
         FeedbackTurns = advisoryFeedbackCount result.FinalMessages |}
  }
  |> Async.StartAsPromise

/// Drive the loop twice to prove **resumability** (Phase 328): run 1 emits a
/// tree and halts on a budget (one iteration, model still wants tools); run 2
/// resumes from run 1's `FinalMessages` with a follow-up prompt and completes.
/// Asserts run 2 carried the prior context (its message seed is non-empty and
/// longer than a cold start) and that the token counter accumulates across both
/// runs. Returns flat.
let runResumeProbe
  ()
  : JS.Promise<
      {| Run1Halt: string
         Run1Tokens: int
         Run2Halt: string
         Run2Tokens: int
         ResumedWithContext: bool
         Run2SeedHadPrior: bool |}
     >
  =
  // Run 1: a single tool call then (budget halts before it can finish).
  let run1Provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.Text "Inspecting."
            AgentContentBlock.ToolUse("tu", "getRuntimeErrors", createObj []) ],
          AgentStopReason.ToolUse,
          Some { InputTokens = 12; OutputTokens = 6 }
        ) ]

  // Run 2: ends the turn immediately (no tools) – completes.
  let run2Provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.Text "All good now." ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 4; OutputTokens = 2 }
        ) ]

  let tl1 = ResizeArray<TimelineEntry>()
  let tl2 = ResizeArray<TimelineEntry>()

  // Run 1 halts at one iteration via the iteration backstop.
  let deps1 = probeDeps run1Provider { defaultBudget with MaxIterations = 1 } tl1

  async {
    let! r1 = runAgentLoop Session.empty Panels.empty [] "build it" deps1
    // Run 2 resumes from run 1's accumulated context.
    let deps2 = probeDeps run2Provider defaultBudget tl2
    let priorSeed = r1.FinalMessages
    let! r2 = runAgentLoop Session.empty Panels.empty priorSeed "now fix the heading" deps2

    return
      {| Run1Halt = haltReasonStr r1.HaltReason
         Run1Tokens = r1.TotalTokens
         Run2Halt = haltReasonStr r2.HaltReason
         Run2Tokens = r2.TotalTokens
         // Run 2's final message list must include run 1's prior turns plus the
         // new prompt – i.e. it is strictly longer than the seed alone.
         ResumedWithContext = (List.length r2.FinalMessages > List.length priorSeed)
         Run2SeedHadPrior = (not (List.isEmpty priorSeed)) |}
  }
  |> Async.StartAsPromise

/// Drive the loop through a full panel-turn conversation (Phase 466): turn 1
/// opens a `$panel` with a tree; turn 2 extends it with an op; turn 3 emits a
/// BROKEN panel op (unknown target node) with no tools – proving a failed
/// panel emission is fed back for repair, not mistaken for "satisfied"; turn 4
/// ends clean. Asserts the panel store contents, the per-panel hash chain, the
/// replay verification (refold + chain recompute), and the timeline row kinds.
/// The main-preview session must be untouched throughout (panel emissions
/// never leak into it). Returns flat.
let runPanelProbe
  ()
  : JS.Promise<
      {| HaltReason: string
         PanelCount: int
         PanelId: string
         PanelTitle: string
         OpsApplied: int
         ChainLength: int
         ChainLinked: bool
         ReplayOk: bool
         ChainOk: bool
         FirstEmissionWasNew: bool
         SecondEmissionWasNew: bool
         BrokenOpFedBack: bool
         MainSessionUntouched: bool
         PanelKindsInTimeline: int
         PanelSummaries: string array |}
     >
  =
  // The scripted emissions are authored with the typed API and encoded through
  // the real canonical encoder – the probe tests the panel CHANNEL, not the
  // author's memory of wire shapes.
  let findingsTree: Fuaran.UI.Types.Node<obj> =
    Fuaran.UI.Fuaran.card
      "f-root"
      { Fuaran.UI.Defaults.card with
          Heading = Some(Fuaran.UI.Types.TextSource.Literal "Findings")
          Children = [ Fuaran.UI.Fuaran.markdown "f-head" "**Findings so far**" ] }

  let rowNode (id: string) (text: string) : Fuaran.UI.Types.Node<obj> = Fuaran.UI.Fuaran.markdown id text

  // 0.4.0: InsertChild appends; ReorderChildren states order. Both call sites
  // below appended anyway (f-root holds one child; "ghost" is a reject probe),
  // so the position parameter is dropped rather than replaced with a Batch.
  let insertRowOp (parent: string) (row: Fuaran.UI.Types.Node<obj>) : string =
    Canon.encodeOp (Fuaran.UI.Ops.Types.TreeOp.InsertChild(Fuaran.UI.Types.NodeId parent, row))

  let fence (envelope: string) : string = "\n\n```json\n" + envelope + "\n```"

  let panelTree =
    "Opening a live findings panel."
    + fence (
      "{\"$panel\":\"findings\",\"title\":\"Findings\",\"tree\":"
      + Canon.encodeNode findingsTree
      + "}"
    )

  let panelOp =
    "Adding the first finding."
    + fence (
      "{\"$panel\":\"findings\",\"op\":"
      + insertRowOp "f-root" (rowNode "f-row-1" "1. The heading overflows on mobile.")
      + "}"
    )

  let brokenOp =
    "Adding another."
    + fence (
      "{\"$panel\":\"findings\",\"op\":"
      + insertRowOp "ghost" (rowNode "f-row-x" "nope")
      + "}"
    )

  // Turns 1–2 carry a tool call (the loop's continuation driver – a turn with
  // no tools ends the loop); turn 3's broken op deliberately calls NO tools,
  // exercising the fed-back-for-repair guard on the panel path; turn 4 ends.
  let provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.Text panelTree
            AgentContentBlock.ToolUse("tu-1", "getRuntimeErrors", createObj []) ],
          AgentStopReason.ToolUse,
          Some { InputTokens = 5; OutputTokens = 5 }
        )
        AgentOutcome.Ok(
          [ AgentContentBlock.Text panelOp
            AgentContentBlock.ToolUse("tu-2", "getRuntimeErrors", createObj []) ],
          AgentStopReason.ToolUse,
          Some { InputTokens = 5; OutputTokens = 5 }
        )
        AgentOutcome.Ok(
          [ AgentContentBlock.Text brokenOp ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        )
        AgentOutcome.Ok(
          [ AgentContentBlock.Text "Done – one finding so far." ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 3; OutputTokens = 2 }
        ) ]

  let timeline = ResizeArray<TimelineEntry>()
  let lastPanels = ref Panels.empty
  let lastSession = ref Session.empty
  let sessionTouched = ref false
  let brokenFedBack = ref false

  // The broken-op turn calls no tools, so the loop must feed the failure back
  // as a user turn (the anti-"satisfied over a broken emission" guard) – we
  // detect that by the message context growing a user turn naming the failure.
  let deps =
    { probeDeps provider defaultBudget timeline with
        OnPanels = fun p -> lastPanels.Value <- p
        OnSession =
          fun s ->
            lastSession.Value <- s

            if s.Tree.IsSome then
              sessionTouched.Value <- true }

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "audit this design" deps

    brokenFedBack.Value <-
      result.FinalMessages
      |> List.exists (fun m ->
        m.Role = User
        && m.Content
           |> List.exists (fun b ->
             match b with
             | AgentContentBlock.Text t -> t.Contains "did NOT apply"
             | _ -> false))

    // The panels in display order (the store keeps the order beside the map).
    let panels =
      lastPanels.Value.Order
      |> List.choose (fun id -> Map.tryFind id lastPanels.Value.Panels)

    let panel = List.tryHead panels

    let verifyReport = panel |> Option.map Panels.verify

    let panelEmissions =
      timeline
      |> Seq.choose (fun e ->
        match e with
        | TimelineEntry.PanelEmission(_, _, _, ok, isNew, _) -> Some(ok, isNew)
        | _ -> None)
      |> Seq.toList

    let panelSummaries =
      timeline
      |> Seq.choose (fun e ->
        match e with
        | TimelineEntry.PanelEmission(_, _, _, ok, _, summary) -> Some((if ok then "ok: " else "FAIL: ") + summary)
        | _ -> None)
      |> Seq.toArray

    let chainLinked =
      match panel with
      | Some p ->
        match p.Chain with
        | [ entry ] -> entry.Prev = p.BaseHash && entry.Hash <> "" && entry.Seq = 1
        | _ -> false
      | None -> false

    return
      {| HaltReason = haltReasonStr result.HaltReason
         PanelCount = List.length panels
         PanelId = panel |> Option.map _.Id |> Option.defaultValue ""
         PanelTitle = panel |> Option.bind _.Title |> Option.defaultValue ""
         OpsApplied = panel |> Option.map (fun p -> List.length p.OpsJson) |> Option.defaultValue -1
         ChainLength = panel |> Option.map (fun p -> List.length p.Chain) |> Option.defaultValue -1
         ChainLinked = chainLinked
         ReplayOk = verifyReport |> Option.map _.ReplayOk |> Option.defaultValue false
         ChainOk = verifyReport |> Option.map _.ChainOk |> Option.defaultValue false
         FirstEmissionWasNew =
          (match List.tryItem 0 panelEmissions with
           | Some(true, isNew) -> isNew
           | _ -> false)
         SecondEmissionWasNew =
          (match List.tryItem 1 panelEmissions with
           | Some(true, isNew) -> isNew
           | _ -> true)
         BrokenOpFedBack = brokenFedBack.Value
         MainSessionUntouched = not sessionTouched.Value
         PanelKindsInTimeline = panelEmissions |> List.length
         PanelSummaries = panelSummaries |}
  }
  |> Async.StartAsPromise

// ─── the askUser probes (Phase 465 §18 in the live loop) ─────────────────────
//
// The elicitation seam exercised headlessly: a scripted provider issues an
// `askUser` tool call, a SCRIPTED answerer plays the human (the same injection
// point an evaluation harness uses), and the probe asserts the typed outcome
// threads back into the model's next turn. No DOM, no LLM.

/// A minimal valid elicitation envelope (a markdown + select question with one
/// enum-typed answer field), as canonical wire JSON. Authored typed and encoded
/// through the shipped codec, so the probe drives the REAL envelope path.
let private probeEnvelopeWire () : string =
  let flavourOptions: Fuaran.UI.Types.SelectOption list =
    [ { Value = "compact"; Label = "Compact" }
      { Value = "detailed"
        Label = "Detailed" } ]

  let tree: Fuaran.UI.Types.Node<obj> =
    Fuaran.UI.Fuaran.stack
      "ask-root"
      { Fuaran.UI.Defaults.stack with
          Children =
            [ Fuaran.UI.Fuaran.markdown "ask-why" "Which flavour should I build?"
              Fuaran.UI.Fuaran.select
                "ask-flavour"
                { Fuaran.UI.Defaults.select with
                    Label = Fuaran.UI.Types.TextSource.Literal "Flavour"
                    Source = Fuaran.UI.Types.Binding.Static(Some flavourOptions)
                    Value = Fuaran.UI.Types.Binding.State("ask-flavour-value", Some "compact") } ] }

  let envelope: ElicitationEnvelope =
    { ElicitationId = "probe-ask-1"
      Tree = tree
      Contract =
        { Fields =
            [ { Name = "flavour"
                NodeId = Fuaran.UI.Types.NodeId "ask-flavour"
                StateKey = "ask-flavour-value"
                Space = Fuaran.Core.Enum [ "compact"; "detailed" ]
                Required = true } ] }
      TimeoutMs = None
      Default = Some(Map [ "flavour", AnswerValue.Str "compact" ]) }

  match Elicitation.encodeEnvelope envelope with
  | Ok wire -> wire
  | Error e -> failwith ("probe envelope failed its own encode: " + e.Code)

[<Emit("JSON.parse($0)")>]
let private jsonParseRaw (json: string) : obj = jsNative

/// Drive the loop through a full ask-and-answer round trip: turn 1 calls
/// `askUser` with a valid envelope; the scripted answerer resolves per
/// `outcomeKind` ("answered" resolves a conforming enum answer; anything else
/// resolves Declined); turn 2 ends the run. Asserts the outcome wire threads
/// back as the tool result the model reads and the timeline carries the `ask`
/// row. Returns flat.
let runAskProbe
  (outcomeKind: string)
  : JS.Promise<
      {| HaltReason: string
         Iterations: int
         TimelineKinds: string array
         ToolSummaries: string array
         ThreadedOutcome: string
         AskedElicitationId: string |}
     >
  =
  let envelopeWire = probeEnvelopeWire ()

  let scriptedAnswerer (envJson: string) : Async<string> =
    async {
      let id =
        match Elicitation.decodeEnvelope envJson with
        | Ok env -> env.ElicitationId
        | Error _ -> "unknown"

      let outcome =
        if outcomeKind = "answered" then
          ElicitationOutcome.Answered(Map [ "flavour", AnswerValue.Str "detailed" ])
        else
          ElicitationOutcome.Declined

      return
        Elicitation.encodeOutcome
          { ElicitationId = id
            Outcome = outcome }
    }

  // The provider records what the loop threads back on its second call.
  let threadedOutcome = ref ""
  let calls = ref 0

  let script =
    [ AgentOutcome.Ok(
        [ AgentContentBlock.Text "I need your decision before building."
          AgentContentBlock.ToolUse("tu-ask", "askUser", jsonParseRaw envelopeWire) ],
        AgentStopReason.ToolUse,
        Some { InputTokens = 10; OutputTokens = 5 }
      )
      AgentOutcome.Ok(
        [ AgentContentBlock.Text "Thanks – proceeding with your choice." ],
        AgentStopReason.EndTurn,
        Some { InputTokens = 8; OutputTokens = 3 }
      ) ]

  let provider =
    { new IAgenticProvider with
        member _.SendAgentic(request) =
          async {
            let i = calls.Value
            calls.Value <- i + 1

            if i = 1 then
              threadedOutcome.Value <-
                request.Messages
                |> List.collect _.Content
                |> List.tryPick (fun b ->
                  match b with
                  | AgentContentBlock.ToolResult(_, content, _) -> Some content
                  | _ -> None)
                |> Option.defaultValue ""

            let idx = min i (List.length script - 1)
            return List.item idx script
          } }

  let timeline = ResizeArray<TimelineEntry>()

  let deps =
    { probeDeps provider defaultBudget timeline with
        AskUser = scriptedAnswerer }

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "Build me a thing." deps

    let askedId =
      timeline
      |> Seq.tryPick (fun e ->
        match e with
        | TimelineEntry.Ask(_, id, _) -> Some id
        | _ -> None)
      |> Option.defaultValue ""

    let toolSummaries =
      timeline
      |> Seq.choose (fun e ->
        match e with
        | TimelineEntry.ToolCall(_, _, _, summary, _) -> Some summary
        | _ -> None)
      |> Seq.toArray

    return
      {| HaltReason = haltReasonStr result.HaltReason
         Iterations = result.Iterations
         TimelineKinds = timeline |> Seq.map timelineKind |> Seq.toArray
         ToolSummaries = toolSummaries
         ThreadedOutcome = threadedOutcome.Value
         AskedElicitationId = askedId |}
  }
  |> Async.StartAsPromise

/// Drive the loop where the model calls `askUser` with a MALFORMED envelope
/// (an object that is not an elicitation): the typed refusal must come back as
/// an error tool result (never a fabricated outcome, never a crash), and the
/// loop must continue to the next turn. Returns flat.
let runAskRefusedProbe
  ()
  : JS.Promise<
      {| HaltReason: string
         RefusalThreaded: bool
         RefusalCode: string
         AskRowMounted: bool |}
     >
  =
  let threaded = ref ""
  let calls = ref 0

  let script =
    [ AgentOutcome.Ok(
        [ AgentContentBlock.Text "Asking (badly)."
          AgentContentBlock.ToolUse("tu-bad", "askUser", createObj [ "question" ==> "which colour?" ]) ],
        AgentStopReason.ToolUse,
        Some { InputTokens = 10; OutputTokens = 5 }
      )
      AgentOutcome.Ok(
        [ AgentContentBlock.Text "Understood – proceeding without asking." ],
        AgentStopReason.EndTurn,
        Some { InputTokens = 8; OutputTokens = 3 }
      ) ]

  let provider =
    { new IAgenticProvider with
        member _.SendAgentic(request) =
          async {
            let i = calls.Value
            calls.Value <- i + 1

            if i = 1 then
              threaded.Value <-
                request.Messages
                |> List.collect _.Content
                |> List.tryPick (fun b ->
                  match b with
                  | AgentContentBlock.ToolResult(_, content, _) -> Some content
                  | _ -> None)
                |> Option.defaultValue ""

            let idx = min i (List.length script - 1)
            return List.item idx script
          } }

  let timeline = ResizeArray<TimelineEntry>()
  let deps = probeDeps provider defaultBudget timeline

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "Build me a thing." deps

    let code =
      try
        let parsed = jsonParseRaw threaded.Value
        string parsed?code
      with _ ->
        ""

    return
      {| HaltReason = haltReasonStr result.HaltReason
         RefusalThreaded = threaded.Value.Contains "elicitation envelope refused"
         RefusalCode = code
         AskRowMounted = timeline |> Seq.exists (fun e -> timelineKind e = "ask") |}
  }
  |> Async.StartAsPromise
