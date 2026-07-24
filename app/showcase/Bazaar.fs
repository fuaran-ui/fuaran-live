module Fuaran.Showcase.Bazaar

// ============================================================================
//  The Bazaar – compose an app out of apps, each sandboxed and capability-gated.
//  Pillar: "the app is a value" × "the machine can see the UI".
//
//  Every stall is a real little Fuaran app plus a capability manifest – its
//  nutrition label, in the shipped `CapabilityTag` vocabulary. Mount a stall into
//  the workspace and its tree runs live inside its own scope. Each guest is
//  DEFAULT-DENY: a capability it wants is blocked until you grant it explicitly,
//  and a capability it never declared – an over-reach – is refused outright, with
//  the deny record shown. Over-asking (declared but unused) and over-reaching
//  (undeclared, denied) are two different things, and the gate treats them so.
//  The composed workspace is itself an app – export its wire JSON.
//
//  Honest scope: the capability manifests use the real `CapabilityTag` type; the
//  gate is the documented per-mount default-deny policy (a guest action reaches
//  the host only if its capability is granted; an empty grant is deny-all). Guest
//  trees render live in their scopes; the composition exports as real canonical
//  wire. Nothing here needs a server. (Rendering the literal `NodeKind.Mount`
//  boundary needs the host guest-registry; the demo resolves each guest's scope
//  directly and states so.)
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── Capability vocabulary (plain words over the raw tags) ───────────────────

let private plainLabel (CapabilityTag t) : string =
  match t with
  | "read:state" -> "read app state"
  | "read:filters" -> "read your filters"
  | "send:out" -> "send data to the internet"
  | "navigate" -> "navigate you away"
  | "storage" -> "use local storage"
  | other -> other

let private tagStr (CapabilityTag t) : string = t

// ─── The stalls – real little apps + declared manifests ──────────────────────

let private card (id: string) (heading: string) (value: string) : Node<unit> =
  Fuaran.card
    id
    { Defaults.card with
        Heading = Some(TextSource.Literal heading)
        Children = [ Fuaran.markdown (id + "-v") value ] }

let private callout (id: string) (tone: ToneVariant) (heading: string) (body: string) : Node<unit> =
  Fuaran.callout
    id
    { Defaults.callout with
        Tone = tone
        Heading = Some(TextSource.Literal heading)
        Body = TextSource.Literal body }

let private row (id: string) (children: Node<unit> list) : Node<unit> =
  Fuaran.box
    id
    { Layout =
        BoxLayout.Flex
          { Direction = Horizontal
            Wrap = true
            Gap = Some 8 }
      Role = BoxRole.Group
      Heading = None
      Children = children }

type private Stall =
  { Key: string
    Name: string
    Blurb: string
    Manifest: CapabilityTag list // what it declares it needs
    Requests: CapabilityTag list // what it actually tries to use
    Tree: string -> Node<unit> } // built with a per-instance id prefix

let private stalls: Stall list =
  [ { Key = "kpi"
      Name = "Revenue KPI"
      Blurb = "A single headline number."
      Manifest = [ CapabilityTag "read:state" ]
      Requests = [ CapabilityTag "read:state" ]
      Tree = fun p -> card (p + "kpi") "Revenue" "£128k" }
    { Key = "orders"
      Name = "Orders grid"
      Blurb = "A small orders table."
      Manifest = [ CapabilityTag "read:state"; CapabilityTag "read:filters" ]
      Requests = [ CapabilityTag "read:filters" ]
      Tree =
        fun p ->
          row
            (p + "grid")
            [ card (p + "o1") "Acme" "£4.1k"
              card (p + "o2") "Globex" "£2.9k"
              card (p + "o3") "Initech" "£1.5k" ] }
    { Key = "alert"
      Name = "Status banner"
      Blurb = "A live status callout."
      Manifest = [ CapabilityTag "read:state" ]
      Requests = [ CapabilityTag "read:state" ]
      Tree = fun p -> callout (p + "alert") ToneVariant.Success "All systems normal" "No incidents in the last 24h." }
    { Key = "notes"
      Name = "Scratch notes"
      Blurb = "A notepad that remembers."
      Manifest = [ CapabilityTag "read:state"; CapabilityTag "storage" ]
      Requests = [ CapabilityTag "storage" ]
      Tree = fun p -> callout (p + "notes") ToneVariant.Subdued "Notes" "Draft the Q3 plan · review with finance." }
    { Key = "bars"
      Name = "Region split"
      Blurb = "Revenue by region."
      Manifest = [ CapabilityTag "read:state" ]
      Requests = [ CapabilityTag "read:state" ]
      Tree = fun p -> row (p + "bars") [ card (p + "b1") "EMEA" "£5.9k"; card (p + "b2") "APAC" "£4.2k" ] }
    { Key = "newsletter"
      Name = "Digest sender"
      Blurb = "Reads your KPIs – and ASKS to email them (over-declares)."
      Manifest = [ CapabilityTag "read:state"; CapabilityTag "send:out" ]
      Requests = [ CapabilityTag "read:state" ]
      Tree = fun p -> card (p + "news") "Weekly digest" "ready to compose" }
    { Key = "tracker"
      Name = "Free analytics"
      Blurb = "Looks harmless – but REACHES for a capability it never declared."
      Manifest = [ CapabilityTag "read:state" ]
      Requests = [ CapabilityTag "read:state"; CapabilityTag "send:out" ]
      Tree = fun p -> card (p + "trk") "Visitors" "1,204 today" } ]

