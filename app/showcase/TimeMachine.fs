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
//  shown BESIDE the trunk's head, and "Merge into the trunk" folds it back with
//  the SHIPPED 3-way merge engine (`TreeMerge.merge3Way` – the same function
//  the server host runs, Fable-portable in the browser): ancestor = the frame
//  the branch forked from, ours = everything the trunk did after it, theirs =
//  the branch. A clean auto-compose lands as one tree; a real `MergeConflict`
//  names the contended cell, and the lenient resolution falls back to the
//  ancestor's value.
//
//  The inspector shows, per turn, the REAL canonical op encoding
//  (`CanonicalJson.encodeOp`) and a REAL content-addressed hash chain
//  (`prev ++ encodeOp ++ seq ++ actor`, the OpRecord rule minus the timestamp so
//  the exhibit is deterministic). No API key, no server – the op-stream is
//  bundled, the reliability floor the whole site rides.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

module Apply = Fuaran.UI.Ops.Apply
module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

let private sha256Hex (s: string) : string = Fuaran.UI.Hashing.sha256Hex s
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
type private Tag =
  | Normal
  | Mistake
  | Fix

type private Turn =
  { Op: TreeOp<unit>
    Actor: Actor
    Label: string
    Tag: Tag }

let private agent = Actor.Agent("assistant", "1", "author")
let private human = Actor.Human "you"

let private wireStr (s: string) : PropValue = PropValue.Wire(JStr s)

let private turns: Turn list =
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

let private turnCount = List.length turns

// ─── Genuine reconstruction: fold the shipped apply engine over a prefix ─────

