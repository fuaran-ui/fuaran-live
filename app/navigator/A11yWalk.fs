module Fuaran.Live.A11yWalk

// ============================================================================
//  The accessibility lens — the cursor walk turned into an audit.
//
//  The navigator already steps every node in the tree. An accessibility review
//  is inherently node-by-node, so the two are the same motion: switch the lens
//  on and each step reports what a screen reader gets from the focused node
//  instead of the general property panel. Stepping the whole tree IS the audit;
//  finishing with no flags is the pass.
//
//  ── What is REAL here, and what is re-derived ──────────────────────────────
//
//  The distinction matters, because an audit that quietly invents its own idea
//  of correctness is worse than no audit at all.
//
//  REAL (consumed, never re-implemented):
//   · The emitted `aria-*` — `Fuaran.UI.Renderer.Accessibility.
//     accessibilityAttributes`, the single projection BOTH renderers call (the
//     Feliz client renderer at `Render.fs`, the ViewEngine server renderer).
//     What this lens shows as "emitted" is therefore the same list the DOM
//     really gets, not a second opinion about it.
//   · WHICH fields a kind requires — the canonical wire-format JSON Schema's
//     per-kind `required` array, reached through `Agent.getKindSchema` (the
//     same resolver the property panel and the agent loop use). No table of
//     required fields is written here.
//   · WHICH fields are editable, and what they currently hold —
//     `PropertyEditor.fields`. Every flag's fix target IS one of those derived
//     rows, so a fix cannot address a field no op can reach.
//   · The fix itself — `PropertyEditor.commit`, unchanged. That means an a11y
//     fix is validator-gated, recorded against the navigator actor, and
//     undoable by replay, for free and by construction. There is no second
//     edit path in this module.
//
//  RE-DERIVED (minimally, and said so):
//   · The RULES. `FUARAN040` / `FUARAN041` live in `Fuaran.UI.Validator`, which
//     is a build-time F# **source** validator: it walks a syntax tree via the
//     F# Compiler Service, keys its findings on `file:line:column`, ships as an
//     `Exe`, and — unlike `Fuaran.UI` / `Fuaran.UI.Ops` / the renderer core —
//     packs no `fable/` sources. It cannot run in a browser, and even if it
//     could there is no F# source here to walk: the playground's trees arrive
//     as wire JSON from a model.
//     But the Fable-safe RUNTIME validator (`Fuaran.UI.PreEmitValidate`) DOES
//     run in a browser, over a decoded tree, and since Phase 727 it carries the
//     accessibility family. Two of the three checks below are therefore its
//     verdict, not a re-derivation (see "The mapping, PERFORMED"); the third is
//     re-derived over the DECODED TREE, grounded in a real surface, and says so.
//
//  ── STALE CLAIM, CORRECTED (2026-09-09) ────────────────────────────────────
//
//  This block used to end by saying the Fable-safe RUNTIME validator
//  (`Fuaran.UI.PreEmitValidate`, which the property panel already gates every
//  edit on) "carries no accessibility rules at all". THAT IS NO LONGER TRUE.
//  It carries three:
//
//    · `MissingAccessibleName`        — FUARAN109 (Warning)
//    · `DanglingAccessibilityReference` — FUARAN110 (Warning)
//    · `EmptyAccessibilityDeclaration`  — FUARAN111 (Warning)
//
//  A header that asserts a surface does not exist is worse than one that says
//  nothing, because it is the thing a reader checks INSTEAD of looking. It was
//  corrected before the consuming swap landed; the swap has since landed, and
//  the block below says what it did.
//
//  ── The mapping, PERFORMED (Phase 1676) ────────────────────────────────────
//
//  Two of the three checks below no longer re-derive anything: `A11Y-NAME` IS
//  FUARAN109 and `A11Y-REF` IS FUARAN110, read off `PreEmitValidate.validate`
//  over the same tree, with the validator's own message and its own severity.
//  The local codes survive as the lens's UI vocabulary (a quick-fix is keyed by
//  them, and the flag card reads the same way it always did), but they are now
//  a LABEL on somebody else's verdict rather than a verdict of their own — so a
//  rule the language tightens tightens here in the same package bump, with no
//  edit in this file.
//
//  That also retired the hand `interactiveKinds` list and its source-lock test.
//  The list existed because this lens had to decide interactivity for itself;
//  it does not any more. What remains is the naming SLOT — which required field
//  of an interactive kind is the one FUARAN109 speaks for — and that is read
//  from the language's own `Defaults.Accessibility.*` VALUES rather than from a
//  table of kind names (`interactiveNamingSlot`, below). A pin test is
//  redundant against a reference to the value itself: if the language stopped
//  pairing a kind with an interactive default, this would return `None` and the
//  audit would go quiet about that kind, which is the direction it is allowed
//  to be wrong in.
//
//  `A11Y-TEXT` DOES NOT MAP, and it is KEPT. It looks like FUARAN111 and is a
//  different check on a different subject:
//
//    · FUARAN111's subject is the ACCESSIBILITY TRAIT — a slot the node
//      DECLARES and leaves empty (`accessibility.label` bound to a static
//      empty string, or a `labelledBy` / `describedBy` naming one). Its whole
//      argument is that a declared-and-empty slot SILENCES FUARAN109.
//    · `A11Y-TEXT`'s subject is the KIND'S OWN CONTENT — a field the wire
//      schema lists in that kind's `required` array, holding the empty string.
//      A `Button` whose `label` is `""` renders blank; nothing about its
//      accessibility trait is involved, and FUARAN111 is silent on it.
//
//  So the two do not overlap in subject, only in intent, and mapping one onto
//  the other would DELETE the content check while appearing to preserve it.
//
//  The alternative the question posed — move `A11Y-TEXT` into the validator as
//  a rule of its own — is also rejected, for a reason that will not change: the
//  check is driven by the canonical JSON Schema's per-kind `required` array,
//  read at runtime through `Agent.getKindSchema`. The validator has no schema
//  document; this lens does. A rule cannot be moved to a place that cannot
//  answer the question it asks.
//
//  ── One correction the substrate's own comments get wrong ──────────────────
//
//  `Defaults.fs` and the FUARAN040 message both say the renderer "derives
//  `aria-label` from `ButtonSpec.Label` when `Accessibility.Label` is None".
//  It does not: `renderButton` emits the label as the button's TEXT CONTENT and
//  injects no `aria-label`. The distinction matters for an audit, because it
//  changes what counts as a pass. The accessible name of a node is therefore
//  modelled here the way a browser computes it:
//
//      Accessibility.Label  →  aria-labelledby target  →  element text content
//
//  which is why an empty structural label on an interactive kind is a real
//  finding, and why a node carrying `accessibility.label` is not flagged even
//  when its structural label is blank.
//
//  ── Declared, not merely emitted ───────────────────────────────────────────
//
//  The flags test what the node DECLARES (the `accessibility` keys present on
//  the wire), while the card DISPLAYS what is emitted. The two differ for a
//  bound label: `accessibilityAttributes` resolves bindings, and the playground
//  resolves non-`Static` bindings to nothing, so emission alone would accuse a
//  correctly-bound label of being absent. The build-time rule makes the same
//  concession in the same place ("trust the binding to produce a non-empty
//  string at runtime"), so this is the substrate's posture, not a local
//  softening of it.
//
//  ── Not conflated ─────────────────────────────────────────────────────────
//
//  `kind.role` (a `BoxRole` — what a container MEANS) and `accessibility.role`
//  (the ARIA role) are different keys at different levels of the same node and
//  are never read for each other.
// ============================================================================

