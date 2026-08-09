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
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Live.Ports

module Decode = Fuaran.UI.Ops.JsonDecode
module ApplyEngine = Fuaran.UI.Ops.Apply
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

type ConversationTurn = { Role: ProviderRole; Content: string }

// ─── the attributed op record (Phase 712) ────────────────────────────────────
//
// `Ops` below already records WHAT changed; what it cannot answer is WHO changed
// it. That question is the whole point of a provenance-bearing editor: a session
// in which a human retyped a heading and a model rewrote a chart is a different
// artefact from one the model produced alone, and only the record can tell them
// apart afterwards.
//
// The attribution rides the estate's SHIPPED contract rather than a second
// vocabulary invented here: `Actor` (Human / Agent, from the op-stream
// abstractions) is the same DU the panel chain records and the same one the
// other hosts encode byte-identically. Folding it through
// `HashChain.computeHash` puts it INSIDE the hash, so re-attributing an op after
// the fact breaks the chain — attribution is part of the record, not merely
// written down beside it. That is the difference between a log and provenance.
// The chain is unkeyed, so breaking it catches corruption and a careless edit,
// not a writer who recomputes the following hashes; signing is a separate seam.

/// A fixed timestamp, so the session chain is CONTENT-ADDRESSED — a pure
/// function of prev-hash + sequence + actor + op, exactly as the panel chain is
/// (Phase 466). Replaying the same ops in the same order reproduces the same
/// hashes, which is what makes an exported log checkable by whoever receives it;
/// a wall-clock stamp would make every replay disagree with the original.
/// Public because verification has to hash with the same value the record was
/// written with — a verifier that supplies its own timestamp checks nothing.
let chainTimestamp =
  System.DateTimeOffset(2020, 1, 1, 0, 0, 0, System.TimeSpan.Zero)

/// The top-level `$type` of a canonical op document — the op's kind, for display.
[<Emit("(function(j){ try { var t = JSON.parse(j).$type; return (typeof t === 'string') ? t : 'op'; } catch(e){ return 'op'; } })($0)")>]
let private opKindOf (canonJson: string) : string = jsNative

/// The origin marker for an op a HUMAN authored — a navigator property-panel
/// commit. One stable id: the playground has no accounts, so claiming a user
/// identity would be a fiction; what is true and useful is the surface.
let navigatorActor: Actor = Actor.Human "navigator"

/// The origin marker for an op the MODEL emitted through the closed loop.
/// `model` is the provider's model id where the caller knows it (`ingestBy`);
/// the plain `ingest` path is not told which model produced the text, and
/// records an empty model rather than guessing one — an `Agent` attribution
/// with an unknown model is honest, a fabricated model id is not.
let modelActor (model: string) : Actor = Actor.Agent(model, "", "emission")

/// One applied op's worth of provenance: its position in the session stream,
/// its canonical bytes, who authored it, and the hash chaining it to its
/// predecessor. Mirrors the panel chain's `ChainEntry` (Phase 466) — same
/// primitive, same `computeHash` call, one scope up.
type LogEntry =
  { Seq: int
    OpJson: string
    OpKind: string
    Actor: Actor
    Hash: string
    Prev: string }

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
    /// The attributed, hash-chained record of the session's ops (Phase 712) —
    /// one entry per op of `Ops`, in the same order, **plus any redo tail**.
    ///
    /// The undo cursor is not stored: it IS `List.length Ops`. Entries at or past
    /// that index are undone-but-redoable, which is why this list can be longer
    /// than `Ops` and never shorter. A new edit made while undone truncates the
    /// tail (`recordOp`), so the history stays linear — see `OpLog`.
    Log: LogEntry list
    /// The conversation transcript.
    History: ConversationTurn list
  }

let empty: SessionState =
  { Tree = None
    Ops = []
    Snapshots = []
    Log = []
    History = [] }

// ─── recording ───────────────────────────────────────────────────────────────

/// The tree the recorded sequence replays from — `Snapshots[0]`, the session's
/// base. `None` before the first emission.
let baseTree (session: SessionState) : Node<obj> option = List.tryHead session.Snapshots

/// The chain seed: the content hash of the base tree, so the chain is BOUND to
/// the tree it started from — an op log replayed against a different base does
/// not verify, rather than verifying against the wrong thing. Genesis before
/// there is a base at all.
let baseHash (session: SessionState) : string =
  match baseTree session with
  | Some tree -> HashChain.sha256Hex (Canon.encodeNode tree)
  | None -> HashChain.genesisPreviousHash

