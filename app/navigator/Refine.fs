module Fuaran.Live.Refine

// ============================================================================
//  "Refine from here" — closing the generative loop inside the playground.
//
//  The cycle this pane makes operable is: the model EMITS, the human EDITS in
//  the navigator, and the human RE-PROMPTS — with the edited tree, not the
//  model's last guess, as the context the next emission starts from.
//
//  Last-mile correction is where generative UI usually falls over, and it falls
//  over in one of two directions. Regenerate, and the model rewrites from its
//  own stale idea of the artefact, discarding the fixes. Hand-edit, and you have
//  left the model behind — the next prompt has to re-describe from scratch what
//  you already did with your hands. Both failures are the same failure: the
//  human and the model are editing different things and neither can see the
//  other's work.
//
//  Here they are not. The human's property-panel commits and the model's
//  emissions are the same kind of event — a typed `TreeOp`, applied through the
//  same public engine, recorded in the same attributed chain — so handing the
//  work back is not an integration, it is just reading the tree and the trail.
//  `Session.refinePrompt` sends the current tree; `Session.refineSystemSuffix`
//  states the human's ops as constraints. This module is the surface over them,
//  plus the answer to the question a human immediately asks next:
//
//      "did it keep my edits?"
//
//  That question is answered by DIFF, not by assertion. The baseline — the
//  canonical bytes of the tree at the instant "refine from here" was pressed,
//  and the ids the human had touched — is captured before the run, and the
//  re-emission is diffed against it with the shipped `TreeDiff`. So the pane
//  reports what actually changed, including the case the feature exists to
//  prevent: an edited node the model overwrote anyway. A model can ignore its
//  context, and a loop that could only ever say "kept" would be decoration.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Replay

module Decode = Fuaran.UI.Ops.JsonDecode
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module Diff = Fuaran.UI.OpStream.Replay.TreeDiff

// ─── the baseline (captured at re-prompt time) ───────────────────────────────

/// What the human approved, frozen at the moment they asked for a refinement.
///
/// The tree is held as canonical JSON rather than as a `Node<obj>` for one
/// reason worth stating: it is the same bytes that went into the context. A
/// live reference could be re-pointed by an in-flight op and the comparison
/// would quietly become a comparison with something the model never saw.
type Baseline =
  {
    /// The canonical wire JSON of the edited tree that was sent as context.
    TreeJson: string
    /// The ids the human had edited since the last emission — the nodes whose
    /// survival is the actual claim under test.
    EditedIds: string list
    /// The ask that accompanied it (echoed in the readout, so the pane says
    /// what was asked as well as what changed).
    Prompt: string
  }

/// A named field of a canonical op document (the human trail's target ids).
[<Emit("""(function(j,f){ try { var v = JSON.parse(j)[f];
  return (typeof v === 'string') ? v : ''; } catch(e){ return ''; } })($0,$1)""")>]
let private opTarget (canonJson: string) (field: string) : string = jsNative

/// Freeze the current session as a refine baseline. `None` before there is a
/// tree — there is nothing to refine from.
let baselineOf (session: Session.SessionState) (prompt: string) : Baseline option =
  match session.Tree with
  | None -> None
  | Some tree ->
    let edited =
      Session.humanOpsSinceEmission session
      |> List.collect (fun e ->
        // `target` covers the addressed ops; `parentId` covers the structural
        // ones, whose subject is the parent that gained or reordered children.
        [ opTarget e.OpJson "target"; opTarget e.OpJson "parentId" ])
      |> List.filter (fun s -> s <> "")
      |> List.distinct

    Some
      { TreeJson = Canon.encodeNode tree
        EditedIds = edited
        Prompt = prompt }

/// The baseline tree, read back through the real strict decoder. `None` when the
/// bytes do not decode — which would be a defect in the encoder, not in the
/// baseline, and is reported as "no comparison" rather than as "no changes".
let baselineTree (baseline: Baseline) : Node<obj> option =
  match Decode.decodeNode baseline.TreeJson with
  | Error _ -> None
  | Ok node -> Some(WireTree.reify node)

// ─── the loop's stage (derived, never stored) ────────────────────────────────

/// Where the cycle stands. Derived from the session and the presence of a
/// baseline, so it cannot disagree with what is on screen.
[<RequireQualifiedAccess>]
type Stage =
  /// No tree yet — the loop has not started.
  | Empty
  /// A model emission is the newest thing; the human has changed nothing since.
  | Emitted
  /// The human has made `n` edits since the last emission.
  | Edited of int
  /// A refinement is in flight against a captured baseline.
  | Refining
  /// A refinement has come back and can be compared with the baseline.
  | Refined

