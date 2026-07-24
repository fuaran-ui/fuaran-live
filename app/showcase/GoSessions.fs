module Fuaran.Showcase.GoSessions

// ============================================================================
//  Go sessions – the same playground identity in a new key: BYOK becomes BYOS.
//  Pillar: "one wire, many worlds".
//
//  A Go session server is another world speaking the same wire. This page puts
//  that story on the playground in its two honest forms:
//
//   1. RECORDED REPLAY (zero setup, no server). A canned recording of the demo
//      binary's scripted run – the actual resolved-projection frames the Go core
//      served (first paint -> $state write -> valid op -> a validator reject that
//      keeps the last good tree) – stepped through THIS renderer. Loaded through
//      the shared replay-loader seam; labelled as recorded, never live.
//
//   2. BYOS LIVE-CONNECT. "Run one binary", then the page connects to
//      http://localhost:14050 and drives the live session end to end: it renders
//      the resolved projection, sends interactions, and re-renders per response –
//      surfacing the typed validator reject and the kept last-good tree. The
//      distribution constraint IS the demo: the server is one binary you just ran.
//
//  Both modes render by DECODING the core's resolved-projection wire JSON and
//  drawing it through the F# Fuaran.UI.Renderer – the site is exhibit zero. The
//  resolved projection folds scalar Transforms to literals but leaves $state
//  slots unresolved, so the current $state values ride alongside (a per-frame
//  `states` map in the artefact; the client's own writes in BYOS) and seed the
//  renderer's binding sources – a decode-only surface with no evaluator.
//
//  Boundary: the server's public framing is "a Go session surface of the Fuaran
//  UI wire format, over the Rust reference core" – never "conformant host". No
//  private sibling / product name appears here or in the recorded artefact.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module Decode = Fuaran.UI.Ops.JsonDecode

// ─── small JS interop (mirrors Evaluation.fs' tolerant-read helpers) ──────────

[<Emit("(function(){ try { return JSON.parse($0); } catch(e){ return null; } })()")>]
let private tryParseJson (s: string) : obj = jsNative

[<Emit("($0 == null ? null : $0[$1])")>]
let private field (o: obj) (k: string) : obj = jsNative

[<Emit("(function(){ var v = ($0==null?null:$0[$1]); return Array.isArray(v)?v:[]; })()")>]
let private fieldArr (o: obj) (k: string) : obj[] = jsNative

[<Emit("JSON.stringify($0)")>]
let private stringify (o: obj) : string = jsNative

[<Emit("Object.keys($0 || {})")>]
let private objKeys (o: obj) : string[] = jsNative

let private gStr (o: obj) (k: string) : string =
  let v = field o k
  if isNull (box v) then "" else string v

// ─── replay artefact: pure parsing (testable across the Fable boundary) ───────
//
// The artefact is regenerable from the demo binary (`--record <dir>`), never
// hand-authored. These project its shape to flat values a headless test asserts on
// and the page consumes.

/// The artefact id the shared replay-loader fetches from ./replays/<id>.json.
[<Literal>]
let replayId = "go-sessions"

/// Parse the artefact JSON. Returns `null` for anything malformed – the page maps
/// that to a Missing state (an honest absent view, never fabricated success).
let parseArtefact (json: string) : obj = tryParseJson json

/// The frames array (empty for a malformed / frameless artefact).
let private frames (art: obj) : obj[] = fieldArr art "frames"

/// How many frames the artefact carries.
let frameCount (art: obj) : int = (frames art).Length

let private frameAt (art: obj) (i: int) : obj =
  let fs = frames art
  if i >= 0 && i < fs.Length then fs.[i] else null

/// A frame's resolved-projection wire JSON (the tree the renderer draws), or "".
let frameResolvedJson (art: obj) (i: int) : string =
  let r = field (frameAt art i) "resolved"
  if isNull (box r) then "" else stringify r

/// A frame's human label (the interaction narration).
let frameLabel (art: obj) (i: int) : string = gStr (frameAt art i) "label"

/// A frame's interaction event kind ("load" | "setState" | "applyOp").
let frameActionKind (art: obj) (i: int) : string =
  gStr (field (frameAt art i) "action") "kind"

/// Does this frame carry a validator reject?
let frameIsReject (art: obj) (i: int) : bool =
  not (isNull (box (field (frameAt art i) "reject")))

/// The typed reject code on a reject frame, or "".
let frameRejectCode (art: obj) (i: int) : string =
  gStr (field (frameAt art i) "reject") "code"

