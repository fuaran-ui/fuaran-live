module Fuaran.Showcase.Situation

// ============================================================================
//  The Situation Room — a dense dashboard that explains itself (Phase 1112).
//  Pillar: "the machine can see the UI".
//
//  A tooltip is the smallest possible capability and the easiest one to get
//  wrong. Two rules are the whole design, and this page is built to make both
//  visible rather than to enumerate a prop:
//
//   * A HINT DESCRIBES; IT DOES NOT NAME. `tooltip` is a node-level trait that
//     reaches assistive technology through `aria-describedby` — supplementary
//     information ABOUT a thing that already has a name. It is not a substitute
//     for the name. On a control whose own text is empty — an icon-only button
//     is the case this was built for — a tooltip alone leaves the element with
//     a description and NO NAME AT ALL, and FUARAN109 says so at validate time
//     rather than letting it ship.
//   * AN EMPTY HINT IS A DEFECT, NOT A NO-OP. A declared tooltip that hints
//     nothing produces no hint element in any renderer, and FUARAN118 reports
//     it rather than leaving an author with markup that silently did not
//     appear.
//
//  The composition is an operations board, because that is where hints
//  genuinely earn their place: eleven numbers whose definitions are the whole
//  argument between two teams, and none of which fits in a label. The read-back
//  pane counts the `aria-describedby` wiring the renderer actually emitted into
//  this page's DOM — a measured fact, not a caption.
// ============================================================================

open Fable.Core
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

/// Count the elements inside the rendered board that carry a description
/// wiring, and how many distinct hint elements they point at. Read from the
/// live DOM after mount — the renderer's own emission, not this page's record
/// of what it asked for.
[<Emit("(function(){ var root=document.querySelector('.sr-board'); if(!root) return [0,0]; var described=root.querySelectorAll('[aria-describedby]'); var ids={}; described.forEach(function(el){ (el.getAttribute('aria-describedby')||'').split(/\\s+/).forEach(function(id){ if(id) ids[id]=1; }); }); return [described.length, Object.keys(ids).length]; })()")>]
let private readDescribed () : int[] = jsNative

// ─── the board ───────────────────────────────────────────────────────────────

