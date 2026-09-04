module Fuaran.Showcase.Invoice

// ============================================================================
//  The Invoice — a document that knows it might be printed
//  (Phases 1124 / 1473). Pillar: "the app is a value".
//
//  Paper is a medium the tree cannot see and cannot be told about. What the
//  document CAN say is which of its own parts are indivisible — and that is a
//  fact only the tree has. A host laying out pages sees boxes; it cannot infer
//  that the totals block is ONE THING that reads wrong when halved, or that a
//  table row split across a sheet boundary has become two half-rows. Only the
//  document knows its own subtrees, and no rendering carries that fact back.
//
//  Four declarations, and every one of them is inert on a screen:
//
//   * `keepTogether` on a box — it and everything under it stay on one page.
//   * `breakBefore` on a box — it starts at the top of a fresh page. There is
//     deliberately NO break-AFTER counterpart: a break after this box is a
//     break before the next one, and a second spelling would buy nothing while
//     being exactly the near-synonym pressure the vocabulary charter forbids.
//   * `keepRowsTogether` on a grid — a row is one thing.
//   * `repeatHeader` on a grid — the column headers repeat on every page the
//     table continues onto.
//
//  And ONE action: `Print`. It is the first payload-free `Action` case, and the
//  emptiness is the ruling. The paged MEDIUM is the host's, so a document may
//  say *print now* and nothing whatever about how — no page size, no margin, no
//  sheet range, no target subtree. `{"$type":"Print"}` is the whole encoding.
//
//  It is not a hatch. It opens a dialogue the reader operates and can cancel,
//  hands the page to no third party, and returns nothing the tree can read — so
//  it discloses strictly less than the clipboard write on the page next door. It
//  is gated all the same, because a host rendering untrusted trees must be able
//  to refuse an unbidden dialogue.
//
//  NAMING NOTHING ABOUT THE MEDIUM is the whole discipline here, and it is why
//  the four declarations are so plain: no page size, no margin, no sheet number,
//  no running header or footer. Those are the host's, and the ratified charter
//  row keeps them out of the language.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── the line items ──────────────────────────────────────────────────────────

let private lineRows: Row list =
  [ [ "code", box "DSN-014"
      "item", box "Discovery workshop, two days"
      "qty", box 2.0
      "rate", box 1450.0
      "net", box 2900.0 ]
    [ "code", box "DSN-021"
      "item", box "Wire-format specification review"
      "qty", box 1.0
      "rate", box 980.0
      "net", box 980.0 ]
    [ "code", box "ENG-104"
      "item", box "Reference host implementation"
      "qty", box 12.0
      "rate", box 620.0
      "net", box 7440.0 ]
    [ "code", box "ENG-118"
      "item", box "Conformance corpus extension"
      "qty", box 4.0
      "rate", box 620.0
      "net", box 2480.0 ]
    [ "code", box "ENG-133"
      "item", box "Renderer parity fixes"
      "qty", box 6.0
      "rate", box 620.0
      "net", box 3720.0 ]
    [ "code", box "OPS-007"
      "item", box "Release engineering"
      "qty", box 3.0
      "rate", box 540.0
      "net", box 1620.0 ]
    [ "code", box "OPS-011"
      "item", box "Publication pipeline"
      "qty", box 2.0
      "rate", box 540.0
      "net", box 1080.0 ]
    [ "code", box "DOC-002"
      "item", box "Guide and component reference"
      "qty", box 5.0
      "rate", box 480.0
      "net", box 2400.0 ]
    [ "code", box "DOC-009"
      "item", box "Migration notes"
      "qty", box 1.0
      "rate", box 480.0
      "net", box 480.0 ]
    [ "code", box "SUP-001"
      "item", box "Support retainer, Q4"
      "qty", box 1.0
      "rate", box 3600.0
      "net", box 3600.0 ] ]
  |> List.map Map.ofList

let private rowText (field: string) (r: Row) : string =
  defaultArg (Map.tryFind field r |> Option.map string) ""

let private rowFloat (field: string) (r: Row) : float =
  match Map.tryFind field r with
  | Some v -> unbox<float> v
  | None -> 0.0

