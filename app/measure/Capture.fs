module Fuaran.Live.Measure.Capture

// ============================================================================
//  The browser half of the render-latency harness (Phase 1628).
//
//  Everything here needs a real browser: a DOM to commit into, a layout engine
//  to force, and the real `Fuaran.UI.Renderer` to drive. The pure half lives in
//  `Timing.fs` and `Baseline.fs` and is unit-tested headlessly.
//
//  THE CAPTURE IS THE POINT OF THIS PHASE. The harness Phase 202 built was
//  complete apparatus that never took a reading — its own evidence document
//  recorded the real-browser run as deferred and its decision table was four
//  rows of `pending` — and the perf gate's `render.*` axes have stood armed
//  over an absence ever since. A harness that builds and does not capture has
//  not discharged anything.
//
//  Method, and the reason for each choice:
//
//   * ONE SAMPLE IS A BATCH OF `batch` RENDERS, DIVIDED. This is not an
//     optimisation; it is the difference between a measurement and a coin flip.
//     `performance.now()` in an ordinary (non-cross-origin-isolated) browsing
//     context is CLAMPED — 100 microseconds in Chromium — and the smallest
//     corpus tree renders in about one tick. Timed singly, every `Small` sample
//     reads 0.100 ms, no regression below 100% is visible, and the axis reports
//     the clock rather than the renderer. Timing forty renders and dividing
//     puts every sample two to three orders of magnitude above the tick. THE
//     FIRST VERSION OF THIS HARNESS TIMED SINGLE RENDERS, and its whole corpus
//     came back as exact multiples of 0.100 ms — which is how the clamp was
//     found. Do not undo the batching without re-checking for that signature.
//   * DECODING IS OUTSIDE THE CLOCK. Each corpus document is decoded once,
//     before any timing. This measures rendering; the decoder has its own axes.
//   * THE TREE PROJECTION IS INSIDE IT. `Render.renderWithSources` runs per
//     iteration, inside the timed region, because projecting a Fuaran node tree
//     into elements is the Fuaran-specific half of the cost and the half a
//     renderer regression lands in. Hoisting it out would leave the axis
//     measuring React.
//   * EACH ITERATION MOUNTS A FRESH ROOT into a fresh container, both created
//     outside the clock. Re-rendering into an existing root would measure a
//     diff of an identical tree, which is a different and much cheaper
//     operation than an initial render.
//   * THE COMMIT IS FORCED SYNCHRONOUS (`flushSync`). Without it React 19
//     schedules the work and `render` returns before any DOM exists, so the
//     mark would measure the scheduling call.
//   * THE TWO MARKS ARE TWO PASSES over the same tree, rather than two marks
//     inside one iteration — because batching is what makes either readable and
//     a batch cannot carry a nested mark. The `ttfp` pass commits without
//     forcing layout; the `full` pass forces a style + layout flush after each
//     commit. Layout is drained after every batch, outside the clock, so one
//     pass never inherits the other's deferred work.
//   * WARM-UP BATCHES RUN FIRST and are discarded. The first render of a shape
//     pays one-off costs — module initialisation, first-call paths, style-sheet
//     matching for class names not yet seen — that are not what a regression
//     gate is looking for.
//
//  The capture is synchronous from first mark to last: it never yields to the
//  event loop, so no frame is painted during it. That is consistent with what
//  the two marks claim (commit, and commit + layout — neither claims paint) and
//  it is what keeps the numbers off the compositor's 16.7 ms tick.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Browser
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops.Types

module Decode = Fuaran.UI.Ops.JsonDecode
module Brand = Fuaran.Live.Brand

// The stylesheets a rendered tree is laid out under on a real page. They are
// part of the measurement rather than decoration: class-name matching and the
// resulting box model are most of what the forced layout pass costs, so a tree
// measured without them is not the tree the site renders. The playground
// SHELL's own stylesheet is deliberately absent — it styles chrome this harness
// never mounts.
importSideEffects "@fuaran-ui/renderer/css"
importSideEffects "../brand/fuaran-brand.css"

[<Literal>]
let private rootId = "fuaran-measure-root"

[<Literal>]
let private themeId = "fuaran-measure-theme"

/// Renders per timed sample. Chosen so that the SMALLEST corpus tree — about
/// one clock tick when timed singly — produces a batch of several milliseconds,
/// two orders of magnitude above the 100 microsecond clamp, while the largest
/// keeps a batch well inside a tenth of a second. Raising it is safe; lowering
/// it walks back towards measuring the clock.
[<Literal>]
let DefaultBatch = 40

[<Import("createRoot", "react-dom/client")>]
let private createRoot (container: Browser.Types.Element) : obj = jsNative

[<Import("flushSync", "react-dom")>]
let private flushSync (f: unit -> unit) : unit = jsNative

/// Force a synchronous style + layout pass over the document. Reading a
/// geometry property is the canonical way to do it; `void` discards the value
/// so no optimiser can decide the read is dead.
[<Emit("void document.documentElement.offsetHeight")>]
let private forceLayout () : unit = jsNative

[<Emit("new Date().toISOString()")>]
let private nowIso () : string = jsNative

[<Emit("navigator.userAgent")>]
let private userAgent () : string = jsNative

