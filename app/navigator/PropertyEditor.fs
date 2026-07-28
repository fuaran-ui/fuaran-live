module Fuaran.Live.PropertyEditor

// ============================================================================
//  The Navigator's property editor — a form DERIVED from the schema, never
//  hand-built per kind.
//
//  In Fuaran an editor needs no mutation model of its own. The ops exist
//  (`UpdateProp` / `UpdateStyle` / `EditNode`), the apply engine exists, and the
//  pre-emit validator already gates what the orchestrator emits. The only
//  genuinely new obligation here is mapping "a field changed in a form" to the
//  right op — and the whole point of deriving the form from the schema is that
//  the mapping stays CLOSED UNDER VOCABULARY GROWTH: a `NodeKind` added
//  tomorrow gets an editable panel with no edit to this file.
//
//  ── The derivation route (three public surfaces, no per-kind code here) ─────
//
//  WHICH fields exist and are reachable → `Fuaran.UI.Ops.Introspect`:
//    · `availableFields`      — the authoritative UpdateProp surface. Its own
//      contract is that a name appears there only if some op can really reach
//      it, so it is exactly the right question to ask, and asking it is what
//      makes this panel grow by itself.
//    · `availableNestedPaths` — the per-kind indexed patterns
//      (`Columns[i].Label`), expanded below against the node's real collection
//      lengths. Payload collections have no identity, so positional addressing
//      is the sanctioned form (0.2.0 rule).
//    · `availableBindingSlots` — used only to WORD the read-only reason on a
//      bound slot; a binding is replaced by `ReplaceBinding`, not typed into.
//
//  WHAT SHAPE each field is → the canonical wire-format JSON Schema
//  (`Fuaran.UI.Ops.SchemaGen.wireFormatSchema`), reached through the app's
//  existing `Agent.getKindSchema` resolver — the same authoritative shape the
//  agent loop hands the model. A property's resolved `$def` gives its JSON type
//  (`string` / `integer` / `number` / `boolean`), its `enum` case list where it
//  is a bare-string enum, and its union branches otherwise. That is where the
//  control type and a select's options come from — not from a table here.
//
//  WHAT THE VALUE IS NOW → the node's own canonical wire JSON
//  (`CanonicalJson.encodeNode`), read at the field's wire path. The wire form is
//  the honest description of a node, so the panel reports it rather than
//  inventing a second vocabulary — the same stance the Phase 710 node card took.
//
//  ── Why the kind is read off the wire, not off `Introspect.kindName` ────────
//
//  `kindName` is the *display* tag and is NOT the wire discriminator for every
//  kind — `NodeKind.DataGrid` names itself "Grid" while the wire (and therefore
//  the schema) says "DataGrid". Reading `kind.$type` out of the encoded node
//  cannot drift from the schema, because it IS what the encoder wrote.
//
//  ── The safety rule that shapes the classification ─────────────────────────
//
//  A `TextSource` is a union whose first branch is a BARE STRING (a literal) and
//  whose other branches are a bound binding or an i18n key. So a text field is
//  editable-as-text only while its current value really is a bare scalar; the
//  moment it carries a binding it goes read-only, because committing a string
//  over it would silently destroy the binding. The same rule falls out for every
//  union in the vocabulary, present and future — it is a property of the shape,
//  not a list of exceptions.
//
//  ── The gate ───────────────────────────────────────────────────────────────
//
//  `apply` is PURE, so a candidate tree is free: build the op, apply it to a
//  candidate, run `PreEmitValidate` over the candidate, and only then fold it
//  into the session. On rejection the session is returned untouched — not
//  restored, never mutated in the first place. The gate reports only defects the
//  edit INTRODUCES (candidate minus current): a tree that already carries a
//  defect must still be editable, or the panel would refuse to help fix it.
//
//  Every mutation on this path is an op through the public apply engine. There
//  is no direct tree surgery anywhere in the navigator.
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