/// The line-item table. Both print declarations sit on it, and both are inert
/// on screen: a row is one thing, and the headers come back at the top of every
/// page the table runs onto.
let private lines: Node<obj> =
  Fuaran.grid
    "inv-lines"
    id
    { Defaults.grid<Row, obj> with
        Source = Binding.Static(Some(Seq.ofList lineRows))
        RowKey = rowText "code"
        KeepRowsTogether = true
        RepeatHeader = true
        Columns =
          [ Column.text "Code" (rowText "code")
            Column.text "Description" (rowText "item")
            Column.numeric "Qty" (rowFloat "qty")
            Column.numeric "Rate" (rowFloat "rate")
            |> Column.withFormat (CellFormat.Currency "GBP")
            Column.numeric "Net" (rowFloat "net")
            |> Column.withFormat (CellFormat.Currency "GBP") ] }

let private net = lineRows |> List.sumBy (rowFloat "net")
let private vat = System.Math.Round(net * 0.2, 2)
let private gross = net + vat

let private amountRow (id: string) (label: string) (value: float) (emphasis: bool) : Node<obj> =
  Fuaran.labelValueRow
    id
    { Defaults.labelValueRow with
        Label = TextSource.Literal label
        Value = Binding.Static(Some value)
        Format = CellFormat.Currency "GBP"
        Emphasis = emphasis }

/// The totals block. `keepTogether` is the clearest instance of the charter's
/// irreducibility test: a host laying out pages sees three label/value rows and
/// cannot infer that they are one statement which reads wrong when halved.
let private totals: Node<obj> =
  Fuaran.box
    "inv-totals"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 6)
      Role = BoxRole.Card
      KeepTogether = true
      BreakBefore = false
      Heading = Some(TextSource.Literal "Total due")
      Children =
        [ amountRow "inv-net" "Net" net false
          amountRow "inv-vat" "VAT at 20%" vat false
          amountRow "inv-gross" "Total including VAT" gross true ] }

/// The terms. `breakBefore` — a fresh page. There is no break-AFTER anywhere in
/// the language, because a break after the totals IS a break before this.
let private terms: Node<obj> =
  Fuaran.box
    "inv-terms"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 8)
      Role = BoxRole.Group
      KeepTogether = false
      BreakBefore = true
      Heading = Some(TextSource.Literal "Terms")
      Children =
        [ Fuaran.markdown
            "inv-terms-body"
            "Payment is due 30 days from the invoice date. Interest accrues at 4% above base on any sum outstanding after that date, calculated daily. Please quote the reference **INV-2026-0417** with your remittance; a payment we cannot attribute is held unallocated and does not stop interest running.\n\nWork is delivered against the statement of work of 4 August 2026. Change requests are quoted separately and do not vary these terms."
          Fuaran.markdown
            "inv-terms-bank"
            "**Remit to** — Sort code 04-00-72, account 41180245, reference INV-2026-0417. For payments from outside the United Kingdom, IBAN GB29 NWBK 6016 1331 9268 19." ] }

let private header: Node<obj> =
  Fuaran.box
    "inv-head"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 8)
      Role = BoxRole.Group
      KeepTogether = true
      BreakBefore = false
      Heading = Some(TextSource.Literal "Invoice INV-2026-0417")
      Children =
        [ Fuaran.factSpec
            "inv-to"
            { Defaults.fact with
                Label = TextSource.Literal "Billed to"
                Value = TextSource.Literal "Riverside Analytics Ltd, 14 Clyde Street, Glasgow" }
          Fuaran.factSpec
            "inv-date"
            { Defaults.fact with
                Label = TextSource.Literal "Invoice date"
                Value = TextSource.Literal "2026-09-01" }
          Fuaran.factSpec
            "inv-due"
            { Defaults.fact with
                Label = TextSource.Literal "Due"
                Value = TextSource.Literal "2026-10-01" } ] }

let private printButton: Node<obj> =
  Fuaran.button
    "inv-print"
    { Defaults.button with
        Label = TextSource.Literal "Print this invoice"
        Variant = ButtonVariant.Primary
        Icon = Some "printer"
        Tooltip =
          Some(
            TextSource.Literal
              "Opens your own print dialogue. The document says print; it says nothing about paper size, margins or which pages — those are yours."
          )
        OnClick = Action.Print }

let private invoice: Node<obj> =
  Fuaran.box
    "inv-root"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 18)
      Role = BoxRole.Dashboard
      KeepTogether = false
      BreakBefore = false
      Heading = None
      Children = [ header; lines; totals; terms ] }

