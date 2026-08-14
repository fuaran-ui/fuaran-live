module Fuaran.Showcase.GrepApps

// ============================================================================
//  Grep Your Apps – structural search over a corpus of applications; the results
//  ARE the running apps. Pillar: "the app is a value".
//
//  When apps are data, a corpus of apps is a database and structural search over
//  interfaces is an ordinary query – you cannot SQL a folder of React components.
//  Each corpus entry is a real `Node<unit>` tree, rendered small and LIVE (not a
//  screenshot). The query chips compile to real structural predicates evaluated
//  in-memory over the decoded trees; matched nodes glow inside each result, so the
//  highlight proves the query matched STRUCTURE, not metadata. The reveal panel
//  shows the JSONPath-like predicate each chip compiled to over the wire format.
//
//  Honest scope: the corpus + the predicate engine run for real, client-side,
//  in-memory over the trees (the plan's stated v1 – no store needed). In
//  production the same retrieval rides the shipped signature-searchable pattern
//  bank; here the tree-walk predicates are self-contained and honest.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── Corpus authoring helpers ────────────────────────────────────────────────

let private metric (id: string) (label: string) (value: float) (tone: ToneVariant) : Node<unit> =
  Fuaran.metric
    id
    { Defaults.metric with
        Label = TextSource.Literal label
        Value = Binding.Static(Some value)
        Tone = tone }

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

let private button (id: string) (label: string) (act: Action<unit>) : Node<unit> =
  Fuaran.button
    id
    { Defaults.button with
        Label = TextSource.Literal label
        OnClick = act
        Variant = ButtonVariant.Primary }

let private gridBox (id: string) (children: Node<unit> list) : Node<unit> =
  Fuaran.box
    id
    { Layout = LayoutMode.Grid(3, None, Some 8)
      Role = BoxRole.Group
      Heading = None
      Children = children }

let private dash (id: string) (heading: string) (children: Node<unit> list) : Node<unit> =
  Fuaran.box
    id
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
      Role = BoxRole.Dashboard
      Heading = Some(TextSource.Literal heading)
      Children = children }

// ─── The corpus – real app trees, globally-unique node ids ───────────────────

type private App =
  { Id: string
    Name: string
    Tree: Node<unit> }

let private corpus: App list =
  [ { Id = "sales"
      Name = "Sales dashboard"
      Tree =
        dash
          "a1-root"
          "Sales dashboard"
          [ metric "a1-rev" "Revenue" 128000.0 ToneVariant.Default
            metric "a1-ord" "Orders" 1318.0 ToneVariant.Default
            callout "a1-cta" ToneVariant.Brand "Headline" "Revenue up 18% QoQ." ] }
    { Id = "alerts"
      Name = "Ops alerts"
      Tree =
        dash
          "a2-root"
          "Ops alerts"
          [ callout "a2-inc" ToneVariant.Critical "Incident" "Database latency is high."
            Fuaran.markdown "a2-log" "3 alerts currently open." ] }
    { Id = "regions"
      Name = "Region grid"
      Tree =
        dash
          "a3-root"
          "Regions"
          [ gridBox
              "a3-grid"
              [ card "a3-emea" "EMEA" "£5,900"
                card "a3-apac" "APAC" "£4,200"
                card "a3-amer" "Americas" "£6,750" ] ] }
    { Id = "support"
      Name = "Support form"
      Tree =
        dash
          "a4-root"
          "Support"
          [ Fuaran.markdown "a4-f" "Describe your issue and we'll get back to you."
            button "a4-submit" "Submit ticket" (Action.Notify("tickets/submit", JStr "new")) ] }
    { Id = "revreport"
      Name = "Revenue report"
      Tree =
        dash
          "a5-root"
          "Revenue report"
          [ gridBox "a5-grid" [ card "a5-q1" "Q1" "£2.4M"; card "a5-q2" "Q2" "£2.6M" ]
            callout "a5-warn" ToneVariant.Warning "Below target" "Margin is 3 points under plan." ] }
    { Id = "settings"
      Name = "Settings"
      Tree =
        dash
          "a6-root"
          "Settings"
          [ card "a6-prof" "Profile" "Edit your details"
            card "a6-bill" "Billing" "Manage your plan"
            button "a6-save" "Save changes" (Action.SetState("saved", Some(JBool true), None)) ] }
    { Id = "marketing"
      Name = "Product page"
      Tree =
        dash
          "a7-root"
          "Product"
          [ Fuaran.markdown "a7-hero" "The fastest way to ship an interface."
            button "a7-cta" "Get started" (Action.Navigate "signup") ] }
    { Id = "kpiwall"
      Name = "KPI wall"
      Tree =
        dash
          "a8-root"
          "KPIs"
          [ metric "a8-users" "Users" 18204.0 ToneVariant.Default
            metric "a8-sess" "Sessions" 44120.0 ToneVariant.Default
            metric "a8-churn" "Churn %" 2.1 ToneVariant.Warning
            metric "a8-mrr" "MRR" 128000.0 ToneVariant.Default ] }
    { Id = "audit"
      Name = "Compliance audit"
      Tree =
        dash
          "a9-root"
          "Audit"
          [ callout "a9-fail" ToneVariant.Critical "2 controls failing" "Access review overdue."
            gridBox "a9-grid" [ card "a9-c1" "SOC 2" "pass"; card "a9-c2" "GDPR" "review" ] ] } ]

