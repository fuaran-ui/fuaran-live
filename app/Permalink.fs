module Fuaran.Live.Permalink

// ============================================================================
//  Shareable-creation permalinks (F# port of src/session/permalink.ts).
//
//  A creation is shared by encoding its canonical wire JSON into the URL
//  fragment (`#t=<base64>`) – client-only, nothing leaves the browser, the link
//  IS the data. Opening such a link restores the same folded tree a fresh
//  emission would (with inert closures by design). Encode/decode go through the
//  same `Fuaran.UI.Ops` / `CanonicalJson` codec the inspector + the loop trust.
// ============================================================================

open Fable.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

module Decode = Fuaran.UI.Ops.JsonDecode
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// UTF-8-safe base64 (btoa/atob are latin1-only; the escape/encodeURIComponent
// sandwich round-trips arbitrary text).
[<Emit("btoa(unescape(encodeURIComponent($0)))")>]
let private b64encode (s: string) : string = jsNative

[<Emit("decodeURIComponent(escape(atob($0)))")>]
let private b64decode (s: string) : string = jsNative

[<Emit("(window.location.origin + window.location.pathname)")>]
let private pageBase (unit: unit) : string = jsNative

[<Emit("(window.location.hash || '')")>]
let private currentHash (unit: unit) : string = jsNative

[<Emit("{ window.location.hash = $0; }")>]
let private writeHash (h: string) : unit = jsNative

[<Literal>]
let private prefix = "#t="

/// The canonical wire JSON of `tree`, base64-encoded for a URL fragment.
let encode (tree: Node<obj>) : string = b64encode (Canon.encodeNode tree)

/// Decode a fragment payload back to a tree, or `None` if it is not a valid
/// base64-encoded canonical wire document.
let decode (payload: string) : Node<obj> option =
  try
    match Decode.decodeNode (b64decode payload) with
    // `decodeNode` yields a `WireTree` (inert closures by design); reify to the
    // raw `Node<obj>` the permalink round-trip encodes + the preview renders.
    | Ok node -> Some(WireTree.reify node)
    | Error _ -> None
  with _ ->
    None

/// A full shareable URL for `tree` (this page + the `#t=` fragment).
let shareUrl (tree: Node<obj>) : string = pageBase () + prefix + encode tree

/// Write `tree` into the live URL fragment (so a copy of the address bar shares it).
let writeToHash (tree: Node<obj>) : unit = writeHash (prefix + encode tree)

/// The tree carried by the current URL fragment on load, if any.
let fromHash () : Node<obj> option =
  let h = currentHash ()

  if h.StartsWith(prefix) then
    decode (h.Substring(prefix.Length))
  else
    None

/// Diagnostic (for the permalink test): does `wireJson` survive a full
/// decode → encode-to-fragment → decode round-trip byte-identically (canonical
/// JSON)? `false` for a non-decodable input.
let roundTrips (wireJson: string) : bool =
  match Decode.decodeNode wireJson with
  | Error _ -> false
  | Ok wire ->
    let tree = WireTree.reify wire

    match decode (encode tree) with
    | Some restored -> Canon.encodeNode restored = Canon.encodeNode tree
    | None -> false
