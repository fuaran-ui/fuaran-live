module Fuaran.Live.RunMode

// ============================================================================
//  RUN MODE — a generated emission stops being a picture and becomes an app.
//
//  Everywhere else in this playground a decoded tree is RENDERED: you see what
//  the model emitted. Here the same tree is RUN. A click becomes a `LiveEvent`,
//  the bounded interpreter folds it against a state store, the tree's bindings
//  re-resolve, and the renderer draws the result — in the page, with no account,
//  no key and no server round-trip.
//
//  Nothing in this file is an interpreter. The fold, the re-resolution, the
//  budget and the closed effect vocabulary all come from the published
//  `Fuaran.Program.Runtime` package, which shares them with the server
//  placement. That sharing is the claim worth making: the tree you watch run
//  here behaves the way it would behave on a server, because it is the same
//  code, not a browser-shaped reimplementation of it.
//
//  ── What makes this safe to point at an LLM's output ────────────────────────
//  The tree is untrusted by construction, and three separate things bound it:
//
//    1. The interpreter is TOTAL and never invokes a closure the tree carries.
//       A decoded tree's handlers are inert sentinels; the fold reads the
//       action's DATA and nothing else.
//    2. Every step passes a dispatch gate that names the action arms this host
//       will interpret at all, positively. Anything else is refused and said so.
//    3. Effects pass a CLOSED, default-deny registry. The vocabulary is a fixed
//       DU — an emission cannot invent an effect — and of that vocabulary this
//       host registers only the arms that cannot leave the page. The rest are
//       refused, and the refusal is RENDERED rather than swallowed.
//
//  Point 3 is the demonstration, not merely the precaution. A model that emits
//  a `Navigate` here does not get a broken button and a silent console: it gets
//  a visible, recorded denial naming the capability this host does not offer.
//  "Nothing happened" and "this host refused that" are different facts, and a
//  playground whose whole subject is what a generated app may do has to show
//  the difference.
// ============================================================================

open Fable.Core
open Feliz
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Renderer
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.Program.Bounded
open Fuaran.Program.Runtime

// ─── the two effect performers this host offers ──────────────────────────────
//
// Both stay inside the page: one writes to the clipboard the visitor's own
// click asked to fill, the other moves focus. Neither can reach a network
// origin, which is why they are the two that can be offered at all here.

[<Emit("navigator.clipboard && navigator.clipboard.writeText($0)")>]
let private writeClipboard (text: string) : unit = jsNative

[<Emit("(function(){ var e = document.getElementById($0); if (e && e.focus) e.focus(); })()")>]
let private focusNode (nodeId: string) : unit = jsNative

/// Render an arbitrary store value for the state panel. The bounded store holds
/// the structural `obj` shapes `JVal` lowers to, so JSON is the honest
/// projection — it shows the value's SHAPE, not a stringified guess at it.
[<Emit("(function(v){ try { return JSON.stringify(v); } catch (e) { return String(v); } })($0)")>]
let private showValue (value: obj) : string = jsNative

/// The clicked element's nearest Fuaran-addressed ancestor, or `null`. The
/// renderer stamps `data-fuaran-node-id` on every node it draws, which is how a
/// delegated listener recovers the node identity an event belongs to.
[<Emit("$0 && $0.target && $0.target.closest ? $0.target.closest('[data-fuaran-node-id]') : null")>]
let private closestNode (ev: obj) : obj = jsNative

[<Emit("$0 ? $0.getAttribute('data-fuaran-node-id') : null")>]
let private nodeIdOf (el: obj) : string = jsNative

[<Emit("$0 !== null && $0 !== undefined")>]
let private isSome (x: obj) : bool = jsNative

// ─── the host's declared posture ─────────────────────────────────────────────

/// The effect arms this host PERFORMS. Everything the closed `ClientEffect`
/// vocabulary can name and this set omits — `Navigate`, `PushState`,
/// `Download`, `ReadFileBody` — is absent from this host rather than
/// present-and-refused, and a tree reaching for one gets a recorded
/// `Unregistered` denial.
///
/// The omissions are not an oversight to be closed later. Three of them move
/// the visitor's browser somewhere the emission chose, and the fourth reads a
/// local file's body into a tree an LLM wrote. A page that runs untrusted
/// emissions with no trust boundary behind it has no business offering any of
/// them, and saying so out loud is more useful to a reader than offering them
/// behind a policy.
let performedEffects: Set<string> = Set.ofList [ "WriteToClipboard"; "Focus" ]

