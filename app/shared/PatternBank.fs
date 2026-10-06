namespace Fuaran.Live

// ============================================================================
//  The Pattern Bank – composition by lookup, not by generation. The ONE
//  definition of the bank, shared by both entries of the site: the playground
//  (app/FuaranLive.fsproj) compiles it, and the showcase (app/showcase/
//  Showcase.fsproj) links the very same file, as it links navigator/Cursor.fs.
//
//  Describe the kind of node you want and the holes you can fill, and the bank
//  returns the patterns you can run – instantly, with no key and no model call.
//  The search is the REAL `Fuaran.Core.FunctionRegistry.findBySignature`,
//  compiled to JavaScript via Fable: deterministic, total, in-memory. No server.
//
//  Generic over the message type: the playground loads a pattern into its
//  editable `Node<obj>` session; the showcase renders it as a `Node<unit>`.
//
//  Two seed catalogues, held here side by side. They share their twelve ids and
//  differ in titles, holes and trees (the playground's builders ignore hole
//  values; the showcase's honour them). Which ONE catalogue the site offers is a
//  product decision, not a refactoring one, so both are kept exactly as each
//  entry has always shipped them; test/patternBank.test.ts pins every id, hole
//  and encoded tree of both to those bytes.
// ============================================================================

/// The signature-search façade – isolated so it can `open Fuaran.Core` without
/// `HoleDecl` colliding with `Fuaran.UI.Types.HoleDecl`.
module PatternBankEngine =

  open Fuaran.Core
  open Fuaran.Compute

  type Pattern<'msg> =
    { Id: string
      Title: string
      Summary: string
      ResultType: string
      Holes: HoleDecl list
      Build: Map<string, string> -> Fuaran.UI.Types.Node<'msg> }

  type Bank<'msg> =
    { Registry: FunctionRegistry
      Patterns: Map<string, Pattern<'msg>> }

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

  let bank (patterns: Pattern<'msg> list) : Bank<'msg> =
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

  let find (mode: MatchMode) (q: Query) (b: Bank<'msg>) : Pattern<'msg> list =
    let sq: SignatureQuery =
      { ResultType = q.Produce
        Available = q.Provide |> List.map sigEntryOf }

    FunctionRegistry.findBySignature mode sq b.Registry
    |> List.choose (fun (e: FunctionEntry) -> Map.tryFind e.Capability.Id b.Patterns)

  let findRunnable (q: Query) (b: Bank<'msg>) : Pattern<'msg> list = find Subsumes q b

  let query (provide: HoleDecl list) (produce: string option) : Query =
    { Provide = provide; Produce = produce }

  /// The holes a set of "provide" context groups supplies: the text addresses
  /// the caller names, the four metric values, and the data table.
  let groupHoles (textAddrs: string list) (ctx: Set<string>) : HoleDecl list =
    [ if ctx.Contains "text" then
        yield! textAddrs |> List.map (fun a -> textHole a a)
      if ctx.Contains "numbers" then
        yield!
          [ "metric.value"; "m0.value"; "m1.value"; "m2.value" ]
          |> List.map (fun a -> numberHole a a 0 1000000)
      if ctx.Contains "data" then
        yield textHole "data.source" "data.source" ]

  /// The names of a pattern's declared holes – for display ("needs: …").
  let holeNames (p: Pattern<'msg>) : string list = p.Holes |> List.map (fun h -> h.Name)

  let tryPattern (id: string) (b: Bank<'msg>) : Pattern<'msg> option = Map.tryFind id b.Patterns

  let instantiate (p: Pattern<'msg>) (values: Map<string, string>) : Fuaran.UI.Types.Node<'msg> = p.Build values

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


/// The two seed catalogues and the tree helpers they are built from.
module PatternBankSeeds =

  open Fuaran.UI
  open Fuaran.UI.Types
  open PatternBankEngine

  let private strOf (v: Map<string, string>) (addr: string) (dflt: string) : string =
    match Map.tryFind addr v with
    | Some s when s <> "" -> s
    | _ -> dflt

  let private metric (id: string) (label: string) (value: float) (fmt: CellFormat) (tone: ToneVariant) : Node<'msg> =
    Fuaran.metric
      id
      { Defaults.metric with
          Label = TextSource.Literal label
          Value = Binding.Static(Some value)
          Format = fmt
          Tone = tone }

  /// A metric at the default format and tone.
  let private metricNode (id: string) (label: string) (value: float) : Node<'msg> =
    metric id label value Defaults.metric.Format Defaults.metric.Tone

  let private computeMetric (id: string) (label: string) : Node<'msg> =
    Fuaran.metric
      id
      { Defaults.metric with
          Label = TextSource.Literal label
          Value = computedRevenue }

  let private headingNode (id: string) (level: int) (text: string) : Node<'msg> =
    Fuaran.heading
      id
      { Level = level
        Text = TextSource.Literal text
        Variant = HeadingVariant.Standard }

  let private calloutNode (id: string) (tone: ToneVariant) (heading: string) (body: string) : Node<'msg> =
    Fuaran.callout
      id
      { Defaults.callout with
          Tone = tone
          Heading = Some(TextSource.Literal heading)
          Body = TextSource.Literal body }

  let private metricGrid (id: string) (cols: int) (children: Node<'msg> list) : Node<'msg> =
    Fuaran.gridLayout
      id
      { Defaults.gridLayout with
          Cols = cols
          Children = children }

  let private vstack (id: string) (children: Node<'msg> list) : Node<'msg> =
    Fuaran.stack
      id
      { Defaults.stack with
          Orientation = Orientation.Vertical
          Children = children }

  let private dashboardNode (id: string) (title: string) (children: Node<'msg> list) : Node<'msg> =
    Fuaran.dashboard
      id
      { Defaults.dashboard with
          Children = headingNode (id + "-h") 1 title :: children }

  let private vbox (id: string) (role: BoxRole) (heading: TextSource option) (children: Node<'msg> list) : Node<'msg> =
    Fuaran.box
      id
      { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 12)
        Role = role
        KeepTogether = false
        BreakBefore = false
        Heading = heading
        Children = children }

  let private hbox (id: string) (children: Node<'msg> list) : Node<'msg> =
    Fuaran.box
      id
      { Layout = LayoutMode.Flex(Orientation.Horizontal, true, Some 12)
        Role = BoxRole.Group
        KeepTogether = false
        BreakBefore = false
        Heading = None
        Children = children }

  let private ctaButton (id: string) (label: string) : Node<'msg> =
    Fuaran.button
      id
      { Defaults.button with
          Label = TextSource.Literal label
          OnClick = Action.navigate "cta"
          Variant = ButtonVariant.Primary }

  /// The playground's catalogue – its builders load a fixed exemplar tree and
  /// ignore hole values (the playground edits the tree by prompt afterwards).
  let playground<'msg> () : Pattern<'msg> list =
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
              { Defaults.card with
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


  /// The showcase catalogue – its builders honour the hole values they are given.
  let showcase<'msg> () : Pattern<'msg> list =
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
        Build = fun v -> computeMetric "cm" (strOf v "metric.label" "Revenue by region") }
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
              [ computeMetric "cd-m" "Revenue by region" ] } ]