/// The reject detail message on a reject frame, or "".
let frameRejectMessage (art: obj) (i: int) : string =
  gStr (field (frameAt art i) "reject") "message"

/// A frame's `$state` values, as a `Map<string, obj>` for the binding sources –
/// the resolved projection leaves State slots unresolved, so these carry the
/// current values the renderer folds in (a decode-only surface, no evaluator).
let private frameStateMap (art: obj) (i: int) : Map<string, obj> =
  let states = field (frameAt art i) "states"

  if isNull (box states) then
    Map.empty
  else
    Map.ofList [ for k in objKeys states -> k, box (field states k) ]

// ─── BYOS client: pure request builders + response parsing (testable) ─────────
//
// The client logic – URL construction, request bodies, envelope parsing, reject
// and connection-refused detection – is pure F# so the vitest suite covers it
// against a stubbed local endpoint. Only the fetch itself is interop.

let sessionUrl (baseUrl: string) : string =
  baseUrl.TrimEnd('/') + "/api/v1/session"

let stateUrl (baseUrl: string) : string = baseUrl.TrimEnd('/') + "/api/v1/state"
let opUrl (baseUrl: string) : string = baseUrl.TrimEnd('/') + "/api/v1/op"

/// The POST body for a `$state` write: `{"key":…,"value":<raw json>}`. `valueJson`
/// is a raw JSON literal (e.g. "7500"), embedded verbatim as the core expects.
let stateRequestBody (key: string) (valueJson: string) : string =
  sprintf "{\"key\":%s,\"value\":%s}" (stringify key) valueJson

/// The POST body for an op: `{"op":<wire TreeOp>}`, the op embedded verbatim.
let opRequestBody (opJson: string) : string = sprintf "{\"op\":%s}" opJson

/// The flat outcome of a BYOS call – a cross-boundary-friendly object the page and
/// the tests both read: `kind` is one of "ok" | "reject" | "error" | "refused".
[<Emit("{ kind: $0, resolved: $1, rejectCode: $2, rejectMessage: $3, error: $4 }")>]
let private mkResult
  (kind: string)
  (resolved: string)
  (rejectCode: string)
  (rejectMessage: string)
  (error: string)
  : obj =
  jsNative

let private okResult (resolved: string) = mkResult "ok" resolved "" "" ""
let private rejectResult (resolved: string) (code: string) (msg: string) = mkResult "reject" resolved code msg ""
let private errorResult (msg: string) = mkResult "error" "" "" "" msg
let private refusedResult (msg: string) = mkResult "refused" "" "" "" msg

/// Parse a v1 apiResponse envelope text into the flat outcome. Pure – the heart of
/// the client logic the stubbed-endpoint test exercises. `status` is the HTTP
/// status (0 signals a network error / connection refused from the fetch wrapper).
let parseApiResponse (status: int) (text: string) : obj =
  if status = 0 then
    refusedResult (if text = "" then "the connection was refused" else text)
  else
    let o = tryParseJson text

    if isNull (box o) then
      errorResult (sprintf "the server response was not valid JSON (HTTP %d)" status)
    else
      let reject = field o "reject"
      let resolvedRaw = field o "resolved"

      let resolved =
        if isNull (box resolvedRaw) then
          ""
        else
          stringify resolvedRaw

      if not (isNull (box reject)) then
        rejectResult resolved (gStr reject "code") (gStr reject "message")
      elif unbox<bool> (field o "ok") then
        okResult resolved
      else
        let e = gStr o "error"

        errorResult (
          if e = "" then
            sprintf "the server reported a failure (HTTP %d)" status
          else
            e
        )

// The one interop point: fetch → `{status, text}`, a network error mapped to
// status 0 so `parseApiResponse` reports it as refused. Global fetch is present in
// the browser and in the Node test environment.
[<Emit("fetch($0, $1).then(function(r){ return r.text().then(function(t){ return { status: r.status, text: t }; }); }).catch(function(e){ return { status: 0, text: String(e && e.message ? e.message : e) }; })")>]
let private rawFetch (url: string) (init: obj) : JS.Promise<obj> = jsNative

// A single-argument mapper so Fable calls it as f(x) across the .then boundary
// (a curried F# function would be miscalled). Reads the {status,text} pair.
let private parseResp (resp: obj) : obj =
  parseApiResponse (unbox<int> (field resp "status")) (unbox<string> (field resp "text"))

