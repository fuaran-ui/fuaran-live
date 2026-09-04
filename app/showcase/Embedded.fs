module Fuaran.Showcase.Embedded

// ============================================================================
//  Embedded — a third-party document, locked by default (Phase 1111).
//  Pillar: "the machine can see the UI".
//
//  `Embed` frames a document that is not yours and that will not cooperate with
//  you. It is a separate kind from `Mount` on exactly that point: `Mount`
//  composes a COOPERATING guest — a scope id, a declared message channel, a
//  capability request list, a host-side loader that produced the guest tree —
//  and a third-party page has none of those and could not acquire them.
//  Widening `Mount` to admit an uncooperative third party would weaken every
//  guarantee `Mount` makes.
//
//  TWO GATES, and the page shows both because they are genuinely different
//  questions:
//
//   1. The SCHEME FLOOR. `Embed`'s source does not ride the ordinary accept
//      set: the embed egress class admits `https` and NOTHING else — no other
//      scheme, and no schemeless reference either, because a relative reference
//      names a same-origin document, which is precisely the shape where
//      allowing scripts together with same-origin lets the framed document
//      reach its own sandbox attribute and take it off.
//   2. The DESTINATION POLICY. Having survived the floor, the host still asks
//      whether it declared this origin for this class. A refused source drops
//      the `src` attribute entirely and records the refusal in the markup: an
//      `<iframe>` with no source is a well-defined empty frame that fetches
//      nothing, where a refusal URL in the src would be a frame rendering a
//      page the author never named.
//
//  `permissions` is the closed relaxation list, omitted at EMPTY — and empty is
//  TOTAL DENIAL, so the wire-cheapest document is also the safest one. The
//  default a careless author gets is the locked one.
//
//  WHY THE GUEST IS SAME-ORIGIN. The showcase's Content-Security-Policy is
//  `default-src 'self'` with no frame-src relaxation, so a genuine third-party
//  frame is refused by the browser before the sandbox is ever consulted. The
//  posture is not weakened for a demo. Framing this origin's own static
//  `guest.html` costs nothing the exhibit needs: the guest is uncooperative by
//  construction (plain HTML, its own inline script, its own form, imported by
//  nothing here) and it REPORTS ON ITSELF, so the sandbox is visible biting
//  rather than asserted in a caption.
// ============================================================================

open Fable.Core
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

[<Emit("window.location.origin")>]
let private pageOrigin: string = jsNative

[<Emit("window.location.hostname")>]
let private pageHost: string = jsNative

/// The guest's absolute URL at THIS origin. Absolute because the embed class
/// refuses a schemeless reference by design; derived at runtime because the
/// origin is only known where the page is served.
let private guestUrl: string = pageOrigin + "/embedded/guest.html"

/// The policy this page renders under: this origin declared for the embed class
/// and nothing else. Not `permissiveEgress` — a page whose subject is the gate
/// should not be rendered with the gate switched off.
let private policy: Sanitize.EgressPolicy =
  Sanitize.denyNonLocalEgress
  |> Sanitize.allowOrigin (Sanitize.EgressOrigin.ExactHost pageHost) [ Sanitize.EgressClass.Embed ]

// ─── the three frames ────────────────────────────────────────────────────────

let private frame (id: string) (permissions: EmbedPermission list) : Node<obj> =
  Fuaran.embedSpec
    id
    { Defaults.embed with
        Src = Binding.Static(Some guestUrl)
        Title = TextSource.Literal "A guest document reporting on its own sandbox"
        AspectRatio = ImageAspect.SixteenNine
        Permissions = permissions }

let private locked: Node<obj> = frame "embed-locked" []

let private scripted: Node<obj> =
  frame "embed-scripted" [ EmbedPermission.AllowScripts ]

let private scriptedForms: Node<obj> =
  frame "embed-forms" [ EmbedPermission.AllowScripts; EmbedPermission.AllowForms ]

// ─── the source gate, run live by the shipped sanitiser ──────────────────────
//
// Not a table of what the floor WOULD do — the actual shipped function, called
// in this page over each candidate, with whatever it returns printed. The FIRST
// row is the source the frames above use, so the table and the frames cannot
// disagree — which also means that on a plain-http origin the first row refuses
// and the frames are empty, together.
//
// The SECOND row exists so the ALLOW path is visible wherever this page is
// served. It is the same document at the same host over https: the scheme floor
// accepts it and the policy declares that host for the embed class, so it
// resolves to `framed:` even while the page itself is being served over http and
// the frames above are empty. Without it a reader on localhost would see five
// refusals and no evidence that anything is ever admitted.

let private candidates: (string * string) list =
  [ guestUrl, "the source the three frames above actually use — this origin's own guest"
    "https://" + pageHost + "/embedded/guest.html", "the same document at the same host, over https"
    "http://" + pageHost + "/embedded/guest.html", "the same document over plain http"
    "./embedded/guest.html", "a schemeless, same-origin reference"
    "https://example.invalid/widget.html", "https, but an origin this page never declared"
    "javascript:alert(1)", "a script URL" ]

let private verdictRow (url: string, why: string) : ReactElement =
  let emitted, markers = Sanitize.sanitizeEmbedSrcForEgress policy url

  let verdictText =
    match emitted with
    | Some safe -> "framed: " + safe
    | None ->
      match markers with
      | (_, value) :: _ -> "src dropped — " + value
      | [] -> "src dropped"

  let rowClass =
    match emitted with
    | Some _ -> "eb-row eb-row-ok"
    | None -> "eb-row eb-row-no"

  Html.tr
    [ prop.className rowClass
      prop.children
        [ Html.td [ prop.className "eb-url"; prop.children [ Html.code [ prop.text url ] ] ]
          Html.td [ prop.className "eb-why"; prop.text why ]
          Html.td [ prop.className "eb-verdict"; prop.text verdictText ] ] ]

