module Fuaran.Live.StructuralEdit

// ============================================================================
//  The Navigator's structural edits — insert / remove / move / reorder, as ops.
//
//  The property editor made the card's FIELDS editable. This makes the tree's
//  SHAPE editable, and it does so with no new machinery at all: the four
//  structural ops already exist (`InsertChild` / `RemoveNode` / `MoveNode` /
//  `ReorderChildren`), the apply engine already applies them, and the property
//  editor's validator gate is already generic over an op. What is genuinely new
//  here is two things — deciding WHICH node to insert (the palette) and WHERE it
//  lands (the placement) — and both are answered without a per-kind table.
//
//  ── Placement is stated by naming ids, never by an index (0.4.0) ────────────
//
//  `InsertChild` and `MoveNode` **append**. They change membership only; order
//  is stated separately by `ReorderChildren`, which names the full sibling id
//  list. So placing a node anywhere but last is
//
//      Batch [ InsertChild(parent, child); ReorderChildren(parent, ids…) ]
//
//  and there is no integer anywhere in it. That derivation — the post-op
//  sibling permutation, the dropped reorder leg when appending already yields
//  the wanted order, and the pre-checks that mirror the apply engine's own
//  refusals — ships in the apply-engine tier as `Fuaran.UI.Ops.Placement`
//  (`Placement` / `Target` / `PlaceError` / `placeOp` / `moveOp` / `nudgeOp` /
//  `canPlace`), and this module CONSUMES it rather than carrying its own copy.
//  The types are re-exported below so callers keep addressing them as
//  `StructuralEdit.Placement` / `StructuralEdit.Target`.
//
//  One consequence worth naming: the packaged pre-checks refuse an op the apply
//  engine would refuse — an unknown anchor, a childless destination, a move
//  into a descendant — BEFORE emission, with a sentence, where this module once
//  emitted the op and let the gate reject it after the fact. Same outcome
//  either way (the commit funnel below turns both into a rejection that leaves
//  the session untouched); the refusal just arrives earlier and reads better.
//
//  ── The palette is derived, not written ────────────────────────────────────
//
//  Which kinds can be inserted comes from the same two surfaces the property
//  editor derives its fields from — `Agent.getKindSchema` with no argument
//  lists every kind the canonical schema knows, and with a kind names that
//  kind's required fields and their shapes. A default node is SYNTHESISED from
//  that shape (required fields only, each filled with the minimal legal value
//  its schema admits), then put through the REAL strict decoder. A kind added
//  to the language therefore appears in this palette with no edit to this file,
//  and a kind whose minimal shape this module cannot synthesise is simply not
//  offered rather than offered and broken.
//
//  ── "Validator-checked before offer" is meant literally ────────────────────
//
//  A kind is offered only if inserting its default node HERE would be accepted:
//  the palette runs each candidate through the very gate the button would run
//  (`PropertyEditor.commitOp` — apply to a candidate tree, reject on a newly
//  introduced error-severity defect) and keeps the ones that pass. The offer
//  set and the commit behaviour cannot disagree, because they are the same
//  code path. This is also why the palette is per-TARGET: a kind legal under a
//  Box may be illegal under some other parent, and saying so before the click
//  is better than a rejection after it.
//
//  ── Identity of the new node ───────────────────────────────────────────────
//
//  The synthesiser emits SENTINEL ids (it knows shapes, not identity); this
//  module mints real ones, checked against every id already in the tree — the
//  whole tree, via `Introspect.allNodeIds`, not just the target's siblings,
//  because the duplicate-id rejection the apply engine performs is tree-wide.
//  A synthesised default may be more than one node (a kind with a required
//  `Node` field), so every sentinel in the template gets its own fresh id.
//
//  Nothing in this module edits a tree by hand. The only way a byte moves is an
//  op through the public apply engine, gated, and recorded with its origin.
// ============================================================================

open Fable.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

module Introspect = Fuaran.UI.Ops.Introspect
module Decode = Fuaran.UI.Ops.JsonDecode

// ─── placement (the packaged vocabulary, re-exported) ────────────────────────

/// Where a node should sit among its new siblings — the packaged placement
/// vocabulary (`Fuaran.UI.Ops`), re-exported so callers keep addressing it as
/// `StructuralEdit.Placement`.
type Placement = Fuaran.UI.Ops.Placement

