module Fuaran.Live.Console

// ============================================================================
//  THE CONSOLE — query and poke the live tree, in the page.
//
//  Everywhere else in this playground the emitted tree is something you LOOK at:
//  the preview renders it, the Source card projects it into ten languages, the
//  Editor walks it. The Console is where you INTERROGATE it — you ask the
//  running UI what a node's typed state is, what one of its bindings currently
//  resolves to, where it actually landed on screen — and where you can hand it a
//  `TreeOp` and watch the preview change. The point being made is that a Fuaran
//  UI is a queryable, controllable typed object, not opaque rendered markup.
//
//  ── What it drives, and why not `window.__fuaran` ───────────────────────────
//
//  The renderer already ships this exact method surface: `DebugGlobal`, the
//  in-page introspection REPL. What it ALSO ships is a deliberate production
//  gate — `register` publishes `window.__fuaran` only under a DEBUG build with
//  an explicit host opt-in, so in a release build the global is `undefined` and
//  the whole registration is dead-code-eliminated. That gate is the right
//  posture and this pane does not touch it: the shipped site registers no
//  global, and a visitor's DevTools console still finds `__fuaran` undefined.
//
//  Instead the pane calls `DebugGlobal.buildGlobalWith`, which builds the SAME
//  surface object the global would have been bound to and carries no gate of its
//  own. So the console answers with the shipped implementation — one definition
//  of `getNodeState`, not a second one written for a panel — while the published
//  global stays exactly as gated as it was.
//
//  ── The input is a call syntax, NOT JavaScript ──────────────────────────────
//
//  `parse` accepts a fixed, tiny grammar — `name(arg, …)` over the nine verbs
//  enumerated in `Query` — and refuses everything else by naming what it does
//  accept. There is no `eval`, no `Function`, no dynamic import: an input this
//  page cannot parse is an error message, never something that runs. That is
//  what makes the pane shippable on a public, keyless, no-account page.
//
//  ── The apply path is the navigator's gate, not a second one ────────────────
//
//  An `apply(...)` goes through the shipped `DebugGlobal.applyResult` pipeline:
//  the runtime's `CanDispatch(ApplyTreeOp …)` gate decides FIRST (default-deny
//  by shape — a refused op is never decoded), and only then does the host
//  handler run. That handler is `PropertyEditor.commitOpAs`, which is the
//  navigator's one edit gate — so "what the Console will accept" and "what the
//  Editor will accept" have one definition and cannot drift into two. An op the
//  navigator would refuse (one that INTRODUCES a validator defect) is refused
//  here with the same message, and the session is returned untouched.
//
//  Both of the pipeline's durable-emission seams are wired: a denial reaches an
//  `IFuaranTelemetrySink` and a permitted op reaches the journal callback, and
//  both land in the log below. So the log is the telemetry record of what this
//  console did rather than a bespoke narration beside one.
//
//  ── Honest scope ────────────────────────────────────────────────────────────
//
//  The log narrates THIS CONSOLE's activity — every call, every apply outcome,
//  every telemetry record the shipped pipeline emits into it. It does not
//  narrate the model-emission loop: nothing in this app routes emissions through
//  a telemetry sink, and the conversation transcript already reports them, so a
//  second copy here would be duplication rather than information.
//
//  Bindings resolve against `BindingResolver.empty`, which is what the live
//  preview renders with — so `getBindingValue` answers about the picture on
//  screen rather than about a hypothetical richer host.
//
//  Everything here is ephemeral and local: no network call, no storage write, no
//  global registration. The pane reads the tree already in memory and writes
//  back only into the session the visitor is already editing.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Telemetry.Abstractions

module Decode = Fuaran.UI.Ops.JsonDecode

// ─── the parsed call ─────────────────────────────────────────────────────────

/// The verbs this console accepts. A closed set, deliberately: the input is
/// parsed, never evaluated, so a verb that is not here cannot be reached.
[<RequireQualifiedAccess>]
type Query =
  | NodeState of nodeId: string
  | BindingValue of nodeId: string * slot: string
  | RenderedDom of nodeId: string
  | InspectTree
  | FindNodes of kind: string
  | Affordances of moduleId: string option
  | TreeRevision
  | Apply of opJson: string
  | Help

