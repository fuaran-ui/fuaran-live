module Fuaran.Live.FableHost

// ============================================================================
//  Phase 85 – fuaran-live F# (Fable) render host.
//
//  A standalone browser entry, loaded inside an iframe by the preview pane's
//  host-switch. The parent window posts canonical wire-format JSON over
//  `postMessage`; this host decodes it through the canonical
//  `Fuaran.UI.Ops.JsonDecode.decodeNode` and renders the resulting
//  `Node<obj>` tree through the F# `Fuaran.UI.Renderer`. The SAME wire JSON
//  renders through the TS `@fuaran-ui/renderer` in a sibling iframe – the two
//  conformant hosts produce byte-parity DOM + CSS-class vocabulary, the
//  behavioural-parity claim made visible.
//
//  postMessage protocol (mirrored in src/hosts/protocol.ts):
//    parent -> host : { kind: "fuaran:render", wire: <canonical JSON string> }
//                     { kind: "fuaran:clear" }
//    host -> parent : { kind: "fuaran:ready",    host: "fsharp" }
//                     { kind: "fuaran:rendered", host: "fsharp", ok, error, canonical }
//
//  `canonical` is `encode(decode(wire))` – the host's canonical re-encoding, so
//  the parent's parity view asserts byte-identity against the sibling TS host.
//
//  No effect ports are wired: decoded trees carry closure-sentinel actions
//  (the decoder cannot reconstruct CLR closures), so `Dispatch` is `ignore`
//  and the visualisation adapter is the no-op. The host renders structure +
//  class vocabulary, which is exactly what parity asserts.
//
//  Live mode (`?live=1`): opt-in interactive render. The tree renders inside
//  an isolated renderer scope with a `BrowserRuntime`, and the surface
//  re-renders on every write within that scope – so declarative interactivity
//  (tabs, filters, form state: the write-back default) WORKS, with no keys and
//  no effect ports. Same decode, same renderer; only the render entry differs
//  (`renderWithSourcesInScope` + scope subscription vs the one-shot
//  `renderWithSources`). Default (no param) stays the dead-interactivity
//  parity render – byte-stable DOM is what parity asserts.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Browser
open Elmish
open Elmish.React
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

[<Literal>]
let private hostId = "fsharp"

// ─── live mode (`?live=1`) ───────────────────────────────────────────────────

let private isLive = window.location.search.Contains "live=1"

[<Literal>]
let private liveScope = "fable-host-live"

/// The live mode's `Action.SetState` / write-back substrate: scoped writes
/// route into the store the live surface subscribes to.
let private liveRuntime: Runtime.IFuaranRuntime = BrowserRuntime.create ()

/// Interactive render (mirrors the playground panel-row pattern): subscribe to
/// the host's isolated scope and re-render on any write, so tab switches,
/// filter chips, and form fields stay live. Re-subscribing per tick keeps the
/// closure fresh.
[<ReactComponent>]
let private LiveTree (node: Node<obj>) : ReactElement =
  let tick, setTick = React.useState 0

  React.useEffect (
    ((fun () -> (StateStore.forScope liveScope).Subscribe(fun () -> setTick (tick + 1))): unit -> unit -> unit),
    [| box tick |]
  )

  Render.renderWithSourcesInScope liveScope BindingResolver.empty liveRuntime ignore node

// Phase 293 – the client-only KaTeX math pass. The F# (Fable) host reuses the
// SAME canonical, unit-tested enhancer the TS host ships (`@fuaran-ui/renderer`),
// imported here via interop rather than re-implemented – the enhancement is
// host-agnostic DOM work over the identical `.fuaran-math-source` /
// `.fuaran-markdown` markup both renderers emit, so one implementation serves
// both hosts with zero divergence.
[<Import("enhanceMath", "@fuaran-ui/renderer/enhance-math")>]
let private enhanceMath (root: Browser.Types.Element) : unit = jsNative

type private Model =
  { Decoded: Result<Node<obj>, JsonDecode.DecodeError> option }

type private Msg =
  | Render of string
  | Clear

let private postToParent (payload: obj) : unit =
  emitJsStatement payload "window.parent.postMessage($0, '*')"

let private postRendered (ok: bool) (error: string option) (canonical: string option) : unit =
  postToParent
    {| kind = "fuaran:rendered"
       host = hostId
       ok = ok
       error =
        (match error with
         | Some e -> box e
         | None -> null)
       canonical =
        (match canonical with
         | Some c -> box c
         | None -> null) |}

let private init () : Model * Cmd<Msg> =
  let listen: Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
      window.addEventListener (
        "message",
        fun ev ->
          let data = ev?data

          if not (isNull (box data)) then
            match data?kind with
            | "fuaran:render" -> dispatch (Render(string data?wire))
            | "fuaran:clear" -> dispatch Clear
            | _ -> ()
      )
      // Tell the parent the host is mounted and ready to receive wire JSON.
      postToParent
        {| kind = "fuaran:ready"
           host = hostId |})

  { Decoded = None }, listen

let private update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
  match msg with
  | Clear -> { Decoded = None }, Cmd.none
  | Render wire ->
    // `decodeNode` yields a `WireTree` (inert closure sentinels – the decoder
    // cannot reconstruct CLR closures); this parity host renders structure + class
    // vocabulary only (`Dispatch` is `ignore`), so reify to the raw `Node<obj>` the
    // renderer + canonical re-encoder work over. Dead interactivity is by design.
    let decoded = JsonDecode.decodeNode wire |> Result.map WireTree.reify

    let ack: Cmd<Msg> =
      Cmd.ofEffect (fun _ ->
        match decoded with
        | Ok node ->
          postRendered true None (Some(Canon.encodeNode node))
          // KaTeX-upgrade the just-rendered deterministic math fallback. Scheduled
          // on the next frame so the synchronous Elmish render has committed the
          // DOM; idempotent, so a fresh tree re-enhances cleanly.
          let root = document.getElementById "fable-host-root"

          if not (isNull (box root)) then
            window.requestAnimationFrame (fun _ -> enhanceMath root) |> ignore
        | Error e -> postRendered false (Some e.Message) None)

    { Decoded = Some decoded }, ack

// The TS `<FuaranRenderer theme=...>` applies the theme by wrapping the tree in
// `<div class="fuaran-root" style="--fuaran-…">`. The F# `renderWithTheme`
// instead emits a bare `<style>` + node with no wrapper. To make the two hosts
// produce structurally identical DOM (the parity claim), this host mirrors the
// TS wrapper: a `fuaran-root` div carrying the theme `<style>` + the rendered
// tree. The `<style>` applies the same Defaults.theme variable bundle the TS
// side injects inline, so the two render visually identically too.
let private view (model: Model) (_: Msg -> unit) : ReactElement =
  match model.Decoded with
  | None -> Html.none
  | Some(Ok node) ->
    Html.div
      [ prop.className "fuaran-root"
        prop.children
          [ Render.themeStyleElement Defaults.theme
            if isLive then
              LiveTree node
            else
              Render.renderWithSources BindingResolver.empty ignore node ] ]
  | Some(Error err) ->
    Html.pre
      [ prop.className "fl-fable-host-error"
        prop.text (sprintf "%s – %s (at %s)" err.Code err.Message err.Path) ]

Program.mkProgram init update view
|> Program.withReactSynchronous "fable-host-root"
|> Program.run
