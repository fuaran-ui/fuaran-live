module Fuaran.Showcase.Briefing

// ============================================================================
//  The Briefing — a media node that carries its own accessibility (Phase 1110).
//  Pillar: "intent, not implementation".
//
//  A release briefing: play it, or read it. The point is that BOTH are one
//  node. `Media` carries a `tracks` list — captions, subtitles, chapters,
//  descriptions, each with its own language and its own kind — and a
//  `transcript` slot beside them, so a page does not have to choose between
//  being playable and being readable, and a machine reading the wire gets the
//  words without decoding a single audio frame.
//
//  Two design facts about the vocabulary are what the page is really showing:
//
//   * `Label` is MANDATORY on `Media` and has no decorative case. An image can
//     honestly declare `alt=""` and leave the accessibility tree; a media
//     element is a TRANSPORT — always an interactive control — and one with no
//     accessible name is announced as "audio" and nothing more.
//   * `Controls` DEFAULTS TO TRUE and is omitted from the wire at that default.
//     A media element with no transport cannot be paused, seeked or muted by a
//     keyboard user at all, so the accessible value is the default and turning
//     it off is the deviation the document has to spell out.
//
//  THE ASSET. `briefing.wav` is synthesised by `scripts/make-briefing-track.mjs`
//  — one low tone per chapter, so the chapter marks are audibly where the
//  chapter track says they are. It is not a recording of anyone, and the page
//  says so. This site fetches nothing off-origin; borrowing a stock clip would
//  have put a third party inside the one surface whose whole claim is that it
//  does not.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── the transcript, authored once and used twice ────────────────────────────
//
// The same text is the `transcript` slot on the media node AND the body of the
// reading pane below it. One string, so the two can never disagree — which is
// the failure the slot exists to prevent (a transcript kept in a sibling
// element drifts from the recording the first time either is edited).

let private transcriptText =
  "**What changed.** The platform-baseline release closes the gaps a reader hits in the first five minutes: media that carries its own captions, embeds that are sandboxed by default, hints on the affordances that need them, and a form vocabulary wide enough for an ordinary intake form.\n\n"
  + "**Where it lands.** Every one of them is a slot on the wire, so a tree written by a model on one host renders the same way on the next. Nothing here is a renderer feature with a document that cannot say it.\n\n"
  + "**What to check.** Open the wire under any exhibit on this site. If the capability is not in the bytes, it is not in the language — it is a host doing something the document never asked for.\n\n"
  + "**Close.** Every page here is itself a Fuaran tree, drawn by the same renderer. The site is exhibit zero."

// ─── the tree ────────────────────────────────────────────────────────────────

let private track (kind: TrackKind) (label: string) (lang: string) (src: string) (isDefault: bool) : TrackEntry =
  { Kind = kind
    Label = TextSource.Literal label
    SrcLang = lang
    Src = Binding.Static(Some src)
    Default = isDefault }

/// The whole exhibit is this ONE node. Everything the page claims —
/// playability, three tracks of two different kinds, chapter marks, and a
/// transcript that cannot drift — is a slot on it.
let private briefing: Node<obj> =
  Fuaran.mediaSpec
    "briefing-media"
    { Defaults.media with
        Kind = MediaKind.Audio
        Src = Binding.Static(Some "./briefing/briefing.wav")
        Label = TextSource.Literal "Release briefing — four chapters, 24 seconds"
        Tracks =
          [ track TrackKind.Captions "English captions" "en" "./briefing/captions-en.vtt" true
            track TrackKind.Subtitles "Sous-titres français" "fr" "./briefing/subtitles-fr.vtt" false
            track TrackKind.Chapters "Chapters" "en" "./briefing/chapters.vtt" false ]
        Transcript = Some(TextSource.Literal transcriptText) }

/// The reading pane. The SAME string as the transcript slot above — the page
/// authors it once, so "the transcript matches the recording" is a property of
/// the source rather than a promise.
let private readingPane: Node<obj> =
  Fuaran.box
    "briefing-read"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 12)
      Role = BoxRole.Card
      KeepTogether = false
      BreakBefore = false
      Heading = Some(TextSource.Literal "The same briefing, read")
      Children = [ Fuaran.markdown "briefing-read-body" transcriptText ] }

let private wire: string = CJson.encodeNode briefing

// ─── the panels ──────────────────────────────────────────────────────────────

let private playPanel: ReactElement =
  Exhibit.panel
    "Press play"
    "Three tracks ride this one node: English captions, French subtitles, and chapter marks. Open the transport's track menu to switch between them; the chapter boundaries are the tone changes you can hear."
    [ Exhibit.renderLive briefing ]

let private readPanel: ReactElement =
  Exhibit.panel
    "The words, without pressing anything"
    "This pane and the node's own transcript slot are the SAME authored string. A transcript kept in a sibling element drifts from its recording the first time either is edited; carried on the node, it cannot."
    [ Exhibit.renderStatic readingPane ]

let private point (text: string) : ReactElement = Html.li [ prop.text text ]

let private vocabularyPanel: ReactElement =
  let points =
    Html.ul
      [ prop.className "px-points"
        prop.children
          [ point
              "`label` is mandatory and has no decorative case. An image can honestly declare an empty alt and leave the accessibility tree; a media element is a transport — always an interactive control — and one with no name is announced as \"audio\" and nothing more."
            point
              "`controls` defaults to TRUE and is omitted from the wire at that default. A media element with no transport cannot be paused, seeked or muted by a keyboard user at all, so the accessible value is the default and switching it off is what a document has to spell out."
            point
              "There is one media kind carrying a variant, not a Video kind beside an Audio kind. Everything the two share — the source, the label, the transport, the loop, the tracks, the transcript — sits in one record, so a page that swaps a recording for a film changes one field."
            point
              "Autoplay does not exist on the audio variant. Not off by default — absent, because a slot that defaults to off is one a caller can switch on, and there is no document this language wants to be able to state in which a page begins making sound unbidden." ] ]

  Exhibit.panel
    "What the vocabulary insists on"
    ""
    [ points
      Exhibit.wireDrawer "Show the wire — the whole exhibit is this node" wire ]

// ─── the page ────────────────────────────────────────────────────────────────

let private claims: Exhibit.Claim list =
  [ Exhibit.Claim.Verified
      "The player is the shipped renderer's own Media rendering, driven by the tree above — not an embedded widget. The track menu you open is the browser's, populated from the node's tracks list."
    Exhibit.Claim.Verified
      "The three tracks are real WebVTT files served from this origin, of two different kinds: captions carry the sound for a reader who cannot hear it, subtitles carry the words for a reader who does not read the language."
    Exhibit.Claim.Verified
      "The chapter boundaries in chapters.vtt are the tone changes in the audio, because both come from one table in the generator script."
    Exhibit.Claim.Limit
      "The audio is SYNTHESISED — one low tone per chapter — not a recording of anyone. This site fetches nothing off-origin, and borrowing a stock clip would have put a third party inside the one surface whose whole claim is that it does not."
    Exhibit.Claim.Limit
      "Descriptions is the fourth track kind and is not used here. It narrates what is on screen for a reader who cannot see it, and there is no picture on this page to narrate — a description track over an audio-only node would be a slot filled to look complete." ]

let page: ReactElement =
  Exhibit.shell
    "briefing"
    "The Briefing"
    "Play it, or read it — it is one node either way. A Fuaran media element carries its own caption, subtitle and chapter tracks and its own transcript, so the words are on the wire whether or not anyone presses play."
    [ playPanel; readPanel; vocabularyPanel ]
    claims