/// The one-line reference the parser refuses with, and the pane shows beside the
/// input. Kept beside `parse` so the two cannot disagree about what is accepted.
let accepted =
  "getNodeState(\"id\") · getBindingValue(\"id\", \"Slot\") · getRenderedDom(\"id\") · "
  + "inspectTree() · findNodes(\"Kind\") · getAffordances() · treeRevision() · "
  + "apply({…}) · help()"

let private stripPrefix (line: string) : string =
  let t = line.Trim()

  if t.StartsWith "window.__fuaran." then
    t.Substring(16).Trim()
  elif t.StartsWith "__fuaran." then
    t.Substring(9).Trim()
  else
    t

/// Split an argument list on TOP-LEVEL commas — a comma inside a quoted string
/// or a bracketed group belongs to the argument, not between two of them. A
/// naive `Split(',')` would tear `apply({"a":1,"b":2})` in half.
let private splitArgs (raw: string) : string list =
  if raw.Trim() = "" then
    []
  else
    let mutable quote = ' '
    let mutable depth = 0
    let mutable inQuote = false
    let mutable escaped = false
    let mutable current = ""
    let mutable acc: string list = []

    for ch in raw do
      if escaped then
        current <- current + string ch
        escaped <- false
      elif inQuote then
        current <- current + string ch

        if ch = '\\' then
          escaped <- true
        elif ch = quote then
          inQuote <- false
      elif ch = '"' || ch = '\'' then
        inQuote <- true
        quote <- ch
        current <- current + string ch
      elif ch = '{' || ch = '[' || ch = '(' then
        depth <- depth + 1
        current <- current + string ch
      elif ch = '}' || ch = ']' || ch = ')' then
        depth <- depth - 1
        current <- current + string ch
      elif ch = ',' && depth = 0 then
        acc <- acc @ [ current ]
        current <- ""
      else
        current <- current + string ch

    acc @ [ current ]

/// Strip one matched pair of surrounding quotes, if present. A node id is
/// written `"submit-btn"` in the examples, but a visitor who omits the quotes
/// meant the same thing.
let private unquote (arg: string) : string =
  let t = arg.Trim()

  if
    t.Length >= 2
    && ((t.StartsWith "\"" && t.EndsWith "\"") || (t.StartsWith "'" && t.EndsWith "'"))
  then
    t.Substring(1, t.Length - 2)
  else
    t

/// Split `name(body)` into its two halves. A bare `name` with no parentheses is
/// accepted as a nullary call, so `help` works as well as `help()`.
let private splitCall (line: string) : Result<string * string, string> =
  let t = stripPrefix line
  let openAt = t.IndexOf "("

  if t = "" then
    Error("Type a call. Accepted: " + accepted)
  elif openAt < 0 then
    Ok(t, "")
  elif not (t.EndsWith ")") then
    Error(sprintf "Unclosed call — '%s' is missing its ')'." t)
  else
    Ok(t.Substring(0, openAt).Trim(), t.Substring(openAt + 1, t.Length - openAt - 2))

let private arity (name: string) (expected: string) : Result<Query, string> =
  Error(sprintf "%s takes %s." name expected)