/// A structural destination: which parent, and where among its children — the
/// packaged `Fuaran.UI.Ops.Target`, re-exported.
type Target = Fuaran.UI.Ops.Target

/// The raw string of a `NodeId` (the wire spelling).
let idText (NodeId s) : string = s

/// The ids of a parent's structural children, in order (empty when the node is
/// absent or is a kind the structural ops cannot reach into).
let childIdsOf (root: Node<obj>) (parentId: NodeId) : NodeId list =
  Introspect.findNode parentId root
  |> Option.bind (fun parent -> Introspect.getChildren parent.Kind)
  |> Option.map (List.map (fun c -> (NodeId c.Id)))
  |> Option.defaultValue []

/// The structural destination a cursor on `focused` implies: under the focused
/// node when it accepts children, otherwise beside it under its parent. `None`
/// only for a childless root, which has nowhere to put anything.
let defaultTarget (root: Node<obj>) (focused: NodeId) : Target option =
  match Introspect.findNode focused root with
  | None -> None
  | Some node ->
    match Introspect.getChildren node.Kind with
    | Some _ ->
      Some
        { ParentId = focused
          Placement = Placement.Last }
    | None ->
      Introspect.findParent focused root
      |> Option.map (fun (parent, _) ->
        { ParentId = (NodeId parent.Id)
          Placement = Placement.After focused })

// ─── op construction (delegated) + the shared commit funnel ──────────────────
//
// The ops themselves come from the packaged placement algebra. What is local is
// the translation of its typed refusal into the sentence the navigator's status
// line shows — each `PlaceError` case is a pre-statement of the apply-time
// refusal the emitted op would have met, so the sentence describes the same
// fact the engine would have reported after the fact.

let private placeErrorText (error: PlaceError) : string =
  match error with
  | PlaceError.ParentNotFound(NodeId parentId) -> "no node '" + parentId + "' to place under"
  | PlaceError.ChildlessKind(NodeId parentId) -> "'" + parentId + "' is a kind that takes no children"
  | PlaceError.NodeNotFound(NodeId nodeId) -> "no node '" + nodeId + "' in the tree"
  | PlaceError.UnknownAnchor(NodeId anchor) -> "no sibling '" + anchor + "' to place beside"
  | PlaceError.DuplicateId(NodeId nodeId) -> "the id '" + nodeId + "' is already taken"
  | PlaceError.MoveIntoSelf(NodeId nodeId) -> "cannot move '" + nodeId + "' into itself"
  | PlaceError.MoveIntoDescendant(NodeId nodeId, NodeId parentId) ->
    "cannot move '" + nodeId + "' into '" + parentId + "', which is inside it"
  | PlaceError.CannotNudgeRoot _ -> "the root node has no siblings to reorder"
  | PlaceError.NudgeOutOfRange(_, delta) ->
    if delta < 0 then
      "already the first of its siblings"
    else
      "already the last of its siblings"

/// The one seam every placed op passes through: a refusal from the packaged
/// pre-checks becomes a rejection (session untouched, exactly as an apply-time
/// rejection would leave it); a derived op goes through the very gate a
/// property edit uses — apply to a CANDIDATE tree, refuse on a newly-introduced
/// error-severity defect, and only then fold, recording the op against the
/// navigator's `Human` actor. That shared channel is what makes undo cover
/// structure for free: the replay knows nothing about which op it is replaying,
/// so a structural op undoes because it applies.
let private commitPlaced
  (session: Session.SessionState)
  (planned: Result<TreeOp<obj>, PlaceError>)
  : PropertyEditor.CommitOutcome =
  match planned with
  | Error error -> PropertyEditor.Rejected(placeErrorText error)
  | Ok op -> PropertyEditor.commitOp session op

/// Where the cursor should land once `nodeId` is removed: the previous sibling,
/// else the next, else the parent. Computed BEFORE the removal (the node is
/// still there to be located) and applied after, so focus moves somewhere the
/// user was already looking rather than to the deepest surviving ancestor the
/// cursor's own fallback would otherwise choose.
let fallbackAfterRemove (root: Node<obj>) (nodeId: NodeId) : NodeId option =
  match Introspect.findParent nodeId root with
  | None -> None
  | Some(parent, index) ->
    // Indices below `index` are unmoved by the removal; the node that was at
    // `index + 1` is at `index` afterwards.
    let survivors =
      childIdsOf root (NodeId parent.Id) |> List.filter (fun id -> id <> nodeId)

    if index - 1 >= 0 && index - 1 < List.length survivors then
      Some survivors[index - 1]
    elif index < List.length survivors then
      Some survivors[index]
    else
      Some(NodeId parent.Id)

