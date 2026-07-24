namespace Fuaran.Showcase

// ============================================================================
//  The Pattern Bank – composition by lookup, not by generation. Pillar: "the
//  machine can see the UI".
//
//  Describe the shape you want – the holes you can fill, the kind of node you
//  want to produce – and the bank returns which known patterns you can run,
//  instantly. The search is the REAL `Fuaran.Core.FunctionRegistry.findBySignature`
//  (the same engine that powers the shipped `Fuaran.UI.FastPath` package),
//  compiled to JavaScript via Fable: deterministic, total, in-memory. No model
//  call, no server, zero latency. Pick a match and it instantiates into a real
//  Fuaran tree you can render, export, or keep building on.
//
//  This is the fast path: before reaching for a model, look up a known-good
//  pattern by its structure. It is the anti-generative half of AI composition –
//  a pattern can't be hallucinated, and it resolves in microseconds.
//
//  Honest scope: the signature-search engine + the match semantics are the real
//  `Fuaran.Core` registry; the pattern set here mirrors the shipped
//  `Fuaran.UI.FastPath` seed catalogue (a consumer of the same engine can pull
//  that package directly – e.g. the fuaran-live playground). Nothing needs a
//  server.
// ============================================================================

/// The signature-search façade over the real `Fuaran.Core` registry – isolated in
/// its own module so it can `open Fuaran.Core` without `HoleDecl` colliding with
/// `Fuaran.UI.Types.HoleDecl`.
module internal PatternBankEngine =

  open Fuaran.Core

  type Pattern =
    { Id: string
      Title: string
      Summary: string
      ResultType: string
      Holes: HoleDecl list
      Build: Map<string, string> -> Fuaran.UI.Types.Node<unit> }

  type Bank =
    { Registry: FunctionRegistry
      Patterns: Map<string, Pattern> }

  type Query =
    { Provide: HoleDecl list
      Produce: string option }

  let valueHole (addr: string) (name: string) (space: ValueSpace) : HoleDecl =
    { Addr = addr
      Name = name
      Kind = ValueHole space }

  let textHole (addr: string) (name: string) : HoleDecl = valueHole addr name AnyString

  let numberHole (addr: string) (name: string) (lo: int) (hi: int) : HoleDecl = valueHole addr name (IntRange(lo, hi))

  let private sigEntryOf (h: HoleDecl) : SigEntry =
    let kindStr, space, slot, action, required =
      match h.Kind with
      | ValueHole s -> "value", Some s, None, None, true
      | SlotHole c -> "slot", None, c, None, true
      | RepeatHole s -> "repeat", Some s, None, None, false
      | ActionHole e -> "action", None, None, Some e, false

    { Addr = h.Addr
      Name = h.Name
      Kind = kindStr
      Space = space
      Slot = slot
      Action = action
      Required = required }

  let private signatureOf (name: string) (holes: HoleDecl list) : Signature =
    { Name = name
      Holes = holes |> List.map sigEntryOf
      Effect = Effect.pureDeterministic }

  let bank (patterns: Pattern list) : Bank =
    let registry =
      (FunctionRegistry.empty, patterns)
      ||> List.fold (fun r p ->
        let cap =
          Capability.create p.Id (signatureOf p.Title p.Holes) Placement.ClientDeclarative

        match FunctionRegistry.register (FunctionRegistry.entry p.ResultType cap) r with
        | Ok next -> next
        | Error _ -> r)

    { Registry = registry
      Patterns = patterns |> List.map (fun p -> p.Id, p) |> Map.ofList }

  let find (mode: MatchMode) (q: Query) (b: Bank) : Pattern list =
    let sq: SignatureQuery =
      { ResultType = q.Produce
        Available = q.Provide |> List.map sigEntryOf }

    FunctionRegistry.findBySignature mode sq b.Registry
    |> List.choose (fun (e: FunctionEntry) -> Map.tryFind e.Capability.Id b.Patterns)

  let findRunnable (q: Query) (b: Bank) : Pattern list = find Subsumes q b

  let query (provide: HoleDecl list) (produce: string option) : Query =
    { Provide = provide; Produce = produce }

  /// The names of a pattern's declared holes – for display ("needs: …").
  let holeNames (p: Pattern) : string list = p.Holes |> List.map (fun h -> h.Name)

  let tryPattern (id: string) (b: Bank) : Pattern option = Map.tryFind id b.Patterns

  let instantiate (p: Pattern) (values: Map<string, string>) : Fuaran.UI.Types.Node<unit> = p.Build values

  // ── the ComputeLayer sample: an embedded table + a real transform pipeline ──
  // Built here (Fuaran.Core open) and exposed as a ready `Binding<float>` so the
  // page's tree-building code never needs to open Fuaran.Core.
  let computedRevenue: Fuaran.UI.Types.Binding<float> =
    let table: Table =
      { Schema = [ "region", StringType; "revenue", FloatType ]
        Columns =
          [ { Name = "region"
              Type = StringType
              Cells = [ Cell.Str "North"; Cell.Str "South"; Cell.Str "East" ] }
            { Name = "revenue"
              Type = FloatType
              Cells = [ Cell.Float 4800.0; Cell.Float 9100.0; Cell.Float 3600.0 ] } ] }

    let pipeline: Transform list =
      [ GroupBy(
          [ "region" ],
          [ { Name = "revenue"
              Fn = Sum
              Of = "revenue" } ]
        ) ]

    Fuaran.UI.Types.Binding.Transform(DataSource.Embedded table, pipeline, [])


