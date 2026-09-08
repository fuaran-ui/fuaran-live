module Fuaran.Live.Measure.Baseline

// ============================================================================
//  The render-latency baseline artefact (Phase 1628).
//
//  The same envelope every other perf-baseline producer writes and the perf
//  release-gate consumes: `schema_version` / `artifact` / `status` /
//  `captured_at_utc` / `runtime` / `metrics[]`, values in the metric's own
//  `unit`. The gate reads it with a plain JSON reader keyed on those names, so
//  the shape — not any type here — is the contract.
//
//  `runtime` is LOAD-BEARING and is why this module takes a runtime record
//  rather than inventing one. The gate classifies a host-sensitive unit (`ms`
//  is one) as `Incomparable` when two artefacts name different hosts, or when
//  either names none: an unstamped runtime reads as UNKNOWN, never as
//  agreement. So `cpu` carries the machine AND the browser engine and version,
//  because a millisecond measured in one engine says nothing about the same
//  render in another.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop

[<Literal>]
let SchemaVersion = 1

[<Literal>]
let Artifact = "render-latency"

/// The host an artefact was captured on. Every field is a string, and an empty
/// `cpu` is what the gate reads as "this artefact does not name its host".
type RuntimeInfo =
  { Dotnet: string
    Os: string
    Cpu: string }

type PerfMetric =
  {
    Id: string
    /// `None` serialises as JSON `null` — the `pending` shape.
    Value: float option
    Unit: string
    Note: string
  }

/// The declared metric catalogue: one TTFP + one full-render metric per corpus
/// label. These ids are the contract the budget table keys against.
let catalogue () : (string * string * string) list =
  [ for t in Corpus.corpus do
      Timing.ttfpMetric t.Label,
      "ms",
      sprintf "Time to committed DOM — render start to the tree committed (%d nodes, %d levels)." t.NodeCount t.Depth

      Timing.fullMetric t.Label,
      "ms",
      sprintf
        "Time to laid-out render — commit plus a forced style + layout pass (%d nodes, %d levels)."
        t.NodeCount
        t.Depth ]

let private noteOf (id: string) =
  catalogue ()
  |> List.tryPick (fun (i, _, n) -> if i = id then Some n else None)
  |> Option.defaultValue ""

let private unitOf (id: string) =
  catalogue ()
  |> List.tryPick (fun (i, u, _) -> if i = id then Some u else None)
  |> Option.defaultValue "ms"

/// Fold a captured run's stats into the artefact's metric rows.
///
/// The baseline records the **minimum** batch, and that is a deliberate
/// departure from the retired Phase 202 emitter, which recorded the p50.
/// Timing noise in a browser is ONE-SIDED — an interruption can only add time
/// to a render, never remove it — so the fastest observed batch is the sample
/// least contaminated by everything that is not the renderer, and the median
/// sits in the middle of the contamination. It is not a theoretical
/// preference: measured over ten captures of unchanged code on the reference
/// workstation, the worst run-to-run spread was 36.1% recording the p50 and
/// 25.0% recording the minimum, over the same configuration. The measurement
/// is tabulated in `README.md` beside this file.
///
/// A stat with no usable samples emits a `null` value, which makes the whole
/// artefact read as not-captured to the gate rather than as a captured zero.
let metricsOf (stats: Timing.RenderStat seq) : PerfMetric list =
  // Rounded to a tenth of a microsecond before serialisation. A raw IEEE
  // double of a divided batch total is written as `0.04749999940395355`, which
  // is nineteen digits of which three are measurement and sixteen are binary
  // representation — noise in the committed artefact and in every diff of it.
  // Four decimal places keeps at least three significant figures on the
  // smallest metric in the corpus.
  let round4 (v: float) = System.Math.Round(v, 4)

  stats
  |> Seq.map (fun s ->
    { Id = s.Metric
      Value = (if s.Samples = 0 then None else Some(round4 s.Min))
      Unit = unitOf s.Metric
      Note = noteOf s.Metric })
  |> List.ofSeq

/// Serialise the artefact. Keys are emitted in declaration order rather than
/// through an anonymous record (whose fields F# sorts alphabetically), so the
/// committed file reads in the same order as every sibling baseline.
let private json (status: string) (capturedAtUtc: string) (runtime: RuntimeInfo) (metrics: PerfMetric list) : string =
  let rows =
    metrics
    |> List.map (fun m ->
      createObj
        [ "id", box m.Id
          "value",
          (match m.Value with
           | Some v -> box v
           | None -> null)
          "unit", box m.Unit
          "note", box m.Note ])
    |> Array.ofList

  let doc =
    createObj
      [ "schema_version", box SchemaVersion
        "artifact", box Artifact
        "status", box status
        "captured_at_utc", box capturedAtUtc
        "runtime", createObj [ "dotnet", box runtime.Dotnet; "os", box runtime.Os; "cpu", box runtime.Cpu ]
        "metrics", box rows ]

  JS.JSON.stringify (doc, unbox null, 2) + "\n"

/// The captured artefact: every declared metric carries a number.
let captured (stats: Timing.RenderStat seq) (capturedAtUtc: string) (runtime: RuntimeInfo) : string =
  json "captured" capturedAtUtc runtime (metricsOf stats)

/// The pending template: every metric declared, every value null. Emitted by
/// nothing in the ordinary path — it exists so that "what does this artefact
/// look like before it is captured" has one answer, and so a harness that
/// produced no usable sample cannot present as a captured run.
let pendingTemplate () : string =
  json
    "pending"
    ""
    { Dotnet = ""; Os = ""; Cpu = "" }
    (catalogue ()
     |> List.map (fun (id, unit, note) ->
       { Id = id
         Value = None
         Unit = unit
         Note = note }))