/// Parse one console line. Pure and total — every input is either a `Query` or a
/// message saying what this console accepts instead.
let parse (line: string) : Result<Query, string> =
  match splitCall line with
  | Error message -> Error message
  | Ok(name, body) ->
    // `apply` is the one verb whose argument is a JSON document rather than a
    // string literal, so it takes the body whole — splitting it into arguments
    // would be reading a structure as a list.
    if name = "apply" then
      let opJson = unquote body

      if opJson.Trim() = "" then
        arity "apply" "one TreeOp, as JSON"
      else
        Ok(Query.Apply opJson)
    else
      let args = splitArgs body |> List.map unquote |> List.filter (fun a -> a <> "")

      match name, args with
      | "getNodeState", [ id ] -> Ok(Query.NodeState id)
      | "getNodeState", _ -> arity "getNodeState" "one node id"
      | "getBindingValue", [ id; slot ] -> Ok(Query.BindingValue(id, slot))
      | "getBindingValue", _ -> arity "getBindingValue" "a node id and a slot name"
      | "getRenderedDom", [ id ] -> Ok(Query.RenderedDom id)
      | "getRenderedDom", _ -> arity "getRenderedDom" "one node id"
      | "inspectTree", [] -> Ok Query.InspectTree
      | "inspectTree", _ -> arity "inspectTree" "no arguments"
      | "findNodes", [ kind ] -> Ok(Query.FindNodes kind)
      | "findNodes", _ -> arity "findNodes" "one kind name"
      | "getAffordances", [] -> Ok(Query.Affordances None)
      | "getAffordances", [ moduleId ] -> Ok(Query.Affordances(Some moduleId))
      | "getAffordances", _ -> arity "getAffordances" "an optional module id"
      | "treeRevision", [] -> Ok Query.TreeRevision
      | "treeRevision", _ -> arity "treeRevision" "no arguments"
      | "help", [] -> Ok Query.Help
      | "help", _ -> arity "help" "no arguments"
      | _ -> Error(sprintf "'%s' is not one of this console's calls. Accepted: %s" name accepted)

// ─── the log ─────────────────────────────────────────────────────────────────

/// How an entry reads. `Refused` is a first-class outcome rather than an error:
/// a gate declining an op, or the edit gate refusing one that would introduce a
/// defect, is the posture working — a different fact from a failure.
[<RequireQualifiedAccess>]
type Level =
  | Info
  | Refused
  | Failed

type Entry =
  {
    Seq: int
    Level: Level
    Head: string
    /// The payload, pretty-printed; `""` when the head says everything.
    Detail: string
  }

type State =
  {
    Input: string
    /// Newest first, so the latest result is on screen without scrolling.
    Log: Entry list
    Seq: int
  }

let empty: State = { Input = ""; Log = []; Seq = 0 }

/// The example calls the pane offers as one-click chips — the ones that answer
/// something on any tree, so a first-time visitor gets a real result rather than
/// a "no such node".
let examples =
  [ "inspectTree()"; "findNodes(\"Button\")"; "treeRevision()"; "help()" ]

let private levelClass (level: Level) : string =
  match level with
  | Level.Info -> "fl-console-entry fl-console-info"
  | Level.Refused -> "fl-console-entry fl-console-refused"
  | Level.Failed -> "fl-console-entry fl-console-failed"

// ─── driving the shipped surface ─────────────────────────────────────────────

[<Emit("$0[$1].apply($0, $2)")>]
let private callMethod (surface: obj) (name: string) (args: obj array) : obj = jsNative

[<Emit("typeof $0 === 'string'")>]
let private isJsString (value: obj) : bool = jsNative

/// The apply envelope's own `status` — `applied` / `denied` / `rejected` /
/// `decodeFailed` / `unwired`. Read off the envelope rather than inferred from
/// whether a session came back, so the log distinguishes a REFUSAL (the gate or
/// the edit gate said no) from a FAILURE (the document did not decode at all).
[<Emit("($0 && $0.status) ? $0.status : \"\"")>]
let private statusOf (value: obj) : string = jsNative

[<Emit("(function(v){ try { return JSON.stringify(v, null, 2); } catch (e) { return String(v); } })($0)")>]
let private pretty (value: obj) : string = jsNative

/// Render a surface result for the log: a string comes back as itself (`help()`
/// is prose), everything else as pretty JSON.
let private show (value: obj) : string =
  if isJsString value then
    unbox<string> value
  else
    pretty value

// ─── the console's own runtime ───────────────────────────────────────────────

