module Fuaran.Live.Testing.SiteViewProbes

// ============================================================================
//  TEST-ONLY. The site view's flat diagnostic surface (load/switch/move results, notices, page names).
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

module Introspect = Fuaran.UI.Ops.Introspect
module Decode = Fuaran.UI.Ops.JsonDecode
module BindingWalk = Fuaran.UI.BindingWalk

open Fuaran.Live
open Fuaran.Live.SiteView

// ─── flat diagnostic surface (cross-boundary friendly) ───────────────────────
//
// F# records, options and DUs are awkward to assert on from the JS side of the
// Fable boundary, so — exactly as the cursor / op-log / structural-edit helpers
// do — the same logic is projected to plain strings, arrays and flat records.
// `Site` and `SessionState` travel opaquely (the tests thread them back in);
// everything asserted on is flat.

let private noSite: Site =
  { Active = ""
    Names = []
    Shelf = Map.empty
    MoveStack = []
    Notice = None }

/// `loadPages` + `ofPages`, flattened: `Ok` false leaves `Site`/`Session` as
/// inert placeholders a test must not read.
let loadResult
  (bundleJson: string)
  : {| Ok: bool
       Error: string
       Site: Site
       Session: Session.SessionState |}
  =
  match loadPages bundleJson with
  | Ok pages ->
    let site, active = ofPages pages

    {| Ok = true
       Error = ""
       Site = site
       Session = active |}
  | Error message ->
    {| Ok = false
       Error = message
       Site = noSite
       Session = Session.empty |}

/// Every page name, in order.
let pageNames (site: Site) : string array = Array.ofList site.Names

/// The active page's name.
let activePage (site: Site) : string = site.Active

/// A SHELVED page's session (the active page's session is the live one the
/// caller already holds). `Session.empty` for an unknown/active name.
let shelfSessionOf (site: Site) (name: string) : Session.SessionState =
  site.Shelf |> Map.tryFind name |> Option.defaultValue Session.empty

/// `switchPage`, flattened. `Ok` false returns the inputs unchanged.
let switchResult
  (site: Site)
  (session: Session.SessionState)
  (name: string)
  : {| Ok: bool
       Site: Site
       Session: Session.SessionState |}
  =
  match switchPage site session name with
  | Some(next, active) ->
    {| Ok = true
       Site = next
       Session = active |}
  | None ->
    {| Ok = false
       Site = site
       Session = session |}

/// `movePage`, flattened, with the guard rail's keys surfaced as a plain array
/// (empty when the moved subtree reads no module state).
let moveResult
  (site: Site)
  (session: Session.SessionState)
  (nodeId: string)
  (page: string)
  : {| Ok: bool
       Error: string
       WarnKeys: string array
       Site: Site
       Session: Session.SessionState |}
  =
  match movePage site session (NodeId nodeId) page with
  | Ok(next, active) ->
    let warnKeys =
      match next.Notice with
      | Some(Notice.MovedWithStateReads(keys, _)) -> Array.ofList keys
      | _ -> [||]

    {| Ok = true
       Error = ""
       WarnKeys = warnKeys
       Site = next
       Session = active |}
  | Error message ->
    {| Ok = false
       Error = message
       WarnKeys = [||]
       // The same posture as `step`: a refusal lands in the notice, and no
       // session anywhere has changed.
       Site =
        { site with
            Notice = Some(Notice.Refused message) }
       Session = session |}

/// `undoMove`, flattened. `Ok` false returns the inputs unchanged.
let undoMoveResult
  (site: Site)
  (session: Session.SessionState)
  : {| Ok: bool
       Site: Site
       Session: Session.SessionState |}
  =
  match undoMove site session with
  | Some(next, active) ->
    {| Ok = true
       Site = next
       Session = active |}
  | None ->
    {| Ok = false
       Site = site
       Session = session |}

/// The notice's kind, as a plain tag (`""` when none) — the typed warning made
/// assertable by shape.
let noticeKind (site: Site) : string =
  match site.Notice with
  | None -> ""
  | Some(Notice.Info _) -> "info"
  | Some(Notice.Refused _) -> "refused"
  | Some(Notice.MovedWithStateReads _) -> "state-warning"

/// The notice's sentence (`""` when none).
let noticeLine (site: Site) : string =
  match site.Notice with
  | None -> ""
  | Some notice -> noticeText notice

/// The pre-move advisory, addressed by plain string: the `$state` keys the
/// subtree at `nodeId` reads.
let stateKeysAt (session: Session.SessionState) (nodeId: string) : string array =
  match session.Tree with
  | None -> [||]
  | Some root -> stateKeys root (NodeId nodeId) |> Array.ofList
