module Fuaran.Live.Agent

// ============================================================================
//  The agent self-debug loop (F# port of src/agent/*) – Phase 326 Stage 6.
//
//  The opt-in "watch the AI debug its own UI" mode: an emit → observe → repair
//  loop, driven by the model through provider tool-use. Each iteration is one
//  `SendAgentic` round-trip; the model emits a tree/op as fenced JSON (ingested
//  exactly as in the normal closed loop) and may call six tools in the same
//  turn: five read-only introspection tools, plus the interactive `askUser` –
//  a typed elicitation (the wire format's §18 envelope) that renders the
//  model's question AS a live form in the transcript, suspends the loop until
//  the human resolves it, and returns exactly one typed outcome. The loop
//  applies the emission, renders it, runs the tools against the live result,
//  and feeds the results back. It ends when the model returns a turn with no
//  tool calls – or the instant a hard budget (iterations / tokens /
//  wall-clock) or an abort is hit (default-deny by budget, so the loop can't
//  run away on a BYOK key).
//
//  Four of the five read-only tools are PURE (getNodeState / getBindingValue /
//  getRuntimeErrors over the current wire tree; getKindSchema over the static
//  wire-format schema) – unit-testable headlessly. getRenderedDom reads the
//  live preview geometry via getBoundingClientRect and is browser-only; the
//  loop depends only on the `DomReader` interface, so tests inject a
//  deterministic fake. `askUser` is likewise a seam: the loop depends only on
//  `AskUser: string -> Async<string>` (envelope wire in, outcome wire out) –
//  the browser host renders the live form; a test injects a SCRIPTED answerer
//  (the same hook an evaluation harness uses to play the human). The loop +
//  dispatcher are pure given an `IAgenticProvider` + a `DomReader` + an
//  `AskUser` + a `ToolContext` of getters, so the whole thing is testable
//  without a live LLM or a DOM (see test/agentLoop.test.ts + askUser.test.ts).
//
//  As in Session.fs, the generic JSON walking (DOM-free, deterministic) ports the
//  TS verbatim as inline-JS `[<Emit>]`, the same Fable-interop idiom the F# tier
//  uses throughout. Sample-data resolution (the TS `MockResolution` path) has no
//  F# counterpart in this build, so non-Static bindings report as unresolved
//  by design (the honest answer the playground always gave for Static emissions).
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Live.Ports

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module Schema = Fuaran.UI.Ops.SchemaGen

// ─── budget + halt + timeline value types (port of agent/types.ts) ───────────

/// Hard ceilings that bound an agent run. The loop halts the instant any is
/// reached – default-deny by budget. As of Phase 328 this is a **generous,
/// hidden safety backstop** (runaway protection on a BYOK key), no longer the
/// user-facing control: the live token counter + elapsed timer + Stop button
/// are the surfaced controls, and a halted run is resumable. The ceilings are
/// set far above any reasonable interactive run so they only catch a genuine
/// runaway, never a normal multi-round repair session.
type AgentBudget =
  { MaxIterations: int
    MaxTokens: int
    MaxWallClockMs: int }

let defaultBudget: AgentBudget =
  { MaxIterations = 40
    MaxTokens = 2_000_000
    MaxWallClockMs = 600_000 }

[<RequireQualifiedAccess>]
type HaltReason =
  | Completed
  | MaxIterations
  | MaxTokens
  | MaxWallClock
  | Aborted
  | ProviderError

/// A human label for each halt reason, for the timeline.
let haltLabel (reason: HaltReason) : string =
  match reason with
  | HaltReason.Completed -> "Loop complete – the model is satisfied."
  | HaltReason.MaxIterations -> "Halted – iteration budget reached."
  | HaltReason.MaxTokens -> "Halted – token budget reached."
  | HaltReason.MaxWallClock -> "Halted – time budget reached."
  | HaltReason.Aborted -> "Stopped by you."
  | HaltReason.ProviderError -> "Halted – the provider call failed."

/// The stable string form of a halt reason (timeline keys + the flat test surface).
let haltReasonStr (reason: HaltReason) : string =
  match reason with
  | HaltReason.Completed -> "completed"
  | HaltReason.MaxIterations -> "max-iterations"
  | HaltReason.MaxTokens -> "max-tokens"
  | HaltReason.MaxWallClock -> "max-wall-clock"
  | HaltReason.Aborted -> "aborted"
  | HaltReason.ProviderError -> "provider-error"

/// One entry in the visible tool-call timeline. `ord` is a monotonic sequence
/// number (the React key); `input` (tool args) crosses the boundary as raw JSON.
[<RequireQualifiedAccess>]
type TimelineEntry =
  | UserPrompt of ord: int * text: string
  | ModelCall of ord: int * iteration: int * usage: ProviderUsage option
  | ModelText of ord: int * text: string
  | Emission of ord: int * mode: string * ok: bool * summary: string
  | ToolCall of ord: int * name: string * input: obj * resultSummary: string * ok: bool
  /// A panel-turn emission (Phase 466): the model streamed a tree/op into the
  /// named live panel. `isNew` marks the panel's first appearance – the
  /// transcript mounts the panel's LIVE row there (rendered from the panel
  /// store, so later ops update it in place); subsequent emissions log as
  /// compact audit rows beneath it.
  | PanelEmission of ord: int * panelId: string * mode: string * ok: bool * isNew: bool * summary: string
  /// A typed elicitation opened by the `askUser` tool (Phase 465 §18): the
  /// model's question mounts as a LIVE form row at this spot in the transcript
  /// (rendered from the host's ask store, exactly as a panel renders from the
  /// panel store); the loop is suspended until the human resolves it.
  | Ask of ord: int * elicitationId: string * summary: string
  | Halt of ord: int * reason: HaltReason * detail: string option

/// The sequence number an entry carries – its React key.
let entryOrd (entry: TimelineEntry) : int =
  match entry with
  | TimelineEntry.UserPrompt(o, _) -> o
  | TimelineEntry.ModelCall(o, _, _) -> o
  | TimelineEntry.ModelText(o, _) -> o
  | TimelineEntry.Emission(o, _, _, _) -> o
  | TimelineEntry.ToolCall(o, _, _, _, _) -> o
  | TimelineEntry.PanelEmission(o, _, _, _, _, _) -> o
  | TimelineEntry.Ask(o, _, _) -> o
  | TimelineEntry.Halt(o, _, _) -> o

/// The kind discriminator of an entry, as a string (the flat test surface).
let timelineKind (entry: TimelineEntry) : string =
  match entry with
  | TimelineEntry.UserPrompt _ -> "user-prompt"
  | TimelineEntry.ModelCall _ -> "model-call"
  | TimelineEntry.ModelText _ -> "model-text"
  | TimelineEntry.Emission _ -> "emission"
  | TimelineEntry.ToolCall _ -> "tool-call"
  | TimelineEntry.PanelEmission _ -> "panel-emission"
  | TimelineEntry.Ask _ -> "ask"
  | TimelineEntry.Halt _ -> "halt"

type AgentRunResult =
  {
    HaltReason: HaltReason
    Iterations: int
    TotalTokens: int
    RuntimeErrors: Session.IngestError list
    ProviderError: ProviderError option
    /// The full accumulated provider-message context at halt – the seed for a
    /// resumed run (Phase 328). A follow-up prompt re-enters `runAgentLoop` with
    /// these as `priorMessages`, so the model keeps its context across a halt /
    /// Stop instead of starting cold.
    FinalMessages: AgentMessage list
  }

// ─── the rendered-DOM reader (port of agent/domGeometry.ts) ──────────────────
//
// Every Fuaran node renders to a wrapper carrying `data-fuaran-node-id`; the
// browser reader measures each within the preview container with
// `getBoundingClientRect` + the scroll/client box, deriving the layout flags an
// AI cares about when self-correcting (overflowing / truncated / hidden). The
// reader is an injected port: the browser impl touches `document`; the loop
// depends only on the interface, so its tests pass a deterministic fake. The
// report is returned as a plain JS object (camelCase) ready to JSON-stringify
// into the tool result – the exact shape the original TS tool returned.

type DomReader =
  /// Geometry report for all rendered nodes, or just `nodeId` if given.
  abstract member Read: string option -> obj

[<Emit("""(function(nodeId, containerSelector){
  var empty = { container:null, nodeCount:0, nodes:[], flags:{ overflowingIds:[], truncatedIds:[], hiddenIds:[] } };
  if (typeof document === 'undefined') return empty;
  var container = document.querySelector(containerSelector);
  if (container === null) return empty;
  var round = function(n){ return Math.round(n*100)/100; };
  var measure = function(el){
    var id = el.getAttribute('data-fuaran-node-id') || '';
    var r = el.getBoundingClientRect();
    var sw = el.scrollWidth || 0, cw = el.clientWidth || 0, sh = el.scrollHeight || 0, ch = el.clientHeight || 0;
    var truncated = sw > cw + 1;
    var overflowing = truncated || sh > ch + 1;
    var hidden = r.width === 0 && r.height === 0;
    return { id:id, rect:{ x:round(r.x), y:round(r.y), width:round(r.width), height:round(r.height) },
             overflowing:overflowing, truncated:truncated, hidden:hidden };
  };
  var selector = (nodeId == null)
    ? '[data-fuaran-node-id]'
    : '[data-fuaran-node-id="' + ((typeof CSS !== 'undefined' && CSS.escape) ? CSS.escape(nodeId) : nodeId) + '"]';
  var elements = Array.prototype.slice.call(container.querySelectorAll(selector));
  var nodes = elements.map(measure);
  var cr = container.getBoundingClientRect();
  var report = {
    container: { width: round(cr.width), height: round(cr.height) },
    nodeCount: nodes.length,
    nodes: nodes,
    flags: {
      overflowingIds: nodes.filter(function(n){ return n.overflowing; }).map(function(n){ return n.id; }),
      truncatedIds: nodes.filter(function(n){ return n.truncated; }).map(function(n){ return n.id; }),
      hiddenIds: nodes.filter(function(n){ return n.hidden; }).map(function(n){ return n.id; })
    }
  };
  if (nodeId != null && nodes.length === 0) { report.notFound = nodeId; }
  return report;
})($0, $1)""")>]
let private readDomJs (nodeId: string) (containerSelector: string) : obj = jsNative

