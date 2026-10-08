module Fuaran.Live.Testing.PropertyEditorProbes

// ============================================================================
//  TEST-ONLY. The property editor's flat diagnostic surface (field summaries, flat commits).
//
//  Compiled into the playground project so the vitest suite can import it
//  from the Fable output, and imported by NOTHING in the app: it sits in
//  app/testing/ so that the production module holds only what the playground
//  runs.
// ============================================================================

open Fable.Core
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

module Introspect = Fuaran.UI.Ops.Introspect
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module Decode = Fuaran.UI.Ops.JsonDecode
module ApplyEngine = Fuaran.UI.Ops.Apply
module Schema = Fuaran.UI.Ops.SchemaGen
module Validate = Fuaran.UI.PreEmitValidate

open Fuaran.Live
open Fuaran.Live.PropertyEditor

// ─── flat diagnostic surface (cross-boundary friendly) ───────────────────────
//
// F# lists, records and DUs are awkward to assert on from the JS side of the
// Fable boundary, so — exactly as `Session.ingestResult` and the Phase 710
// cursor helpers do — the same logic is projected to plain strings and flat
// records. These are the headless test surface AND a host-language-agnostic
// description of the derived panel.

/// Every derived field as `"<group>|<path>|<editor>|<current>"`.
let fieldSummary (node: Node<obj>) : string array =
  fields node
  |> List.map (fun f -> f.Group + "|" + f.Path + "|" + editorTag f.Editor + "|" + f.Current)
  |> Array.ofList

/// The op paths of every field that is genuinely editable.
let editablePaths (node: Node<obj>) : string array =
  fields node
  |> List.filter (fun f ->
    match f.Editor with
    | Editor.ReadOnly _ -> false
    | _ -> true)
  |> List.map _.Path
  |> Array.ofList

/// The read-only reason for one op path ("" when the path is editable or
/// absent) — so the honesty of the read-only wording is assertable.
let readOnlyReason (node: Node<obj>) (path: string) : string =
  fields node
  |> List.tryFind (fun f -> f.Path = path)
  |> Option.bind (fun f ->
    match f.Editor with
    | Editor.ReadOnly r -> Some r
    | _ -> None)
  |> Option.defaultValue ""

/// The select options offered for one op path (empty when it is not a choice).
let choiceOptions (node: Node<obj>) (path: string) : string array =
  fields node
  |> List.tryFind (fun f -> f.Path = path)
  |> Option.map (fun f ->
    match f.Editor with
    | Editor.Choice options -> Array.ofList options
    | _ -> [||])
  |> Option.defaultValue [||]

/// The session's op log as a plain string array — an F# list is a linked
/// structure across the Fable boundary, so a test asserting "one op per
/// committed change" needs the projection rather than a `.length`.
let opLog (session: Session.SessionState) : string array = Array.ofList session.Ops

/// `commit`, addressed by plain strings and projected flat: commit `raw` to the
/// field at `path` on the node with `nodeId`. `Ok` false leaves `Next` as the
/// input session, so a test can assert the tree really was untouched.
let commitAt
  (session: Session.SessionState)
  (nodeId: string)
  (path: string)
  (raw: string)
  : {| Ok: bool
       Error: string
       Next: Session.SessionState |}
  =
  let refused message =
    {| Ok = false
       Error = message
       Next = session |}

  match session.Tree with
  | None -> refused "there is no tree to edit"
  | Some tree ->
    match Introspect.findNode (NodeId nodeId) tree with
    | None -> refused ("no node with id '" + nodeId + "'")
    | Some node ->
      match fields node |> List.tryFind (fun f -> f.Path = path) with
      | None -> refused ("no derived field at path '" + path + "'")
      | Some field ->
        match commit session node field raw with
        | Committed next -> {| Ok = true; Error = ""; Next = next |}
        | Rejected message -> refused message
