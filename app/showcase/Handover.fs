module Fuaran.Showcase.Handover

// ============================================================================
//  The Handover — copy what you are LOOKING at (Phase 1126).
//  Pillar: "intent, not implementation".
//
//  A copy button whose payload is a literal the author typed is a copy button
//  that copies yesterday's value. The clipboard action's payload is a
//  `TextSource`, so what a reader copies may be a bound value or a computed
//  reference rather than only a fixed string — and a bound payload RESOLVES AT
//  DISPATCH TIME, through the same binding resolver the surrounding tree renders
//  through. So the copied text is what the reader was looking at when they
//  asked, not what the document held when it was decoded.
//
//  THE CASE WAS WIDENED, NOT JOINED. There is no `WriteToClipboardBound` sibling
//  beside the literal one: two cases for one intent is the permanent
//  near-synonym pair the vocabulary charter exists to forbid, and a source break
//  the compiler names once is cheaper than a vocabulary that stays ambiguous
//  forever. The wire did not move for a literal payload — a literal is
//  canonically the bare JSON string, so a pre-existing document is byte-identical
//  — and it is the construction sites that break, at compile time.
//
//  THERE IS DELIBERATELY NO CLIPBOARD READ. A tree that could read the clipboard
//  without a paste gesture is a keylogger-adjacent capability. Paste is
//  user-initiated by construction, and that is the boundary — which is why the
//  file-upload kind can accept a paste and no action can ask for one.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── state ───────────────────────────────────────────────────────────────────

let private kIncident = "handover.incident"
let private kSeverity = "handover.severity"
let private kOwner = "handover.owner"
let private kNote = "handover.note"

let private watchedKeys = Set.ofList [ kIncident; kSeverity; kOwner; kNote ]

let private option (value: string) : SelectOption = { Value = value; Label = value }

/// The shift-handover card. Every field is editable and every field is bound,
/// which is what makes the copy buttons beside them worth anything.
let private card: Node<obj> =
  Fuaran.form
    "hv-form"
    { Defaults.form with
        SubmitLabel = TextSource.Literal "Close the handover"
        OnSubmit = Action.Chain []
        Fields =
          [ { Id = "hv-incident"
              Label = TextSource.Literal "Incident reference"
              Kind = FormFieldKind.Text(Some(Binding.State(kIncident, Some "INC-4471")), None)
              Required = true
              Help = Some(TextSource.Literal "Edit it, then press the copy button beside it.")
              Rule = None }
            { Id = "hv-severity"
              Label = TextSource.Literal "Severity"
              Kind =
                FormFieldKind.Choice(
                  Binding.Static(Some [ option "SEV-1"; option "SEV-2"; option "SEV-3" ]),
                  Some(Binding.State(kSeverity, Some "SEV-2")),
                  None
                )
              Required = true
              Help = None
              Rule = None }
            { Id = "hv-owner"
              Label = TextSource.Literal "Handing over to"
              Kind = FormFieldKind.Text(Some(Binding.State(kOwner, Some "Mhairi (late shift)")), None)
              Required = true
              Help = None
              Rule = None }
            { Id = "hv-note"
              Label = TextSource.Literal "What the next shift needs to know"
              Kind =
                FormFieldKind.TextArea(
                  Some(
                    Binding.State(
                      kNote,
                      Some
                        "Replica lag peaked at 41s at 03:12 and is back under 2s. Root cause is the nightly reindex overlapping the batch window. Do not restart the follower — it is catching up cleanly."
                    )
                  ),
                  None,
                  4
                )
              Required = false
              Help = None
              Rule = None } ] }

/// A copy button whose payload is BOUND. It carries no text of its own: what it
/// puts on the clipboard is whatever the key holds at the moment it is pressed.
let private copyBound (id: string) (label: string) (key: string) (hint: string) : Node<obj> =
  Fuaran.button
    id
    { Defaults.button with
        Label = TextSource.Literal label
        Variant = ButtonVariant.Secondary
        Icon = Some "copy"
        Tooltip = Some(TextSource.Literal hint)
        OnClick = Action.WriteToClipboard(TextSource.Bound(Binding.State(key, Some ""))) }

/// A copy button whose payload is a LITERAL — the shape that existed before this
/// release. It is here for the comparison: press it after editing the reference
/// above and it hands you the value the document was written with.
let private copyLiteral: Node<obj> =
  Fuaran.button
    "hv-copy-literal"
    { Defaults.button with
        Label = TextSource.Literal "Copy \"INC-4471\" (literal)"
        Variant = ButtonVariant.Tertiary
        Icon = Some "copy"
        Tooltip =
          Some(
            TextSource.Literal
              "The payload the author typed. Edit the reference above and press this: you get the old value, because that is what a literal is."
          )
        OnClick = Action.WriteToClipboard(TextSource.Literal "INC-4471") }

