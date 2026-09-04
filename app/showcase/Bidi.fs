module Fuaran.Showcase.Bidi

// ============================================================================
//  Right to Left — a declared direction, and the case for declaring one
//  (Phases 1114 / 1472). Pillar: "intent, not implementation".
//
//  Bidirectional text mostly looks after itself. The Unicode bidirectional
//  algorithm reads a string, finds its first strong character, and lays the
//  run out accordingly — which is what `direction: auto` names, and it is the
//  right answer almost every time. It is the DEFAULT, and it is omitted from
//  the wire, so a document that says nothing about direction is the document it
//  always was.
//
//  It fails in exactly one shape, and that shape is common enough to be worth a
//  slot: an OPAQUE IDENTIFIER inside prose of the other direction. An account
//  number, a reference code, a product SKU, a URL — a run with no strong
//  character of its own. The algorithm has nothing to read, so it inherits the
//  surrounding paragraph's direction, and an invoice reference reads back to
//  front inside an Arabic sentence. `INV-2026-0417` becomes `0417-2026-INV`,
//  and the reader retypes it wrong.
//
//  That is what `style.direction` is for, and the scope is deliberately narrow.
//  It names the direction of ONE VALUE. It does not name a document direction,
//  a locale, or a layout side: those are the host's, and putting them in the
//  vocabulary would mean every tree carrying a claim about a page it does not
//  own.
//
//  This page shows the same card three ways — the algorithm doing its job, the
//  algorithm failing on the one shape it cannot read, and the declaration
//  fixing it — so the slot is justified rather than merely demonstrated.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// The Arabic sentences carry their own strong characters, so `auto` resolves
// them correctly with nothing declared. The reference does not, which is the
// whole exhibit.
let private arabicIntro = "تم إصدار الفاتورة التالية إلى حسابكم، والرقم المرجعي هو"
let private arabicTail = "يرجى ذكره عند السداد."
let private reference = "INV-2026-0417"
let private englishLine = "The reference for this invoice is"

let private factRow (id: string) (label: string) (value: string) : Node<obj> =
  Fuaran.factSpec
    id
    { Defaults.fact with
        Label = TextSource.Literal label
        Value = TextSource.Literal value }

/// One statement card. `refDirection` is what the card declares about the
/// reference value; everything else is identical between the three.
let private card (idPrefix: string) (heading: string) (refDirection: TextDirection option) : Node<obj> =
  let referenceNode =
    let bare = factRow (idPrefix + "-ref") "الرقم المرجعي" reference

    match refDirection with
    | None -> bare
    | Some d -> Node.withDirection d bare

  Fuaran.box
    (idPrefix + "-card")
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 10)
      Role = BoxRole.Card
      KeepTogether = false
      BreakBefore = false
      Heading = Some(TextSource.Literal heading)
      Children =
        [ Fuaran.markdown (idPrefix + "-intro") (arabicIntro + " " + reference + " — " + arabicTail)
          referenceNode
          factRow (idPrefix + "-amount") "المبلغ" "£1,284.00"
          factRow (idPrefix + "-due") "تاريخ الاستحقاق" "2026-10-01" ] }

let private autoCard =
  card "bd-auto" "Declared nothing — the algorithm reads the text" None

let private brokenCard =
  card "bd-broken" "Declared nothing — and here it has nothing to read" None

let private fixedCard =
  card "bd-fixed" "Declared ltr on the reference alone" (Some TextDirection.Ltr)

/// The English card, for the symmetry: the same failure runs the other way. An
/// Arabic name inside an English sentence resolves fine, because it HAS strong
/// characters; an opaque code inside an Arabic sentence does not.
let private englishCard: Node<obj> =
  Fuaran.box
    "bd-en-card"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 10)
      Role = BoxRole.Card
      KeepTogether = false
      BreakBefore = false
      Heading = Some(TextSource.Literal "The same page in English, declaring nothing")
      Children =
        [ Fuaran.markdown "bd-en-intro" (englishLine + " " + reference + ". Please quote it when paying.")
          factRow "bd-en-ref" "Reference" reference
          factRow "bd-en-amount" "Amount" "£1,284.00"
          factRow "bd-en-due" "Due" "2026-10-01" ] }