let stage (session: Session.SessionState) (baseline: Baseline option) (running: bool) : Stage =
  match session.Tree, baseline, running with
  | None, _, _ -> Stage.Empty
  | Some _, Some _, true -> Stage.Refining
  | Some _, Some _, false -> Stage.Refined
  | Some _, None, _ ->
    match Session.humanOpCount session with
    | 0 -> Stage.Emitted
    | n -> Stage.Edited n

/// The stage as the strip reads it — "emitted → edited (n) → re-prompted" made
/// literal, because a loop nobody can see the shape of is not an affordance.
let stageLabel (s: Stage) : string =
  match s with
  | Stage.Empty -> "nothing emitted yet"
  | Stage.Emitted -> "emitted — walk the tree and edit it, then refine from here"
  | Stage.Edited 1 -> "emitted → edited (1 of your ops)"
  | Stage.Edited n -> sprintf "emitted → edited (%d of your ops)" n
  | Stage.Refining -> "emitted → edited → re-prompted (running…)"
  | Stage.Refined -> "emitted → edited → re-prompted → re-emitted"

// ─── the comparison ──────────────────────────────────────────────────────────

/// One change, as a line. Reuses the shipped classification rather than
/// re-deriving one: `Added` / `Removed` / `Moved` / `KindChanged` /
/// `PropChanged` / `TextChanged` is the estate's vocabulary for what happened
/// between two snapshots, and the playground should not invent a second.
let changeLine (change: NodeChange) : string =
  let (NodeId id) = change.NodeId

  match change.Change with
  | NodeChangeKind.Added _ -> sprintf "added #%s" id
  | NodeChangeKind.Removed _ -> sprintf "removed #%s" id
  | NodeChangeKind.Moved _ -> sprintf "moved #%s" id
  | NodeChangeKind.KindChanged(fromKind, toKind) -> sprintf "#%s: %s → %s" id fromKind toKind
  | NodeChangeKind.TextChanged(fromText, toText) -> sprintf "#%s: \"%s\" → \"%s\"" id fromText toText
  | NodeChangeKind.PropChanged _ -> sprintf "#%s: properties changed" id

/// The changes the re-emission made against the edited baseline (`[]` when
/// there is nothing to compare — no baseline, no live tree, or bytes that will
/// not decode).
let changes (session: Session.SessionState) (baseline: Baseline option) : NodeChange list =
  match baseline, session.Tree with
  | Some b, Some live ->
    match baselineTree b with
    | Some before -> (Diff.diff before live).Changes
    | None -> []
  | _ -> []

/// Per edited node: did the re-emission leave it alone? An id absent from the
/// diff was retained — which is the acceptance criterion, stated as a fact
/// about the two trees rather than as a hope about the prompt.
let retention (session: Session.SessionState) (baseline: Baseline option) : (string * bool) list =
  match baseline with
  | None -> []
  | Some b ->
    let touched =
      changes session baseline
      |> List.map (fun c ->
        let (NodeId id) = c.NodeId
        id)
      |> Set.ofList

    b.EditedIds |> List.map (fun id -> id, not (Set.contains id touched))

// ─── flat diagnostic surface (cross-boundary friendly) ───────────────────────
//
// The same projection-to-plain-values discipline as `Session.ingestResult` and
// the Phase 710–712 helpers: F# lists and DUs are awkward to assert on from the
// JS side of the Fable boundary, so the loop's claims are also available as
// arrays and flat records.

let changeLines (session: Session.SessionState) (baseline: Baseline option) : string array =
  changes session baseline |> List.map changeLine |> Array.ofList

/// The edited ids the re-emission left untouched.
let retainedIds (session: Session.SessionState) (baseline: Baseline option) : string array =
  retention session baseline |> List.filter snd |> List.map fst |> Array.ofList

/// The edited ids the re-emission changed anyway — the honest half.
let overwrittenIds (session: Session.SessionState) (baseline: Baseline option) : string array =
  retention session baseline
  |> List.filter (snd >> not)
  |> List.map fst
  |> Array.ofList

// ─── the pane ────────────────────────────────────────────────────────────────

