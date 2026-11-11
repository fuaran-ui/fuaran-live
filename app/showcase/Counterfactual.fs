module Fuaran.Showcase.Counterfactual

// ============================================================================
//  The Counterfactual Corner – try N what-ifs side by side, each a live isolated
//  branch; adopt the ones you like and the merge engine composes them. Pillar:
//  "the app is a value".
//
//  A combination demo: Mount isolation × branching DAG × structural 3-way merge.
//  From one base app, three counterfactual VARIANTS open as live, side-by-side
//  guests – each a real branch (a `TreeOp` list applied off the common base),
//  rendered in its own scope so nothing leaks between them or into your app.
//
//  "Adopt" folds a variant back into your trunk as a DERIVATION: the trunk's
//  ops and the variant's ops are the two deltas from the common base, and the
//  SHIPPED reconciliation policy (`DagReconcile`, the same function the server
//  host runs, here in the browser) reads Core's conflict report over them:
//   - Adopt two variants that touch DISJOINT cells → the fold composes them into
//     one app (both changes land, nothing to resolve).
//   - Adopt a variant that makes an edit you already adopted, byte for byte →
//     the AGREEMENT rule: the fold applies it once, and the merge says so.
//   - Adopt a variant that rewrites a cell you already changed → the merge
//     REFUSES, naming the contended address, its shape and its class; you settle
//     it, your current value winning by default (human-primacy).
//
//  Honest scope: the branch trees are authored with the shipped `Apply.apply`;
//  the isolation is real (each variant is a separate scope / op-stream off the base);
//  the merge – derivation, agreement, refusal – is the real
//  `Fuaran.UI.OpStream.Dag.Merge.DagReconcile`. Rendering the literal
//  `NodeKind.Mount` boundary needs the host guest-registry, so each guest scope is
//  resolved directly and re-id'd for the side-by-side preview (stated below).
//  Nothing here needs a server.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Dag.Merge

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

module Apply = Fuaran.UI.Ops.Apply

let private nid (s: string) : NodeId = NodeId s
let private wireStr (s: string) : PropValue = PropValue.Wire(JStr s)

// ─── The base app (the common ancestor – your app) ───────────────────────────

let private card (id: string) (label: string) (value: string) : Node<unit> =
  Fuaran.card
    id
    { Defaults.card with
        Heading = Some(TextSource.Literal label)
        Children = [ Fuaran.markdown (id + "-v") value ] }

let private baseTree: Node<unit> =
  Fuaran.box
    "cc-root"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 10)
      Role = BoxRole.Dashboard
      KeepTogether = false
      BreakBefore = false
      Heading = None
      Children =
        [ Fuaran.heading
            "cc-headline"
            { Level = 2
              Text = TextSource.Literal "Ship your ideas faster"
              Variant = HeadingVariant.Standard }
          card "cc-price" "Pro plan" "£24 / mo"
          Fuaran.markdown "cc-features" "Unlimited projects · priority support"
          Fuaran.callout
            "cc-cta"
            { Defaults.callout with
                Tone = ToneVariant.Brand
                Heading = Some(TextSource.Literal "Get started")
                Body = TextSource.Literal "Start your free trial today." } ] }

// ─── Branch authoring (real ops) ─────────────────────────────────────────────

let private applyAll (ops: TreeOp<unit> list) (tree: Node<unit>) : Node<unit> =
  (tree, ops)
  ||> List.fold (fun t op ->
    match Apply.apply op t with
    | Ok next -> next
    | Error _ -> t)

type private Variant =
  { Key: string
    Name: string
    Tagline: string
    Ops: TreeOp<unit> list }