let private hint (text: string) (node: Node<'msg>) : Node<'msg> =
  Node.withTooltip (TextSource.Literal text) node

let private tile (id: string) (label: string) (value: float) (fmt: CellFormat) (tone: ToneVariant) (sub: string) =
  Fuaran.metric
    id
    { Defaults.metric with
        Label = TextSource.Literal label
        Value = Binding.Static(Some value)
        Format = fmt
        Tone = tone
        Subtext = Some(TextSource.Literal sub) }

/// Eleven tiles, each with a definition that does not fit in its label. The
/// hints are the point: a dashboard whose numbers cannot be interrogated is a
/// dashboard two teams will disagree about forever.
let private board: Node<obj> =
  Fuaran.box
    "sr-root"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 14)
      Role = BoxRole.Dashboard
      KeepTogether = false
      BreakBefore = false
      Heading = Some(TextSource.Literal "Payments — last 24 hours")
      Children =
        [ Fuaran.gridLayout
            "sr-grid"
            { Defaults.gridLayout<obj> with
                Cols = 4
                Children =
                  [ tile
                      "sr-auth"
                      "Authorisation rate"
                      0.9412
                      (CellFormat.Percent(Some 2))
                      ToneVariant.Success
                      "vs 94.9% baseline"
                    |> hint
                      "Authorised divided by attempted, counting a retry of the same order once. Excludes 3-D Secure abandonments, which land in the challenge funnel below rather than here."
                    tile
                      "sr-challenge"
                      "Challenge rate"
                      0.083
                      (CellFormat.Percent(Some 1))
                      ToneVariant.Default
                      "step-up sent"
                    |> hint
                      "Share of attempts where the issuer asked for a step-up. A rise here with a flat authorisation rate is the issuer being cautious; a rise with a falling authorisation rate is the challenge failing."
                    tile "sr-capture" "Capture latency (ms)" 412.0 (CellFormat.Number(Some 0)) ToneVariant.Default "p95"
                    |> hint
                      "Ninety-fifth percentile from authorisation to captured funds, measured at our edge. It does not include the acquirer's own settlement window, which is a next-day figure and belongs on the finance board."
                    tile
                      "sr-decline"
                      "Hard declines"
                      1284.0
                      (CellFormat.Number(Some 0))
                      ToneVariant.Warning
                      "issuer-final"
                    |> hint
                      "Declines the issuer marked final, so a retry on the same instrument cannot succeed. Soft declines are retried automatically and never appear in this count."
                    tile
                      "sr-fraud"
                      "Fraud rate"
                      0.0011
                      (CellFormat.Percent(Some 3))
                      ToneVariant.Critical
                      "by value, 30-day trailing"
                    |> hint
                      "Confirmed fraudulent value divided by processed value over a trailing 30 days — not 24 hours. A one-day fraud rate is mostly reporting lag, so the tile deliberately disagrees with the board's own window."
                    tile
                      "sr-chargeback"
                      "Chargebacks"
                      37.0
                      (CellFormat.Number(Some 0))
                      ToneVariant.Warning
                      "received today"
                    |> hint
                      "Cases opened by an issuer today, whatever transaction date they name. Roughly two thirds concern transactions more than a fortnight old, so this is not a signal about today's traffic."
                    tile "sr-refund" "Refunds" 18420.0 (CellFormat.Currency "GBP") ToneVariant.Default "value, today"
                    |> hint
                      "Merchant-initiated returns only. A chargeback that we accept without contest is settled through the dispute path and is counted there, never twice."
                    tile
                      "sr-retry"
                      "Retry recovery"
                      0.212
                      (CellFormat.Percent(Some 1))
                      ToneVariant.Success
                      "of soft declines"
                    |> hint
                      "Share of soft declines that authorised on a later attempt within the same order. The denominator is soft declines, not all declines — recovery against every decline would flatter the number by about a third."
                    tile
                      "sr-queue"
                      "Settlement queue"
                      906.0
                      (CellFormat.Number(Some 0))
                      ToneVariant.Default
                      "awaiting batch"
                    |> hint
                      "Authorised transactions not yet in a settlement batch. It empties on the batch boundary, so a large value shortly before the cut is normal and a large value shortly after it is not."
                    tile
                      "sr-tokens"
                      "Tokenised share"
                      0.774
                      (CellFormat.Percent(Some 1))
                      ToneVariant.Brand
                      "of attempts"
                    |> hint
                      "Attempts using a network token rather than a stored card number. Higher is better for authorisation and for what a breach would expose, and it moves slowly — a jump means a routing change, not customer behaviour."
                    tile
                      "sr-cost"
                      "Cost per authorisation"
                      0.0612
                      (CellFormat.Currency "GBP")
                      ToneVariant.Default
                      "blended"
                    |> hint
                      "Scheme fees, acquirer margin and our own processing, divided by authorisations. Blended across regions, so it moves when the traffic mix moves even if no price has changed." ] } ] }

/// The action strip. Two of these prove the two rules the module header names:
/// a labelled button whose hint SUPPLEMENTS its name, and an icon-only button
/// whose name has to come from somewhere else because a hint cannot be one.
let private actions: Node<obj> =
  Fuaran.box
    "sr-actions"
    { Layout = LayoutMode.Flex(Orientation.Horizontal, true, Some 10)
      Role = BoxRole.Group
      KeepTogether = false
      BreakBefore = false
      Heading = None
      Children =
        [ Fuaran.button
            "sr-freeze"
            { Defaults.button with
                Label = TextSource.Literal "Freeze routing"
                Variant = ButtonVariant.Destructive
                Tooltip =
                  Some(
                    TextSource.Literal
                      "Pins every acquirer weight at its current value until someone unfreezes it. In-flight authorisations are unaffected; the next routing decision is not."
                  )
                OnClick = Action.Chain [] }
          Fuaran.button
            "sr-drill"
            { Defaults.button with
                Label = TextSource.Literal "Open decline breakdown"
                Variant = ButtonVariant.Secondary
                Tooltip =
                  Some(TextSource.Literal "Groups today's hard declines by issuer response code, largest first.")
                OnClick = Action.Chain [] }
          // The icon-only case. Its own text is empty, so the hint CANNOT be its
          // name — the accessible name is declared separately, and without that
          // declaration FUARAN109 refuses the node at pre-emit rather than
          // shipping a control announced as "button" with a description.
          Fuaran.button
            "sr-refresh"
            { Defaults.button with
                Label = TextSource.Literal ""
                Icon = Some "refresh"
                Variant = ButtonVariant.Tertiary
                Tooltip = Some(TextSource.Literal "Re-reads the board from the last completed minute, not from now.")
                OnClick = Action.Chain [] }
          |> Node.withAccessibility (
            Some
              { Defaults.Accessibility.empty with
                  Label = Some(Binding.Static(Some "Refresh the board")) }
          ) ] }