/// Whether `moved` may legally be dropped under `parentId` — the pick-up/drop
/// interaction's own guard, delegated to the packaged pre-check (`canPlace`
/// with an end placement, since drop legality is about the destination, not
/// the position): a parent that takes children, that is not the node itself,
/// and that is not inside it (a move into your own descendant is a cycle,
/// refused by the apply engine — saying so before the click is friendlier than
/// a rejection after it).
let canDrop (root: Node<obj>) (moved: NodeId) (parentId: NodeId) : bool =
  Placement.canPlace
    root
    moved
    { ParentId = parentId
      Placement = Placement.Last }
  |> Result.isOk

// ─── the palette: synthesising a minimal-valid node per kind ─────────────────

/// The id the synthesiser writes where a fresh `NodeId` belongs. It exists only
/// inside a template this module just produced (never in a tree), so the
/// substitution below cannot collide with anything real.
[<Literal>]
let private sentinel = "fuaran-new-node-"

/// Synthesise the minimal legal wire JSON for a node of `disc` from that kind's
/// schema: every REQUIRED field, each filled with the least value its resolved
/// schema admits (a `const` as itself, an enum's first case, a union's first
/// synthesisable branch, an object over its own required fields, an array
/// empty). Returns `""` when the shape cannot be synthesised — a kind reaching
/// a position this walk does not recognise is not offered, rather than offered
/// with a guess in it. Nested `Node` positions get sentinel ids; identity is the
/// F# side's business.
[<Emit("""(function(kindSchema, disc, sentinel){
  var defs = (kindSchema && kindSchema.defs) || {};
  var MISSING = { missing: true };
  var seq = 1;
  var title = function(s){ return s ? (s.charAt(0).toUpperCase() + s.slice(1)) : 'text'; };
  var synth = function(s, depth, hint){
    if (depth > 8) { return MISSING; }
    if (s === true) { return {}; }
    if (s === null || typeof s !== 'object') { return MISSING; }
    if (typeof s.$ref === 'string') {
      var name = s.$ref.replace('#/$defs/','');
      if (name === 'Node') {
        var inner = synth({ $ref: '#/$defs/NodeKind' }, depth + 1, '');
        if (inner === MISSING) { return MISSING; }
        return { id: sentinel + (seq++), kind: inner };
      }
      if (!defs[name]) { return MISSING; }
      return synth(defs[name], depth + 1, hint);
    }
    if (typeof s.const !== 'undefined') { return s.const; }
    if (Array.isArray(s.enum)) { return s.enum.length ? s.enum[0] : MISSING; }
    if (Array.isArray(s.allOf)) {
      var merged = {};
      for (var a = 0; a < s.allOf.length; a++) {
        var part = synth(s.allOf[a], depth + 1, hint);
        if (part === MISSING || part === null || typeof part !== 'object' || Array.isArray(part)) { return MISSING; }
        for (var pk in part) { if (Object.prototype.hasOwnProperty.call(part, pk)) { merged[pk] = part[pk]; } }
      }
      return merged;
    }
    if (Array.isArray(s.oneOf)) {
      for (var b = 0; b < s.oneOf.length; b++) {
        var branch = synth(s.oneOf[b], depth + 1, hint);
        if (branch !== MISSING) { return branch; }
      }
      return MISSING;
    }
    if (s.type === 'string') { return title(hint); }
    if (s.type === 'integer') { return 1; }
    if (s.type === 'number') { return 0; }
    if (s.type === 'boolean') { return false; }
    if (s.type === 'array') { return []; }
    if (s.type === 'object') {
      var out = {};
      var req = Array.isArray(s.required) ? s.required : [];
      var props = s.properties || {};
      for (var r = 0; r < req.length; r++) {
        var key = req[r];
        if (!Object.prototype.hasOwnProperty.call(props, key)) { return MISSING; }
        var v = synth(props[key], depth + 1, key);
        if (v === MISSING) { return MISSING; }
        out[key] = v;
      }
      return out;
    }
    if (s.not) { return title(hint); }
    return MISSING;
  };
  var required = (kindSchema && kindSchema.required) || [];
  var properties = (kindSchema && kindSchema.properties) || {};
  if (!Array.isArray(required)) { return ''; }
  var kindObj = { $type: disc };
  for (var i = 0; i < required.length; i++) {
    var f = required[i];
    if (f === '$type') { continue; }
    if (!Object.prototype.hasOwnProperty.call(properties, f)) { return ''; }
    var val = synth(properties[f], 1, f);
    if (val === MISSING) { return ''; }
    kindObj[f] = val;
  }
  try { return JSON.stringify({ id: sentinel + '0', kind: kindObj }); } catch (e) { return ''; }
})($0, $1, $2)""")>]
let private synthesiseJs (kindSchema: obj) (disc: string) (sentinelPrefix: string) : string = jsNative

