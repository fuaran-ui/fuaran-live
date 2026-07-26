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
//  "Adopt" folds a variant back into your trunk with the SHIPPED merge engine
//  (`TreeMerge.merge3Way`, fuaran#179, made Fable-portable by fuaran#501 – the
//  same function the server host runs, here in the browser):
//   - Adopt two variants that touch DISJOINT cells → the engine auto-composes them
//     into one app (both changes land, nothing to resolve).
//   - Adopt a variant that rewrites a cell you already changed → the engine returns
//     a real `MergeConflict` naming the contended node; you resolve it, your current
//     value winning by default (human-primacy: it lives in the common ancestor).
//
//  Honest scope: the branch trees are authored with the shipped `Apply.apply`; the
//  isolation is real (each variant is a separate scope / op-stream off the base);
//  the merge – auto-compose, conflict detection, primacy default – is the real
//  `Fuaran.UI.OpStream.Dag.Merge.TreeMerge` engine. Rendering the literal
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

module Apply = Fuaran.UI.Ops.Apply

let private nid (s: string) : NodeId = NodeId s
let private nodeIdStr (NodeId s) : string = s
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
    { Layout =
        BoxLayout.Flex
          { Direction = Vertical
            Wrap = false
            Gap = Some 10 }
      Role = BoxRole.Dashboard
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

let private opTarget (op: TreeOp<unit>) : string option =
  match op with
  | TreeOp.UpdateProp(n, _, _) -> Some(nodeIdStr n)
  | TreeOp.InsertChild(_, child) -> Some(nodeIdStr child.Id)
  | TreeOp.RemoveNode n -> Some(nodeIdStr n)
  | _ -> None

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
      Tagline = "A launch-offer callout (a brand-new card)"
      Ops =
        [ TreeOp.InsertChild(
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
    | NodeKind.Layout(LayoutKind.Box s) ->
      NodeKind.Layout(
        LayoutKind.Box
          { s with
              Children = s.Children |> List.map (reId prefix) }
      )
    | other -> other

  { n with
      Id = NodeId(prefix + nodeIdStr n.Id)
      Kind = newKind }

let private renderTree (n: Node<unit>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

// ─── The merge (the real engine) ─────────────────────────────────────────────

/// Adopt = fold the variant branch into the current trunk via the shipped 3-way
/// merge (ancestor = base). `Ok` = a clean auto-compose; `Error` = real conflicts.
let private adoptMerge (trunk: Node<unit>) (v: Variant) : Result<Node<unit>, MergeConflict list> =
  TreeMerge.merge3Way baseTree trunk (variantTree v)

/// The variant's ops that do NOT target a contended node – the disjoint changes
/// that merge onto the (already-diverged) trunk cleanly regardless of the conflict.
/// Resolution operates on the trunk itself (not the base) because the trunk is the
/// evolving app: "keep mine" applies only these disjoint ops (the trunk's contended
/// value survives – human-primacy); "take the variant" applies all the variant's
/// ops, so its value wins the contended cell.
let private disjointOps (v: Variant) (conflictIds: Set<string>) : TreeOp<unit> list =
  v.Ops
  |> List.filter (fun op ->
    match opTarget op with
    | Some t -> not (conflictIds.Contains t)
    | None -> true)

// ─── The page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private CounterfactualView () : ReactElement =
  let trunk, setTrunk = React.useState baseTree
  let adopted, setAdopted = React.useState (Set.empty: Set<string>)
  // A pending conflict: the variant being adopted + the contended node ids.
  let pending, setPending = React.useState (None: (Variant * string list) option)

  let variantByKey (k: string) : Variant =
    variants |> List.find (fun v -> v.Key = k)

  let adopt (v: Variant) : unit =
    match adoptMerge trunk v with
    | Ok merged ->
      setTrunk merged
      setAdopted (Set.add v.Key adopted)
      setPending None
    | Error cs -> setPending (Some(v, cs |> List.map (fun c -> c.NodeId) |> List.distinct))

  let resolveKeep () : unit =
    match pending with
    | Some(v, ids) ->
      setTrunk (applyAll (disjointOps v (Set.ofList ids)) trunk)
      setAdopted (Set.add v.Key adopted)
      setPending None
    | None -> ()

  let resolveTake () : unit =
    match pending with
    | Some(v, _) ->
      setTrunk (applyAll v.Ops trunk)
      setAdopted (Set.add v.Key adopted)
      setPending None
    | None -> ()

  let resetAll () : unit =
    setTrunk baseTree
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
            Html.div [ prop.className "cf-trunk-app"; prop.children [ renderTree trunk ] ]
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
    | Some(v, ids) ->
      Html.div
        [ prop.className "cf-conflict"
          prop.children
            [ Html.div
                [ prop.className "cf-conflict-msg"
                  prop.children
                    [ Html.span [ prop.className "cf-conflict-mark"; prop.text "merge conflict" ]
                      Html.span
                        [ prop.text (
                            sprintf
                              "“%s” changes a cell you already adopted (%s). The disjoint changes merged; you decide this one."
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
                    "Human-primacy is the default: your app keeps its current value on the contended cell unless you hand it to the variant – the disjoint changes merge in either way." ] ] ]

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
            Html.div [ prop.className "cf-guest-app"; prop.children [ renderTree preview ] ]
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
    Html.div
      [ prop.className "cf-honesty"
        prop.children
          [ Html.h3 [ prop.text "How honest is this?" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Each counterfactual is a real branch – a list of typed edit operations applied off the common base with the shipped apply engine – rendered in its own scope so nothing leaks between the variants or into your app until you adopt it." ]
                    Html.li
                      [ prop.text
                          "Adopt runs the shipped structural three-way merge (the same engine the server host uses, compiled into this page). Adopting two variants that touch different cells auto-composes them; adopting one that rewrites a cell you already changed returns a real conflict, detected by comparing that node's canonical encoding across base, trunk, and branch." ]
                    Html.li
                      [ prop.text
                          "On a conflict your app keeps its current value by default while the disjoint changes still merge around it; you can hand the contended cell to the variant instead. Adopting is incremental – your trunk evolves, and every later adoption merges against it, not the original base." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "The isolation boundary is Mount; rendering the literal boundary node needs the host guest-registry, so each guest scope is resolved directly here. Exploring counterfactuals without committing is the branch-and-merge face of the "
                            Html.a [ prop.href "#/pillar/value"; prop.text "app-is-a-value" ]
                            Html.text " story." ] ] ] ] ] ]

  Html.div
    [ prop.className "cf-page"
      prop.children
        [ Html.h1 [ prop.className "cf-title"; prop.text "The Counterfactual Corner" ]
          Html.p
            [ prop.className "cf-lede"
              prop.text
                "Ask “what if?” and parallel universes of your app open side by side – each a live, isolated branch. Adopt the ones you like and a real structural merge folds them together; when two collide, you decide, with your own version winning by default." ]
          trunkPane
          resolveBanner
          guests
          honesty ] ]

let page: ReactElement = CounterfactualView()