/// The action arms this host will interpret. Positive list, and it names
/// exactly what the bounded interpreter has a meaning for — including the
/// effect-emitting arms, deliberately: refusing `Navigate` HERE would mean the
/// step never reaches the effect registry, and the denial the visitor needs to
/// see would never be recorded. The two gates answer different questions, and
/// this one must let the second one speak.
let private canDispatch (action: Action<obj>) : bool =
  match action with
  | Action.SetState _
  | Action.Chain _
  | Action.Navigate _
  | Action.WriteToClipboard _
  | Action.ReadFileBody _ -> true
  | _ -> false

[<Emit("console.warn('[fuaran-live] effect refused', $0)")>]
let private warnRefused (detail: string) : unit = jsNative

/// The denial sink the playground wires. The journal is what the visitor reads;
/// this line is what somebody debugging their own emission greps for, so both
/// exist rather than one standing in for the other. It carries the description
/// only — never the effect's payload, which came off the wire.
let consoleDenialSink (denial: EffectDenial) : unit =
  warnRefused (EffectDenial.describe denial)

let private registry (onDenied: EffectDenial -> unit) : EffectRegistry =
  EffectRegistry.denyAll
  |> EffectRegistry.register "WriteToClipboard" (fun fx ->
    match fx with
    | ClientEffect.WriteToClipboard text -> writeClipboard text
    | _ -> ())
  |> EffectRegistry.register "Focus" (fun fx ->
    match fx with
    | ClientEffect.Focus nodeId -> focusNode nodeId
    | _ -> ())
  // Registration alone does not permit — the gate still decides, and it decides
  // by the same declared set, so the two facts cannot drift apart.
  |> EffectRegistry.withGate (fun name -> Set.contains name performedEffects)
  |> EffectRegistry.onDenied onDenied

// ─── the observable record of a run ──────────────────────────────────────────

/// One step, as the visitor sees it. Effects, denials, refusals and diagnostics
/// are kept apart rather than flattened into a message list: they are answers to
/// different questions, and a teaching surface that blurs them teaches the blur.
type StepRecord =
  {
    Seq: int
    NodeId: string
    Event: string
    /// Effects the fold REACHED, whatever this host then did about them.
    Effects: string list
    /// What this host declined, and why.
    Denials: string list
    /// Why the step produced no new tree at all, if it did not.
    Rejected: string option
    /// Actions that are inert or refused on the bounded path.
    Diagnostics: string list
    /// Ops the step produced — the same journal a server-run step would write.
    OpCount: int
  }

type State =
  {
    Program: Program
    /// Newest first.
    Steps: StepRecord list
    Seq: int
    /// Where the program's `OnApply` seam deposits the op count of the step
    /// currently being taken. A cell created once in `start` and closed over by
    /// the services, rather than a wrapper re-applied per step: the program
    /// carries its services forward, so wrapping them on each step would nest a
    /// new closure per interaction for the rest of the session.
    LastOps: int ref
  }

let private describeEffect (fx: ClientEffect) : string = ClientEffect.kind fx

let private describeDiagnostic (d: BoundedDiagnostic) : string = BoundedDiagnostic.describe d

let private describeReject (r: ProgramReject) : string =
  match r with
  | Gate reason -> RejectReason.describe reason
  | BudgetExceeded detail -> detail

// ─── starting and stepping ───────────────────────────────────────────────────

/// Start running the session's folded tree.
///
/// `WireTree.ofDecoded` is the safe direction of the marker and the correct one
/// here: this tree reached the session through the strict wire decoder, so its
/// closures already ARE inert sentinels — the marker records that fact rather
/// than asserting anything new about it. The bounded loop never invokes one
/// either way.
let start (onDenied: EffectDenial -> unit) (tree: Node<obj>) : State =
  let lastOps = ref 0

  let services =
    { ProgramServices.create ignore with
        // The Elmish loop owns rendering: `Program.Resolved` is read out of the
        // model each frame, so a push-render seam here would be a second,
        // racing source of truth for the same picture.
        CanDispatch = canDispatch
        Effects = registry onDenied
        OnApply = fun applied -> lastOps.Value <- List.length applied }

  { Program = Program.mkBounded services BindingResolver.empty (WireTree.ofDecoded tree)
    Steps = []
    Seq = 0
    LastOps = lastOps }