/// The comparison readout — shown only once a refinement has come back.
let private comparison (session: Session.SessionState) (baseline: Baseline) : ReactElement =
  let lines = changeLines session (Some baseline)
  let retained = retainedIds session (Some baseline)
  let overwritten = overwrittenIds session (Some baseline)

  Html.div
    [ prop.className "fl-rf-result"
      prop.children
        [ Html.p
            [ prop.className "fl-nav-count"
              prop.text (
                if lines.Length = 0 then
                  "The re-emission is identical to the version you approved."
                else
                  sprintf "%d change(s) against the version you approved:" lines.Length
              ) ]
          (if lines.Length = 0 then
             Html.none
           else
             Html.ul
               [ prop.className "fl-rf-changes"
                 prop.children
                   [ for line in lines do
                       Html.li [ prop.key line; prop.className "fl-rf-change"; prop.text line ] ] ])
          (if retained.Length = 0 && overwritten.Length = 0 then
             Html.none
           else
             Html.p
               [ prop.className "fl-rf-retention"
                 prop.text (
                   (if retained.Length > 0 then
                      sprintf "Your edits kept: %s. " (String.concat ", " retained)
                    else
                      "")
                   + (if overwritten.Length > 0 then
                        sprintf "Changed anyway: %s." (String.concat ", " overwritten)
                      else
                        "")
                 ) ]) ] ]

[<ReactComponent>]
let RefinePane
  (session: Session.SessionState)
  (baseline: Baseline option)
  (running: bool)
  (canSend: bool)
  (onRefine: string -> unit)
  : ReactElement =
  let draft, setDraft = React.useState ""
  let here = stage session baseline running
  let corrections = Session.correctionLineArray session

  let submit () =
    if draft.Trim() <> "" && canSend && not running then
      onRefine (draft.Trim())
      setDraft ""

  Html.div
    [ prop.className "fl-rf"
      prop.children
        [ Html.p [ prop.className "fl-rf-stage"; prop.text (stageLabel here) ]
          Html.p
            [ prop.className "fl-nav-help"
              prop.text
                "Refining sends the tree AS YOU HAVE EDITED IT — not the model's last emission — plus a note of \
what you changed, so the next version starts from what you approved." ]
          (if corrections.Length = 0 then
             Html.none
           else
             Html.details
               [ prop.className "fl-rf-trail"
                 prop.children
                   [ Html.summary [ prop.text (sprintf "What will be sent as your edits (%d)" corrections.Length) ]
                     Html.ul
                       [ prop.children
                           [ for line in corrections do
                               Html.li [ prop.key line; prop.text line ] ] ] ] ])
          Html.textarea
            [ prop.className "fl-prompt fl-rf-input"
              prop.value draft
              prop.rows 2
              prop.placeholder "Refine from here — what should change next?"
              prop.disabled (running || Option.isNone session.Tree)
              prop.ariaLabel "Refine from here"
              prop.onChange (fun (v: string) -> setDraft v)
              // Enter submits, Shift+Enter is a newline — so the whole loop is
              // reachable from the keyboard without leaving the tab.
              prop.onKeyDown (fun ev ->
                if ev.key = "Enter" && not ev.shiftKey then
                  ev.preventDefault ()
                  submit ()) ]
          Html.div
            [ prop.className "fl-nav-controls"
              prop.children
                [ Html.button
                    [ prop.className "fl-btn ghost"
                      prop.text (if running then "Refining…" else "Refine from here ↻")
                      prop.disabled (running || draft.Trim() = "" || not canSend || Option.isNone session.Tree)
                      prop.title
                        "Send the tree as you have edited it, plus your edits as constraints, and ask for the next version"
                      prop.onClick (fun _ -> submit ()) ] ] ]
          (match baseline with
           | Some b when not running -> comparison session b
           | _ -> Html.none) ] ]

/// The Navigator tab with the loop beneath it. Takes the already-built navigator
/// element rather than its inputs — the same composition posture `ProjectionSync.beside`
/// takes, and for the same reason: this pane is unaffected by whatever the
/// Navigator's own entry point happens to take, so the two evolve independently.
let below
  (navigatorTab: ReactElement)
  (session: Session.SessionState)
  (baseline: Baseline option)
  (running: bool)
  (canSend: bool)
  (onRefine: string -> unit)
  : ReactElement =
  Html.div
    [ prop.className "fl-nav-loop"
      prop.children [ navigatorTab; RefinePane session baseline running canSend onRefine ] ]
