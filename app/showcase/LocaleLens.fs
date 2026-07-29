module Fuaran.Showcase.LocaleLens

// ============================================================================
//  The Locale Lens – the same instant, rendered through many locales at once.
//  Pillar: "intent, not implementation".
//
//  The thesis: a date/time in a Fuaran tree is stored as a semantically
//  unambiguous value – whole Unix-epoch seconds for an absolute instant, a
//  signed unit count for relative time – plus a bounded formatting INTENT
//  (`Format.Date Full`, `Format.RelativeTime Day`) and a locale SELECTOR
//  (`Ambient` or a pinned `Explicit` tag). No locale string, no pre-formatted
//  text, no month name ever appears in the data. Localisation is entirely the
//  renderer's job, resolved at draw time through the browser's own CLDR data
//  (`Intl.DateTimeFormat` / `Intl.RelativeTimeFormat` / `Intl.NumberFormat`).
//
//  What this page does: builds ONE briefing tree from one epoch number, then
//  renders that identical tree side by side through the real renderer, once per
//  panel, varying only the host-supplied ambient locale (`BindingSources.Locale`
//  – the same single field a real host sets app-wide). Language, field order,
//  separators, digit shapes (Cairo), even the calendar year (Bangkok's Buddhist
//  calendar, Tokyo's Reiwa era) all change; the wire bytes do not – the wire
//  panel shows the canonical encoding so you can check for yourself.
//
//  Honest scope: every panel shares your device's clock and time zone – this
//  page compares locales, not time zones. The instant is the same everywhere on
//  Earth, which is exactly why it can be stored as one number. One row pins
//  `LocaleSource.Explicit "en-GB"` to show a document date that must NOT float
//  with the viewer's locale – it renders identically in every panel.
// ============================================================================

open Feliz
open Fable.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

// ─── Time interop (page chrome only – the tree itself stores plain numbers) ──

[<Emit("Date.now() / 1000")>]
let private nowSeconds () : float = jsNative

[<Emit("Date.parse($0) / 1000")>]
let private parseLocalInput (s: string) : float = jsNative

[<Emit("new Date($0 * 1000).toISOString()")>]
let private isoUtc (epochSeconds: float) : string = jsNative

[<Emit("(function(t){var d=new Date(t*1000);var p=function(n){return(n<10?'0':'')+n;};return d.getFullYear()+'-'+p(d.getMonth()+1)+'-'+p(d.getDate())+'T'+p(d.getHours())+':'+p(d.getMinutes());})($0)")>]
let private toLocalInputValue (epochSeconds: float) : string = jsNative

[<Emit("isNaN($0)")>]
let private isNaNJs (v: float) : bool = jsNative

// ─── The lens roster (each is a BCP-47 tag the panels hand the renderer) ─────

type private Lens =
  { Tag: string
    Place: string
    Note: string
    DefaultOn: bool }

let private roster: Lens list =
  [ { Tag = "en-US"
      Place = "New York"
      Note = "month first, 12-hour habits"
      DefaultOn = true }
    { Tag = "en-GB"
      Place = "London"
      Note = "same language, day first"
      DefaultOn = true }
    { Tag = "de-DE"
      Place = "Berlin"
      Note = "dotted dates, comma decimals"
      DefaultOn = false }
    { Tag = "fr-FR"
      Place = "Paris"
      Note = "narrow spaces as group separators"
      DefaultOn = false }
    { Tag = "ja-JP"
      Place = "Tokyo"
      Note = "year first, 年月日 units"
      DefaultOn = true }
    { Tag = "ar-EG"
      Place = "Cairo"
      Note = "Arabic script, Eastern Arabic digits"
      DefaultOn = true }
    { Tag = "th-TH"
      Place = "Bangkok"
      Note = "Buddhist calendar – the year itself changes"
      DefaultOn = true }
    { Tag = "hi-IN"
      Place = "Delhi"
      Note = "lakh/crore digit grouping"
      DefaultOn = false }
    { Tag = "pt-BR"
      Place = "São Paulo"
      Note = "day first, comma decimals"
      DefaultOn = false }
    { Tag = "ja-JP-u-ca-japanese"
      Place = "Tokyo · Reiwa era"
      Note = "the calendar is a locale extension – 令和"
      DefaultOn = false } ]

// ─── Countdown: pick the honest unit for the gap, store a signed count ───────

let private countdown (deltaSeconds: float) : RelativeTimeUnit * float =
  let magnitude = abs deltaSeconds

  if magnitude < 5400. then
    RelativeTimeUnit.Minute, System.Math.Round(deltaSeconds / 60.)
  elif magnitude < 172800. then
    RelativeTimeUnit.Hour, System.Math.Round(deltaSeconds / 3600.)
  else
    RelativeTimeUnit.Day, System.Math.Round(deltaSeconds / 86400.)

