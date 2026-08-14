namespace Fuaran.Live

// ============================================================================
//  The Pattern Bank – the fast path. Composition by lookup, not by generation.
//
//  Before reaching for a model, look up a known-good pattern by its structure:
//  describe the kind of node you want + the holes you can fill, and the bank
//  returns which patterns you can run – instantly, with no key and no model call.
//  Pick one and it loads into the playground as a real Fuaran tree you can then
//  edit by prompt.
//
//  The search is the REAL `Fuaran.Core.FunctionRegistry.findBySignature` (the
//  same engine that powers the public `Fuaran.UI.FastPath` package), compiled to
//  JavaScript via Fable: deterministic, total, in-memory. No server.
// ============================================================================

/// The signature-search façade – isolated so it can `open Fuaran.Core` without
/// `HoleDecl` colliding with `Fuaran.UI.Types.HoleDecl`.
module internal PatternBankEngine =

  open Fuaran.Core

  type Pattern =
    { Id: string
      Title: string
      Summary: string
      ResultType: string
      Holes: HoleDecl list
      Build: Map<string, string> -> Fuaran.UI.Types.Node<obj> }

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

  let instantiate (p: Pattern) : Fuaran.UI.Types.Node<obj> = p.Build Map.empty

  /// The compute sample – an embedded table + a real transform pipeline, exposed
  /// as a ready `Binding<float>` so the tree-building code never opens Fuaran.Core.
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

    Fuaran.UI.Types.Binding.Transform(Fuaran.UI.Types.TransformSource.Data(DataSource.Embedded table), pipeline, None)


