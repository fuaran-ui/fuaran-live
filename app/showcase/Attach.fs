module Fuaran.Showcase.Attach

// ============================================================================
//  The Attachment — four ways to hand over a file, and one that is refused
//  (Phases 1115 / 1116 / 1117). Pillar: "intent, not implementation".
//
//  A file picker is the control everyone has. What a document could not say
//  until this release is how a reader is ALLOWED to reach it, and the four
//  answers are four independent declarations rather than one "modern upload"
//  flag:
//
//   * `dropTarget` — a file dragged onto it is accepted.
//   * `acceptPaste` — a file on the clipboard, pasted, is accepted. This is the
//     other half of the clipboard boundary: an ACTION may never read the
//     clipboard, because that would be a keylogger-adjacent capability, but a
//     CONTROL may accept a paste, because a paste is user-initiated by
//     construction.
//   * `capture` — the reader's own camera or microphone, named. Two cases and
//     no third; it is not a "no device" enum case, because an upload that names
//     no device is asking for the picker, which is a different statement from
//     asking for a device and being unable to say which.
//   * `destination` — where the selected files GO. Absent, the selection never
//     leaves the client, which is the pre-1117 control and the wire identity.
//
//  ALL FOUR ARE OFF BY DEFAULT, and every one of those defaults is the wire
//  identity: an upload authored before any of this encodes to exactly the bytes
//  it always did, and the shortest document is the plain picker. Turning a
//  gesture on is the thing an emitter has to ask for.
//
//  THE DESTINATION LEG IS HONESTLY REFUSED HERE, and that is the exhibit rather
//  than a gap. A destination is a NAME; the bytes are moved by a sink the HOST
//  registers, and this site has no host — it is static files on a CDN with no
//  server of any kind. So the declared-destination upload below reaches the
//  streaming shell, finds no sink, and refuses with the reason named. That is
//  the correct behaviour and it is worth seeing: a document naming a
//  destination no host serves must fail loudly, not silently pretend to upload.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

let private kPicked = "attach.picked"

/// Record what the reader chose, as data. The handler receives the selection and
/// returns an ACTION — so "the reader picked these" becomes a state write the
/// page can read, with no callback escaping into host code.
let private recordSelection (files: HostPrelude.FileSelection list) : Action<obj> =
  let describe (f: HostPrelude.FileSelection) =
    f.Name + " · " + string (f.Size / 1024L) + " KB · " + f.MimeType

  Action.SetState(kPicked, Some(JStr(files |> List.map describe |> String.concat "\n")), None)

let private upload
  (id: string)
  (label: string)
  (accept: string list)
  (drop: bool)
  (paste: bool)
  (capture: CaptureSource option)
  (destination: string option)
  : Node<obj> =
  Fuaran.fileUpload
    id
    { Defaults.fileUpload<obj> with
        Label = TextSource.Literal label
        Accept = accept
        Multiple = true
        OnSelect = Some recordSelection
        DropTarget = drop
        AcceptPaste = paste
        Capture = capture
        Destination = destination }

/// The plain picker — every declaration at its default. This is the control the
/// language always had, and its wire is byte-identical to what it always was.
let private plain =
  upload "at-plain" "Attach a file" [ ".pdf"; ".png"; ".jpg" ] false false None None

/// Drop and paste, together. Two independent declarations; a document may turn
/// on either without the other.
let private dropPaste =
  upload "at-droppaste" "Drop a file here, or paste one" [ ".pdf"; ".png"; ".jpg" ] true true None None

/// The camera. `capture` names a DEVICE, and the closed vocabulary has two cases
/// because there are two devices worth naming.
let private camera =
  upload "at-camera" "Photograph the damage" [ "image/*" ] false false (Some CaptureSource.Camera) None

let private microphone =
  upload "at-mic" "Record a spoken statement" [ "audio/*" ] false false (Some CaptureSource.Microphone) None

/// The declared destination. There is no sink on this origin, so this one
/// refuses with the reason named — which is the point of showing it.
let private streamed =
  upload "at-streamed" "Send the file to claims intake" [ ".pdf"; ".png" ] true false None (Some "claims-intake")

let private wirePlain: string = CJson.encodeNode plain
let private wireDropPaste: string = CJson.encodeNode dropPaste
let private wireStreamed: string = CJson.encodeNode streamed