/// Re-point the session at a new base tree: the op sequence, the snapshots and
/// the attributed record all reset together, because none of them can replay
/// against a tree they were not built from.
///
/// This exists as ONE function rather than three field assignments at each call
/// site because the three must move together and nothing checks that they did.
/// A site that cleared `Ops` and `Snapshots` but kept `Log` would look correct
/// and leave a redo tail pointing at a base that no longer exists — `canRedo`
/// would say yes and the replay would be against the wrong tree.
let rebase (tree: Node<obj>) (session: SessionState) : SessionState =
  { session with
      Tree = Some tree
      Ops = []
      Snapshots = [ tree ]
      Log = [] }

/// The `Log` that results from appending one freshly-applied op. Truncates the
/// redo tail first — a new edit made after an undo abandons the undone branch
/// (linear history; DAG branching is deliberately out of scope) — then chains
/// the new entry onto whatever is now the head. Pure: returns the list, applies
/// nothing, and never throws.
let recordOp (session: SessionState) (actor: Actor) (op: TreeOp<obj>) (canonOp: string) : LogEntry list =
  let cursor = List.length session.Ops
  let kept = session.Log |> List.truncate cursor

  let prev =
    kept
    |> List.tryLast
    |> Option.map (fun e -> e.Hash)
    |> Option.defaultValue (baseHash session)

  let seq = cursor + 1

  let hash =
    HashChain.computeHash prev op seq chainTimestamp actor None OpResultEnvelope.Success

  kept
  @ [ { Seq = seq
        OpJson = canonOp
        OpKind = opKindOf canonOp
        Actor = actor
        Hash = hash
        Prev = prev } ]

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
/// never throws. `actor` is the origin recorded against the applied op (Phase 712)
/// – an emission is an `Agent` op; the navigator's own edits record as `Human`
/// through `PropertyEditor.commit`.
let ingestBy (actor: Actor) (session: SessionState) (rawAssistantText: string) : IngestOutcome =
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
            let canonOp = Canon.encodeOp op

            // The redo tail (if the user had undone anything) is truncated by
            // `recordOp`, so `Ops` and the applied prefix of `Log` stay the same
            // sequence — a model emission after an undo abandons the undone
            // branch exactly as a human edit does.
            Ingested(
              "op",
              { session with
                  Tree = Some newTree
                  Ops = session.Ops @ [ canonOp ]
                  Snapshots = session.Snapshots @ [ newTree ]
                  Log = recordOp session actor op canonOp }
            )
    else
      match Decode.decodeNode json with
      | Error e -> IngestFailed { Kind = "decode"; Message = e.Message }
      | Ok node ->
        // `decodeNode` yields a `WireTree` (inert closure sentinels); the live
        // preview renders it as-is (dead interactivity by design), so reify to the
        // raw `Node<obj>` the session cache + renderer + apply engine work over.
        let tree = WireTree.reify node

        // A full-tree emission is a new base: the op sequence that built the old
        // tree cannot replay against it, so the record resets rather than
        // accumulating across the discontinuity. The export of a reset session is
        // therefore honest about what it can reproduce — this base plus these
        // ops — and nothing more.
        Ingested("tree", rebase tree session)

/// `ingestBy` with the closed loop's default origin — an `Agent` op whose model
/// id is unknown at this seam (see `modelActor`). The signature is unchanged from
/// before Phase 712, so every existing call site keeps working.
let ingest (session: SessionState) (rawAssistantText: string) : IngestOutcome =
  ingestBy (modelActor "") session rawAssistantText

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

/// EVERY message `buildMessages` produces, as a plain array — the accumulated
/// conversation as the provider would receive it. `lastMessageContent` answers
/// "what is injected"; this answers "what else is carried", which is the
/// question the refine loop turns on.
let allMessageContents (session: SessionState) (prompt: string) : string array =
  buildMessages session prompt |> List.map _.Content |> Array.ofList

