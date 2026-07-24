module Fuaran.Showcase.WireVersioning

// ============================================================================
//  The Versioning Envelope – one wire, many *versions*. Pillar: "one wire, many
//  worlds".
//
//  One artefact is stamped with the schema profile that authored it (core@1.2)
//  and put on the wire in a `$profile` / `$payload` envelope. Three hosts, each
//  speaking a different profile, read the SAME bytes and negotiate:
//
//   • core@1.2 (Current) – decodes everything, renders the whole app.
//   • core@1.0 (Behind)  – meets a kind minted in 1.2 (LiveTicker) it does not
//     understand; must-ignore-but-preserve keeps it verbatim, renders the rest,
//     and the whole artefact still round-trips byte-for-byte (nothing lost).
//   • core@2.0 (Foreign) – a different major is a hard wall; refuse, never
//     silently mis-decode.
//
//  Everything here runs the REAL `Fuaran.Core.Wire.Versioning` substrate: the
//  canonical envelope bytes, `negotiate`, `decodeTolerant` / `reencode`, and the
//  computed `classify` / `bump` (additive → minor, breaking → major). No fakery.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── the real versioning engine (Fuaran.Core.Wire.Versioning) ────────────────

module private Engine =

  open Fuaran.Core

  type Widget =
    | WMetric of label: string * value: int
    | WCallout of heading: string * body: string
    | WTicker of label: string * symbol: string

  type Slot =
    | Known of Widget
    | Preserved of kind: string * bytes: string

  type Negotiation =
    | Current
    | Behind
    | Foreign

  type HostView =
    { Profile: string
      Negotiation: Negotiation
      Slots: Slot list
      RoundTripsExact: bool
      Reason: string }

  // The authored app, stamped core@1.2. `LiveTicker` is the kind minted in 1.2 –
  // the one an older core@1.0 host will not understand.
  let private metricJ l (v: int) =
    Canon.typed "Metric" [ "label", JStr l; "value", JInt v ]

  let private calloutJ h b =
    Canon.typed "Callout" [ "heading", JStr h; "body", JStr b ]

  let private tickerJ l s =
    Canon.typed "LiveTicker" [ "label", JStr l; "symbol", JStr s ]

  let private appPayload =
    JArr
      [ metricJ "Revenue" 128000
        calloutJ "All systems nominal" "Nightly batch finished at 02:14 with no retries."
        tickerJ "FUARAN" "up 2.3% today" ]

  let private authored =
    match Versioning.Profile.tryParse "core@1.2" with
    | Ok p -> p
    | Error _ -> Versioning.Profile.coreV1

  let private envelope: Versioning.Envelope =
    { Profile = authored
      Payload = appPayload }

  /// The one artefact on the wire – canonical bytes carrying `$profile` + `$payload`.
  let envelopeBytes = Versioning.render envelope
  let authoredText = Versioning.Profile.render authored

  let private tagOf (el: JVal) =
    Decode.getProp "$type" el |> Result.bind Decode.asString

  let private decodeWidget (el: JVal) : Result<Widget, string> =
    tagOf el
    |> Result.bind (fun tag ->
      match tag with
      | "Metric" ->
        Decode.strField "label" el
        |> Result.bind (fun l -> Decode.intField "value" el |> Result.map (fun v -> WMetric(l, v)))
      | "Callout" ->
        Decode.strField "heading" el
        |> Result.bind (fun h -> Decode.strField "body" el |> Result.map (fun b -> WCallout(h, b)))
      | "LiveTicker" ->
        Decode.strField "label" el
        |> Result.bind (fun l -> Decode.strField "symbol" el |> Result.map (fun s -> WTicker(l, s)))
      | other -> Error("unrecognised kind: " + other))

  let private encodeWidget (w: Widget) : JVal =
    match w with
    | WMetric(l, v) -> metricJ l v
    | WCallout(h, b) -> calloutJ h b
    | WTicker(l, s) -> tickerJ l s

  // A consumer profile's known-kind set: core@1.0 knows two kinds; the additive
  // 1.2 bump added LiveTicker, so a 1.2+ host knows all three.
  let private knownAt (p: Versioning.Profile) : Set<string> =
    if p.Major = 1 && p.Minor >= 2 then
      set [ "Metric"; "Callout"; "LiveTicker" ]
    else
      set [ "Metric"; "Callout" ]

  /// Read the one wire artefact as a host speaking `consumerText`, running the
  /// real negotiate + tolerant-decode path.
  let hostView (consumerText: string) : HostView =
    let consumer =
      match Versioning.Profile.tryParse consumerText with
      | Ok p -> p
      | Error _ -> Versioning.Profile.coreV1

    match Versioning.parse envelopeBytes with
    | Error e ->
      { Profile = consumerText
        Negotiation = Foreign
        Slots = []
        RoundTripsExact = false
        Reason = "unreadable wire: " + e }
    | Ok env ->
      match Versioning.negotiate consumer env.Profile with
      | Versioning.Foreign a ->
        { Profile = consumerText
          Negotiation = Foreign
          Slots = []
          RoundTripsExact = false
          Reason =
            sprintf
              "authored %s · this host speaks %s – a different major is a hard wall. Cross it with a migration shim, never a silent mis-read."
              (Versioning.Profile.render a)
              consumerText }
      | comp ->
        let known = knownAt consumer
        let isKnown k = Set.contains k known

        let items =
          match env.Payload with
          | JArr xs -> xs
          | x -> [ x ]

        let decoded =
          items
          |> List.map (fun el -> el, Versioning.decodeTolerant tagOf isKnown decodeWidget el)

        let slots =
          decoded
          |> List.map (fun (el, r) ->
            match r with
            | Ok(Versioning.Known w) -> Known w
            | Ok(Versioning.Unknown u) -> Preserved(u.Kind, Canon.render u.Payload)
            | Error _ -> Preserved("malformed", Canon.render el))

        // Does the old host round-trip the WHOLE artefact byte-for-byte –
        // re-emitting what it renders AND the unknown kind it preserved?
        let reencoded =
          JArr(
            decoded
            |> List.map (fun (el, r) ->
              match r with
              | Ok d -> Versioning.reencode encodeWidget d
              | Error _ -> el)
          )

        let roundTrips = Canon.render reencoded = Canon.render env.Payload

        let neg =
          match comp with
          | Versioning.Current -> Current
          | _ -> Behind

        { Profile = consumerText
          Negotiation = neg
          Slots = slots
          RoundTripsExact = roundTrips
          Reason = "" }

  // The computed version story: adding a kind is additive → minor bump; removing
  // one is breaking → major bump. Both derived by the real `classify` / `bump`.
  let private describe (before: string list) (after: string list) (baseP: Versioning.Profile) : string * string =
    let ev = Versioning.classify (Set.ofList before) (Set.ofList after)

    let cls =
      match ev with
      | Versioning.Additive _ -> "additive → minor bump"
      | Versioning.Breaking _ -> "breaking → major bump"

    cls, Versioning.Profile.render (Versioning.bump baseP ev)

  let additiveStory =
    describe
      [ "Metric"; "Callout" ]
      [ "Metric"; "Callout"; "LiveTicker" ]
      { Versioning.Profile.coreV1 with
          Minor = 1 }

  let breakingStory =
    describe
      [ "Metric"; "Callout" ]
      [ "Callout" ]
      { Versioning.Profile.coreV1 with
          Minor = 9 }

