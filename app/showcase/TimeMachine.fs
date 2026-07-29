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
//  divergent tree that shares the trunk's hash at the fork point.
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

[<ReactComponent>]
let private TimeMachineView () : ReactElement =
  // `turn` is the trunk scrubber position, 0..N. `branch` names the active
  // fork (branch id + the turn it forked from); None is the trunk view.
  let turn, setTurn = React.useState 0
  let branch, setBranch = React.useState (None: (string * int) option)

  let scrubTo (n: int) : unit =
    setBranch None
    setTurn n

  // The tree currently on stage: a fork's reconstruction, or the trunk frame.
  let stageResult: Result<Node<unit>, string> =
    match branch with
    | None -> Ok trunkFrames[turn]
    | Some(bid, k) ->
      match branches |> List.tryFind (fun b -> b.Id = bid) with
      | Some b -> foldApply trunkFrames[k] b.Ops
      | None -> Ok trunkFrames[turn]

  let stage =
    Html.div
      [ prop.className "tm-stage"
        prop.children
          [ match stageResult with
            | Ok tree -> renderTree tree
            | Error err ->
              Html.div
                [ prop.className "tm-apply-error"
                  prop.children
                    [ Html.strong [ prop.text "The apply engine refused this branch here." ]
                      Html.p
                        [ prop.text
                            "Forking this early hits a real typed ApplyError – the branch edits a node that this frame doesn't have yet. Scrub further along the trunk, then fork." ]
                      Html.code [ prop.className "tm-apply-error-code"; prop.text err ] ] ] ] ]

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
    match branch with
    | Some(bid, k) ->
      let bName =
        branches
        |> List.tryFind (fun b -> b.Id = bid)
        |> Option.map (fun b -> b.Name)
        |> Option.defaultValue bid

      Html.div
        [ prop.className "tm-fork tm-fork-active"
          prop.children
            [ Html.span
                [ prop.className "tm-branch-ribbon"
                  prop.text (sprintf "branch · %s · forked from turn %d" bName k) ]
              Html.button
                [ prop.className "tm-trunk-btn"
                  prop.text "← Return to the trunk"
                  prop.onClick (fun _ -> setBranch None) ] ] ]
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
                            prop.onClick (fun _ -> setBranch (Some(b.Id, turn))) ] ] ] ] ]

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