let private variants: Variant list =
  [ { Key = "punchy"
      Name = "Punchier copy"
      Tagline = "Bolder headline + sharper feature line"
      Ops =
        [ TreeOp.UpdateProp(nid "cc-headline", "Text", wireStr "Ship 10× faster – skip the busywork")
          TreeOp.UpdateProp(nid "cc-features", "Text", wireStr "Unlimited projects · same-day support · no credit card") ] }
    { Key = "urgency"
      Name = "Add urgency"
      Tagline = "A launch-offer callout and the launch price"
      Ops =
        [ // The launch price is the same edit the value framing makes: adopt
          // both and the fold applies it once (the agreement rule).
          TreeOp.UpdateProp(nid "cc-price-v", "Text", wireStr "£19 / mo")
          TreeOp.InsertChild(
            nid "cc-root",
            Fuaran.callout
              "cc-offer"
              { Defaults.callout with
                  Tone = ToneVariant.Info
                  Heading = Some(TextSource.Literal "Launch offer")
                  Body = TextSource.Literal "20% off if you start this week." }
          ) ] }
    { Key = "value"
      Name = "Value framing"
      Tagline = "A different headline + a lower price"
      Ops =
        [ TreeOp.UpdateProp(nid "cc-headline", "Text", wireStr "The fastest way to ship")
          TreeOp.UpdateProp(nid "cc-price-v", "Text", wireStr "£19 / mo") ] } ]

let private variantTree (v: Variant) : Node<unit> = applyAll v.Ops baseTree

// Re-id a tree so a side-by-side preview never collides with the trunk's ids
// (the guest-scope stand-in – the literal Mount boundary node is elided).
let rec private reId (prefix: string) (n: Node<unit>) : Node<unit> =
  let newKind =
    match n.Kind with
    | NodeKind.Box s ->
      NodeKind.Box(
        { s with
            Children = s.Children |> List.map (reId prefix) }
      )
    | other -> other

  { n with
      Id = prefix + n.Id
      Kind = newKind }

// ─── The merge (the real engine) ─────────────────────────────────────────────

/// Adopt = derive the merge of the trunk's ops and the variant's ops from the
/// base. `Ok` = the derived tree, script and agreements; `Error` = the refused
/// pairs (address, shape, class) or another typed fault.
let private adoptMerge (trunkOps: TreeOp<unit> list) (v: Variant) : Result<Reconciled<unit>, ReconcileFault<unit>> =
  DagReconcile.reconcile baseTree trunkOps v.Ops

/// The variant's ops that are not one side of a refused pair – the disjoint
/// changes that merge onto the (already-diverged) trunk regardless of the
/// conflict. "Keep mine" derives the merge over only these (the trunk's contended
/// value survives – human-primacy); "take the variant" names the variant's side,
/// whose refused edits the merge re-asserts as script.
let private disjointOps (v: Variant) (refusals: Refusal<unit> list) : TreeOp<unit> list =
  let refused =
    refusals
    |> List.collect (fun r -> [ CJson.encodeOp r.Conflict.Left; CJson.encodeOp r.Conflict.Right ])
    |> Set.ofList

  v.Ops |> List.filter (fun op -> not (Set.contains (CJson.encodeOp op) refused))

/// The trunk's ops after an adoption: its own, then the variant's that it does not
/// already hold (an agreed edit is one op), then the merge's script.
let private extendTrunk (trunkOps: TreeOp<unit> list) (opsIn: TreeOp<unit> list) (r: Reconciled<unit>) =
  let held = trunkOps |> List.map CJson.encodeOp |> Set.ofList

  trunkOps
  @ (opsIn |> List.filter (fun op -> not (Set.contains (CJson.encodeOp op) held)))
  @ r.Script

