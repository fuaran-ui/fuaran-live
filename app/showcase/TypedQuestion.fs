module Fuaran.Showcase.TypedQuestion

// ============================================================================
//  The Typed Question – an agent asks AS a live form and gets back a typed,
//  contract-checked answer, never prose. Pillar: "the machine can see the UI"
//  (its conversational face – WIRE_FORMAT.md §18, the elicitation envelope).
//
//  The whole exchange is wire data, end to end, through the SHIPPED codec
//  (`Fuaran.UI.OpStream.Abstractions.Elicitation` – the same module certified
//  against the shared wire-format-fixtures corpus):
//
//    1. The page ENCODES a real elicitation envelope – a question tree + a
//       declared answer contract + a default + a timeout – to canonical bytes,
//       then DECODES those bytes back and renders the DECODED artefact. What
//       you fill in is what arrived on the wire, not an F# object that never
//       left the page.
//    2. No closure crossed the wire: the form's inputs work via the renderer's
//       declarative write-back (a `Binding.State` value binding), and even the
//       submit button is data – a wire-survivable `Action.SetState` the page
//       (playing the presenting host) observes.
//    3. Submitting runs the real §18.4 gate: the committed state becomes a
//       canonical answer object, `decodeAnswerJson` types it, and
//       `validateAnswer` checks it against the contract. A conforming answer
//       becomes an `Answered` outcome envelope; a non-conforming one is
//       refused with the actual typed error – it never reaches the agent.
//    4. The outcome set is closed: Answered / Declined / TimedOut / Superseded,
//       each shown as its real canonical outcome bytes. `timeoutMs` is data –
//       the PAGE's clock (the presenting host) dispatches TimedOut, per spec.
//
//  Honest scope: the private resolution runtime (policy gate, journal, broker)
//  is a host-side concern; this page plays the presenting host in-tab. The
//  envelope/outcome/answer codecs and every refusal shown are the shipped,
//  fixture-certified implementations running under Fable in the browser.
// ============================================================================

open Fable.Core
open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module Decode = Fuaran.UI.Ops.JsonDecode
module Elc = Fuaran.UI.OpStream.Abstractions.Elicitation

// ─── Interop ─────────────────────────────────────────────────────────────────

[<Emit("window.setTimeout($0, $1)")>]
let private setTimeoutJs (callback: unit -> unit) (ms: int) : int = jsNative

[<Emit("window.clearTimeout($0)")>]
let private clearTimeoutJs (handle: int) : unit = jsNative

// ─── The state keys (the contract's stateKeys – one namespace, tq-prefixed) ──

let private canaryKey = "tq-canary"
let private envKey = "tq-environment"
let private noteKey = "tq-note"

/// The wire-survivable commit signal: the form's own submit button is
/// `Action.SetState(commitKey, JStr <elicitation id>)` – pure data on the
/// wire; the page observes the key and runs the §18.4 gate.
let private commitKey = "tq-commit"

/// Post-resolution freeze, also pure data: the form's `Disabled` slot is
/// `Binding.State(frozenKey n, false)`; resolving writes `true` and the
/// renderer's `<fieldset disabled>` cascade freezes the question.
let private frozenKey (round: int) : string = sprintf "tq-frozen-%d" round

let private watchedKeys =
  Set.ofList [ canaryKey; envKey; noteKey; commitKey; frozenKey 1; frozenKey 2 ]

let private envelopeId (round: int) : string = sprintf "elc-deploy-%d" round

// ─── The question, AS a Fuaran tree (everything on it rides the wire) ────────