module PatternBank =

  open Feliz
  open Fuaran.UI
  open Fuaran.UI.Types
  open PatternBankEngine

  // ── tree helpers (the pattern builders produce Node<obj>, like Gallery) ──
  let private metric (id: string) (label: string) (value: float) (fmt: CellFormat) (tone: ToneVariant) : Node<obj> =
    Fuaran.metric
      id
      { Defaults.metric with
          Label = TextSource.Literal label
          Value = Binding.Static(Some value)
          Format = fmt
          Tone = tone }

  let private computeMetric (id: string) (label: string) : Node<obj> =
    Fuaran.metric
      id
      { Defaults.metric with
          Label = TextSource.Literal label
          Value = computedRevenue }

  let private headingNode (id: string) (level: int) (text: string) : Node<obj> =
    Fuaran.heading
      id
      { Level = level
        Text = TextSource.Literal text
        Variant = HeadingVariant.Standard }

  let private calloutNode (id: string) (tone: ToneVariant) (heading: string) (body: string) : Node<obj> =
    Fuaran.callout
      id
      { Defaults.callout with
          Tone = tone
          Heading = Some(TextSource.Literal heading)
          Body = TextSource.Literal body }

  let private metricGrid (id: string) (cols: int) (children: Node<obj> list) : Node<obj> =
    Fuaran.gridLayout
      id
      { Defaults.gridLayout<obj> with
          Cols = cols
          Children = children }

  let private vstack (id: string) (children: Node<obj> list) : Node<obj> =
    Fuaran.stack
      id
      { Defaults.stack with
          Orientation = Orientation.Vertical
          Children = children }

  let private dashboardNode (id: string) (title: string) (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
      id
      { Defaults.dashboard with
          Children = headingNode (id + "-h") 1 title :: children }

  // ── the seed patterns (mirror the public Fuaran.UI.FastPath catalogue) ───
  let private seed: Pattern list =
    [ { Id = "single-metric"
        Title = "Single metric"
        Summary = "One labelled KPI value."
        ResultType = "Metric"
        Holes =
          [ textHole "metric.label" "a label"
            numberHole "metric.value" "a number" 0 1000000 ]
        Build = fun _ -> metric "sm" "Revenue" 128000.0 (CellFormat.Currency "GBP") ToneVariant.Brand }
      { Id = "metric-strip"
        Title = "Metric strip"
        Summary = "A row of three KPIs."
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
            metricGrid
              "strip"
              3
              [ metric "s0" "Revenue" 128000.0 (CellFormat.Currency "GBP") ToneVariant.Brand
                metric "s1" "Orders" 1318.0 (CellFormat.Number(Some 0)) ToneVariant.Default
                metric "s2" "Conversion" 0.058 (CellFormat.Percent(Some 1)) ToneVariant.Success ] }
      { Id = "kpi-card"
        Title = "KPI card"
        Summary = "A single big value inside a titled card."
        ResultType = "Card"
        Holes = [ textHole "card.label" "a label"; textHole "card.value" "a value" ]
        Build =
          fun _ ->
            Fuaran.card
              "kpi"
              { Defaults.card<obj> with
                  Heading = Some(TextSource.Literal "Revenue")
                  Children = [ Fuaran.markdown "kpi-v" "**£128k** this month" ] } }
      { Id = "dashboard-shell"
        Title = "Dashboard shell"
        Summary = "A titled dashboard with a metric strip."
        ResultType = "Box"
        Holes =
          [ textHole "title" "a title"
            textHole "m0.label" "labels"
            numberHole "m0.value" "numbers" 0 1000000
            textHole "m1.label" "labels"
            numberHole "m1.value" "numbers" 0 1000000 ]
        Build =
          fun _ ->
            dashboardNode
              "dash"
              "Q3 performance"
              [ metricGrid
                  "dash-strip"
                  3
                  [ metric "d0" "Revenue" 128000.0 (CellFormat.Currency "GBP") ToneVariant.Brand
                    metric "d1" "Orders" 1318.0 (CellFormat.Number(Some 0)) ToneVariant.Default
                    metric "d2" "Margin" 0.58 (CellFormat.Percent(Some 0)) ToneVariant.Success ] ] }
      { Id = "hero"
        Title = "Hero"
        Summary = "A headline and a supporting line."
        ResultType = "Box"
        Holes = [ textHole "headline" "a headline"; textHole "sub" "a supporting line" ]
        Build =
          fun _ ->
            vstack
              "hero"
              [ headingNode "hero-h" 1 "Ship your ideas faster"
                Fuaran.markdown "hero-sub" "The fastest way to build – no server, just data." ] }
      { Id = "callout-info"
        Title = "Info callout"
        Summary = "A titled informational callout."
        ResultType = "Callout"
        Holes = [ textHole "heading" "a heading"; textHole "body" "a body" ]
        Build = fun _ -> calloutNode "info" ToneVariant.Info "Heads up" "Something worth knowing." }
      { Id = "empty-state"
        Title = "Empty state"
        Summary = "A subdued placeholder."
        ResultType = "Callout"
        Holes = [ textHole "message" "a message" ]
        Build =
          fun _ -> calloutNode "empty" ToneVariant.Subdued "Nothing here yet" "Add your first item to get started." }
      { Id = "error-state"
        Title = "Error state"
        Summary = "A critical-tone failure message."
        ResultType = "Callout"
        Holes = [ textHole "message" "a message" ]
        Build = fun _ -> calloutNode "error" ToneVariant.Critical "Something went wrong" "Please try again." }
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
          fun _ ->
            vstack
              "section"
              [ headingNode "sec-h" 2 "About"
                Fuaran.markdown "sec-b" "A short paragraph of copy you can edit by prompt." ] }
      { Id = "compute-metric"
        Title = "Computed metric"
        Summary = "A KPI computed live from data by a transform pipeline – no server."
        ResultType = "Metric"
        Holes = [ textHole "metric.label" "a label"; textHole "data.source" "a data table" ]
        Build = fun _ -> computeMetric "cm" "Revenue by region" }
      { Id = "compute-dashboard"
        Title = "Computed dashboard"
        Summary = "A dashboard whose figure is computed live from data – no server."
        ResultType = "Box"
        Holes = [ textHole "title" "a title"; textHole "data.source" "a data table" ]
        Build = fun _ -> dashboardNode "cd" "Revenue" [ computeMetric "cd-m" "Revenue by region" ] } ]

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

  let private kinds =
    [ None, "Any"
      Some "Box", "Layout"
      Some "Metric", "Metric"
      Some "Card", "Card"
      Some "Callout", "Callout"
      Some "Markdown", "Text" ]

  let private groups = [ "text", "text"; "numbers", "numbers"; "data", "data" ]

  [<ReactComponent>]
  let Panel (onLoad: Node<obj> -> unit) : ReactElement =
    let produce, setProduce = React.useState (None: string option)
    let context, setContext = React.useState (Set.ofList [ "text"; "numbers" ])

    let matches = findRunnable (query (groupHoles context) produce) theBank

    let toggle (g: string) : unit =
      setContext (
        if context.Contains g then
          Set.remove g context
        else
          Set.add g context
      )

    Html.div
      [ prop.className "fl-pb"
        prop.children
          [ Html.p
              [ prop.className "fl-pb-intro"
                prop.text "Or search the pattern bank – a known-good app, no key, no model:" ]
            Html.div
              [ prop.className "fl-pb-filters"
                prop.children
                  [ Html.div
                      [ prop.className "fl-pb-row"
                        prop.children
                          [ for (k, label) in kinds ->
                              Html.button
                                [ prop.className (
                                    if produce = k then
                                      "fl-pb-pill fl-pb-pill-on"
                                    else
                                      "fl-pb-pill"
                                  )
                                  prop.text label
                                  prop.onClick (fun _ -> setProduce k) ] ] ]
                    Html.div
                      [ prop.className "fl-pb-row"
                        prop.children
                          [ for (g, label) in groups ->
                              Html.button
                                [ prop.className (
                                    if context.Contains g then
                                      "fl-pb-chip fl-pb-chip-on"
                                    else
                                      "fl-pb-chip"
                                  )
                                  prop.text ((if context.Contains g then "✓ " else "") + label)
                                  prop.onClick (fun _ -> toggle g) ] ] ] ] ]
            Html.div
              [ prop.className "fl-pb-count"
                prop.text (sprintf "%d match · deterministic · 0 ms" (List.length matches)) ]
            Html.div
              [ prop.className "fl-pb-list"
                prop.children
                  [ for p in matches ->
                      Html.button
                        [ prop.className "fl-pb-item"
                          prop.title p.Summary
                          prop.onClick (fun _ -> onLoad (instantiate p))
                          prop.children
                            [ Html.span [ prop.className "fl-pb-item-title"; prop.text p.Title ]
                              Html.span [ prop.className "fl-pb-item-summary"; prop.text p.Summary ] ] ] ] ] ] ]

  /// The pattern-bank panel – pick a pattern and it loads into the playground.
  let panel (onLoad: Node<obj> -> unit) : ReactElement = Panel onLoad
