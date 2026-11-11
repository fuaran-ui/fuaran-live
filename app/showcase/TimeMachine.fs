module Fuaran.Showcase.TimeMachine

// ============================================================================
//  The Time Machine – scrub an app's whole life like video, then fork any frame.
//  Pillar: "the app is a value".
//
//  Every Fuaran app is `initial + ops[1..n]`: replay the prefix and you have the
//  exact tree at any turn. This page stages a scripted 12-turn authoring arc –
//  an agent building a small sales dashboard, including one visible
//  mistake-and-correction – recorded as real TreeOps. Dragging the scrubber
//  reconstructs the tree at each turn by FOLDING THE SHIPPED APPLY ENGINE over
//  the op prefix (`Fuaran.UI.Ops.Apply.apply`), not by playing pre-rendered
//  frames. "Fork from here" branches the DAG: the trunk prefix up to the chosen
//  frame, then a canned alternative op-set applied on top – a genuinely
//  divergent tree that shares the trunk's hash at the fork point. A fork is
//  shown BESIDE the trunk's head, and "Merge into the trunk" folds it back as a
//  DERIVATION, not an assertion: the base is the frame the branch forked from,
//  the two deltas are everything the trunk did after it and the branch's own
//  ops, and the SHIPPED reconciliation policy (`DagReconcile`, the same function
//  the server host runs, Fable-portable in the browser) reads Core's conflict
//  report over them. Each reported pair is an agreement (both sides made the
//  same edit, so the fold applies it once), an interleave (both appended to one
//  parent, ordered by the declared child-order rule), or a refusal that names
//  its address, its shape and its class. The merge records only the script the
//  fold cannot decide itself. A refusal is settled by naming a side, which the
//  merge re-asserts as script.
//
//  The inspector shows, per turn, the REAL canonical op encoding
//  (`CanonicalJson.encodeOp`) and the op's REAL Core node identity (the DAG
//  address, `DagOpRecord.ofEntry`: sha256 over the parents, the actor and the
//  op's envelope, with a fixed timestamp so the exhibit is deterministic). No API
//  key, no server – the op-stream is bundled, the reliability floor the whole
//  site rides.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

module Apply = Fuaran.UI.Ops.Apply
module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

let private nid (s: string) : NodeId = NodeId s

// ─── The one app, as intent – small tree pieces the arc grows ────────────────

let private metricCard (id: string) (label: string) (value: string) : Node<unit> =
  Fuaran.card
    id
    { Defaults.card with
        Heading = Some(TextSource.Literal label)
        Children = [ Fuaran.markdown (id + "-v") value ] }

let private accountsGrid: Node<unit> =
  Fuaran.box
    "tm-accounts"
    { Layout = LayoutMode.Grid(3, None, Some 10)
      Role = BoxRole.Group
      KeepTogether = false
      BreakBefore = false
      Heading = None
      Children =
        [ metricCard "tm-acc-1" "Stark" "£6,750"
          metricCard "tm-acc-2" "Acme" "£4,120"
          metricCard "tm-acc-3" "Globex" "£2,880" ] }

let private initialTree: Node<unit> =
  Fuaran.box
    "tm-root"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
      Role = BoxRole.Dashboard
      KeepTogether = false
      BreakBefore = false
      Heading = None
      Children =
        [ Fuaran.heading
            "tm-title"
            { Level = 2
              Text = TextSource.Literal "Sales overview (draft)"
              Variant = HeadingVariant.Standard }
          Fuaran.callout
            "tm-note"
            { Defaults.callout with
                Tone = ToneVariant.Info
                Heading = Some(TextSource.Literal "Draft")
                Body = TextSource.Literal "Starting from a blank dashboard." } ] }

// ─── The recorded authoring arc – twelve real TreeOps ────────────────────────

/// A turn's provenance tag – surfaced so the "mistake, then self-correction"
/// beat reads on the timeline. Narration only; the op itself is the substrate.
[<RequireQualifiedAccess>]
type internal Tag =
  | Normal
  | Mistake
  | Fix

type internal Turn =
  { Op: TreeOp<unit>
    Actor: Actor
    Label: string
    Tag: Tag }

let private agent = Actor.Agent("assistant", "1", "author")
let private human = Actor.Human "you"