module PatternBank =

  open Feliz
  open Fuaran.UI
  open Fuaran.UI.Types
  open Fuaran.UI.Renderer
  open Fuaran.UI.OpStream.Abstractions
  open PatternBankEngine

  // ── tree helpers (the pattern builders) ──────────────────────────────────
  let private strOf (v: Map<string, string>) (addr: string) (dflt: string) : string =
    match Map.tryFind addr v with
    | Some s when s <> "" -> s
    | _ -> dflt

  let private metricNode (id: string) (label: string) (value: float) : Node<unit> =
    Fuaran.metric
      id
      { Defaults.metric with
          Label = TextSource.Literal label
          Value = Binding.Static value }

  let private computeMetricNode (id: string) (label: string) : Node<unit> =
    Fuaran.metric
      id
      { Defaults.metric with
          Label = TextSource.Literal label
          Value = computedRevenue }

  let private vbox (id: string) (role: BoxRole) (heading: TextSource option) (children: Node<unit> list) : Node<unit> =
    Fuaran.box
      id
      { Layout =
          BoxLayout.Flex
            { Direction = Vertical
              Wrap = false
              Gap = Some 12 }
        Role = role
        Heading = heading
        Children = children }

  let private hbox (id: string) (children: Node<unit> list) : Node<unit> =
    Fuaran.box
      id
      { Layout =
          BoxLayout.Flex
            { Direction = Horizontal
              Wrap = true
              Gap = Some 12 }
        Role = BoxRole.Group
        Heading = None
        Children = children }

  let private headingNode (id: string) (level: int) (text: string) : Node<unit> =
    Fuaran.heading
      id
      { Level = level
        Text = TextSource.Literal text
        Variant = HeadingVariant.Standard }

  let private calloutNode (id: string) (tone: ToneVariant) (heading: string) (body: string) : Node<unit> =
    Fuaran.callout
      id
      { Defaults.callout with
          Tone = tone
          Heading = Some(TextSource.Literal heading)
          Body = TextSource.Literal body }

  let private ctaButton (id: string) (label: string) : Node<unit> =
    Fuaran.button
      id
      { Defaults.button with
          Label = TextSource.Literal label
          OnClick = Action.Navigate "cta"
          Variant = ButtonVariant.Primary }

  // ── the seed patterns (mirror the shipped Fuaran.UI.FastPath catalogue) ──
  let private seed: Pattern list =
    [ { Id = "single-metric"
        Title = "Single metric"
        Summary = "One labelled KPI value."
        ResultType = "Metric"
        Holes =
          [ textHole "metric.label" "a label"
            numberHole "metric.value" "a number" 0 1000000 ]
        Build = fun v -> metricNode "sm" (strOf v "metric.label" "Revenue") 128000.0 }
      { Id = "metric-strip"
        Title = "Metric strip"
        Summary = "A horizontal strip of three KPIs."
        ResultType = "Box"
        Holes =
          [ textHole "m0.label" "labels"
            numberHole "m0.value" "numbers" 0 1000000
            textHole "m1.label" "labels"
            numberHole "m1.value" "numbers" 0 1000000
            textHole "m2.label" "labels"
            numberHole "m2.value" "numbers" 0 1000000 ]
        Build =
          fun _ ->
            hbox
              "strip"
              [ metricNode "s0" "Revenue" 128000.0
                metricNode "s1" "Orders" 1318.0
                metricNode "s2" "Margin %" 58.0 ] }
      { Id = "kpi-card"
        Title = "KPI card"
        Summary = "A single big value inside a titled card."
        ResultType = "Card"
        Holes = [ textHole "card.label" "a label"; textHole "card.value" "a value" ]
        Build =
          fun v ->
            Fuaran.card
              "kpi"
              { Defaults.card with
                  Heading = Some(TextSource.Literal(strOf v "card.label" "Revenue"))
                  Children = [ Fuaran.markdown "kpi-v" (strOf v "card.value" "£128k") ] } }
      { Id = "dashboard-shell"
        Title = "Dashboard shell"
        Summary = "A titled dashboard with a two-metric strip."
        ResultType = "Box"
        Holes =
          [ textHole "title" "a title"
            textHole "m0.label" "labels"
            numberHole "m0.value" "numbers" 0 1000000
            textHole "m1.label" "labels"
            numberHole "m1.value" "numbers" 0 1000000 ]
        Build =
          fun v ->
            vbox
              "dash"
              BoxRole.Dashboard
              (Some(TextSource.Literal(strOf v "title" "Q3 performance")))
              [ hbox "dash-strip" [ metricNode "d0" "Revenue" 128000.0; metricNode "d1" "Orders" 1318.0 ] ] }
      { Id = "hero"
        Title = "Hero"
        Summary = "A headline, a supporting line, and a call to action."
        ResultType = "Box"
        Holes =
          [ textHole "headline" "a headline"
            textHole "sub" "a supporting line"
            textHole "cta" "a button label" ]
        Build =
          fun v ->
            vbox
              "hero"
              BoxRole.Group
              None
              [ headingNode "hero-h" 2 (strOf v "headline" "Ship your ideas faster")
                Fuaran.markdown "hero-sub" (strOf v "sub" "The fastest way to build.")
                ctaButton "hero-cta" (strOf v "cta" "Get started") ] }
      { Id = "callout-info"
        Title = "Info callout"
        Summary = "A titled informational callout."
        ResultType = "Callout"
        Holes = [ textHole "heading" "a heading"; textHole "body" "a body" ]
        Build =
          fun v ->
            calloutNode
              "info"
              ToneVariant.Info
              (strOf v "heading" "Heads up")
              (strOf v "body" "Something worth knowing.") }
      { Id = "empty-state"
        Title = "Empty state"
        Summary = "A subdued placeholder for when there is nothing to show."
        ResultType = "Callout"
        Holes = [ textHole "message" "a message" ]
        Build =
          fun v ->
            calloutNode
              "empty"
              ToneVariant.Subdued
              "Nothing here yet"
              (strOf v "message" "Add your first item to get started.") }
      { Id = "error-state"
        Title = "Error state"
        Summary = "A critical-tone message for a failed operation."
        ResultType = "Callout"
        Holes = [ textHole "message" "a message" ]
        Build =
          fun v ->
            calloutNode "error" ToneVariant.Critical "Something went wrong" (strOf v "message" "Please try again.") }
      { Id = "feature-list"
        Title = "Feature list"
        Summary = "A three-item bulleted list."
        ResultType = "Markdown"
        Holes = [ textHole "f0" "items"; textHole "f1" "items"; textHole "f2" "items" ]
        Build = fun _ -> Fuaran.markdown "features" "- Fast\n- Portable\n- Typed" }
      { Id = "section"
        Title = "Section"
        Summary = "A heading with a body paragraph."
        ResultType = "Box"
        Holes = [ textHole "title" "a title"; textHole "body" "a body" ]
        Build =
          fun v ->
            vbox
              "section"
              BoxRole.Group
              None
              [ headingNode "sec-h" 3 (strOf v "title" "About")
                Fuaran.markdown "sec-b" (strOf v "body" "A short paragraph of copy.") ] }
      { Id = "compute-metric"
        Title = "Computed metric"
        Summary = "A KPI computed live from data by a transform pipeline – no server."
        ResultType = "Metric"
        Holes = [ textHole "metric.label" "a label"; textHole "data.source" "a data table" ]
        Build = fun v -> computeMetricNode "cm" (strOf v "metric.label" "Revenue by region") }
      { Id = "compute-dashboard"
        Title = "Computed dashboard"
        Summary = "A titled dashboard whose figure is computed live from data – no server."
        ResultType = "Box"
        Holes = [ textHole "title" "a title"; textHole "data.source" "a data table" ]
        Build =
          fun v ->
            vbox
              "cd"
              BoxRole.Dashboard
              (Some(TextSource.Literal(strOf v "title" "Revenue")))
              [ computeMetricNode "cd-m" "Revenue by region" ] } ]

  let private theBank: Bank = bank seed

  // ── the "provide" context groups → the holes they supply ─────────────────
  let private textAddrs =
    [ "metric.label"
      "m0.label"
      "m1.label"
      "m2.label"
      "card.label"
      "card.value"
      "title"
      "headline"
      "sub"
      "cta"
      "heading"
      "body"
      "message"
      "f0"
      "f1"
      "f2" ]

  let private numberAddrs = [ "metric.value"; "m0.value"; "m1.value"; "m2.value" ]

  let private groupHoles (ctx: Set<string>) =
    [ if ctx.Contains "text" then
        yield! textAddrs |> List.map (fun a -> textHole a a)
      if ctx.Contains "numbers" then
        yield! numberAddrs |> List.map (fun a -> numberHole a a 0 1000000)
      if ctx.Contains "data" then
        yield textHole "data.source" "data.source" ]

  let private renderTree (n: Node<unit>) : ReactElement =
    Render.renderWithSources BindingResolver.empty ignore n

  let private kinds =
    [ None, "Any"
      Some "Box", "Dashboard"
      Some "Metric", "Metric"
      Some "Card", "Card"
      Some "Callout", "Callout"
      Some "Markdown", "Text" ]

  let private groups =
    [ "text", "labels & copy"; "numbers", "numbers"; "data", "a data table" ]

  [<ReactComponent>]
  let private PatternBankView () : ReactElement =
    let produce, setProduce = React.useState (None: string option)
    let context, setContext = React.useState (Set.ofList [ "text"; "numbers" ])
    let selected, setSelected = React.useState (None: string option)
    let showWire, setShowWire = React.useState false

    let matches = findRunnable (query (groupHoles context) produce) theBank

    let toggleContext (g: string) : unit =
      setContext (
        if context.Contains g then
          Set.remove g context
        else
          Set.add g context
      )

    // ── the query panel ──────────────────────────────────────────────────
    let producePills =
      Html.div
        [ prop.className "pb-pills"
          prop.children
            [ for (k, label) in kinds ->
                Html.button
                  [ prop.className (if produce = k then "pb-pill pb-pill-on" else "pb-pill")
                    prop.text label
                    prop.onClick (fun _ -> setProduce k) ] ] ]

    let contextChips =
      Html.div
        [ prop.className "pb-chips"
          prop.children
            [ for (g, label) in groups ->
                Html.button
                  [ prop.className (
                      if context.Contains g then
                        "pb-chip pb-chip-on"
                      else
                        "pb-chip"
                    )
                    prop.text ((if context.Contains g then "✓ " else "") + label)
                    prop.onClick (fun _ -> toggleContext g) ] ] ]

    let queryPanel =
      Html.div
        [ prop.className "pb-query"
          prop.children
            [ Html.div
                [ prop.className "pb-q-row"
                  prop.children
                    [ Html.span [ prop.className "pb-q-label"; prop.text "I want to produce" ]
                      producePills ] ]
              Html.div
                [ prop.className "pb-q-row"
                  prop.children
                    [ Html.span [ prop.className "pb-q-label"; prop.text "and I can provide" ]
                      contextChips ] ] ] ]

    // ── the results ──────────────────────────────────────────────────────
    let resultCard (p: Pattern) : ReactElement =
      Html.button
        [ prop.className (
            if selected = Some p.Id then
              "pb-result pb-result-on"
            else
              "pb-result"
          )
          prop.onClick (fun _ -> setSelected (Some p.Id))
          prop.children
            [ Html.div [ prop.className "pb-result-title"; prop.text p.Title ]
              Html.div [ prop.className "pb-result-summary"; prop.text p.Summary ]
              Html.div
                [ prop.className "pb-result-needs"
                  prop.text ("needs " + (holeNames p |> List.distinct |> String.concat ", ")) ] ] ]

    let resultsPanel =
      Html.div
        [ prop.className "pb-results"
          prop.children
            [ Html.div
                [ prop.className "pb-results-head"
                  prop.children
                    [ Html.span
                        [ prop.className "pb-count"
                          prop.text (sprintf "%d patterns match" (List.length matches)) ]
                      Html.span [ prop.className "pb-latency"; prop.text "no model · no server · 0 ms" ] ] ]
              (if List.isEmpty matches then
                 Html.p
                   [ prop.className "pb-empty"
                     prop.text "Nothing matches – turn on more context above." ]
               else
                 Html.div
                   [ prop.className "pb-results-grid"
                     prop.children [ for p in matches -> resultCard p ] ]) ] ]

    // ── the selected pattern preview ─────────────────────────────────────
    let previewPanel =
      match selected |> Option.bind (fun id -> tryPattern id theBank) with
      | None ->
        Html.p
          [ prop.className "pb-preview-hint"
            prop.text "Pick a match to instantiate it into a real Fuaran app." ]
      | Some p ->
        let tree = instantiate p Map.empty
        let wire = CanonicalJson.encodeNode tree

        Html.div
          [ prop.className "pb-preview"
            prop.children
              [ Html.div
                  [ prop.className "pb-preview-head"
                    prop.children
                      [ Html.span [ prop.className "pb-preview-title"; prop.text ("Instantiated: " + p.Title) ]
                        Html.span
                          [ prop.className "pb-preview-note"
                            prop.text "a real Fuaran tree – render it, export it, keep building" ] ] ]
                Html.div [ prop.className "pb-preview-render"; prop.children [ renderTree tree ] ]
                Html.button
                  [ prop.className "pb-wire-toggle"
                    prop.text (if showWire then "Hide the wire" else "Show the wire")
                    prop.onClick (fun _ -> setShowWire (not showWire)) ]
                (if showWire then
                   Html.pre [ prop.className "wire-json"; prop.children [ Html.code [ prop.text wire ] ] ]
                 else
                   Html.none) ] ]

    let liveNote =
      Html.div
        [ prop.className "pb-live-note"
          prop.children
            [ Html.strong [ prop.text "The fast path. " ]
              Html.text "In the "
              Html.a
                [ prop.href "https://fuaran-ui.live"
                  prop.target "_blank"
                  prop.rel "noreferrer"
                  prop.text "fuaran-live playground" ]
              Html.text
                ", this is what happens before you reach for a model: look up a known-good pattern by its structure, get a real app instantly, then edit or prompt from there." ] ]

    let honesty =
      Html.div
        [ prop.className "pb-honesty"
          prop.children
            [ Html.h3 [ prop.text "How honest is this?" ]
              Html.ul
                [ prop.children
                    [ Html.li
                        [ prop.text
                            "The search is the real Fuaran.Core signature-search engine (findBySignature), compiled to JavaScript via Fable – the same engine that powers the shipped Fuaran.UI.FastPath package. It is deterministic and total: a pattern is matched by its structure (the node kind it produces + the holes it requires), never guessed, and it resolves in-memory with no model call and no network." ]
                      Html.li
                        [ prop.text
                            "Each match instantiates into a genuine Fuaran tree, rendered here through the real renderer; the wire JSON is the canonical encoding. Two patterns are compute-bound – their value is a real transform pipeline evaluated client-side, so even a data-driven figure needs no server." ]
                      Html.li
                        [ prop.text
                            "This is composition by lookup, the anti-generative half of AI-built UI: the pattern bank is what the machine consults first, and only when it misses does generation take over. The pattern set here mirrors the public FastPath catalogue; a real consumer (the playground) pulls that package directly." ]
                      Html.li
                        [ prop.children
                            [ Html.text "The interface is structured data a machine can search – the "
                              Html.a [ prop.href "#/pillar/machine"; prop.text "machine-can-see-the-UI" ]
                              Html.text " thesis, applied to composition itself." ] ] ] ] ] ]

    Html.div
      [ prop.className "pb-page"
        prop.children
          [ Html.h1 [ prop.className "pb-title"; prop.text "The Pattern Bank" ]
            Html.p
              [ prop.className "pb-lede"
                prop.text
                  "Describe the shape you want – the bank finds a runnable pattern instantly. No model call, no server, zero latency. Composition by lookup, not by generation." ]
            queryPanel
            resultsPanel
            previewPanel
            liveNote
            honesty ] ]

  let page: ReactElement = PatternBankView()
