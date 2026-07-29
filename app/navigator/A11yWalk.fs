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
//     as wire JSON from a model. The Fable-safe RUNTIME validator
//     (`Fuaran.UI.PreEmitValidate`, which the property panel already gates
//     every edit on) carries no accessibility rules at all.
//     So the three checks below are re-derived over the DECODED TREE, each
//     grounded in a real surface, and each says which.
//   · WHICH kinds are interactive (`interactiveKinds`). The language states
//     this — every smart constructor in `Fuaran.fs` passes a per-kind
//     `Defaults.Accessibility.*` — but it states it as one value per call site,
//     not as a queryable `NodeKind -> Accessibility` surface. The set below is
//     the four kinds paired with an INTERACTIVE role default. A source-lock
//     test pins it against that language source, in the safe direction only:
//     every kind named here must really carry a non-`none` default, so the
//     audit can never accuse a node of a defect the language does not
//     recognise. It deliberately does NOT assert completeness — a newly
//     interactive kind is then un-audited rather than falsely flagged, and
//     un-audited is the failure this lens can afford.
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

// ─── the one re-derived table ────────────────────────────────────────────────

/// The kinds whose rendered element is INTERACTIVE and so must reach a screen
/// reader with a name. Each is a kind the language pairs with an interactive
/// `Defaults.Accessibility.*` in `Fuaran.fs` — `button` and `fileUpload` take
/// `Role = Button`, `select` takes `Role = Custom "combobox"`, `form` takes
/// `Role = Form`. Pinned by the source-lock test; see the header for why the
/// pin is one-directional.
let interactiveKinds = [ "Button"; "Select"; "Form"; "FileUpload" ]

/// The wire names of the fields whose text content NAMES an interactive
/// element — `label` on Button / Select / FileUpload, `submitLabel` on Form.
/// Both are required fields of their kinds, so the schema surfaces them; this
/// list only says which of a kind's required fields is the naming one.
let private nameFieldWireNames = [ "label"; "submitLabel" ]

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

/// Every accessibility finding against one node, in a stable order.
/// `root` is needed only to resolve `labelledBy` / `describedBy` references.
let nodeFlags (root: Node<obj>) (node: Node<obj>) : Flag list =
  let json = nodeJson node

  if json = "" then
    []
  else
    let id = idText (NodeId node.Id)
    let disc = displayOf (valueAt json "kind.$type")
    let required = requiredRows node disc

    // A node HAS a declared name when the trait names it directly or points at
    // a labelling node. Tested on the declaration, not the emission — see the
    // header: a bound label resolves to nothing here and is still a name.
    let declaresName =
      isPresent (valueAt json "accessibility.label")
      || isPresent (valueAt json "accessibility.labelledBy")

    let isInteractive = List.contains disc interactiveKinds

    let nameRow =
      if not isInteractive then
        None
      else
        required
        |> List.tryFind (fun (wireName, field) -> List.contains wireName nameFieldWireNames && isTextRow field)

    // ── A11Y-NAME — an interactive element that reaches nobody by name ──
    let nameFlags =
      if not isInteractive then
        []
      else
        match nameRow with
        | Some(_, field) when not declaresName && field.Current = "" ->
          [ { NodeId = id
              Code = "A11Y-NAME"
              Severity = Severity.Error
              Message =
                sprintf
                  "%s reaches a screen reader with no name: '%s' is empty and the node declares neither accessibility.label nor accessibility.labelledBy. Its accessible name would come from its text content, and there is none."
                  disc
                  field.Path
              Fix = Some field.Path
              Unfixable = "" } ]
        | _ -> []

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

    // ── A11Y-REF — an accessibility reference pointing at nothing ──
    // No op in the vocabulary reaches the `accessibility` trait: `UpdateProp`
    // paths are rooted INSIDE the kind spec, and `Introspect.availableFields`
    // publishes no accessibility field precisely because none is reachable.
    // So this one is reported honestly and left unfixable rather than given a
    // fix path that would not work.
    let refFlags =
      [ "labelledBy", "aria-labelledby"; "describedBy", "aria-describedby" ]
      |> List.choose (fun (key, attr) ->
        let target = valueAt json ("accessibility." + key)

        if not (isPresent target) then
          None
        else
          let wanted = displayOf target

          match Introspect.findNode (NodeId wanted) root with
          | Some _ -> None
          | None ->
            Some
              { NodeId = id
                Code = "A11Y-REF"
                Severity = Severity.Error
                Message =
                  sprintf
                    "accessibility.%s names '%s', which is not a node in this tree — the emitted %s points at nothing and the reference is silently ignored."
                    key
                    wanted
                    attr
                Fix = None
                Unfixable =
                  "no op reaches the accessibility trait — UpdateProp paths are rooted inside the kind spec. Fix it at the source of the emission." })

    nameFlags @ textFlags @ refFlags

// ─── the walk ────────────────────────────────────────────────────────────────

/// Every node in DFS pre-order — the same order the navigator's cursor steps,
/// built over the same traversal surface (`Introspect.descendantNodes`), so
/// "the next flagged node" and "the next node" agree about what comes next.
let rec private walk (node: Node<obj>) : Node<obj> list =
  node :: (Introspect.descendantNodes node |> List.collect walk)

/// Every flag in the tree, in walk order.
let treeFlags (root: Node<obj>) : Flag list =
  walk root |> List.collect (nodeFlags root)

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

/// The re-derived interactive-kind set as plain strings — an F# list is a
/// linked structure across the Fable boundary, so the source-lock test needs
/// the projection rather than the list itself.
let interactiveKindNames: string array = Array.ofList interactiveKinds

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
              prop.title "A — audit the tree as you walk it: what a screen reader gets from each node"
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
