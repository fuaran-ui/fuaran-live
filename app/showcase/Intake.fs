module Fuaran.Showcase.Intake

// ============================================================================
//  The Intake Form — the four controls a real form could not do without
//  (Phases 1113 / 1121 / 1130). Pillar: "intent, not implementation".
//
//  An ordinary conference-talk submission. Nothing about it is unusual, and
//  that is the exhibit: until this release a model asked to emit this form had
//  to reach for a plain text field four times and hope the host guessed.
//
//   * COMBOBOX (1113) — a closed list you can TYPE into. A `Choice` makes you
//     scroll ninety countries; a `Text` field admits "Untied Kingdom". The
//     combobox is neither: `allowFreeText` says whether a value outside the
//     list is admissible, and the two answers are genuinely different fields —
//     which is why it is one slot rather than two kinds.
//   * TOKENS (1121) — several values in one control, each removable. Its
//     `allowFreeText` DEFAULTS TO TRUE, the opposite polarity to the combobox,
//     and the decoder refuses a tokens field that declares it false with no
//     suggestion source: such a field could admit no token by any gesture, so
//     it is a control that cannot be used, and the language says so rather than
//     rendering it.
//   * RATING (1130) — a bounded ordinal. `max` must be at least one, refused
//     at decode: a scale with no positions cannot be rendered or announced.
//   * COLOR (1130) — a colour, as a value rather than as CSS. It is a form
//     field, not a style: what it produces is data the submitter chose.
//
//  Every field is bound to a State key and every key is readable, so the pane
//  beside the form shows the ACTUAL resolved values as you edit — this is the
//  submission, not a picture of one.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── state keys ──────────────────────────────────────────────────────────────

let private kTitle = "intake.title"
let private kTrack = "intake.track"
let private kRoom = "intake.room"
let private kTags = "intake.tags"
let private kLevel = "intake.level"
let private kAccent = "intake.accent"

let private watchedKeys =
  Set.ofList [ kTitle; kTrack; kRoom; kTags; kLevel; kAccent ]

let private option (value: string) (label: string) : SelectOption = { Value = value; Label = label }

// ─── the form ────────────────────────────────────────────────────────────────

/// Ninety-odd rooms across four buildings — the case a `Choice` cannot serve
/// and a `Text` field serves badly. `allowFreeText = false`: a room that is not
/// on this list does not exist, so a typed value that matches nothing is not a
/// submission the venue can honour.
let private rooms: SelectOption list =
  [ option "kelvin-1" "Kelvin Hall — Room 1"
    option "kelvin-2" "Kelvin Hall — Room 2"
    option "kelvin-aud" "Kelvin Hall — Auditorium"
    option "mitchell-a" "Mitchell Building — A"
    option "mitchell-b" "Mitchell Building — B"
    option "mitchell-loft" "Mitchell Building — Loft"
    option "riverside-1" "Riverside — Studio 1"
    option "riverside-2" "Riverside — Studio 2"
    option "riverside-hall" "Riverside — Great Hall"
    option "annexe-n" "The Annexe — North"
    option "annexe-s" "The Annexe — South" ]

/// The opposite answer on the same slot. A track is a shortlist the programme
/// committee publishes, and a proposal that fits none of them is a real thing a
/// submitter needs to be able to say — so free text is admitted here.
let private tracks: SelectOption list =
  [ option "languages" "Languages & type systems"
    option "tooling" "Developer tooling"
    option "systems" "Distributed systems"
    option "accessibility" "Accessibility"
    option "practice" "Practice & craft" ]

let private topics: SelectOption list =
  [ option "fsharp" "F#"
    option "wire-formats" "Wire formats"
    option "a11y" "Accessibility"
    option "compilers" "Compilers"
    option "testing" "Testing"
    option "ui" "User interfaces"
    option "provenance" "Provenance" ]

let private field (id: string) (label: string) (help: string) (kind: FormFieldKind<obj>) : FormField<obj> =
  { Id = id
    Label = TextSource.Literal label
    Kind = kind
    Required = false
    Help = (if help = "" then None else Some(TextSource.Literal help))
    Rule = None }

let private submission: Node<obj> =
  Fuaran.form
    "intake-form"
    { Defaults.form with
        SubmitLabel = TextSource.Literal "Submit the proposal"
        OnSubmit = Action.Chain []
        Fields =
          [ field
              "intake-title"
              "Talk title"
              ""
              (FormFieldKind.Text(Some(Binding.State(kTitle, Some "Wire formats are a design decision")), None))
            field
              "intake-track"
              "Track"
              "A closed shortlist you can type into — and one that admits a value outside it, because a proposal that fits no published track is a real thing to be able to say."
              (FormFieldKind.Combobox(true, None, Binding.Static(Some tracks), Some(Binding.State(kTrack, Some ""))))
            field
              "intake-room"
              "Preferred room"
              "The same control with the opposite answer: a room not on this list does not exist, so free text is refused."
              (FormFieldKind.Combobox(false, None, Binding.Static(Some rooms), Some(Binding.State(kRoom, Some ""))))
            field
              "intake-tags"
              "Topics"
              "Several values in one control, each removable. Suggestions are offered; anything else you type is admitted, which is this field's default."
              (FormFieldKind.Tokens(
                true,
                None,
                Some(Binding.Static(Some topics)),
                Some(Binding.State(kTags, Some [ "wire-formats"; "a11y" ]))
              ))
            field
              "intake-level"
              "How prepared is the material?"
              "A bounded ordinal — five positions, halves admitted. The scale is on the wire, so a host cannot quietly render four."
              (FormFieldKind.Rating(true, 5, None, Some(Binding.State(kLevel, Some 3.5))))
            field
              "intake-accent"
              "Slide accent colour"
              "A colour as a VALUE, not as styling. What it produces is data the submitter chose, and it travels in the submission like every other answer."
              (FormFieldKind.Color(None, Some(Binding.State(kAccent, Some "#3f6f5f")))) ] }

