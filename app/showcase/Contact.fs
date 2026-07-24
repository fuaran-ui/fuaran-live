module Fuaran.Showcase.Contact

// ============================================================================
//  Contact – the last exhibit. Even reaching us is UI-as-data, client-side.
//
//  The form is a Fuaran tree: what you type renders live as a Fuaran card, and
//  "Show the wire" reveals your note as canonical Fuaran JSON. There is NO
//  backend – "Send" composes a `mailto:` in your browser and hands it to your
//  own mail client. The site never receives, stores, or transmits the message.
//  So contact closes the argument the demos open: UI-as-data, nothing uploaded.
// ============================================================================

open Fable.Core
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

[<Emit("encodeURIComponent($0)")>]
let private encodeUri (s: string) : string = jsNative

[<Emit("(function(){ try { navigator.clipboard.writeText($0); return true; } catch(e){ return false; } })()")>]
let private copyText (s: string) : bool = jsNative

/// Fire a URL (a `mailto:`) at click time, so no `mailto:` is ever written into
/// the page – that is the string email harvesters grep for first.
[<Emit("window.location.href = $0")>]
let private navigate (url: string) : unit = jsNative

// Anti-harvest: the address is assembled at runtime from split parts, so the
// contiguous string never appears as a literal in the JS bundle, and the mailto:
// is built on click (below) rather than baked into any href. Together with the
// SPA serving an empty document, that defeats the HTML-scrape, mailto:-regex and
// bundle-grep harvesters. (A JS-executing headless scraper can still read the
// rendered text – airtight would need a backend relay, which this page avoids.)
let private contactEmail = String.concat "@" [ "andrew"; "fuaran.com" ]

/// Hosted email-updates signup URL (a provider-run form: Buttondown / EmailOctopus /
/// a fuaran-ui.io page). The subscribe section renders ONLY when this is non-empty,
/// so shipping it blank is safe – set the URL here to turn it on. The list is stored
/// with the provider, never by this site, so the "nothing uploaded" promise holds.
let private subscribeUrl = ""

/// The visitor's note, as a Fuaran tree – the live preview + the wire source.
let private noteTree (name: string) (message: string) : Node<unit> =
  Fuaran.card
    "ct-note"
    { Defaults.card with
        Heading = Some(TextSource.Literal(if name.Trim() = "" then "Your message" else name.Trim()))
        Children =
          [ Fuaran.markdown
              "ct-note-body"
              (if message.Trim() = "" then
                 "_start typing your message…_"
               else
                 message) ] }

/// The `mailto:` – the whole delivery mechanism, composed client-side. The human
/// message leads; a one-line provenance note reinforces that it travelled as data.
let private buildMailto (name: string) (email: string) (message: string) : string =
  let signature =
    [ (if name.Trim() = "" then "" else "\n\n– " + name.Trim())
      (if email.Trim() = "" then "" else "  (" + email.Trim() + ")")
      "\n\n(sent from the Fuaran demo site – composed as UI-as-data in the browser; nothing was uploaded)" ]
    |> String.concat ""

  sprintf
    "mailto:%s?subject=%s&body=%s"
    contactEmail
    (encodeUri "Hello from the Fuaran demo site")
    (encodeUri (message + signature))

