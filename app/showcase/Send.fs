module Fuaran.Showcase.Send

// ============================================================================
//  Send Me That App – one artefact, three projections. Pillar: "one wire, many
//  worlds".
//
//  Because the app is a tree, "render" is a choice of projection, not an
//  architecture. The same `Node<unit>` is shown three ways from one wire:
//
//   • Live – the full interactive F# render.
//   • Email – a REAL email-safe projection (this page's net-new surface): a walk
//     over the tree that emits table-based, inline-styled, no-JS HTML for the
//     Display subset; Tabs degrade to STACKED SECTIONS (every pane under its
//     label – a per-kind degradation policy, possible because the tree is
//     typed); other interactive kinds become "open live" links. HTML email is
//     the most hostile render target in computing, and it drops out of the same
//     tree.
//   • Document – the render shape a crawler / SSR host sees (class + aria
//     parity-locked to the server renderer), with a mock search-result card the
//     typed crawlable links produce.
//
//  The wire JSON in the drawer is identical across all three tabs – the whole
//  point: zero forks, one artefact.
//
//  Honest scope: the email projection runs for real, client-side, over the live
//  tree. The genuine server-side SSR + islands hydration (and the "hydration
//  x-ray") need the Giraffe/SSR host – declared, not faked here; the document tab
//  shows the parity-locked render shape a browser produces from the same tree.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── The one artefact ─────────────────────────────────────────────────────────

let private kpi (id: string) (label: string) (value: string) : Node<unit> =
  Fuaran.card
    id
    { Defaults.card with
        Heading = Some(TextSource.Literal label)
        Children = [ Fuaran.markdown (id + "-v") value ] }

let private artefact: Node<unit> =
  Fuaran.box
    "sm-root"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
      Role = BoxRole.Dashboard
      Heading = Some(TextSource.Literal "Weekly performance")
      Children =
        [ Fuaran.markdown "sm-intro" "Your Monday digest – the very same dashboard you can open live."
          Fuaran.box
            "sm-kpis"
            { Layout = LayoutMode.Flex(Orientation.Horizontal, true, Some 12)
              Role = BoxRole.Group
              Heading = None
              Children =
                [ kpi "sm-rev" "Revenue" "£128k"
                  kpi "sm-ord" "Orders" "1,318"
                  kpi "sm-mar" "Margin" "58%" ] }
          Fuaran.callout
            "sm-cta"
            { Defaults.callout with
                Tone = ToneVariant.Brand
                Heading = Some(TextSource.Literal "Headline")
                Body = TextSource.Literal "Revenue up 18% QoQ, led by Stark Industries." }
          Fuaran.markdown "sm-note" "Top account this week: **Stark Industries** at £6,750."
          Fuaran.tabs
            "sm-tabs"
            { Defaults.tabs with
                ActiveIndex = Binding.State("sm-active-tab", Some 0)
                TabHeaders =
                  Some
                    [ { Defaults.tabHeader with
                          Label = TextSource.Literal "This week" }
                      { Defaults.tabHeader with
                          Label = TextSource.Literal "Last week" }
                      { Defaults.tabHeader with
                          Label = TextSource.Literal "Notes" } ]
                Children =
                  [ Fuaran.markdown "sm-tab-week" "Revenue **£128k** (+18% QoQ) across 1,318 orders at 58% margin."
                    Fuaran.markdown "sm-tab-prev" "Last week closed at **£108k** across 1,204 orders at 55% margin."
                    Fuaran.markdown
                      "sm-tab-notes"
                      "Stark Industries renewal signed; Wayne Enterprises trial extended to Q3." ] }
          Fuaran.button
            "sm-open"
            { Defaults.button with
                Label = TextSource.Literal "Refresh data"
                OnClick = Action.Navigate "dashboard/refresh"
                Variant = ButtonVariant.Primary } ] }

let private artefactJson = CJson.encodeNode artefact

// ─── The email-safe projection (the net-new render target) ───────────────────

let private txt (ts: TextSource) : string =
  match ts with
  | TextSource.Literal s -> s
  | _ -> ""

let private esc (s: string) : string =
  s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")

// Minimal **bold** → <strong>, then escape the rest – enough for the digest copy.
let private mdInline (s: string) : string =
  let escaped = esc s
  System.Text.RegularExpressions.Regex.Replace(escaped, @"\*\*(.+?)\*\*", "<strong>$1</strong>")