// ─── "refine from here" — the EDITED tree as the next emission's context ─────
//
// The playground's ordinary turn resumes the model's own conversation: the
// accumulated message list is replayed, and the newest thing in it is the
// model's last emission. That is exactly the wrong baseline once a human has
// been through the navigator, because the tree on screen is no longer what the
// model last said — it is what the human APPROVED, and the difference between
// the two is the whole of the correction the human just made.
//
// Refining "from here" therefore does two things, and both are subtractive
// rather than clever:
//
//  1. It drops the accumulated transcript and sends ONE message carrying the
//     CURRENT tree. The model's stale emission is not summarised, not
//     down-weighted — it is absent, so there is nothing for the next emission
//     to revert to. This reuses `buildMessages`' existing injection seam
//     verbatim (a history-free session), rather than opening a second path that
//     could word the same thing differently.
//
//  2. It states the human's edits as CONSTRAINTS in the system prompt. The
//     edits are already IN the tree, so the summary is not the source of truth
//     and is never load-bearing — it is emphasis, telling the model which parts
//     of the tree it was handed are deliberate. That framing is what makes
//     truncation safe: dropping a line loses emphasis, never data.
//
// The correction trail comes from the Phase 712 record, which is why the
// attribution had to be inside the hash before this could be built: "the human's
// ops since the last emission" is a claim about provenance, and a log whose
// attribution could be edited afterwards without trace would make it a guess.

/// The human's ops since the last model emission — the trailing run of `Human`
/// entries in the APPLIED prefix of the record (the redo tail is excluded for
/// the same reason the export excludes it: an undone op did not happen).
///
/// "Since the last emission" is literally that: everything after the last
/// `Agent` entry. A full-tree emission rebases the session, so the record is
/// empty and every entry in it is post-emission by construction; an op emission
/// leaves an `Agent` entry to cut at.
let humanOpsSinceEmission (session: SessionState) : LogEntry list =
  let applied = session.Log |> List.truncate (List.length session.Ops)

  let lastAgent =
    applied
    |> List.mapi (fun i e -> i, e)
    |> List.filter (fun (_, e) ->
      match e.Actor with
      | Agent _ -> true
      | Human _ -> false)
    |> List.tryLast
    |> Option.map fst

  let after =
    match lastAgent with
    | Some i -> applied |> List.skip (i + 1)
    | None -> applied

  after
  |> List.filter (fun e ->
    match e.Actor with
    | Human _ -> true
    | Agent _ -> false)

/// The character budget for the correction block. ~4 characters per token is the
/// usual rule of thumb across the providers this playground offers, so this is
/// roughly a 300-token ceiling — small enough that the block can never crowd out
/// the tree it annotates, which is the thing the model actually has to read.
[<Literal>]
let correctionBudgetChars = 1200

/// The per-value ceiling inside one line. A pasted paragraph is a legitimate
/// edit and would otherwise consume the whole budget by itself.
[<Literal>]
let correctionValueChars = 72

/// A named field of a canonical op document, as text: a JSON string yields its
/// value, anything else its compact JSON, an absent field the empty string.
[<Emit("""(function(j,f){ try { var v = JSON.parse(j)[f];
  if (v === undefined || v === null) return '';
  return (typeof v === 'string') ? v : JSON.stringify(v); } catch(e){ return ''; } })($0,$1)""")>]
let private opField (canonJson: string) (field: string) : string = jsNative

/// How many sub-ops a canonical `Batch` document carries (`0` for anything else).
[<Emit("(function(j){ try { var o = JSON.parse(j); return Array.isArray(o.ops) ? o.ops.length : 0; } catch(e){ return 0; } })($0)")>]
let private batchCount (canonJson: string) : int = jsNative

/// Flatten to one line and cap at `limit`, marking any elision. Newlines are
/// stripped rather than escaped because these lines are read as prose by a
/// model, not parsed.
let private oneLine (limit: int) (text: string) : string =
  let flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim()

  if flat.Length <= limit then
    flat
  else
    flat.Substring(0, max 1 (limit - 1)) + "…"

