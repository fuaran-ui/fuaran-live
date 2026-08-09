module Fuaran.Showcase.Teleport

// ============================================================================
//  Teleport – the app is a value. Pillar: "the app is a value".
//
//  A small stateful signup wizard whose live state is unmistakable – rendered
//  beside a LIVE BOARDING PASS: the whole app (typed tree + state) re-encoded
//  on every edit into one deflate+base64url, digest-signed string, shown as a
//  QR with its true byte count. The QR is not a link to the app; it IS the
//  app, and it visibly reshapes as you type.
//
//  The bundle rides the URL FRAGMENT (never the query string), so it is never
//  transmitted to any server – the static host only ever serves the player.
//  Scanning the QR / opening the link / pasting the raw string all decode,
//  digest-verify, and resume the exact mid-interaction moment. A same-origin
//  hop window can carry edits BACK to the original window (round trip), a
//  "flip one byte" vignette shows the decoder refusing a tampered bundle with
//  the real typed error, and a localStorage save covers close-the-tab-and-
//  come-back-tomorrow. No relay server anywhere.
//
//  The honesty of the claim (footer): the bytes are a genuine `Teleport.encode`
//  of a genuine Fuaran `Node` tree + `Binding.State` map – the same canonical
//  codec, digest-verified on arrival. Closures never ride the wire (only
//  wire-survivable actions resume live); "continue on your phone" here is not
//  a sync service, it falls out of the type system.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

// ─── The exemplar wizard state ──────────────────────────────────────────────

type private Wizard =
  { Step: int
    Name: string
    Email: string
    Plan: string
    Referral: string
    TeamSize: string }

let private stepTitles = [| "Your details"; "Pick a plan"; "Last details" |]
let private stepCount = stepTitles.Length
let private plans = [ "Starter"; "Pro"; "Team" ]

let private referralOptions =
  [ "A friend or colleague"; "Web search"; "A conference talk"; "Social media" ]

let private teamSizeOptions = [ "Just me"; "2–10"; "11–50"; "51+" ]

/// Starts on step 1 with the details pre-filled – there is visible state to
/// carry from the very first frame, without dropping the visitor mid-flow.
let private seed: Wizard =
  { Step = 0
    Name = "Ada Lovelace"
    Email = "ada@analytical.engine"
    Plan = ""
    Referral = ""
    TeamSize = "" }

let private clampStep (i: int) : int = max 0 (min (stepCount - 1) i)

// ─── Interop – URL, localStorage, clipboard, windows, QR ────────────────────

// (The receiver origin is resolved by teleport-qr.ts – localStorage dev
// override → build-time VITE_RECEIVER_ORIGIN → this page's own origin.)

[<Emit("window.localStorage.getItem($0)")>]
let private lsGet (key: string) : string = jsNative

[<Emit("window.localStorage.setItem($0, $1)")>]
let private lsSet (key: string) (value: string) : unit = jsNative

[<Emit("window.localStorage.removeItem($0)")>]
let private lsRemove (key: string) : unit = jsNative

[<Emit("navigator.clipboard && navigator.clipboard.writeText($0)")>]
let private clipboardWrite (text: string) : unit = jsNative

// replaceState (not location.hash assignment) – updates the address bar
// without pushing history entries or firing hashchange, so the live pass can
// mirror into the URL without re-triggering the receive path.
[<Emit("window.history.replaceState(null, \"\", $0)")>]
let private replaceUrl (hash: string) : unit = jsNative

// The site page hops into a NAMED window ("fuaran-teleport-hop"): repeated
// hops re-navigate the same popup (its hashchange listener receives each new
// bundle) instead of spawning windows. The receiver hops onward with "_blank"
// – a named window would match ITSELF and self-navigate.
[<Emit("window.open($0, $1)")>]
let private windowOpen (url: string) (target: string) : unit = jsNative

[<Emit("window.close()")>]
let private closeSelf () : unit = jsNative

/// A GIF data-URL QR of `text`, or "" when the text overflows a single QR.
let private qrDataUrl (text: string) (cell: int) : string = import "qrDataUrl" "./teleport-qr.ts"

let private hasOpener () : bool = import "hasOpener" "./teleport-qr.ts"

let private hopBack (hash: string) (encoded: string) : bool = import "hopBack" "./teleport-qr.ts"

/// Where bundles land (two-origin mode): localStorage dev override
/// ("fuaran-receiver-origin") → build-time VITE_RECEIVER_ORIGIN → own origin.
let private receiverOrigin () : string =
  import "receiverOrigin" "./teleport-qr.ts"