// ─── the derived field model (pure — no DOM, no React) ───────────────────────

/// The control a field is edited with. Every case here is decided by the
/// field's resolved schema shape, never by its name or its kind.
[<RequireQualifiedAccess>]
type Editor =
  /// A JSON string (or a union branch currently holding a bare string).
  | Text
  /// A JSON integer.
  | Integer
  /// A JSON number.
  | Number
  /// A JSON boolean.
  | Toggle
  /// A bare-string enum — the schema's own case list, in schema order.
  | Choice of options: string list
  /// Not editable from a form, with the honest reason why.
  | ReadOnly of reason: string

/// A stable tag for `Editor`, for the flat cross-boundary surface below.
let editorTag (e: Editor) : string =
  match e with
  | Editor.Text -> "text"
  | Editor.Integer -> "integer"
  | Editor.Number -> "number"
  | Editor.Toggle -> "toggle"
  | Editor.Choice _ -> "choice"
  | Editor.ReadOnly _ -> "readonly"

/// Which op a committed change becomes.
[<RequireQualifiedAccess>]
type Route =
  /// `UpdateProp(id, Path, …)` — the granular spec-field route.
  | Prop
  /// `UpdateStyle(id, …)` — the node-level semantic style block.
  | Style

/// One derived row of the panel.
type Field =
  {
    /// Display grouping ("Properties" / "Contained data" / "Style").
    Group: string
    /// The op-facing address: an `UpdateProp` path (`"Level"`,
    /// `"Columns[0].Label"`) or, for a style row, the style token key.
    Path: string
    /// The wire path into the node's canonical JSON, used to read the current
    /// value (`"kind.level"`, `"kind.columns[0].label"`, `"style.tone"`).
    Wire: string
    /// The label shown beside the control.
    Label: string
    Editor: Editor
    /// The current value rendered as display text ("" when the field is
    /// omitted-when-default and so absent from the wire).
    Current: string
    Route: Route
  }

// ─── JS leaves (the schema walk + the wire-JSON reads) ───────────────────────
//
// These are structural walks over parsed JSON — the same inline-JS idiom
// `Session.fs` uses for its extraction leaves. Each is total: a shape it does
// not recognise yields a null/undefined the F# side treats as "unknown", which
// renders read-only rather than guessing.

/// The raw value at a dotted, optionally-indexed path in a JSON document
/// (`"kind.columns[0].label"`), or `undefined`.
[<Emit("""(function(json, path){
  var v; try { v = JSON.parse(json); } catch (e) { return undefined; }
  var segs = String(path).split('.');
  for (var i = 0; i < segs.length; i++) {
    if (v === null || typeof v !== 'object') { return undefined; }
    var m = /^([^\[]+)((?:\[\d+\])*)$/.exec(segs[i]);
    if (!m) { return undefined; }
    v = v[m[1]];
    var idx = m[2] ? m[2].match(/\d+/g) : null;
    if (idx) {
      for (var k = 0; k < idx.length; k++) {
        if (!Array.isArray(v)) { return undefined; }
        v = v[parseInt(idx[k], 10)];
      }
    }
  }
  return v;
})($0, $1)""")>]
let private valueAt (json: string) (wirePath: string) : obj = jsNative

/// A raw JSON value as display text ("" when absent; compact JSON when
/// structured, so a read-only row still SHOWS what is there).
[<Emit("""(function(v){
  if (v === null || v === undefined) { return ''; }
  if (typeof v === 'object') { try { return JSON.stringify(v); } catch (e) { return '…'; } }
  return String(v);
})($0)""")>]
let private displayOf (v: obj) : string = jsNative

/// The length of the array at a wire path, or 0.
[<Emit("(function(v){ return Array.isArray(v) ? v.length : 0; })($0)")>]
let private arrayLength (v: obj) : int = jsNative