open Fable.Core
open Feliz
open Fuaran.UI.Types

module Introspect = Fuaran.UI.Ops.Introspect
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module Aria = Fuaran.UI.Renderer.Accessibility
module Resolver = Fuaran.UI.Renderer.BindingResolver
module Validate = Fuaran.UI.PreEmitValidate
module Defaults = Fuaran.UI.Defaults

// ─── the flag model (pure — no DOM, no React) ────────────────────────────────

/// Mirrors `Fuaran.UI.Validator.Findings.Severity` — the vocabulary an author
/// already knows from the build-time validator, so a finding here reads the
/// same way one there does. Qualified because `Error` is also `Result`'s.
[<RequireQualifiedAccess>]
type Severity =
  | Error
  | Warning

/// The raw string of a `NodeId` — the wire spelling, and the value every flat
/// surface below is keyed by.
let private idText (NodeId s) : string = s

let severityTag (s: Severity) : string =
  match s with
  | Severity.Error -> "error"
  | Severity.Warning -> "warning"

/// One accessibility finding against one node.
type Flag =
  {
    /// The node the finding is about, as its plain id string.
    NodeId: string
    /// A stable code. Deliberately NOT `FUARAN0xx`: these are tree-walk
    /// re-derivations, and borrowing the build-time validator's identifiers
    /// would claim a provenance they do not have.
    Code: string
    Severity: Severity
    Message: string
    /// The `PropertyEditor.Field.Path` a quick-fix opens — always a real
    /// derived row, so committing it is an ordinary op. `None` when no op in
    /// the vocabulary reaches the defect (see `Fix` on `A11Y-REF`).
    Fix: string option
    /// Why there is no fix, when there is none.
    Unfixable: string
  }