/// The browser reader. `containerSelector` scopes the query to the preview pane,
/// so the playground's own chrome nodes (also carrying data-fuaran-node-id) are
/// never measured.
let createBrowserDomReader (containerSelector: string) : DomReader =
  { new DomReader with
      member _.Read(nodeId) =
        readDomJs (Option.toObj nodeId) containerSelector }

/// A no-op reader returning an empty report – the default for headless tests and
/// pure tool-dispatch checks (no DOM available).
let nullDomReader: DomReader =
  { new DomReader with
      member _.Read(_nodeId) =
        createObj
          [ "container" ==> null
            "nodeCount" ==> 0
            "nodes" ==> ([||]: obj[])
            "flags"
            ==> createObj
              [ "overflowingIds" ==> ([||]: obj[])
                "truncatedIds" ==> ([||]: obj[])
                "hiddenIds" ==> ([||]: obj[]) ] ] }

// ─── tree introspection (port of agent/introspection.ts – pure, DOM-free) ────
//
// Rather than walk the typed in-memory tree (whose children live under different
// keys per NodeKind), these operate on the *canonical wire JSON* – the exact
// bytes the inspector shows and the decoder round-trips. Every node is uniformly
// an object carrying `id` + `kind`, so one recursive descent locates any node by
// id. Pure + DOM-free: fully unit-testable. (`mode` selects node-state vs
// binding-value; the shared walk lives in one IIFE.)

[<Emit("""(function(mode, treeJson, id){
  var notFound = (mode === 'binding')
    ? { found:false, id:id, bindings:[], availableIds:[] }
    : { found:false, id:id, availableIds:[] };
  if (treeJson == null) return notFound;
  var parsed;
  try { parsed = JSON.parse(treeJson); } catch(e) { return notFound; }
  var isObject = function(v){ return typeof v === 'object' && v !== null; };
  var isNodeObject = function(v){ return isObject(v) && typeof v.id === 'string' && ('kind' in v); };
  var walk = function(value, visit){
    visit(value);
    if (Array.isArray(value)) { for (var i=0;i<value.length;i++) walk(value[i], visit); }
    else if (isObject(value)) { for (var k in value) if (Object.prototype.hasOwnProperty.call(value,k)) walk(value[k], visit); }
  };
  var collectNodeIds = function(p){ var ids=[]; walk(p, function(v){ if (isNodeObject(v)) ids.push(v.id); }); return ids; };
  var findNodeObject = function(p, wanted){
    var found=null;
    walk(p, function(v){ if (found===null && isNodeObject(v) && v.id===wanted) found=v; });
    return found;
  };
  var kindPathOf = function(node){
    var kind = node.kind;
    if (!isObject(kind)) return 'unknown';
    var outer = typeof kind.$type === 'string' ? kind.$type : 'unknown';
    var inner = kind.kind;
    if (isObject(inner) && typeof inner.$type === 'string') return outer + '/' + inner.$type;
    return outer;
  };
  var BINDING_TYPES = { Static:1, Query:1, Filter:1, Selection:1, State:1, Computed:1, I18n:1, Local:1, Formatted:1 };
  var collectNodeBindings = function(node){
    var out=[];
    var visit = function(value, path, isRoot){
      if (!isRoot && isNodeObject(value)) return;
      if (isObject(value)) {
        var t = value.$type;
        if (typeof t === 'string' && BINDING_TYPES[t]) { out.push({ path:path, type:t, raw:value }); return; }
        for (var k in value) if (Object.prototype.hasOwnProperty.call(value,k)) visit(value[k], path===''?k:path+'.'+k, false);
      } else if (Array.isArray(value)) {
        for (var i=0;i<value.length;i++) visit(value[i], path+'['+i+']', false);
      }
    };
    visit(node, '', true);
    return out;
  };
  var node = findNodeObject(parsed, id);
  if (node === null) {
    return (mode === 'binding')
      ? { found:false, id:id, bindings:[], availableIds: collectNodeIds(parsed) }
      : { found:false, id:id, availableIds: collectNodeIds(parsed) };
  }
  if (mode === 'binding') {
    var NOTE = 'Unresolved – this playground renders Static emissions only, so Query/State/Computed/etc. bindings have no host to resolve against (this is by design, not an error). There is no sample-data mode in this build.';
    var bindings = collectNodeBindings(node).map(function(b){
      if (b.type === 'Static') return { path:b.path, type:b.type, value: (b.raw.value != null ? b.raw.value : null) };
      return { path:b.path, type:b.type, note: NOTE };
    });
    return { found:true, id:id, bindings:bindings };
  }
  return {
    found:true, id:id,
    kindPath: kindPathOf(node),
    state: (node.state != null ? node.state : {}),
    style: (node.style != null ? node.style : {})
  };
})($0, $1, $2)""")>]
let private introspectJs (mode: string) (treeJson: string) (id: string) : obj = jsNative

/// The runtime state + style + kind of one node, read off the wire JSON.
let getNodeState (treeJson: string option) (id: string) : obj =
  introspectJs "node" (Option.toObj treeJson) id

/// Every binding within a node, with its resolved value where known. Static
/// bindings always carry their value; others report why they are unresolved.
let getBindingValue (treeJson: string option) (id: string) : obj =
  introspectJs "binding" (Option.toObj treeJson) id

// ─── kind-schema lookup (the missing introspection: shape, not just errors) ──
//
// The decoder reports ONE missing field per failure, so a model repairing a
// complex kind (Select/Chart/Table/Filters/…) by reading decode errors converges
// agonisingly – `source` → `value` → `onChange` → … – and often never does. This
// resolves the AUTHORITATIVE shape of a kind up-front, over the same hand-written
// JSON Schema the encoder/decoder mirror (Fuaran.UI.Ops.SchemaGen). Given a kind's
// `$type` it returns that kind's required + optional fields and the transitive
// closure of every binding/spec def they reference – a self-contained sub-schema.
// With no kind it lists every valid `$type`. Pure: parses the static schema string
// (no DOM, no tree), so it is unit-testable headlessly.

[<Emit("""(function(schemaText, wanted){
  var schema;
  try { schema = JSON.parse(schemaText); } catch(e){ return { error: 'schema parse failed' }; }
  var defs = (schema && schema.$defs) || {};
  var constOf = function(rec){
    return (rec && rec.properties && rec.properties.$type && typeof rec.properties.$type.const === 'string')
      ? rec.properties.$type.const : null;
  };
  var kindBranch = {};
  var addBranch = function(branch){
    if (branch == null || typeof branch !== 'object') return;
    if (Array.isArray(branch.allOf)) {
      var disc = null, specName = null;
      branch.allOf.forEach(function(part){
        if (part && typeof part.$ref === 'string') specName = part.$ref.replace('#/$defs/','');
        else { var c = constOf(part); if (c) disc = c; }
      });
      if (disc) {
        var spec = specName ? defs[specName] : null;
        kindBranch[disc] = { specName: specName, required: (spec && spec.required) || [], properties: (spec && spec.properties) || {} };
      }
    } else {
      var c = constOf(branch);
      if (c) {
        var props = {};
        for (var k in branch.properties) if (k !== '$type' && Object.prototype.hasOwnProperty.call(branch.properties,k)) props[k] = branch.properties[k];
        var req = (branch.required || []).filter(function(r){ return r !== '$type'; });
        kindBranch[c] = { specName: null, required: req, properties: props };
      }
    }
  };
  ['LayoutKind','DisplayKind','InputKind','VisKind'].forEach(function(cat){
    var d = defs[cat]; if (d && Array.isArray(d.oneOf)) d.oneOf.forEach(addBranch);
  });
  var nk = defs['NodeKind'];
  if (nk && Array.isArray(nk.oneOf)) nk.oneOf.forEach(function(b){ if (b && typeof b.$ref !== 'string') addBranch(b); });
  var kinds = Object.keys(kindBranch).sort();
  if (wanted == null) return { kinds: kinds };
  if (!Object.prototype.hasOwnProperty.call(kindBranch, wanted))
    return { error: "Unknown kind '" + wanted + "'. Call getKindSchema with no argument to list valid kinds.", validKinds: kinds };
  var branch = kindBranch[wanted];
  var refsFrom = function(node, acc){
    if (node && typeof node === 'object') {
      if (typeof node.$ref === 'string') acc[node.$ref.replace('#/$defs/','')] = 1;
      for (var k in node) if (Object.prototype.hasOwnProperty.call(node,k)) refsFrom(node[k], acc);
    }
    return acc;
  };
  var included = {}, queue = [], seed = refsFrom(branch.properties, {});
  for (var n in seed) queue.push(n);
  while (queue.length) {
    var name = queue.shift();
    if (included[name] || !defs[name]) continue;
    included[name] = defs[name];
    var more = refsFrom(defs[name], {});
    for (var m in more) if (!included[m]) queue.push(m);
  }
  return { kind: wanted, spec: branch.specName, required: branch.required, properties: branch.properties, defs: included };
})($0, $1)""")>]
let private kindSchemaJs (schemaText: string) (wanted: string) : obj = jsNative

/// The schema for one node kind (by its `$type`, e.g. "Select"), or – with `wanted`
/// null – the list of every valid kind. Resolved over the canonical wire-format
/// schema, so it is always in lock-step with the decoder the loop runs.
let getKindSchema (wanted: string option) : obj =
  kindSchemaJs Schema.wireFormatSchema (Option.toObj wanted)

// ─── the tool definitions + dispatcher (port of agent/tools.ts) ──────────────

[<Emit("JSON.stringify($0, null, 2)")>]
let jsonPretty (value: obj) : string = jsNative

[<Emit("(function(input){ return (input != null && typeof input === 'object' && typeof input.nodeId === 'string') ? input.nodeId : null; })($0)")>]
let private nodeIdRaw (input: obj) : string = jsNative

let private nodeIdOf (input: obj) : string option = Option.ofObj (nodeIdRaw input)

[<Emit("(function(input){ return (input != null && typeof input === 'object' && typeof input.kind === 'string') ? input.kind : null; })($0)")>]
let private kindArgRaw (input: obj) : string = jsNative

let private kindArgOf (input: obj) : string option = Option.ofObj (kindArgRaw input)

/// Collapse whitespace + truncate to 240 chars (the timeline summary).
[<Emit("(function(s){ var o=String(s).replace(/\\s+/g,' ').trim(); return o.length>240 ? o.slice(0,240)+'…' : o; })($0)")>]
let private summariseResult (content: string) : string = jsNative