let private askTree (round: int) : Node<obj> =
  let heading, intro =
    if round = 1 then
      "Deploy decision – build 2117",
      "_The agent says:_ I'm ready to roll out **build 2117**. Set the parameters and send – I read the **typed** answer, never your prose."
    else
      "Re-asked – production caps the canary at 25%",
      "_The agent says:_ Policy tightened while you were deciding – I've **superseded** my first question with this one. Same form, **stricter contract**: the canary space is now `1–25`. Your draft carried over."

  Fuaran.card
    (sprintf "ask-card-%d" round)
    { Defaults.card with
        Heading = Some(TextSource.Literal heading)
        Children =
          [ Fuaran.markdown (sprintf "ask-intro-%d" round) intro
            Fuaran.form
              "ask-deploy"
              { Defaults.form with
                  Fields =
                    [ { Id = "tq-canary-field"
                        Label = TextSource.Literal "Canary share (%)"
                        Kind = FormFieldKind.Number(Some(Binding.State(canaryKey, Some 10.0)), None)
                        Required = true
                        Help =
                          Some(TextSource.Literal "A whole percentage. Try 12.5 – the contract says integers only.") }
                      { Id = "tq-env-field"
                        Label = TextSource.Literal "Environment"
                        Kind =
                          FormFieldKind.Choice(
                            Binding.Static(
                              Some
                                [ { Value = "staging"; Label = "Staging" }
                                  { Value = "production"
                                    Label = "Production" } ]
                            ),
                            Some(Binding.State(envKey, Some "staging")),
                            None
                          )
                        Required = true
                        Help = None }
                      { Id = "tq-note-field"
                        Label = TextSource.Literal "Note for the log"
                        Kind = FormFieldKind.Text(Some(Binding.State(noteKey, Some "")), None)
                        Required = false
                        Help = Some(TextSource.Literal "Optional – up to 120 characters.") } ]
                  OnSubmit = Action.SetState(commitKey, Some(JStr(envelopeId round)), None)
                  SubmitLabel = TextSource.Literal "Send the typed answer"
                  Disabled = Some(Binding.State(frozenKey round, Some false)) } ] }

// ─── The answer contract (which state entries ARE the answer, typed) ─────────

let private contractFor (round: int) : AnswerContract =
  let canaryMax = if round = 1 then 50 else 25

  { Fields =
      [ { Name = "canary"
          NodeId = NodeId "ask-deploy"
          StateKey = canaryKey
          Space = IntRange(1, canaryMax)
          Required = true }
        { Name = "environment"
          NodeId = NodeId "ask-deploy"
          StateKey = envKey
          Space = Enum [ "staging"; "production" ]
          Required = true }
        { Name = "note"
          NodeId = NodeId "ask-deploy"
          StateKey = noteKey
          Space = StringLen(0, 120)
          Required = false } ] }

let private timeoutSeconds = 120

let private envelopeFor (round: int) : ElicitationEnvelope =
  { ElicitationId = envelopeId round
    Tree = askTree round
    Contract = contractFor round
    TimeoutMs = Some(timeoutSeconds * 1000)
    Default = Some(Map [ "canary", AnswerValue.Int 10; "environment", AnswerValue.Str "staging" ]) }

// ─── Encode → decode: the page renders the DECODED artefact ─────────────────

type private Round =
  { Index: int
    Wire: string
    Envelope: ElicitationEnvelope }

let private mkRound (round: int) : Result<Round, Decode.DecodeError> =
  Elc.encodeEnvelope (envelopeFor round)
  |> Result.bind (fun wire ->
    Elc.decodeEnvelope wire
    |> Result.map (fun env ->
      { Index = round
        Wire = wire
        Envelope = env }))

let private roundsResult: Result<Round list, Decode.DecodeError> =
  match mkRound 1, mkRound 2 with
  | Ok a, Ok b -> Ok [ a; b ]
  | Error e, _
  | _, Error e -> Error e

// ─── Rendering the decoded tree with the real state substrate ────────────────
//  `BrowserRuntime` routes the decoded submit button's `Action.SetState` into
//  the observable StateStore (the diagnostic default would only warn), and the
//  handler-free fields land their edits there via the write-back default.

let private browserRuntime: Runtime.IFuaranRuntime = BrowserRuntime.create ()

let private renderLive (node: Node<obj>) : ReactElement =
  Render.render
    { Sources =
        { BindingResolver.empty with
            State = StateStore.snapshot () }
      Runtime = browserRuntime
      VisAdapter = VisAdapter.noOp<obj>
      Dispatch = ignore
      TelemetrySink = None
      InErrorBoundary = false
      Fragments = Render.collectFragments Map.empty node
      ExpandingFragments = Set.empty
      Scope = None
      SessionContext = Map.empty }
    node

