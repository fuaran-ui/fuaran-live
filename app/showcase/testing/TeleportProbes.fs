module Fuaran.Showcase.Testing.TeleportProbes

// ============================================================================
//  TEST-ONLY. The teleport page's headless receive and tamper reports.
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Showcase.Teleport


/// The receive path, headless: decode a bundle exactly as this page's hosts
/// do and report what it mounts. `test/teleportReceiver.test.ts` certifies
/// the decision against real bundles – this page's own, and a foreign host's.
let mountReport (encoded: string) : obj =
  match Teleport.decode encoded with
  | Ok d ->
    let m = mountOf d

    createObj
      [ "ok" ==> true
        "digest" ==> m.Digest
        "foreign" ==> m.Foreign
        "mountedTreeJson" ==> CanonicalJson.encodeNode m.Tree ]
  | Error e -> createObj [ "ok" ==> false; "error" ==> errorText e ]

/// A foreign app: a tree this page never authors, standing in for whatever a
/// different conformant host encodes and sends here.
let private foreignSample: Node<obj> =
  Fuaran.card
    "guest-app"
    { Defaults.card with
        Heading = Some(TextSource.Literal "Arrived from another host")
        Children = [ Fuaran.markdown "guest-line" "This tree was built somewhere else." ] }

/// Bundles for the receiver lock: this page's own exemplar app, and the
/// foreign app above, both encoded by the one codec the wire format defines.
let sampleBundles: obj =
  let orEmpty (r: Result<string, TeleportError>) =
    match r with
    | Ok s -> s
    | Error _ -> ""

  createObj
    [ "exemplar" ==> orEmpty (encodeWizard seed)
      "exemplarTreeJson" ==> CanonicalJson.encodeNode (exemplarTree seed)
      "foreign" ==> orEmpty (Teleport.encode (TeleportBundle.ofTree foreignSample))
      "foreignTreeJson" ==> CanonicalJson.encodeNode foreignSample ]

/// The tamper vignette, headless: stage the one-byte flip and report what the
/// decoder did with the result. Locked by `test/teleportReceiver.test.ts` over
/// BOTH staging paths – the exemplar's readable heading swap and the node-id
/// flip any other host's app takes – because a vignette that cannot stage, or
/// that trips a parse error instead of the digest check, quietly stops making
/// the claim it is on the page to make.
let tamperReport (encoded: string) : obj =
  match tamperOneByte encoded with
  | Ok(what, bad) ->
    match Teleport.decode bad with
    | Ok _ -> createObj [ "staged" ==> true; "what" ==> what; "refused" ==> false ]
    | Error e ->
      createObj
        [ "staged" ==> true
          "what" ==> what
          "refused" ==> true
          "digestMismatch"
          ==> (match e with
               | TeleportError.DigestMismatch _ -> true
               | _ -> false)
          "refusal" ==> tamperRefusal e ]
  | Error why -> createObj [ "staged" ==> false; "why" ==> why ]