let private unitName (u: RelativeTimeUnit) : string =
  match u with
  | RelativeTimeUnit.Second -> "Second"
  | RelativeTimeUnit.Minute -> "Minute"
  | RelativeTimeUnit.Hour -> "Hour"
  | RelativeTimeUnit.Day -> "Day"
  | RelativeTimeUnit.Week -> "Week"
  | RelativeTimeUnit.Month -> "Month"
  | RelativeTimeUnit.Year -> "Year"

// ─── The briefing tree – ONE value, built once, rendered through every lens ──

let private factRow (nid: string) (label: string) (help: string option) (value: Binding<string>) : Node<unit> =
  Fuaran.factSpec
    nid
    { Defaults.fact with
        Label = TextSource.Literal label
        Value = TextSource.Bound value
        Help = help |> Option.map TextSource.Literal }

let private briefingTree (epochSeconds: float) (unit: RelativeTimeUnit) (count: float) : Node<unit> =
  let at = Binding.Static(Some epochSeconds)

  Fuaran.card
    "ll-briefing"
    { Defaults.card with
        Heading = Some(TextSource.Literal "Launch briefing")
        Children =
          [ factRow "ll-day" "Launch day" None (binding.format at (localeFormat.date DateStyle.Full) locale.ambient)
            factRow
              "ll-stub"
              "On the ticket stub"
              None
              (binding.format at (localeFormat.date DateStyle.Short) locale.ambient)
            factRow
              "ll-count"
              "Countdown"
              (Some(sprintf "stored as a signed count: %s %s" (unitName unit) (string count)))
              (binding.format (Binding.Static(Some count)) (localeFormat.relativeTime unit) locale.ambient)
            factRow
              "ll-guests"
              "Expected guests"
              None
              (binding.format (Binding.Static(Some 1234567.)) (localeFormat.number None) locale.ambient)
            factRow
              "ll-price"
              "Ticket price"
              (Some "the ISO-4217 code is data; the symbol and its position are not")
              (binding.format (Binding.Static(Some 149.5)) (localeFormat.currency "EUR") locale.ambient)
            factRow
              "ll-contract"
              "Contract date – pinned"
              (Some "LocaleSource.Explicit \"en-GB\" – identical in every panel")
              (binding.format at (localeFormat.date DateStyle.Long) (locale.explicit "en-GB")) ] }

/// The money shot: the SAME tree, drawn by the one real renderer – the only
/// thing that differs per panel is the ambient locale handed to binding
/// resolution, exactly the single `BindingSources.Locale` field a real host
/// sets once, app-wide.
let private renderThroughLens (tag: string) (n: Node<unit>) : ReactElement =
  Render.renderWithSources
    { BindingResolver.empty with
        Locale = tag }
    ignore
    n