let private renderStatic (n: Node<'msg>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

// ─── The presenting host's answer capture ────────────────────────────────────
//  Committed state → one canonical answer object. Classification is by VALUE
//  (§18.2): a whole-valued number is an integer; the REAL classification and
//  every verdict below run through the shipped `decodeAnswerJson` +
//  `validateAnswer` – this builder only lays out the bytes.

let private wholeInt32 (f: float) : bool =
  f >= -2147483648.0 && f <= 2147483647.0 && f = floor f

let private readAnswerJson () : string =
  let entries =
    [ match StateStore.get canaryKey with
      | Some v ->
        let f = unbox<float> v

        if not (System.Double.IsNaN f) then
          if wholeInt32 f then
            yield "canary", JInt(int f)
          else
            yield "canary", JFloat f
      | None -> ()
      match StateStore.get envKey with
      | Some v ->
        let s = unbox<string> v

        if s <> "" then
          yield "environment", JStr s
      | None -> ()
      match StateStore.get noteKey with
      | Some v ->
        let s = unbox<string> v

        if s <> "" then
          yield "note", JStr s
      | None -> () ]

  Canon.render (JObj(List.sortBy fst entries))

let private seedStore () : unit =
  // The presenting host pre-fills the envelope's declared `default` (§18.1)
  // and clears the signal keys. The commit signal is cleared by OVERWRITING
  // with the empty sentinel, never `remove`: string state persists to
  // localStorage and `Get` re-hydrates a removed key from there, so a removed
  // commit signal would rise from persistence as a ghost and re-fire the
  // submit gate on every state tick ("" hydrates as absent by contract).
  StateStore.set canaryKey (box 10.0)
  StateStore.set envKey (box "staging")
  StateStore.set noteKey (box "")
  StateStore.set commitKey (box "")
  StateStore.remove (frozenKey 1)
  StateStore.remove (frozenKey 2)

// ─── Display helpers ─────────────────────────────────────────────────────────

let private spaceText (s: ValueSpace) : string =
  match s with
  | IntRange(lo, hi) -> sprintf "integer %d–%d" lo hi
  | FloatRange(lo, hi) -> sprintf "number %s–%s" (string lo) (string hi)
  | StringLen(lo, hi) -> sprintf "string, %d–%d chars" lo hi
  | Enum values -> "one of " + String.concat " | " values
  | AnyString -> "any string"

let private answerValueText (v: AnswerValue) : string =
  match v with
  | AnswerValue.Int i -> sprintf "%d (integer)" i
  | AnswerValue.Float f -> sprintf "%s (number)" (string f)
  | AnswerValue.Str s -> sprintf "\"%s\" (string)" s

let private outcomeKindName (o: ElicitationOutcome) : string =
  match o with
  | ElicitationOutcome.Answered _ -> "Answered"
  | ElicitationOutcome.Declined -> "Declined"
  | ElicitationOutcome.TimedOut -> "TimedOut"
  | ElicitationOutcome.Superseded _ -> "Superseded"

let private errorView (prefix: string) (e: Decode.DecodeError) : ReactElement =
  Html.div
    [ prop.className "tq-refusal"
      prop.children
        [ Html.div
            [ prop.className "tq-refusal-head"
              prop.children
                [ Html.span [ prop.className "tq-refusal-mark"; prop.text "⛔ REFUSED" ]
                  Html.span [ prop.className "tq-refusal-layer"; prop.text prefix ] ] ]
          Html.div
            [ prop.className "tq-refusal-body"
              prop.children
                [ Html.code [ prop.className "tq-refusal-code"; prop.text e.Code ]
                  Html.code [ prop.className "tq-refusal-path"; prop.text e.Path ]
                  Html.span [ prop.className "tq-refusal-detail"; prop.text e.Message ] ] ] ] ]

// ─── The cheat chips – non-conforming answers, run through the real gate ─────

type private Cheat =
  { Chip: string
    Story: string
    Raw: string }

let private cheats: Cheat list =
  [ { Chip = "Smuggle an undeclared key"
      Story = "The contract's answer surface is closed by shape – a key it never declared is refused, not ignored."
      Raw = "{\"canary\":10,\"environment\":\"staging\",\"adminOverride\":\"grant\"}" }
    { Chip = "Send the number as prose"
      Story = "\"ten\" is a string; the declared space is an integer range. No coercion – a type mismatch."
      Raw = "{\"canary\":\"ten\",\"environment\":\"staging\"}" }
    { Chip = "Blow the canary range"
      Story = "90 is a perfectly good integer – outside the declared space."
      Raw = "{\"canary\":90,\"environment\":\"staging\"}" }
    { Chip = "Invent an environment"
      Story = "The enum closes the value set; an undeclared member is out of space."
      Raw = "{\"canary\":10,\"environment\":\"prod-eu-2\"}" }
    { Chip = "Omit a required field"
      Story = "`environment` is declared required; an answer without it never reaches the agent."
      Raw = "{\"canary\":10}" }
    { Chip = "Send a fractional canary"
      Story = "12.5 is a number, but not an integer – classification is by value, identically in every host."
      Raw = "{\"canary\":12.5,\"environment\":\"staging\"}" } ]

// ─── The forge chips – tampered envelopes, run through the real decode gate ──

type private Forge =
  { Chip: string
    Story: string
    Mutate: string -> string }

let private forges: Forge list =
  [ { Chip = "Bump the version tag"
      Story = "The envelope's evolution is explicit – an unknown `$elicitation` version is refused, never tolerated."
      Mutate = fun w -> w.Replace("\"$elicitation\":\"1\"", "\"$elicitation\":\"9\"") }
    { Chip = "Smuggle an envelope key"
      Story =
        "Every object position in the artefact rejects undeclared keys – it is a protocol artefact, not a forward-compat carrier."
      Mutate = fun w -> w.Replace("{\"$elicitation\"", "{\"$agentMemo\":\"do it quietly\",\"$elicitation\"") }
    { Chip = "Point the contract at a ghost node"
      Story = "Every contract field must name a node that exists in the question tree."
      Mutate = fun w -> w.Replace("\"nodeId\":\"ask-deploy\"", "\"nodeId\":\"ghost-form\"") } ]

// ─── The page ────────────────────────────────────────────────────────────────

type private Resolution =
  { EnvId: string
    Kind: string
    Wire: string }

[<ReactComponent>]
let private TypedQuestionView (rounds: Round list) : ReactElement =
  let tick = StateStore.useStateKeys watchedKeys
  let activeIx, setActiveIx = React.useState 0
  let resolutions, setResolutions = React.useState ([]: Resolution list)

  let verdict, setVerdict =
    React.useState (None: Result<Map<string, AnswerValue>, Decode.DecodeError> option)

  let remaining, setRemaining = React.useState timeoutSeconds

  let cheatVerdict, setCheatVerdict =
    React.useState (None: (Cheat * Result<Map<string, AnswerValue>, Decode.DecodeError>) option)

  let forgeVerdict, setForgeVerdict =
    React.useState (None: (Forge * Result<unit, Decode.DecodeError>) option)

  let active = List.item activeIx rounds
  let activeId = active.Envelope.ElicitationId

  let activeResolved = resolutions |> List.exists (fun r -> r.EnvId = activeId)

  // Seed the presenting host's store once on mount; sweep the keys on unmount
  // so the page leaves nothing behind in the shared default store.
  React.useEffectOnce (
    (fun () ->
      seedStore ()

      (fun () ->
        // Neutralise the persisted commit signal before sweeping (see
        // seedStore – `remove` leaves the localStorage copy behind).
        StateStore.set commitKey (box "")

        for key in watchedKeys do
          StateStore.remove key))
    : unit -> unit -> unit
  )

  let resolve (outcome: ElicitationOutcome) : unit =
    let wire =
      Elc.encodeOutcome
        { ElicitationId = activeId
          Outcome = outcome }

    setResolutions (
      resolutions
      @ [ { EnvId = activeId
            Kind = outcomeKindName outcome
            Wire = wire } ]
    )

    StateStore.set (frozenKey active.Index) (box true)

  let submit () : unit =
    let raw = readAnswerJson ()

    match Elc.decodeAnswerJson raw with
    | Error e -> setVerdict (Some(Error e))
    | Ok answer ->
      match Elc.validateAnswer active.Envelope.Contract answer with
      | Error e -> setVerdict (Some(Error e))
      | Ok() ->
        setVerdict (Some(Ok answer))
        resolve (ElicitationOutcome.Answered answer)

  // The decoded submit button wrote the commit signal (`Action.SetState` –
  // pure wire data). Observe it, clear it, run the §18.4 gate.
  React.useEffect (
    (fun () ->
      match StateStore.get commitKey with
      | Some v when unbox<string> v = activeId ->
        // Consume by overwriting with the empty sentinel (never `remove` –
        // the persisted copy would re-hydrate and re-fire; see seedStore).
        StateStore.set commitKey (box "")

        if not activeResolved then
          submit ()
      | _ -> ()),
    [| box tick |]
  )

  // The presenting host's clock – `timeoutMs` is data; no codec reads a clock.
  React.useEffect (
    ((fun () ->
      if activeResolved then
        fun () -> ()
      elif remaining <= 0 then
        resolve ElicitationOutcome.TimedOut
        fun () -> ()
      else
        let handle = setTimeoutJs (fun () -> setRemaining (remaining - 1)) 1000
        fun () -> clearTimeoutJs handle)
    : unit -> unit -> unit),
    [| box remaining; box activeResolved; box activeIx |]
  )

  let supersede () : unit =
    resolve (ElicitationOutcome.Superseded(Some(envelopeId 2)))
    setActiveIx 1
    setVerdict None
    setRemaining timeoutSeconds
    StateStore.remove (frozenKey 2)

  let reset () : unit =
    setActiveIx 0
    setResolutions []
    setVerdict None
    setCheatVerdict None
    setForgeVerdict None
    setRemaining timeoutSeconds
    seedStore ()

  // ── the ask panel ──
  let askPanel =
    Html.div
      [ prop.className "tq-ask"
        prop.children
          [ Html.div
              [ prop.className "tq-ask-status"
                prop.children
                  [ Html.span [ prop.className "tq-ask-id"; prop.text (sprintf "elicitation %s" activeId) ]
                    (if activeResolved then
                       let kind =
                         resolutions
                         |> List.tryFind (fun r -> r.EnvId = activeId)
                         |> Option.map _.Kind
                         |> Option.defaultValue "?"

                       Html.span [ prop.className "tq-ask-resolved"; prop.text (sprintf "resolved · %s" kind) ]
                     else
                       Html.span
                         [ prop.className (
                             if remaining <= 15 then
                               "tq-ask-clock tq-ask-clock-late"
                             else
                               "tq-ask-clock"
                           )
                           prop.text (sprintf "TimedOut in %ds – the host's clock, not the codec's" remaining) ]) ] ]
            renderLive active.Envelope.Tree
            Html.div
              [ prop.className "tq-host-controls"
                prop.children
                  [ if not activeResolved then
                      Html.button
                        [ prop.className "tq-host-btn"
                          prop.title "Resolve this elicitation as Declined – a first-class typed outcome, not an error."
                          prop.text "Decline to answer"
                          prop.onClick (fun _ ->
                            setVerdict None
                            resolve ElicitationOutcome.Declined) ]

                      Html.button
                        [ prop.className "tq-host-btn"
                          prop.title "Fast-forward the presenting host's clock to the deadline."
                          prop.text "Fast-forward the clock"
                          prop.onClick (fun _ -> setRemaining 0) ]

                      if activeIx = 0 then
                        Html.button
                          [ prop.className "tq-host-btn tq-host-btn-super"
                            prop.title
                              "The agent replaces its question with a stricter one – the first resolves Superseded, and your draft carries over."
                            prop.text "Agent re-asks (tighter contract)"
                            prop.onClick (fun _ -> supersede ()) ]
                    else
                      Html.button
                        [ prop.className "tq-host-btn tq-host-btn-super"
                          prop.text "Reset the demo"
                          prop.onClick (fun _ -> reset ()) ] ] ]
            Html.details
              [ prop.className "tq-wire-drawer"
                prop.children
                  [ Html.summary
                      [ prop.text (
                          sprintf
                            "The envelope on the wire – %d canonical bytes, decoded before render"
                            active.Wire.Length
                        ) ]
                    Html.pre [ prop.className "wire-json"; prop.text active.Wire ] ] ] ] ]

  // ── the contract + live answer preview ──
  let contractPanel =
    Html.div
      [ prop.className "tq-contract"
        prop.children
          [ Html.h3 [ prop.text "The answer contract" ]
            Html.p
              [ prop.className "tq-muted"
                prop.text
                  "Declared on the envelope: which state entries constitute the answer, each typed by a value space. This is what the agent will hold your answer to." ]
            Html.table
              [ prop.className "tq-contract-table"
                prop.children
                  [ Html.thead
                      [ Html.tr
                          [ Html.th [ prop.text "field" ]
                            Html.th [ prop.text "space" ]
                            Html.th [ prop.text "required" ] ] ]
                    Html.tbody
                      [ for f in active.Envelope.Contract.Fields ->
                          Html.tr
                            [ Html.td [ Html.code [ prop.text f.Name ] ]
                              Html.td [ prop.text (spaceText f.Space) ]
                              Html.td [ prop.text (if f.Required then "yes" else "no") ] ] ] ] ]
            Html.h3 [ prop.text "The answer object, live" ]
            Html.p
              [ prop.className "tq-muted"
                prop.text
                  "Built from the form's committed state as you type – canonical bytes, ready for the gate. This is ALL the agent will ever see." ]
            Html.pre [ prop.className "tq-answer-json"; prop.text (readAnswerJson ()) ] ] ]

  // ── the submit verdict ──
  let verdictPanel =
    match verdict with
    | None -> Html.none
    | Some(Error e) -> errorView "Answer gate · §18.4 validateAnswer" e
    | Some(Ok answer) ->
      Html.div
        [ prop.className "tq-accepted"
          prop.children
            [ Html.div
                [ prop.className "tq-accepted-head"
                  prop.children
                    [ Html.span [ prop.className "tq-accepted-mark"; prop.text "✓ CONFORMING" ]
                      Html.span
                        [ prop.className "tq-accepted-layer"
                          prop.text "Answered – dispatched to the agent as typed data" ] ] ]
              Html.ul
                [ prop.className "tq-accepted-fields"
                  prop.children
                    [ for name, value in Map.toList answer ->
                        Html.li
                          [ Html.code [ prop.text name ]
                            Html.span [ prop.text (" = " + answerValueText value) ] ] ] ]
              Html.p
                [ prop.className "tq-muted"
                  prop.text
                    "No prose was parsed. The agent reads these fields by name, each already in its declared space." ] ] ]

  // ── the outcome ledger ──
  let ledger =
    if List.isEmpty resolutions then
      Html.none
    else
      Html.div
        [ prop.className "tq-ledger"
          prop.children
            [ Html.h3 [ prop.text "The outcome ledger – exactly one typed outcome each" ]
              Html.ul
                [ prop.className "tq-ledger-list"
                  prop.children
                    [ for r in resolutions ->
                        Html.li
                          [ prop.className "tq-ledger-row"
                            prop.children
                              [ Html.div
                                  [ prop.className "tq-ledger-head"
                                    prop.children
                                      [ Html.code [ prop.className "tq-ledger-kind"; prop.text r.Kind ]
                                        Html.span [ prop.className "tq-ledger-id"; prop.text r.EnvId ] ] ]
                                Html.pre [ prop.className "wire-json"; prop.text r.Wire ] ] ] ] ] ] ]

  // ── the cheat chips ──
  let cheatPanel =
    Html.div
      [ prop.className "tq-cheats"
        prop.children
          [ Html.h3 [ prop.text "Try to cheat the contract" ]
            Html.p
              [ prop.className "tq-muted"
                prop.text
                  "Each chip is a hand-crafted answer document run through the shipped gate against the live contract. None of them resolves the elicitation – a refused answer never reaches the agent." ]
            Html.div
              [ prop.className "tq-chips"
                prop.children
                  [ for c in cheats ->
                      Html.button
                        [ prop.className "tq-chip"
                          prop.title c.Story
                          prop.text c.Chip
                          prop.onClick (fun _ ->
                            let result =
                              Elc.decodeAnswerJson c.Raw
                              |> Result.bind (fun ans ->
                                Elc.validateAnswer active.Envelope.Contract ans |> Result.map (fun () -> ans))

                            setCheatVerdict (Some(c, result))) ] ] ]
            match cheatVerdict with
            | None -> Html.none
            | Some(c, Error e) ->
              Html.div
                [ prop.children
                    [ Html.pre [ prop.className "tq-cheat-payload"; prop.text c.Raw ]
                      errorView "Answer gate · §18.4 validateAnswer" e ] ]
            | Some(c, Ok _) ->
              Html.div
                [ prop.className "tq-cheat-passed"
                  prop.text (sprintf "\"%s\" conformed to the current contract – no refusal to show." c.Chip) ] ] ]

  // ── the forge chips ──
  let forgePanel =
    Html.div
      [ prop.className "tq-forges"
        prop.children
          [ Html.h3 [ prop.text "Forge the envelope itself" ]
            Html.p
              [ prop.className "tq-muted"
                prop.text
                  "Each chip takes the real envelope bytes above, flips one thing, and runs the shipped decoder. The refusal is the decoder's actual first error – the same one every conformant host surfaces." ]
            Html.div
              [ prop.className "tq-chips"
                prop.children
                  [ for f in forges ->
                      Html.button
                        [ prop.className "tq-chip"
                          prop.title f.Story
                          prop.text f.Chip
                          prop.onClick (fun _ ->
                            let mutated = f.Mutate active.Wire

                            let result = Elc.decodeEnvelope mutated |> Result.map ignore

                            setForgeVerdict (Some(f, result))) ] ] ]
            match forgeVerdict with
            | None -> Html.none
            | Some(_, Error e) -> errorView "Envelope gate · §18.4 decodeEnvelope" e
            | Some(f, Ok()) ->
              Html.div
                [ prop.className "tq-cheat-passed"
                  prop.text (sprintf "\"%s\" still decoded – the mutation missed." f.Chip) ] ] ]

  let honesty =
    Html.div
      [ prop.className "tq-honesty"
        prop.children
          [ Html.h3 [ prop.text "A real protocol, a real gate" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "The envelope, answer, and outcome codecs are the shipped language-tier implementations (the wire format's elicitation section), running under Fable in this tab – the same module certified against the shared cross-host conformance corpus." ]
                    Html.li
                      [ prop.text
                          "The form you filled in is the DECODED envelope – encoded to canonical bytes, decoded back, then rendered. No closure crossed the wire: the inputs work via the renderer's declarative write-back, and the submit button is a wire-survivable SetState action the page observes." ]
                    Html.li
                      [ prop.text
                          "Every refusal shown is the real typed error, code and path verbatim. An answer that fails the contract never reaches the agent – there is no prose fallback to sneak through." ]
                    Html.li
                      [ prop.text
                          "Honest scope: pairing outcomes with pending questions, policy-gating the dispatch, and journaling for resume are a host runtime's job – this page plays the presenting host in-tab. And per the spec, timeoutMs is data: the page's clock dispatched your TimedOut, not the codec." ] ] ] ] ]

  Html.div
    [ prop.className "tq-page"
      prop.children
        [ Html.h1 [ prop.className "tq-title"; prop.text "The Typed Question" ]
          Html.p
            [ prop.className "tq-lede"
              prop.text
                "An agent that needs your decision shouldn't get prose back – prose has to be re-parsed, and re-parsing is where meaning dies. Here the agent asks AS a live form, with a declared, typed answer contract. Fill it in honestly, or try to cheat – the contract decides what reaches the agent." ]
          askPanel
          contractPanel
          verdictPanel
          ledger
          cheatPanel
          forgePanel
          honesty ] ]

let page: ReactElement =
  match roundsResult with
  | Ok rounds -> TypedQuestionView rounds
  | Error e ->
    // Unreachable in practice – the envelope is authored in-repo – but if the
    // codec ever refuses it, show the honest typed error rather than a blank.
    renderStatic (
      Fuaran.callout
        "tq-broken"
        { Defaults.callout with
            Tone = ToneVariant.Critical
            Heading = Some(TextSource.Literal "The demo envelope failed its own gate")
            Body = TextSource.Literal(sprintf "%s at %s – %s" e.Code e.Path e.Message) }
    )