let private wire: string = CJson.encodeNode submission

// ─── the read-back ───────────────────────────────────────────────────────────

let private readString (key: string) (fallback: string) : string =
  match StateStore.get key with
  | Some v -> unbox<string> v
  | None -> fallback

let private readNumber (key: string) (fallback: float) : float =
  match StateStore.get key with
  | Some v -> unbox<float> v
  | None -> fallback

let private readList (key: string) : string list =
  match StateStore.get key with
  | Some v -> unbox<string list> v
  | None -> []

let private seed () : unit =
  StateStore.set kTitle (box "Wire formats are a design decision")
  StateStore.set kTrack (box "")
  StateStore.set kRoom (box "")
  StateStore.set kTags (box [ "wire-formats"; "a11y" ])
  StateStore.set kLevel (box 3.5)
  StateStore.set kAccent (box "#3f6f5f")

// ─── the page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private IntakeView () : ReactElement =
  // The subscription is what re-renders the page as the form is edited; the
  // value it returns is not needed.
  StateStore.useStateKeys watchedKeys |> ignore
  React.useEffectOnce (fun () -> seed ())

  let row (label: string) (value: string) =
    Html.div
      [ prop.className "ik-row"
        prop.children
          [ Html.span [ prop.className "ik-row-k"; prop.text label ]
            Html.span [ prop.className "ik-row-v"; prop.text (if value = "" then "—" else value) ] ] ]

  let tags = readList kTags
  let accent = readString kAccent "#3f6f5f"

  let readback =
    Html.div
      [ prop.className "ik-readback"
        prop.children
          [ row "title" (readString kTitle "")
            row "track" (readString kTrack "")
            row "room" (readString kRoom "")
            row "topics" (String.concat ", " tags)
            row "readiness" (string (readNumber kLevel 0.0) + " of 5")
            Html.div
              [ prop.className "ik-row"
                prop.children
                  [ Html.span [ prop.className "ik-row-k"; prop.text "accent" ]
                    Html.span
                      [ prop.className "ik-row-v ik-row-colour"
                        prop.children
                          [ Html.span [ prop.className "ik-swatch"; prop.style [ style.backgroundColor accent ] ]
                            Html.span [ prop.text accent ] ] ] ] ] ] ]

  let formPanel =
    Exhibit.panel
      "One form, four controls that did not exist a release ago"
      "Edit anything. The pane below is not a mock-up of a submission — it is the resolved value of each field's own State key, read back as you type."
      [ Html.div
          [ prop.className "ik-split"
            prop.children
              [ Html.div [ prop.className "ik-form"; prop.children [ Exhibit.renderLive submission ] ]
                Html.div
                  [ prop.className "ik-side"
                    prop.children
                      [ Html.h4 [ prop.text "The submission, as data" ]
                        readback
                        Html.p
                          [ prop.className "ik-note"
                            prop.text
                              "Nothing here is transmitted. This page has no server and no key; the store is the tab's." ] ] ] ] ] ]

  let point (text: string) = Html.li [ prop.text text ]

  let rulesPanel =
    Exhibit.panel
      "The rules that are refusals, not conventions"
      ""
      [ Html.ul
          [ prop.className "px-points"
            prop.children
              [ point
                  "A Tokens field that declares allowFreeText false and names no suggestion source is REFUSED at decode. It could admit no token by any gesture — a control that cannot be used — so the language will not carry it."
                point
                  "A Rating with a max below one is refused at decode: a scale with no positions cannot be rendered, and cannot be announced to a reader who is not looking at it."
                point
                  "The two allowFreeText slots have OPPOSITE defaults, and the difference is the field. A combobox narrows an existing list, so its default is closed; a token field collects what the reader has to say, so its default is open. Both are omitted from the wire at their own default, so both shortest documents are the ordinary ones."
                point
                  "Color is a form field, not a style slot. The distinction is who chose the value: a semantic style is the document's intent, and this is the reader's answer." ] ]
        Exhibit.wireDrawer "Show the form's wire — six fields, four vocabularies" wire ]

  Exhibit.shell
    "intake"
    "The Intake Form"
    "A conference submission — a closed list you can type into, an open one you can add to, several values in one control, a bounded ordinal, and a colour. Ordinary, which is exactly the point: this is the shape a model reaches for, and until this release it had to fake all four."
    [ formPanel; rulesPanel ]
    [ Exhibit.Claim.Verified
        "Every control is the shipped renderer's own rendering of that form-field kind. No component library is loaded on this page."
      Exhibit.Claim.Verified
        "The read-back pane resolves each field's own State key out of the live store. Edit a field and the value beside it moves, because it is the same value — not a copy this page keeps in step."
      Exhibit.Claim.Verified
        "The two comboboxes differ in exactly one slot. Type a room that is not on the list and it is refused; type a track that is not on the list and it is kept."
      Exhibit.Claim.Limit
        "Submitting does nothing. The page has no server, no key and no destination — and a form that pretended to post would be the one untrue thing on it."
      Exhibit.Claim.Limit
        "The values live in this tab's store and nowhere else. Reloading loses them, which is what a page with no backend should do rather than quietly persisting a stranger's talk proposal." ]

let page: ReactElement = IntakeView()