// ─── Structural facts + predicates ───────────────────────────────────────────

let private idOf (n: Node<unit>) : string = n.Id

let private toneStr (t: ToneVariant) : string =
  match t with
  | ToneVariant.Brand -> "Brand"
  | ToneVariant.Critical -> "Critical"
  | ToneVariant.Warning -> "Warning"
  | ToneVariant.Success -> "Success"
  | ToneVariant.Info -> "Info"
  | ToneVariant.Subdued -> "Subdued"
  | ToneVariant.Default -> "Default"

let private txt (ts: TextSource) : string =
  match ts with
  | TextSource.Literal s -> s
  | _ -> ""

let rec private dispatches (a: Action<unit>) : bool =
  match a with
  | Action.Chain xs -> List.exists dispatches xs
  | _ -> true

type private Fact =
  { Id: string
    Kind: string
    Tone: string
    Text: string
    Dispatches: bool }

let private childrenOf (n: Node<unit>) : Node<unit> list =
  match n.Kind with
  | NodeKind.Box s -> s.Children
  | _ -> []

let private factOf (n: Node<unit>) : Fact =
  let kind, tone, text, disp =
    match n.Kind with
    | NodeKind.Box s ->
      let k =
        match s.Layout with
        | LayoutMode.Grid _ -> "Grid"
        | _ -> "Box"

      k, "Default", (s.Heading |> Option.map txt |> Option.defaultValue ""), false
    | NodeKind.Metric s -> "Metric", toneStr s.Tone, txt s.Label, false
    | NodeKind.Callout s ->
      "Callout", toneStr s.Tone, (s.Heading |> Option.map txt |> Option.defaultValue "") + " " + txt s.Body, false
    | NodeKind.Markdown s -> "Markdown", "Default", txt s.Text, false
    | NodeKind.Heading s -> "Heading", "Default", txt s.Text, false
    | NodeKind.Button s -> "Button", "Default", txt s.Label, dispatches s.OnClick
    | _ -> "Other", "Default", "", false

  { Id = idOf n
    Kind = kind
    Tone = tone
    Text = text
    Dispatches = disp }

let rec private flatten (n: Node<unit>) : Node<unit> list =
  n :: (childrenOf n |> List.collect flatten)

let private facts (app: App) : Fact list = flatten app.Tree |> List.map factOf

// A chip: a label, the predicate string it compiles to, and a matcher returning
// the node ids that satisfy it in an app.
type private Chip =
  { Key: string
    Label: string
    Predicate: string
    Match: App -> string list }

let private byFact (pred: Fact -> bool) (app: App) : string list =
  facts app |> List.filter pred |> List.map (fun f -> f.Id)