let private schemaObj (properties: obj) (required: string list) : obj =
  if List.isEmpty required then
    createObj
      [ "type" ==> "object"
        "properties" ==> properties
        "additionalProperties" ==> false ]
  else
    createObj
      [ "type" ==> "object"
        "properties" ==> properties
        "required" ==> List.toArray required
        "additionalProperties" ==> false ]

let private nodeIdProp (description: string) : obj =
  createObj [ "nodeId" ==> createObj [ "type" ==> "string"; "description" ==> description ] ]

/// The four introspection tools exposed to the model in agent mode.
let toolDefinitions: ToolDefinition list =
  [ { Name = "getRenderedDom"
      Description =
        "Inspect the REAL rendered geometry of the live preview: each node’s bounding box plus layout flags (overflowing, truncated, hidden). Call this after emitting to verify your UI actually fits and is visible. Omit nodeId for the whole tree, or pass a nodeId to inspect one node."
      InputSchema = schemaObj (nodeIdProp "Optional: restrict to one node id.") [] }
    { Name = "getRuntimeErrors"
      Description =
        "Return the decode/apply errors from your MOST RECENT emission (malformed wire JSON, unknown ids, illegal ops) PLUS the pre-emit advisories on the currently-applied tree – declared intent that does nothing as authored (e.g. FUARAN090: editable grid over a non-writable source; FUARAN069: a handler-free control with no writable binding), each with the declarative fix named. The slate is cleared on each new emission, so a non-empty result is always about the tree you just emitted – not a pile-up from earlier attempts. Call this if an emission did not take effect, or to check an applied tree for dead intent. Tip: if the error is a missing/wrong field on a kind, call getKindSchema for that kind to get its whole shape at once."
      InputSchema = schemaObj (createObj []) [] }
    { Name = "getKindSchema"
      Description =
        "Return the AUTHORITATIVE schema for a node kind: its required and optional fields, plus the shapes of every binding/spec they reference (a self-contained sub-schema). Pass `kind` as the kind's $type (e.g. \"Select\", \"Chart\", \"Tabs\", \"Metric\"); omit it to list every valid kind. This is the canonical wire-format contract the decoder enforces – call it BEFORE emitting any kind whose fields you are not certain of, so you emit the whole correct shape in one go instead of discovering missing fields one decode error at a time."
      InputSchema =
        schemaObj
          (createObj
            [ "kind"
              ==> createObj
                [ "type" ==> "string"
                  "description"
                  ==> "Optional: the kind's $type (e.g. \"Select\"). Omit to list all valid kinds." ] ])
          [] }
    { Name = "getNodeState"
      Description =
        "Read one node’s runtime state, style, and kind from the current wire tree. Returns the available ids if the node is not found."
      InputSchema = schemaObj (nodeIdProp "The id of the node to inspect.") [ "nodeId" ] }
    { Name = "getBindingValue"
      Description =
        "List the data bindings on a node and their resolved values. Static bindings always carry a value; others report why they are unresolved (this build renders Static emissions only)."
      InputSchema = schemaObj (nodeIdProp "The id of the node whose bindings to read.") [ "nodeId" ] }
    { Name = "askUser"
      Description =
        "Ask the USER a typed question AS a live form (an elicitation envelope). The input is the whole envelope: the version tag \"$elicitation\": \"1\" (required on every call; it cannot be declared in this schema because provider schemas reject $-prefixed property keys), plus a question tree (canonical Node wire format) plus an answer contract declaring which state entries constitute the answer, each typed by a value space. The user sees your question rendered live in the conversation; the loop waits; you receive exactly ONE typed outcome: Answered (a validated answer object – every field already checked against its declared space), Declined, or TimedOut. Never returns prose. Use it only when you genuinely need the user's decision; ask at most one question per turn."
      InputSchema =
        // The envelope's "$elicitation" version tag is NOT declared here:
        // Anthropic rejects tool input_schema property keys outside
        // '^[a-zA-Z0-9_.-]{1,64}$'. additionalProperties therefore stays open
        // so the tag (taught in the description + system prompt, enforced by
        // the host codec) is not schema-forbidden.
        createObj
          [ "type" ==> "object"
            "properties"
            ==> createObj
              [ "id"
                ==> createObj
                  [ "type" ==> "string"
                    "description" ==> "Your short id for this question (kebab-case)." ]
                "tree"
                ==> createObj
                  [ "type" ==> "object"
                    "description"
                    ==> "The question UI as a canonical wire-format Node. Give each answer field a control whose value binding is {\"$type\":\"State\",\"key\":\"<stateKey>\",\"default\":...} – the control's edits commit to that key with no handler (the write-back default)." ]
                "contract"
                ==> createObj
                  [ "type" ==> "object"
                    "description"
                    ==> "{\"fields\":[{\"name\":\"<answer key>\",\"nodeId\":\"<a node id in tree>\",\"stateKey\":\"<the State key>\",\"required\":true|false,\"space\":{\"$type\":\"intRange\",\"min\":..,\"max\":..}|{\"$type\":\"floatRange\",\"min\":..,\"max\":..}|{\"$type\":\"stringLen\",\"min\":..,\"max\":..}|{\"$type\":\"enum\",\"values\":[..]}|{\"$type\":\"anyString\"}}]}" ]
                "timeoutMs"
                ==> createObj
                  [ "type" ==> "integer"
                    "description" ==> "Optional: ms until the host resolves TimedOut." ]
                "default"
                ==> createObj
                  [ "type" ==> "object"
                    "description"
                    ==> "Optional: {\"<name>\": <scalar>} suggested answer; must conform to the contract." ] ]
            "required" ==> [| "id"; "tree"; "contract" |]
            "additionalProperties" ==> true ] } ]

/// The live state the tools read. Getters, so the dispatcher always sees the
/// loop's latest working tree / error log without re-wiring.
type ToolContext =
  {
    CurrentTreeJson: unit -> string option
    RuntimeErrors: unit -> Session.IngestError list
    /// Pre-emit advisories for the CURRENT applied tree (Phase 664) – defects
    /// that don't stop an emission applying but mark dead intent (FUARAN090
    /// inert editable grid, FUARAN069 inert control, …). Same fresh-slate
    /// discipline as `RuntimeErrors`.
    Advisories: unit -> Session.Advisory list
    Dom: DomReader
  }

type ToolDispatchResult = { Ok: bool; Content: string }

/// Dispatch one tool call against the live context. Pure given its context (a set
/// of getters + the DOM reader); never throws.
let dispatchTool (ctx: ToolContext) (name: string) (input: obj) : ToolDispatchResult =
  match name with
  | "getRenderedDom" ->
    let report = ctx.Dom.Read(nodeIdOf input)

    { Ok = true
      Content = jsonPretty report }
  | "getRuntimeErrors" ->
    let errors = ctx.RuntimeErrors()
    let advisories = ctx.Advisories()

    let payload =
      createObj
        [ "count" ==> List.length errors
          "errors"
          ==> (errors
               |> List.map (fun e -> createObj [ "kind" ==> e.Kind; "message" ==> e.Message ])
               |> List.toArray)
          // Phase 664 – applied-but-advisory defects on the CURRENT tree. The
          // emission took effect; each advisory names intent that is dead as
          // authored (with the declarative fix in its message).
          "advisoryCount" ==> List.length advisories
          "advisories"
          ==> (advisories
               |> List.map (fun a ->
                 createObj [ "code" ==> a.Code; "severity" ==> a.Severity; "message" ==> a.Message ])
               |> List.toArray) ]

    { Ok = true
      Content = jsonPretty payload }
  | "getKindSchema" ->
    let report = getKindSchema (kindArgOf input)

    { Ok = true
      Content = jsonPretty report }
  | "getNodeState" ->
    match nodeIdOf input with
    | None ->
      { Ok = false
        Content = jsonPretty (createObj [ "error" ==> "getNodeState requires a string \"nodeId\"." ]) }
    | Some id ->
      let result = getNodeState (ctx.CurrentTreeJson()) id

      { Ok = unbox<bool> result?found
        Content = jsonPretty result }
  | "getBindingValue" ->
    match nodeIdOf input with
    | None ->
      { Ok = false
        Content = jsonPretty (createObj [ "error" ==> "getBindingValue requires a string \"nodeId\"." ]) }
    | Some id ->
      let result = getBindingValue (ctx.CurrentTreeJson()) id

      { Ok = unbox<bool> result?found
        Content = jsonPretty result }
  | "askUser" ->
    // Interactive – handled by the loop itself via the AskUser seam, never by
    // this pure dispatcher. Reaching here means a host without an ask surface.
    { Ok = false
      Content = jsonPretty (createObj [ "error" ==> "askUser is interactive and unavailable in this host." ]) }
  | _ ->
    { Ok = false
      Content = jsonPretty (createObj [ "error" ==> ("Unknown tool: " + name) ]) }

// ─── the agent system prompt (port of agent/agentSystemPrompt.ts) ────────────

