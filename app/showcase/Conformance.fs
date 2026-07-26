module Fuaran.Showcase.Conformance

// ============================================================================
//  The CI conformance-artifact panel – honest status, never a fake green.
//
//  The showcase deploy runs the projection-conformance gate (every Node fixture in
//  the wire-format corpus, projected to source, executed, and byte-compared on
//  re-encode) and publishes a versioned report JSON to
//  public/conformance/report.generated.json — see scripts/conformance-report.mjs
//  and public/conformance/README.md. This module loads that report and renders a
//  shared panel. The load-bearing rule: when the
//  report is MISSING or STALE the panel goes grey and says so – it never
//  fabricates a pass. "Run the gate yourself" beats "trust the demo".
// ============================================================================

open Fable.Core
open Elmish
open Fuaran.UI
open Fuaran.UI.Types

/// The published conformance-gate report shape (see public/conformance/README.md).
type Report =
  { Generated: string
    Commit: string
    Passed: int
    Failed: int
    Total: int
    Ok: bool }

/// The panel's state. `Stale` is the honest-status leg – a grey panel with a
/// reason, shown whenever the report is absent, unparseable, or self-reports a
/// failure it cannot dress up.
[<RequireQualifiedAccess>]
type PanelState =
  | Pending
  | Ready of Report
  | Stale of string

let reportUrl = "./conformance/report.generated.json"

[<Emit("fetch($0).then(function(r){ if (!r.ok) throw new Error('HTTP ' + r.status); return r.text(); }).then($1).catch(function(e){ $2(String(e && e.message ? e.message : e)); })")>]
let private fetchInto (url: string) (onText: string -> unit) (onErr: string -> unit) : unit = jsNative

[<Emit("(function(){ try { return JSON.parse($0); } catch (e) { return null; } })()")>]
let private tryParseJson (s: string) : obj = jsNative

[<Emit("($0 == null ? null : $0[$1])")>]
let private field (o: obj) (k: string) : obj = jsNative

let private parseReport (raw: string) : PanelState =
  let o = tryParseJson raw

  if isNull (box o) then
    PanelState.Stale "the published report could not be parsed"
  else
    let getStr (k: string) : string =
      let v = field o k
      if isNull (box v) then "" else string v

    let getInt (k: string) : int =
      let v = field o k
      if isNull (box v) then 0 else int (unbox<float> v)

    let getBool (k: string) : bool =
      let v = field o k
      not (isNull (box v)) && unbox<bool> v

    PanelState.Ready
      { Generated = getStr "generated"
        Commit = getStr "commit"
        Passed = getInt "passed"
        Failed = getInt "failed"
        Total = getInt "total"
        Ok = getBool "ok" }

/// Load the conformance report as an Elmish command. A missing report is not an
/// error – it is the honest "no gate has run for this deploy yet" grey state.
let loadCmd (onResult: PanelState -> 'Msg) : Cmd<'Msg> =
  Cmd.ofEffect (fun dispatch ->
    fetchInto reportUrl (parseReport >> onResult >> dispatch) (fun _ ->
      dispatch (onResult (PanelState.Stale "no conformance report has been published for this deploy yet"))))

let private shortSha (c: string) : string =
  if c = "" then "unknown" else c.Substring(0, min 7 c.Length)

[<Literal>]
let SpecUrl = "https://fuaran-ui.io/guide/wire-format"

/// What the published numbers actually certify. Stated in the box because a bare
/// "85 of 85 checks pass" invites the reader to guess what was checked – and the
/// honest claim is narrower and more interesting than the guess: the gate is a
/// byte-comparison against the specification corpus, so conformance here is
/// measured, not asserted.
///
/// Ordered benefit-first deliberately. The mechanism is the reason to believe it,
/// but it is not the reason to care – a reader who stops after the first line
/// should already have the point (the code on these pages is real, and it
/// travels). The closing paragraph is load-bearing in the other direction: this
/// report does NOT cover a rendering audit or the full cross-host matrix, and a
/// panel that exists to refuse a fake green cannot overclaim in its own note.
let private note =
  sprintf
    """**What this means.** The code on these pages is certified, not illustrative: copy it out and it reconstructs exactly the tree you were looking at, on any conformant host.

**How it is checked.** Every node fixture in the [wire-format specification](%s) corpus is projected to source by this site's own projector, executed against the real `@fuaran-ui/*` packages, and re-encoded – and the bytes must match the fixture exactly. No structural comparison, no "close enough".

**Why that is worth something.** The oracle is the published specification rather than this implementation's own expectations, and it is the same corpus every other host runs – so portability across hosts is measured, not promised. The figures above come from this deploy's own run and carry its commit, so the claim is checkable rather than a badge.

**What it does not cover.** The node-fixture corpus through this site's projector: not a rendering audit, and not the full cross-host matrix."""
    SpecUrl

/// The shared status panel: a Fuaran card wrapping an honest-status callout plus
/// the note above – the same shape as the evaluation card beside it, so the two
/// live artefacts read as one pair. Grey (Subdued) on Pending/Stale, green
/// (Success) only on a genuine all-pass, red (Critical) on a reported failure.
/// The tone can only be green when the report says every check passed.
let panel (state: PanelState) : Node<'Msg> =
  let tone, heading, body =
    match state with
    | PanelState.Pending -> ToneVariant.Subdued, "Conformance – checking…", "Loading the latest gate report."
    | PanelState.Stale reason ->
      ToneVariant.Subdued,
      "Conformance – status unavailable",
      sprintf "Showing grey rather than a fake green: %s." reason
    | PanelState.Ready r when r.Ok && r.Failed = 0 ->
      ToneVariant.Success,
      "Conformance – green",
      sprintf "%d of %d checks pass · commit %s · %s." r.Passed r.Total (shortSha r.Commit) r.Generated
    | PanelState.Ready r ->
      ToneVariant.Critical,
      "Conformance – failing",
      sprintf "%d of %d checks fail · commit %s · %s." r.Failed r.Total (shortSha r.Commit) r.Generated

  Fuaran.card
    "ds-conformance-card"
    { Defaults.card with
        Children =
          [ Fuaran.callout
              "ds-conformance"
              { Defaults.callout with
                  Tone = tone
                  Heading = Some(TextSource.Literal heading)
                  Body = TextSource.Literal body }
            Fuaran.markdown "ds-conformance-note" note ] }