/// One sample: `batch` initial renders of `node`, timed together and divided.
/// `withLayout` selects the pass — `false` is the `render.ttfp.*` mark (project
/// and commit), `true` the `render.full.*` mark (the same, plus a forced style
/// and layout flush after each commit).
///
/// Containers, roots and teardown are all outside the clock: element allocation
/// and React root construction are the harness's own costs, not the renderer's,
/// and at the small end they would be most of the number.
let private timeBatch (host: Browser.Types.Element) (node: Node<obj>) (batch: int) (withLayout: bool) : float =
  let containers =
    Array.init batch (fun _ ->
      let c = document.createElement "div"
      host.appendChild c |> ignore
      c)

  let roots = containers |> Array.map createRoot

  let t0 = Timing.nowMs ()

  for i in 0 .. batch - 1 do
    // `roots[i]` is bound before the dynamic call rather than written inline:
    // Fantomas 7.0.5 crashes formatting an index-without-dot expression under
    // the `?` operator ("cannot determine if Expr IndexWithoutDot ... is
    // uppercase or lowercase"), and an unformattable file fails the gate.
    let root = roots[i]
    let element = Render.renderWithSources BindingResolver.empty ignore node
    flushSync (fun () -> root?render (box element) |> ignore)

    if withLayout then
      forceLayout ()

  let elapsed = Timing.nowMs () - t0

  for i in 0 .. batch - 1 do
    let root = roots[i]
    root?unmount () |> ignore
    host.removeChild containers[i] |> ignore

  // Drain whatever the un-forced pass deferred, outside the clock, so the next
  // batch does not open by paying for this one.
  forceLayout ()
  elapsed / float batch

/// Decode every corpus document once. A tree that does not decode FAILS the
/// capture loudly rather than being skipped: a skipped tree emits a null metric,
/// which makes the whole artefact read as not-captured — a confusing way to
/// report a corpus that has fallen out of step with the wire format.
let private decodeCorpus () : (Corpus.CorpusTree * Node<obj>) list =
  Corpus.corpus
  |> List.map (fun t ->
    match Decode.decodeNode t.Wire with
    | Ok wire -> t, WireTree.reify wire
    | Error e -> failwithf "corpus tree '%s' does not decode: %s — %s (at %s)" t.Label e.Code e.Message e.Path)

/// Mount the theme's CSS custom properties once, outside every timed region.
/// The variables are what the reference stylesheet resolves against, so a tree
/// measured without them is laid out against fallbacks; mounting them per
/// iteration would instead fold a constant style-sheet insertion into every
/// sample.
let private mountTheme () =
  let el = document.getElementById themeId

  if not (isNull (box el)) && isNull (box (el.getAttribute "data-mounted")) then
    let root = createRoot el
    flushSync (fun () -> root?render (box (Render.themeStyleElement (Brand.theme false))) |> ignore)
    el.setAttribute ("data-mounted", "1")

let private host () : Browser.Types.Element =
  let el = document.getElementById rootId

  if isNull (box el) then
    failwithf "the measure page has no #%s element to render into" rootId

  el

let private intOr (o: obj) (name: string) (fallback: int) : int =
  let v = o?(name)
  if isNull (box v) then fallback else int (unbox<float> v)

let private strOr (o: obj) (name: string) (fallback: string) : string =
  let v = o?(name)
  if isNull (box v) then fallback else string v

/// Run the corpus and return the captured baseline artefact as JSON text.
///
/// `optionsJson` carries `{ repeats, warmups, machineCpu, machineOs, dotnet }`;
/// the machine fields come from the driver, which can see the host, and the
/// browser engine and version are read here, where they are known. The two are
/// stamped together into `runtime.cpu` — see `Timing.browserId`.
let capture (optionsJson: string) : string =
  let opts: obj =
    if System.String.IsNullOrWhiteSpace optionsJson then
      createObj []
    else
      JS.JSON.parse optionsJson

  let repeats = intOr opts "repeats" 25
  let warmups = intOr opts "warmups" 2
  let batch = intOr opts "batch" DefaultBatch
  let machineCpu = strOr opts "machineCpu" ""
  let machineOs = strOr opts "machineOs" ""
  let dotnet = strOr opts "dotnet" "n/a (browser host)"

  mountTheme ()
  let h = host ()
  let decoded = decodeCorpus ()

  let sample (node: Node<obj>) (withLayout: bool) =
    for _ in 1..warmups do
      timeBatch h node batch withLayout |> ignore

    [ for _ in 1..repeats -> timeBatch h node batch withLayout ]

  let stats =
    [ for (tree, node) in decoded do
        yield Timing.aggregate (Timing.ttfpMetric tree.Label) (sample node false)
        yield Timing.aggregate (Timing.fullMetric tree.Label) (sample node true) ]

  let browser = Timing.browserId (userAgent ())

  // The machine and the engine are ONE host identity. A run whose driver names
  // no machine still names the engine, so the artefact is never unstamped —
  // which matters, because an unstamped runtime is what the gate reads as
  // "unknown host" and withholds every wall-time axis over.
  let cpu =
    if machineCpu = "" then
      browser
    else
      machineCpu + " · " + browser

  Baseline.captured
    stats
    (nowIso ())
    { Dotnet = dotnet
      Os = machineOs
      Cpu = cpu }

/// The corpus's own shape facts, for a driver that wants to report what it
/// measured without re-deriving them.
let corpusSummary () : string =
  let rows =
    Corpus.corpus
    |> List.map (fun t -> createObj [ "label", box t.Label; "nodeCount", box t.NodeCount; "depth", box t.Depth ])
    |> Array.ofList

  JS.JSON.stringify (box rows)

// The page's only surface. A driver (a Playwright spec, or a human with a
// console) calls `window.__fuaranMeasure.capture(JSON.stringify(options))` and
// receives the artefact text. Strings in both directions, deliberately: the
// boundary then carries no F#-shaped value whose representation could change
// under it.
window?__fuaranMeasure <-
  createObj
    [ "capture", box (System.Func<string, string>(capture))
      "corpus", box (System.Func<string>(corpusSummary)) ]