let private childrenOf (n: Node<unit>) : Node<unit> list =
  match n.Kind with
  | NodeKind.Box s -> s.Children
  | _ -> []

let private isHorizontal (n: Node<unit>) : bool =
  match n.Kind with
  | NodeKind.Box s ->
    match s.Layout with
    | LayoutMode.Flex(dir, _, _) -> dir = Orientation.Horizontal
    | _ -> false
  | _ -> false

let private headingOf (n: Node<unit>) : string option =
  match n.Kind with
  | NodeKind.Box s -> s.Heading |> Option.map txt
  | _ -> None

// A KPI cell = a Box with a heading + exactly one Markdown value child.
let private asKpi (n: Node<unit>) : (string * string) option =
  match headingOf n, childrenOf n with
  | Some label, [ v ] ->
    match v.Kind with
    | NodeKind.Markdown m -> Some(label, txt m.Text)
    | _ -> None
  | _ -> None

let rec private emailNode (n: Node<unit>) : string =
  match asKpi n with
  | Some(label, value) ->
    sprintf
      "<div style=\"font:12px Arial,sans-serif;color:#7a7a86;text-transform:uppercase;letter-spacing:.4px\">%s</div><div style=\"font:700 22px Arial,sans-serif;color:#1c1c22;margin-top:2px\">%s</div>"
      (esc label)
      (esc value)
  | None ->

    match n.Kind with
    | NodeKind.Box s ->
      let head =
        match s.Heading |> Option.map txt with
        | Some h when h <> "" ->
          sprintf "<h2 style=\"font:700 20px Arial,sans-serif;color:#1c1c22;margin:0 0 10px\">%s</h2>" (esc h)
        | _ -> ""

      if isHorizontal n then
        let cells =
          s.Children
          |> List.map (fun c ->
            sprintf
              "<td valign=\"top\" style=\"padding:12px;border:1px solid #e2e2e8;border-radius:6px\">%s</td>"
              (emailNode c))
          |> String.concat "<td style=\"width:10px\"></td>"

        head
        + sprintf
            "<table cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%%;margin:8px 0\"><tr>%s</tr></table>"
            cells
      else
        head + (s.Children |> List.map emailNode |> String.concat "")
    | NodeKind.Heading h ->
      sprintf "<h2 style=\"font:700 20px Arial,sans-serif;color:#1c1c22;margin:0 0 8px\">%s</h2>" (esc (txt h.Text))
    | NodeKind.Markdown m ->
      sprintf "<p style=\"font:15px/1.5 Arial,sans-serif;color:#3a3a44;margin:8px 0\">%s</p>" (mdInline (txt m.Text))
    | NodeKind.Callout c ->
      let h =
        match c.Heading |> Option.map txt with
        | Some x when x <> "" -> sprintf "<strong style=\"color:#1c1c22\">%s</strong><br>" (esc x)
        | _ -> ""

      sprintf
        "<table cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%%;margin:10px 0\"><tr><td style=\"padding:12px 14px;background:#eef2ff;border-left:4px solid #3358d4;font:15px/1.5 Arial,sans-serif;color:#2a2a34\">%s%s</td></tr></table>"
        h
        (esc (txt c.Body))
    | NodeKind.Tabs t ->
      // Email cannot run the tab switcher, so Tabs degrade to STACKED SECTIONS:
      // every pane rendered sequentially under its tab label. Nothing hidden,
      // nothing to click – the typed tree makes the degradation a policy, not
      // a scrape. Labels come from TabHeaders (1:1 with Children, FUARAN047)
      // or fall back to the pane's own heading / a positional caption.
      let headerFor (i: int) (c: Node<unit>) : string =
        match t.TabHeaders with
        | Some hs when i < hs.Length -> txt hs.[i].Label
        | _ ->
          match headingOf c with
          | Some h -> h
          | None -> sprintf "Section %d" (i + 1)

      t.Children
      |> List.mapi (fun i c ->
        sprintf
          "<h3 style=\"font:700 15px Arial,sans-serif;color:#1c1c22;margin:14px 0 2px;padding-top:10px;border-top:1px solid #e2e2e8\">%s</h3>%s"
          (esc (headerFor i c))
          (emailNode c))
      |> String.concat ""
    | NodeKind.Button b ->
      sprintf
        "<a href=\"#/demo/send-me\" style=\"display:inline-block;margin:10px 0;padding:11px 18px;background:#3358d4;color:#fff;text-decoration:none;border-radius:6px;font:600 14px Arial,sans-serif\">▸ %s – open the live dashboard</a>"
        (esc (txt b.Label))
    | _ -> "<p style=\"font:14px Arial,sans-serif;color:#7a7a86\">▸ (interactive element – open the live dashboard)</p>"