/// Classify one field: walk `wirePath` (kind-relative) down the kind schema's
/// `properties` / `items` / `$ref` chain, then decide the control from the
/// resolved `$def` and — for a union — from the current value's own shape.
/// Returns `{ tag, options, reason }`.
[<Emit("""(function(kindSchema, path, value){
  var defs = (kindSchema && kindSchema.defs) || {};
  var deref = function(s){
    var n = 0;
    while (s && typeof s.$ref === 'string' && n++ < 8) { s = defs[s.$ref.replace('#/$defs/','')]; }
    return s;
  };
  var scalar = function(s){
    if (!s || typeof s !== 'object') { return null; }
    if (Array.isArray(s.enum)) { return { tag: 'choice', options: s.enum, reason: '' }; }
    if (s.type === 'string')  { return { tag: 'text',    options: null, reason: '' }; }
    if (s.type === 'integer') { return { tag: 'integer', options: null, reason: '' }; }
    if (s.type === 'number')  { return { tag: 'number',  options: null, reason: '' }; }
    if (s.type === 'boolean') { return { tag: 'toggle',  options: null, reason: '' }; }
    return null;
  };
  var unknown = { tag: 'readonly', options: null, reason: 'this kind publishes no schema for the field' };
  var cursor = { properties: (kindSchema && kindSchema.properties) || null };
  if (!cursor.properties) { return unknown; }
  var schema = null;
  var segs = String(path).split('.');
  for (var i = 0; i < segs.length; i++) {
    var m = /^([^\[]+)((?:\[\d+\])*)$/.exec(segs[i]);
    if (!m || !cursor || !cursor.properties) { return unknown; }
    schema = deref(cursor.properties[m[1]]);
    var depth = m[2] ? m[2].match(/\d+/g).length : 0;
    for (var k = 0; k < depth; k++) { schema = schema ? deref(schema.items) : null; }
    if (!schema) { return unknown; }
    cursor = schema;
  }
  var direct = scalar(schema);
  if (direct) { return direct; }
  if (Array.isArray(schema.oneOf)) {
    var bare = null;
    for (var b = 0; b < schema.oneOf.length; b++) {
      var t = scalar(deref(schema.oneOf[b]));
      if (t) { bare = t; break; }
    }
    if (bare) {
      var now = (value === null || value === undefined) ? 'undefined' : typeof value;
      if (now === 'undefined' || now === 'string' || now === 'number' || now === 'boolean') { return bare; }
      return { tag: 'readonly', options: null,
               reason: 'currently bound — committing text here would discard the binding' };
    }
    return { tag: 'readonly', options: null, reason: 'a structured value; swap it with an op' };
  }
  if (schema.type === 'array') {
    return { tag: 'readonly', options: null, reason: 'a collection; use the structural ops' };
  }
  return { tag: 'readonly', options: null, reason: 'a structured value; swap it with an op' };
})($0, $1, $2)""")>]
let private classifyJs (kindSchema: obj) (wirePath: string) (value: obj) : obj = jsNative

/// The semantic-style block's editable tokens, straight from the schema's
/// `SemanticStyle` definition: every property whose resolved `$def` is a
/// bare-string enum, with that enum's case list. A style token added to the
/// language appears here with no edit to this file.
[<Emit("""(function(schemaText){
  var s; try { s = JSON.parse(schemaText); } catch (e) { return []; }
  var defs = (s && s.$defs) || {};
  var st = defs['SemanticStyle'];
  if (!st || !st.properties) { return []; }
  var out = [];
  Object.keys(st.properties).forEach(function(k){
    var p = st.properties[k], n = 0;
    while (p && typeof p.$ref === 'string' && n++ < 8) { p = defs[p.$ref.replace('#/$defs/','')]; }
    if (p && Array.isArray(p.enum)) { out.push({ key: k, options: p.enum }); }
  });
  return out;
})($0)""")>]
let private styleTokensJs (schemaText: string) : obj array = jsNative