let private wireStr (s: string) : PropValue = PropValue.Wire(JStr s)

let internal turns: Turn list =
  [ { Op = TreeOp.InsertChild(nid "tm-root", metricCard "tm-m-rev" "Revenue" "£0")
      Actor = agent
      Label = "Add a Revenue metric"
      Tag = Tag.Normal }
    { Op = TreeOp.UpdateProp(nid "tm-m-rev-v", "Text", wireStr "£128,400")
      Actor = agent
      Label = "Fill in the revenue figure"
      Tag = Tag.Normal }
    { Op = TreeOp.InsertChild(nid "tm-root", metricCard "tm-m-ord" "Orders" "1,204")
      Actor = agent
      Label = "Add an Orders metric"
      Tag = Tag.Normal }
    { Op = TreeOp.InsertChild(nid "tm-root", accountsGrid)
      Actor = agent
      Label = "Add a top-accounts grid"
      Tag = Tag.Normal }
    { Op = TreeOp.UpdateProp(nid "tm-note", "Body", wireStr "Top three accounts added.")
      Actor = agent
      Label = "Update the note"
      Tag = Tag.Normal }
    { Op = TreeOp.InsertChild(nid "tm-root", metricCard "tm-m-churn" "Churn" "£128,400")
      Actor = agent
      Label = "Add a Churn metric – but paste the revenue figure in by mistake"
      Tag = Tag.Mistake }
    { Op = TreeOp.UpdateProp(nid "tm-m-churn-v", "Text", wireStr "2.1%")
      Actor = agent
      Label = "Catch it – churn is a rate, not a currency"
      Tag = Tag.Fix }
    { Op =
        TreeOp.InsertChild(
          nid "tm-root",
          Fuaran.callout
            "tm-cta"
            { Defaults.callout with
                Tone = ToneVariant.Brand
                Heading = Some(TextSource.Literal "Headline")
                Body = TextSource.Literal "Revenue up 18% QoQ, led by Stark Industries." }
        )
      Actor = agent
      Label = "Add a headline callout"
      Tag = Tag.Normal }
    { Op = TreeOp.RemoveNode(nid "tm-note")
      Actor = agent
      Label = "Drop the draft note"
      Tag = Tag.Normal }
    { Op = TreeOp.InsertChild(nid "tm-accounts", metricCard "tm-acc-4" "Umbrella" "£3,300")
      Actor = agent
      Label = "Add a fourth account"
      Tag = Tag.Normal }
    { Op = TreeOp.UpdateProp(nid "tm-m-ord-v", "Text", wireStr "1,318")
      Actor = agent
      Label = "Refresh the order count"
      Tag = Tag.Normal }
    { Op = TreeOp.UpdateProp(nid "tm-title", "Text", wireStr "Q3 sales overview")
      Actor = human
      Label = "You take over – finalise the title"
      Tag = Tag.Normal } ]

let internal turnCount = List.length turns

// ─── Genuine reconstruction: fold the shipped apply engine over a prefix ─────