// ─── the panels ──────────────────────────────────────────────────────────────

let private frameCard (heading: string) (note: string) (node: Node<obj>) : ReactElement =
  Html.div
    [ prop.className "eb-card"
      prop.children
        [ Html.div [ prop.className "eb-card-head"; prop.text heading ]
          Html.p [ prop.className "eb-card-note"; prop.text note ]
          Exhibit.renderLiveWith policy node ] ]

let private framesPanel: ReactElement =
  let frames =
    Html.div
      [ prop.className "eb-frames"
        prop.children
          [ frameCard
              "permissions: [] — total denial"
              "The default a careless author gets. No script runs, so the guest's static fallback text is what you see, and its form cannot submit."
              locked
            frameCard
              "permissions: [AllowScripts]"
              "Script runs — the guest overwrites its own fallback. Its origin reads back opaque: it has been given an origin of its own that matches nothing, so it shares no storage, no cookies and no same-origin access with this page."
              scripted
            frameCard
              "permissions: [AllowScripts, AllowForms]"
              "Now the submit navigates the frame. Two relaxations, each named, each one thing — that is the whole of what widening a sandbox looks like here."
              scriptedForms ] ]

  Exhibit.panel
    "One guest, three permission sets"
    "Every frame below carries the same source and the same title. The only thing that differs is the permissions list — a closed vocabulary of four relaxations, omitted from the wire when empty."
    [ frames ]

let private gatePanel: ReactElement =
  let table =
    Html.table
      [ prop.className "eb-table"
        prop.children
          [ Html.thead
              [ prop.children
                  [ Html.tr
                      [ prop.children
                          [ Html.th [ prop.text "Candidate source" ]
                            Html.th [ prop.text "What it is" ]
                            Html.th [ prop.text "Verdict" ] ] ] ] ]
            Html.tbody [ prop.children [ for c in candidates -> verdictRow c ] ] ] ]

  Exhibit.panel
    "What the source gate does, run live"
    "Each row below is this page calling the SHIPPED sanitiser on that URL, under the policy this page renders with, and printing what came back. The first row is the source the three frames above use, so the table and the frames cannot disagree — including when they are all empty. The second row is the same document over https, so the admitted case is visible wherever this page is served."
    [ table ]

let private wirePanel: ReactElement =
  Exhibit.panel
    "The wire"
    "Note what is NOT in the locked frame's bytes: there is no permissions key at all. Empty is total denial, so the shortest document is the locked one."
    [ Exhibit.wireDrawer "Show the locked frame's wire" (CJson.encodeNode locked)
      Exhibit.wireDrawer "Show the two-relaxation frame's wire" (CJson.encodeNode scriptedForms) ]

let private prose (text: string) : ReactElement =
  Html.p [ prop.className "px-prose"; prop.text text ]

let private notAMountPanel: ReactElement =
  Exhibit.panel
    "Why this is not a Mount"
    ""
    [ prose
        "Mount composes a guest that COOPERATES: it has a scope id, a declared message channel, a capability request list, and a host-side loader that produced its tree. A third-party page has none of those and could not acquire them. Widening Mount to admit an uncooperative third party would weaken every guarantee Mount makes, so the two contracts stay two kinds — bidirectional cooperation on one side, default-deny isolation on the other."
      prose
        "It is equally not a Media variant. Media fetches an asset and DISPLAYS it; Embed fetches a document and lets it EXECUTE. That is a different question about trust, and it gets a different kind and its own egress class."
      prose
        "Four relaxations, closed. A top-level-navigation relaxation would let a framed document navigate the page that framed it — the drive-by redirect — and a downloads relaxation would put a file-save prompt in a third party's hands. Neither is admitted and neither is reserved. Popups, modals, pointer lock, presentation and orientation lock have no recorded demand and ARE reserved, as names a later admission would take. That is why this is an enum and not a bag of booleans." ]

// ─── the page ────────────────────────────────────────────────────────────────

let private claims: Exhibit.Claim list =
  [ Exhibit.Claim.Verified
      "The three frames are the shipped renderer's own Embed rendering of the three trees above. The differences between them are the browser enforcing the sandbox attributes the permissions list produced — the guest is one file, served once."
    Exhibit.Claim.Verified
      "The verdict table calls the shipped sanitiser in this page, under the same policy the frames render with. The refusal strings are the ones the renderer would put in the markup."
    Exhibit.Claim.Limit
      "The guest is served from THIS origin, not a third party. The showcase's Content-Security-Policy is default-src 'self' with no frame-src relaxation, so a genuine off-origin frame is refused by the browser before the sandbox is consulted — and this site does not weaken that posture to make a demo look better. The guest is uncooperative in every way that matters here: plain HTML with its own script and its own form, imported by nothing in the bundle."
    Exhibit.Claim.Limit
      "On a plain-http origin — local development — every frame here is empty and the table's first row refuses, because the embed class admits https and nothing else. That is the floor working rather than the page breaking; the published site is https."
    Exhibit.Claim.Limit
      "AllowFullscreen is the fourth relaxation and is not shown. It maps to a permissions-policy directive rather than a sandbox token, and a fullscreen guest inside a scrolling exhibit would demonstrate the browser's fullscreen UI rather than anything about the vocabulary." ]

let page: ReactElement =
  Exhibit.shell
    "embedded"
    "Embedded"
    "The same guest document, framed three times with three different relaxations. It reports on itself, so you are watching the sandbox bite rather than reading a caption that says it did. An embed that asks for nothing gets nothing — the wire-cheapest document is the safest one."
    [ framesPanel; gatePanel; wirePanel; notAMountPanel ]
    claims