let private describeError (e: 'e) : string = sprintf "%A" e

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
let private trunkFrames: Node<unit> array =
  [| for n in 0..turnCount ->
       match foldApply initialTree (turns |> List.truncate n |> List.map (fun t -> t.Op)) with
       | Ok t -> t
       | Error _ -> initialTree |]

/// A genuine content-addressed chain over the canonical op encodings, mirroring
/// the shipped OpRecord rule: Hash[n] = sha256(prev ++ encodeOp ++ seq ++
/// actor). `trunkChain[i]` is (thisHash, prevHash) for op i+1.
let private trunkChain: (string * string) list =
  turns
  |> List.mapi (fun i t -> i, t)
  |> List.fold
    (fun (prev, acc) (i, t) ->
      let payload =
        prev
        + "|"
        + CJson.encodeOp t.Op
        + "|"
        + string (i + 1)
        + "|"
        + Actor.encode t.Actor

      let h = sha256Hex payload
      (h, acc @ [ (h, prev) ]))
    (HashChain.genesisPreviousHash, [])
  |> snd

let private initialHash = sha256Hex (CJson.encodeNode initialTree)

let private shortHash (h: string) : string =
  if h.Length <= 12 then h else h.Substring(0, 12)

// ─── Fork branches – canned alternative op-sets off the trunk ────────────────

type private Branch =
  { Id: string
    Name: string
    Blurb: string
    Ops: TreeOp<unit> list }

let private branches: Branch list =
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

// ─── Branch reconstruction + the 3-way merge (the real engine) ───────────────

let private tryBranch (bid: string) : Branch option =
  branches |> List.tryFind (fun b -> b.Id = bid)

/// The tree a branch reaches when forked at trunk frame `k`: the trunk prefix,
/// then the branch's op-set, through the apply engine. `Error` is a real typed
/// `ApplyError` (the branch edits a node that frame does not hold yet).
let private branchTree (b: Branch) (k: int) : Result<Node<unit>, string> = foldApply trunkFrames[k] b.Ops

/// Fold a fork back into the trunk HEAD with the shipped 3-way merge. The common
/// ancestor is the frame the branch forked from; "ours" is what the trunk did
/// after that frame; "theirs" is the branch. Outer `Error` = the fork itself
/// failed to apply; inner `Ok` = a clean auto-compose; inner `Error` = real
/// `MergeConflict`s naming the contended cells.
let private mergeBranch (b: Branch) (k: int) : Result<Result<Node<unit>, MergeConflict list>, string> =
  branchTree b k
  |> Result.map (fun theirs -> TreeMerge.merge3Way trunkFrames[k] trunkFrames[turnCount] theirs)

/// The lenient resolution of the same merge: every conflict falls back to the
/// ancestor's value, so a tree always comes back.
let private mergeBranchLenient (b: Branch) (k: int) : Result<Node<unit>, string> =
  branchTree b k
  |> Result.map (fun theirs -> TreeMerge.merge3WayLenient trunkFrames[k] trunkFrames[turnCount] theirs)

let private conflictIds (cs: MergeConflict list) : string list =
  cs |> List.map (fun c -> c.NodeId) |> List.distinct

// ─── Headless surface – the verification gate drives these from vitest ──────
//
// Flat string projections across the Fable boundary (the Phase 710-713 pattern):
// every tree crosses as its canonical wire JSON, so the test compares bytes the
// real encoder produced and never a hand-waved shape.

/// The number of recorded turns (frames run 0..turnTotal).
let turnTotal: int = turnCount

/// Canonical JSON of trunk frame `n` – the tree the scrubber shows at turn n.
let frameJson (n: int) : string = CJson.encodeNode trunkFrames[n]

/// Frame `n` re-derived ONE step from frame n-1 through the apply engine – the
/// replay claim, checkable against `frameJson n` byte-for-byte.
let stepJson (n: int) : string =
  match Apply.apply (List.item (n - 1) turns).Op trunkFrames[n - 1] with
  | Ok t -> CJson.encodeNode t
  | Error e -> "error:" + describeError e

/// The fork branches on offer, by id.
let branchIds: string array = branches |> List.map (fun b -> b.Id) |> Array.ofList

/// `ok:<json>` for the branch's tree when forked at frame `k`, or `error:<msg>`
/// carrying the real apply error.
let forkJson (bid: string) (k: int) : string =
  match tryBranch bid with
  | None -> "error:unknown branch " + bid
  | Some b ->
    match branchTree b k with
    | Ok t -> "ok:" + CJson.encodeNode t
    | Error e -> "error:" + e

/// `merged:<json>` for a clean 3-way merge of the branch (forked at `k`) into
/// the trunk head, `conflict:<id,id,…>` naming the contended nodes, or
/// `error:<msg>` when the fork itself cannot apply.
let mergeJson (bid: string) (k: int) : string =
  match tryBranch bid with
  | None -> "error:unknown branch " + bid
  | Some b ->
    match mergeBranch b k with
    | Error e -> "error:" + e
    | Ok(Ok merged) -> "merged:" + CJson.encodeNode merged
    | Ok(Error cs) -> "conflict:" + String.concat "," (conflictIds cs)

/// `merged:<json>` for the lenient resolution (conflicts fall back to the
/// ancestor's value), or `error:<msg>`.
let mergeLenientJson (bid: string) (k: int) : string =
  match tryBranch bid with
  | None -> "error:unknown branch " + bid
  | Some b ->
    match mergeBranchLenient b k with
    | Ok merged -> "merged:" + CJson.encodeNode merged
    | Error e -> "error:" + e

// ─── View helpers ────────────────────────────────────────────────────────────

let private renderTree (n: Node<unit>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

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
  | Composed of Node<unit>
  | Conflicts of MergeConflict list
  | Resolved of Node<unit>

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

  // The merge outcome panel (only once "Merge into the trunk" has been pressed).
  let mergePanel: ReactElement list =
    match merge with
    | None -> []
    | Some(MergeView.Composed merged) -> [ panel "merged · 3-way, auto-composed" (renderTree merged) ]
    | Some(MergeView.Resolved merged) ->
      [ panel "merged · conflicts settled to the ancestor's value" (renderTree merged) ]
    | Some(MergeView.Conflicts cs) ->
      let resolve () =
        match activeBranch with
        | Some(b, k) ->
          match mergeBranchLenient b k with
          | Ok t -> setMerge (Some(MergeView.Resolved t))
          | Error _ -> ()
        | None -> ()

      [ panel
          "merge · conflicts"
          (Html.div
            [ prop.className "tm-conflicts"
              prop.children
                [ Html.p
                    [ prop.text
                        "Both sides changed the same cell after the fork. The engine returns each one as a real MergeConflict rather than picking silently." ]
                  Html.ul
                    [ prop.children
                        [ for c in cs ->
                            Html.li
                              [ Html.code [ prop.text c.NodeId ]
                                Html.text (sprintf " · %s · %A" c.Facet c.Class) ] ] ]
                  Html.button
                    [ prop.className "tm-trunk-btn"
                      prop.text "Resolve – fall back to the ancestor's value"
                      prop.onClick (fun _ -> resolve ()) ] ] ]) ]

  let stage =
    match activeBranch, stageResult with
    | None, Ok tree -> Html.div [ prop.className "tm-stage"; prop.children [ renderTree tree ] ]
    | None, Error err -> Html.div [ prop.className "tm-stage"; prop.children [ applyError err ] ]
    | Some(b, _), forkResult ->
      // A fork is staged BESIDE the trunk's head – two live trees, side by side –
      // and the merge result joins them as a third once asked for.
      let forkBody =
        match forkResult with
        | Ok tree -> renderTree tree
        | Error err -> applyError err

      Html.div
        [ prop.className "tm-stage tm-split"
          prop.children (
            [ panel (sprintf "trunk · head (turn %d)" turnCount) (renderTree trunkFrames[turnCount])
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
      let thisHash, prevHash = List.item (turn - 1) trunkChain

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
                    [ Html.span [ prop.className "tm-hash-key"; prop.text "op hash" ]
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
                  prop.title "3-way merge: ancestor = the fork frame, ours = the trunk head, theirs = this branch"
                  prop.text "Merge into the trunk (3-way)"
                  prop.disabled (not canMerge)
                  prop.onClick (fun _ ->
                    match mergeBranch b k with
                    | Ok(Ok merged) -> setMerge (Some(MergeView.Composed merged))
                    | Ok(Error cs) -> setMerge (Some(MergeView.Conflicts cs))
                    | Error _ -> ()) ]
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
    Html.div
      [ prop.className "tm-honesty"
        prop.children
          [ Html.h3 [ prop.text "Each frame is replayed, not recorded" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Dragging the scrubber folds the shipped apply engine over the op prefix – the tree at turn n is genuinely reconstructed from the initial tree plus the first n operations, never a stored snapshot or a video frame." ]
                    Html.li
                      [ prop.text
                          "The inspector shows the real canonical encoding of each operation and a real content-addressed hash chain: every hash is sha256 over the previous hash, the encoded op, its sequence number, and its author. Change one op and every hash downstream changes." ]
                    Html.li
                      [ prop.text
                          "Forking replays the trunk up to the chosen frame, then applies an alternative op-set on top – a genuinely divergent app that shares the trunk's history and hash at the branch point. Fork too early and the apply engine returns a real typed error, shown as-is." ]
                    Html.li
                      [ prop.text
                          "Merging runs the shipped structural 3-way merge in the browser – the same engine a server host uses – with the fork frame as the common ancestor. Disjoint changes compose into one tree automatically; a cell both sides rewrote comes back as a real, named conflict, never a silent pick. Fork from the head and the merge is clean by construction; fork earlier and the trunk's later edits meet the branch's." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "The twelve-turn arc is a bundled recording – no key, no server. The same value-not-code property runs through the "
                            Html.a [ prop.href "#/pillar/value"; prop.text "app-is-a-value" ]
                            Html.text " story across the site." ] ] ] ] ] ]

  Html.div
    [ prop.className "tm-page"
      prop.children
        [ Html.h1 [ prop.className "tm-title-h"; prop.text "The Time Machine" ]
          Html.p
            [ prop.className "tm-lede"
              prop.text
                "Scrub through an app's entire life like video – then fork it from any frame. Because the app is a value, its whole history replays on demand." ]
          stage
          scrubber
          Html.div
            [ prop.className "tm-panels"
              prop.children
                [ Html.div [ prop.className "tm-panel-inspect"; prop.children [ inspector ] ]
                  Html.div [ prop.className "tm-panel-fork"; prop.children [ forkControls ] ] ] ]
          honesty ] ]

let page: ReactElement = TimeMachineView()