let agentSystemPrompt =
  SystemPrompt.value
  + """

# Agent mode – the self-debug loop

You are now running in **agent mode**: an emit → observe → repair loop. Each turn you may emit a tree/op AND call introspection tools to inspect the rendered result, then repair on the next turn.

You have six tools. Five are read-only (all resolve instantly in the browser; none cost a network round-trip beyond this conversation); the sixth, **askUser**, is interactive – it renders a typed question to the user and waits:

- **getKindSchema** – the AUTHORITATIVE shape of a node kind: its required + optional fields and every binding/spec they reference. Pass the kind's $type (e.g. "Select", "Chart", "Tabs"); omit the argument to list every valid kind. Call this BEFORE emitting any kind you are not certain of – it is the exact contract the decoder enforces, so you never have to guess a field name.
- **getRenderedDom** – the REAL geometry of what rendered: each node's bounding box plus flags (overflowing, truncated, hidden). This is ground truth, not your assumption.
- **getRuntimeErrors** – the decode/apply failures from your MOST RECENT emission (the slate clears each emission, so a non-empty result is never a stale pile-up), plus the pre-emit ADVISORIES on the applied tree: an emission can apply cleanly and still carry dead intent (an `editable: true` grid whose source is not `$state` – FUARAN090; a handler-free control bound to nothing writable – FUARAN069). Each advisory names its declarative fix. If an emission "didn't take", call this to see exactly why; a non-empty advisory list after a successful emission means part of what you declared does nothing.
- **getNodeState** – one node's runtime state/style/kind from the live tree.
- **getBindingValue** – a node's data bindings and their values. Note: this playground renders Static emissions only, so non-Static bindings report as unresolved by design – do not treat that as an error.

## How to run the loop

1. **Look up shapes you are unsure of.** Before emitting any kind beyond the simplest (Heading, Markdown, Metric, Card, Stack, Dashboard), call **getKindSchema** with that kind to get its full required-field shape. One schema lookup beats discovering missing fields one decode error at a time – and a kind you "remember" may have required fields (bindings, closures, sub-specs) you don't expect.
2. **Emit** your tree (first turn) or a TreeOp (later turns) in a fenced JSON block, exactly as normal. The playground applies and renders it before your tools run.
3. **Observe** by calling one or more tools in the SAME turn as your emission. Prefer getRenderedDom to confirm the layout actually fits, and getRuntimeErrors if anything looks off.
4. **Repair** on the next turn if the tools reveal a problem (overflow, truncation, a hidden node, a decode error). If a decode error names a missing/wrong field, call getKindSchema for that kind rather than guessing the next field – then emit a TreeOp (or a corrected tree) and observe again.
5. **Stop** when the UI is correct: reply with a brief plain-text confirmation and **call no tools**. A turn with no tool calls ends the loop.

Keep the loop tight – a few rounds at most. The playground enforces hard iteration, token, and time budgets and will halt you if you exceed them, so make each turn count. When in doubt that something rendered correctly, inspect it rather than assume.

## Panel turns – express results AS live UI in the conversation

Besides the main preview, you can stream a **panel** directly into the conversation transcript: a live, working piece of UI (a progress dashboard that fills in, a findings table that grows row by row, a plan tree, a chart) that stays interactive after your turn ends. Use a panel when the UI IS your answer or a live progress report; use the main preview when the user asked you to build them an app.

Emit a panel as one fenced JSON document wrapping a normal Node or TreeOp in a `$panel` envelope:

- Open (or wholesale-replace) a panel: `{"$panel": "<short-id>", "title": "<optional human title>", "tree": <Node>}`
- Extend an existing panel: `{"$panel": "<same-id>", "op": <TreeOp or Batch>}`

Rules: one emission per turn (panel or main-preview, not both); the inner `tree`/`op` is ordinary canonical wire format – everything you know about kinds and ops applies unchanged; panel ids are yours to mint (short, kebab-case) and are how you address the same panel again in ANY later turn ("the findings table above" = another `$panel` op on its id); each panel keeps its own hash-chained op stream, so extend a panel with ops rather than re-emitting its whole tree. A panel is for *expressing* – if you need to ASK the user something with a typed answer, that is the **askUser** tool, not a panel.

## Asking the user – the askUser tool (typed elicitation)

When you genuinely need the user's decision – ambiguous requirements, a choice only they can make – do not ask in prose. Call **askUser** with an elicitation envelope; the user sees your question AS a live form, and you get back one typed outcome, never prose to re-parse.

The tool input IS the envelope object:

- `"$elicitation": "1"` – the format version tag, always "1".
- `"id"` – your short kebab-case id for the question.
- `"tree"` – the question UI, an ordinary canonical Node. Give EVERY answer field a control (Select, Input, Slider, …) whose value binding is `{"$type": "State", "key": "<stateKey>", "default": <initial>}` – with no onChange, the control's edits commit to that key automatically. Add a Markdown node explaining what you are asking and why.
- `"contract"` – which state entries constitute the answer: `{"fields": [{"name": "<answer key>", "nodeId": "<a node id in tree>", "stateKey": "<the State key>", "required": true, "space": {"$type": "enum", "values": ["a", "b"]}}]}`. Spaces: `intRange`/`floatRange` (`min`/`max`), `stringLen` (`min`/`max`), `enum` (`values`), `anyString`.
- `"timeoutMs"` (optional) – ms until the host resolves TimedOut. `"default"` (optional) – a suggested answer object; must conform to the contract. Mirror each State binding's `default` here.

The result is exactly one outcome: `{"$type": "Answered", "answer": {…}}` – each field already validated against its declared space, so trust it and use it directly – or `{"$type": "Declined"}` / `{"$type": "TimedOut"}`. A malformed envelope is refused with a typed decode error naming the exact path; fix it and re-ask.

Etiquette: ask at most ONE question per turn, and only when genuinely blocked – prefer proceeding with a stated assumption for anything minor. On Declined, proceed with your best assumption and say what you assumed. On TimedOut, proceed with your declared default. Keep question trees small: a heading or markdown line plus one control per answer field."""

// ─── the loop (port of agent/agentLoop.ts) ───────────────────────────────────

/// A monotonic clock in ms. Browser `Date.now`; injectable for deterministic tests.
[<Emit("Date.now()")>]
let nowMs () : float = jsNative

/// Yield until the DOM has painted (double rAF), so getRenderedDom observes the
/// freshly-applied tree. A no-op resolves immediately for tests. The rAF is
/// RACED against a short timeout: a hidden/backgrounded tab never fires rAF
/// (browsers throttle it to zero), and without the fallback the whole loop
/// stalls at the first emission until the tab is foregrounded again.
[<Emit("new Promise(function(resolve){ if (typeof requestAnimationFrame === 'undefined') { resolve(); return; } var done=false; var finish=function(){ if(!done){ done=true; resolve(); } }; requestAnimationFrame(function(){ requestAnimationFrame(finish); }); setTimeout(finish, 250); })")>]
let private rafTwice () : JS.Promise<unit> = jsNative

let waitForPaint () : Async<unit> = rafTwice () |> Async.AwaitPromise

type AgentLoopDeps =
  {
    Provider: IAgenticProvider
    Dom: DomReader
    Model: string
    /// Per-call output-token cap passed to the provider.
    MaxTokens: int
    Budget: AgentBudget
    /// Timeline sink – called with each entry as it happens.
    Emit: TimelineEntry -> unit
    /// Working-session sink – called whenever the rendered tree advances.
    OnSession: Session.SessionState -> unit
    /// Panel-store sink (Phase 466) – called whenever a panel-turn emission
    /// advances the panel store.
    OnPanels: Panels.PanelStore -> unit
    /// Polled abort flag (the Stop button); the loop halts at the next checkpoint.
    ShouldAbort: unit -> bool
    /// Yield until the DOM has painted before tools run.
    WaitForPaint: unit -> Async<unit>
    /// Monotonic clock in ms.
    Now: unit -> float
    /// Text appended to the agent system prompt.
    SystemSuffix: string
    /// The `askUser` elicitation seam (Phase 465 §18): given the envelope's
    /// canonical wire JSON (already decode-gated by the loop), present the
    /// question and resolve with the outcome's canonical wire JSON. The browser
    /// host renders a live form in the transcript; a headless host (or an
    /// evaluation harness playing the human) injects a scripted answerer.
    /// Time spent awaiting this does NOT count against the wall-clock budget.
    AskUser: string -> Async<string>
  }

/// A headless `AskUser`: immediately resolves every question as `Declined` –
/// the honest no-surface answer (never a fabricated `Answered`).
let autoDeclineAskUser (envelopeJson: string) : Async<string> =
  async {
    let id =
      match Elicitation.decodeEnvelope envelopeJson with
      | Ok env -> env.ElicitationId
      | Error _ -> "unknown"

    return
      Elicitation.encodeOutcome
        { ElicitationId = id
          Outcome = ElicitationOutcome.Declined }
  }

/// One-line summary of an opened ask, for the timeline row.
let private askSummary (env: ElicitationEnvelope) : string =
  let fields = env.Contract.Fields |> List.map _.Name |> String.concat ", "

  sprintf "%d field(s): %s" (List.length env.Contract.Fields) fields

/// One-line summary of a resolved outcome wire, for the tool-call row.
let private outcomeSummary (outcomeWire: string) : string =
  match Elicitation.decodeOutcome outcomeWire with
  | Error _ -> "outcome"
  | Ok o ->
    match o.Outcome with
    | ElicitationOutcome.Answered answer ->
      let parts =
        answer
        |> Map.toList
        |> List.map (fun (k, v) ->
          let value =
            match v with
            | AnswerValue.Int i -> string i
            | AnswerValue.Float f -> string f
            | AnswerValue.Str s -> s

          k + "=" + value)

      "Answered – " + String.concat ", " parts
    | ElicitationOutcome.Declined -> "Declined"
    | ElicitationOutcome.TimedOut -> "TimedOut"
    | ElicitationOutcome.Superseded _ -> "Superseded"

[<Emit("JSON.stringify($0)")>]
let private jsonStringifyRaw (value: obj) : string = jsNative

let private textOf (content: AgentContentBlock list) : string =
  content
  |> List.choose (fun b ->
    match b with
    | AgentContentBlock.Text t -> Some t
    | _ -> None)
  |> String.concat "\n"

let private toolUsesOf (content: AgentContentBlock list) : (string * string * obj) list =
  content
  |> List.choose (fun b ->
    match b with
    | AgentContentBlock.ToolUse(id, name, input) -> Some(id, name, input)
    | _ -> None)

[<Emit("(function(j){ try { var t = JSON.parse(j).$type; return (typeof t === 'string') ? t : 'op'; } catch(e){ return 'op'; } })($0)")>]
let private opTypeOf (canonJson: string) : string = jsNative

let private emissionSummary (mode: string) (next: Session.SessionState) : string =
  if mode = "tree" then
    "Set the full tree."
  else
    match List.tryLast next.Ops with
    | Some canon -> "Applied " + opTypeOf canon + "."
    | None -> "Applied an op."