/// Step the program with one interaction and record what happened.
let step (state: State) (nodeId: string) (event: string) : State =
  let ev: LiveEvent =
    { ConnId = "client-only"
      NodeId = nodeId
      Event = event
      Payload = Map.empty
      LastSeq = 0 }

  // A refused step never reaches `OnApply`, so the cell is cleared first rather
  // than left holding the previous step's count.
  state.LastOps.Value <- 0
  let next, out = Program.handleEvent state.Program ev
  let seq = state.Seq + 1

  let record =
    { Seq = seq
      NodeId = nodeId
      Event = event
      Effects = out.Effects |> List.map describeEffect
      Denials = out.Denials |> List.map EffectDenial.describe
      Rejected = out.Rejected |> Option.map describeReject
      Diagnostics = out.Diagnostics |> List.map describeDiagnostic
      OpCount = state.LastOps.Value }

  { state with
      Program = next
      Steps = record :: state.Steps
      Seq = seq }

/// The node id an interaction landed on, if the click reached a rendered node.
let nodeIdFromEvent (browserEvent: obj) : string option =
  let el = closestNode browserEvent
  if isSome el then Some(nodeIdOf el) else None

// ─── the panes ───────────────────────────────────────────────────────────────

/// The running app itself, resolved against the live store.
///
/// The renderer's own dispatch callback is `ignore` on purpose: interactivity
/// here belongs to the bounded loop, which owns it through the delegated
/// listener on the wrapper. Two dispatch paths over one tree would mean two
/// answers to "what did that click do".
let runningApp (state: State) (onInteract: string -> unit) : ReactElement =
  let sources =
    { BindingResolver.empty with
        State = state.Program.Store.State }

  Html.div
    [ prop.className "fl-run-surface"
      prop.onClick (fun ev ->
        match nodeIdFromEvent (box ev) with
        | Some nodeId -> onInteract nodeId
        | None -> ())
      prop.children [ Render.renderWithSources sources ignore state.Program.Resolved ] ]

/// The live `$state` map — the bounded algebra's whole memory, on screen.
let statePanel (state: State) : ReactElement =
  let entries = state.Program.Store.State |> Map.toList

  let body =
    if List.isEmpty entries then
      Html.div
        [ prop.className "fl-run-state-empty"
          prop.text "Empty — no bounded action has written a key yet." ]
    else
      Html.dl
        [ prop.className "fl-run-state-list"
          prop.children
            [ for key, value in entries do
                Html.dt [ prop.className "fl-run-state-key"; prop.text key ]
                Html.dd [ prop.className "fl-run-state-value"; prop.text (showValue value) ] ] ]

  Html.div
    [ prop.className "fl-run-state"
      prop.children [ Html.div [ prop.className "fl-run-state-title"; prop.text "$state" ]; body ] ]

/// The step journal — what each interaction reached, and what this host did
/// about it. A denial is styled as a first-class outcome rather than an error,
/// because it IS one: the host declining a capability is the default-deny
/// posture working, not the app breaking.
let private line (cls: string) (text: string) : ReactElement =
  Html.div [ prop.className cls; prop.text text ]

let private stepLines (record: StepRecord) : ReactElement list =
  let head =
    line "fl-run-step-head" (sprintf "%d · %s on %s" record.Seq record.Event record.NodeId)

  let outcome =
    match record.Rejected with
    | Some reason -> line "fl-run-step-reject" ("refused — " + reason)
    | None -> line "fl-run-step-ops" (sprintf "%d op(s) journalled" record.OpCount)

  [ head
    outcome
    yield!
      record.Effects
      |> List.map (fun e -> line "fl-run-step-effect" ("effect · " + e))
    yield!
      record.Denials
      |> List.map (fun d -> line "fl-run-step-denial" ("denied · " + d))
    yield!
      record.Diagnostics
      |> List.map (fun d -> line "fl-run-step-diagnostic" ("inert · " + d)) ]

let journal (state: State) : ReactElement =
  let body =
    if List.isEmpty state.Steps then
      line "fl-run-journal-empty" "Click something in the running app — every step is recorded here."
    else
      Html.ol
        [ prop.className "fl-run-journal-list"
          prop.children
            [ for record in state.Steps ->
                Html.li
                  [ prop.className "fl-run-step"
                    prop.key (string record.Seq)
                    prop.children (stepLines record) ] ] ]

  Html.div
    [ prop.className "fl-run-journal"
      prop.children [ line "fl-run-journal-title" "Steps"; body ] ]

/// The one-line statement of what this host will and will not do, shown beside
/// the running app so the posture is legible BEFORE a denial appears rather
/// than only after one.
let posture: ReactElement =
  Html.div
    [ prop.className "fl-run-posture"
      prop.children
        [ Html.span
            [ prop.className "fl-run-posture-yes"
              prop.text "performs: WriteToClipboard, Focus" ]
          Html.span
            [ prop.className "fl-run-posture-no"
              prop.text "refuses: Navigate, PushState, Download, ReadFileBody" ] ] ]