/// One recorded op as a sentence a model can act on. Reads the canonical op
/// document by field name rather than re-decoding to the typed DU: the wire
/// field names are the format's own stable contract, so a `TreeOp` case added
/// to the vocabulary degrades to "<Kind> on #<target>" instead of failing to
/// compile or silently disappearing from the summary.
let correctionLine (entry: LogEntry) : string =
  let target = opField entry.OpJson "target"
  let at = if target = "" then "" else " on #" + target

  match entry.OpKind with
  | "UpdateProp" ->
    let path = opField entry.OpJson "path"
    let value = oneLine correctionValueChars (opField entry.OpJson "value")
    sprintf "set %s%s to \"%s\"" (if path = "" then "a property" else path) at value
  | "EditNode" -> sprintf "changed the kind of the node%s" at
  | "UpdateStyle" -> sprintf "restyled the node%s" at
  | "UpdateState" -> sprintf "changed the loading/empty/error behaviour%s" at
  | "ReplaceBinding" -> sprintf "rebound %s%s" (opField entry.OpJson "slot") at
  | "RemoveNode" -> sprintf "removed the node%s" at
  | "MoveNode" -> sprintf "moved the node%s under #%s" at (opField entry.OpJson "newParentId")
  | "InsertChild" -> sprintf "inserted a child under #%s" (opField entry.OpJson "parentId")
  | "ReorderChildren" -> sprintf "reordered the children of #%s" (opField entry.OpJson "parentId")
  | "Batch" -> sprintf "applied %d edits together" (batchCount entry.OpJson)
  | other -> sprintf "%s%s" other at

/// The human's corrections as budget-capped lines, oldest elided first.
///
/// **The truncation rule, and why it drops the OLDEST.** Later edits supersede
/// earlier ones — on the same node literally, and in intent generally, because
/// the last thing the human touched is the thing they were still deciding. So
/// the tail is kept and the head is dropped, with the elision stated in place
/// ("N earlier edit(s) omitted") rather than left for the model to infer. Every
/// dropped edit is still present in the tree above it, and the marker says so;
/// a silent truncation would let the model read the block as exhaustive and
/// treat an un-mentioned edit as fair game.
let correctionLines (session: SessionState) : string list =
  let all = humanOpsSinceEmission session |> List.map correctionLine

  let kept =
    all
    |> List.rev
    |> List.fold
      (fun (acc, used) line ->
        let cost = line.Length + 3

        if used + cost <= correctionBudgetChars then
          (line :: acc, used + cost)
        else
          (acc, correctionBudgetChars + 1))
      ([], 0)
    |> fst

  let dropped = List.length all - List.length kept

  if dropped > 0 then
    sprintf "(%d earlier edit(s) omitted for brevity — they are already reflected in the tree above.)" dropped
    :: kept
  else
    kept

/// The system-prompt suffix for a refine turn: the human's corrections, framed
/// as constraints. Empty when the human has made no edits since the last
/// emission — an empty "the human changed:" heading would be a lie the model
/// would try to honour.
let refineSystemSuffix (session: SessionState) : string =
  match correctionLines session with
  | [] -> ""
  | lines ->
    "\n\n# The person has edited this UI by hand\n\n"
    + "The tree you have been given is THEIR version, not your last emission. They:\n\n"
    + (lines |> List.map (fun l -> "- " + l) |> String.concat "\n")
    + "\n\nTreat those choices as decided. Keep them exactly as they are unless the request below "
    + "explicitly asks you to change them, and do not reinstate anything you emitted earlier that "
    + "they have since altered."

/// The single user message a "refine from here" turn sends: the CURRENT
/// (human-edited) tree as canonical wire JSON, then the ask. Built by
/// `buildMessages` against a history-free session, so the tree injection is the
/// SAME seam the ordinary loop uses — the only difference is which tree it
/// carries and what is absent around it.
let refinePrompt (session: SessionState) (userPrompt: string) : string =
  lastMessageContent { session with History = [] } userPrompt

// ─── flat diagnostic surface for the refine loop ─────────────────────────────

/// The correction lines as a plain array (an F# list has no `.length` across
/// the Fable boundary).
let correctionLineArray (session: SessionState) : string array = correctionLines session |> Array.ofList

/// How many human ops stand between the last emission and now — the "edited (n
/// human ops)" readout, and the test's handle on the trail.
let humanOpCount (session: SessionState) : int =
  humanOpsSinceEmission session |> List.length

/// The budget constants as READABLE values. A `[<Literal>]` is inlined at every
/// use site and never reaches the module's exports, so a test that imported the
/// literal directly would silently receive `undefined` and assert nothing — the
/// budget check would pass whatever the budget was.
let correctionBudget: int = correctionBudgetChars

let correctionValueCap: int = correctionValueChars
