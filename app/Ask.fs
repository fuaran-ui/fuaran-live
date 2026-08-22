module Fuaran.Live.Ask

// ============================================================================
//  The askUser host surface – the presenting side of a §18 elicitation
//  (Phase 465) inside the playground's agent loop.
//
//  When the model calls the `askUser` tool, the loop mounts an ASK ROW at that
//  spot in the transcript: the DECODED envelope tree rendered live in its own
//  state scope (so its `Binding.State` keys can't collide with panels or other
//  asks), plus host chrome – a "Send typed answer" button that runs the real
//  §18.4 gate (`decodeAnswerJson` + `validateAnswer`), a Decline button, and
//  the envelope's own timeout counting down. A non-conforming answer is
//  refused IN PLACE with the actual typed error and never reaches the agent;
//  a conforming one resolves the elicitation as an `Answered` outcome the
//  loop threads back to the model as the tool result.
//
//  Mirrors the showcase's Typed Question page, with two pragmatic deltas for
//  a live loop: (1) the submit path is host chrome rather than a
//  wire-survivable commit action, because the MODEL authors the envelope and
//  should not have to know a host-private commit-key convention; (2) each ask
//  renders in a per-elicitation scope (`StateStore.forScope`), the same
//  isolation the transcript's live panels use.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module Decode = Fuaran.UI.Ops.JsonDecode
module Elc = Fuaran.UI.OpStream.Abstractions.Elicitation

[<Emit("window.setTimeout($0, $1)")>]
let private setTimeoutJs (callback: unit -> unit) (ms: int) : int = jsNative

[<Emit("window.clearTimeout($0)")>]
let private clearTimeoutJs (handle: int) : unit = jsNative

/// One ask's host-side record: the decoded envelope + its canonical wire, and
/// the resolution once the human (or the clock) provides one – `(kind, wire)`.
type AskRecord =
  { Envelope: ElicitationEnvelope
    Wire: string
    Outcome: (string * string) option }

/// The renderer state scope an ask's tree lives in – per-elicitation, so its
/// `Binding.State` keys are isolated from the default store, every panel, and
/// every other ask (the same isolation `Panels.scopeId` gives panels).
let scopeId (elicitationId: string) : string = "ask/" + elicitationId

// ─── the answer builder (pure given a state getter) ─────────────────────────

/// §18.2 integer classification, identical to the codec's: a whole-valued
/// number within 32-bit signed range IS an integer.
let private wholeInt32 (f: float) : bool =
  f >= -2147483648.0 && f <= 2147483647.0 && f = floor f

/// Classify one committed state value as a canonical answer scalar, or `None`
/// to omit it (absent, empty string, NaN). A bool commits as "true"/"false" –
/// the contract's value-space vocabulary has no boolean, so a toggle-backed
/// field is declared as `enum ["true","false"]`.
let private classify (v: obj) : JVal option =
  if isNull v then
    None
  else
    match v with
    | :? string as s -> if s = "" then None else Some(JStr s)
    | :? bool as b -> Some(JStr(if b then "true" else "false"))
    | :? float as f ->
      if System.Double.IsNaN f then None
      elif wholeInt32 f then Some(JInt(int f))
      else Some(JFloat f)
    | _ -> None

/// Build the canonical answer object from the committed state, reading each
/// contract field's `stateKey` through `getState`. Pure given the getter –
/// headlessly testable; the row passes the ask's scoped store.
let buildAnswerJson (getState: string -> obj option) (contract: AnswerContract) : string =
  let entries =
    contract.Fields
    |> List.choose (fun f -> getState f.StateKey |> Option.bind classify |> Option.map (fun jv -> f.Name, jv))

  Canon.render (JObj(List.sortBy fst entries))

/// Flat headless wrapper: `stateJson` is a plain `{"<stateKey>": <scalar>}`
/// object standing in for the scoped store; the contract comes from the
/// envelope wire. Returns the canonical answer object JSON ("" on bad input).
let buildAnswerJsonFlat (stateJson: string) (envelopeWire: string) : string =
  match Elc.decodeEnvelope envelopeWire with
  | Error _ -> ""
  | Ok env ->
    match Json.parse stateJson with
    | Ok(JObj fields) ->
      let getState (key: string) : obj option =
        fields
        |> List.tryPick (fun (k, v) ->
          if k <> key then
            None
          else
            match v with
            | JStr s -> Some(box s)
            | JInt i -> Some(box (float i))
            | JFloat f -> Some(box f)
            | JBool b -> Some(box b)
            | _ -> None)

      buildAnswerJson getState env.Contract
    | _ -> ""