/// Set one dotted path in a JSON document to a bare string, creating missing
/// intermediate objects, and re-render the document. Used only for the style
/// round-trip below.
[<Emit("""(function(json, path, value){
  var o; try { o = JSON.parse(json); } catch (e) { return json; }
  var segs = String(path).split('.');
  var cur = o;
  for (var i = 0; i < segs.length - 1; i++) {
    if (cur[segs[i]] === null || typeof cur[segs[i]] !== 'object') { cur[segs[i]] = {}; }
    cur = cur[segs[i]];
  }
  cur[segs[segs.length - 1]] = value;
  try { return JSON.stringify(o); } catch (e) { return json; }
})($0, $1, $2)""")>]
let private patchString (json: string) (path: string) (value: string) : string = jsNative

/// `Number(s)` with empty/non-finite mapped to NaN — JS parsing semantics, so
/// the number rows accept exactly what JSON would.
[<Emit("(function(s){ var n = Number(s); return (String(s).trim() !== '' && isFinite(n)) ? n : NaN; })($0)")>]
let private parseNumber (s: string) : float = jsNative

// Reading a `{tag,options,reason}` bag back into F#.
[<Emit("($0 && $0.tag) || 'readonly'")>]
let private tagOf (o: obj) : string = jsNative

[<Emit("($0 && $0.reason) || ''")>]
let private reasonOf (o: obj) : string = jsNative

[<Emit("($0 && Array.isArray($0.options)) ? $0.options : []")>]
let private optionsOf (o: obj) : string array = jsNative

[<Emit("($0 && $0.key) || ''")>]
let private tokenKey (o: obj) : string = jsNative

[<Emit("($0 && Array.isArray($0.options)) ? $0.options : []")>]
let private tokenOptions (o: obj) : string array = jsNative

// ─── name conventions ────────────────────────────────────────────────────────

/// The wire key of an op-path segment: the canonical encoder writes
/// lowerCamelCase of the F# field name (`SomeField` → `someField`), and any
/// `[i]` index rides along unchanged. Deliberately illustrated with a name no
/// kind actually has — nothing in this module knows one field from another.
let private wireSegment (segment: string) : string =
  if segment = "" then
    segment
  else
    string (System.Char.ToLowerInvariant segment[0]) + segment.Substring 1

/// The wire path of an op path (`"Columns[0].Label"` → `"columns[0].label"`).
let private wirePathOf (path: string) : string =
  path.Split('.') |> Array.map wireSegment |> String.concat "."

// ─── schema access (memoised) ────────────────────────────────────────────────
//
// `Agent.getKindSchema` parses the whole ~40 KB schema document per call and
// `fields` runs on every render, so the per-kind result is cached. Module-level
// mutable state, justified: a pure memo of a pure function of a compile-time
// constant — it can be dropped at any time with no behavioural difference.

let private schemaCache = System.Collections.Generic.Dictionary<string, obj>()

let private kindSchema (disc: string) : obj =
  match schemaCache.TryGetValue disc with
  | true, cached -> cached
  | _ ->
    let resolved = Agent.getKindSchema (Some disc)
    schemaCache[disc] <- resolved
    resolved

let private styleTokens = lazy (styleTokensJs Schema.wireFormatSchema)

// ─── derivation ──────────────────────────────────────────────────────────────

/// The node's canonical wire JSON — the source of every current value, and of
/// the `$type` discriminator the schema is keyed by.
let private nodeJson (node: Node<obj>) : string =
  try
    Canon.encodeNode node
  with _ ->
    ""

let private editorOf (schema: obj) (kindWirePath: string) (current: obj) : Editor =
  let bag = classifyJs schema kindWirePath current

  match tagOf bag with
  | "text" -> Editor.Text
  | "integer" -> Editor.Integer
  | "number" -> Editor.Number
  | "toggle" -> Editor.Toggle
  | "choice" -> Editor.Choice(List.ofArray (optionsOf bag))
  | _ -> Editor.ReadOnly(reasonOf bag)

