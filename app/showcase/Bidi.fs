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
//  It fails in one shape, and that shape is common enough to be worth a slot:
//  an OPAQUE IDENTIFIER that STARTS with characters of no direction of their
//  own (digits, a slash, a plus sign) inside a context of the other direction.
//  An account number, a reference code, a phone number. Those leading
//  characters take the surrounding direction, so in a right-to-left document
//  the letters jump to the front: `0417/2026-B` reads back as `B-0417/2026`,
//  and the reader retypes it wrong. A code that starts with a letter
//  (`INV-2026-0417`) is read correctly: its first strong character carries the
//  digits after it. An earlier version of this page used that code as its
//  failing example and so showed no failure; the two cases are now shown
//  apart, the one the algorithm reads and the one it cannot.
//
//  The failure needs a right-to-left context, and the TREE does not provide
//  one: document direction is the host's (see below). So the page plays the
//  host for the Arabic cards and renders them inside a right-to-left container,
//  as an Arabic host would. The trees declare nothing but the one slot.
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
// them correctly with nothing declared. The references are the exhibit: one
// starts with a letter and is read correctly; the other starts with digits and
// is not.
let private arabicIntro = "تم إصدار الفاتورة التالية إلى حسابكم، والرقم المرجعي هو"
let private arabicTail = "يرجى ذكره عند السداد."

/// The intro the two comparison cards carry: it names no reference, so the one
/// row that holds the reference is the only place the two cards differ.
let private arabicIntroNoRef =
  "تم إصدار الفاتورة التالية إلى حسابكم. يرجى ذكر الرقم المرجعي أدناه عند السداد."

/// Starts with a letter: the algorithm reads it correctly with nothing declared.
let private readableReference = "INV-2026-0417"

/// Starts with digits: in a right-to-left context the letter jumps to the front.
let private reference = "0417/2026-B"
let private englishLine = "The reference for this invoice is"

let private factRow (id: string) (label: string) (value: string) : Node<obj> =
  Fuaran.factSpec
    id
    { Defaults.fact with
        Label = TextSource.Literal label
        Value = TextSource.Literal value }

/// One statement card. `refDirection` is what the card declares about the
/// reference value. The two comparison cards differ in that slot alone (and
/// their headings); the card that shows the algorithm succeeding carries the
/// readable reference, in its intro sentence as well as its row.
let private card
  (idPrefix: string)
  (heading: string)
  (refText: string)
  (refInIntro: bool)
  (refDirection: TextDirection option)
  : Node<obj> =
  let referenceNode =
    let bare = factRow (idPrefix + "-ref") "الرقم المرجعي" refText

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
        [ Fuaran.markdown
            (idPrefix + "-intro")
            (if refInIntro then
               arabicIntro + " " + refText + " — " + arabicTail
             else
               arabicIntroNoRef)
          referenceNode
          factRow (idPrefix + "-amount") "المبلغ" "£1,284.00"
          factRow (idPrefix + "-due") "تاريخ الاستحقاق" "2026-10-01" ] }

let private autoCard =
  card "bd-auto" "Declared nothing — the algorithm reads the text" readableReference true None

let private brokenCard =
  card "bd-broken" "Declared nothing — and the reference starts with digits" reference false None

let private fixedCard =
  card "bd-fixed" "Declared ltr on the reference alone" reference false (Some TextDirection.Ltr)

/// The page acting as an Arabic host: a right-to-left container around a card.
/// Document direction is the host's, never the tree's, so it is set here, on
/// the page, and in no tree.
let private rtlHost (child: ReactElement) : ReactElement =
  Html.div
    [ prop.className "bd-rtl-host"
      prop.custom ("dir", "rtl")
      prop.children [ child ] ]

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
        [ Fuaran.markdown "bd-en-intro" (englishLine + " " + readableReference + ". Please quote it when paying.")
          factRow "bd-en-ref" "Reference" readableReference
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
                  rtlHost (Exhibit.renderStatic brokenCard)
                  Html.p
                    [ prop.className "bd-col-note"
                      prop.text
                        "The reference starts with digits, which have no direction of their own, so they take the right-to-left document's direction and the letter jumps to the front: 0417/2026-B reads as B-0417/2026. A reader copying it out gets it wrong." ] ] ]
          Html.div
            [ prop.className "bd-col"
              prop.children
                [ Html.div
                    [ prop.className "bd-col-tag bd-col-yes"
                      prop.text "direction: ltr, on that value only" ]
                  rtlHost (Exhibit.renderStatic fixedCard)
                  Html.p
                    [ prop.className "bd-col-note"
                      prop.text
                        "One slot on one node: the reference is declared left to right and reads as written. The surrounding text is untouched — it never needed help, and a declaration on the page would have been a claim this document has no business making." ] ] ] ] ]

let private autoPanel: ReactElement =
  Exhibit.panel
    "Most of the time, saying nothing is right"
    "This card declares no direction at all and sits in a right-to-left document. Its reference, INV-2026-0417, starts with a letter, and the bidirectional algorithm carries the digits after it the same way, so it reads correctly with nothing on the wire — and `auto` is both the default and the wire identity, so the shortest document is the one that trusts the algorithm."
    [ rtlHost (Exhibit.renderStatic autoCard); Exhibit.renderStatic englishCard ]

let private failurePanel: ReactElement =
  Exhibit.panel
    "The one shape it cannot read"
    "Both cards below carry the same reference, 0417/2026-B, and sit in the same right-to-left document. Only one of them says which way round the reference goes."
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
    "An invoice in Arabic with a reference code on it, shown as an Arabic host shows it: in a right-to-left document. The bidirectional algorithm gets almost all of it right on its own — and gets one shape wrong every time: a code that starts with digits."
    [ autoPanel; failurePanel; scopePanel ]
    [ Exhibit.Claim.Verified
        "Both cards in the comparison are the shipped renderer's rendering of trees that differ in their heading and in one slot on one node. The reordering you can see is the browser's own bidirectional algorithm, not a string this page reversed."
      Exhibit.Claim.Verified
        "The Arabic cards are rendered inside a right-to-left container, as an Arabic host would render them. That container is this page acting as the host; no tree here declares a document direction."
      Exhibit.Claim.Verified
        "The declaration lands on the reference value alone. Open both wires: apart from the heading and the node ids, the only difference is the direction on one node."
      Exhibit.Claim.Limit
        "The Arabic here is short and written for the exhibit. It is enough to establish a right-to-left paragraph context, which is what the failure needs — it is not a translation of anything, and no claim is made about its register."
      Exhibit.Claim.Limit
        "The language does not switch a document's direction: that is the host's, deliberately. The right-to-left container around the Arabic cards is outside every tree, which is where that decision belongs." ]