let private wireAuto: string = CJson.encodeNode brokenCard
let private wireFixed: string = CJson.encodeNode fixedCard

// ─── the panels ──────────────────────────────────────────────────────────────

let private compare: ReactElement =
  Html.div
    [ prop.className "bd-pair"
      prop.children
        [ Html.div
            [ prop.className "bd-col"
              prop.children
                [ Html.div [ prop.className "bd-col-tag bd-col-no"; prop.text "no declaration" ]
                  Exhibit.renderStatic brokenCard
                  Html.p
                    [ prop.className "bd-col-note"
                      prop.text
                        "The reference has no strong character of its own, so it inherits the paragraph around it. The digits and the letters come back in the wrong order, and a reader copying it out gets it wrong." ] ] ]
          Html.div
            [ prop.className "bd-col"
              prop.children
                [ Html.div
                    [ prop.className "bd-col-tag bd-col-yes"
                      prop.text "direction: ltr, on that value only" ]
                  Exhibit.renderStatic fixedCard
                  Html.p
                    [ prop.className "bd-col-note"
                      prop.text
                        "One slot on one node. The surrounding text is untouched — it never needed help, and a declaration on the page would have been a claim this document has no business making." ] ] ] ] ]

let private autoPanel: ReactElement =
  Exhibit.panel
    "Most of the time, saying nothing is right"
    "This card declares no direction at all. Every run in it carries strong characters of its own, so the bidirectional algorithm resolves it correctly with nothing on the wire — and `auto` is both the default and the wire identity, so the shortest document is the one that trusts the algorithm."
    [ Exhibit.renderStatic autoCard; Exhibit.renderStatic englishCard ]

let private failurePanel: ReactElement =
  Exhibit.panel
    "The one shape it cannot read"
    "Both cards below carry the same reference. Only one of them says which way round it goes."
    [ compare ]

let private scopePanel: ReactElement =
  let point (text: string) = Html.li [ prop.text text ]

  Exhibit.panel
    "What the slot deliberately does NOT say"
    ""
    [ Html.ul
        [ prop.className "px-points"
          prop.children
            [ point
                "It is not a document direction. Nothing here names which way the page runs — that belongs to the host that owns the page, and a tree carrying it would be asserting something about a document it was pasted into."
              point
                "It is not a locale. A locale decides how a number, a date or a currency is written, and that is a Format binding on the value. Direction is a layout fact about one run of text, and the two travel separately because they are separately true."
              point
                "It is not a layout side. Which edge a panel sits against is the renderer's, decided from the document direction it is rendering under."
              point
                "Auto is the wire identity, so this whole page is byte-identical to what it would have been before the slot existed — except for the one card that needed it." ] ]
      Exhibit.wireDrawer "The card that declares nothing" wireAuto
      Exhibit.wireDrawer "The card that declares ltr — one key, on one node" wireFixed ]

// ─── the page ────────────────────────────────────────────────────────────────

let page: ReactElement =
  Exhibit.shell
    "bidi"
    "Right to Left"
    "An invoice in Arabic with an English reference code on it. The bidirectional algorithm gets almost all of this right on its own — and gets exactly one thing wrong, every time, for a reason no amount of care in the text can fix."
    [ autoPanel; failurePanel; scopePanel ]
    [ Exhibit.Claim.Verified
        "Both cards in the comparison are the shipped renderer's rendering of trees that differ in one slot on one node. The reordering you can see is the browser's own bidirectional algorithm, not a string this page reversed."
      Exhibit.Claim.Verified
        "The declaration lands on the reference value alone. Open both wires: the only difference is a style block on one node."
      Exhibit.Claim.Limit
        "The Arabic here is short and written for the exhibit. It is enough to establish a right-to-left paragraph context, which is what the failure needs — it is not a translation of anything, and no claim is made about its register."
      Exhibit.Claim.Limit
        "This page does not switch the document's own direction. That is the host's, deliberately, and a page that flipped itself would be demonstrating a capability the language does not have and should not." ]