let private wire: string = CJson.encodeNode board

// ─── the page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private SituationView () : ReactElement =
  let counts, setCounts = React.useState ([| 0; 0 |]: int[])

  // Read AFTER the board has mounted: the numbers are the renderer's emission
  // into this document, not this page's account of what it asked for.
  React.useEffectOnce (fun () -> setCounts (readDescribed ()))

  let describedCount = if counts.Length > 0 then counts[0] else 0
  let hintCount = if counts.Length > 1 then counts[1] else 0

  let boardPanel =
    Exhibit.panel
      "Eleven numbers, eleven arguments"
      "Hover or focus any tile. Every definition here is one two teams have disagreed about, and none of them fits in a label — which is the whole case for a hint being a slot on the node rather than prose somewhere else."
      [ Html.div [ prop.className "sr-board"; prop.children [ Exhibit.renderStatic board ] ] ]

  let actionPanel =
    Exhibit.panel
      "A hint describes; it does not name"
      "The first two buttons carry a hint that supplements a name they already have. The third has no text of its own — so its name is declared separately, and the language refuses it at pre-emit if it is not."
      [ Exhibit.renderStatic actions ]

  let readbackPanel =
    Exhibit.panel
      "What the renderer actually emitted"
      "Counted from this page's live DOM after mount, not from the tree above."
      [ Html.div
          [ prop.className "sr-readback"
            prop.children
              [ Html.div
                  [ prop.className "sr-stat"
                    prop.children
                      [ Html.span [ prop.className "sr-stat-n"; prop.text (string describedCount) ]
                        Html.span [ prop.className "sr-stat-l"; prop.text "elements carrying aria-describedby" ] ] ]
                Html.div
                  [ prop.className "sr-stat"
                    prop.children
                      [ Html.span [ prop.className "sr-stat-n"; prop.text (string hintCount) ]
                        Html.span [ prop.className "sr-stat-l"; prop.text "distinct hint elements they point at" ] ] ] ] ]
        Exhibit.wireDrawer "Show the board's wire — every hint is a slot on its node" wire ]

  Exhibit.shell
    "situation"
    "The Situation Room"
    "A payments board dense enough that every number needs a definition, and short enough that none of them can carry one. The hints are on the nodes, on the wire, and in the accessibility tree — the same fact three ways, because it is one fact."
    [ boardPanel; actionPanel; readbackPanel ]
    [ Exhibit.Claim.Verified
        "The hints are the shipped renderer's own tooltip rendering of the node-level trait, revealed by its own hover, focus and long-press affordance. No tooltip library is loaded on this page."
      Exhibit.Claim.Verified
        "The read-back counts are queried from this page's live DOM after mount. They are what the renderer emitted, so a regression that stopped wiring descriptions would show here as a number falling rather than as a page that merely looked the same."
      Exhibit.Claim.Verified
        "The icon-only button declares its accessible name separately from its hint. That is not decoration: a control whose only text is a description reaches a screen reader with no name, and the language refuses that shape at pre-emit."
      Exhibit.Claim.Limit
        "The numbers are invented. The definitions are the exhibit — an operations board is simply the honest place to find eleven of them that genuinely do not fit in a label."
      Exhibit.Claim.Limit
        "The buttons do nothing. Freezing a routing table needs a host with a routing table, and this page has no server of any kind; a button that pretended to act would be the one dishonest thing on a page about saying what you mean." ]

let page: ReactElement = SituationView()