/// Run the self-debug loop. Returns when the model is satisfied, a budget is
/// exhausted, the run is aborted, or a provider call fails. Never throws.
///
/// `priorMessages` seeds the conversation for a **resumed** run (Phase 328): on
/// a fresh run it is `[]`; on a follow-up after a halt / Stop it is the previous
/// run's `AgentRunResult.FinalMessages`, so the model keeps its accumulated
/// context (and the new prompt continues the same conversation).
let runAgentLoop
  (initialSession: Session.SessionState)
  (initialPanels: Panels.PanelStore)
  (priorMessages: AgentMessage list)
  (userPrompt: string)
  (deps: AgentLoopDeps)
  : Async<AgentRunResult> =
  let seqN = ref 0

  let nextSeq () =
    let s = seqN.Value
    seqN.Value <- s + 1
    s

  let runtimeErrors = ResizeArray<Session.IngestError>()

  // Phase 664 – pre-emit advisories for the current applied tree, on the same
  // fresh-slate discipline as `runtimeErrors`. `lastAdvisoryFeedback` is the
  // fingerprint of the advisory set last fed back on a no-tools turn, so an
  // identical set is never fed twice (a model that deliberately keeps the
  // shape completes normally; warnings can never burn the budget).
  let advisories = ResizeArray<Session.Advisory>()
  let lastAdvisoryFeedback = ref ""

  let working =
    ref (Session.withTurn initialSession { Role = User; Content = userPrompt })

  // The live panel store (Phase 466) – panels persist across runs (they are
  // seeded from the model), so a later turn can extend a panel opened earlier.
  let panels = ref initialPanels

  // The op-stream author every panel chain entry records for this run.
  let panelAuthor = Actor.Agent(deps.Model, "1", "fuaran-live-loop")

  let messages =
    ref (
      priorMessages
      @ [ { Role = User
            Content = [ AgentContentBlock.Text userPrompt ] } ]
    )

  let iterations = ref 0
  let totalTokens = ref 0
  let lastAssistantText = ref ""
  let startedAt = deps.Now()

  // Milliseconds spent suspended on `askUser` (human thinking time) – excluded
  // from the wall-clock budget so a pondered question can't kill the run.
  let pausedMs = ref 0.0

  let system =
    if deps.SystemSuffix <> "" then
      agentSystemPrompt + deps.SystemSuffix
    else
      agentSystemPrompt

  let ctx: ToolContext =
    { CurrentTreeJson =
        fun () ->
          match working.Value.Tree with
          | None -> None
          | Some t -> Some(Canon.encodeNode t)
      RuntimeErrors = fun () -> List.ofSeq runtimeErrors
      Advisories = fun () -> List.ofSeq advisories
      Dom = deps.Dom }

  let halt (reason: HaltReason) (detail: string option) (provErr: ProviderError option) : AgentRunResult =
    deps.Emit(TimelineEntry.Halt(nextSeq (), reason, detail))

    if reason = HaltReason.Completed && lastAssistantText.Value.Trim() <> "" then
      working.Value <-
        Session.withTurn
          working.Value
          { Role = Assistant
            Content = lastAssistantText.Value.Trim() }

      deps.OnSession working.Value

    { HaltReason = reason
      Iterations = iterations.Value
      TotalTokens = totalTokens.Value
      RuntimeErrors = List.ofSeq runtimeErrors
      ProviderError = provErr
      FinalMessages = messages.Value }

  let rec loop () : Async<AgentRunResult> =
    async {
      if deps.ShouldAbort() then
        return halt HaltReason.Aborted None None
      elif iterations.Value >= deps.Budget.MaxIterations then
        return halt HaltReason.MaxIterations None None
      elif totalTokens.Value >= deps.Budget.MaxTokens then
        return halt HaltReason.MaxTokens None None
      elif deps.Now() - startedAt - pausedMs.Value >= float deps.Budget.MaxWallClockMs then
        return halt HaltReason.MaxWallClock None None
      else
        iterations.Value <- iterations.Value + 1

        let request =
          { System = system
            Messages = messages.Value
            Model = deps.Model
            MaxTokens = deps.MaxTokens
            Tools = toolDefinitions }

        let! outcome = deps.Provider.SendAgentic request

        match outcome with
        | AgentOutcome.Error e ->
          if deps.ShouldAbort() then
            return halt HaltReason.Aborted None None
          else
            return halt HaltReason.ProviderError (Some e.Message) (Some e)
        | AgentOutcome.Ok(content, _stopReason, usage) ->
          match usage with
          | Some u -> totalTokens.Value <- totalTokens.Value + u.InputTokens + u.OutputTokens
          | None -> ()

          deps.Emit(TimelineEntry.ModelCall(nextSeq (), iterations.Value, usage))
          messages.Value <- messages.Value @ [ { Role = Assistant; Content = content } ]

          let text = textOf content

          if text.Trim() <> "" then
            lastAssistantText.Value <- text
            deps.Emit(TimelineEntry.ModelText(nextSeq (), text))

          // Ingest any emission in the assistant text BEFORE running tools, so the
          // tools observe the freshly-applied tree. `emissionError` records a failed
          // ingest THIS turn, so a model that emits-and-stops over a broken emission
          // is fed the failure rather than mistaken for "satisfied" (see below).
          let mutable emissionError: string option = None

          if Session.hasJson text then
            // Fresh slate per emission: getRuntimeErrors must report THIS emission's
            // outcome, not a cumulative pile from every prior attempt. The unscoped
            // log made the count climb every turn, which misled the model into
            // thinking already-replaced trees were still failing. A turn that emits
            // nothing does not clear it – the prior failure stays visible to repair.
            runtimeErrors.Clear()
            advisories.Clear()

            // Route by envelope: a `$panel` document streams into its named live
            // panel (Phase 466 – its own scoped, hash-chained op stream); a bare
            // Node/TreeOp targets the main preview session exactly as before.
            let panelPayload = Session.extractJson text |> Option.bind Panels.tryPayload

            match panelPayload with
            | Some payload ->
              match Panels.ingest panels.Value panelAuthor payload with
              | Panels.PanelApplied(store, panel, mode, isNew, summary) ->
                panels.Value <- store
                deps.OnPanels store
                deps.Emit(TimelineEntry.PanelEmission(nextSeq (), panel.Id, mode, true, isNew, summary))
                do! deps.WaitForPaint()
              | Panels.PanelFailed err ->
                runtimeErrors.Add err
                emissionError <- Some err.Message
                deps.Emit(TimelineEntry.PanelEmission(nextSeq (), payload.PanelId, "panel", false, false, err.Message))
            | None ->
              // Phase 712 — the loop knows which model produced this text, so
              // the op it applies is recorded against that model rather than a
              // bare "an agent did it". Same reasoning as `panelAuthor` above.
              match Session.ingestBy (Session.modelActor deps.Model) working.Value text with
              | Session.Ingested(mode, next) ->
                working.Value <-
                  { next with
                      History = working.Value.History }

                // Phase 664 – the emission APPLIED; compute its advisory set
                // (applied-but-dead intent, e.g. FUARAN090) for the tool
                // surface, the timeline summary, and the no-tools feedback.
                advisories.AddRange(Session.preEmitAdvisories next)

                let summary =
                  let baseSummary = emissionSummary mode next

                  if advisories.Count = 0 then
                    baseSummary
                  else
                    let codes = advisories |> Seq.map _.Code |> Seq.distinct |> String.concat ", "

                    sprintf "%s %d pre-emit advisory(ies): %s." baseSummary advisories.Count codes

                deps.OnSession working.Value
                deps.Emit(TimelineEntry.Emission(nextSeq (), mode, true, summary))
                do! deps.WaitForPaint()
              | Session.IngestFailed err ->
                runtimeErrors.Add err
                emissionError <- Some err.Message

                deps.Emit(
                  TimelineEntry.Emission(
                    nextSeq (),
                    (if working.Value.Tree.IsNone then "tree" else "op"),
                    false,
                    err.Message
                  )
                )

          let toolUses = toolUsesOf content

          if List.isEmpty toolUses then
            match emissionError with
            | Some msg ->
              // The model emitted something that did NOT apply and then asked for no
              // tools – it believes it is done, but the preview is unchanged/empty.
              // Halting as Completed here is the "satisfied over a broken UI" bug.
              // Feed the failure back as a plain turn so the self-debug loop can
              // actually self-correct; the iteration/token budget still bounds it.
              messages.Value <-
                messages.Value
                @ [ { Role = User
                      Content =
                        [ AgentContentBlock.Text(
                            "Your last emission did NOT apply – the preview is unchanged. Decode/apply error: "
                            + msg
                            + "\n\nThis turn called no tools, so nothing was verified. Emit a corrected version. "
                            + "If the error says there is no tree yet, emit the COMPLETE Node tree first – a TreeOp/Batch only edits a tree that already exists. "
                            + "If a field is missing or malformed, call getKindSchema for that kind to get its exact shape before re-emitting."
                          ) ] } ]

              return! loop ()
            | None ->
              // Phase 664 – the emission applied, the model called no tools, but
              // the tree carries pre-emit advisories: intent that is dead as
              // authored (an inert editable grid, an unwired control). Feed the
              // advisory set back ONCE so the model can self-correct; an
              // identical set is never fed twice (fingerprint guard), so a model
              // that keeps the shape deliberately completes normally.
              let fingerprint =
                advisories |> Seq.map (fun a -> a.Code + "|" + a.Message) |> String.concat "\n"

              if advisories.Count > 0 && fingerprint <> lastAdvisoryFeedback.Value then
                lastAdvisoryFeedback.Value <- fingerprint

                let listing =
                  advisories
                  |> Seq.map (fun a -> sprintf "- %s (%s): %s" a.Code a.Severity a.Message)
                  |> String.concat "\n"

                messages.Value <-
                  messages.Value
                  @ [ { Role = User
                        Content =
                          [ AgentContentBlock.Text(
                              "Your emission APPLIED and the preview updated, but the tree carries pre-emit advisories – declared intent that does nothing as authored:\n\n"
                              + listing
                              + "\n\nEach message names the declarative fix. Emit a corrected tree/op if the intent matters; if the current shape is deliberate, say so and finish."
                            ) ] } ]

                return! loop ()
              else
                return halt HaltReason.Completed None None
          else
            // Sequential (not mapped) because `askUser` is async: it suspends
            // the loop on the human. The read-only tools stay synchronous.
            let results = ResizeArray<AgentContentBlock>()

            for (id, name, input) in toolUses do
              if name = "askUser" then
                let envelopeJson = jsonStringifyRaw input

                match Elicitation.decodeEnvelope envelopeJson with
                | Error e ->
                  // The typed refusal is the tool result – the model repairs
                  // its envelope exactly as it repairs a broken emission.
                  let content =
                    jsonPretty (
                      createObj
                        [ "error" ==> "elicitation envelope refused"
                          "code" ==> e.Code
                          "path" ==> e.Path
                          "message" ==> e.Message ]
                    )

                  deps.Emit(
                    TimelineEntry.ToolCall(
                      nextSeq (),
                      name,
                      input,
                      "envelope refused: " + e.Code + " at " + e.Path,
                      false
                    )
                  )

                  results.Add(AgentContentBlock.ToolResult(id, content, true))
                | Ok env ->
                  // Mount the live form row, suspend on the human, and thread
                  // the typed outcome back. Thinking time is off the clock.
                  deps.Emit(TimelineEntry.Ask(nextSeq (), env.ElicitationId, askSummary env))
                  let t0 = deps.Now()
                  let! outcomeWire = deps.AskUser envelopeJson
                  pausedMs.Value <- pausedMs.Value + (deps.Now() - t0)
                  deps.Emit(TimelineEntry.ToolCall(nextSeq (), name, input, outcomeSummary outcomeWire, true))
                  results.Add(AgentContentBlock.ToolResult(id, outcomeWire, false))
              else
                let r = dispatchTool ctx name input
                deps.Emit(TimelineEntry.ToolCall(nextSeq (), name, input, summariseResult r.Content, r.Ok))
                results.Add(AgentContentBlock.ToolResult(id, r.Content, not r.Ok))

            messages.Value <-
              messages.Value
              @ [ { Role = User
                    Content = List.ofSeq results } ]

            return! loop ()
    }

  async {
    deps.OnSession working.Value
    deps.Emit(TimelineEntry.UserPrompt(nextSeq (), userPrompt))
    return! loop ()
  }