let private emailHtml: string =
  "<div style=\"max-width:600px;margin:0 auto;background:#fff;padding:20px\">"
  + emailNode artefact
  + "</div>"

// ─── DOM read for the document view-source ───────────────────────────────────

[<Emit("(function(){var el=document.querySelector('.sm-doc-render');return el?el.innerHTML:'';})()")>]
let private docSourceHtml () : string = jsNative

// ─── The page ────────────────────────────────────────────────────────────────

[<RequireQualifiedAccess>]
type private Tab =
  | Live
  | Email
  | Document

// The live pane needs a real state channel: the exemplar's Tabs drive their
// ActiveIndex through `Binding.State`, and the renderer's write-back default
// lands the clicked index in the observable StateStore via the browser
// runtime (the TypedQuestion pattern). The empty-sources/no-runtime shape
// would render the tabs dead.
//
// Deny-by-default is deliberate here, and the page needs no policy of its own.
// The write-back is a tree-originated State write rather than a dispatched
// action, so it never meets the gate — the tabs work under a runtime that
// refuses everything. The exemplar's one gated action is the "Refresh data"
// button's `Action.Navigate "dashboard/refresh"`, and refusing it is the
// CORRECT outcome rather than a capability this page has lost: the exemplar is
// an artefact on display, not a live dashboard, and the browser runtime routes
// Navigate to `window.location.hash` — which this showcase also routes on, so
// an allowed navigation would steer the whole site to a route that does not
// exist. Do not "repair" this to `createPermissive`.
let private browserRuntime: Runtime.IFuaranRuntime = BrowserRuntime.create ()

let private renderTree (n: Node<unit>) : ReactElement =
  Render.render
    { Sources =
        { BindingResolver.empty with
            State = StateStore.snapshot () }
      Runtime = browserRuntime
      VisAdapter = VisAdapter.noOp<unit>
      Dispatch = ignore
      TelemetrySink = None
      InErrorBoundary = false
      Fragments = Render.collectFragments Map.empty n
      ExpandingFragments = Set.empty
      Scope = None
      SessionContext = Map.empty
      // No user-action record sink: this is a client-only page with no
      // durable destination, and the action log is privacy-classed, so an
      // unconfigured host must record nothing and pay nothing. `None`
      // reproduces the renderer's own default at every convenience entry
      // point. `CurrentNodeId` is renderer-owned - `render` sets it per
      // node, and only when a sink is wired - so `None` is the only
      // correct value at construction.
      ActionSink = None
      CurrentNodeId = None }
    n