/// The kind discriminators the canonical schema knows — `getKindSchema` with no
/// argument. This is the palette's whole vocabulary source.
[<Emit("($0 && Array.isArray($0.kinds)) ? $0.kinds : []")>]
let private kindsOf (report: obj) : string array = jsNative

// The synthesised template depends on the KIND alone (identity is substituted
// afterwards), so it is memoised: `getKindSchema` re-parses the whole schema
// document per call, and the palette asks about every kind at once. Module-level
// mutable state, justified on the same grounds as the property editor's schema
// cache — a pure memo of a pure function of a compile-time constant, droppable
// at any moment with no behavioural difference.
let private templateCache = System.Collections.Generic.Dictionary<string, string>()

let private template (disc: string) : string =
  match templateCache.TryGetValue disc with
  | true, cached -> cached
  | _ ->
    let built = synthesiseJs (Agent.getKindSchema (Some disc)) disc sentinel
    templateCache[disc] <- built
    built

/// `count` ids of the form `<base>-<n>` that appear nowhere in the tree — and
/// nowhere in each other. Tree-wide, because the apply engine's duplicate-id
/// rejection is tree-wide.
let private mintIds (root: Node<obj>) (baseName: string) (count: int) : string list =
  let taken = Introspect.allNodeIds root |> List.map idText |> Set.ofList

  let rec pick (n: int) (chosen: string list) (used: Set<string>) =
    if List.length chosen >= count then
      List.rev chosen
    else
      let candidate = baseName + "-" + string n

      if used.Contains candidate then
        pick (n + 1) chosen used
      else
        pick (n + 1) (candidate :: chosen) (used.Add candidate)

  pick 1 [] taken

/// How many sentinel ids a template carries (they are numbered from 0 without
/// gaps, so counting is a walk until one is absent).
let private sentinelCount (json: string) : int =
  let rec count (n: int) =
    if json.Contains("\"" + sentinel + string n + "\"") then
      count (n + 1)
    else
      n

  count 0

/// A `NodeKind` discriminator as an id stem (`"SomeKind"` → `"somekind"`).
/// Deliberately illustrated with a name no kind actually has — nothing in this
/// module knows one kind from another, and a source lock in the suite says so.
let private stemOf (disc: string) : string = disc.ToLowerInvariant()

/// The raw synthesised wire JSON for `disc`, sentinels replaced by fresh ids —
/// the bytes this module hand-shapes, before any decoder has seen them. `""`
/// when the kind cannot be synthesised.
///
/// Exposed because it is an **in-page wire emitter** in the repo's sense, and
/// the convention is that every one ships with its lock: the suite decodes this
/// with the real strict decoder and asserts it re-encodes to itself, so the
/// synthesiser is both decodable AND already canonical. A union whose first
/// branch happened to be a non-canonical spelling (the `{"$type":"Literal"}`
/// envelope over a bare string, say) would survive decode and fail that lock —
/// which is exactly the drift the convention exists to catch.
let defaultNodeWire (root: Node<obj>) (disc: string) : string =
  let raw = template disc

  if raw = "" then
    ""
  else
    mintIds root (stemOf disc) (sentinelCount raw)
    |> List.mapi (fun i minted -> i, minted)
    |> List.fold
      (fun (json: string) (i, minted) -> json.Replace("\"" + sentinel + string i + "\"", "\"" + minted + "\""))
      raw