// ─── flat test surface (cross-boundary friendly – used by the loop tests) ─────
//
// As in Session.fs, the F# DU / Async results are awkward to assert on from the
// vitest (TS-over-Fable-output) side. These helpers project the same logic to
// flat values (anonymous records / a Promise), so the pure tool dispatch and the
// full loop – driven by a MOCK agentic provider scripted in F# – are testable
// headlessly without a live LLM or a DOM.

/// `dispatchTool` over a default context (null DOM, no runtime errors), with the
/// tree JSON passed flat (`null` ⇒ no tree). Lets the test exercise the three
/// pure tools directly.
let dispatchToolFlat (treeJson: string) (name: string) (input: obj) : {| Ok: bool; Content: string |} =
  let ctx: ToolContext =
    { CurrentTreeJson = fun () -> Option.ofObj treeJson
      RuntimeErrors = fun () -> []
      Advisories = fun () -> []
      Dom = nullDomReader }

  let r = dispatchTool ctx name input
  {| Ok = r.Ok; Content = r.Content |}

/// A scripted mock agentic provider: returns the i-th canned outcome per call,
/// clamping to the last (so an over-budget loop keeps getting the final outcome).
let scriptedAgenticProvider (script: AgentOutcome list) : IAgenticProvider =
  let calls = ref 0

  { new IAgenticProvider with
      member _.Id = "mock"
      member _.Label = "Mock"
      member _.DefaultModel = "mock-model"

      member _.Send(_request) =
        async { return ProviderOutcome.Ok("", None) }

      member _.SendAgentic(_request) =
        async {
          let i = calls.Value
          calls.Value <- i + 1
          let idx = if i < List.length script then i else List.length script - 1
          return List.item idx script
        } }

let private probeDeps
  (provider: IAgenticProvider)
  (budget: AgentBudget)
  (timeline: ResizeArray<TimelineEntry>)
  : AgentLoopDeps =
  { Provider = provider
    Dom = nullDomReader
    Model = "mock-model"
    MaxTokens = 1024
    Budget = budget
    Emit = timeline.Add
    OnSession = ignore
    OnPanels = ignore
    ShouldAbort = fun () -> false
    WaitForPaint = fun () -> async { return () }
    Now = nowMs
    SystemSuffix = ""
    AskUser = autoDeclineAskUser }

/// Drive the loop against a mock provider scripted `tool_use → end_turn`: turn 1
/// emits text + a tool call on `nodeId`; turn 2 ends the turn with no tools. The
/// session is seeded with `seedEmission` so the tool finds a real node. Proves
/// tool-running + result-threading (the 2nd provider call must carry the
/// tool_result) + the end_turn halt. Returns a flat result as a JS Promise.
let runLoopProbe
  (seedEmission: string)
  (toolName: string)
  (nodeId: string)
  : JS.Promise<
      {| HaltReason: string
         Iterations: int
         ToolCalls:
           {| Name: string
              Ok: bool
              ResultSummary: string |} array
         SecondTurnThreadedToolResult: bool
         TimelineKinds: string array |}
     >
  =
  let seeded =
    match Session.ingest Session.empty seedEmission with
    | Session.Ingested(_, s) -> s
    | Session.IngestFailed _ -> Session.empty

  let secondTurnThreaded = ref false
  let calls = ref 0

  let script =
    [ AgentOutcome.Ok(
        [ AgentContentBlock.Text "Let me inspect what rendered."
          AgentContentBlock.ToolUse("tu-1", toolName, createObj [ "nodeId" ==> nodeId ]) ],
        AgentStopReason.ToolUse,
        Some { InputTokens = 10; OutputTokens = 5 }
      )
      AgentOutcome.Ok(
        [ AgentContentBlock.Text "The UI looks correct." ],
        AgentStopReason.EndTurn,
        Some { InputTokens = 8; OutputTokens = 3 }
      ) ]

  let provider =
    { new IAgenticProvider with
        member _.Id = "mock"
        member _.Label = "Mock"
        member _.DefaultModel = "mock-model"

        member _.Send(_request) =
          async { return ProviderOutcome.Ok("", None) }

        member _.SendAgentic(request) =
          async {
            let i = calls.Value
            calls.Value <- i + 1

            if i = 1 then
              secondTurnThreaded.Value <-
                request.Messages
                |> List.exists (fun m ->
                  m.Content
                  |> List.exists (fun b ->
                    match b with
                    | AgentContentBlock.ToolResult _ -> true
                    | _ -> false))

            let idx = if i < List.length script then i else List.length script - 1
            return List.item idx script
          } }

  let timeline = ResizeArray<TimelineEntry>()

  async {
    let! result = runAgentLoop seeded Panels.empty [] "Inspect the metric." (probeDeps provider defaultBudget timeline)

    let toolCalls =
      timeline
      |> Seq.choose (fun e ->
        match e with
        | TimelineEntry.ToolCall(_, name, _, summary, ok) ->
          Some
            {| Name = name
               Ok = ok
               ResultSummary = summary |}
        | _ -> None)
      |> Seq.toArray

    return
      {| HaltReason = haltReasonStr result.HaltReason
         Iterations = result.Iterations
         ToolCalls = toolCalls
         SecondTurnThreadedToolResult = secondTurnThreaded.Value
         TimelineKinds = timeline |> Seq.map timelineKind |> Seq.toArray |}
  }
  |> Async.StartAsPromise

/// Drive the loop against a provider that always wants more tools, with a small
/// iteration budget – proves the budget halts a runaway loop. Returns flat.
let runBudgetProbe
  (maxIterations: int)
  : JS.Promise<
      {| HaltReason: string
         Iterations: int |}
     >
  =
  let provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.ToolUse("tu", "getRuntimeErrors", createObj []) ],
          AgentStopReason.ToolUse,
          None
        ) ]

  let timeline = ResizeArray<TimelineEntry>()

  let deps =
    probeDeps
      provider
      { defaultBudget with
          MaxIterations = maxIterations }
      timeline

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "loop forever" deps

    return
      {| HaltReason = haltReasonStr result.HaltReason
         Iterations = result.Iterations |}
  }
  |> Async.StartAsPromise

/// Drive the loop where turn 1 emits a TreeOp with no tree yet (ingest fails) and
/// calls NO tools – the "emit-and-stop over a broken emission" case – then turn 2
/// emits a valid full tree, also no tools. Proves the loop does NOT halt as
/// Completed on the failed turn (it feeds the failure back so the model can
/// recover), and DOES complete once a good tree lands. Returns flat.
let runEmitRepairProbe
  ()
  : JS.Promise<
      {| HaltReason: string
         Iterations: int
         FinalTreeSet: bool
         FinalErrorCount: int |}
     >
  =
  let failingOp =
    "Editing the tree.\n\n```json\n{\"$type\":\"RemoveNode\",\"target\":\"ghost\"}\n```"

  let fullTree =
    "Here is the full tree.\n\n```json\n{\"id\":\"m\",\"kind\":{\"$type\":\"Markdown\",\"text\":{\"$type\":\"Literal\",\"text\":\"hi\"}}}\n```"

  let provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.Text failingOp ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        )
        AgentOutcome.Ok(
          [ AgentContentBlock.Text fullTree ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        ) ]

  let timeline = ResizeArray<TimelineEntry>()
  let lastSession = ref Session.empty

  let deps =
    { probeDeps provider defaultBudget timeline with
        OnSession = fun s -> lastSession.Value <- s }

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "build it" deps

    return
      {| HaltReason = haltReasonStr result.HaltReason
         Iterations = result.Iterations
         FinalTreeSet = lastSession.Value.Tree.IsSome
         FinalErrorCount = List.length result.RuntimeErrors |}
  }
  |> Async.StartAsPromise

// ─── Phase 664 probes – pre-emit advisories in the loop ──────────────────────

/// A valid grid emission that APPLIES but is inert-editable (FUARAN090):
/// `editable: true` over a Transform source.
let private inertEditableEmission =
  "Here is the grid.\n\n```json\n{\"id\":\"g\",\"kind\":{\"$type\":\"DataGrid\",\"editable\":true,\"rowKeyField\":\"q\",\"columns\":[{\"field\":\"q\",\"kind\":{\"$type\":\"Text\"},\"label\":\"Q\"}],\"source\":{\"$type\":\"Transform\",\"pipeline\":[],\"source\":{\"columns\":{\"q\":{\"validity\":[true],\"values\":[\"Q1\"]}},\"schema\":[{\"name\":\"q\",\"type\":\"string\"}]}}}}\n```"

/// The corrected shape – the same grid over a shared `$state` rows source
/// (the Phase 663 write-back floor); draws no advisories.
let private stateEditableEmission =
  "Corrected.\n\n```json\n{\"id\":\"g\",\"kind\":{\"$type\":\"DataGrid\",\"editable\":true,\"rowKeyField\":\"q\",\"columns\":[{\"field\":\"q\",\"kind\":{\"$type\":\"Text\"},\"label\":\"Q\"}],\"source\":{\"$type\":\"State\",\"key\":\"rows\",\"defaultValue\":[{\"q\":\"Q1\"}]}}}\n```"

/// Count the advisory feedback turns the loop injected (the no-tools leg).
let private advisoryFeedbackCount (msgs: AgentMessage list) : int =
  msgs
  |> List.filter (fun m ->
    m.Role = User
    && m.Content
       |> List.exists (fun b ->
         match b with
         | AgentContentBlock.Text t -> t.Contains "pre-emit advisories"
         | _ -> false))
  |> List.length