let private wire: string = CJson.encodeNode invoice

// ─── the panels ──────────────────────────────────────────────────────────────

let private documentPanel: ReactElement =
  Exhibit.panel
    "The document"
    "On screen this is an ordinary invoice, and it is byte-for-byte the rendering it would have been before any of these declarations existed. Press the button and the four of them come into effect at once."
    [ Html.div
        [ prop.className "inv-actions"
          prop.children [ Exhibit.renderLive printButton ] ]
      Html.div [ prop.className "inv-sheet"; prop.children [ Exhibit.renderStatic invoice ] ] ]

let private declaration (title: string) (where: string) (body: string) : ReactElement =
  Html.div
    [ prop.className "inv-decl"
      prop.children
        [ Html.div [ prop.className "inv-decl-t"; prop.text title ]
          Html.div [ prop.className "inv-decl-w"; prop.text where ]
          Html.p [ prop.className "inv-decl-b"; prop.text body ] ] ]

let private declarationsPanel: ReactElement =
  Exhibit.panel
    "The four things this document says about being printed"
    "Every one of them is a fact about the TREE, not about paper. That is the test the charter applies: a host laying out pages sees boxes and rows, and cannot recover any of these from the rendering."
    [ Html.div
        [ prop.className "inv-decls"
          prop.children
            [ declaration
                "keepTogether"
                "on the totals block"
                "It and everything under it stay on one page. Three label/value rows are one statement, and a statement split across a sheet boundary reads wrong — but nothing in the layout says so."
              declaration
                "breakBefore"
                "on the terms"
                "The terms start a fresh page. There is deliberately no break-after in the language: a break after the totals is a break before the terms, and a second spelling would be a permanent near-synonym for no gain."
              declaration
                "keepRowsTogether"
                "on the line-item table"
                "A row is one thing. Split across a page, a line item becomes two half-rows that each look like a whole one."
              declaration
                "repeatHeader"
                "on the line-item table"
                "The column headers come back at the top of every page the table continues onto — because on the second sheet, a column of numbers with no heading is a column of numbers." ] ] ]

let private actionPanel: ReactElement =
  let point (text: string) = Html.li [ prop.text text ]

  Exhibit.panel
    "Print, as data"
    ""
    [ Html.ul
        [ prop.className "px-points"
          prop.children
            [ point
                "It is the first payload-free action case, and the emptiness IS the ruling. The paged medium is host chrome, so a document may say print now and nothing about how — no page size, no margin, no sheet range, no target subtree."
              point
                "It is not an escape hatch. It opens a dialogue the reader operates and can cancel, hands the page to no third party, and returns nothing the tree can read — strictly less disclosure than the clipboard write on the page next door."
              point
                "It is gated all the same. A host rendering trees it did not write must be able to refuse an unbidden dialogue, so Print carries its own action descriptor like every other effect." ] ]
      Exhibit.wireDrawer "Show the invoice's wire — four declarations, no medium" wire
      Exhibit.wireDrawer "Show the print button's wire" (CJson.encodeNode printButton) ]

// ─── the page ────────────────────────────────────────────────────────────────

let page: ReactElement =
  Exhibit.shell
    "invoice"
    "The Invoice"
    "A document that says which of its own parts are indivisible — and nothing at all about paper. Print it and watch four declarations take effect that were invisible a moment ago."
    [ documentPanel; declarationsPanel; actionPanel ]
    [ Exhibit.Claim.Verified
        "The print button dispatches the shipped Print action through the renderer's own gate. It opens your browser's print dialogue and this page never learns what you did with it."
      Exhibit.Claim.Verified
        "The four declarations are slots on the tree, visible in the wire. The renderer lowers them to print-media rules, so a screen rendering is byte-for-byte what it was before they existed."
      Exhibit.Claim.Verified
        "Use your browser's print preview to see them bite: the totals block does not split, the terms start a page, and the table's header repeats where it runs on."
      Exhibit.Claim.Limit
        "Whether a preview shows a page break depends on your paper size and margins, which are yours and which the document deliberately says nothing about. On a very large sheet nothing needs to break, and the declarations correctly do nothing."
      Exhibit.Claim.Limit
        "The invoice is invented. The bank details are the published test-vector IBAN and a sort code from the reserved range — they name no account anywhere." ]
