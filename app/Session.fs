module Fuaran.Live.Session

// ============================================================================
//  The in-memory session cache + the closed loop (F# port of src/session/*) –
//  Stage 3 of the entirely-F#/Fable rebuild.
//
//  Everything is ephemeral and lives only in browser memory – no persistence, no
//  server. A `SessionState` holds the folded Fuaran tree, the op stream that built
//  it, and the conversation transcript. Each turn:
//   1. buildMessages – inject the CURRENT tree JSON into the latest user message
//      so the model edits existing state instead of re-emitting (the closed loop);
//   2. ingest – decode the model's emission (a full Node OR a TreeOp/Batch), apply
//      it through the linked Fable-safe `Fuaran.UI.Ops` engine, and fold the result
//      back in. A malformed emission yields a typed error envelope, never a throw.
//
//  The decode/apply/encode core is pure F# over the linked Ops/CanonicalJson
//  source; the leaf string-munging (JSON extraction + pretty-print + the `$type`
//  discriminator read) ports `session.ts` verbatim as inline JS `[<Emit>]`, the
//  same Fable-interop idiom the F# tier uses throughout – robust + byte-faithful.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.Live.Ports

module Decode = Fuaran.UI.Ops.JsonDecode
module ApplyEngine = Fuaran.UI.Ops.Apply
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

type ConversationTurn = { Role: ProviderRole; Content: string }

type SessionState =
  {
    /// The current folded tree, or `None` before the first successful emission.
    Tree: Node<obj> option
    /// The canonical JSON of each applied op (the inspector Ops view).
    Ops: string list
    /// A folded-tree snapshot at every step of this session – `Snapshots[0]` is
    /// the base (first full emission), `Snapshots[i]` the tree after op `i`. This
    /// is the time-travel record (Phase 296): scrubbing renders the exact tree at
    /// a point without re-evaluating anything, and each snapshot WAS built by the
    /// apply engine, so the replay is op-by-op and exact. Reset on a full-tree
    /// replacement.
    Snapshots: Node<obj> list
    /// The conversation transcript.
    History: ConversationTurn list
  }

let empty: SessionState =
  { Tree = None
    Ops = []
    Snapshots = []
    History = [] }

// ─── leaf string helpers (verbatim port of session.ts, as inline JS) ─────────

/// Extract the single canonical JSON document from a model emission – the last
/// fenced ```json block, else the first balanced top-level object; `null` when
/// none. Verbatim port of `session.ts` `extractJson`.
[<Emit("""(function(text){
  var fence = /```(?:json)?\s*([\s\S]*?)```/gi, m, last = null;
  while ((m = fence.exec(text)) !== null) { var b = (m[1]||'').trim(); if (b.charAt(0)==='{') last = b; }
  if (last !== null) return last;
  var start = text.indexOf('{'); if (start < 0) return null;
  var depth=0, inStr=false, esc=false;
  for (var i=start;i<text.length;i++){ var ch=text[i];
    if(inStr){ if(esc)esc=false; else if(ch==='\\')esc=true; else if(ch==='"')inStr=false; continue; }
    if(ch==='"')inStr=true; else if(ch==='{')depth++; else if(ch==='}'){depth--; if(depth===0) return text.slice(start,i+1);} }
  return null;
})($0)""")>]
let private extractJsonRaw (text: string) : string = jsNative

/// The top-level `$type` discriminator of a JSON document, or `null`.
[<Emit("(function(j){ try { var p = JSON.parse(j); return (p && typeof p.$type === 'string') ? p.$type : null; } catch(e){ return null; } })($0)")>]
let private topLevelType (json: string) : string = jsNative

/// Re-indent canonical (compact) JSON for display; falls back to raw on failure.
[<Emit("(function(c){ try { return JSON.stringify(JSON.parse(c), null, 2); } catch(e){ return c; } })($0)")>]
let prettyJson (compact: string) : string = jsNative

let private isNull (s: string) : bool = emitJsExpr s "$0 == null"

let private opKinds =
  set
    [ "EditNode"
      "UpdateProp"
      "ReplaceBinding"
      "UpdateStyle"
      "UpdateState"
      "InsertChild"
      "RemoveNode"
      "MoveNode"
      "ReorderChildren"
      "Batch" ]

/// Whether the text carries an extractable JSON document. The agent loop
/// pre-checks this before attempting ingestion, so a pure-text / tool-only turn
/// is not logged as a spurious `no-json` runtime error.
let hasJson (text: string) : bool = not (isNull (extractJsonRaw text))

/// The single extractable JSON document of an emission, if any – the same
/// extraction `ingest` runs, exposed so the panel-turn path (Phase 466) can
/// probe the document for its envelope marker before choosing an ingest route.
let extractJson (text: string) : string option =
  let raw = extractJsonRaw text
  if isNull raw then None else Some raw

// ─── emission ingestion ──────────────────────────────────────────────────────

type IngestError = { Kind: string; Message: string }

type IngestOutcome =
  | Ingested of mode: string * next: SessionState
  | IngestFailed of IngestError