// ─── the page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private AttachView () : ReactElement =
  StateStore.useStateKeys (Set.ofList [ kPicked ]) |> ignore

  let picked =
    match StateStore.get kPicked with
    | Some v -> unbox<string> v
    | None -> ""

  let control (heading: string) (note: string) (node: Node<obj>) =
    Html.div
      [ prop.className "at-card"
        prop.children
          [ Html.div [ prop.className "at-card-head"; prop.text heading ]
            Html.p [ prop.className "at-card-note"; prop.text note ]
            Exhibit.renderLive node ] ]

  let gesturesPanel =
    Exhibit.panel
      "Four declarations, four gestures"
      "Every control below is the same kind. What differs is which gestures the document says are allowed — and all four defaults are off, so the plainest document is the plain picker."
      [ Html.div
          [ prop.className "at-grid"
            prop.children
              [ control
                  "everything at its default"
                  "The picker the language always had. Its wire is byte-identical to what it was before any of this existed."
                  plain
                control
                  "dropTarget + acceptPaste"
                  "Drag a file onto it, or copy one and press paste while it has focus. Two independent slots — a document may turn on either alone."
                  dropPaste
                control
                  "capture: Camera"
                  "On a phone this opens the camera directly rather than the file browser. On a desktop it is an ordinary picker, because there is nothing to open — the declaration is a request, not a promise."
                  camera
                control
                  "capture: Microphone"
                  "The second and last case. There is no third and no none-case: an upload that names no device is asking for the picker, which is a different statement from asking for a device and being unable to say which."
                  microphone ] ]
        Html.div
          [ prop.className "at-picked"
            prop.children
              [ Html.h4 [ prop.text "What the document recorded" ]
                (if picked = "" then
                   Html.p [ prop.className "at-picked-empty"; prop.text "Nothing chosen yet." ]
                 else
                   Html.pre [ prop.className "at-picked-list"; prop.text picked ])
                Html.p
                  [ prop.className "at-picked-note"
                    prop.text
                      "The selection handler returns an ACTION — a state write — so what the reader chose becomes data the document can read rather than a callback disappearing into host code. Nothing is read, uploaded or transmitted: only the name, size and type the browser reports." ] ] ] ]

  let destinationPanel =
    Exhibit.panel
      "A destination this host cannot serve — refused, with the reason"
      "This upload names a destination. A destination is a NAME; the bytes are moved by a sink the HOST registers, and this site has no host of any kind. Choose a file and watch it refuse rather than pretend."
      [ control
          "destination: claims-intake"
          "Reaching the streaming shell with no sink registered produces a named refusal. A document naming a destination nobody serves has to fail loudly — a silent success would be the one genuinely dangerous outcome."
          streamed ]

  let point (text: string) = Html.li [ prop.text text ]

  let designPanel =
    Exhibit.panel
      "Why four slots and not one flag"
      ""
      [ Html.ul
          [ prop.className "px-points"
            prop.children
              [ point
                  "A drop target and a paste target are different permissions over different surfaces, and plenty of real forms want one without the other. One \"modern upload\" flag would have made them inseparable forever."
                point
                  "Paste is the other half of the clipboard boundary. An ACTION may never read the clipboard — that is keylogger-adjacent — but a CONTROL may accept a paste, because a paste is user-initiated by construction. The asymmetry is the rule, not an inconsistency."
                point
                  "Capture names a device, and the request is honest about being a request: on a surface with no camera it is an ordinary picker, because a declaration that silently failed would be worse than one that gracefully does nothing."
                point
                  "Destination is a name and never a URL. The document says where the file belongs; the host decides whether it serves that name, what endpoint it means, and what credentials it uses — none of which belongs in a tree that may have been written by a model." ] ]
        Exhibit.wireDrawer "The plain picker — every default, so nothing but the essentials" wirePlain
        Exhibit.wireDrawer "Drop and paste — two keys more" wireDropPaste
        Exhibit.wireDrawer "The streamed one — a destination NAME, not an endpoint" wireStreamed ]

  Exhibit.shell
    "attach"
    "The Attachment"
    "The same file control, four times, differing only in which gestures the document allows — plus one that names a destination this host does not serve, and says so instead of pretending."
    [ gesturesPanel; destinationPanel; designPanel ]
    [ Exhibit.Claim.Verified
        "Every control is the shipped renderer's own FileUpload rendering. Drop a file on the second one and it takes it; drop one on the first and it does not, because the declaration is absent."
      Exhibit.Claim.Verified
        "The recorded selection is a real state write produced by the control's own handler, showing the name, size and type the browser reported. No file is read, and no bytes leave your machine."
      Exhibit.Claim.Verified
        "The destination upload genuinely refuses, and names why. That is the shipped refusal path, not a message this page wrote."
      Exhibit.Claim.Limit
        "The capture declarations do nothing visible on a desktop. They ask the platform for a device, and a browser with no camera to open shows an ordinary picker — which is the honest behaviour for a request rather than a promise. Open this page on a phone to see them bite."
      Exhibit.Claim.Limit
        "There is no working upload anywhere on this site, and there cannot be: it is static files with no server, and a sink is something a host registers. The refusal above is the whole of what a hostless page can honestly show of that half." ]

let page: ReactElement = AttachView()