// Promise .then combinators (this project doesn't reference the Fable.Promise CE,
// so the two we need are thin Emit wrappers over the native method).
[<Emit("$0.then($1)")>]
let private promiseThen (p: JS.Promise<obj>) (f: obj -> obj) : JS.Promise<obj> = jsNative

[<Emit("$0.then($1)")>]
let private promiseIter (p: JS.Promise<obj>) (f: obj -> unit) : unit = jsNative

let private runFetch (url: string) (init: obj) : JS.Promise<obj> =
  promiseThen (rawFetch url init) parseResp

let private postInit (body: string) : obj =
  createObj
    [ "method" ==> "POST"
      "headers" ==> createObj [ "Content-Type" ==> "application/json" ]
      "body" ==> body ]

/// GET the current session projection.
let byosGetSession (baseUrl: string) : JS.Promise<obj> =
  runFetch (sessionUrl baseUrl) (createObj [])

/// POST a `$state` write.
let byosPostState (baseUrl: string) (key: string) (valueJson: string) : JS.Promise<obj> =
  runFetch (stateUrl baseUrl) (postInit (stateRequestBody key valueJson))

/// POST an op (valid or a deliberate reject).
let byosPostOp (baseUrl: string) (opJson: string) : JS.Promise<obj> =
  runFetch (opUrl baseUrl) (postInit (opRequestBody opJson))

// The two preset interactions the BYOS controls drive (mirroring the recorded
// script): one valid structural op, one deliberate validator reject.
[<Literal>]
let private editHeadlineOp =
  """{"$type":"EditNode","newKind":{"$type":"Markdown","text":{"$type":"Literal","text":"Investigating incident"}},"target":"headline"}"""

[<Literal>]
let private invalidOp =
  """{"$type":"EditNode","newKind":{"$type":"NotAKind"},"target":"headline"}"""

[<Literal>]
let defaultBaseUrl = "http://localhost:14050"

// ─── rendering: decode the resolved wire + fold in $state, draw via the renderer ─

