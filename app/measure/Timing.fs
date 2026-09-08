module Fuaran.Live.Measure.Timing

// ============================================================================
//  The timing primitives of the render-latency harness (Phase 1628).
//
//  Pure, DOM-free, renderer-free — everything here type-checks and unit-tests
//  headlessly, which is why it is a separate module from `Capture.fs`. The
//  browser-only half is exactly the half that needs a browser.
//
//  WHAT THE TWO MARKS ARE, precisely, because the metric names outlive the
//  code and a reader of `render.ttfp.Large.ms` deserves to know what was
//  timed:
//
//    render.ttfp.<Label>.ms  render start -> the tree is COMMITTED TO THE DOM.
//                            The first instant at which anything could be
//                            painted. Under a whole-tree renderer that is the
//                            commit of the entire tree.
//    render.full.<Label>.ms  render start -> committed AND LAID OUT. The same
//                            commit plus a forced synchronous style + layout
//                            pass over the document, so the browser's own work
//                            on the tree is inside the number.
//
//  So `full >= ttfp` by construction and the gap is the layout cost. This is a
//  DELIBERATE REFRAMING of the retired Phase 202 harness's pair, which meant
//  "first committed subtree" vs "whole tree committed" and existed to answer
//  whether PROGRESSIVE render beat whole-tree render. That question is answered
//  and closed — Phase 1147 measured it on the emission side and recorded no
//  crossover at any tree size — and this renderer commits whole trees, so under
//  the old definitions the two marks would be the same number and one of the
//  gate's two axes would be decoration. These definitions measure two different
//  real costs of the same render.
//
//  Both marks come off `performance.now()` and neither waits for a frame. A
//  mark taken inside `requestAnimationFrame` would be quantised to the
//  compositor's ~16.7 ms tick, which for a render costing single-digit
//  milliseconds is a frame-rate metric wearing a render metric's name.
// ============================================================================

open Fable.Core

/// A summary over many samples of one metric — one baseline row. Mirrors the
/// `RenderStat` shape the shared perf-baseline producers report.
type RenderStat =
  { Metric: string
    Samples: int
    Min: float
    Mean: float
    P50: float
    P95: float
    Max: float }

/// Monotonic wall-clock in milliseconds. `performance.now()` where available —
/// which in every browser this harness runs in, it is.
[<Emit("(typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now()")>]
let nowMs () : float = jsNative

/// Nearest-rank percentile (0..1) of an already-sorted, non-empty array.
let private percentileOfSorted (p: float) (sorted: float[]) : float =
  if sorted.Length = 0 then
    nan
  else
    let rank = int (ceil (p * float sorted.Length)) - 1
    sorted[max 0 (min (sorted.Length - 1) rank)]

/// Summarise a metric's samples. NaN and infinite values are DROPPED rather
/// than folded in: an un-taken mark is not a zero, and one NaN would otherwise
/// poison the mean of an otherwise good run. An empty result (`Samples = 0`)
/// carries NaN throughout, which the emitter renders as a null value — the
/// artefact then reads as `pending` to the gate rather than as a captured zero.
/// Takes a `seq` rather than a list so the module stays callable across the
/// Fable boundary from the unit suite, where an F# list is not a JS array.
let aggregate (metric: string) (values: float seq) : RenderStat =
  let usable =
    values
    |> Seq.filter (fun v -> not (System.Double.IsNaN v || System.Double.IsInfinity v))
    |> Seq.sort
    |> List.ofSeq

  match usable with
  | [] ->
    { Metric = metric
      Samples = 0
      Min = nan
      Mean = nan
      P50 = nan
      P95 = nan
      Max = nan }
  | _ ->
    let arr = Array.ofList usable

    { Metric = metric
      Samples = arr.Length
      Min = arr[0]
      Mean = (Array.sum arr) / float arr.Length
      P50 = percentileOfSorted 0.5 arr
      P95 = percentileOfSorted 0.95 arr
      Max = arr[arr.Length - 1] }

/// The metric id a corpus label + phase produce. One place, because these
/// strings ARE the keys the consuming performance gate matches its budget rules
/// against (by the `render.ttfp.` / `render.full.` prefix) — a typo here is not
/// a typo, it is an axis the gate silently stops recognising.
let ttfpMetric (label: string) : string = sprintf "render.ttfp.%s.ms" label

let fullMetric (label: string) : string = sprintf "render.full.%s.ms" label

/// A compact browser-engine identity read from a User-Agent string —
/// `"Chromium 131.0.6778.85"`, `"Firefox 133.0"`, `"unknown"`.
///
/// It is stamped into the baseline's `runtime.cpu` beside the machine, and that
/// is the point rather than decoration: the perf gate declines to compare a
/// host-sensitive unit (`ms` is one) across artefacts whose hosts differ, so
/// folding the engine into the host identity makes "measured in a different
/// browser" as un-comparable as "measured on a different machine" — which it
/// is. An unrecognised agent reads as `unknown`, never as a match.
///
/// Order is load-bearing: every Chromium-family agent carries `Chrome/`, so the
/// more specific brands are tested first.
let browserId (userAgent: string) : string =
  let after (marker: string) : string option =
    let i = userAgent.IndexOf marker

    if i < 0 then
      None
    else
      let rest = userAgent.Substring(i + marker.Length)
      let stop = rest.Split([| ' '; ';'; ')' |])

      if stop.Length = 0 || stop[0] = "" then
        None
      else
        Some stop[0]

  let named (marker: string) (name: string) =
    after marker |> Option.map (fun v -> name + " " + v)

  named "Edg/" "Edge"
  |> Option.orElseWith (fun () -> named "Firefox/" "Firefox")
  |> Option.orElseWith (fun () -> named "HeadlessChrome/" "HeadlessChrome")
  |> Option.orElseWith (fun () -> named "Chrome/" "Chromium")
  |> Option.orElseWith (fun () -> named "Version/" "Safari")
  |> Option.defaultValue "unknown"