let private savedKey = "fuaran-teleport-save"

// ─── The fragment carries the app ───────────────────────────────────────────
//  Everything before the '#' is sent to the server in the HTTP request; the
//  fragment never is. Riding the bundle on the fragment makes "nothing was
//  uploaded" a checkable fact, not a policy claim.
//
//  Two fragment shapes exist: the site page's `#/demo/teleport?t=FT1…` (the
//  hash router owns the path part) and the bare receiver's `#t=FT1…` (no
//  router – the whole fragment is payload). The reader tolerates both, so a
//  bundle can land on either page whichever way it travelled.

let private teleportHash (encoded: string) : string = "#/demo/teleport?t=" + encoded

let private receiverHash (encoded: string) : string = "#t=" + encoded

/// The share target – QR, copy-link, and hop all point at the BARE RECEIVER:
/// a visibly vacant second document (possibly on a different origin – the
/// two-origin mode) that materializes whatever arrives.
let private receiverUrlOf (encoded: string) : string =
  receiverOrigin () + "/receiver.html" + receiverHash encoded

/// Cut a payload at the first `&` – the bundle itself is base64url (no `&`),
/// so anything after one is a further fragment param, not app bytes.
let private untilAmp (s: string) : string =
  let j = s.IndexOf '&'
  if j >= 0 then s.Substring(0, j) else s

/// Read a bundle out of the current fragment, whichever shape carried it.
let private tryReadPayload () : string option =
  let h: string = Browser.Dom.window.location.hash
  let marker = "t=" + Teleport.FormatPrefix
  let i = h.IndexOf marker
  if i >= 0 then Some(untilAmp (h.Substring(i + 2))) else None

/// Accept a pasted raw `FT1.…` string OR a whole teleport link (either shape).
let private extractPayload (raw: string) : string =
  let s = raw.Trim()
  let i = s.IndexOf("t=" + Teleport.FormatPrefix)
  if i >= 0 then untilAmp (s.Substring(i + 2)) else s

// ─── The exemplar app AS a Fuaran value ─────────────────────────────────────

let private fieldLine (label: string) (value: string) : string =
  let shown =
    if System.String.IsNullOrWhiteSpace value then
      "_(not yet entered)_"
    else
      value

  "**" + label + ":** " + shown

let private exemplarTree (w: Wizard) : Node<unit> =
  let progress =
    sprintf "_Step %d of %d – %s_" (w.Step + 1) stepCount stepTitles.[clampStep w.Step]

  Fuaran.card
    "tp-app"
    { Defaults.card with
        Heading = Some(TextSource.Literal "New account")
        Children =
          [ Fuaran.stepper
              "tp-steps"
              { Defaults.stepper<unit> with
                  ActiveStep = Binding.Static(Some(clampStep w.Step))
                  Children =
                    [ Fuaran.markdown "tp-s0" stepTitles.[0]
                      Fuaran.markdown "tp-s1" stepTitles.[1]
                      Fuaran.markdown "tp-s2" stepTitles.[2] ] }
            Fuaran.markdown "tp-progress" progress
            Fuaran.box
              "tp-fields"
              { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
                Role = BoxRole.Group
                Heading = None
                Children =
                  [ Fuaran.markdown "tp-f-name" (fieldLine "Name" w.Name)
                    Fuaran.markdown "tp-f-email" (fieldLine "Email" w.Email)
                    Fuaran.markdown "tp-f-plan" (fieldLine "Plan" w.Plan)
                    Fuaran.markdown "tp-f-referral" (fieldLine "Heard via" w.Referral)
                    Fuaran.markdown "tp-f-team" (fieldLine "Team size" w.TeamSize) ] } ] }

// ─── Wizard state ⇄ teleport bundle ─────────────────────────────────────────

let private captureWizard (w: Wizard) : Map<string, JVal> =
  Map
    [ "step", JInt w.Step
      "name", JStr w.Name
      "email", JStr w.Email
      "plan", JStr w.Plan
      "referral", JStr w.Referral
      "teamSize", JStr w.TeamSize ]

let private jstr (m: Map<string, JVal>) (k: string) : string =
  match Map.tryFind k m with
  | Some(JStr s) -> s
  | _ -> ""

let private jint (m: Map<string, JVal>) (k: string) : int =
  match Map.tryFind k m with
  | Some(JInt i) -> i
  | Some(JFloat f) -> int f
  | _ -> 0