/// Expand one nested pattern (`"Columns[i].Label"`) against the node's real
/// collection length, yielding one concrete path per index. Patterns carrying
/// more than one `[i]` are not expanded — none exist in the vocabulary today,
/// and inventing an expansion for a shape no kind publishes would be guessing.
let private expandNested (json: string) (pattern: string) : string list =
  let marker = "[i]"
  let at = pattern.IndexOf marker

  if at < 0 || pattern.IndexOf(marker, at + marker.Length) >= 0 then
    []
  else
    let head = pattern.Substring(0, at)
    let tail = pattern.Substring(at + marker.Length)
    let count = arrayLength (valueAt json ("kind." + wirePathOf head))

    [ for i in 0 .. count - 1 -> head + "[" + string i + "]" + tail ]

/// The derived panel for a node: every reachable property, every contained-data
/// position, and the semantic-style block. Pure — a function of the node alone.
let fields (node: Node<obj>) : Field list =
  let json = nodeJson node

  if json = "" then
    []
  else
    let disc = displayOf (valueAt json "kind.$type")
    let schema = kindSchema disc
    let bindingSlots = Introspect.availableBindingSlots node.Kind |> Set.ofList

    let derive (group: string) (path: string) : Field =
      let wire = "kind." + wirePathOf path
      let current = valueAt json wire

      let editor =
        // `Children` is advertised by `availableFields` as a signpost to the
        // structural ops, not as an UpdateProp target; say so rather than let
        // the array classification give a vaguer reason.
        if path = "Children" then
          Editor.ReadOnly "the structural ops own this — InsertChild / RemoveNode / MoveNode / ReorderChildren"
        else
          match editorOf schema (wirePathOf path) current with
          | Editor.ReadOnly r when bindingSlots.Contains path ->
            Editor.ReadOnly(r + " (a bound slot — ReplaceBinding replaces it)")
          | e -> e

      { Group = group
        Path = path
        Wire = wire
        Label = path
        Editor = editor
        Current = displayOf current
        Route = Route.Prop }

    let top = Introspect.availableFields node.Kind |> List.map (derive "Properties")

    let nested =
      Introspect.availableNestedPaths node.Kind
      |> List.collect (expandNested json)
      |> List.map (derive "Contained data")

    let style =
      styleTokens.Value
      |> Array.toList
      |> List.map (fun token ->
        let key = tokenKey token
        let wire = "style." + key

        { Group = "Style"
          Path = key
          Wire = wire
          Label = wireSegment key
          Editor = Editor.Choice(List.ofArray (tokenOptions token))
          Current = displayOf (valueAt json wire)
          Route = Route.Style })

    top @ nested @ style

// ─── edit → op ───────────────────────────────────────────────────────────────

/// The op a committed field change becomes. `Prop` rows go through the granular
/// `UpdateProp` with a canonical `Wire` payload — the population the apply
/// engine's per-field coercers are built for, and the one that persists and
/// content-hashes faithfully. `Style` rows patch the node's own wire JSON and
/// run it back through the REAL decoder, so an illegal token is caught by the
/// decoder that every host runs, and no enum-parsing table is written here.
let opFor (node: Node<obj>) (field: Field) (raw: string) : Result<TreeOp<obj>, string> =
  match field.Editor with
  | Editor.ReadOnly reason -> Error("read-only — " + reason)
  | _ ->
    match field.Route with
    | Route.Style ->
      let patched = patchString (nodeJson node) field.Wire raw

      match Decode.decodeNode patched with
      | Error e -> Error e.Message
      | Ok wire -> Ok(TreeOp.UpdateStyle(node.Id, (WireTree.reify wire).Style))
    | Route.Prop ->
      let payload =
        match field.Editor with
        | Editor.Text -> Ok(JStr raw)
        | Editor.Choice options ->
          if List.contains raw options then
            Ok(JStr raw)
          else
            Error(sprintf "'%s' is not one of: %s" raw (String.concat ", " options))
        | Editor.Toggle -> Ok(JBool(raw = "true"))
        | Editor.Integer ->
          let n = parseNumber raw

          if System.Double.IsNaN n then
            Error(sprintf "'%s' is not a number" raw)
          elif n <> floor n then
            Error(sprintf "'%s' is not a whole number" raw)
          else
            Ok(JInt(int n))
        | Editor.Number ->
          let n = parseNumber raw

          if System.Double.IsNaN n then
            Error(sprintf "'%s' is not a number" raw)
          else
            Ok(JFloat n)
        | Editor.ReadOnly reason -> Error("read-only — " + reason)

      payload
      |> Result.map (fun value -> TreeOp.UpdateProp(node.Id, field.Path, PropValue.Wire value))