let private renderNode (n: Node<'msg>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

let private headingNode (id: string) (level: int) (text: string) : Node<unit> =
  Fuaran.heading
    id
    { Level = level
      Text = TextSource.Literal text
      Variant = HeadingVariant.Standard }

/// Draw a resolved-projection wire tree through the renderer, folding the given
/// `$state` values into the binding sources. A decode failure surfaces honestly.
let private renderResolved (resolvedJson: string) (states: Map<string, obj>) : ReactElement =
  match Decode.decodeNodeObj resolvedJson with
  | Error e ->
    Html.div
      [ prop.className "gs-decode-error"
        prop.children
          [ renderNode (
              Fuaran.callout
                "gs-decode-err"
                { Defaults.callout with
                    Tone = ToneVariant.Critical
                    Heading = Some(TextSource.Literal "The wire did not decode")
                    Body = TextSource.Literal e.Message }
            ) ] ]
  | Ok node ->
    let sources =
      { BindingResolver.empty with
          State = states }

    Html.div
      [ prop.className "gs-stage"
        prop.children [ Render.renderWithSources sources ignore node ] ]

// ─── mode toggle ──────────────────────────────────────────────────────────────

[<RequireQualifiedAccess>]
type private Mode =
  | Replay
  | Byos

// ─── recorded-replay state ────────────────────────────────────────────────────

[<RequireQualifiedAccess>]
type private ReplayLoad =
  | Loading
  | Loaded of obj // the parsed artefact
  | Missing of string

// fetch(url) → text, in JS; a non-OK / network error routes to onErr.
[<Emit("fetch($0).then(function(r){ if(!r.ok) throw new Error('HTTP '+r.status); return r.text(); }).then($1).catch(function(e){ $2(String(e&&e.message?e.message:e)); })")>]
let private fetchText (url: string) (onText: string -> unit) (onErr: string -> unit) : unit = jsNative

[<Emit("setInterval($0,$1)")>]
let private setInterval (cb: unit -> unit) (ms: int) : int = jsNative

[<Emit("clearInterval($0)")>]
let private clearInterval (id: int) : unit = jsNative

let private recordedBadge: ReactElement =
  Html.span
    [ prop.className "gs-badge gs-badge-recorded"
      prop.children
        [ Html.span [ prop.className "gs-dot" ]
          Html.text "Recorded – no server running" ] ]

let private liveBadge: ReactElement =
  Html.span
    [ prop.className "gs-badge gs-badge-live"
      prop.children
        [ Html.span [ prop.className "gs-dot" ]
          Html.text "Live – driving your local binary" ] ]

[<ReactComponent>]
let private ReplayView () : ReactElement =
  let load, setLoad = React.useState ReplayLoad.Loading
  let step, setStep = React.useState 0
  let playing, setPlaying = React.useState false

  React.useEffectOnce (fun () ->
    fetchText
      (sprintf "./replays/%s.json" replayId)
      (fun text ->
        match box (parseArtefact text) with
        | null -> setLoad (ReplayLoad.Missing "the recording could not be parsed")
        | art -> setLoad (ReplayLoad.Loaded art))
      (fun err -> setLoad (ReplayLoad.Missing(sprintf "no recording available (%s)" err))))

  // Hooks must be unconditional (Rules of Hooks), so `count`/`i` and the auto-play
  // effect are computed at the top level, before the render match – never inside a
  // match arm (which would add/remove a hook when `load` transitions and crash React).
  let loadedArt =
    match load with
    | ReplayLoad.Loaded art -> Some art
    | _ -> None

  let count =
    match loadedArt with
    | Some art -> frameCount art
    | None -> 0

  let i = if count = 0 then 0 else (max 0 (min step (count - 1)))

  // Auto-advance while playing. The effect re-arms on each frame change (i is a
  // dependency), so the captured i is always current – no functional-updater setter
  // is needed (Feliz useState setters take a value). Stop at the last frame.
  React.useEffect (
    (fun () ->
      let timer =
        if playing && count > 0 && i < count - 1 then
          setInterval (fun () -> setStep (i + 1)) 1600
        else
          (if playing && i >= count - 1 then
             setPlaying false

           -1)

      { new System.IDisposable with
          member _.Dispose() =
            if timer >= 0 then
              clearInterval timer }),
    [| box playing; box count; box i |]
  )

  match load with
  | ReplayLoad.Loading -> renderNode (Fuaran.markdown "gs-loading" "_Loading the recorded session…_")
  | ReplayLoad.Missing reason ->
    renderNode (
      Fuaran.callout
        "gs-missing"
        { Defaults.callout with
            Tone = ToneVariant.Subdued
            Heading = Some(TextSource.Literal "Replay unavailable")
            Body = TextSource.Literal reason }
    )
  | ReplayLoad.Loaded art ->
    if count = 0 then
      renderNode (Fuaran.markdown "gs-empty" "_The recording has no frames._")
    else
      let atReject = frameIsReject art i

      let stateStrip =
        Html.div
          [ prop.className "gs-strip"
            prop.children
              [ recordedBadge
                Html.span
                  [ prop.className "gs-frame-counter"
                    prop.text (sprintf "Frame %d of %d" (i + 1) count) ] ] ]

      let narration =
        renderNode (
          Fuaran.callout
            "gs-narration"
            { Defaults.callout with
                Tone = (if atReject then ToneVariant.Critical else ToneVariant.Info)
                Heading = Some(TextSource.Literal(sprintf "Step %d – %s" i (frameActionKind art i)))
                Body = TextSource.Literal(frameLabel art i) }
        )

      let rejectBanner =
        if atReject then
          renderNode (
            Fuaran.callout
              "gs-reject"
              { Defaults.callout with
                  Tone = ToneVariant.Critical
                  Heading = Some(TextSource.Literal(sprintf "Validator reject – %s" (frameRejectCode art i)))
                  Body =
                    TextSource.Literal(
                      sprintf
                        "%s – the op never applied; the last good tree below is unchanged."
                        (frameRejectMessage art i)
                    ) }
          )
        else
          Html.none

      let controls =
        Html.div
          [ prop.className "gs-controls"
            prop.children
              [ Html.button
                  [ prop.className "gs-btn"
                    prop.disabled (i <= 0)
                    prop.onClick (fun _ ->
                      setPlaying false
                      setStep (i - 1))
                    prop.text "‹ Prev" ]
                Html.button
                  [ prop.className "gs-btn gs-btn-primary"
                    prop.onClick (fun _ ->
                      if i >= count - 1 then
                        setStep 0
                        setPlaying true
                      else
                        setPlaying (not playing))
                    prop.text (
                      if playing then "❚❚ Pause"
                      elif i >= count - 1 then "↻ Replay"
                      else "▶ Play"
                    ) ]
                Html.button
                  [ prop.className "gs-btn"
                    prop.disabled (i >= count - 1)
                    prop.onClick (fun _ ->
                      setPlaying false
                      setStep (i + 1))
                    prop.text "Next ›" ] ] ]

      Html.div
        [ prop.className "gs-panel"
          prop.children
            [ stateStrip
              controls
              narration
              rejectBanner
              renderResolved (frameResolvedJson art i) (frameStateMap art i) ] ]

// ─── BYOS live-connect state ──────────────────────────────────────────────────

[<RequireQualifiedAccess>]
type private Conn =
  | Idle
  | Busy
  | Live of string // the current resolved wire JSON
  | Refused of string

[<ReactComponent>]
let private ByosView () : ReactElement =
  let baseUrl, setBaseUrl = React.useState defaultBaseUrl
  let conn, setConn = React.useState Conn.Idle
  // The client tracks its own $state writes for render fidelity (the resolved
  // projection leaves $state slots unresolved – the client owns the values it set).
  let clientState, setClientState = React.useState (Map.empty: Map<string, obj>)
  let reject, setReject = React.useState (None: (string * string) option)

  // Apply a flat BYOS result to the page state.
  let apply (result: obj) : unit =
    match gStr result "kind" with
    | "ok" ->
      setReject None
      setConn (Conn.Live(gStr result "resolved"))
    | "reject" ->
      setReject (Some(gStr result "rejectCode", gStr result "rejectMessage"))
      setConn (Conn.Live(gStr result "resolved")) // the kept last-good tree
    | "refused" -> setConn (Conn.Refused(gStr result "error"))
    | _ -> setConn (Conn.Refused(gStr result "error"))

  let run (p: JS.Promise<obj>) : unit =
    setConn Conn.Busy
    promiseIter p apply

  let connect () =
    setClientState Map.empty
    setReject None
    run (byosGetSession baseUrl)

  let writeRevenue () =
    setClientState (Map.ofList [ "revenue", box 7500 ])
    run (byosPostState baseUrl "revenue" "7500")

  let applyValid () = run (byosPostOp baseUrl editHeadlineOp)
  let applyInvalid () = run (byosPostOp baseUrl invalidOp)

  // Bound to a local: `prop.disabled busy` would be parsed as a named
  // argument `disabled(conn = …)`, not an equality test.
  let busy = (conn = Conn.Busy)

  let connectBar =
    Html.div
      [ prop.className "gs-connect-bar"
        prop.children
          [ Html.label [ prop.className "gs-field-label"; prop.text "Server" ]
            Html.input
              [ prop.className "gs-input"
                prop.value baseUrl
                prop.onChange (fun (v: string) -> setBaseUrl v)
                prop.placeholder defaultBaseUrl ]
            Html.button
              [ prop.className "gs-btn gs-btn-primary"
                prop.disabled busy
                prop.onClick (fun _ -> connect ())
                prop.text (if busy then "Connecting…" else "Connect") ] ] ]

  let permissionNote =
    renderNode (
      Fuaran.callout
        "gs-perm"
        { Defaults.callout with
            Tone = ToneVariant.Info
            Heading = Some(TextSource.Literal "Your browser may ask permission")
            Body =
              TextSource.Literal
                "A page served over https connecting to a program on your own machine triggers the browser's local-network access prompt. Allow it to let the page reach the binary. If you decline – or nothing is running – the connection is refused and you can fall back to the recorded replay, which needs no server." }
    )

  let liveControls =
    Html.div
      [ prop.className "gs-controls"
        prop.children
          [ renderNode (
              Fuaran.markdown
                "gs-live-hint"
                "_Each button sends a real request to your binary; the tree below re-renders from your server's response._"
            )
            Html.button
              [ prop.className "gs-btn"
                prop.disabled busy
                prop.onClick (fun _ -> writeRevenue ())
                prop.text "Write $state.revenue = 7500" ]
            Html.button
              [ prop.className "gs-btn"
                prop.disabled busy
                prop.onClick (fun _ -> applyValid ())
                prop.text "Apply valid op (edit headline)" ]
            Html.button
              [ prop.className "gs-btn gs-btn-danger"
                prop.disabled busy
                prop.onClick (fun _ -> applyInvalid ())
                prop.text "Apply invalid op (expect reject)" ] ] ]

  let rejectBanner =
    match reject with
    | Some(code, msg) ->
      renderNode (
        Fuaran.callout
          "gs-byos-reject"
          { Defaults.callout with
              Tone = ToneVariant.Critical
              Heading = Some(TextSource.Literal(sprintf "Validator reject – %s" code))
              Body = TextSource.Literal(sprintf "%s – the last good tree below is unchanged." msg) }
      )
    | None -> Html.none

  let body =
    match conn with
    | Conn.Idle ->
      renderNode (
        Fuaran.markdown
          "gs-byos-idle"
          "**Getting started** – this page never talks to a hosted service; it becomes the client for a server *you* run. Everything below happens between your browser and your own machine.\n\n\
1. **Get the server.** Check out the `fuaran-go-sessions` repository (the “Run it yourself” panel below has the details).\n\
2. **Build and run it** – one binary, listening on `http://localhost:14050`. The exact commands are in the panel below; the repository `README.md` is the authoritative walkthrough.\n\
3. **Press Connect.** Leave the address as-is unless you started the server elsewhere. Your browser may ask permission for a local-network connection – that grant is between you and your browser.\n\
4. **Drive it.** Once connected: write a live `$state` value, apply a valid op, then apply an *invalid* one – the server's validator rejects it with a typed error and keeps the last good tree.\n\n\
_Not connected yet. Start the binary, then **Connect**._"
      )
    | Conn.Busy -> renderNode (Fuaran.markdown "gs-byos-busy" "_Talking to the server…_")
    | Conn.Refused err ->
      Html.div
        [ prop.className "gs-refused"
          prop.children
            [ renderNode (
                Fuaran.callout
                  "gs-refused-callout"
                  { Defaults.callout with
                      Tone = ToneVariant.Warning
                      Heading = Some(TextSource.Literal "Couldn’t reach the server")
                      Body =
                        TextSource.Literal(
                          sprintf
                            "%s. Check the binary is running and the address is right, or switch to the recorded replay – it needs no server."
                            (if err = "" then "The connection was refused" else err)
                        ) }
              ) ] ]
    | Conn.Live resolved ->
      Html.div
        [ prop.className "gs-panel"
          prop.children
            [ Html.div [ prop.className "gs-strip"; prop.children [ liveBadge ] ]
              liveControls
              rejectBanner
              renderResolved resolved clientState ] ]

  Html.div [ prop.className "gs-byos"; prop.children [ connectBar; permissionNote; body ] ]

// ─── "run it yourself" panel (derived from the sibling README – link, don't dup) ─

let private runItYourself: ReactElement =
  Html.details
    [ prop.className "gs-run"
      prop.children
        [ Html.summary [ prop.text "Run it yourself – one binary" ]
          renderNode (
            Fuaran.markdown
              "gs-run-intro"
              "The server is one Go binary you run on your own machine. From a checkout of the fuaran-go repository:"
          )
          renderNode (
            Fuaran.codeBlock
              "gs-run-cmd"
              "powershell"
              "pwsh ./run.ps1 -SkipTests    # builds the core + stages its library\n$env:FUARAN_RS_LIB = (Resolve-Path ..\\fuaran-rs\\target\\release\\fuaran_rs.dll)\ngo run ./cmd/dashboard       # listens on http://localhost:14050"
          )
          renderNode (
            Fuaran.markdown
              "gs-run-more"
              "It serves the interactive HTML dashboard and the small JSON API this page drives (`/api/v1/session`, `/state`, `/op`). Full start + contract details live in the repository's `README.md` and `docs/BYOS-CONTRACT.md` – this panel links rather than duplicates them, so the instructions never drift."
          ) ] ]

// ─── the page ─────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private GoSessionsView () : ReactElement =
  let mode, setMode = React.useState Mode.Replay

  let tab (m: Mode) (label: string) : ReactElement =
    Html.button
      [ prop.className (if mode = m then "gs-tab gs-tab-active" else "gs-tab")
        prop.onClick (fun _ -> setMode m)
        prop.text label ]

  Html.div
    [ prop.className "gs-page"
      prop.children
        [ renderNode (headingNode "gs-title" 1 "Go Sessions – bring your own server")
          renderNode (
            Fuaran.markdown
              "gs-wow"
              "The playground's identity – client-only, no account, no server – extends symmetrically to **bring your own server**: the same static page becomes the client for a session server *you* run locally as one Go binary. Two honest modes below."
          )
          Html.div
            [ prop.className "gs-tabs"
              prop.children [ tab Mode.Replay "Recorded replay"; tab Mode.Byos "Bring your own server" ] ]
          (match mode with
           | Mode.Replay -> ReplayView()
           | Mode.Byos -> ByosView())
          runItYourself ] ]

let page: ReactElement = GoSessionsView()