let private wizardOfState (m: Map<string, JVal>) : Wizard =
  { Step = clampStep (jint m "step")
    Name = jstr m "name"
    Email = jstr m "email"
    Plan = jstr m "plan"
    Referral = jstr m "referral"
    TeamSize = jstr m "teamSize" }

let private encodeWizard (w: Wizard) : Result<string, TeleportError> =
  Teleport.encode
    { Tree = exemplarTree w
      State = captureWizard w
      History = []
      ChainHead = None }

let private errorText (e: TeleportError) : string =
  match e with
  | TeleportError.Oversize(_, msg) -> "Too large to teleport – " + msg
  | TeleportError.InvalidFormat msg -> "Not a teleport bundle – " + msg
  | TeleportError.InvalidJson msg -> "Corrupted payload – " + msg
  | TeleportError.InvalidEnvelope(path, msg) -> sprintf "Malformed bundle at %s – %s" path msg
  | TeleportError.UnsupportedVersion v -> "Unsupported bundle version: " + v
  | TeleportError.DigestMismatch _ -> "Integrity check failed – the bundle was altered in transit."
  | TeleportError.TreeDecode _ -> "The app tree failed to decode."
  | TeleportError.HistoryDecode(i, _) -> sprintf "History entry %d failed to decode." i
  | TeleportError.TreeInvalid _ -> "The decoded app has broken node identity."

let private sizeText (bytes: int) : string =
  if bytes < 1024 then
    sprintf "%d bytes" bytes
  else
    sprintf "%d bytes · %.1f KB" bytes (float bytes / 1024.0)

let private ticketStub (s: string) : string =
  if s.Length > 26 then s.Substring(0, 26) + "…" else s

// ─── The tamper vignette ────────────────────────────────────────────────────
//  Open the sealed envelope, flip ONE content byte (the digest field is left
//  untouched), seal it back up – exactly what corruption or tampering between
//  devices produces. The decoder recomputes the digest over the whole envelope
//  on arrival, so the altered bundle must refuse to resume.

let private tamperOneByte (encoded: string) : Result<string, string> =
  if not (encoded.StartsWith Teleport.FormatPrefix) then
    Error "not a teleport bundle"
  else
    Base64Url.decode (encoded.Substring Teleport.FormatPrefix.Length)
    |> Result.bind (fun compressed ->
      Deflate.inflate 1048576 compressed
      |> Result.mapError (fun _ -> "inflate failed"))
    |> Result.bind Utf8.decode
    |> Result.bind (fun envelope ->
      if envelope.Contains "New account" then
        let tampered = envelope.Replace("New account", "New acc0unt")

        Ok(
          Teleport.FormatPrefix
          + Base64Url.encode (Deflate.compress (Utf8.encode tampered))
        )
      else
        Error "expected content not found in the envelope")

let private tamperRefusal (e: TeleportError) : string =
  match e with
  | TeleportError.DigestMismatch(recomputed, carried) ->
    sprintf
      "TeleportError.DigestMismatch – carried %s…, recomputed %s… → refused to resume"
      (carried.Substring(0, 8))
      (recomputed.Substring(0, 8))
  | other -> errorText other

// ─── Live pass + arrival state ──────────────────────────────────────────────

type private Pass =
  { Encoded: string
    Url: string
    Qr: string
    TreeJson: string }

[<RequireQualifiedAccess>]
type private Arrival =
  | None
  | Received of digest: string * bytes: int
  | Failed of string

// ─── The page (a Feliz function component with its own hooks) ────────────────
//  One component, two hosts: `bare = false` is the site's Teleport page;
//  `bare = true` is the RECEIVER (/receiver.html) – a visibly vacant second
//  document that shows nothing but a waiting prompt until a bundle lands, then
//  materializes the app. Same player, different world.

