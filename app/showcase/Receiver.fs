module Fuaran.Showcase.Receiver

// ============================================================================
//  HOST 2 – the bare receiver (/receiver.html).
//
//  A second, deliberately vacant document: no site shell, no navigation, no
//  application – just the player (the Fuaran renderer) and a waiting prompt.
//  The Teleport page's QR / copy-link / hop all point here, so what a visitor
//  watches is an app materializing on a host that visibly had nothing on it:
//  the bytes in the URL fragment (never sent to any server) decode,
//  digest-verify, and become the running app. Same-origin today; the document
//  is self-contained so deploying it to a second domain is a hosting decision,
//  not a build change.
// ============================================================================

open Fable.Core.JsInterop
open Elmish
open Elmish.React
open Feliz
open Fuaran.UI
open Fuaran.UI.Renderer
open Fuaran.Showcase

// Same stylesheet pair as the site shell: the canonical F# reference CSS (the
// player's class vocabulary) + the site chrome (which carries the rcv-* theme).
importSideEffects "@fuaran-ui/renderer/css"
// The shared brand design-system layer — after the reference CSS, before the
// shell CSS (same contract as the playground + showcase entries).
importSideEffects "../brand/fuaran-brand.css"
importSideEffects "./app.css"
// The icon-contract glyph map, shared with the playground (one file, no copies).
importSideEffects "../icon-glyphs.css"

let private view () (_: unit -> unit) : ReactElement =
  React.Fragment
    [ Render.themeStyleElement Defaults.theme
      Html.div [ prop.className "rcv-shell"; prop.children [ Teleport.receiverPage ] ] ]

Program.mkSimple (fun () -> ()) (fun () () -> ()) view
|> Program.withReactSynchronous "fuaran-receiver-root"
|> Program.run