let private copyRow: Node<obj> =
  Fuaran.box
    "hv-copies"
    { Layout = LayoutMode.Flex(Orientation.Horizontal, true, Some 10)
      Role = BoxRole.Group
      KeepTogether = false
      BreakBefore = false
      Heading = None
      Children =
        [ copyBound
            "hv-copy-ref"
            "Copy the reference"
            kIncident
            "Resolves when you press it, through the same resolver the field beside it renders through."
          copyBound "hv-copy-owner" "Copy the owner" kOwner "The same mechanism over a different key."
          copyBound
            "hv-copy-note"
            "Copy the note"
            kNote
            "A whole paragraph, taken from the field as it stands rather than as it was written."
          copyLiteral ] }

let private wireBound: string =
  CJson.encodeNode (copyBound "hv-copy-ref" "Copy the reference" kIncident "…")

let private wireLiteral: string = CJson.encodeNode copyLiteral

// ─── the page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private HandoverView () : ReactElement =
  StateStore.useStateKeys watchedKeys |> ignore

  React.useEffectOnce (fun () ->
    StateStore.set kIncident (box "INC-4471")
    StateStore.set kSeverity (box "SEV-2")
    StateStore.set kOwner (box "Mhairi (late shift)")

    StateStore.set
      kNote
      (box
        "Replica lag peaked at 41s at 03:12 and is back under 2s. Root cause is the nightly reindex overlapping the batch window. Do not restart the follower — it is catching up cleanly."))

  let read (key: string) =
    match StateStore.get key with
    | Some v -> unbox<string> v
    | None -> ""

  let cardPanel =
    Exhibit.panel
      "Edit a field, then copy it"
      "Change the incident reference. Press the first copy button and paste somewhere: you get what you just typed. Press the last one and you get what the author typed, because that one carries a literal — which is the whole difference this release made."
      [ Exhibit.renderLive card
        Html.div [ prop.className "hv-copies"; prop.children [ Exhibit.renderLive copyRow ] ]
        Html.div
          [ prop.className "hv-live"
            prop.children
              [ Html.span [ prop.className "hv-live-k"; prop.text "the bound button will copy" ]
                Html.code [ prop.className "hv-live-v"; prop.text (read kIncident) ]
                Html.span [ prop.className "hv-live-k"; prop.text "the literal button will copy" ]
                Html.code [ prop.className "hv-live-v"; prop.text "INC-4471" ] ] ] ]

  let point (text: string) = Html.li [ prop.text text ]

  let designPanel =
    Exhibit.panel
      "One case, widened — and one capability that does not exist"
      ""
      [ Html.ul
          [ prop.className "px-points"
            prop.children
              [ point
                  "The payload is a TextSource, so a copy button can carry a literal, a bound value, or a translated string — one intent, one case. There is no second case beside it, because two spellings of one intent is the permanent near-synonym pair the vocabulary charter exists to forbid."
                point
                  "The wire did not move for a literal payload. A literal is canonically the bare JSON string, so a document written before this release encodes and decodes exactly as it did. What broke was construction sites, at compile time, once — which is cheaper than a vocabulary that stays ambiguous forever."
                point
                  "A bound payload resolves at DISPATCH time, never at decode. That is the whole feature: the reader gets what they were looking at, not what the document held when it arrived."
                point
                  "There is deliberately no clipboard READ. A tree that could read your clipboard without a paste gesture is a keylogger-adjacent capability. Paste is user-initiated by construction, which is why the upload kind can accept one and no action can ask for one." ] ]
        Exhibit.wireDrawer "The bound button's wire" wireBound
        Exhibit.wireDrawer "The literal button's wire — a bare string, as it always was" wireLiteral ]

  Exhibit.shell
    "handover"
    "The Handover"
    "A shift handover card with copy buttons beside it. Edit a field and copy it: what lands on your clipboard is what you are looking at, because the payload is a binding that resolves when you press it rather than a string the author typed."
    [ cardPanel; designPanel ]
    [ Exhibit.Claim.Verified
        "The copy buttons dispatch the shipped clipboard action through the renderer's own gate, into your browser's real clipboard. Paste somewhere and check."
      Exhibit.Claim.Verified
        "The read-out under the buttons resolves the same State key the bound button's payload names. It is a preview of the resolution, computed the same way."
      Exhibit.Claim.Verified
        "The literal button is not a straw man — it is the shape every copy button had before this release, rendered by the same renderer, and it hands you the stale value for exactly the reason the widening happened."
      Exhibit.Claim.Limit
        "Nothing here is transmitted or stored. The card lives in this tab and the clipboard is yours; the page never reads it back, and could not."
      Exhibit.Claim.Limit
        "Your browser may ask permission the first time, or refuse in a context that has not been interacted with. That is the platform's gate rather than the language's, and this page does not try to route around it." ]

let page: ReactElement = HandoverView()