let private emissionSummaries (timeline: ResizeArray<TimelineEntry>) : string list =
  timeline
  |> Seq.choose (fun e ->
    match e with
    | TimelineEntry.Emission(_, _, _, summary) -> Some summary
    | _ -> None)
  |> List.ofSeq

/// Turn 1 emits an inert-editable grid (applies; FUARAN090) with no tools –
/// the loop must feed the advisory back rather than halting satisfied. Turn 2
/// emits the corrected `$state`-sourced grid – the loop completes with a clean
/// advisory slate. Returns flat.
let runAdvisoryProbe
  ()
  : JS.Promise<
      {| HaltReason: string
         Iterations: int
         FeedbackTurns: int
         FeedbackNamesFuaran090: bool
         EmissionSummaryNamesFuaran090: bool
         FinalAdvisoryCount: int |}
     >
  =
  let provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.Text inertEditableEmission ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        )
        AgentOutcome.Ok(
          [ AgentContentBlock.Text stateEditableEmission ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        ) ]

  let timeline = ResizeArray<TimelineEntry>()
  let lastSession = ref Session.empty

  let deps =
    { probeDeps provider defaultBudget timeline with
        OnSession = fun s -> lastSession.Value <- s }

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "build an editable grid" deps

    let feedbackTexts =
      result.FinalMessages
      |> List.collect (fun m ->
        m.Content
        |> List.choose (fun b ->
          match b with
          | AgentContentBlock.Text t when t.Contains "pre-emit advisories" -> Some t
          | _ -> None))

    return
      {| HaltReason = haltReasonStr result.HaltReason
         Iterations = result.Iterations
         FeedbackTurns = advisoryFeedbackCount result.FinalMessages
         FeedbackNamesFuaran090 = feedbackTexts |> List.exists _.Contains("FUARAN090")
         EmissionSummaryNamesFuaran090 = emissionSummaries timeline |> List.exists _.Contains("FUARAN090")
         FinalAdvisoryCount = List.length (Session.preEmitAdvisories lastSession.Value) |}
  }
  |> Async.StartAsPromise

/// Both turns emit the SAME inert-editable wire – the fingerprint guard must
/// feed the advisory set exactly once and then complete (warnings can never
/// burn the budget). Returns flat.
let runAdvisoryRepeatProbe
  ()
  : JS.Promise<
      {| HaltReason: string
         Iterations: int
         FeedbackTurns: int |}
     >
  =
  let provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.Text inertEditableEmission ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        )
        AgentOutcome.Ok(
          [ AgentContentBlock.Text inertEditableEmission ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        ) ]

  let timeline = ResizeArray<TimelineEntry>()

  let deps = probeDeps provider defaultBudget timeline

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "build an editable grid" deps

    return
      {| HaltReason = haltReasonStr result.HaltReason
         Iterations = result.Iterations
         FeedbackTurns = advisoryFeedbackCount result.FinalMessages |}
  }
  |> Async.StartAsPromise

/// Drive the loop twice to prove **resumability** (Phase 328): run 1 emits a
/// tree and halts on a budget (one iteration, model still wants tools); run 2
/// resumes from run 1's `FinalMessages` with a follow-up prompt and completes.
/// Asserts run 2 carried the prior context (its message seed is non-empty and
/// longer than a cold start) and that the token counter accumulates across both
/// runs. Returns flat.
let runResumeProbe
  ()
  : JS.Promise<
      {| Run1Halt: string
         Run1Tokens: int
         Run2Halt: string
         Run2Tokens: int
         ResumedWithContext: bool
         Run2SeedHadPrior: bool |}
     >
  =
  // Run 1: a single tool call then (budget halts before it can finish).
  let run1Provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.Text "Inspecting."
            AgentContentBlock.ToolUse("tu", "getRuntimeErrors", createObj []) ],
          AgentStopReason.ToolUse,
          Some { InputTokens = 12; OutputTokens = 6 }
        ) ]

  // Run 2: ends the turn immediately (no tools) – completes.
  let run2Provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.Text "All good now." ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 4; OutputTokens = 2 }
        ) ]

  let tl1 = ResizeArray<TimelineEntry>()
  let tl2 = ResizeArray<TimelineEntry>()

  // Run 1 halts at one iteration via the iteration backstop.
  let deps1 = probeDeps run1Provider { defaultBudget with MaxIterations = 1 } tl1

  async {
    let! r1 = runAgentLoop Session.empty Panels.empty [] "build it" deps1
    // Run 2 resumes from run 1's accumulated context.
    let deps2 = probeDeps run2Provider defaultBudget tl2
    let priorSeed = r1.FinalMessages
    let! r2 = runAgentLoop Session.empty Panels.empty priorSeed "now fix the heading" deps2

    return
      {| Run1Halt = haltReasonStr r1.HaltReason
         Run1Tokens = r1.TotalTokens
         Run2Halt = haltReasonStr r2.HaltReason
         Run2Tokens = r2.TotalTokens
         // Run 2's final message list must include run 1's prior turns plus the
         // new prompt – i.e. it is strictly longer than the seed alone.
         ResumedWithContext = (List.length r2.FinalMessages > List.length priorSeed)
         Run2SeedHadPrior = (not (List.isEmpty priorSeed)) |}
  }
  |> Async.StartAsPromise

/// Drive the loop through a full panel-turn conversation (Phase 466): turn 1
/// opens a `$panel` with a tree; turn 2 extends it with an op; turn 3 emits a
/// BROKEN panel op (unknown target node) with no tools – proving a failed
/// panel emission is fed back for repair, not mistaken for "satisfied"; turn 4
/// ends clean. Asserts the panel store contents, the per-panel hash chain, the
/// replay verification (refold + chain recompute), and the timeline row kinds.
/// The main-preview session must be untouched throughout (panel emissions
/// never leak into it). Returns flat.
let runPanelProbe
  ()
  : JS.Promise<
      {| HaltReason: string
         PanelCount: int
         PanelId: string
         PanelTitle: string
         OpsApplied: int
         ChainLength: int
         ChainLinked: bool
         ReplayOk: bool
         ChainOk: bool
         FirstEmissionWasNew: bool
         SecondEmissionWasNew: bool
         BrokenOpFedBack: bool
         MainSessionUntouched: bool
         PanelKindsInTimeline: int
         PanelSummaries: string array |}
     >
  =
  // The scripted emissions are authored with the typed API and encoded through
  // the real canonical encoder – the probe tests the panel CHANNEL, not the
  // author's memory of wire shapes.
  let findingsTree: Fuaran.UI.Types.Node<obj> =
    Fuaran.UI.Fuaran.card
      "f-root"
      { Fuaran.UI.Defaults.card with
          Heading = Some(Fuaran.UI.Types.TextSource.Literal "Findings")
          Children = [ Fuaran.UI.Fuaran.markdown "f-head" "**Findings so far**" ] }

  let rowNode (id: string) (text: string) : Fuaran.UI.Types.Node<obj> = Fuaran.UI.Fuaran.markdown id text

  // 0.4.0: InsertChild appends; ReorderChildren states order. Both call sites
  // below appended anyway (f-root holds one child; "ghost" is a reject probe),
  // so the position parameter is dropped rather than replaced with a Batch.
  let insertRowOp (parent: string) (row: Fuaran.UI.Types.Node<obj>) : string =
    Canon.encodeOp (Fuaran.UI.Ops.Types.TreeOp.InsertChild(Fuaran.UI.Types.NodeId parent, row))

  let fence (envelope: string) : string = "\n\n```json\n" + envelope + "\n```"

  let panelTree =
    "Opening a live findings panel."
    + fence (
      "{\"$panel\":\"findings\",\"title\":\"Findings\",\"tree\":"
      + Canon.encodeNode findingsTree
      + "}"
    )

  let panelOp =
    "Adding the first finding."
    + fence (
      "{\"$panel\":\"findings\",\"op\":"
      + insertRowOp "f-root" (rowNode "f-row-1" "1. The heading overflows on mobile.")
      + "}"
    )

  let brokenOp =
    "Adding another."
    + fence (
      "{\"$panel\":\"findings\",\"op\":"
      + insertRowOp "ghost" (rowNode "f-row-x" "nope")
      + "}"
    )

  // Turns 1–2 carry a tool call (the loop's continuation driver – a turn with
  // no tools ends the loop); turn 3's broken op deliberately calls NO tools,
  // exercising the fed-back-for-repair guard on the panel path; turn 4 ends.
  let provider =
    scriptedAgenticProvider
      [ AgentOutcome.Ok(
          [ AgentContentBlock.Text panelTree
            AgentContentBlock.ToolUse("tu-1", "getRuntimeErrors", createObj []) ],
          AgentStopReason.ToolUse,
          Some { InputTokens = 5; OutputTokens = 5 }
        )
        AgentOutcome.Ok(
          [ AgentContentBlock.Text panelOp
            AgentContentBlock.ToolUse("tu-2", "getRuntimeErrors", createObj []) ],
          AgentStopReason.ToolUse,
          Some { InputTokens = 5; OutputTokens = 5 }
        )
        AgentOutcome.Ok(
          [ AgentContentBlock.Text brokenOp ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 5; OutputTokens = 5 }
        )
        AgentOutcome.Ok(
          [ AgentContentBlock.Text "Done – one finding so far." ],
          AgentStopReason.EndTurn,
          Some { InputTokens = 3; OutputTokens = 2 }
        ) ]

  let timeline = ResizeArray<TimelineEntry>()
  let lastPanels = ref Panels.empty
  let lastSession = ref Session.empty
  let sessionTouched = ref false
  let brokenFedBack = ref false

  // The broken-op turn calls no tools, so the loop must feed the failure back
  // as a user turn (the anti-"satisfied over a broken emission" guard) – we
  // detect that by the message context growing a user turn naming the failure.
  let deps =
    { probeDeps provider defaultBudget timeline with
        OnPanels = fun p -> lastPanels.Value <- p
        OnSession =
          fun s ->
            lastSession.Value <- s

            if s.Tree.IsSome then
              sessionTouched.Value <- true }

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "audit this design" deps

    brokenFedBack.Value <-
      result.FinalMessages
      |> List.exists (fun m ->
        m.Role = User
        && m.Content
           |> List.exists (fun b ->
             match b with
             | AgentContentBlock.Text t -> t.Contains "did NOT apply"
             | _ -> false))

    let panels = Panels.panelsInOrder lastPanels.Value
    let panel = List.tryHead panels

    let verifyReport = panel |> Option.map Panels.verify

    let panelEmissions =
      timeline
      |> Seq.choose (fun e ->
        match e with
        | TimelineEntry.PanelEmission(_, _, _, ok, isNew, _) -> Some(ok, isNew)
        | _ -> None)
      |> Seq.toList

    let panelSummaries =
      timeline
      |> Seq.choose (fun e ->
        match e with
        | TimelineEntry.PanelEmission(_, _, _, ok, _, summary) -> Some((if ok then "ok: " else "FAIL: ") + summary)
        | _ -> None)
      |> Seq.toArray

    let chainLinked =
      match panel with
      | Some p ->
        match p.Chain with
        | [ entry ] -> entry.Prev = p.BaseHash && entry.Hash <> "" && entry.Seq = 1
        | _ -> false
      | None -> false

    return
      {| HaltReason = haltReasonStr result.HaltReason
         PanelCount = List.length panels
         PanelId = panel |> Option.map _.Id |> Option.defaultValue ""
         PanelTitle = panel |> Option.bind _.Title |> Option.defaultValue ""
         OpsApplied = panel |> Option.map (fun p -> List.length p.OpsJson) |> Option.defaultValue -1
         ChainLength = panel |> Option.map (fun p -> List.length p.Chain) |> Option.defaultValue -1
         ChainLinked = chainLinked
         ReplayOk = verifyReport |> Option.map _.ReplayOk |> Option.defaultValue false
         ChainOk = verifyReport |> Option.map _.ChainOk |> Option.defaultValue false
         FirstEmissionWasNew =
          (match List.tryItem 0 panelEmissions with
           | Some(true, isNew) -> isNew
           | _ -> false)
         SecondEmissionWasNew =
          (match List.tryItem 1 panelEmissions with
           | Some(true, isNew) -> isNew
           | _ -> true)
         BrokenOpFedBack = brokenFedBack.Value
         MainSessionUntouched = not sessionTouched.Value
         PanelKindsInTimeline = panelEmissions |> List.length
         PanelSummaries = panelSummaries |}
  }
  |> Async.StartAsPromise

