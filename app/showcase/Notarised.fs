module Fuaran.Showcase.Notarised

// ============================================================================
//  The Notarised Dashboard – an AI-built dashboard that is audit-ready by
//  construction. Pillar: "the app is a value" (its governance face).
//
//  A finance dashboard is authored across a scripted 9-turn history (7 assistant
//  turns + 2 human edits), each turn a real hash-chained `OpRecord`. Click any
//  node to open its provenance dossier – which ops created or touched it, by whom
//  (human vs assistant), and the chain hash. An audit strip shows one link per op
//  with the head hash. The tamper button genuinely rewrites a historical op's
//  payload and re-runs the SHIPPED `Verify.chain`: the strip breaks red at the
//  exact link and the doctored replay is refused, with the library's real
//  `VerificationError` shown verbatim.
//
//  Honest scope: hash-chaining proves the integrity of the record, not the
//  cryptographic identity of the author – signed attestation is a separate,
//  future concern, and the footer says so rather than overclaiming.
// ============================================================================

open System
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

module Apply = Fuaran.UI.Ops.Apply

let private nid (s: string) : NodeId = NodeId s
let private nodeIdStr (NodeId s) : string = s

let private shortHash (h: string) : string =
  if h.Length <= 10 then h else h.Substring(0, 10)

// ─── The dashboard, as intent – tree pieces the 9-turn history builds ────────

let private metricCard (id: string) (label: string) (value: string) : Node<unit> =
  Fuaran.card
    id
    { Defaults.card with
        Heading = Some(TextSource.Literal label)
        Children = [ Fuaran.markdown (id + "-v") value ] }

let private initialTree: Node<unit> =
  Fuaran.box
    "nd-root"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
      Role = BoxRole.Dashboard
      Heading = Some(TextSource.Literal "Q3 finance review")
      Children =
        [ Fuaran.callout
            "nd-note"
            { Defaults.callout with
                Tone = ToneVariant.Subdued
                Heading = None
                Body = TextSource.Literal "Drafted by the assistant; figures pending finance review." } ] }

// ─── The recorded 9-turn history – real TreeOps, mixed authorship ────────────

type private Turn =
  { Op: TreeOp<unit>
    Actor: Actor
    Label: string }

let private assistant = Actor.Agent("assistant", "1", "author")
let private analyst = Actor.Human "finance"

let private wireStr (s: string) : PropValue = PropValue.Wire(JStr s)

let private turns: Turn list =
  [ { Op = TreeOp.InsertChild(nid "nd-root", metricCard "nd-rev" "Revenue" "£0")
      Actor = assistant
      Label = "Add a Revenue metric" }
    { Op = TreeOp.UpdateProp(nid "nd-rev-v", "Text", wireStr "£2.4M")
      Actor = assistant
      Label = "Fill in revenue" }
    { Op = TreeOp.InsertChild(nid "nd-root", metricCard "nd-margin" "Gross margin" "61%")
      Actor = assistant
      Label = "Add a Gross margin metric" }
    { Op = TreeOp.InsertChild(nid "nd-root", metricCard "nd-opex" "Operating expense" "£1.1M")
      Actor = assistant
      Label = "Add an Operating expense metric" }
    { Op =
        TreeOp.InsertChild(
          nid "nd-root",
          Fuaran.callout
            "nd-runway"
            { Defaults.callout with
                Tone = ToneVariant.Info
                Heading = Some(TextSource.Literal "Runway")
                Body = TextSource.Literal "14 months at the current burn rate." }
        )
      Actor = assistant
      Label = "Add a Runway callout" }
    { Op = TreeOp.UpdateProp(nid "nd-margin-v", "Text", wireStr "58%")
      Actor = analyst
      Label = "Finance corrects the gross margin" }
    { Op =
        TreeOp.InsertChild(
          nid "nd-root",
          Fuaran.callout
            "nd-headline"
            { Defaults.callout with
                Tone = ToneVariant.Brand
                Heading = Some(TextSource.Literal "Headline")
                Body = TextSource.Literal "Revenue up 12% QoQ; margin holding above 55%." }
        )
      Actor = assistant
      Label = "Add a headline callout" }
    { Op = TreeOp.UpdateProp(nid "nd-rev-v", "Text", wireStr "£2.41M")
      Actor = assistant
      Label = "Revise revenue after reconciliation" }
    { Op = TreeOp.UpdateProp(nid "nd-runway", "Body", wireStr "Runway 15 months after the finance-reviewed opex.")
      Actor = analyst
      Label = "Finance signs off the runway note" } ]