/// The dispatch policy an op issued from this pane runs under.
///
/// It PERMITS exactly one descriptor — `ApplyTreeOp` — and refuses every other,
/// which makes it strictly narrower than the authority the Editor beside it
/// already has: the navigator applies ops to this same session with no dispatch
/// gate at all. So the console is not a new capability, it is a second, gated
/// route to an existing one. Everything effectful still delegates to the stock
/// browser runtime; only the policy is ours, and that runtime's own
/// deny-by-default `CanDispatch` is never consulted because the renderer asks
/// THIS wrapper — so the page stays absent from a `grep permissive` sweep.
let private consoleRuntime: Runtime.IFuaranRuntime =
  let effects = BrowserRuntime.create ()

  { new Runtime.IFuaranRuntime with
      member _.CanDispatch(action) =
        match action with
        | Runtime.ActionDescriptor.ApplyTreeOp _ -> true
        | _ -> false

      member _.Call(endpoint, onResult) = effects.Call(endpoint, onResult)
      member _.Notify(channel, payload) = effects.Notify(channel, payload)
      member _.Navigate(route) = effects.Navigate(route)
      member _.SetState(key, value) = effects.SetState(key, value)

      member _.InvokeAiTool(toolName, args) = effects.InvokeAiTool(toolName, args)

      member _.WriteToClipboard(text) = effects.WriteToClipboard(text)

      member _.ReadFileBody(file, encoding, onRead) =
        effects.ReadFileBody(file, encoding, onRead)

      member _.Warn(message) = effects.Warn(message)
      member _.LayoutObserver = effects.LayoutObserver

      member _.TryRenderCustom(moduleId, componentId, props) =
        effects.TryRenderCustom(moduleId, componentId, props)

      member _.TryGetCustomRenderer(moduleId, componentId) =
        effects.TryGetCustomRenderer(moduleId, componentId)

      member _.TryRenderCustomInScope(scope, moduleId, componentId, props) =
        effects.TryRenderCustomInScope(scope, moduleId, componentId, props)

      member _.TryGetCustomRendererInScope(scope, moduleId, componentId) =
        effects.TryGetCustomRendererInScope(scope, moduleId, componentId)

      member _.TryLoadGuest(scopeId) = effects.TryLoadGuest(scopeId) }

// ─── the telemetry sink the shipped pipeline writes into ─────────────────────

/// An `IFuaranTelemetrySink` that appends to a buffer instead of a backend.
/// Wired as the apply pipeline's deny sink, so a refused op is recorded through
/// the shipped seam rather than narrated separately — the deny envelope the
/// caller sees and the record in the log are the same event.
///
/// The other five members are implemented because the contract has them, and
/// each records enough to identify the record should a future wiring route one
/// here. Nothing in this app emits them today.
let private recordingSink (buffer: (Level * string * string) list ref) : IFuaranTelemetrySink =
  let append (level: Level) (head: string) (detail: string) =
    buffer.Value <- buffer.Value @ [ (level, head, detail) ]

  { new IFuaranTelemetrySink with
      member _.RecordDeny(t) =
        append Level.Refused ("deny · " + t.ToolName) t.Reason

      member _.RecordOpApply(t) =
        append Level.Info "op-apply" (sprintf "%s · seq %d" t.StreamId t.Sequence)

      member _.RecordRenderFailure(t) =
        append Level.Failed ("render failure · " + t.NodeId) t.ErrorMessage

      member _.RecordProviderCall(t) =
        append Level.Info "provider call" (t.ProviderId + " / " + t.ModelId)

      member _.RecordCacheStat(t) =
        append Level.Info "cache stat" t.CacheName

      member _.RecordValidateOutcome(t) =
        append Level.Info "validate outcome" (String.concat ", " t.TopCodes) }

// ─── evaluation ──────────────────────────────────────────────────────────────

/// The attribution a console-issued op is recorded under. A `Human` actor, and a
/// distinct one from the navigator's, so the session's op stream still answers
/// "who changed this" after the fact.
let consoleActor: Actor = Actor.Human "console"

/// What one evaluated line produced: the next console state, and the next
/// session when — and only when — an op actually applied.
type Outcome =
  { State: State
    Session: Session.SessionState option }

let private push (state: State) (level: Level) (head: string) (detail: string) : State =
  let seq = state.Seq + 1

  { state with
      Seq = seq
      Log =
        { Seq = seq
          Level = level
          Head = head
          Detail = detail }
        :: state.Log }