[<ReactComponent>]
let private TeleportView (bare: bool) : ReactElement =
  let wizard, setWizard = React.useState seed
  let pass, setPass = React.useState (None: Result<Pass, string> option)
  let arrival, setArrival = React.useState Arrival.None
  let arrivalKey, setArrivalKey = React.useState 0
  // The bare receiver stays visibly vacant – no pass, no URL mirror, no app –
  // until the first bundle lands.
  let arrivedLive, setArrivedLive = React.useState (not bare)
  let savedExists, setSavedExists = React.useState false
  let copied, setCopied = React.useState (None: string option)
  let tamper, setTamper = React.useState (None: (string * string) option)
  let pasteText, setPasteText = React.useState ""

  // Stable refs – safe inside the mount-time hashchange closure.
  // `lastWrittenHash` mirrors the fragment this window's CURRENT state
  // encodes to (the live pass keeps it fresh); an incoming bundle is worth
  // receiving exactly when it differs from that.
  let lastWrittenHash = React.useRef ""
  let arrivalCounter = React.useRef 0

  // Receive a bundle (fragment on load, a hashchange – a hop landing or a
  // hop-back – or a pasted string): decode → digest-verify → resume. The
  // arrival key remounts the live pane so the materialize animation replays.
  let receive (encoded: string) : unit =
    match Teleport.decode encoded with
    | Ok d ->
      setWizard (wizardOfState d.State)
      setArrival (Arrival.Received(d.Digest, encoded.Length))
      setArrivedLive true
      // Deterministic encode: the resumed state re-encodes to exactly
      // this bundle, so record it as already-written up front (the live
      // pass would land on the same value 350ms later anyway).
      lastWrittenHash.current <- (if bare then receiverHash encoded else teleportHash encoded)
      arrivalCounter.current <- arrivalCounter.current + 1
      setArrivalKey arrivalCounter.current
    | Error e -> setArrival (Arrival.Failed(errorText e))

  // Receive only what this window's current state does NOT already encode –
  // a real landing, not our own echo. Compares in this host's own fragment
  // shape (the shape `lastWrittenHash` records).
  let receiveIfNew (enc: string) : unit =
    let selfShape = if bare then receiverHash enc else teleportHash enc

    if selfShape <> lastWrittenHash.current then
      receive enc

  // Mount: resume from the fragment if a bundle rode in; otherwise offer any
  // saved session (site page only – a vacant host has nothing saved on it).
  // Then keep listening on two return channels: a same-origin hop-back
  // rewrites this window's fragment (hashchange IS the landing); a
  // cross-origin hop-back arrives as a postMessage instead (a cross-origin
  // popup cannot touch this window's fragment). The message's shape is
  // checked loosely on purpose – the digest verification inside decode is
  // the real gate, exactly as for a scanned QR or a pasted string.
  React.useEffect (
    (fun () ->
      (match tryReadPayload () with
       | Some enc -> receive enc
       | None ->
         if not bare then
           let s = lsGet savedKey

           if not (System.String.IsNullOrEmpty s) then
             setSavedExists true)

      let hashHandler =
        fun (_: Browser.Types.Event) ->
          match tryReadPayload () with
          | Some enc -> receiveIfNew enc
          | None -> ()

      let messageHandler =
        fun (e: Browser.Types.Event) ->
          let data: obj = e?data
          let msgType: string = !!data?``type``
          let enc: string = !!data?encoded

          if msgType = "fuaran-teleport-bundle" && not (System.String.IsNullOrEmpty enc) then
            receiveIfNew enc

      Browser.Dom.window.addEventListener ("hashchange", hashHandler)
      Browser.Dom.window.addEventListener ("message", messageHandler)

      { new System.IDisposable with
          member _.Dispose() =
            Browser.Dom.window.removeEventListener ("hashchange", hashHandler)
            Browser.Dom.window.removeEventListener ("message", messageHandler) }),
    [||]
  )

  // The LIVE boarding pass: re-encode the whole app on every edit (debounced)
  // and mirror it into the URL fragment via replaceState – the address bar
  // always carries the current app, so refresh / bookmark / copy all capture
  // this exact moment. Deterministic encode ⇒ an untouched arrival re-writes
  // the identical string. The share surfaces (QR / copy / hop) point at the
  // bare receiver. A vacant receiver runs no pass at all until a bundle lands
  // (its URL must stay visibly empty – the vacancy is the claim).
  React.useEffect (
    (fun () ->
      let id =
        Browser.Dom.window.setTimeout (
          (fun () ->
            if arrivedLive then
              match encodeWizard wizard with
              | Ok enc ->
                let url = receiverUrlOf enc

                setPass (
                  Some(
                    Ok
                      { Encoded = enc
                        Url = url
                        Qr = qrDataUrl url 4
                        TreeJson = CanonicalJson.encodeNode (exemplarTree wizard) }
                  )
                )

                let h = if bare then receiverHash enc else teleportHash enc

                if h <> lastWrittenHash.current then
                  lastWrittenHash.current <- h
                  replaceUrl h
              | Error e -> setPass (Some(Error(errorText e)))),
          350
        )

      { new System.IDisposable with
          member _.Dispose() = Browser.Dom.window.clearTimeout id }),
    [| box wizard; box arrivedLive |]
  )

  let update (w: Wizard) : unit =
    setWizard w
    setCopied None
    setTamper None

  let saveForLater () : unit =
    match encodeWizard wizard with
    | Ok enc ->
      lsSet savedKey enc
      setSavedExists true
    | Error _ -> ()

  let resumeSaved () : unit =
    let s = lsGet savedKey

    if not (System.String.IsNullOrEmpty s) then
      receive s
      setSavedExists false

  let discardSaved () : unit =
    lsRemove savedKey
    setSavedExists false

  let hop () : unit =
    match pass with
    | Some(Ok p) -> windowOpen p.Url (if bare then "_blank" else "fuaran-teleport-hop")
    | _ -> ()

  // Round trip: hand THIS window's current bundle (edits included) back to
  // the opener and close – the app visibly leaves this window. Same-origin
  // openers get a fragment rewrite (the site-page shape; every reader
  // tolerates it); cross-origin openers get the bundle via postMessage.
  let hopItBack () : unit =
    match pass with
    | Some(Ok p) ->
      if hopBack (teleportHash p.Encoded) p.Encoded then
        closeSelf ()
    | _ -> ()

  let runTamper () : unit =
    match tamper with
    | Some _ -> setTamper None
    | None ->
      match pass with
      | Some(Ok p) ->
        match tamperOneByte p.Encoded with
        | Ok bad ->
          let refusal =
            match Teleport.decode bad with
            | Ok _ -> "the decoder accepted it – this should never happen"
            | Error e -> tamperRefusal e

          setTamper (Some("flipped one byte inside the sealed envelope (“account” → “acc0unt”)", refusal))
        | Error e -> setTamper (Some("couldn't stage the tamper", e))
      | _ -> ()

  // ── The live wizard chrome (React inputs) ──────────────────────────────

  let textField (label: string) (value: string) (onSet: string -> unit) (kind: string) : ReactElement =
    Html.label
      [ prop.className "tp-field"
        prop.children
          [ Html.span [ prop.className "tp-field-label"; prop.text label ]
            Html.input
              [ prop.className "tp-input"
                prop.type' kind
                prop.value value
                prop.onChange (fun (v: string) -> onSet v) ] ] ]

  let stepBody =
    match clampStep wizard.Step with
    | 0 ->
      Html.div
        [ prop.className "tp-step-body"
          prop.children
            [ textField "Name" wizard.Name (fun v -> update { wizard with Name = v }) "text"
              textField "Email" wizard.Email (fun v -> update { wizard with Email = v }) "email" ] ]
    | 1 ->
      Html.div
        [ prop.className "tp-step-body"
          prop.children
            [ Html.span [ prop.className "tp-field-label"; prop.text "Plan" ]
              Html.div
                [ prop.className "tp-plan-row"
                  prop.children
                    [ for p in plans ->
                        Html.button
                          [ prop.className (if wizard.Plan = p then "tp-plan tp-plan-on" else "tp-plan")
                            prop.text p
                            prop.onClick (fun _ -> update { wizard with Plan = p }) ] ] ] ] ]
    | _ ->
      let selectField (label: string) (value: string) (options: string list) (onSet: string -> unit) =
        Html.label
          [ prop.className "tp-field"
            prop.children
              [ Html.span [ prop.className "tp-field-label"; prop.text label ]
                Html.select
                  [ prop.className "tp-input tp-select"
                    prop.value value
                    prop.onChange (fun (v: string) -> onSet v)
                    // Explicit cons – mixing a bare item with a `for`
                    // in one implicit-yield list silently drops the item.
                    prop.children (
                      Html.option [ prop.value ""; prop.text "Choose…" ]
                      :: [ for o in options -> Html.option [ prop.value o; prop.text o ] ]
                    ) ] ] ]

      Html.div
        [ prop.className "tp-step-body"
          prop.children
            [ selectField "How did you hear about us?" wizard.Referral referralOptions (fun v ->
                update { wizard with Referral = v })
              selectField "Team size" wizard.TeamSize teamSizeOptions (fun v -> update { wizard with TeamSize = v }) ] ]

  let stepNav =
    Html.div
      [ prop.className "tp-step-nav"
        prop.children
          [ Html.button
              [ prop.className "tp-nav-btn"
                prop.disabled (wizard.Step <= 0)
                prop.text "← Back"
                prop.onClick (fun _ ->
                  update
                    { wizard with
                        Step = clampStep (wizard.Step - 1) }) ]
            Html.span
              [ prop.className "tp-step-count"
                prop.text (sprintf "Step %d of %d" (clampStep wizard.Step + 1) stepCount) ]
            Html.button
              [ prop.className "tp-nav-btn"
                prop.disabled (wizard.Step >= stepCount - 1)
                prop.text "Next →"
                prop.onClick (fun _ ->
                  update
                    { wizard with
                        Step = clampStep (wizard.Step + 1) }) ] ] ]

  // Keyed by the arrival counter so a received bundle remounts the pane and
  // the materialize animation replays over the newly-resumed state.
  let livePane =
    Html.div
      [ prop.key ("tp-arrival-" + string arrivalKey)
        prop.className "tp-live tp-materialize"
        prop.children
          [ Html.div
              [ prop.className "tp-wizard"
                prop.children
                  [ Html.h3 [ prop.className "tp-pane-title"; prop.text stepTitles.[clampStep wizard.Step] ]
                    stepBody
                    stepNav ] ]
            Html.div
              [ prop.className "tp-value"
                prop.children
                  [ Html.h3 [ prop.className "tp-pane-title"; prop.text "Your app, as a value" ]
                    Html.div
                      [ prop.className "tp-render"
                        prop.children [ Render.renderWithSources BindingResolver.empty ignore (exemplarTree wizard) ] ] ] ] ] ]

  // ── The live boarding pass ──────────────────────────────────────────────

  let tamperStrip =
    match tamper with
    | Some(what, refusal) ->
      Html.div
        [ prop.className "tp-tamper"
          prop.children
            [ Html.p
                [ prop.className "tp-tamper-what"
                  prop.text ("In transit, we " + what + " – the digest was left untouched.") ]
              Html.code [ prop.className "tp-tamper-refusal"; prop.text refusal ]
              Html.p
                [ prop.className "tp-tamper-note"
                  prop.text
                    "One wrong byte, one dead bundle: the digest is recomputed over the whole envelope on arrival, so a corrupted or altered app refuses to resume rather than resuming something subtly wrong. Your pass above is untouched." ] ] ]
    | None -> Html.none

  let passView =
    Html.div
      [ prop.className "tp-pass"
        prop.children
          [ Html.div
              [ prop.className "tp-pass-head"
                prop.children
                  [ Html.span [ prop.text "Fuaran Teleport" ]
                    Html.span [ prop.text "live boarding pass" ] ] ]
            (match pass with
             | Some(Error err) ->
               Html.div
                 [ prop.className "tp-pass-body tp-pass-err"
                   prop.children [ Html.p [ prop.text err ] ] ]
             | Some(Ok p) ->
               let comfortable = p.Encoded.Length <= TeleportBudget.QrComfortableBytes

               Html.div
                 [ prop.className "tp-pass-body"
                   prop.children
                     [ (if p.Qr = "" then
                          Html.none
                        else
                          Html.img
                            [ prop.key p.Encoded
                              prop.className "tp-qr tp-qr-fresh"
                              prop.src p.Qr
                              // Hover shows exactly where the bundle lands
                              // (receiver-origin aware – two-origin mode).
                              prop.title p.Url
                              prop.alt "Teleport QR code – scan to carry this app to another device" ])
                       Html.div
                         [ prop.className "tp-pass-meta"
                           prop.children
                             [ Html.div
                                 [ prop.className "tp-size"
                                   prop.text ("Your app is " + sizeText p.Encoded.Length) ]
                               Html.div
                                 [ prop.className "tp-pass-caption"
                                   prop.text "re-encoded as you type – this QR is the app itself, not a link to it" ]
                               Html.code [ prop.className "tp-pass-stub"; prop.text (ticketStub p.Encoded) ]
                               Html.span
                                 [ prop.className (
                                     if p.Qr = "" then "tp-qr-note tp-qr-dense"
                                     elif comfortable then "tp-qr-note tp-qr-ok"
                                     else "tp-qr-note tp-qr-dense"
                                   )
                                   prop.text (
                                     if p.Qr = "" then
                                       "too large for one QR – use the link or the string below"
                                     elif comfortable then
                                       "scans cleanly"
                                     else
                                       "dense QR – hold the camera steady"
                                   ) ]
                               Html.div
                                 [ prop.className "tp-pass-actions"
                                   prop.children
                                     [ Html.button
                                         [ prop.className "tp-copy"
                                           prop.text (
                                             if copied = Some "link" then
                                               "Link copied ✓"
                                             else
                                               "Copy link"
                                           )
                                           prop.onClick (fun _ ->
                                             clipboardWrite p.Url
                                             setCopied (Some "link")) ]
                                       Html.button
                                         [ prop.className "tp-hop-btn"
                                           prop.text (
                                             if bare then
                                               "Hop it onward"
                                             else
                                               "No phone handy? Hop it to a bare host"
                                           )
                                           prop.onClick (fun _ -> hop ()) ]
                                       Html.button
                                         [ prop.className "tp-tamper-btn"
                                           prop.text (
                                             if Option.isSome tamper then
                                               "Hide the tamper"
                                             else
                                               "Flip one byte"
                                           )
                                           prop.onClick (fun _ -> runTamper ()) ]
                                       (if bare then
                                          Html.none
                                        else
                                          Html.button
                                            [ prop.className "tp-save-btn"
                                              prop.text "Save for later"
                                              prop.onClick (fun _ -> saveForLater ()) ]) ] ] ] ] ] ]
             | None ->
               Html.div
                 [ prop.className "tp-pass-body"
                   prop.children [ Html.p [ prop.className "tp-pass-caption"; prop.text "encoding…" ] ] ])
            tamperStrip ] ]

  // ── The app as a string (copy / paste-to-materialize) ──────────────────

  // Shared between the string drawer and the receiver's vacant screen.
  let pasteRow =
    Html.div
      [ prop.className "tp-paste-row"
        prop.children
          [ Html.textarea
              [ prop.className "tp-string-box"
                prop.placeholder "Paste a teleported app here (an FT1.… string, or a whole link)…"
                prop.value pasteText
                prop.rows 3
                prop.onChange (fun (v: string) -> setPasteText v) ]
            Html.button
              [ prop.className "tp-materialize-btn"
                prop.disabled (pasteText.Trim() = "")
                prop.text "Materialize"
                prop.onClick (fun _ ->
                  receive (extractPayload pasteText)
                  setPasteText "") ] ] ]

  let stringDrawer =
    Html.details
      [ prop.className "tp-string-drawer"
        prop.children
          [ Html.summary [ prop.text "The app as a string – no page, no URL, just the value" ]
            Html.p
              [ prop.className "tp-string-note"
                prop.text
                  "This is the entire application. Keep it in a text file for a year, send it over any channel that carries text, then paste it back below – or into any conformant host." ]
            (match pass with
             | Some(Ok p) ->
               Html.div
                 [ prop.className "tp-string-row"
                   prop.children
                     [ Html.textarea
                         [ prop.className "tp-string-box"
                           prop.readOnly true
                           prop.value p.Encoded
                           prop.rows 3 ]
                       Html.button
                         [ prop.className "tp-copy"
                           prop.text (
                             if copied = Some "string" then
                               "Copied ✓"
                             else
                               "Copy the app"
                           )
                           prop.onClick (fun _ ->
                             clipboardWrite p.Encoded
                             setCopied (Some "string")) ] ] ]
             | _ -> Html.none)
            pasteRow ] ]

  // ── Banners: arrival + resume-after-death ──────────────────────────────

  let arrivalBanner =
    match arrival with
    | Arrival.Received(digest, bytes) ->
      Html.div
        [ prop.className "tp-banner tp-banner-ok"
          prop.children
            [ Html.strong [ prop.text ("⚡ This app just materialized from " + sizeText bytes + ".") ]
              Html.span
                [ prop.text (
                    sprintf
                      " Integrity verified – digest %s…. Same step, same fields; no account, no session, no upload."
                      (digest.Substring(0, 8))
                  ) ]
              (if hasOpener () then
                 Html.button
                   [ prop.className "tp-mini-btn"
                     prop.text "Edit something, then hop it back ↩"
                     prop.onClick (fun _ -> hopItBack ()) ]
               else
                 Html.none) ] ]
    | Arrival.Failed msg ->
      Html.div
        [ prop.className "tp-banner tp-banner-err"
          prop.children [ Html.strong [ prop.text "Couldn't resume: " ]; Html.span [ prop.text msg ] ] ]
    | Arrival.None -> Html.none

  let savedBanner =
    if savedExists then
      Html.div
        [ prop.className "tp-banner tp-banner-save"
          prop.children
            [ Html.span [ prop.text "You have a saved session on this device." ]
              Html.button
                [ prop.className "tp-mini-btn"
                  prop.text "Resume"
                  prop.onClick (fun _ -> resumeSaved ()) ]
              Html.button
                [ prop.className "tp-mini-btn tp-mini-ghost"
                  prop.text "Discard"
                  prop.onClick (fun _ -> discardSaved ()) ] ] ]
    else
      Html.none

  // ── Honesty footer ─────────────────────────────────────────────────────

  let honesty =
    Html.div
      [ prop.className "tp-honesty"
        prop.children
          [ Html.h3 [ prop.text "What actually travels" ]
            Html.ul
              [ prop.children
                  [ (if bare then
                       Html.li
                         [ prop.text
                             "What this page shipped with: a player – the same Fuaran renderer every host runs – plus this demo's wizard controls. What it did NOT ship with is an application: the app you see, its tree and its state, arrived as the bytes in this page's URL fragment, digest-verified on the way in." ]
                     else
                       Html.none)
                    Html.li
                      [ prop.text
                          "The QR, the link, and the string are the same few hundred bytes: the whole app – its typed tree and live state – deflate-compressed and digest-signed. Not a pointer to a session; there is no session, anywhere." ]
                    Html.li
                      [ prop.text
                          "The bundle rides the URL fragment, which never leaves your browser – open the network tab and watch: the server only ever serves the player, and has no database to lose. If every server behind this site were wiped tonight, this QR would still resume tomorrow. (Refresh this page – you won't lose your place; the URL is carrying the app right now.)" ]
                    Html.li
                      [ prop.text
                          "Integrity is checked, not promised – flip one byte (try it on the pass) and the bundle refuses to resume rather than resuming something subtly wrong." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "Closures can't ride the wire – by design. Only declarative, wire-survivable actions live again after a hop, and every one still passes the host's "
                            Html.a
                              [ prop.href (if bare then "/#/pillar/machine" else "#/pillar/machine")
                                prop.text "default-deny gate" ]
                            Html.text
                              " on the way in. \"Continue on your phone\" isn't a feature here; it falls out of treating the app as data." ] ] ] ] ] ]

  let mainAssembly =
    Html.div
      [ prop.className (if bare then "tp-page rcv-page" else "tp-page")
        prop.children
          [ (if bare then
               Html.div [ prop.className "rcv-badge"; prop.text "HOST 2 – an app materialized here" ]
             else
               Html.h1 [ prop.className "tp-title"; prop.text "Teleport" ])
            (if bare then
               Html.none
             else
               Html.p
                 [ prop.className "tp-lede"
                   prop.text
                     "Fill in this app on your laptop. Scan the boarding pass on your phone – same step, mid-interaction. The QR isn't a link to the app; it is the app, re-encoded live as you type." ])
            arrivalBanner
            savedBanner
            livePane
            passView
            stringDrawer
            (match pass with
             | Some(Ok p) ->
               Html.details
                 [ prop.className "tp-wire-drawer"
                   prop.children
                     [ Html.summary [ prop.text "What's inside – the app tree, human-readable" ]
                       Html.pre [ prop.className "tp-wire"; prop.text p.TreeJson ] ] ]
             | _ -> Html.none)
            honesty ] ]

  // ── The bare receiver's vacant screen ───────────────────────────────────
  //  What ships before any bytes arrive: a player, a waiting prompt, and an
  //  invitation to verify the vacancy. Nothing else – the emptiness IS the
  //  argument the receiver exists to stage.

  let vacant =
    Html.div
      [ prop.className "tp-page rcv-page"
        prop.children
          [ Html.div [ prop.className "rcv-badge"; prop.text "HOST 2 – VACANT" ]
            Html.h1 [ prop.className "rcv-title"; prop.text "Nothing is installed here." ]
            Html.p
              [ prop.className "rcv-line"
                prop.text
                  "This page ships a player – the Fuaran renderer – and no application. View source: there is no app on this host. When one arrives, it arrives as bytes." ]
            Html.p
              [ prop.className "rcv-waiting"
                prop.children [ Html.span [ prop.className "rcv-cursor" ]; Html.text " waiting for bytes…" ] ]
            arrivalBanner // a failed decode reports here even pre-arrival
            Html.p
              [ prop.className "rcv-hint"
                prop.text "Scan a boarding pass to land an app on this host – or paste one:" ]
            pasteRow ] ]

  if bare && not arrivedLive then vacant else mainAssembly

let page: ReactElement = TeleportView false

/// The bare receiver (/receiver.html) – same player, visibly vacant world.
let receiverPage: ReactElement = TeleportView true