// ─── The per-mount default-deny gate ─────────────────────────────────────────

[<RequireQualifiedAccess>]
type private Verdict =
  | Allowed
  | Blocked // declared + wanted, but not yet granted (default-deny)
  | Denied // never declared – an over-reach

// The documented policy: a guest action reaches the host only if its capability
// is in the granted set; a capability the guest never declared is refused as an
// over-reach regardless of grants.
let private gate (manifest: CapabilityTag list) (granted: Set<string>) (req: CapabilityTag) : Verdict =
  if not (List.contains req manifest) then Verdict.Denied
  elif Set.contains (tagStr req) granted then Verdict.Allowed
  else Verdict.Blocked

// ─── The page ────────────────────────────────────────────────────────────────

let private renderTree (n: Node<unit>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

type private Mounted = { Instance: int; Stall: Stall }

[<ReactComponent>]
let private BazaarView () : ReactElement =
  let mounted, setMounted = React.useState ([]: Mounted list)
  let nextId, setNextId = React.useState 1
  let granted, setGranted = React.useState (Map.empty: Map<int, Set<string>>)

  let addStall (s: Stall) : unit =
    setMounted (mounted @ [ { Instance = nextId; Stall = s } ])
    setGranted (Map.add nextId Set.empty granted)
    setNextId (nextId + 1)

  let removeMount (inst: int) : unit =
    setMounted (mounted |> List.filter (fun m -> m.Instance <> inst))
    setGranted (Map.remove inst granted)

  let grant (inst: int) (t: CapabilityTag) : unit =
    let cur = Map.tryFind inst granted |> Option.defaultValue Set.empty
    setGranted (Map.add inst (Set.add (tagStr t) cur) granted)

  // The composed workspace as one wire artefact – the guests under one host box.
  let workspaceJson =
    let hostTree =
      Fuaran.box
        "bazaar-workspace"
        { Layout =
            BoxLayout.Flex
              { Direction = Vertical
                Wrap = false
                Gap = None }
          Role = BoxRole.Dashboard
          Heading = Some(TextSource.Literal "My composition")
          Children = mounted |> List.map (fun m -> m.Stall.Tree(sprintf "g%d-" m.Instance)) }

    CJson.encodeNode hostTree

  // ── stalls ──
  let stallWall =
    Html.div
      [ prop.className "bz-stalls"
        prop.children
          [ for s in stalls ->
              Html.div
                [ prop.className "bz-stall"
                  prop.children
                    [ Html.div [ prop.className "bz-stall-name"; prop.text s.Name ]
                      Html.div [ prop.className "bz-stall-blurb"; prop.text s.Blurb ]
                      Html.div
                        [ prop.className "bz-manifest"
                          prop.children
                            [ Html.span [ prop.className "bz-manifest-tag"; prop.text "asks for:" ]
                              for c in s.Manifest -> Html.span [ prop.className "bz-cap"; prop.text (plainLabel c) ] ] ]
                      Html.button
                        [ prop.className "bz-add-btn"
                          prop.text "Mount →"
                          prop.onClick (fun _ -> addStall s) ] ] ] ] ]

  // ── one mounted guest, with its live tree + gate rows ──
  let mountCard (m: Mounted) : ReactElement =
    let g = Map.tryFind m.Instance granted |> Option.defaultValue Set.empty
    let verdicts = m.Stall.Requests |> List.map (fun r -> r, gate m.Stall.Manifest g r)
    let anyDenied = verdicts |> List.exists (fun (_, v) -> v = Verdict.Denied)

    Html.div
      [ prop.className (if anyDenied then "bz-mount bz-mount-bad" else "bz-mount")
        prop.key (string m.Instance)
        prop.children
          [ Html.div
              [ prop.className "bz-mount-head"
                prop.children
                  [ Html.span [ prop.className "bz-mount-name"; prop.text m.Stall.Name ]
                    Html.button
                      [ prop.className "bz-close"
                        prop.text "✕"
                        prop.onClick (fun _ -> removeMount m.Instance) ] ] ]
            Html.div
              [ prop.className "bz-guest"
                prop.children [ renderTree (m.Stall.Tree(sprintf "g%d-" m.Instance)) ] ]
            Html.div
              [ prop.className "bz-gate"
                prop.children
                  [ for (req, v) in verdicts ->
                      Html.div
                        [ prop.className "bz-gate-row"
                          prop.children
                            [ Html.span [ prop.className "bz-gate-cap"; prop.text (plainLabel req) ]
                              (match v with
                               | Verdict.Allowed -> Html.span [ prop.className "bz-v bz-v-ok"; prop.text "✓ granted" ]
                               | Verdict.Blocked ->
                                 Html.button
                                   [ prop.className "bz-grant"
                                     prop.text "default-deny · Grant"
                                     prop.onClick (fun _ -> grant m.Instance req) ]
                               | Verdict.Denied ->
                                 Html.span
                                   [ prop.className "bz-v bz-v-bad"
                                     prop.title (sprintf "%s is not in the manifest" (tagStr req))
                                     prop.text "⛔ denied – over-reach (undeclared)" ]) ] ] ] ] ] ]

  let workspace =
    Html.div
      [ prop.className "bz-workspace"
        prop.children
          [ Html.div
              [ prop.className "bz-workspace-head"
                prop.text (sprintf "Workspace – %d mounted guest(s)" (List.length mounted)) ]
            (if List.isEmpty mounted then
               Html.div
                 [ prop.className "bz-empty"
                   prop.text "Mount a stall – each arrives as a sandboxed guest wearing its permissions on its sleeve." ]
             else
               Html.div
                 [ prop.className "bz-mounts"
                   prop.children [ for m in mounted -> mountCard m ] ]) ] ]

  let export =
    if List.isEmpty mounted then
      Html.none
    else
      Html.details
        [ prop.className "bz-export"
          prop.children
            [ Html.summary [ prop.text "Your composition is itself an app – export its wire JSON" ]
              Html.pre
                [ prop.className "wire-json"
                  prop.children [ Html.code [ prop.text workspaceJson ] ] ] ] ]

  let honesty =
    Html.div
      [ prop.className "bz-honesty"
        prop.children
          [ Html.h3 [ prop.text "An app composed out of apps" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Each stall is a real little Fuaran app that runs live in its own scope, plus a capability manifest in the shipped tag vocabulary – its permissions worn on its sleeve, before you mount it." ]
                    Html.li
                      [ prop.text
                          "Every guest is default-deny: a capability it declared is blocked until you grant it explicitly, and you watch exactly that one channel open. A capability it never declared is refused outright – over-asking and over-reaching are different, and the gate treats them so." ]
                    Html.li
                      [ prop.text
                          "The composed workspace is itself an app – export its wire JSON and it is a value like any other. Curated stalls today; a public submission store is the signing-and-trust productisation step, not a claim this demo makes." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "Discover by structure, install by mounting, trust by capability, ship by export – an app marketplace falling out of the "
                            Html.a [ prop.href "#/pillar/machine"; prop.text "machine-can-see-the-UI" ]
                            Html.text " substrate. No server; a composition is a value." ] ] ] ] ] ]

  Html.div
    [ prop.className "bz-page"
      prop.children
        [ Html.h1 [ prop.className "bz-title"; prop.text "The Bazaar" ]
          Html.p
            [ prop.className "bz-lede"
              prop.text
                "Browse a marketplace of apps, mount one into your workspace, and it runs there – sandboxed, capability-gated, live. You just composed an application out of applications." ]
          Html.h3 [ prop.className "bz-section"; prop.text "The stalls" ]
          stallWall
          workspace
          export
          honesty ] ]

let page: ReactElement = BazaarView()