[<ReactComponent>]
let private ContactView () : ReactElement =
  let name, setName = React.useState ""
  let email, setEmail = React.useState ""
  let message, setMessage = React.useState ""
  let showWire, setShowWire = React.useState false
  let copied, setCopied = React.useState false

  let tree = noteTree name message
  let wire = CJson.encodeNode tree
  // The mailto: is built only inside click handlers (never in an href / the DOM).
  let sendMail () =
    navigate (buildMailto name email message)

  let field (label: string) (placeholder: string) (value: string) (onSet: string -> unit) : ReactElement =
    Html.label
      [ prop.className "ct-field"
        prop.children
          [ Html.span [ prop.className "ct-field-label"; prop.text label ]
            Html.input
              [ prop.className "ct-input"
                prop.type' "text"
                prop.placeholder placeholder
                prop.value value
                prop.onChange (fun (v: string) -> onSet v) ] ] ]

  Html.div
    [ prop.className "ct-page"
      prop.children
        [ Html.h1 [ prop.className "ct-title"; prop.text "Get in touch" ]
          Html.p
            [ prop.className "ct-lede"
              prop.text
                "Even this is UI-as-data. Type a note and watch it become a Fuaran value, live. There is no backend: when you send, your own mail client sends it – the site never sees your message, stores it, or transmits it." ]
          Html.div
            [ prop.className "ct-grid"
              prop.children
                [ // ── the form (Feliz inputs) ──
                  Html.div
                    [ prop.className "ct-form"
                      prop.children
                        [ field "Your name" "Ada Lovelace" name setName
                          field "Your email (optional)" "ada@example.com" email setEmail
                          Html.label
                            [ prop.className "ct-field"
                              prop.children
                                [ Html.span [ prop.className "ct-field-label"; prop.text "Message" ]
                                  Html.textarea
                                    [ prop.className "ct-input ct-textarea"
                                      prop.rows 5
                                      prop.placeholder "What's on your mind?"
                                      prop.value message
                                      prop.onChange (fun (v: string) -> setMessage v) ] ] ]
                          Html.div
                            [ prop.className "ct-actions"
                              prop.children
                                [ Html.button
                                    [ prop.className (
                                        if message.Trim() = "" then
                                          "ct-send ct-send-disabled"
                                        else
                                          "ct-send"
                                      )
                                      prop.disabled (message.Trim() = "")
                                      prop.onClick (fun _ ->
                                        if message.Trim() <> "" then
                                          sendMail ())
                                      prop.text "Send via your mail client →" ]
                                  Html.button
                                    [ prop.className "ct-copy"
                                      prop.text (if copied then "Copied ✓" else "Copy address")
                                      prop.onClick (fun _ -> setCopied (copyText contactEmail)) ] ] ]
                          Html.p
                            [ prop.className "ct-plain"
                              prop.children
                                [ Html.text "Or write to "
                                  Html.button
                                    [ prop.className "ct-email-link"
                                      prop.onClick (fun _ -> sendMail ())
                                      prop.text contactEmail ]
                                  Html.text " directly." ] ] ] ]
                  // ── the live Fuaran preview ──
                  Html.div
                    [ prop.className "ct-preview"
                      prop.children
                        [ Html.span [ prop.className "ct-preview-tag"; prop.text "Your note, as a Fuaran tree" ]
                          Render.renderWithSources BindingResolver.empty ignore tree
                          Html.button
                            [ prop.className "ct-wire-toggle"
                              prop.text (
                                if showWire then
                                  "Hide the wire"
                                else
                                  "Show the wire – your note is data"
                              )
                              prop.onClick (fun _ -> setShowWire (not showWire)) ]
                          (if showWire then
                             Html.pre
                               [ prop.className "ct-wire-json"
                                 prop.children [ Html.code [ prop.text wire ] ] ]
                           else
                             Html.none) ] ] ] ]
          (if subscribeUrl <> "" then
             Html.div
               [ prop.className "ct-subscribe"
                 prop.children
                   [ Html.h3 [ prop.className "ct-subscribe-title"; prop.text "Email updates" ]
                     Html.p
                       [ prop.className "ct-subscribe-note"
                         prop.text
                           "Want the occasional note when something ships? The list is run by a dedicated email provider – your address is stored with them, with one-click unsubscribe, and never with this site." ]
                     Html.a
                       [ prop.className "ct-subscribe-link"
                         prop.href subscribeUrl
                         prop.target "_blank"
                         prop.rel "noreferrer"
                         prop.text "Subscribe to updates →" ] ] ]
           else
             Html.none)
          Html.div
            [ prop.className "ct-honesty"
              prop.children
                [ Html.h3 [ prop.text "How honest is this?" ]
                  Html.ul
                    [ prop.children
                        [ Html.li
                            [ prop.text
                                "There is no server and no form endpoint. \"Send\" opens your own mail client with the message pre-filled – the site never receives, stores, or transmits what you type." ]
                          Html.li
                            [ prop.text
                                "The address is assembled in your browser and never written into the page as a link, so the link-scraping bots that harvest addresses come up empty." ]
                          Html.li
                            [ prop.text
                                "The preview is your message rendered live through the same Fuaran.UI.Renderer as every demo, and the wire above is its real canonical JSON – the note genuinely is a typed value." ]
                          Html.li
                            [ prop.children
                                [ Html.text "Contact, like everything here, is "
                                  Html.a [ prop.href "#/pillar/intent"; prop.text "UI-as-data" ]
                                  Html.text " – client-side, nothing uploaded." ] ] ] ] ] ] ] ]

let page: ReactElement = ContactView()