let internal describeError (e: 'e) : string = sprintf "%A" e

/// Fold `Apply.apply` over `ops` starting from `start`. Ok tree, or the first
/// real `ApplyError` (surfaced verbatim – an early fork can genuinely fail
/// because the node it edits does not exist yet, and that honest error is part
/// of the demo).
let private foldApply (start: Node<unit>) (ops: TreeOp<unit> list) : Result<Node<unit>, string> =
  (Ok start, ops)
  ||> List.fold (fun acc op ->
    match acc with
    | Error _ -> acc
    | Ok tree -> Apply.apply op tree |> Result.mapError describeError)

/// The trunk frames 0..N – the tree at each turn. Precomputed once (folding 12
/// ops is trivial); the trunk never errors.
let internal trunkFrames: Node<unit> array =
  [| for n in 0..turnCount ->
       match foldApply initialTree (turns |> List.truncate n |> List.map (fun t -> t.Op)) with
       | Ok t -> t
       | Error _ -> initialTree |]

/// The fixed instant every node of the exhibit carries, so its addresses are
/// the same on every load.
let private exhibitEpoch =
  System.DateTimeOffset(2026, 1, 1, 0, 0, 0, System.TimeSpan.Zero)

let private entryOf (op: TreeOp<unit>) : StreamEntry<unit> =
  { Op = op
    Timestamp = exhibitEpoch
    PromptId = None
    ResultEnvelope = OpResultEnvelope.Success }

/// A run of ops as Core DAG nodes: each node's parent is the one before it, and
/// the first hangs off `firstParents` (none for a genesis). The address is
/// `DagOpRecord`'s own, Core's `Dag.nodeId` – the identity the fold addresses.
let private nodeChain (firstParents: string list) (steps: (Actor * TreeOp<unit>) list) : DagOpRecord<unit> list =
  ((firstParents, []), steps)
  ||> List.fold (fun (parents, acc) (actor, op) ->
    let r = DagOpRecord.ofEntry "tm" parents actor (entryOf op)
    [ r.Hash ], r :: acc)
  |> snd
  |> List.rev

/// The trunk's turns as nodes; `trunkNodes[i]` is turn i+1.
let private trunkNodes: DagOpRecord<unit> list =
  nodeChain [] (turns |> List.map (fun t -> t.Actor, t.Op))

let private initialHash = Fuaran.Core.Hash.sha256Hex (CJson.encodeNode initialTree)

let private shortHash (h: string) : string =
  if h.Length <= 12 then h else h.Substring(0, 12)

// ─── Fork branches – canned alternative op-sets off the trunk ────────────────

type internal Branch =
  { Id: string
    Name: string
    Blurb: string
    Ops: TreeOp<unit> list }

let internal branches: Branch list =
  [ { Id = "region"
      Name = "Split by region"
      Blurb = "Break the accounts grid out by geography."
      // 0.4.0: these two were InsertChild(..., 0, ...) — each PREPENDED, so the
      // rendered pair read EMEA, APAC ahead of the three accounts. InsertChild
      // now appends, and the old placement is NOT recoverable with a static
      // ReorderChildren: a branch forks from trunkFrames[k], so tm-accounts holds
      // three children or four depending where the scrubber is, and an exact
      // permutation would be rejected as OrderingMismatch at most fork points.
      // The pair is therefore appended, with the source order swapped so their
      // relative order (EMEA before APAC) survives.
      Ops =
        [ TreeOp.InsertChild(nid "tm-accounts", metricCard "tm-br-emea" "EMEA" "£5,900")
          TreeOp.InsertChild(nid "tm-accounts", metricCard "tm-br-apac" "APAC" "£4,200")
          // The branch recounts the orders and reaches the figure the trunk's
          // turn 11 records: the SAME edit on both sides, which a merge applies
          // once (the agreement rule) rather than refusing as a clash.
          TreeOp.UpdateProp(nid "tm-m-ord-v", "Text", wireStr "1,318")
          TreeOp.UpdateProp(nid "tm-title", "Text", wireStr "Q3 sales by region") ] }
    { Id = "exec"
      Name = "Executive summary"
      Blurb = "Strip it to a single headline number."
      Ops =
        [ TreeOp.RemoveNode(nid "tm-accounts")
          TreeOp.RemoveNode(nid "tm-m-ord")
          TreeOp.UpdateProp(nid "tm-title", "Text", wireStr "Q3 – executive summary")
          // 0.4.0: was InsertChild(..., 0, ...) — the headline callout used to be
          // prepended. Same fork-point argument as above; it now appends.
          TreeOp.InsertChild(
            nid "tm-root",
            Fuaran.callout
              "tm-br-one"
              { Defaults.callout with
                  Tone = ToneVariant.Brand
                  Heading = Some(TextSource.Literal "One number")
                  Body = TextSource.Literal "Revenue £128,400 – up 18% QoQ." }
          ) ] } ]

// ─── Branch reconstruction + the merge (the shipped policy over Core's fold) ──

let internal tryBranch (bid: string) : Branch option =
  branches |> List.tryFind (fun b -> b.Id = bid)

/// The tree a branch reaches when forked at trunk frame `k`: the trunk prefix,
/// then the branch's op-set, through the apply engine. `Error` is a real typed
/// `ApplyError` (the branch edits a node that frame does not hold yet).
let internal branchTree (b: Branch) (k: int) : Result<Node<unit>, string> = foldApply trunkFrames[k] b.Ops

/// What the trunk did after frame `k`: the merge's first delta.
let private trunkDelta (k: int) : TreeOp<unit> list =
  turns |> List.skip k |> List.map (fun t -> t.Op)

/// Merge a fork back into the trunk HEAD as a derivation. The base is the frame
/// the branch forked from; the deltas are the trunk's later turns and the
/// branch's ops. Outer `Error` = the fork itself failed to apply; inner `Ok` =
/// the derived tree, its script and the agreements and interleaves the fold
/// admitted; inner `Error` = a typed `ReconcileFault`, a refusal naming each
/// pair's address, shape and class.
let internal mergeBranch (b: Branch) (k: int) : Result<Result<Reconciled<unit>, ReconcileFault<unit>>, string> =
  branchTree b k
  |> Result.map (fun _ -> DagReconcile.reconcile trunkFrames[k] (trunkDelta k) b.Ops)

/// The same merge with `side` named: the pairs the policy refuses are admitted
/// and that side's op is re-asserted as the tail of the merge's script. `A` is
/// the trunk, `B` the branch.
let internal mergeBranchResolved
  (side: ReconcileSide)
  (b: Branch)
  (k: int)
  : Result<Result<Reconciled<unit>, ReconcileFault<unit>>, string> =
  branchTree b k
  |> Result.map (fun _ -> DagReconcile.reconcileWith (Some side) trunkFrames[k] (trunkDelta k) b.Ops)

/// The address of the merge node of a fork and its derivation: a node of the
/// trunk head and the branch head whose op is the script, addressed by Core's
/// `Dag.nodeId` (the node `DagMerge.mergeDag` mints).
let internal mergeNodeId (b: Branch) (k: int) (r: Reconciled<unit>) : string =
  let parents =
    if k = 0 then
      []
    else
      [ (List.item (k - 1) trunkNodes).Hash ]

  let branchHead =
    nodeChain parents (b.Ops |> List.map (fun op -> Actor.Agent("branch", "1", b.Id), op))
    |> List.last

  (DagOpRecord.ofEntry
    "tm"
    [ (List.last trunkNodes).Hash; branchHead.Hash ]
    MergeAttribution.anonymous.Actor
    (entryOf (TreeOp.Batch r.Script)))
    .Hash

let internal conflictIds (refusals: Refusal<unit> list) : string list =
  refusals |> List.map (fun r -> r.Conflict.Address) |> List.distinct

// ─── View helpers ────────────────────────────────────────────────────────────

let private actorLabel (a: Actor) : string =
  match a with
  | Actor.Human _ -> "you"
  | Actor.Agent _ -> "agent"

let private actorClass (a: Actor) : string =
  match a with
  | Actor.Human _ -> "tm-actor tm-actor-human"
  | Actor.Agent _ -> "tm-actor tm-actor-agent"

let private tagBadge (tag: Tag) : ReactElement =
  match tag with
  | Tag.Normal -> Html.none
  | Tag.Mistake -> Html.span [ prop.className "tm-tag tm-tag-mistake"; prop.text "mistake" ]
  | Tag.Fix -> Html.span [ prop.className "tm-tag tm-tag-fix"; prop.text "self-corrected" ]

// ─── The page ────────────────────────────────────────────────────────────────

/// What the merge panel shows for the active fork – a clean auto-compose, the
/// real conflicts, or the tree the lenient resolution settled. Kept distinct so
/// the panel never labels a resolved merge as an auto-composed one.
[<RequireQualifiedAccess>]
type private MergeView =
  | Composed of Reconciled<unit> * nodeId: string
  | Conflicts of Refusal<unit> list * unsettled: string option
  | Resolved of Reconciled<unit> * ReconcileSide * nodeId: string

[<ReactComponent>]
let private TimeMachineView () : ReactElement =
  // `turn` is the trunk scrubber position, 0..N. `branch` names the active
  // fork (branch id + the turn it forked from); None is the trunk view.
  let turn, setTurn = React.useState 0
  let branch, setBranch = React.useState (None: (string * int) option)
  // The merge outcome for the active fork, once asked for. Cleared whenever the
  // fork changes.
  let merge, setMerge = React.useState (None: MergeView option)

  let scrubTo (n: int) : unit =
    setMerge None
    setBranch None
    setTurn n

  let forkAt (bid: string) (k: int) : unit =
    setMerge None
    setBranch (Some(bid, k))

  let returnToTrunk () : unit =
    setMerge None
    setBranch None

  let activeBranch: (Branch * int) option =
    branch
    |> Option.bind (fun (bid, k) -> tryBranch bid |> Option.map (fun b -> b, k))

  // The tree currently on stage: a fork's reconstruction, or the trunk frame.
  let stageResult: Result<Node<unit>, string> =
    match activeBranch with
    | None -> Ok trunkFrames[turn]
    | Some(b, k) -> branchTree b k

  let applyError (err: string) : ReactElement =
    Html.div
      [ prop.className "tm-apply-error"
        prop.children
          [ Html.strong [ prop.text "The apply engine refused this branch here." ]
            Html.p
              [ prop.text
                  "Forking this early hits a real typed ApplyError – the branch edits a node that this frame doesn't have yet. Scrub further along the trunk, then fork." ]
            Html.code [ prop.className "tm-apply-error-code"; prop.text err ] ] ]

  let panel (title: string) (body: ReactElement) : ReactElement =
    Html.div
      [ prop.className "tm-split-panel"
        prop.children [ Html.h4 [ prop.className "tm-split-title"; prop.text title ]; body ] ]

  // How a derived merge reads: what the fold admitted and the node it records.
  let derivation (r: Reconciled<unit>) (nodeId: string) : ReactElement =
    Html.p
      [ prop.className "tm-derivation"
        prop.text (
          sprintf
            "Derived by the fold: %d agreed edit(s) applied once, %d parent(s) interleaved by the child-order rule, %d op(s) re-asserted. The merge node %s records a script of %d op(s); the tree is derived, never stored."
            r.Agreements
            r.Interleaves
            r.Reasserted
            (shortHash nodeId)
            (List.length r.Script)
        ) ]

  let sideName (side: ReconcileSide) : string =
    match side with
    | ReconcileSide.A -> "the trunk's"
    | ReconcileSide.B -> "the branch's"

  // The merge outcome panel (only once "Merge into the trunk" has been pressed).
  let mergePanel: ReactElement list =
    match merge with
    | None -> []
    | Some(MergeView.Composed(r, nodeId)) ->
      [ panel "merged · derived by the fold" (Html.div [ derivation r nodeId; Exhibit.renderStatic r.Tree ]) ]
    | Some(MergeView.Resolved(r, side, nodeId)) ->
      [ panel
          (sprintf "merged · refusals settled to %s side" (sideName side))
          (Html.div [ derivation r nodeId; Exhibit.renderStatic r.Tree ]) ]
    | Some(MergeView.Conflicts(cs, unsettled)) ->
      let resolve (side: ReconcileSide) =
        match activeBranch with
        | Some(b, k) ->
          match mergeBranchResolved side b k with
          | Ok(Ok r) -> setMerge (Some(MergeView.Resolved(r, side, mergeNodeId b k r)))
          | Ok(Error f) -> setMerge (Some(MergeView.Conflicts(cs, Some(describeError f))))
          | Error e -> setMerge (Some(MergeView.Conflicts(cs, Some e)))
        | None -> ()

      [ panel
          "merge · refused"
          (Html.div
            [ prop.className "tm-conflicts"
              prop.children
                [ Html.p
                    [ prop.text
                        "Core's conflict report named pairs the policy does not admit: both sides changed the same cell after the fork. The merge refuses with each pair's address, shape and class rather than picking silently." ]
                  (match unsettled with
                   | Some why ->
                     Html.p
                       [ prop.className "tm-apply-error"
                         prop.text (
                           "Naming a side cannot settle this one: a removal beside an edit of the removed node folds in neither order, so the fold refuses it again. "
                           + why
                         ) ]
                   | None -> Html.none)
                  Html.ul
                    [ prop.children
                        [ for c in cs ->
                            Html.li
                              [ Html.code [ prop.text c.Conflict.Address ]
                                Html.text (sprintf " · %A · %A" c.Conflict.Shape c.Class) ] ] ]
                  Html.button
                    [ prop.className "tm-trunk-btn"
                      prop.text "Settle to the trunk's side"
                      prop.onClick (fun _ -> resolve ReconcileSide.A) ]
                  Html.button
                    [ prop.className "tm-trunk-btn"
                      prop.text "Settle to the branch's side"
                      prop.onClick (fun _ -> resolve ReconcileSide.B) ] ] ]) ]

  let stage =
    match activeBranch, stageResult with
    | None, Ok tree -> Html.div [ prop.className "tm-stage"; prop.children [ Exhibit.renderStatic tree ] ]
    | None, Error err -> Html.div [ prop.className "tm-stage"; prop.children [ applyError err ] ]
    | Some(b, _), forkResult ->
      // A fork is staged BESIDE the trunk's head – two live trees, side by side –
      // and the merge result joins them as a third once asked for.
      let forkBody =
        match forkResult with
        | Ok tree -> Exhibit.renderStatic tree
        | Error err -> applyError err

      Html.div
        [ prop.className "tm-stage tm-split"
          prop.children (
            [ panel (sprintf "trunk · head (turn %d)" turnCount) (Exhibit.renderStatic trunkFrames[turnCount])
              panel ("branch · " + b.Name) forkBody ]
            @ mergePanel
          ) ]

  // Scrubber + per-turn markers (each marker doubles as a jump target).
  let scrubber =
    Html.div
      [ prop.className "tm-scrub-block"
        prop.children
          [ Html.input
              [ prop.className "tm-scrubber"
                prop.type' "range"
                prop.min 0
                prop.max turnCount
                prop.value turn
                prop.onChange (fun (v: string) -> scrubTo (int v)) ]
            Html.div
              [ prop.className "tm-marks"
                prop.children
                  [ for i in 0..turnCount ->
                      let markClass =
                        let sel = branch.IsNone && i = turn

                        let tagCls =
                          if i = 0 then
                            ""
                          else
                            match (List.item (i - 1) turns).Tag with
                            | Tag.Mistake -> " tm-mark-mistake"
                            | Tag.Fix -> " tm-mark-fix"
                            | Tag.Normal -> ""

                        (if sel then "tm-mark tm-mark-on" else "tm-mark") + tagCls

                      Html.button
                        [ prop.className markClass
                          prop.title (
                            if i = 0 then
                              "initial tree"
                            else
                              (List.item (i - 1) turns).Label
                          )
                          prop.text (string i)
                          prop.onClick (fun _ -> scrubTo i) ] ] ] ] ]

  // The op inspector for the selected trunk turn (genesis at turn 0).
  let inspector =
    if turn = 0 then
      Html.div
        [ prop.className "tm-inspect"
          prop.children
            [ Html.div
                [ prop.className "tm-inspect-head"
                  prop.children
                    [ Html.span [ prop.className "tm-seq"; prop.text "turn 0" ]
                      Html.span [ prop.className "tm-inspect-label"; prop.text "initial tree (genesis)" ] ] ]
              Html.div
                [ prop.className "tm-hash-row"
                  prop.children
                    [ Html.span [ prop.className "tm-hash-key"; prop.text "content hash" ]
                      Html.code [ prop.className "tm-hash"; prop.text (shortHash initialHash) ] ] ] ] ]
    else
      let t = List.item (turn - 1) turns
      let node = List.item (turn - 1) trunkNodes
      let thisHash = node.Hash
      let prevHash = List.tryHead node.Parents |> Option.defaultValue "genesis"

      Html.div
        [ prop.className "tm-inspect"
          prop.children
            [ Html.div
                [ prop.className "tm-inspect-head"
                  prop.children
                    [ Html.span [ prop.className "tm-seq"; prop.text (sprintf "turn %d" turn) ]
                      Html.span [ prop.className (actorClass t.Actor); prop.text (actorLabel t.Actor) ]
                      Html.span [ prop.className "tm-inspect-label"; prop.text t.Label ]
                      tagBadge t.Tag ] ]
              Html.div
                [ prop.className "tm-hash-row"
                  prop.children
                    [ Html.span [ prop.className "tm-hash-key"; prop.text "node id" ]
                      Html.code [ prop.className "tm-hash"; prop.text (shortHash thisHash) ]
                      Html.span [ prop.className "tm-hash-link"; prop.text ("← " + shortHash prevHash) ] ] ]
              Html.pre
                [ prop.className "tm-op-json"
                  prop.children [ Html.code [ prop.text (CJson.encodeOp t.Op) ] ] ] ] ]

  // Fork controls – offered at the current frame; branch view when active.
  let forkControls =
    match activeBranch with
    | Some(b, k) ->
      let canMerge = merge.IsNone && (stageResult |> Result.isOk)

      Html.div
        [ prop.className "tm-fork tm-fork-active"
          prop.children
            [ Html.span
                [ prop.className "tm-branch-ribbon"
                  prop.text (sprintf "branch · %s · forked from turn %d" b.Name k) ]
              Html.button
                [ prop.className "tm-fork-btn"
                  prop.title "Merge by fold: base = the fork frame, deltas = the trunk since then and this branch"
                  prop.text "Merge into the trunk"
                  prop.disabled (not canMerge)
                  prop.onClick (fun _ ->
                    match mergeBranch b k with
                    | Ok(Ok r) -> setMerge (Some(MergeView.Composed(r, mergeNodeId b k r)))
                    | Ok(Error(ReconcileFault.Refused cs)) -> setMerge (Some(MergeView.Conflicts(cs, None)))
                    | _ -> ()) ]
              Html.button
                [ prop.className "tm-trunk-btn"
                  prop.text "← Return to the trunk"
                  prop.onClick (fun _ -> returnToTrunk ()) ] ] ]
    | None ->
      Html.div
        [ prop.className "tm-fork"
          prop.children
            [ Html.span [ prop.className "tm-fork-label"; prop.text (sprintf "Fork from turn %d" turn) ]
              Html.div
                [ prop.className "tm-fork-btns"
                  prop.children
                    [ for b in branches ->
                        Html.button
                          [ prop.className "tm-fork-btn"
                            prop.title b.Blurb
                            prop.text b.Name
                            prop.onClick (fun _ -> forkAt b.Id turn) ] ] ] ] ]

  let honesty =
    Exhibit.honesty
      "Each frame is replayed, not recorded"
      [ Html.li
          [ prop.text
              "Dragging the scrubber folds the shipped apply engine over the op prefix – the tree at turn n is genuinely reconstructed from the initial tree plus the first n operations, never a stored snapshot or a video frame." ]
        Html.li
          [ prop.text
              "The inspector shows the real canonical encoding of each operation and its real Core node identity: every address is sha256 over the node's parents, its author and the op's envelope. Change one op and every address downstream changes." ]
        Html.li
          [ prop.text
              "Forking replays the trunk up to the chosen frame, then applies an alternative op-set on top – a genuinely divergent app that shares the trunk's history and hash at the branch point. Fork too early and the apply engine returns a real typed error, shown as-is." ]
        Html.li
          [ prop.text
              "Merging is a fold over Core's conflict report, run in the browser by the same reconciliation policy a server host uses, with the fork frame as the base. An edit both sides made identically is applied once (the agreement rule); appends to one parent are interleaved by a declared child-order rule; a cell both sides rewrote is refused with its address, shape and class, never silently picked. Every node shown carries its Core address. Fork from the head and the merge is clean by construction; fork earlier and the trunk's later edits meet the branch's." ]
        Html.li
          [ prop.children
              [ Html.text
                  "The twelve-turn arc is a bundled recording – no key, no server. The same value-not-code property runs through the "
                Html.a [ prop.href "#/pillar/value"; prop.text "app-is-a-value" ]
                Html.text " story across the site." ] ] ]

  Exhibit.frame
    "tm"
    "The Time Machine"
    (Exhibit.lede
      "Scrub through an app's entire life like video – then fork it from any frame. Because the app is a value, its whole history replays on demand.")
    [ stage
      scrubber
      Html.div
        [ prop.className "tm-panels"
          prop.children
            [ Html.div [ prop.className "tm-panel-inspect"; prop.children [ inspector ] ]
              Html.div [ prop.className "tm-panel-fork"; prop.children [ forkControls ] ] ] ] ]
    honesty

let page: ReactElement = TimeMachineView()