// ─── JS leaves (structural reads over parsed JSON) ───────────────────────────
//
// The same inline-JS idiom `PropertyEditor` and `Session` use for their
// extraction leaves. Each is total: an unrecognised shape yields a null the F#
// side reads as "absent", which under-reports rather than guesses.

/// The raw value at a dotted path in a JSON document, or `undefined`.
[<Emit("""(function(json, path){
  var v; try { v = JSON.parse(json); } catch (e) { return undefined; }
  var segs = String(path).split('.');
  for (var i = 0; i < segs.length; i++) {
    if (v === null || typeof v !== 'object') { return undefined; }
    v = v[segs[i]];
  }
  return v;
})($0, $1)""")>]
let private valueAt (json: string) (path: string) : obj = jsNative

/// A raw JSON value as display text ("" when absent).
[<Emit("""(function(v){
  if (v === null || v === undefined) { return ''; }
  if (typeof v === 'object') { try { return JSON.stringify(v); } catch (e) { return '…'; } }
  return String(v);
})($0)""")>]
let private displayOf (v: obj) : string = jsNative

/// Whether a JSON value is present at all (a declared key, whatever its value).
[<Emit("($0 !== null && $0 !== undefined)")>]
let private isPresent (v: obj) : bool = jsNative

/// The `required` array published by a resolved kind schema.
[<Emit("($0 && Array.isArray($0.required)) ? $0.required : []")>]
let private requiredOf (kindSchema: obj) : string array = jsNative

/// The keys an object carries, sorted — the declared-trait readout.
[<Emit("($0 && typeof $0 === 'object') ? Object.keys($0).sort() : []")>]
let private keysOf (v: obj) : string array = jsNative

// ─── schema access (memoised, as in the property panel) ──────────────────────
//
// `Agent.getKindSchema` re-parses the whole schema document per call and the
// lens runs over every node of the tree, so the per-kind result is cached. A
// pure memo of a pure function of a compile-time constant: droppable at any
// time with no behavioural difference.

let private schemaCache = System.Collections.Generic.Dictionary<string, obj>()

let private kindSchema (disc: string) : obj =
  match schemaCache.TryGetValue disc with
  | true, cached -> cached
  | _ ->
    let resolved = Agent.getKindSchema (Some disc)
    schemaCache[disc] <- resolved
    resolved

// ─── the naming slot, read off the language's own defaults ───────────────────

/// Which of an interactive kind's required fields is the one whose text NAMES
/// the element — `label` on Button / Select / FileUpload, `submitLabel` on Form
/// (its submit button) — or `None` for a kind the language does not pair with an
/// interactive accessibility default.
///
/// The VERDICT it carries is interactivity, and the verdict comes from the
/// language's own per-kind default VALUE rather than from a table of kind names
/// restating it: if `Defaults.Accessibility.button` stopped declaring a role,
/// this returns `None` and the audit goes quiet about buttons, which is the
/// direction it is allowed to be wrong in. That is why the hand list this
/// replaced needed a source-lock test and this does not.
///
/// It reads the same input `PreEmitValidate`'s own FUARAN109 gate reads, so the
/// two agree by construction — which is what the one remaining consumer needs:
/// `A11Y-TEXT` must exclude this slot OUTRIGHT, including on the nodes where
/// FUARAN109 is silent because the trait names the element another way.
let private interactiveNamingSlot (kind: NodeKind<obj>) : string option =
  let named (dflt: Accessibility option) (slot: string) =
    match dflt with
    | Some a when a.Role.IsSome -> Some slot
    | _ -> None

  match kind with
  | NodeKind.Button _ -> named Defaults.Accessibility.button "label"
  | NodeKind.Select _ -> named Defaults.Accessibility.select "label"
  | NodeKind.Form _ -> named Defaults.Accessibility.form "submitLabel"
  | NodeKind.FileUpload _ -> named Defaults.Accessibility.fileUpload "label"
  | _ -> None