// ─── display helpers ─────────────────────────────────────────────────────────

let private spaceText (s: ValueSpace) : string =
  match s with
  | IntRange(lo, hi) -> sprintf "integer %d–%d" lo hi
  | FloatRange(lo, hi) -> sprintf "number %s–%s" (string lo) (string hi)
  | StringLen(lo, hi) -> sprintf "string, %d–%d chars" lo hi
  | Enum values -> "one of " + String.concat " | " values
  | AnyString -> "any string"

let outcomeKindName (o: ElicitationOutcome) : string =
  match o with
  | ElicitationOutcome.Answered _ -> "Answered"
  | ElicitationOutcome.Declined -> "Declined"
  | ElicitationOutcome.TimedOut -> "TimedOut"
  | ElicitationOutcome.Superseded _ -> "Superseded"

// ─── the ask row ─────────────────────────────────────────────────────────────

/// The write-back substrate for ask trees: routes the decoded controls' value
/// write-backs into this ask's scoped store, which `submit` reads to build the
/// answer. Lazy so importing this module headlessly (the flat test surface)
/// never touches the browser.
///
/// Deny-by-default is deliberate here, and this host wants no policy of its own.
/// The write-back is a tree-originated State write rather than a dispatched
/// action, so it never meets the gate — the question's controls stay fully live
/// under a runtime that refuses everything. Nothing in the ask flow needs a
/// dispatched action at all: `Send typed answer` and `Decline` are host chrome
/// below, and the answer is built by reading the scoped store directly.
///
/// So a model-emitted `Action.SetState` inside an ask tree IS refused, and that
/// is the point rather than a capability lost. An ask is a contract-bound
/// question whose answer must come from the person answering it; a tree that
/// could write its own answer keys would let the asker pre-fill the reply it
/// wanted. (Model-emitted panels take the opposite posture, for the opposite
/// reason — see `panelRuntime`: a panel is an app whose interactivity is the
/// whole product.) A refusal surfaces on the Warn channel as
/// `dispatch denied by policy gate: SetState(<key>)`; do not "repair" it to
/// `createPermissive`.
let private askRuntime = lazy (BrowserRuntime.create (): Runtime.IFuaranRuntime)