// ─── the askUser probes (Phase 465 §18 in the live loop) ─────────────────────
//
// The elicitation seam exercised headlessly: a scripted provider issues an
// `askUser` tool call, a SCRIPTED answerer plays the human (the same injection
// point an evaluation harness uses), and the probe asserts the typed outcome
// threads back into the model's next turn. No DOM, no LLM.

/// A minimal valid elicitation envelope (a markdown + select question with one
/// enum-typed answer field), as canonical wire JSON. Authored typed and encoded
/// through the shipped codec, so the probe drives the REAL envelope path.
let private probeEnvelopeWire () : string =
  let flavourOptions: Fuaran.UI.Types.SelectOption list =
    [ { Value = "compact"
        Label = Fuaran.UI.Types.TextSource.Literal "Compact" }
      { Value = "detailed"
        Label = Fuaran.UI.Types.TextSource.Literal "Detailed" } ]

  let tree: Fuaran.UI.Types.Node<obj> =
    Fuaran.UI.Fuaran.stack
      "ask-root"
      { Fuaran.UI.Defaults.stack with
          Children =
            [ Fuaran.UI.Fuaran.markdown "ask-why" "Which flavour should I build?"
              Fuaran.UI.Fuaran.select
                "ask-flavour"
                { Fuaran.UI.Defaults.select with
                    Label = Fuaran.UI.Types.TextSource.Literal "Flavour"
                    Source = Fuaran.UI.Types.Binding.Static flavourOptions
                    Value = Fuaran.UI.Types.Binding.State("ask-flavour-value", Some "compact") } ] }

  let envelope: ElicitationEnvelope =
    { ElicitationId = "probe-ask-1"
      Tree = tree
      Contract =
        { Fields =
            [ { Name = "flavour"
                NodeId = Fuaran.UI.Types.NodeId "ask-flavour"
                StateKey = "ask-flavour-value"
                Space = Fuaran.Core.Enum [ "compact"; "detailed" ]
                Required = true } ] }
      TimeoutMs = None
      Default = Some(Map [ "flavour", AnswerValue.Str "compact" ]) }

  match Elicitation.encodeEnvelope envelope with
  | Ok wire -> wire
  | Error e -> failwith ("probe envelope failed its own encode: " + e.Code)

[<Emit("JSON.parse($0)")>]
let private jsonParseRaw (json: string) : obj = jsNative

/// Drive the loop through a full ask-and-answer round trip: turn 1 calls
/// `askUser` with a valid envelope; the scripted answerer resolves per
/// `outcomeKind` ("answered" resolves a conforming enum answer; anything else
/// resolves Declined); turn 2 ends the run. Asserts the outcome wire threads
/// back as the tool result the model reads and the timeline carries the `ask`
/// row. Returns flat.
let runAskProbe
  (outcomeKind: string)
  : JS.Promise<
      {| HaltReason: string
         Iterations: int
         TimelineKinds: string array
         ToolSummaries: string array
         ThreadedOutcome: string
         AskedElicitationId: string |}
     >
  =
  let envelopeWire = probeEnvelopeWire ()

  let scriptedAnswerer (envJson: string) : Async<string> =
    async {
      let id =
        match Elicitation.decodeEnvelope envJson with
        | Ok env -> env.ElicitationId
        | Error _ -> "unknown"

      let outcome =
        if outcomeKind = "answered" then
          ElicitationOutcome.Answered(Map [ "flavour", AnswerValue.Str "detailed" ])
        else
          ElicitationOutcome.Declined

      return
        Elicitation.encodeOutcome
          { ElicitationId = id
            Outcome = outcome }
    }

  // The provider records what the loop threads back on its second call.
  let threadedOutcome = ref ""
  let calls = ref 0

  let script =
    [ AgentOutcome.Ok(
        [ AgentContentBlock.Text "I need your decision before building."
          AgentContentBlock.ToolUse("tu-ask", "askUser", jsonParseRaw envelopeWire) ],
        AgentStopReason.ToolUse,
        Some { InputTokens = 10; OutputTokens = 5 }
      )
      AgentOutcome.Ok(
        [ AgentContentBlock.Text "Thanks – proceeding with your choice." ],
        AgentStopReason.EndTurn,
        Some { InputTokens = 8; OutputTokens = 3 }
      ) ]

  let provider =
    { new IAgenticProvider with
        member _.Id = "mock"
        member _.Label = "Mock"
        member _.DefaultModel = "mock-model"

        member _.Send(_request) =
          async { return ProviderOutcome.Ok("", None) }

        member _.SendAgentic(request) =
          async {
            let i = calls.Value
            calls.Value <- i + 1

            if i = 1 then
              threadedOutcome.Value <-
                request.Messages
                |> List.collect _.Content
                |> List.tryPick (fun b ->
                  match b with
                  | AgentContentBlock.ToolResult(_, content, _) -> Some content
                  | _ -> None)
                |> Option.defaultValue ""

            let idx = min i (List.length script - 1)
            return List.item idx script
          } }

  let timeline = ResizeArray<TimelineEntry>()

  let deps =
    { probeDeps provider defaultBudget timeline with
        AskUser = scriptedAnswerer }

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "Build me a thing." deps

    let askedId =
      timeline
      |> Seq.tryPick (fun e ->
        match e with
        | TimelineEntry.Ask(_, id, _) -> Some id
        | _ -> None)
      |> Option.defaultValue ""

    let toolSummaries =
      timeline
      |> Seq.choose (fun e ->
        match e with
        | TimelineEntry.ToolCall(_, _, _, summary, _) -> Some summary
        | _ -> None)
      |> Seq.toArray

    return
      {| HaltReason = haltReasonStr result.HaltReason
         Iterations = result.Iterations
         TimelineKinds = timeline |> Seq.map timelineKind |> Seq.toArray
         ToolSummaries = toolSummaries
         ThreadedOutcome = threadedOutcome.Value
         AskedElicitationId = askedId |}
  }
  |> Async.StartAsPromise

/// Drive the loop where the model calls `askUser` with a MALFORMED envelope
/// (an object that is not an elicitation): the typed refusal must come back as
/// an error tool result (never a fabricated outcome, never a crash), and the
/// loop must continue to the next turn. Returns flat.
let runAskRefusedProbe
  ()
  : JS.Promise<
      {| HaltReason: string
         RefusalThreaded: bool
         RefusalCode: string
         AskRowMounted: bool |}
     >
  =
  let threaded = ref ""
  let calls = ref 0

  let script =
    [ AgentOutcome.Ok(
        [ AgentContentBlock.Text "Asking (badly)."
          AgentContentBlock.ToolUse("tu-bad", "askUser", createObj [ "question" ==> "which colour?" ]) ],
        AgentStopReason.ToolUse,
        Some { InputTokens = 10; OutputTokens = 5 }
      )
      AgentOutcome.Ok(
        [ AgentContentBlock.Text "Understood – proceeding without asking." ],
        AgentStopReason.EndTurn,
        Some { InputTokens = 8; OutputTokens = 3 }
      ) ]

  let provider =
    { new IAgenticProvider with
        member _.Id = "mock"
        member _.Label = "Mock"
        member _.DefaultModel = "mock-model"

        member _.Send(_request) =
          async { return ProviderOutcome.Ok("", None) }

        member _.SendAgentic(request) =
          async {
            let i = calls.Value
            calls.Value <- i + 1

            if i = 1 then
              threaded.Value <-
                request.Messages
                |> List.collect _.Content
                |> List.tryPick (fun b ->
                  match b with
                  | AgentContentBlock.ToolResult(_, content, _) -> Some content
                  | _ -> None)
                |> Option.defaultValue ""

            let idx = min i (List.length script - 1)
            return List.item idx script
          } }

  let timeline = ResizeArray<TimelineEntry>()
  let deps = probeDeps provider defaultBudget timeline

  async {
    let! result = runAgentLoop Session.empty Panels.empty [] "Build me a thing." deps

    let code =
      try
        let parsed = jsonParseRaw threaded.Value
        string parsed?code
      with _ ->
        ""

    return
      {| HaltReason = haltReasonStr result.HaltReason
         RefusalThreaded = threaded.Value.Contains "elicitation envelope refused"
         RefusalCode = code
         AskRowMounted = timeline |> Seq.exists (fun e -> timelineKind e = "ask") |}
  }
  |> Async.StartAsPromise