// ─── the UI (renders each host's real negotiation outcome) ───────────────────

let private renderNode (n: Node<'msg>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

let private widgetNode (i: int) (w: Engine.Widget) : Node<unit> =
  match w with
  | Engine.WMetric(l, v) ->
    Fuaran.metric
      (sprintf "wv-w-%d" i)
      { Defaults.metric with
          Label = TextSource.Literal l
          Value = Binding.Static(float v)
          Format = CellFormat.Number(Some 0)
          Tone = ToneVariant.Brand }
  | Engine.WCallout(h, b) ->
    Fuaran.callout
      (sprintf "wv-w-%d" i)
      { Defaults.callout with
          Heading = Some(TextSource.Literal h)
          Body = TextSource.Literal b
          Tone = ToneVariant.Default }
  | Engine.WTicker(l, s) ->
    Fuaran.callout
      (sprintf "wv-w-%d" i)
      { Defaults.callout with
          Heading = Some(TextSource.Literal l)
          Body = TextSource.Literal s
          Tone = ToneVariant.Success }

let private slotView (i: int) (slot: Engine.Slot) : ReactElement =
  match slot with
  | Engine.Known w -> renderNode (widgetNode i w)
  | Engine.Preserved(kind, bytes) ->
    Html.div
      [ prop.className "wv-preserved"
        prop.children
          [ Html.div
              [ prop.className "wv-preserved-head"
                prop.children
                  [ Html.span [ prop.className "wv-preserved-mark"; prop.text "⬚" ]
                    Html.span
                      [ prop.className "wv-preserved-kind"
                        prop.text (sprintf "%s – a kind from core@1.2" kind) ] ] ]
            Html.p
              [ prop.className "wv-preserved-note"
                prop.text "This host can't render it, so it holds the bytes verbatim rather than dropping them:" ]
            Html.pre
              [ prop.className "wv-preserved-bytes"
                prop.children [ Html.code [ prop.text bytes ] ] ] ] ]

let private negBadge (n: Engine.Negotiation) : ReactElement =
  let cls, label =
    match n with
    | Engine.Current -> "wv-badge wv-badge-current", "Current – decodes fully"
    | Engine.Behind -> "wv-badge wv-badge-behind", "Behind – tolerated"
    | Engine.Foreign -> "wv-badge wv-badge-foreign", "Foreign – refused"

  Html.span [ prop.className cls; prop.text label ]

let private hostCard (consumerText: string) : ReactElement =
  let hv = Engine.hostView consumerText

  let body =
    match hv.Negotiation with
    | Engine.Foreign ->
      Html.div
        [ prop.className "wv-refusal"
          prop.children [ Html.p [ prop.text hv.Reason ] ] ]
    | _ ->
      Html.div
        [ prop.className "wv-host-app"
          prop.children
            [ yield! hv.Slots |> List.mapi slotView
              if hv.Negotiation = Engine.Behind then
                Html.p
                  [ prop.className (
                      if hv.RoundTripsExact then
                        "wv-roundtrip wv-roundtrip-ok"
                      else
                        "wv-roundtrip wv-roundtrip-bad"
                    )
                    prop.text (
                      if hv.RoundTripsExact then
                        "✓ the whole artefact round-trips byte-for-byte – the preserved kind is not lost"
                      else
                        "✗ round-trip mismatch"
                    ) ] ] ]

  Html.div
    [ prop.className "wv-host"
      prop.children
        [ Html.div
            [ prop.className "wv-host-head"
              prop.children
                [ Html.span [ prop.className "wv-host-profile"; prop.text consumerText ]
                  negBadge hv.Negotiation ] ]
          body ] ]

let private view () : ReactElement =
  let additiveClass, additiveResult = Engine.additiveStory
  let breakingClass, breakingResult = Engine.breakingStory

  Html.div
    [ prop.className "wv-page"
      prop.children
        [ Html.h1 [ prop.className "wv-title"; prop.text "The Versioning Envelope" ]
          Html.p
            [ prop.className "wv-lede"
              prop.text
                "One artefact, stamped with the schema that authored it, put on the wire once. Three hosts – each speaking a different version – read the same bytes. The one behind gracefully degrades and preserves what it can't render; a breaking version is refused outright, never silently mis-read. This is how a design outlives not just any framework, but schema change itself." ]

          // the one artefact
          Html.div
            [ prop.className "wv-artefact"
              prop.children
                [ Html.div
                    [ prop.className "wv-artefact-head"
                      prop.children
                        [ Html.span [ prop.className "wv-artefact-tag"; prop.text "The artefact on the wire" ]
                          Html.span
                            [ prop.className "wv-artefact-profile"
                              prop.text (sprintf "$profile: %s" Engine.authoredText) ] ] ]
                  Html.pre
                    [ prop.className "wv-artefact-bytes"
                      prop.children [ Html.code [ prop.text Engine.envelopeBytes ] ] ] ] ]

          // three hosts
          Html.h2 [ prop.className "wv-section-title"; prop.text "Three versions read it" ]
          Html.div
            [ prop.className "wv-hosts"
              prop.children [ hostCard "core@1.2"; hostCard "core@1.0"; hostCard "core@2.0" ] ]

          // computed version story
          Html.div
            [ prop.className "wv-evolution"
              prop.children
                [ Html.h3 [ prop.text "Why those version numbers?" ]
                  Html.p
                    [ prop.className "wv-evolution-note"
                      prop.text
                        "\"Is this change breaking?\" is computed from the schema delta, not a reviewer's opinion – no removed kinds is additive, any removal is breaking." ]
                  Html.ul
                    [ prop.children
                        [ Html.li
                            [ prop.children
                                [ Html.strong [ prop.text "Add LiveTicker: " ]
                                  Html.text (sprintf "%s → %s" additiveClass additiveResult) ] ]
                          Html.li
                            [ prop.children
                                [ Html.strong [ prop.text "Remove Metric: " ]
                                  Html.text (sprintf "%s → %s" breakingClass breakingResult) ] ] ] ] ] ]

          // honesty
          Html.div
            [ prop.className "wv-honesty"
              prop.children
                [ Html.h3 [ prop.text "How honest is this?" ]
                  Html.ul
                    [ prop.children
                        [ Html.li
                            [ prop.text
                                "Every outcome runs the real Fuaran.Core.Wire.Versioning substrate in your browser: the canonical $profile/$payload envelope above, negotiate, decodeTolerant / reencode, and classify / bump. Nothing is scripted." ]
                          Html.li
                            [ prop.text
                                "The core@1.0 host genuinely does not know the LiveTicker kind, so it preserves those bytes verbatim – and the byte-for-byte round-trip check proves the artefact survives intact through a host that can't render all of it." ]
                          Html.li
                            [ prop.text
                                "The core@2.0 refusal is the negotiate result, not a caught error: a different major is declared incompatible before any decode, so the host never mis-interprets bytes it wasn't built for." ]
                          Html.li
                            [ prop.children
                                [ Html.text "Same substrate as "
                                  Html.a [ prop.href "#/demo/rosetta"; prop.text "Rosetta" ]
                                  Html.text
                                    " – there the design outlives the language; here it outlives the schema version." ] ] ] ] ] ] ] ]

let page: ReactElement = view ()