/// Evaluate `state.Input` against the session. Returns the console state with the
/// call and its outcome logged, plus the next session when an op applied.
///
/// Never throws: a parse failure, an absent tree, a decode failure, a gate denial
/// and an edit-gate refusal are all logged outcomes.
let run (state: State) (session: Session.SessionState) : Outcome =
  let line = state.Input.Trim()

  if line = "" then
    { State = state; Session = None }
  else
    match parse line with
    | Error message ->
      { State = push state Level.Failed line message
        Session = None }
    | Ok query ->
      match session.Tree with
      | None ->
        { State = push state Level.Failed line "There is no tree yet — generate or load one first."
          Session = None }
      | Some tree ->
        // Where the apply handler deposits the folded session. `applyResult`
        // calls the handler synchronously, so this is read back immediately
        // after the call rather than left to a later frame.
        let applied: Session.SessionState option ref = ref None
        let records: (Level * string * string) list ref = ref []

        let handler (opJson: string) : DebugGlobal.ApplyOutcome =
          match Decode.decodeOp opJson with
          | Error e -> DebugGlobal.ApplyOutcome.DecodeFailed e.Message
          | Ok op ->
            match PropertyEditor.commitOpAs consoleActor session op with
            | PropertyEditor.Rejected message -> DebugGlobal.ApplyOutcome.Rejected message
            | PropertyEditor.Committed next ->
              applied.Value <- Some next

              match next.Tree with
              | Some newTree -> DebugGlobal.ApplyOutcome.AppliedWithTree(box newTree)
              | None -> DebugGlobal.ApplyOutcome.Applied

        // The journal seam takes a permitted op's JSON. The session's own
        // hash-chained log already recorded it through `commitOpAs`, so this leg
        // REPORTS the journalling rather than performing a second one.
        let sinks =
          { DebugGlobal.DebugSinks.none with
              TelemetrySink = Some(recordingSink records)
              OnApplied = Some(fun opJson -> records.Value <- records.Value @ [ (Level.Info, "op applied", opJson) ])
              UserId = "console" }

        let options =
          { DebugGlobal.DebugOptions.defaults with
              Sinks = sinks
              ApplyHandler = Some handler }

        let surface =
          DebugGlobal.buildGlobalWith tree BindingResolver.empty consoleRuntime options

        let call (name: string) (args: obj array) = show (callMethod surface name args)

        let detail, level =
          match query with
          | Query.NodeState id -> call "getNodeState" [| box id |], Level.Info
          | Query.BindingValue(id, slot) -> call "getBindingValue" [| box id; box slot |], Level.Info
          | Query.RenderedDom id -> call "getRenderedDom" [| box id |], Level.Info
          | Query.InspectTree -> call "inspectTree" [||], Level.Info
          | Query.FindNodes kind -> call "findNodes" [| box kind |], Level.Info
          | Query.Affordances moduleId -> call "getAffordances" [| box (Option.toObj moduleId) |], Level.Info
          | Query.TreeRevision -> call "treeRevision" [||], Level.Info
          | Query.Help -> call "help" [||], Level.Info
          | Query.Apply opJson ->
            let envelope = callMethod surface "apply" [| box opJson |]

            let level =
              match statusOf envelope with
              | "applied" -> Level.Info
              | "denied"
              | "rejected" -> Level.Refused
              | _ -> Level.Failed

            show envelope, level

        let withCall = push state level line detail

        let withRecords =
          records.Value |> List.fold (fun acc (lvl, h, d) -> push acc lvl h d) withCall

        { State = withRecords
          Session = applied.Value }

// ─── the pane ────────────────────────────────────────────────────────────────

/// The one-line explainer. Deliberately says what the pane IS for someone who has
/// never met Fuaran — the rendered UI is a typed object you can ask questions of
/// — before it says how to use it.
let intro: ReactElement =
  Html.p
    [ prop.className "fl-console-intro"
      prop.text (
        "The preview is a typed object, not markup — so you can ask it questions. "
        + "Every call runs against the tree on screen right now, in this tab: nothing is sent anywhere, "
        + "nothing is stored, and the box below takes this fixed set of calls rather than JavaScript."
      ) ]

let private entryView (entry: Entry) : ReactElement =
  Html.li
    [ prop.className (levelClass entry.Level)
      prop.key (string entry.Seq)
      prop.children
        [ Html.div [ prop.className "fl-console-head"; prop.text entry.Head ]
          (if entry.Detail = "" then
             Html.none
           else
             Html.pre [ prop.className "fl-console-detail"; prop.text entry.Detail ]) ] ]