// ─── The page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private LocaleLensView () : ReactElement =
  // Default instant: three days out, on a whole minute so the wire stays tidy.
  let epoch, setEpoch =
    React.useState (fun () -> System.Math.Floor(nowSeconds () / 60.) * 60. + 259200.)

  let enabled, setEnabled =
    React.useState (fun () ->
      roster
      |> List.filter (fun l -> l.DefaultOn)
      |> List.map (fun l -> l.Tag)
      |> Set.ofList)

  let unit, count = countdown (epoch - nowSeconds ())
  let tree = briefingTree epoch unit count
  let wire = CanonicalJson.encodeNode tree

  let toggle (tag: string) =
    setEnabled (
      if Set.contains tag enabled then
        Set.remove tag enabled
      else
        Set.add tag enabled
    )

  let presets =
    [ "in 3 days", 259200.; "90 days ago", -7776000.; "in 18 months", 47304000. ]

  // ── the stored value (what actually sits in the tree – and its wire bytes) ──
  let storedValue =
    Html.div
      [ prop.className "ll-panel"
        prop.children
          [ Html.h3 [ prop.text "The stored value" ]
            Html.p
              [ prop.className "ll-muted"
                prop.text
                  "Pick any instant. What the tree stores is a single number – whole Unix-epoch seconds – plus a bounded formatting intent and an Ambient locale selector. No month name, no locale string, no pre-formatted text sits in the data." ]
            Html.div
              [ prop.className "ll-picker"
                prop.children
                  [ Html.input
                      [ prop.className "ll-input"
                        prop.type' "datetime-local"
                        prop.value (toLocalInputValue epoch)
                        prop.onChange (fun (s: string) ->
                          let t = parseLocalInput s

                          if not (isNaNJs t) then
                            setEpoch t) ]
                    yield!
                      presets
                      |> List.map (fun (label, offset) ->
                        Html.button
                          [ prop.className "ll-chip"
                            prop.text label
                            prop.onClick (fun _ -> setEpoch (System.Math.Floor(nowSeconds () / 60.) * 60. + offset)) ]) ] ]
            Html.div
              [ prop.className "ll-stored-row"
                prop.children
                  [ Html.span [ prop.className "ll-stored-label"; prop.text "epoch seconds" ]
                    Html.code [ prop.className "ll-stored-val"; prop.text (string epoch) ]
                    Html.span [ prop.className "ll-stored-label"; prop.text "the same instant, ISO-8601 UTC" ]
                    Html.code [ prop.className "ll-stored-val"; prop.text (isoUtc epoch) ] ] ]
            Html.details
              [ prop.className "ll-wire"
                prop.children
                  [ Html.summary [ prop.text "The canonical wire bytes of the briefing tree" ]
                    Html.p
                      [ prop.className "ll-muted"
                        prop.text
                          "The real canonical encoding of the tree every panel below renders. Search it for a month name or a city – you won't find one. The dates are the epoch number; the countdown is a signed unit count; the locale selector is the word Ambient." ]
                    Html.pre [ prop.className "wire-json"; prop.text wire ] ] ] ] ]

  // ── the lens picker ──
  let lensPicker =
    Html.div
      [ prop.className "ll-lenses"
        prop.children
          [ for l in roster ->
              let on = Set.contains l.Tag enabled

              Html.button
                [ prop.className (if on then "ll-chip ll-chip-on" else "ll-chip")
                  prop.onClick (fun _ -> toggle l.Tag)
                  prop.children
                    [ Html.span [ prop.text l.Place ]
                      Html.code [ prop.className "ll-chip-tag"; prop.text l.Tag ] ] ] ] ]

  // ── the comparative panels (same tree, one render per enabled lens) ──
  let panels =
    Html.div
      [ prop.className "ll-grid"
        prop.children
          [ for l in roster do
              if Set.contains l.Tag enabled then
                Html.div
                  [ prop.className "ll-lens-card"
                    prop.children
                      [ Html.div
                          [ prop.className "ll-lens-head"
                            prop.children
                              [ Html.span [ prop.className "ll-place"; prop.text l.Place ]
                                Html.code [ prop.className "ll-tag"; prop.text l.Tag ] ] ]
                        Html.p [ prop.className "ll-note"; prop.text l.Note ]
                        renderThroughLens l.Tag tree ] ] ] ]

  let honesty =
    Html.div
      [ prop.className "ll-honesty"
        prop.children
          [ Html.h3 [ prop.text "What is honest here" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Every panel is the one real renderer drawing the identical tree. The only thing that differs is the ambient locale handed to binding resolution – the same single field a real host sets once for its whole app. No panel-specific formatting code exists on this page." ]
                    Html.li
                      [ prop.text
                          "The stored value never changes: open the wire drawer – one epoch number, a bounded dateStyle intent, and the word Ambient. What varies is deep – language, field order, separators, digit shapes in Cairo, even the calendar year in Bangkok and the era in Tokyo – and none of it was authored; the renderer resolves it through the browser's own locale data." ]
                    Html.li
                      [ prop.text
                          "The pinned row is the counter-example: a contract date that must not float with the viewer renders through LocaleSource.Explicit and stays identical in every panel. Ambient by default, pinned when the document demands it – both are one word in the data." ]
                    Html.li
                      [ prop.text
                          "Honest scope: all panels share your device's clock and time zone – this page compares locales, not time zones. The instant itself is the same everywhere on Earth; that is precisely why it can be stored as one number. And the formatting vocabulary is deliberately bounded (a dateStyle, a unit, an ISO code) – semantic intent on the wire, never a raw formatting-options bag." ] ] ] ] ]

  Html.div
    [ prop.className "ll-page"
      prop.children
        [ Html.h1 [ prop.className "ll-title"; prop.text "The Locale Lens" ]
          Html.p
            [ prop.className "ll-lede"
              prop.text
                "A date stored as text is an ambush – 03/04/05 means three different days in three countries. A Fuaran tree stores an instant as one unambiguous number and leaves localisation to the renderer. Here is the same value, rendered through many locales at once: different words, orders, digits, even different years – and the data never changes." ]
          storedValue
          Html.h3 [ prop.className "ll-section-head"; prop.text "Choose your lenses" ]
          lensPicker
          panels
          honesty ] ]

let page: ReactElement = LocaleLensView()
