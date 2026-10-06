module Fuaran.Live.Interop

// ============================================================================
//  The small JavaScript interop helpers both entries of the site use – ONE
//  definition each. The playground compiles this file and the showcase links it
//  (as it links shared/Brand.fs), so a helper is fixed in one place rather than
//  in every page that happened to need it.
//
//  Nothing here holds a key or names a provider origin. The one network helper
//  reads a same-origin artefact as text; the playground's provider egress stays
//  in Byok.fs and is not reachable from here.
// ============================================================================

open Fable.Core

/// The top-level `$type` of a canonical op document – the op's kind, for
/// display. Anything unreadable reads as `op`.
[<Emit("(function(j){ try { var t = JSON.parse(j).$type; return (typeof t === 'string') ? t : 'op'; } catch(e){ return 'op'; } })($0)")>]
let opTypeOf (canonJson: string) : string = jsNative

/// Parse JSON tolerantly: the parsed value, or `null` when the text is not JSON.
[<Emit("(function(){ try { return JSON.parse($0); } catch(e){ return null; } })()")>]
let tryParseJson (text: string) : obj = jsNative

/// A member of a parsed JSON object, or `null` when the object itself is null.
[<Emit("($0 == null ? null : $0[$1])")>]
let field (o: obj) (key: string) : obj = jsNative

/// Fetch a URL as text. A non-OK status or a network error routes to `onErr`
/// with its message.
[<Emit("fetch($0).then(function(r){ if(!r.ok) throw new Error('HTTP '+r.status); return r.text(); }).then($1).catch(function(e){ $2(String(e&&e.message?e.message:e)); })")>]
let fetchText (url: string) (onText: string -> unit) (onErr: string -> unit) : unit = jsNative

/// `fetchText`, bypassing the HTTP cache – for an artefact a poll re-reads.
[<Emit("fetch($0, {cache:'no-store'}).then(function(r){ if(!r.ok) throw new Error('HTTP '+r.status); return r.text(); }).then($1).catch(function(e){ $2(String(e&&e.message?e.message:e)); })")>]
let fetchTextUncached (url: string) (onText: string -> unit) (onErr: string -> unit) : unit = jsNative

/// Write text to the clipboard the visitor's own click asked to fill. `false`
/// when the browser offers no clipboard or refuses the write synchronously.
[<Emit("(function(){ try { if (!navigator.clipboard) return false; navigator.clipboard.writeText($0); return true; } catch(e){ return false; } })()")>]
let writeClipboard (text: string) : bool = jsNative

/// Toggle the chrome-token class on <html>, so the page background outside the
/// max-width shell darkens too; rendered trees re-colour via the theme style.
[<Emit("(function(){ try { document.documentElement.classList.toggle('ds-dark', $0); } catch(e){} })()")>]
let applyDarkClass (dark: bool) : unit = jsNative

/// The fixed timestamp every hash chain on the site is written with, so a chain
/// is content-addressed – a pure function of prev-hash + sequence + actor + op.
/// Replaying the same ops reproduces the same hashes, which is what makes an
/// exported log checkable by whoever receives it; a wall-clock stamp would make
/// every replay disagree with the original. Verification hashes with this same
/// value – a verifier that supplied its own timestamp would check nothing.
let chainTimestamp =
  System.DateTimeOffset(2020, 1, 1, 0, 0, 0, System.TimeSpan.Zero)