let private turnCount = List.length turns

// ─── Reconstruct the verified final dashboard (fold the shipped apply engine) ─

let private finalTree: Node<unit> =
  (initialTree, turns)
  ||> List.fold (fun tree t ->
    match Apply.apply t.Op tree with
    | Ok next -> next
    | Error _ -> tree)

// ─── The genuine hash-chained op-records + Verify.chain ──────────────────────

let private streamId = "notarised-q3"
let private baseTs = 1719792000L // fixed epoch so the exhibit is deterministic

/// Build the real chain: each record's `Hash` is `HashChain.computeHash` over its
/// canonical pre-image (prev-hash + op + seq + timestamp + actor), exactly as a
/// live op-stream sink records it.
let private cleanRecords: OpRecord<unit> list =
  (([], HashChain.genesisPreviousHash), List.indexed turns)
  ||> List.fold (fun (acc, prev) (i, t) ->
    let seq = i + 1
    let ts = DateTimeOffset.FromUnixTimeSeconds(baseTs + int64 i * 3600L)
    let h = HashChain.computeHash prev t.Op seq ts t.Actor None OpResultEnvelope.Success

    let record: OpRecord<unit> =
      { StreamId = streamId
        Sequence = seq
        PreviousHash = prev
        Hash = h
        Op = t.Op
        PromptId = None
        Actor = t.Actor
        Timestamp = ts
        ResultEnvelope = OpResultEnvelope.Success }

    (acc @ [ record ], h))
  |> fst

// The tamper: turn 2 (0-based index 1) is the assistant's revenue figure. Rewrite
// its payload but leave every stored hash untouched – exactly what a quiet
// after-the-fact edit to the record would look like.
let private tamperIndex = 1
let private tamperedOp = TreeOp.UpdateProp(nid "nd-rev-v", "Text", wireStr "£9.9M")

let private tamperedRecords: OpRecord<unit> list =
  cleanRecords
  |> List.mapi (fun i r -> if i = tamperIndex then { r with Op = tamperedOp } else r)

let private headHash = (List.last cleanRecords).Hash

/// The first broken link (1-based sequence) + the verbatim library error, or None
/// on a clean chain – straight from the shipped `Verify.chain`.
let private verifyBreak (records: OpRecord<unit> list) : (int * string) option =
  match Verify.chain records with
  | Ok() -> None
  // Core reports `expected` = the hash recomputed from the record's fields,
  // `actual` = the hash stored on the (tampered) record.
  | Error(VerificationError.HashMismatch(seq, expected, actual)) ->
    Some(seq, sprintf "HashMismatch at op %d – stored %s, recomputed %s" seq (shortHash actual) (shortHash expected))
  | Error(VerificationError.PreviousHashMismatch(seq, _, _)) ->
    Some(seq, sprintf "PreviousHashMismatch at op %d – the chain link is broken" seq)
  | Error(VerificationError.OutOfOrder(expected, actual)) ->
    Some(actual, sprintf "OutOfOrder – expected op %d, got %d" expected actual)

// ─── Node → ops index (the dossier's data) ───────────────────────────────────

