module Fuaran.Showcase.Replay

// ============================================================================
//  The scripted-replay loader seam – the site's reliability floor.
//
//  Every page must deliver its wow with ZERO setup and NO API key; BYOK live
//  modes are an upgrade, never a prerequisite. This is the single most important
//  site-wide rule. This module is the page-agnostic substrate for it: a page
//  asks for a recorded artefact by id and gets its payload back; how to
//  interpret it is the page's concern.
//
//  The shell ships the seam plus the simplest artefact – a single canonical wire
//  tree served from public/replays/<id>.json. Richer recordings (timed op-stream
//  frames, full scrubbable session playback) layer onto this same seam in a later
//  phase without changing the loader's shape.
// ============================================================================

open Fable.Core
open Elmish

/// A loaded artefact's lifecycle. `Missing` carries an honest reason – a page in
/// this state shows an absent/placeholder view, never a fabricated success.
[<RequireQualifiedAccess>]
type ReplayState =
  | Loading
  | Loaded of string
  | Missing of string

let artefactUrl (id: string) : string = sprintf "./replays/%s.json" id

// fetch(url) → text, entirely in JS so no promise-CE package is needed. A non-OK
// response or a network error routes to the error callback (reported Missing).
[<Emit("fetch($0).then(function(r){ if (!r.ok) throw new Error('HTTP ' + r.status); return r.text(); }).then($1).catch(function(e){ $2(String(e && e.message ? e.message : e)); })")>]
let private fetchInto (url: string) (onText: string -> unit) (onErr: string -> unit) : unit = jsNative

/// Load a recorded artefact by id as an Elmish command. The page maps the two
/// outcomes to its own messages; a page-agnostic loader means every page honours
/// the keyless-mode rule the same way.
let loadCmd (id: string) (onLoaded: string -> 'Msg) (onMissing: string -> 'Msg) : Cmd<'Msg> =
  Cmd.ofEffect (fun dispatch ->
    fetchInto (artefactUrl id) (onLoaded >> dispatch) (fun err ->
      dispatch (onMissing (sprintf "no recording available (%s)" err))))