/// The accessibility half of `PreEmitValidate`'s verdict on a whole tree —
/// FUARAN109 and FUARAN110, the two rules this lens consumes rather than
/// re-derives. Computed once per walk: FUARAN110 needs the whole tree (a
/// reference is dangling only relative to every id in it), and re-validating per
/// node would be quadratic for an identical answer.
///
/// Total: a validator that throws on some shape must not take the audit down
/// with it, so a failure reads as "no accessibility findings" — under-reporting,
/// which is this lens's standing direction of error.
let private validatorA11yDefects (root: Node<obj>) : Validate.PreEmitDefect list =
  try
    match Validate.validate root with
    | Ok() -> []
    | Error defects ->
      defects
      |> List.filter (fun d ->
        match d with
        | Validate.PreEmitDefect.InteractiveWithoutAccessibleName _
        | Validate.PreEmitDefect.DanglingAccessibilityReference _ -> true
        | _ -> false)
  with _ ->
    []

/// The validator's severity in this lens's vocabulary. The two mirror
/// `Fuaran.UI.Validator.Findings.Severity` between them, so this is a spelling
/// change and not a judgement.
let private severityOf (s: Validate.DefectSeverity) : Severity =
  match s with
  | Validate.DefectSeverity.Error -> Severity.Error
  | Validate.DefectSeverity.Warning -> Severity.Warning

// ─── derivation ──────────────────────────────────────────────────────────────

/// The node's canonical wire JSON — the source of every declared value, and of
/// the `$type` discriminator the schema is keyed by. Read off the wire rather
/// than from `Introspect.kindName`, which is the DISPLAY tag and differs from
/// the discriminator for at least one kind (`DataGrid` displays as "Grid").
let private nodeJson (node: Node<obj>) : string =
  try
    Canon.encodeNode node
  with _ ->
    ""

/// The `aria-*` / `role` attributes the renderer really emits for this node —
/// the production projection, resolved against empty binding sources (the
/// playground has no consumer data plumbing, exactly as `Agent` documents for
/// its own resolution).
let emittedAria (node: Node<obj>) : (string * string) list =
  Aria.accessibilityAttributes Resolver.empty node.Accessibility

/// The emitted attributes as `"name=value"` text, in the renderer's own
/// deterministic order (label, labelledby, describedby, role, live, hidden).
let ariaSummary (node: Node<obj>) : string array =
  emittedAria node |> List.map (fun (k, v) -> k + "=" + v) |> Array.ofList

/// The `accessibility` keys the node DECLARES on the wire, sorted. Empty when
/// the trait is absent — which, for a model-emitted tree, is the common case:
/// the per-kind `Defaults.Accessibility` values are applied by the smart
/// constructors at authoring time, so a decoded wire node genuinely carries no
/// ARIA unless the JSON said so.
let declaredTrait (node: Node<obj>) : string array =
  keysOf (valueAt (nodeJson node) "accessibility")

/// The required fields of this node's kind, paired with the derived editor row
/// that reaches each — dropping any the property panel does not publish, so a
/// flag can never name a field no op addresses.
let private requiredRows (node: Node<obj>) (disc: string) : (string * PropertyEditor.Field) list =
  let rows =
    PropertyEditor.fields node |> List.filter (fun f -> f.Group = "Properties")

  requiredOf (kindSchema disc)
  |> Array.toList
  |> List.choose (fun wireName ->
    rows
    |> List.tryFind (fun f -> f.Wire = "kind." + wireName)
    |> Option.map (fun f -> wireName, f))

/// Whether a derived row is a free-text field — the only shape "blank" is a
/// meaningful verdict about. A choice, a toggle or a number is never blank.
let private isTextRow (field: PropertyEditor.Field) : bool =
  field.Editor = PropertyEditor.Editor.Text