let private opTarget (op: TreeOp<unit>) : (string * string) option =
  match op with
  | TreeOp.InsertChild(_, child) -> Some(child.Id, "created")
  | TreeOp.UpdateProp(n, path, PropValue.Wire(JStr v)) -> Some(nodeIdStr n, sprintf "set %s = %s" path v)
  | TreeOp.UpdateProp(n, path, _) -> Some(nodeIdStr n, "set " + path)
  | TreeOp.RemoveNode n -> Some(nodeIdStr n, "removed")
  | TreeOp.UpdateStyle(n, _) -> Some(nodeIdStr n, "restyled")
  | _ -> None

type private DossierRow =
  { Turn: int
    Actor: Actor
    Relation: string
    Hash: string }

let private dossierFor (records: OpRecord<unit> list) (nodeId: string) : DossierRow list =
  List.indexed turns
  |> List.choose (fun (i, t) ->
    match opTarget t.Op with
    | Some(tid, rel) when tid = nodeId ->
      Some
        { Turn = i + 1
          Actor = t.Actor
          Relation = rel
          Hash = (List.item i records).Hash }
    | _ -> None)

// Read the innermost addressed node id from a click on the rendered dashboard.
[<Emit("(function(e){ var el = e.target && e.target.closest ? e.target.closest('[data-fuaran-node-id]') : null; return el ? el.getAttribute('data-fuaran-node-id') : null; })($0)")>]
let private nodeIdAtEvent (ev: obj) : string = jsNative

// ─── View helpers ────────────────────────────────────────────────────────────

let private actorTag (a: Actor) : ReactElement =
  match a with
  | Actor.Human _ -> Html.span [ prop.className "nd-actor nd-actor-human"; prop.text "finance" ]
  | Actor.Agent _ -> Html.span [ prop.className "nd-actor nd-actor-agent"; prop.text "assistant" ]