[<ReactComponent>]
let private SendView () : ReactElement =
  let tab, setTab = React.useState Tab.Live
  let showWire, setShowWire = React.useState false
  let docSrc, setDocSrc = React.useState ""
  // Re-render when the StateStore moves (the tab write-back writes there).
  let _, bumpStateVersion = React.useStateWithUpdater 0

  // The effect returns the unsubscribe thunk – Feliz's (unit -> unit -> unit)
  // cleanup overload.
  let subscribeEffect: unit -> unit -> unit =
    fun () -> StateStore.subscribe (fun () -> bumpStateVersion (fun v -> v + 1))

  React.useEffect (subscribeEffect, [||])

  // When the document tab shows, read the rendered markup back for view-source.
  React.useEffect (
    (fun () ->
      if tab = Tab.Document then
        setDocSrc (docSourceHtml ())),
    [| box tab |]
  )

  let tabBtn (t: Tab) (label: string) (sub: string) : ReactElement =
    Html.button
      [ prop.className (if tab = t then "sm-tab sm-tab-on" else "sm-tab")
        prop.onClick (fun _ -> setTab t)
        prop.children
          [ Html.span [ prop.className "sm-tab-label"; prop.text label ]
            Html.span [ prop.className "sm-tab-sub"; prop.text sub ] ] ]

  let tabs =
    Html.div
      [ prop.className "sm-tabs"
        prop.children
          [ tabBtn Tab.Live "Live" "the interactive app"
            tabBtn Tab.Email "Email" "an email-safe digest"
            tabBtn Tab.Document "Document" "crawlable, no client runtime" ] ]

  let livePane =
    Html.div [ prop.className "sm-stage"; prop.children [ renderTree artefact ] ]

  let emailPane =
    Html.div
      [ prop.className "sm-inbox"
        prop.children
          [ Html.div
              [ prop.className "sm-inbox-head"
                prop.children
                  [ Html.div [ prop.className "sm-inbox-from"; prop.text "Analytics · digests@fuaran-ui.ai" ]
                    Html.div [ prop.className "sm-inbox-subj"; prop.text "Your Monday performance digest" ] ] ]
            Html.div [ prop.className "sm-email-body"; prop.dangerouslySetInnerHTML emailHtml ] ] ]

  let documentPane =
    Html.div
      [ prop.className "sm-doc"
        prop.children
          [ Html.div
              [ prop.className "sm-search-card"
                prop.children
                  [ Html.div
                      [ prop.className "sm-search-url"
                        prop.text "fuaran-ui.ai › dashboards › weekly-performance" ]
                    Html.div
                      [ prop.className "sm-search-title"
                        prop.text "Weekly performance – live dashboard" ]
                    Html.div
                      [ prop.className "sm-search-desc"
                        prop.text
                          "Your Monday digest – the very same dashboard you can open live. Revenue £128k · Orders 1,318 · Margin 58% · Revenue up 18% QoQ…" ] ] ]
            Html.div
              [ prop.className "sm-doc-render-wrap"
                prop.children
                  [ Html.span
                      [ prop.className "sm-doc-tag"
                        prop.text "what renders (static markup – no client runtime needed)" ]
                    Html.div [ prop.className "sm-doc-render"; prop.children [ renderTree artefact ] ] ] ]
            (if docSrc <> "" then
               Html.details
                 [ prop.className "sm-viewsource"
                   prop.children
                     [ Html.summary [ prop.text "View source – the class + aria a crawler and the SSR host both see" ]
                       Html.pre [ prop.className "sm-source"; prop.children [ Html.code [ prop.text docSrc ] ] ] ] ]
             else
               Html.none) ] ]

  let pane =
    match tab with
    | Tab.Live -> livePane
    | Tab.Email -> emailPane
    | Tab.Document -> documentPane

  let wireDrawer =
    Html.div
      [ prop.className "sm-wire"
        prop.children
          [ Html.button
              [ prop.className "sm-wire-toggle"
                prop.text (
                  if showWire then
                    "Hide the wire – it is identical across all three tabs"
                  else
                    "Show the wire – one artefact behind all three tabs"
                )
                prop.onClick (fun _ -> setShowWire (not showWire)) ]
            (if showWire then
               Html.pre
                 [ prop.className "wire-json"
                   prop.children [ Html.code [ prop.text artefactJson ] ] ]
             else
               Html.none) ] ]

  let honesty =
    Html.div
      [ prop.className "sm-honesty"
        prop.children
          [ Html.h3 [ prop.text "One artefact, zero forks" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "All three tabs are projections of the same wire tree – the JSON in the drawer is byte-identical across them. Because the app is data, \"render\" is a choice of target, not a rebuild." ]
                    Html.li
                      [ prop.text
                          "The email tab is a real render target: a walk over the tree emitting table-based, inline-styled, no-JS HTML for the Display subset. Tabs degrade to stacked sections (every pane visible under its label – a per-kind policy the typed tree makes possible); other interactive kinds project to open-live links. HTML email is the most hostile target there is, and it falls out of the same artefact – reusable as scheduled report digests, not just a demo." ]
                    Html.li
                      [ prop.text
                          "Honest limits: the document tab shows the render shape a crawler and the server renderer both produce (class + aria are parity-locked by the conformance corpus), but genuine server-side SSR and islands hydration – the \"hydration x-ray\" – need the SSR host and are declared here, not run in this static page." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "The dashboard that arrives in your Monday email and the one you click into are the same bytes – the "
                            Html.a [ prop.href "#/pillar/wire"; prop.text "one-wire-many-worlds" ]
                            Html.text " thesis, at the projection layer." ] ] ] ] ] ]

  Html.div
    [ prop.className "sm-page"
      prop.children
        [ Html.h1 [ prop.className "sm-title"; prop.text "Send Me That App" ]
          Html.p
            [ prop.className "sm-lede"
              prop.text
                "The same app, three ways: a crawlable document a search engine can read, an email-safe digest, and the live interactive thing – one artefact, zero forks." ]
          tabs
          pane
          wireDrawer
          honesty ] ]

let page: ReactElement = SendView()