/// Every accessibility finding against one node, in a stable order, given the
/// validator's verdict over the whole tree (which `treeFlags` computes once).
let private nodeFlagsWith (defects: Validate.PreEmitDefect list) (root: Node<obj>) (node: Node<obj>) : Flag list =
  let json = nodeJson node

  if json = "" then
    []
  else
    let id = idText (NodeId node.Id)
    let disc = displayOf (valueAt json "kind.$type")
    let required = requiredRows node disc

    /// The derived editor row reaching one wire field of this node's kind, when
    /// the property panel publishes one. A flag's fix target is always one of
    /// these, so a fix can never name a field no op addresses.
    let rowFor (wireName: string) =
      required
      |> List.tryFind (fun (name, field) -> name = wireName && isTextRow field)

    // The slot FUARAN109 speaks for on this kind, if any. Read here rather than
    // inside the two consumers below so both agree by construction.
    let namingSlot = interactiveNamingSlot node.Kind
    let nameRow = namingSlot |> Option.bind rowFor

    // ── A11Y-NAME — FUARAN109, verbatim ──
    //
    // The message is the validator's own. Re-writing it here would be a second
    // opinion about a rule this lens no longer owns, and the two would drift the
    // first time the rule's wording was sharpened.
    let nameFlags =
      defects
      |> List.choose (fun d ->
        match d with
        | Validate.PreEmitDefect.InteractiveWithoutAccessibleName(nodeId, _, slot) when nodeId = id ->
          let _, severity, message = Validate.describe d

          Some
            { NodeId = id
              Code = "A11Y-NAME"
              Severity = severityOf severity
              Message = message
              // The slot the rule names, resolved to the row that edits it. A
              // slot the property panel does not publish leaves the finding
              // standing and unfixable rather than dropping it: the defect is
              // real either way, and only the quick-fix is unavailable.
              Fix = rowFor slot |> Option.map (fun (_, field) -> field.Path)
              Unfixable =
                match rowFor slot with
                | Some _ -> ""
                | None -> "no editable field reaches '" + slot + "' on this node" }
        | _ -> None)

    // ── A11Y-TEXT — a required text field left blank (renders empty) ──
    //
    // The naming field of an interactive kind is excluded OUTRIGHT, not merely
    // when A11Y-NAME happened to fire. A11Y-NAME owns that field's verdict, and
    // it has the one piece of context this check does not: whether the trait
    // names the element another way. A button with a blank label and an
    // `accessibility.label` is odd-looking but perfectly announced — flagging
    // it here would be exactly the false positive an audit cannot afford, and
    // gating on "did NAME fire" reintroduces it, because NAME is silent in
    // precisely that case.
    let nameFieldPath = nameRow |> Option.map (fun (_, field) -> field.Path)

    let textFlags =
      required
      |> List.filter (fun (_, field) -> isTextRow field && field.Current = "" && nameFieldPath <> Some field.Path)
      |> List.map (fun (wireName, field) ->
        { NodeId = id
          Code = "A11Y-TEXT"
          Severity = Severity.Error
          Message =
            sprintf
              "%s requires '%s' and it is empty — the node renders blank, so there is nothing for a screen reader to announce."
              disc
              wireName
          Fix = Some field.Path
          Unfixable = "" })

    // ── A11Y-REF — FUARAN110, verbatim ──
    //
    // Reported honestly and left unfixable: no op in the vocabulary reaches the
    // `accessibility` trait — `UpdateProp` paths are rooted INSIDE the kind
    // spec, and `Introspect.availableFields` publishes no accessibility field
    // precisely because none is reachable. A fix path here would not work.
    let refFlags =
      defects
      |> List.choose (fun d ->
        match d with
        | Validate.PreEmitDefect.DanglingAccessibilityReference(nodeId, _, _) when nodeId = id ->
          let _, severity, message = Validate.describe d

          Some
            { NodeId = id
              Code = "A11Y-REF"
              Severity = severityOf severity
              Message = message
              Fix = None
              Unfixable =
                "no op reaches the accessibility trait — UpdateProp paths are rooted inside the kind spec. Fix it at the source of the emission." }
        | _ -> None)

    nameFlags @ textFlags @ refFlags

/// Every accessibility finding against one node, in a stable order.
/// `root` is needed to resolve `labelledBy` / `describedBy` references and to
/// give the validator the whole tree its cross-node rule judges over.
let nodeFlags (root: Node<obj>) (node: Node<obj>) : Flag list =
  nodeFlagsWith (validatorA11yDefects root) root node

// ─── the walk ────────────────────────────────────────────────────────────────

/// Every node in DFS pre-order — the same order the navigator's cursor steps,
/// built over the same traversal surface (`Introspect.descendantNodes`), so
/// "the next flagged node" and "the next node" agree about what comes next.
let rec private walk (node: Node<obj>) : Node<obj> list =
  node :: (Introspect.descendantNodes node |> List.collect walk)