/// Decode + apply a model emission against the current session, returning the next
/// session on success or a typed error on failure. Pure – never mutates `session`,
/// never throws.
let ingest (session: SessionState) (rawAssistantText: string) : IngestOutcome =
  let json = extractJsonRaw rawAssistantText

  if isNull json then
    IngestFailed
      { Kind = "no-json"
        Message = "No JSON document found in the response. Expected a Fuaran Node or TreeOp." }
  else
    let disc = topLevelType json
    let isOp = not (isNull disc) && Set.contains disc opKinds

    if isOp then
      match session.Tree with
      | None ->
        IngestFailed
          { Kind = "no-tree"
            Message =
              "The model emitted a TreeOp, but there is no tree yet. The first emission must be a full Node tree." }
      | Some tree ->
        match Decode.decodeOp json with
        | Error e -> IngestFailed { Kind = "decode"; Message = e.Message }
        | Ok op ->
          match ApplyEngine.apply op tree with
          | Error e -> IngestFailed { Kind = "apply"; Message = e.Message }
          | Ok newTree ->
            Ingested(
              "op",
              { session with
                  Tree = Some newTree
                  Ops = session.Ops @ [ Canon.encodeOp op ]
                  Snapshots = session.Snapshots @ [ newTree ] }
            )
    else
      match Decode.decodeNode json with
      | Error e -> IngestFailed { Kind = "decode"; Message = e.Message }
      | Ok node ->
        // `decodeNode` yields a `WireTree` (inert closure sentinels); the live
        // preview renders it as-is (dead interactivity by design), so reify to the
        // raw `Node<obj>` the session cache + renderer + apply engine work over.
        let tree = WireTree.reify node

        Ingested(
          "tree",
          { session with
              Tree = Some tree
              Ops = []
              Snapshots = [ tree ] }
        )

// ─── pre-emit advisories (Phase 664) ─────────────────────────────────────────
//
// An emission can APPLY cleanly and still carry dead intent — `editable: true`
// over a non-writable source (FUARAN090), an inert control (FUARAN069), a
// decorative filter (FUARAN074). `PreEmitValidate.validate` computes exactly
// this class; each defect projects through the shared `describe` so the loop
// feeds the model the same code + message every other host reports.

type Advisory =
  { Code: string
    Severity: string
    Message: string }

/// The pre-emit advisories for the session's current tree (`[]` when no tree).
/// Pure – never throws; the tree was already decode/apply-validated to exist.
let preEmitAdvisories (session: SessionState) : Advisory list =
  match session.Tree with
  | None -> []
  | Some tree ->
    match Fuaran.UI.PreEmitValidate.validate tree with
    | Ok() -> []
    | Error defects ->
      defects
      |> List.map (fun d ->
        let code, severity, message = Fuaran.UI.PreEmitValidate.describe d

        { Code = code
          Severity =
            (match severity with
             | Fuaran.UI.PreEmitValidate.DefectSeverity.Error -> "error"
             | Fuaran.UI.PreEmitValidate.DefectSeverity.Warning -> "warning")
          Message = message })

// ─── closed-loop turn construction ───────────────────────────────────────────

/// Append a transcript turn (pure).
let withTurn (session: SessionState) (turn: ConversationTurn) : SessionState =
  { session with
      History = session.History @ [ turn ] }

/// Build the provider message list for a turn. History is sent as plain text; the
/// CURRENT tree JSON is injected into the latest user message so the model emits
/// ops against existing state – the closed loop.
let buildMessages (session: SessionState) (userPrompt: string) : ProviderMessage list =
  let prior: ProviderMessage list =
    session.History
    |> List.map (fun t -> ({ Role = t.Role; Content = t.Content }: ProviderMessage))

  match session.Tree with
  | None -> prior @ [ ({ Role = User; Content = userPrompt }: ProviderMessage) ]
  | Some tree ->
    let treeJson = prettyJson (Canon.encodeNode tree)

    let augmented =
      "The UI you are editing currently has this canonical wire-format tree:\n\n```json\n"
      + treeJson
      + "\n```\n\nEmit a TreeOp (or a Batch of TreeOps) to make the following change, "
      + "or a full replacement Node tree if a rewrite is clearer:\n\n"
      + userPrompt

    prior @ [ { Role = User; Content = augmented } ]

/// The canonical wire JSON of the current tree, pretty-printed (inspector view).
let treeJson (session: SessionState) : string =
  match session.Tree with
  | None -> ""
  | Some tree -> prettyJson (Canon.encodeNode tree)

// ─── flat diagnostic surface (cross-boundary friendly – used by the loop tests) ──
//
// `ingest` returns an F# DU and `buildMessages` an F# list, both awkward to assert
// on from the JS/TS side of the Fable boundary. These thin helpers project the same
// logic to flat values (an anonymous record / a string), so the closed loop is
// testable headlessly via vitest over the Fable output – and they double as a
// host-language-agnostic diagnostic surface.

/// `ingest`, projected to a flat record: `Ok` + the `Mode` ("tree" / "op") on
/// success, or `Error` (the failure kind) otherwise, with the resulting `Next`
/// session (the input session unchanged on failure).
let ingestResult
  (session: SessionState)
  (raw: string)
  : {| Ok: bool
       Mode: string
       Error: string
       Next: SessionState |}
  =
  match ingest session raw with
  | Ingested(mode, next) ->
    {| Ok = true
       Mode = mode
       Error = ""
       Next = next |}
  | IngestFailed e ->
    {| Ok = false
       Mode = ""
       Error = e.Kind
       Next = session |}

/// The content of the final (latest user) message `buildMessages` produces – the
/// closed-loop injection point, as a plain string.
let lastMessageContent (session: SessionState) (prompt: string) : string =
  match List.tryLast (buildMessages session prompt) with
  | Some m -> m.Content
  | None -> ""