/// The minimal default node for `disc`, with fresh ids, read back through the
/// REAL strict decoder — so what the palette holds is a node the decoder
/// accepts, not a shape this module believes in.
let defaultNodeFor (root: Node<obj>) (disc: string) : Node<obj> option =
  match defaultNodeWire root disc with
  | "" -> None
  | filled ->
    match Decode.decodeNode filled with
    | Error _ -> None
    | Ok wire -> Some(WireTree.reify wire)

/// One offerable kind, with the node the button would insert.
type PaletteEntry = { Kind: string; Node: Node<obj> }

/// The kinds insertable at `target` — every kind whose default node both
/// decodes AND passes the commit gate at this destination. The gate run here is
/// the very one the button runs, so the offer cannot disagree with the outcome.
let palette (session: Session.SessionState) (target: Target) : PaletteEntry list =
  match session.Tree with
  | None -> []
  | Some root ->
    kindsOf (Agent.getKindSchema None)
    |> Array.toList
    |> List.choose (fun disc ->
      defaultNodeFor root disc
      |> Option.bind (fun node ->
        match commitPlaced session (Placement.placeOp root node target) with
        | PropertyEditor.Committed _ -> Some { Kind = disc; Node = node }
        | PropertyEditor.Rejected _ -> None))

// ─── commit (one gate, shared with the property editor) ──────────────────────
//
// Every entry point below funnels into `commitPlaced` (and so into
// `PropertyEditor.commitOp`), with the op itself derived by the packaged
// placement algebra.

let private withRoot (session: Session.SessionState) (f: Node<obj> -> PropertyEditor.CommitOutcome) =
  match session.Tree with
  | None -> PropertyEditor.Rejected "there is no tree to edit"
  | Some root -> f root

/// Insert `child` at `target`.
let insert (session: Session.SessionState) (target: Target) (child: Node<obj>) : PropertyEditor.CommitOutcome =
  withRoot session (fun root -> commitPlaced session (Placement.placeOp root child target))

/// Remove `nodeId` (the caller confirms; this does not ask).
let remove (session: Session.SessionState) (nodeId: NodeId) : PropertyEditor.CommitOutcome =
  PropertyEditor.commitOp session (TreeOp.RemoveNode nodeId)

/// Move `moved` to `target`.
let move (session: Session.SessionState) (target: Target) (moved: NodeId) : PropertyEditor.CommitOutcome =
  withRoot session (fun root -> commitPlaced session (Placement.moveOp root moved target))

/// Nudge `nodeId` one place among its siblings (`-1` up, `+1` down).
let nudge (session: Session.SessionState) (nodeId: NodeId) (delta: int) : PropertyEditor.CommitOutcome =
  withRoot session (fun root -> commitPlaced session (Placement.nudgeOp root nodeId delta))

// ─── flat diagnostic surface (cross-boundary friendly) ───────────────────────
//
// F# lists, records and DUs are awkward to assert on from the JS side of the
// Fable boundary, so — exactly as the cursor / op-log / property-editor helpers
// do — the same logic is projected to plain strings and flat records. These are
// the headless test surface AND a host-language-agnostic description of the
// structural vocabulary.

/// A placement spelled as a plain string: `"last"`, `"first"`, `"before:<id>"`,
/// `"after:<id>"`. Anything unrecognised is `Last` — the append the ops do on
/// their own, which is the safe reading of an unknown request.
let parsePlacement (spec: string) : Placement =
  if spec = "first" then
    Placement.First
  elif spec.StartsWith "before:" then
    Placement.Before(NodeId(spec.Substring 7))
  elif spec.StartsWith "after:" then
    Placement.After(NodeId(spec.Substring 6))
  else
    Placement.Last

/// The inverse — a placement as its plain-string spelling.
let placementSpec (placement: Placement) : string =
  match placement with
  | Placement.Last -> "last"
  | Placement.First -> "first"
  | Placement.Before anchor -> "before:" + idText anchor
  | Placement.After anchor -> "after:" + idText anchor

let private targetOf (parentId: string) (placement: string) : Target =
  { ParentId = NodeId parentId
    Placement = parsePlacement placement }

/// Every kind discriminator the canonical schema knows — the palette's whole
/// vocabulary source, before any per-destination filtering.
let schemaKinds () : string array = kindsOf (Agent.getKindSchema None)