/// Every flag in the tree, in walk order.
let treeFlags (root: Node<obj>) : Flag list =
  let defects = validatorA11yDefects root
  walk root |> List.collect (nodeFlagsWith defects root)

/// The ids of every flagged node, in walk order, without repeats — a node with
/// three findings is one stop on the flags-only walk, not three.
let flaggedIds (root: Node<obj>) : string array =
  treeFlags root
  |> List.map _.NodeId
  |> List.fold (fun acc id -> if List.contains id acc then acc else acc @ [ id ]) []
  |> Array.ofList

/// `n flagged of m nodes` — the walk summary.
let summary (root: Node<obj>) : int * int =
  Array.length (flaggedIds root), List.length (walk root)

/// The total number of findings (a node may carry several).
let flagCount (root: Node<obj>) : int = List.length (treeFlags root)

/// The next flagged node after `fromId` in walk order, or `None` at the end.
/// Ends STOP rather than wrap, matching the plain walk's rule: holding the key
/// visits every flag exactly once and comes to rest somewhere knowable. An
/// unknown `fromId` starts the search from the top.
let nextFlaggedId (root: Node<obj>) (fromId: string) : string option =
  let order = walk root |> List.map (fun n -> idText (NodeId n.Id))
  let flagged = flaggedIds root |> Set.ofArray

  match order |> List.tryFindIndex (fun id -> id = fromId) with
  | None -> order |> List.tryFind flagged.Contains
  | Some i -> order |> List.skip (i + 1) |> List.tryFind flagged.Contains

/// The previous flagged node before `fromId` in walk order, or `None` at the
/// start. Same stop-at-the-end rule.
let prevFlaggedId (root: Node<obj>) (fromId: string) : string option =
  let order = walk root |> List.map (fun n -> idText (NodeId n.Id))
  let flagged = flaggedIds root |> Set.ofArray

  match order |> List.tryFindIndex (fun id -> id = fromId) with
  | None -> order |> List.tryFind flagged.Contains
  | Some i -> order |> List.truncate i |> List.rev |> List.tryFind flagged.Contains

// ─── flat diagnostic surface (cross-boundary friendly) ───────────────────────
//
// F# lists, records and DUs are awkward to assert on across the Fable boundary,
// so — exactly as the Phase 710 cursor helpers and `PropertyEditor`'s flat
// surface do — the same values are projected to plain strings and arrays. These
// are the headless test surface AND a host-agnostic description of the audit.

/// Every finding as `"<nodeId>|<code>|<severity>|<fixPath>"`, walk order.
/// `fixPath` is empty for an unfixable finding.
let flagSummary (root: Node<obj>) : string array =
  treeFlags root
  |> List.map (fun f ->
    f.NodeId
    + "|"
    + f.Code
    + "|"
    + severityTag f.Severity
    + "|"
    + (f.Fix |> Option.defaultValue ""))
  |> Array.ofList

/// The findings against one node, addressed by its plain id string.
let flagsAt (root: Node<obj>) (nodeId: string) : string array =
  match Introspect.findNode (NodeId nodeId) root with
  | None -> [||]
  | Some node ->
    nodeFlags root node
    |> List.map (fun f -> f.Code + "|" + severityTag f.Severity + "|" + (f.Fix |> Option.defaultValue ""))
    |> Array.ofList

/// The emitted `aria-*` for one node, addressed by its plain id string.
let ariaAt (root: Node<obj>) (nodeId: string) : string array =
  match Introspect.findNode (NodeId nodeId) root with
  | None -> [||]
  | Some node -> ariaSummary node

/// `nextFlaggedId` / `prevFlaggedId` projected to "" for "no further flag", so
/// a test can drive the flags-only walk without an option across the boundary.
let nextFlagText (root: Node<obj>) (fromId: string) : string =
  nextFlaggedId root fromId |> Option.defaultValue ""

let prevFlagText (root: Node<obj>) (fromId: string) : string =
  prevFlaggedId root fromId |> Option.defaultValue ""