// ─── the validator gate ──────────────────────────────────────────────────────

/// The error-severity pre-emit defects of a tree, as `code|message` keys.
/// Warnings are advisory by construction and never block an edit — refusing on
/// a warning would make the panel unable to author the intermediate states a
/// user passes through on the way to a correct tree.
let private errorDefects (tree: Node<obj>) : string list =
  match Validate.validate tree with
  | Ok() -> []
  | Error defects ->
    defects
    |> List.choose (fun d ->
      let code, severity, message = Validate.describe d

      match severity with
      | Validate.DefectSeverity.Error -> Some(code + "|" + message)
      | _ -> None)

/// The defects the edit INTRODUCES — the candidate's error set minus the
/// current tree's. A tree that already fails must stay editable.
let private introduced (before: Node<obj>) (after: Node<obj>) : string list =
  let prior = errorDefects before |> Set.ofList

  errorDefects after
  |> List.filter (fun key -> not (prior.Contains key))
  |> List.map (fun key -> key.Replace("|", ": "))

// ─── commit ──────────────────────────────────────────────────────────────────

type CommitOutcome =
  /// The edit passed the gate; the next session carries the new tree, the op's
  /// canonical JSON, and the new snapshot.
  | Committed of Session.SessionState
  /// The edit was refused. The session is the caller's, unchanged — the
  /// candidate tree was never folded in.
  | Rejected of message: string

/// **The navigator's one edit gate**, generic over the op — apply it to a
/// CANDIDATE tree, refuse on a defect the edit INTRODUCES, and only then fold.
/// It landed here because the property panel was the first route to need it, but
/// nothing about it is property-specific: the structural edits (Phase 713) go
/// through this same function rather than restating the gate, so "what the
/// navigator will accept" has exactly one definition and cannot drift into two.
/// Pure; never throws.
let commitOp (session: Session.SessionState) (op: TreeOp<obj>) : CommitOutcome =
  match session.Tree with
  | None -> Rejected "there is no tree to edit"
  | Some tree ->
    match ApplyEngine.apply op tree with
    | Error e -> Rejected e.Message
    | Ok candidate ->
      match introduced tree candidate with
      | [] ->
        let canonOp = Canon.encodeOp op

        // Phase 712 — the same fold, now attributed. The op is recorded
        // against `navigatorActor` (a `Human`), which is what makes "what did
        // the person change, as opposed to the model" answerable from the
        // stream afterwards rather than only from memory. `recordOp` also
        // truncates any redo tail: committing after undoing abandons the
        // undone branch.
        Committed
          { session with
              Tree = Some candidate
              Ops = session.Ops @ [ canonOp ]
              Snapshots = session.Snapshots @ [ candidate ]
              Log = Session.recordOp session Session.navigatorActor op canonOp }
      | defects -> Rejected(String.concat "; " defects)

/// Build the op a field change becomes, then run it through the gate above.
let commit (session: Session.SessionState) (node: Node<obj>) (field: Field) (raw: string) : CommitOutcome =
  match session.Tree with
  | None -> Rejected "there is no tree to edit"
  | Some _ ->
    match opFor node field raw with
    | Error message -> Rejected message
    | Ok op -> commitOp session op

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