/// The raw synthesised wire JSON for one kind, addressed by plain string — the
/// emitter lock's subject (see `defaultNodeWire`).
let defaultWireFor (session: Session.SessionState) (kind: string) : string =
  match session.Tree with
  | None -> ""
  | Some root -> defaultNodeWire root kind

/// A parent's structural child ids, as plain strings.
let childIds (session: Session.SessionState) (parentId: string) : string array =
  match session.Tree with
  | None -> [||]
  | Some root -> childIdsOf root (NodeId parentId) |> List.map idText |> Array.ofList

/// The kinds offered at a destination, as plain strings.
let paletteKinds (session: Session.SessionState) (parentId: string) (placement: string) : string array =
  palette session (targetOf parentId placement) |> List.map _.Kind |> Array.ofList

/// The default structural destination for a focused node, as
/// `"<parentId>|<placement>"` (`""` when there is nowhere to put anything).
let defaultTargetSpec (session: Session.SessionState) (focused: string) : string =
  match session.Tree with
  | None -> ""
  | Some root ->
    defaultTarget root (NodeId focused)
    |> Option.map (fun t -> idText t.ParentId + "|" + placementSpec t.Placement)
    |> Option.defaultValue ""

/// The id the cursor should fall back to once `nodeId` is removed (`""` for the
/// root, which cannot be removed).
let fallbackIdAfterRemove (session: Session.SessionState) (nodeId: string) : string =
  match session.Tree with
  | None -> ""
  | Some root ->
    fallbackAfterRemove root (NodeId nodeId)
    |> Option.map idText
    |> Option.defaultValue ""

/// The canonical JSON of the most recently APPLIED op (`""` when none) — the
/// surface the placement claim is asserted on: the shape of the op is the claim.
let lastOpJson (session: Session.SessionState) : string =
  session.Ops |> List.tryLast |> Option.defaultValue ""

let private outcome
  (session: Session.SessionState)
  (result: PropertyEditor.CommitOutcome)
  : {| Ok: bool
       Error: string
       Next: Session.SessionState |}
  =
  match result with
  | PropertyEditor.Committed next -> {| Ok = true; Error = ""; Next = next |}
  | PropertyEditor.Rejected message ->
    {| Ok = false
       Error = message
       Next = session |}

/// Insert a default node of `kind` at a destination, addressed by plain strings.
/// `Ok` false leaves `Next` as the input session, so a test can assert the tree
/// really was untouched.
let insertAt
  (session: Session.SessionState)
  (parentId: string)
  (placement: string)
  (kind: string)
  : {| Ok: bool
       Error: string
       Next: Session.SessionState |}
  =
  match session.Tree with
  | None -> outcome session (PropertyEditor.Rejected "there is no tree to edit")
  | Some root ->
    match defaultNodeFor root kind with
    | None ->
      outcome session (PropertyEditor.Rejected("no minimal default node could be synthesised for '" + kind + "'"))
    | Some child -> outcome session (insert session (targetOf parentId placement) child)

/// Remove a node, addressed by plain string.
let removeAt
  (session: Session.SessionState)
  (nodeId: string)
  : {| Ok: bool
       Error: string
       Next: Session.SessionState |}
  =
  outcome session (remove session (NodeId nodeId))

/// Move a node to a destination, addressed by plain strings.
let moveTo
  (session: Session.SessionState)
  (nodeId: string)
  (parentId: string)
  (placement: string)
  : {| Ok: bool
       Error: string
       Next: Session.SessionState |}
  =
  outcome session (move session (targetOf parentId placement) (NodeId nodeId))

/// Nudge a node among its siblings, addressed by plain string.
let nudgeAt
  (session: Session.SessionState)
  (nodeId: string)
  (delta: int)
  : {| Ok: bool
       Error: string
       Next: Session.SessionState |}
  =
  outcome session (nudge session (NodeId nodeId) delta)

/// Whether a node may legally be dropped under a parent, addressed by plain
/// strings — the pick-up/drop interaction's own guard, made assertable.
let canDropAt (session: Session.SessionState) (moved: string) (parentId: string) : bool =
  match session.Tree with
  | None -> false
  | Some root -> canDrop root (NodeId moved) (NodeId parentId)