/// The quick-fix, addressed by plain strings: commit `raw` to the field the
/// flag `code` on node `nodeId` points at. Routed through
/// `PropertyEditor.commitAt`, so this is the SAME op path as any other edit —
/// validator-gated, recorded, undoable. Refuses rather than inventing a path
/// when the flag has no fix.
let quickFixAt
  (session: Session.SessionState)
  (nodeId: string)
  (code: string)
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
  | None -> refused "there is no tree to audit"
  | Some root ->
    match Introspect.findNode (NodeId nodeId) root with
    | None -> refused ("no node with id '" + nodeId + "'")
    | Some node ->
      match nodeFlags root node |> List.tryFind (fun f -> f.Code = code) with
      | None -> refused ("no '" + code + "' finding on node '" + nodeId + "'")
      | Some flag ->
        match flag.Fix with
        | None -> refused flag.Unfixable
        | Some path -> PropertyEditor.commitAt session nodeId path raw

// ─── the lens view ───────────────────────────────────────────────────────────
//
// The card region the navigator swaps in when the lens is on. It owns exactly
// one piece of local state — the draft text of the field being fixed — and
// commits through `PropertyEditor.commit`, the same call the general panel
// makes. Nothing here edits a tree.

/// The per-node cursor badge: the finding count on the focused node, or a tick
/// when it is clean. Rendered in the card head beside the kind and id.
let badge (root: Node<obj>) (node: Node<obj>) : ReactElement =
  match nodeFlags root node with
  | [] ->
    Html.span
      [ prop.className "fl-a11y-badge fl-a11y-badge-ok"
        prop.title "No accessibility findings on this node"
        prop.text "✓ a11y" ]
  | flags ->
    Html.span
      [ prop.className "fl-a11y-badge fl-a11y-badge-flagged"
        prop.title (flags |> List.map _.Message |> String.concat " · ")
        prop.text (
          if List.length flags = 1 then
            "1 a11y finding"
          else
            sprintf "%d a11y findings" (List.length flags)
        ) ]

/// The lens toggle + the walk summary — `n flagged of m`. The count is the
/// audit's whole readout, so it is stated in words rather than left as a dot.
let toggle (root: Node<obj> option) (isOn: bool) (onToggle: unit -> unit) : ReactElement =
  let readout =
    match root with
    | None -> "no tree"
    | Some r ->
      let flagged, total = summary r

      if flagged = 0 then
        sprintf "%d nodes · no findings" total
      else
        sprintf "%d of %d nodes flagged" flagged total

  Html.div
    [ prop.className "fl-a11y-controls"
      prop.children
        [ Html.button
            [ prop.className (if isOn then "fl-btn" else "fl-btn ghost")
              prop.text (
                if isOn then
                  "Accessibility lens: on"
                else
                  "Accessibility lens: off"
              )
              prop.title "Audit the tree as you walk it: what a screen reader gets from each node"
              prop.ariaPressed isOn
              prop.onClick (fun _ -> onToggle ()) ]
          Html.span [ prop.className "fl-nav-count"; prop.text readout ] ] ]