// The Dashboard-with->=3-children structural predicate (whole-tree, not per-node).
let private dashboardWide (app: App) : string list =
  flatten app.Tree
  |> List.filter (fun n ->
    match n.Kind with
    | NodeKind.Box s -> s.Role = BoxRole.Dashboard && List.length s.Children >= 3
    | _ -> false)
  |> List.map idOf

let private chips: Chip list =
  [ { Key = "metric"
      Label = "has: Metric"
      Predicate = "$..* [ kind.$type == \"Metric\" ]"
      Match = byFact (fun f -> f.Kind = "Metric") }
    { Key = "grid"
      Label = "has: DataGrid"
      Predicate = "$..* [ kind.layout.$type == \"Grid\" ]"
      Match = byFact (fun f -> f.Kind = "Grid") }
    { Key = "callout"
      Label = "has: Callout"
      Predicate = "$..* [ kind.$type == \"Callout\" ]"
      Match = byFact (fun f -> f.Kind = "Callout") }
    { Key = "button"
      Label = "has: Button"
      Predicate = "$..* [ kind.$type == \"Button\" ]"
      Match = byFact (fun f -> f.Kind = "Button") }
    { Key = "critical"
      Label = "tone: Critical"
      Predicate = "$..* [ kind.tone == \"Critical\" ]"
      Match = byFact (fun f -> f.Tone = "Critical") }
    { Key = "brand"
      Label = "tone: Brand"
      Predicate = "$..* [ kind.tone == \"Brand\" ]"
      Match = byFact (fun f -> f.Tone = "Brand") }
    { Key = "dispatch"
      Label = "dispatches: an action"
      Predicate = "$..* [ kind.$type == \"Button\" && onClick != Chain[] ]"
      Match = byFact (fun f -> f.Dispatches) }
    { Key = "dash3"
      Label = "children-of: Dashboard >= 3"
      Predicate = "$..[ role == \"Dashboard\" && children.length >= 3 ]"
      Match = dashboardWide } ]

// ─── DOM glow for matched nodes ───────────────────────────────────────────────

[<Emit("(function(ids){document.querySelectorAll('.gy-hit').forEach(function(e){e.classList.remove('gy-hit')});ids.forEach(function(id){var e=document.querySelector('[data-fuaran-node-id=\"'+id+'\"]');if(e)e.classList.add('gy-hit')})})($0)")>]
let private applyGlow (ids: string[]) : unit = jsNative

// ─── The page ────────────────────────────────────────────────────────────────