// ─── The page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private NotarisedView () : ReactElement =
  let tampered, setTampered = React.useState false
  let selected, setSelected = React.useState (None: string option)

  let records = if tampered then tamperedRecords else cleanRecords
  let brk = verifyBreak records

  let stage =
    Html.div
      [ prop.className "nd-stage"
        prop.onClick (fun ev ->
          let id = nodeIdAtEvent ev

          if not (isNull (box id)) then
            setSelected (Some id))
        prop.children [ Render.renderWithSources BindingResolver.empty ignore finalTree ] ]

  // The audit strip – one link per op.
  let strip =
    Html.div
      [ prop.className "nd-strip"
        prop.children
          [ for i in 0 .. turnCount - 1 do
              let seq = i + 1

              let cls =
                match brk with
                | Some(bseq, _) when seq = bseq -> "nd-link nd-link-broken"
                | Some(bseq, _) when seq > bseq -> "nd-link nd-link-unverified"
                | _ -> "nd-link nd-link-ok"

              Html.button
                [ prop.className cls
                  prop.title (sprintf "op %d · %s" seq (shortHash (List.item i records).Hash))
                  prop.text (string seq)
                  prop.onClick (fun _ ->
                    match opTarget (List.item i turns).Op with
                    | Some(tid, _) -> setSelected (Some tid)
                    | None -> ()) ] ] ]

  let banner =
    match brk with
    | None ->
      Html.div
        [ prop.className "nd-banner nd-banner-ok"
          prop.children
            [ Html.span [ prop.className "nd-banner-mark"; prop.text "✓" ]
              Html.span [ prop.text (sprintf "Chain verified · %d links · head %s" turnCount (shortHash headHash)) ] ] ]
    | Some(_, msg) ->
      Html.div
        [ prop.className "nd-banner nd-banner-bad"
          prop.children
            [ Html.span [ prop.className "nd-banner-mark"; prop.text "✗" ]
              Html.div
                [ prop.className "nd-banner-body"
                  prop.children
                    [ Html.span [ prop.text "Doctored replay refused – Verify.chain failed:" ]
                      Html.code [ prop.className "nd-verify-err"; prop.text msg ] ] ] ] ]

  let dossier =
    match selected with
    | None ->
      Html.p
        [ prop.className "nd-dossier-hint"
          prop.text "Click any figure or card in the dashboard to open its provenance dossier." ]
    | Some id ->
      let rows = dossierFor records id

      Html.div
        [ prop.className "nd-dossier"
          prop.children
            [ Html.div
                [ prop.className "nd-dossier-head"
                  prop.children
                    [ Html.span [ prop.className "nd-dossier-node"; prop.text id ]
                      Html.button
                        [ prop.className "nd-dossier-close"
                          prop.text "✕"
                          prop.onClick (fun _ -> setSelected None) ] ] ]
              (if List.isEmpty rows then
                 Html.p
                   [ prop.className "nd-dossier-hint"
                     prop.text "No op directly addresses this node – it was introduced within a parent insert." ]
               else
                 Html.ul
                   [ prop.className "nd-dossier-rows"
                     prop.children
                       [ for r in rows ->
                           let broken =
                             match brk with
                             | Some(bseq, _) -> r.Turn = bseq
                             | None -> false

                           Html.li
                             [ prop.className (if broken then "nd-drow nd-drow-broken" else "nd-drow")
                               prop.children
                                 [ Html.span [ prop.className "nd-drow-turn"; prop.text (sprintf "op %d" r.Turn) ]
                                   actorTag r.Actor
                                   Html.span [ prop.className "nd-drow-rel"; prop.text r.Relation ]
                                   Html.code [ prop.className "nd-drow-hash"; prop.text (shortHash r.Hash) ] ] ] ] ]) ] ]

  let controls =
    Html.div
      [ prop.className "nd-controls"
        prop.children
          [ Html.button
              [ prop.className "nd-btn nd-btn-tamper"
                prop.disabled tampered
                prop.text "Tamper: rewrite the revenue op (£2.4M → £9.9M)"
                prop.onClick (fun _ -> setTampered true) ]
            (if tampered then
               Html.button
                 [ prop.className "nd-btn nd-btn-ghost"
                   prop.text "Restore the record"
                   prop.onClick (fun _ -> setTampered false) ]
             else
               Html.none) ] ]

  let honesty =
    Html.div
      [ prop.className "nd-honesty"
        prop.children
          [ Html.h3 [ prop.text "Audit-ready by construction" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Every turn is a real hash-chained op-record: the hash is computed over the operation, its sequence, timestamp, and author, linked to the previous hash. The dossier and audit strip read that chain directly." ]
                    Html.li
                      [ prop.text
                          "The tamper genuinely rewrites a historical op's payload and re-runs the shipped Verify.chain – the red link and the error text are the library's actual output, not a staged failure. The doctored replay is refused." ]
                    Html.li
                      [ prop.text
                          "Authorship is mixed and accountable: two of the nine turns are human edits, colour-coded throughout. Hash-chaining proves the integrity of the record, not the cryptographic identity of the author – attested identity is a separate concern; this page does not overclaim it." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "The op-stream is the compliance artefact. The same value-not-code property runs through the "
                            Html.a [ prop.href "#/pillar/value"; prop.text "app-is-a-value" ]
                            Html.text " story across the site." ] ] ] ] ] ]

  Html.div
    [ prop.className "nd-page"
      prop.children
        [ Html.h1 [ prop.className "nd-title"; prop.text "The Notarised Dashboard" ]
          Html.p
            [ prop.className "nd-lede"
              prop.text
                "Every element of this AI-built dashboard carries its provenance – which op created it, when, by whom. Try to tamper with the history and watch the chain catch you." ]
          Html.div
            [ prop.className "nd-built-label"
              prop.text "Built by AI in 9 turns · 2 human edits" ]
          Html.div
            [ prop.className "nd-split"
              prop.children
                [ Html.div [ prop.className "nd-stage-col"; prop.children [ stage ] ]
                  Html.div [ prop.className "nd-dossier-col"; prop.children [ dossier ] ] ] ]
          banner
          strip
          controls
          honesty ] ]

let page: ReactElement = NotarisedView()