/// The scrolling log — every call this console made and every telemetry record
/// the apply pipeline emitted into it, newest first.
let logPane (state: State) : ReactElement =
  let body =
    if List.isEmpty state.Log then
      Html.div
        [ prop.className "fl-console-empty"
          prop.text "Nothing yet — run a call and its result is recorded here." ]
    else
      Html.ol
        [ prop.className "fl-console-list"
          prop.children [ for entry in state.Log -> entryView entry ] ]

  Html.div
    [ prop.className "fl-console-log"
      prop.children [ Html.div [ prop.className "fl-console-log-title"; prop.text "Log" ]; body ] ]

/// The input: one-click example calls, the call box (Enter runs, Shift+Enter is a
/// newline — the same convention the refine box uses), and Run.
let inputPane (state: State) (onInput: string -> unit) (onRun: unit -> unit) : ReactElement =
  Html.div
    [ prop.className "fl-console-input-row"
      prop.children
        [ Html.div
            [ prop.className "fl-console-examples"
              prop.children
                [ for example in examples ->
                    Html.button
                      [ prop.className "fl-console-example"
                        prop.key example
                        prop.type' "button"
                        prop.text example
                        prop.onClick (fun _ -> onInput example) ] ] ]
          Html.textarea
            [ prop.className "fl-console-input"
              prop.rows 2
              prop.ariaLabel "Console call"
              prop.placeholder "getNodeState(\"submit-btn\")"
              prop.value state.Input
              prop.onChange (fun (v: string) -> onInput v)
              prop.onKeyDown (fun ev ->
                if ev.key = "Enter" && not ev.shiftKey then
                  ev.preventDefault ()
                  onRun ()) ]
          Html.div
            [ prop.className "fl-console-controls"
              prop.children
                [ Html.button
                    [ prop.className "fl-btn"
                      prop.text "Run"
                      prop.disabled (state.Input.Trim() = "")
                      prop.onClick (fun _ -> onRun ()) ]
                  Html.span [ prop.className "fl-console-accepted"; prop.text ("Accepted: " + accepted) ] ] ] ] ]

// ─── flat surfaces for the headless suite ────────────────────────────────────
//
// F# DUs and records are awkward to assert on across the Fable boundary, so —
// exactly as `Session.ingestResult` and the navigator's cursor helpers do — the
// parser and the log project to plain strings.

/// `parse` as `"ok|<case>|<args…>"` / `"error|<message>"`.
let parseFlat (line: string) : string =
  match parse line with
  | Error message -> "error|" + message
  | Ok query ->
    let case =
      match query with
      | Query.NodeState id -> "getNodeState|" + id
      | Query.BindingValue(id, slot) -> "getBindingValue|" + id + "|" + slot
      | Query.RenderedDom id -> "getRenderedDom|" + id
      | Query.InspectTree -> "inspectTree"
      | Query.FindNodes kind -> "findNodes|" + kind
      | Query.Affordances moduleId -> "getAffordances|" + Option.defaultValue "" moduleId
      | Query.TreeRevision -> "treeRevision"
      | Query.Apply opJson -> "apply|" + opJson
      | Query.Help -> "help"

    "ok|" + case

/// Every log entry as `"<level>|<head>|<detail>"`, oldest first.
let logFlat (state: State) : string array =
  state.Log
  |> List.rev
  |> List.map (fun e ->
    let level =
      match e.Level with
      | Level.Info -> "info"
      | Level.Refused -> "refused"
      | Level.Failed -> "failed"

    level + "|" + e.Head + "|" + e.Detail)
  |> Array.ofList

/// Run one line against a session — the headless entry point, projected flat in
/// the same shape as `Session.ingestResult` / `PropertyEditor.commitAt`.
/// `Applied` false leaves `Next` as the input session, so a test can assert the
/// tree really was untouched.
let runLine
  (session: Session.SessionState)
  (line: string)
  : {| Applied: bool
       Log: string array
       Next: Session.SessionState |}
  =
  let outcome = run { empty with Input = line } session

  {| Applied = outcome.Session.IsSome
     Log = logFlat outcome.State
     Next = Option.defaultValue session outcome.Session |}