[<ReactComponent>]
let AskRow (record: AskRecord) (onResolve: ElicitationOutcome -> unit) : ReactElement =
  let env = record.Envelope
  let scope = scopeId env.ElicitationId
  let resolved = record.Outcome.IsSome

  let tick, setTick = React.useState 0
  let verdict, setVerdict = React.useState (None: Decode.DecodeError option)

  let remaining, setRemaining =
    React.useState (env.TimeoutMs |> Option.map (fun ms -> ms / 1000) |> Option.defaultValue -1)

  // Seed the scoped store once from the envelope's declared default (§18.1),
  // so what the form SHOWS is what an untouched submit sends.
  React.useEffectOnce (fun () ->
    match env.Default with
    | Some d ->
      let scoped = StateStore.forScope scope

      for f in env.Contract.Fields do
        match Map.tryFind f.Name d with
        | Some(AnswerValue.Int i) -> scoped.Set(f.StateKey, box (float i))
        | Some(AnswerValue.Float value) -> scoped.Set(f.StateKey, box value)
        | Some(AnswerValue.Str s) -> scoped.Set(f.StateKey, box s)
        | None -> ()
    | None -> ())

  // Re-render on any write within this ask's scope (the write-back liveness);
  // re-subscribing per tick keeps the closure fresh – the PanelRow idiom.
  React.useEffect (
    ((fun () -> (StateStore.forScope scope).Subscribe(fun () -> setTick (tick + 1))): unit -> unit -> unit),
    [| box env.ElicitationId; box tick |]
  )

  // The envelope's timeoutMs is DATA – this host's clock dispatches TimedOut.
  React.useEffect (
    ((fun () ->
      if resolved || remaining < 0 then
        fun () -> ()
      elif remaining = 0 then
        onResolve ElicitationOutcome.TimedOut
        fun () -> ()
      else
        let handle = setTimeoutJs (fun () -> setRemaining (remaining - 1)) 1000
        fun () -> clearTimeoutJs handle)
    : unit -> unit -> unit),
    [| box remaining; box resolved |]
  )

  let submit () =
    let scoped = StateStore.forScope scope
    let json = buildAnswerJson scoped.Get env.Contract

    match Elc.decodeAnswerJson json with
    | Error e -> setVerdict (Some e)
    | Ok answer ->
      match Elc.validateAnswer env.Contract answer with
      | Error e -> setVerdict (Some e)
      | Ok() ->
        setVerdict None
        onResolve (ElicitationOutcome.Answered answer)

  let statusBadge =
    match record.Outcome with
    | Some(kind, _) ->
      Html.span
        [ prop.className (
            if kind = "Answered" then
              "fl-ask-badge fl-ask-badge-ok"
            else
              "fl-ask-badge"
          )
          prop.text ("resolved · " + kind) ]
    | None ->
      if remaining >= 0 then
        Html.span
          [ prop.className "fl-ask-badge fl-ask-badge-live"
            prop.text (sprintf "awaiting your answer · times out in %ds" remaining) ]
      else
        Html.span
          [ prop.className "fl-ask-badge fl-ask-badge-live"
            prop.text "awaiting your answer" ]

  let refusal =
    match verdict with
    | None -> Html.none
    | Some e ->
      Html.div
        [ prop.className "fl-ask-refusal"
          prop.children
            [ Html.span [ prop.className "fl-ask-refusal-mark"; prop.text "⛔ refused" ]
              Html.code [ prop.className "fl-ask-refusal-code"; prop.text e.Code ]
              Html.code [ prop.className "fl-ask-refusal-path"; prop.text e.Path ]
              Html.span [ prop.className "fl-ask-refusal-detail"; prop.text e.Message ] ] ]

  let chrome =
    if resolved then
      Html.none
    else
      Html.div
        [ prop.className "fl-ask-chrome"
          prop.children
            [ Html.button
                [ prop.className "fl-btn fl-ask-send"
                  prop.title
                    "Runs the answer gate: your committed values become one canonical answer object, validated against the declared contract. Non-conforming answers are refused here and never reach the agent."
                  prop.text "Send typed answer"
                  prop.onClick (fun _ -> submit ()) ]
              Html.button
                [ prop.className "fl-btn ghost"
                  prop.title
                    "Resolve as Declined – a first-class typed outcome; the agent proceeds without your answer."
                  prop.text "Decline"
                  prop.onClick (fun _ ->
                    setVerdict None
                    onResolve ElicitationOutcome.Declined) ] ] ]

  let contractDrawer =
    Html.details
      [ prop.className "fl-ask-drawer"
        prop.children
          [ Html.summary [ prop.text "The answer contract – what the agent will hold your answer to" ]
            Html.table
              [ prop.className "fl-ask-contract"
                prop.children
                  [ Html.thead
                      [ Html.tr
                          [ Html.th [ prop.text "field" ]
                            Html.th [ prop.text "space" ]
                            Html.th [ prop.text "required" ] ] ]
                    Html.tbody
                      [ for f in env.Contract.Fields ->
                          Html.tr
                            [ Html.td [ Html.code [ prop.text f.Name ] ]
                              Html.td [ prop.text (spaceText f.Space) ]
                              Html.td [ prop.text (if f.Required then "yes" else "no") ] ] ] ] ]
            (match record.Outcome with
             | Some(_, wire) -> Html.pre [ prop.className "fl-code fl-ask-wire"; prop.text wire ]
             | None -> Html.none) ] ]

  Html.div
    [ prop.className (if resolved then "fl-ask fl-ask-resolved" else "fl-ask")
      prop.children
        [ Html.div
            [ prop.className "fl-ask-head"
              prop.children
                [ Html.span [ prop.className "fl-ask-title"; prop.text "❓ The agent asks – typed answer" ]
                  Html.code [ prop.className "fl-ask-id"; prop.text ("#" + env.ElicitationId) ]
                  statusBadge ] ]
          Html.div
            [ prop.className "fl-ask-body"
              prop.children
                [ Render.renderWithSourcesInScope scope BindingResolver.empty askRuntime.Value ignore env.Tree ] ]
          refusal
          chrome
          contractDrawer ] ]