// ─── The page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private CounterfactualView () : ReactElement =
  let trunk, setTrunk = React.useState baseTree
  // The trunk as ops from the base: the merge's first delta.
  let trunkOps, setTrunkOps = React.useState ([]: TreeOp<unit> list)
  // What the last adoption derived (agreements applied once, interleaves).
  let note, setNote = React.useState (None: string option)
  let adopted, setAdopted = React.useState (Set.empty: Set<string>)
  // A pending conflict: the variant being adopted + the contended node ids.
  let pending, setPending =
    React.useState (None: (Variant * Refusal<unit> list) option)

  let variantByKey (k: string) : Variant =
    variants |> List.find (fun v -> v.Key = k)

  let landAdoption (v: Variant) (opsIn: TreeOp<unit> list) (r: Reconciled<unit>) : unit =
    setTrunk r.Tree
    setTrunkOps (extendTrunk trunkOps opsIn r)
    setAdopted (Set.add v.Key adopted)
    setPending None

    setNote (
      Some(
        sprintf
          "Derived by the fold: %d agreed edit(s) applied once, %d parent(s) interleaved, %d op(s) re-asserted; the merge records a script of %d op(s)."
          r.Agreements
          r.Interleaves
          r.Reasserted
          (List.length r.Script)
      )
    )

  let adopt (v: Variant) : unit =
    match adoptMerge trunkOps v with
    | Ok r -> landAdoption v v.Ops r
    | Error(ReconcileFault.Refused cs) -> setPending (Some(v, cs))
    | Error _ -> ()

  let resolveKeep () : unit =
    match pending with
    | Some(v, refusals) ->
      let mine = disjointOps v refusals

      match DagReconcile.reconcile baseTree trunkOps mine with
      | Ok r -> landAdoption v mine r
      | Error _ -> ()
    | None -> ()

  let resolveTake () : unit =
    match pending with
    | Some(v, _) ->
      match DagReconcile.reconcileWith (Some ReconcileSide.B) baseTree trunkOps v.Ops with
      | Ok r -> landAdoption v v.Ops r
      | Error _ -> ()
    | None -> ()

  let resetAll () : unit =
    setTrunk baseTree
    setTrunkOps []
    setNote None
    setAdopted Set.empty
    setPending None

  // ── your app (the trunk) ────────────────────────────────────────────────
  let trunkPane =
    Html.div
      [ prop.className "cf-trunk"
        prop.children
          [ Html.div
              [ prop.className "cf-trunk-head"
                prop.children
                  [ Html.span [ prop.className "cf-trunk-title"; prop.text "Your app" ]
                    (if Set.isEmpty adopted then
                       Html.span [ prop.className "cf-trunk-sub"; prop.text "the base – nothing adopted yet" ]
                     else
                       Html.span
                         [ prop.className "cf-trunk-sub"
                           prop.text (
                             "adopted: "
                             + (adopted
                                |> Set.toList
                                |> List.map (variantByKey >> (fun v -> v.Name))
                                |> String.concat " + ")
                           ) ]) ] ]
            Html.div [ prop.className "cf-trunk-app"; prop.children [ Exhibit.renderStatic trunk ] ]
            (match note with
             | Some n -> Html.p [ prop.className "cf-derivation"; prop.text n ]
             | None -> Html.none)
            (if Set.isEmpty adopted then
               Html.none
             else
               Html.button
                 [ prop.className "cf-reset"
                   prop.text "Reset to base"
                   prop.onClick (fun _ -> resetAll ()) ]) ] ]

  // ── the resolve banner (only while a conflict is pending) ───────────────
  let resolveBanner =
    match pending with
    | None -> Html.none
    | Some(v, refusals) ->
      let ids = refusals |> List.map (fun r -> r.Conflict.Address) |> List.distinct

      Html.div
        [ prop.className "cf-conflict"
          prop.children
            [ Html.div
                [ prop.className "cf-conflict-msg"
                  prop.children
                    [ Html.span [ prop.className "cf-conflict-mark"; prop.text "merge refused" ]
                      Html.span
                        [ prop.text (
                            sprintf
                              "“%s” changes a cell you already adopted (%s). Core's conflict report names it and the policy refuses the pair; the disjoint changes derive around it once you decide."
                              v.Name
                              (String.concat ", " ids)
                          ) ] ] ]
              Html.div
                [ prop.className "cf-conflict-opts"
                  prop.children
                    [ Html.button
                        [ prop.className "cf-conflict-btn"
                          prop.text "Keep my current version"
                          prop.onClick (fun _ -> resolveKeep ()) ]
                      Html.button
                        [ prop.className "cf-conflict-btn cf-conflict-take"
                          prop.text (sprintf "Take “%s”" v.Name)
                          prop.onClick (fun _ -> resolveTake ()) ] ] ]
              Html.p
                [ prop.className "cf-primacy"
                  prop.text
                    "Human-primacy is the default: your app keeps its current value on the contended cell unless you hand it to the variant, whose edit the merge then re-asserts as script – the disjoint changes merge in either way." ] ] ]

  // ── one counterfactual guest (an isolated live branch) ──────────────────
  let guestPane (v: Variant) : ReactElement =
    let isAdopted = Set.contains v.Key adopted

    let isPending =
      (match pending with
       | Some(pv, _) -> pv.Key = v.Key
       | None -> false)

    let preview = reId (sprintf "g-%s-" v.Key) (variantTree v)

    Html.div
      [ prop.className (
          if isAdopted then
            "cf-guest cf-guest-adopted"
          else
            "cf-guest"
        )
        prop.children
          [ Html.div
              [ prop.className "cf-guest-head"
                prop.children
                  [ Html.div
                      [ prop.className "cf-guest-titles"
                        prop.children
                          [ Html.span [ prop.className "cf-guest-name"; prop.text v.Name ]
                            Html.span [ prop.className "cf-guest-tag"; prop.text v.Tagline ] ] ]
                    Html.span [ prop.className "cf-guest-branch"; prop.text ("branch: " + v.Key) ] ] ]
            Html.div
              [ prop.className "cf-guest-app"
                prop.children [ Exhibit.renderStatic preview ] ]
            Html.button
              [ prop.className "cf-adopt"
                prop.disabled (isAdopted || (pending.IsSome && not isPending))
                prop.text (
                  if isAdopted then "✓ adopted"
                  elif isPending then "resolving…"
                  else "Adopt into your app"
                )
                prop.onClick (fun _ -> adopt v) ] ] ]

  let guests =
    Html.div
      [ prop.className "cf-guests-wrap"
        prop.children
          [ Html.div
              [ prop.className "cf-guests-h"
                prop.text "Counterfactuals – three what-ifs, each a live isolated branch" ]
            Html.div
              [ prop.className "cf-guests"
                prop.children [ for v in variants -> guestPane v ] ] ] ]

  let honesty =
    Exhibit.honesty
      "How honest is this?"
      [ Html.li
          [ prop.text
              "Each counterfactual is a real branch – a list of typed edit operations applied off the common base with the shipped apply engine – rendered in its own scope so nothing leaks between the variants or into your app until you adopt it." ]
        Html.li
          [ prop.text
              "Adopt runs the shipped reconciliation policy over Core's conflict report (the same function the server host uses, compiled into this page). Adopting two variants that touch different cells composes them; an edit both make identically is an agreement and is applied once; adopting one that rewrites a cell you already changed is refused, naming the cell's address, shape and class." ]
        Html.li
          [ prop.text
              "On a refusal your app keeps its current value by default while the disjoint changes still merge around it; you can hand the contended cell to the variant instead. Adopting is incremental – your trunk's ops grow, and every later adoption is derived against them from the same base." ]
        Html.li
          [ prop.children
              [ Html.text
                  "The isolation boundary is Mount; rendering the literal boundary node needs the host guest-registry, so each guest scope is resolved directly here. Exploring counterfactuals without committing is the branch-and-merge face of the "
                Html.a [ prop.href "#/pillar/value"; prop.text "app-is-a-value" ]
                Html.text " story." ] ] ]

  Exhibit.frame
    "cf"
    "The Counterfactual Corner"
    (Exhibit.lede
      "Ask “what if?” and parallel universes of your app open side by side – each a live, isolated branch. Adopt the ones you like and a real merge, derived as a fold, composes them; when two collide, you decide, with your own version winning by default.")
    [ trunkPane; resolveBanner; guests ]
    honesty

let page: ReactElement = CounterfactualView()