[<ReactComponent>]
let private LensPanel
  (session: Session.SessionState)
  (root: Node<obj>)
  (node: Node<obj>)
  (onEdit: Session.SessionState -> unit)
  : ReactElement =
  let drafts, setDrafts = React.useState (Map.empty: Map<string, string>)
  let failure, setFailure = React.useState (None: (string * string) option)

  let nodeKey = idText (NodeId node.Id)

  // A new focus means a new node's findings: drop drafts + the inline error
  // rather than carry one node's half-typed fix onto another's.
  React.useEffect (
    (fun () ->
      setDrafts Map.empty
      setFailure None),
    [| box nodeKey |]
  )

  let flags = nodeFlags root node
  let emitted = emittedAria node
  let declared = declaredTrait node

  let commitFix (path: string) (raw: string) =
    match PropertyEditor.fields node |> List.tryFind (fun f -> f.Path = path) with
    | None -> setFailure (Some(path, "that field is no longer derived for this node"))
    | Some field ->
      match PropertyEditor.commit session node field raw with
      | PropertyEditor.Committed next ->
        setDrafts (Map.remove path drafts)
        setFailure None
        onEdit next
      | PropertyEditor.Rejected message -> setFailure (Some(path, message))

  // ── what a screen reader gets ──
  let emission =
    Html.div
      [ prop.className "fl-a11y-group"
        prop.children
          [ Html.h4
              [ prop.className "fl-nav-group-title"
                prop.text "Emitted to the accessibility tree" ]
            (if List.isEmpty emitted then
               Html.p
                 [ prop.className "fl-a11y-none"
                   prop.text
                     "Nothing — this node emits no aria-* or role attributes. Its accessible name, if it has one, comes from its text content." ]
             else
               Html.ul
                 [ prop.className "fl-a11y-attrs"
                   prop.children
                     [ for name, value in emitted ->
                         Html.li
                           [ prop.key name
                             prop.children
                               [ Html.code [ prop.className "fl-a11y-attr"; prop.text name ]
                                 Html.span [ prop.className "fl-a11y-attr-value"; prop.text value ] ] ] ] ]) ] ]

  // ── the trait as declared on the wire ──
  let declaration =
    Html.div
      [ prop.className "fl-a11y-group"
        prop.children
          [ Html.h4 [ prop.className "fl-nav-group-title"; prop.text "Accessibility trait" ]
            (if Array.isEmpty declared then
               Html.p
                 [ prop.className "fl-a11y-none"
                   prop.text
                     "Not declared. The per-kind defaults are applied when a node is CONSTRUCTED, so a tree decoded from wire JSON carries only the ARIA the JSON stated." ]
             else
               Html.p
                 [ prop.className "fl-a11y-declared"
                   prop.text ("declares " + String.concat ", " (List.ofArray declared)) ]) ] ]

  // ── the findings, each with its one-keystroke fix ──
  let finding (index: int) (flag: Flag) =
    let control =
      match flag.Fix with
      | None -> [ Html.p [ prop.className "fl-a11y-unfixable"; prop.text flag.Unfixable ] ]
      | Some path ->
        let current =
          PropertyEditor.fields node
          |> List.tryFind (fun f -> f.Path = path)
          |> Option.map _.Current
          |> Option.defaultValue ""

        let draft = drafts |> Map.tryFind path |> Option.defaultValue current

        // Bound rather than written inline: `prop.autoFocus (index = 0)` reads
        // to F# as a NAMED ARGUMENT, not an equality test.
        let isFirstFinding = index = 0

        [ Html.div
            [ prop.className "fl-nav-field"
              prop.children
                [ Html.label [ prop.className "fl-nav-field-label"; prop.text path ]
                  Html.input
                    [ prop.className "fl-nav-field-input"
                      prop.type' "text"
                      // The first finding's field is the one Enter opens, so it
                      // takes focus when the card mounts — that IS the "one
                      // keystroke opens the relevant field pre-focused".
                      prop.autoFocus isFirstFinding
                      prop.value draft
                      prop.placeholder "type the fix, then Enter"
                      prop.onChange (fun (v: string) -> setDrafts (Map.add path v drafts))
                      prop.onKeyDown (fun ev ->
                        if ev.key = "Enter" then
                          ev.preventDefault ()
                          commitFix path draft)
                      prop.onBlur (fun _ ->
                        if draft <> current then
                          commitFix path draft) ] ] ] ]

    let error =
      match failure, flag.Fix with
      | Some(failedPath, message), Some path when failedPath = path ->
        [ Html.p [ prop.className "fl-nav-field-error"; prop.role "alert"; prop.text message ] ]
      | _ -> []

    Html.li
      [ prop.key (flag.Code + "/" + string index)
        prop.className ("fl-a11y-flag fl-a11y-flag-" + severityTag flag.Severity)
        prop.children (
          [ Html.div
              [ prop.className "fl-a11y-flag-head"
                prop.children
                  [ Html.code [ prop.className "fl-a11y-code"; prop.text flag.Code ]
                    Html.span [ prop.className "fl-a11y-sev"; prop.text (severityTag flag.Severity) ] ] ]
            Html.p [ prop.className "fl-a11y-flag-msg"; prop.text flag.Message ] ]
          @ control
          @ error
        ) ]

  let findings =
    Html.div
      [ prop.className "fl-a11y-group"
        prop.children
          [ Html.h4 [ prop.className "fl-nav-group-title"; prop.text "Findings" ]
            (if List.isEmpty flags then
               Html.p
                 [ prop.className "fl-a11y-pass"
                   prop.text "This node passes — nothing to fix here." ]
             else
               Html.ul [ prop.className "fl-a11y-flags"; prop.children (flags |> List.mapi finding) ]) ] ]

  Html.div
    [ prop.className "fl-a11y-lens"
      prop.children [ findings; emission; declaration ] ]

/// The lens card for the focused node — what the navigator renders in place of
/// the general property panel while the lens is on.
let panel
  (session: Session.SessionState)
  (root: Node<obj>)
  (node: Node<obj>)
  (onEdit: Session.SessionState -> unit)
  : ReactElement =
  LensPanel session root node onEdit