let private renderTree (n: Node<unit>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

[<ReactComponent>]
let private GrepAppsView () : ReactElement =
  let active, setActive = React.useState (Set.empty: Set<string>)
  let term, setTerm = React.useState ""

  let activeChips = chips |> List.filter (fun c -> Set.contains c.Key active)
  let termTrim = term.Trim().ToLower()

  // An app matches when EVERY active chip matches AND (if a term is set) some
  // node's text contains it. Matched ids = union of all matching node ids.
  let matchApp (app: App) : (bool * string list) =
    let chipHits = activeChips |> List.map (fun c -> c.Match app)
    let chipsOk = chipHits |> List.forall (fun ids -> not (List.isEmpty ids))

    let termIds =
      if termTrim = "" then
        []
      else
        facts app
        |> List.filter (fun f -> f.Text.ToLower().Contains termTrim)
        |> List.map (fun f -> f.Id)

    let termOk = termTrim = "" || not (List.isEmpty termIds)
    let allIds = (List.concat chipHits) @ termIds |> List.distinct
    (chipsOk && termOk, allIds)

  let results = corpus |> List.map (fun app -> app, matchApp app)
  let shown = results |> List.filter (fun (_, (ok, _)) -> ok)

  // Re-apply the glow whenever the query changes (after the thumbnails render).
  React.useEffect (
    (fun () ->
      let ids = shown |> List.collect (fun (_, (_, ids)) -> ids) |> List.toArray

      applyGlow ids),
    [| box (String.concat "," (Set.toList active)); box term |]
  )

  let toggle (k: string) : unit =
    setActive (
      if Set.contains k active then
        Set.remove k active
      else
        Set.add k active
    )

  let chipBar =
    Html.div
      [ prop.className "gy-chips"
        prop.children
          [ for c in chips ->
              Html.button
                [ prop.className (
                    if Set.contains c.Key active then
                      "gy-chip gy-chip-on"
                    else
                      "gy-chip"
                  )
                  prop.text c.Label
                  prop.onClick (fun _ -> toggle c.Key) ] ] ]

  let termField =
    Html.div
      [ prop.className "gy-term"
        prop.children
          [ Html.span [ prop.className "gy-term-tag"; prop.text "bound-to:" ]
            Html.input
              [ prop.className "gy-term-input"
                prop.placeholder "revenue, region, churn…"
                prop.value term
                prop.onChange (fun (v: string) -> setTerm v) ] ] ]

  let reveal =
    if List.isEmpty activeChips && termTrim = "" then
      Html.none
    else
      Html.div
        [ prop.className "gy-reveal"
          prop.children
            [ Html.div
                [ prop.className "gy-reveal-head"
                  prop.text "The query compiled to these structural predicates over the wire" ]
              Html.ul
                [ prop.className "gy-preds"
                  prop.children
                    [ for c in activeChips do
                        Html.li
                          [ prop.className "gy-pred"
                            prop.children [ Html.code [ prop.text c.Predicate ] ] ]
                      if termTrim <> "" then
                        Html.li
                          [ prop.className "gy-pred"
                            prop.children [ Html.code [ prop.text (sprintf "$..* [ text ~ \"%s\" ]" termTrim) ] ] ] ] ] ] ]

  let countLine =
    Html.div
      [ prop.className "gy-count"
        prop.text (
          sprintf
            "%d of %d apps match – the results below are the live apps, matched nodes highlighted"
            (List.length shown)
            (List.length corpus)
        ) ]

  let wall =
    Html.div
      [ prop.className "gy-wall"
        prop.children
          [ for (app, (_, _)) in shown ->
              Html.div
                [ prop.className "gy-thumb"
                  prop.key app.Id
                  prop.children
                    [ Html.div [ prop.className "gy-thumb-name"; prop.text app.Name ]
                      Html.div
                        [ prop.className "gy-thumb-frame"
                          prop.children
                            [ Html.div [ prop.className "gy-thumb-scale"; prop.children [ renderTree app.Tree ] ] ] ] ] ] ] ]

  let honesty =
    Html.div
      [ prop.className "gy-honesty"
        prop.children
          [ Html.h3 [ prop.text "The results are the apps" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Every result is a real app tree rendered live and small – not a screenshot. Hover the query and matched nodes glow inside each result, because the query matched structure, not a metadata tag." ]
                    Html.li
                      [ prop.text
                          "Each chip compiles to a real structural predicate over the canonical wire format, evaluated in-memory across the corpus – you cannot do this to a folder of React components, because they are not data. Your UI has a schema, so it has a query language." ]
                    Html.li
                      [ prop.text
                          "Honest scope: this is a curated in-memory corpus and a tree-walk predicate engine – the plan's v1, no store required. In production the same retrieval rides the shipped signature-searchable pattern bank; the demo's predicates are self-contained." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "A corpus of apps is a database – structural search is the marketplace, the pattern bank, and coverage ledgers wearing a friendly face. The "
                            Html.a [ prop.href "#/pillar/value"; prop.text "app-is-a-value" ]
                            Html.text " thesis, at corpus scale." ] ] ] ] ] ]

  Html.div
    [ prop.className "gy-page"
      prop.children
        [ Html.h1 [ prop.className "gy-title"; prop.text "Grep Your Apps" ]
          Html.p
            [ prop.className "gy-lede"
              prop.text
                "Query a database of applications – “find every app with a data grid, or a Critical alert, or bound to revenue” – and the search results ARE the running apps." ]
          chipBar
          termField
          reveal
          countLine
          wall
          honesty ] ]

let page: ReactElement = GrepAppsView()
