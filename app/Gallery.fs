module Fuaran.Live.Gallery

// ============================================================================
//  THE GET-STARTED EXAMPLES LIBRARY, live tier.
//
//  A curated showcase. Each example is a ready-made `Fuaran.UI` tree the visitor
//  can load WITHOUT a key – proof the renderer + the wire format stand on their
//  own, and a starting point to then edit by prompt. Built with the F# smart
//  constructors and rendered by the same F# renderer that draws an LLM's
//  emission.
//
//  ONE COOL SIMPLE APP PER FEATURE. Every entry carries a `Feature` tag naming
//  the feature area it leads with, so the list reads as a tour of the language
//  rather than a bag of demos, and a reader can find the entry for the thing
//  they are trying to author. The tags are display strings, not a closed set –
//  a new entry names its own area.
//
//  WHY THESE TREES CANNOT DRIFT FROM THE SCHEMA. Every one is built through the
//  real `Fuaran.*` smart constructors over the typed `NodeKind` and frozen to
//  wire JSON by the reference `CanonicalJson` encoder, so it is canon by
//  construction – the emitter-lock convention in CLAUDE.md excludes exactly this
//  shape, because the reference encoder is its own oracle. `exampleWires` is the
//  cross-boundary surface `test/permalinkGallery.test.ts` certifies: every entry
//  is strictly decodable by `@fuaran-ui/ops`, already canonical (decode then
//  re-encode is the identity), and permalink-shareable.
//
//  AND THE FOUR-HOST SOURCE COMES FOR FREE. Loading an entry puts its tree in
//  the playground session, which is what the Output box projects to JSON / F# /
//  TypeScript / Python / C# / VB (`Projection.fs`). So an example is authored
//  once, here, and read in any host – no per-host hand-duplication, and nothing
//  to keep in step.
//
//  VOCABULARY CEILING. The playground consumes the published `Fuaran.UI` family
//  at the version pinned in `app/FuaranLive.fsproj` (held deliberately – the
//  reason is recorded beside the pin). An entry may only use vocabulary that
//  version carries; a kind added to the language later belongs in the runnable
//  code-sample tier until the pin moves.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// The columnar data strand + the declarative transform algebra are used FULLY
// QUALIFIED below (`Fuaran.Core.Filter`, `Fuaran.Core.Str`, …) rather than opened:
// several of their names — `Column`, `Filter`, `Format` — also exist in the
// `Fuaran.UI` vocabulary this file is written in, and an `open` would decide which
// one won by file order rather than by intent.

/// One curated example. `Feature` is the feature area the entry leads with (the
/// gallery groups and labels by it); `Blurb` is the one-line "what this shows"
/// the visitor reads before loading it.
type Example =
  { Title: string
    Feature: string
    Blurb: string
    Tree: Node<obj> }

let private metric (id: string) (label: string) (value: float) (fmt: CellFormat) (tone: ToneVariant) : Node<obj> =
  Fuaran.metric
    id
    { Defaults.metric with
        Label = TextSource.Literal label
        Value = Binding.Static(Some value)
        Format = fmt
        Tone = tone }

let private salesDashboard: Node<obj> =
  Fuaran.dashboard
    "ex-sales"
    { Defaults.dashboard with
        Children =
          [ Fuaran.heading
              "ex-sales-title"
              { Level = 1
                Text = TextSource.Literal "Q4 Sales"
                Variant = HeadingVariant.Standard }
            Fuaran.gridLayout
              "ex-sales-kpis"
              { Defaults.gridLayout<obj> with
                  Cols = 3
                  Children =
                    [ metric "ex-sales-rev" "Revenue" 142500.0 (CellFormat.Currency "GBP") ToneVariant.Brand
                      metric "ex-sales-orders" "Orders" 1284.0 (CellFormat.Number(Some 0)) ToneVariant.Default
                      metric "ex-sales-conv" "Conversion" 0.043 (CellFormat.Percent(Some 1)) ToneVariant.Success ] }
            Fuaran.markdown
              "ex-sales-note"
              "Updated hourly. **No server** – this is canonical Fuaran wire JSON rendered live. Type a prompt to edit it." ] }

let private welcomeCard: Node<obj> =
  Fuaran.card
    "ex-welcome"
    { Defaults.card<obj> with
        Heading = Some(TextSource.Literal "Welcome to fuaran-live")
        Children =
          [ Fuaran.markdown
              "ex-welcome-body"
              "Prompt an LLM with your own key and it emits a typed UI as **canonical wire-format JSON** – decoded, applied, and rendered live, with no server and no code execution.\n\nThe whole playground is itself a Fuaran app, built in F#." ] }

// ─── the developer headline – a generated docs / tutorial page (Phase 294) ───
//
// Code + an equation + a GFM table, each a first-class Fuaran primitive rather
// than plain Markdown: `CodeBlock` (Phase 290) carries a language tag + copy
// button, `Math` (Phase 293) KaTeX-renders live, and the Markdown table routes
// to the real `fuaran-table` render. The dev-audience flagship – "prompt → a
// polished docs page → grab the code" – alongside the data + designer demos.

let private tsSnippet =
  """import { fuaran, binding } from '@fuaran-ui/ui';

// A UI is data: author it, or let a model emit the same wire JSON.
const revenue = fuaran.metric({
  id: 'revenue',
  label: 'Revenue',
  value: binding.static(142500),
  format: { kind: 'currency', code: 'GBP' },
});"""

// Triple-quoted so the LaTeX backslashes stay literal (no escape processing).
let private conversionLatex =
  """\text{conversion} = \frac{\text{orders}}{\text{sessions}} \times 100\%"""

let private docsPage: Node<obj> =
  Fuaran.dashboard
    "ex-docs"
    { Defaults.dashboard with
        Children =
          [ Fuaran.heading
              "ex-docs-title"
              { Level = 1
                Text = TextSource.Literal "Getting started with Fuaran"
                Variant = HeadingVariant.Standard }
            Fuaran.markdown
              "ex-docs-intro"
              "A Fuaran UI is **data** – a typed tree emitted as canonical wire-format JSON and rendered live, with no code execution. Author one by hand in TypeScript, F#, or Python, or let an LLM emit it."
            Fuaran.heading
              "ex-docs-code-h"
              { Level = 2
                Text = TextSource.Literal "Author a metric in TypeScript"
                Variant = HeadingVariant.Standard }
            Fuaran.codeBlockSpec
              "ex-docs-code"
              { Defaults.codeBlock with
                  Language = "typescript"
                  LineNumbers = true
                  Code = tsSnippet }
            Fuaran.heading
              "ex-docs-math-h"
              { Level = 2
                Text = TextSource.Literal "How conversion is computed"
                Variant = HeadingVariant.Standard }
            Fuaran.math "ex-docs-math" conversionLatex
            Fuaran.heading
              "ex-docs-table-h"
              { Level = 2
                Text = TextSource.Literal "Display kinds at a glance"
                Variant = HeadingVariant.Standard }
            Fuaran.markdown
              "ex-docs-table"
              "| Kind | Use |\n|---|---|\n| `Metric` | A single KPI value |\n| `CodeBlock` | A syntax-tagged code sample with copy |\n| `Math` | A LaTeX equation, KaTeX-rendered |\n| `Chart` | A data visualisation |" ] }

let private statStack: Node<obj> =
  Fuaran.stack
    "ex-stats"
    { Defaults.stack with
        Orientation = Orientation.Vertical
        Children =
          [ Fuaran.heading
              "ex-stats-title"
              { Level = 2
                Text = TextSource.Literal "Service health"
                Variant = HeadingVariant.Standard }
            metric "ex-stats-uptime" "Uptime" 0.9993 (CellFormat.Percent(Some 2)) ToneVariant.Success
            metric "ex-stats-p95" "p95 latency (ms)" 184.0 (CellFormat.Number(Some 0)) ToneVariant.Default
            metric "ex-stats-errors" "Error rate" 0.002 (CellFormat.Percent(Some 1)) ToneVariant.Warning ] }

// ─── the DATA headline – an interactive dataset explorer (the Compute layer) ──
//
// The newest and most striking thing the language does in a browser: the filter,
// the aggregation and the sort are DATA, not code. The `Select` writes one state
// slot; the declarative pipeline below is PARAMETERISED on that slot, so changing
// the region re-runs `filter → group → sort` client-side and every reader – the
// chart and the headline metric – re-renders from the recomputed rows. No
// handler, no server, no model call. And because a pipeline is a value, the whole
// explorer survives the wire: share the permalink and the recompute goes with it.

let private explorerColumn
  (name: string)
  (ty: Fuaran.Core.ColumnType)
  (cells: Fuaran.Core.Cell list)
  : Fuaran.Core.Column =
  { Name = name
    Type = ty
    Cells = cells }

let private explorerSource: Fuaran.Core.DataSource =
  let region =
    [ Fuaran.Core.Str "North"
      Fuaran.Core.Str "North"
      Fuaran.Core.Str "North"
      Fuaran.Core.Str "North"
      Fuaran.Core.Str "South"
      Fuaran.Core.Str "South"
      Fuaran.Core.Str "South"
      Fuaran.Core.Str "South"
      Fuaran.Core.Str "West"
      Fuaran.Core.Str "West"
      Fuaran.Core.Str "West"
      Fuaran.Core.Str "West" ]

  let product =
    [ Fuaran.Core.Str "Widgets"
      Fuaran.Core.Str "Gadgets"
      Fuaran.Core.Str "Sprockets"
      Fuaran.Core.Str "Cogs"
      Fuaran.Core.Str "Widgets"
      Fuaran.Core.Str "Gadgets"
      Fuaran.Core.Str "Sprockets"
      Fuaran.Core.Str "Cogs"
      Fuaran.Core.Str "Widgets"
      Fuaran.Core.Str "Gadgets"
      Fuaran.Core.Str "Sprockets"
      Fuaran.Core.Str "Cogs" ]

  let units =
    [ 120; 80; 45; 210; 95; 130; 60; 175; 140; 70; 90; 250 ]
    |> List.map Fuaran.Core.Int

  let revenue =
    [ 24000.0
      12800.0
      6750.0
      10500.0
      19000.0
      20800.0
      9000.0
      8750.0
      28000.0
      11200.0
      13500.0
      12500.0 ]
    |> List.map Fuaran.Core.Float

  Fuaran.Core.Embedded
    { Schema =
        [ "region", Fuaran.Core.StringType
          "product", Fuaran.Core.StringType
          "units", Fuaran.Core.IntType
          "revenue", Fuaran.Core.FloatType ]
      Columns =
        [ explorerColumn "region" Fuaran.Core.StringType region
          explorerColumn "product" Fuaran.Core.StringType product
          explorerColumn "units" Fuaran.Core.IntType units
          explorerColumn "revenue" Fuaran.Core.FloatType revenue ] }

/// The region scope, as an expression: "All" means no constraint, anything else
/// matches the column. `Param "region"` is bound per evaluation from the state
/// slot the `Select` writes — the pipeline itself names no value.
let private regionScope: Fuaran.Core.ColExpr =
  Fuaran.Core.Binary(
    Fuaran.Core.Or,
    Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Param "region", Fuaran.Core.Lit(Fuaran.Core.Str "All")),
    Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "region", Fuaran.Core.Param "region")
  )

/// The one place the param is bound. Both readers below share it, so both
/// re-derive from the same slot on a change — one declaration, two subscribers.
let private regionParam: TransformParam =
  { From = Binding.State("explorer.region", Some(Fuaran.Core.JStr "All"))
    Name = "region" }

let private byProduct: Fuaran.Core.Transform list =
  [ Fuaran.Core.Filter regionScope
    Fuaran.Core.GroupBy(
      [ "product" ],
      [ ({ Name = "revenue"
           Fn = Fuaran.Core.Sum
           Of = "revenue" }
        : Fuaran.Core.Agg)
        ({ Name = "units"
           Fn = Fuaran.Core.Sum
           Of = "units" }
        : Fuaran.Core.Agg) ]
    )
    Fuaran.Core.Sort [ "revenue", Fuaran.Core.Desc ] ]

/// The same scope with no group key — a global aggregate, which resolves as the
/// 1×1 result cell a scalar slot (a `Metric`'s value) reads.
let private totalRevenue: Fuaran.Core.Transform list =
  [ Fuaran.Core.Filter regionScope
    Fuaran.Core.GroupBy(
      [],
      [ ({ Name = "revenue"
           Fn = Fuaran.Core.Sum
           Of = "revenue" }
        : Fuaran.Core.Agg) ]
    ) ]

let private datasetExplorer: Node<obj> =
  Fuaran.dashboard
    "ex-explorer"
    { Defaults.dashboard with
        Children =
          [ Fuaran.heading
              "ex-explorer-title"
              { Level = 1
                Text = TextSource.Literal "Sales explorer"
                Variant = HeadingVariant.Standard }
            Fuaran.markdown
              "ex-explorer-intro"
              "Pick a region. The filter, the grouping and the sort are **declarative data** – the browser re-derives the rows, with no handler and no server."
            Fuaran.select
              "ex-explorer-region"
              { Defaults.select<obj> with
                  Label = TextSource.Literal "Region"
                  Source =
                    Binding.Static(
                      Some
                        [ { Label = "All regions"; Value = "All" }
                          { Label = "North"; Value = "North" }
                          { Label = "South"; Value = "South" }
                          { Label = "West"; Value = "West" } ]
                    )
                  Value = Binding.State("explorer.region", Some "All") }
            Fuaran.metric
              "ex-explorer-total"
              { Defaults.metric with
                  Label = TextSource.Literal "Revenue in view"
                  Value = Binding.Transform(TransformSource.Data explorerSource, totalRevenue, Some [ regionParam ])
                  Format = CellFormat.Currency "GBP"
                  Tone = ToneVariant.Brand }
            Fuaran.chart
              "ex-explorer-chart"
              { Defaults.chart<obj> with
                  Kind = ChartKind.Bar
                  Source = Binding.Transform(TransformSource.Data explorerSource, byProduct, Some [ regionParam ])
                  XField = "product"
                  YFields = [ "revenue" ]
                  Title = Some(TextSource.Literal "Revenue by product")
                  ValueFormat = Some(localeFormat.currency "GBP") } ] }

// ─── Display – a project status board (Callout / Badge / Progress) ────────────

let private statusColumn
  (slug: string)
  (name: string)
  (state: string)
  (variant: BadgeVariant)
  (share: float)
  : Node<obj> =
  Fuaran.card
    ("ex-status-" + slug)
    { Defaults.card<obj> with
        Heading = Some(TextSource.Literal name)
        Children =
          [ Fuaran.badge
              ("ex-status-" + slug + "-badge")
              { Label = TextSource.Literal state
                Variant = variant }
            Fuaran.progress
              ("ex-status-" + slug + "-progress")
              { Defaults.progress with
                  Fraction = Binding.Static(Some share)
                  Label = Some(TextSource.Literal(sprintf "%d%% complete" (int (share * 100.0)))) } ] }

let private statusBoard: Node<obj> =
  Fuaran.dashboard
    "ex-status"
    { Defaults.dashboard with
        Children =
          [ Fuaran.heading
              "ex-status-title"
              { Level = 1
                Text = TextSource.Literal "Release 4.2"
                Variant = HeadingVariant.Standard }
            Fuaran.callout
              "ex-status-callout"
              { Defaults.callout with
                  Tone = ToneVariant.Warning
                  Heading = Some(TextSource.Literal "Ship date moved")
                  Body = TextSource.Literal "Two workstreams are behind. The date moved to the 28th." }
            Fuaran.gridLayout
              "ex-status-cols"
              { Defaults.gridLayout<obj> with
                  Cols = 3
                  Children =
                    [ statusColumn "api" "API" "On track" BadgeVariant.Success 0.82
                      statusColumn "web" "Web client" "At risk" BadgeVariant.Warning 0.54
                      statusColumn "docs" "Docs" "Blocked" BadgeVariant.Critical 0.2 ] } ] }

// ─── Navigation – settings + an FAQ (Stack / Tabs / Disclosure) ───────────────

let private faqEntry (slug: string) (question: string) (answer: string) : Node<obj> =
  Fuaran.disclosure
    ("ex-faq-" + slug)
    { Defaults.disclosure<obj> with
        Heading = TextSource.Literal question
        Open = Binding.State("faq." + slug, Some false)
        Children = [ Fuaran.markdown ("ex-faq-" + slug + "-body") answer ] }

let private settingsAndFaq: Node<obj> =
  Fuaran.tabs
    "ex-settings"
    { Defaults.tabs<obj> with
        ActiveIndex = Binding.State("settings.tab", Some 0)
        TabHeaders =
          Some
            [ { Defaults.tabHeader with
                  Label = TextSource.Literal "Profile" }
              { Defaults.tabHeader with
                  Label = TextSource.Literal "Notifications" }
              { Defaults.tabHeader with
                  Label = TextSource.Literal "FAQ" } ]
        Children =
          [ Fuaran.summaryList
              "ex-settings-profile"
              { Defaults.summaryList<obj> with
                  Heading = Some(TextSource.Literal "Your details")
                  Children =
                    [ Fuaran.fact "ex-settings-name" "Name" "Ada Lovelace"
                      Fuaran.fact "ex-settings-email" "Email" "ada@example.org"
                      Fuaran.fact "ex-settings-plan" "Plan" "Team" ] }
            Fuaran.stack
              "ex-settings-notify"
              { Defaults.stack with
                  Children =
                    [ Fuaran.markdown
                        "ex-settings-notify-note"
                        "Choose what reaches you. The active tab is a state slot, so a permalink restores the tab you were on."
                      Fuaran.badge
                        "ex-settings-notify-badge"
                        { Label = TextSource.Literal "Weekly digest on"
                          Variant = BadgeVariant.Info } ] }
            Fuaran.stack
              "ex-settings-faq"
              { Defaults.stack with
                  Children =
                    [ faqEntry
                        "wire"
                        "Is the UI really just data?"
                        "Yes. Everything here is a typed tree serialised as canonical JSON. There is no code in the payload, and none can be smuggled in."
                      faqEntry
                        "keys"
                        "Do you see my API key?"
                        "No. There is no server. Your key stays in the tab and goes only to the provider you chose."
                      faqEntry
                        "hosts"
                        "Can I author in another language?"
                        "Yes – F#, TypeScript and Python are co-equal hosts of the same wire format. Load any example and read the Output box." ] } ] }

// ─── Interaction – a mode toggle (Button + SetState + Switch) ─────────────────

let private modeButton (slug: string) (label: string) (value: string) (variant: ButtonVariant) : Node<obj> =
  Fuaran.button
    ("ex-toggle-" + slug)
    { Defaults.button<obj> with
        Label = TextSource.Literal label
        OnClick = Action.setState "gallery.mode" (Fuaran.Core.JStr value)
        Variant = variant }

let private modeToggle: Node<obj> =
  Fuaran.card
    "ex-toggle"
    { Defaults.card<obj> with
        Heading = Some(TextSource.Literal "Pick a mode")
        Children =
          [ Fuaran.markdown
              "ex-toggle-note"
              "A `Button` carries a typed `SetState` action; a `Switch` renders the branch that matches. Two declarations, and no update function anywhere."
            Fuaran.stack
              "ex-toggle-buttons"
              { Defaults.stack with
                  Orientation = Orientation.Horizontal
                  Children =
                    [ modeButton "calm" "Calm" "calm" ButtonVariant.Primary
                      modeButton "bold" "Bold" "bold" ButtonVariant.Secondary ] }
            Fuaran.switch
              "ex-toggle-switch"
              { Defaults.switch<obj> with
                  On = Binding.State("gallery.mode", Some "calm")
                  Cases =
                    [ { Match = "calm"
                        Child =
                          Fuaran.callout
                            "ex-toggle-calm"
                            { Defaults.callout with
                                Tone = ToneVariant.Info
                                Heading = Some(TextSource.Literal "Calm")
                                Body =
                                  TextSource.Literal "Quiet tones, generous spacing, nothing shouting for attention." } }
                      { Match = "bold"
                        Child =
                          Fuaran.callout
                            "ex-toggle-bold"
                            { Defaults.callout with
                                Tone = ToneVariant.Brand
                                Heading = Some(TextSource.Literal "Bold")
                                Body = TextSource.Literal "High contrast, tight rhythm, the headline doing the work." } } ]
                  Default = Fuaran.markdown "ex-toggle-none" "_Pick a mode above._" } ] }

// ─── Forms – a signup form (Form + declarative fields) ────────────────────────

let private formFieldOf (id: string) (label: string) (required: bool) (kind: FormFieldKind<obj>) : FormField<obj> =
  { Defaults.formField<obj> with
      Id = id
      Label = TextSource.Literal label
      Required = required
      Kind = kind }

let private signupForm: Node<obj> =
  Fuaran.card
    "ex-signup"
    { Defaults.card<obj> with
        Heading = Some(TextSource.Literal "Create your account")
        Children =
          [ Fuaran.markdown
              "ex-signup-note"
              "Each field binds a state slot declaratively; the submit carries a typed action. Nothing here is a closure, which is why the whole form survives a permalink."
            Fuaran.form
              "ex-signup-form"
              { Defaults.form<obj> with
                  SubmitLabel = TextSource.Literal "Create account"
                  OnSubmit = Action.setState "signup.submitted" (Fuaran.Core.JBool true)
                  Fields =
                    [ formFieldOf
                        "signup-name"
                        "Full name"
                        true
                        (FormFieldKind.textDeclarative (Binding.State("signup.name", Some "")))
                      formFieldOf
                        "signup-team"
                        "Team size"
                        false
                        (FormFieldKind.choiceDeclarative
                          (Binding.Static(
                            Some
                              [ { Label = "Just me"; Value = "1" }
                                { Label = "2 to 10"; Value = "10" }
                                { Label = "More than 10"; Value = "50" } ]
                          ))
                          (Binding.State("signup.team", Some "1")))
                      formFieldOf
                        "signup-digest"
                        "Send me the weekly digest"
                        false
                        (FormFieldKind.toggleDeclarative (Binding.State("signup.digest", Some true))) ] } ] }

// ─── Charts, tables and maps – a regional review ──────────────────────────────

let private monthRow (month: string) (sales: float) : Fuaran.Core.Row =
  Map.ofList [ "month", box month; "sales", box sales ]

let private regionalReview: Node<obj> =
  Fuaran.dashboard
    "ex-regional"
    { Defaults.dashboard with
        Children =
          [ Fuaran.heading
              "ex-regional-title"
              { Level = 1
                Text = TextSource.Literal "Regional review"
                Variant = HeadingVariant.Standard }
            Fuaran.chart
              "ex-regional-chart"
              { Defaults.chart<obj> with
                  Kind = ChartKind.Line
                  Source =
                    Binding.Static(
                      Some(
                        Seq.ofList
                          [ monthRow "Jan" 18400.0
                            monthRow "Feb" 21050.0
                            monthRow "Mar" 19600.0
                            monthRow "Apr" 24800.0
                            monthRow "May" 26150.0
                            monthRow "Jun" 25400.0 ]
                      )
                    )
                  XField = "month"
                  YFields = [ "sales" ]
                  Title = Some(TextSource.Literal "Monthly sales")
                  ValueFormat = Some(localeFormat.currency "GBP") }
            // A per-store breakdown as facts rather than a static table. The
            // static-table shape is deliberately ABSENT from the live tier at
            // this pin: the published `Fuaran.UI` version this app is held at
            // encodes a rows-absent grid source as `{"$type":"Static"}`, where
            // the corpus (`nodes/table-1.json`) and the `@fuaran-ui/ops` decoder
            // this app ships with both make it `{"$type":"Static","value":[]}`.
            // The entry would render, and its permalink would still restore —
            // but it would not be the canonical form of itself, which is the one
            // property this library promises. It belongs in the runnable
            // code-sample tier (which builds against the current tier) until the
            // pin moves. `test/permalinkGallery.test.ts` is what caught it.
            Fuaran.summaryList
              "ex-regional-stores"
              { Defaults.summaryList<obj> with
                  Heading = Some(TextSource.Literal "Revenue by store")
                  Children =
                    [ Fuaran.labelValueRow
                        "ex-regional-leeds"
                        { Defaults.labelValueRow with
                            Label = TextSource.Literal "Leeds (North)"
                            Value = Binding.Static(Some 54000.0)
                            Format = CellFormat.Currency "GBP" }
                      Fuaran.labelValueRow
                        "ex-regional-bristol"
                        { Defaults.labelValueRow with
                            Label = TextSource.Literal "Bristol (South)"
                            Value = Binding.Static(Some 57550.0)
                            Format = CellFormat.Currency "GBP" }
                      Fuaran.labelValueRow
                        "ex-regional-cardiff"
                        { Defaults.labelValueRow with
                            Label = TextSource.Literal "Cardiff (West)"
                            Value = Binding.Static(Some 65200.0)
                            Format = CellFormat.Currency "GBP"
                            Emphasis = true } ] }
            Fuaran.map
              "ex-regional-map"
              { Defaults.map<obj> with
                  Source =
                    Binding.Static(
                      Some
                        [ { Label = "Leeds"
                            Latitude = 53.8008
                            Longitude = -1.5491 }
                          { Label = "Bristol"
                            Latitude = 51.4545
                            Longitude = -2.5879 }
                          { Label = "Cardiff"
                            Latitude = 51.4816
                            Longitude = -3.1791 } ]
                    )
                  CentreLatitude = 52.4
                  CentreLongitude = -2.4
                  Zoom = 6 } ] }

// ─── Bindings – one value, several readings (State + Format) ──────────────────

let private priceBoard: Node<obj> =
  let price: Binding<float> = Binding.State("price.gbp", Some 42.5)

  Fuaran.card
    "ex-price"
    { Defaults.card<obj> with
        Heading = Some(TextSource.Literal "One value, several readings")
        Children =
          [ Fuaran.markdown
              "ex-price-note"
              "Every reader below binds the **same** state slot. A `Format` binding turns the number into locale-aware text with no formatting call anywhere in the tree."
            Fuaran.metric
              "ex-price-metric"
              { Defaults.metric with
                  Label = TextSource.Literal "List price"
                  Value = price
                  Format = CellFormat.Currency "GBP"
                  Tone = ToneVariant.Brand }
            Fuaran.labelValueRow
              "ex-price-row"
              { Defaults.labelValueRow with
                  Label = TextSource.Literal "Units in stock"
                  Value = Binding.State("price.stock", Some 318.0)
                  Format = CellFormat.Number(Some 0) }
            Fuaran.factSpec
              "ex-price-fact"
              { Defaults.fact with
                  Label = TextSource.Literal "Shown to the customer"
                  Value = TextSource.Bound(binding.format price (localeFormat.currency "GBP") locale.ambient) } ] }

// ─── Capabilities – deferred compute behind a declared signature ──────────────

let private capabilityCard: Node<obj> =
  Fuaran.card
    "ex-invoke"
    { Defaults.card<obj> with
        Heading = Some(TextSource.Literal "Run a computation")
        Children =
          [ Fuaran.markdown
              "ex-invoke-note"
              "Some work does not belong in a pipeline. A capability is a named, typed, host-registered computation: the tree declares the **call** and its arguments, the host owns the body, and the arguments are checked against the declared signature before anything runs. Until the value arrives the node renders its declared pending state."
            Fuaran.metric
              "ex-invoke-result"
              { Defaults.metric with
                  Label = TextSource.Literal "Value at risk (95%)"
                  Value = binding.invoke "risk.var" [ "confidence", "0.95"; "horizonDays", "1" ]
                  Format = CellFormat.Currency "GBP"
                  Tone = ToneVariant.Warning }
            |> Node.onLoading (Fuaran.skeleton "ex-invoke-pending" 1) ] }

// ─── Fragments – declare a subtree once, expand it anywhere ───────────────────

let private fragmentBoard: Node<obj> =
  Fuaran.dashboard
    "ex-fragment"
    { Defaults.dashboard with
        Children =
          [ Fuaran.heading
              "ex-fragment-title"
              { Level = 2
                Text = TextSource.Literal "Declare once, expand anywhere"
                Variant = HeadingVariant.Standard }
            Fuaran.markdown
              "ex-fragment-note"
              "The card below is declared **once**. Each expansion namespaces the interior ids under its own, so three copies of one subtree stay individually addressable – which is what lets a targeted edit reach exactly one of them."
            Fuaran.fragmentDecl
              "ex-fragment-decl"
              { Defaults.fragmentDecl<obj> with
                  Name = "stat-card"
                  Body =
                    Fuaran.card
                      "body"
                      { Defaults.card<obj> with
                          Heading = Some(TextSource.Literal "This quarter")
                          Children =
                            [ Fuaran.metric
                                "value"
                                { Defaults.metric with
                                    Label = TextSource.Literal "Signups"
                                    Value = Binding.Static(Some 1240.0)
                                    Format = CellFormat.Number(Some 0) }
                              Fuaran.markdown "caption" "_Expanded from the `stat-card` fragment._" ] } }
            Fuaran.gridLayout
              "ex-fragment-row"
              { Defaults.gridLayout<obj> with
                  Cols = 3
                  Children =
                    [ Fuaran.fragmentRef "ex-fragment-a" "stat-card"
                      Fuaran.fragmentRef "ex-fragment-b" "stat-card"
                      Fuaran.fragmentRef "ex-fragment-c" "stat-card" ] } ] }

// ─── Resilience – a declared fallback for a subtree that may fail ─────────────

let private resilientPage: Node<obj> =
  Fuaran.dashboard
    "ex-boundary"
    { Defaults.dashboard with
        Children =
          [ Fuaran.markdown
              "ex-boundary-note"
              "An `ErrorBoundary` is the author saying *I expect this part to fail under some inputs, and here is what to show instead*. One broken region degrades to its fallback; the rest of the page is unaffected."
            Fuaran.errorBoundary
              "ex-boundary-guard"
              { Child =
                  Fuaran.card
                    "ex-boundary-live"
                    { Defaults.card<obj> with
                        Heading = Some(TextSource.Literal "Live feed")
                        Children =
                          [ Fuaran.metric
                              "ex-boundary-value"
                              { Defaults.metric with
                                  Label = TextSource.Literal "Requests / sec"
                                  Value = Binding.State("feed.rate", Some 1840.0)
                                  Format = CellFormat.Number(Some 0) } ] }
                Fallback =
                  Fuaran.callout
                    "ex-boundary-fallback"
                    { Defaults.callout with
                        Tone = ToneVariant.Warning
                        Heading = Some(TextSource.Literal "Feed unavailable")
                        Body =
                          TextSource.Literal
                            "The live figures could not be drawn. Everything else on this page still works." } } ] }

// ─── Accessibility – roles and a live region, authored in the tree ────────────

let private accessibleForm: Node<obj> =
  Fuaran.dashboard
    "ex-a11y"
    { Defaults.dashboard with
        Children =
          [ Fuaran.heading
              "ex-a11y-title"
              { Level = 1
                Text = TextSource.Literal "Book a callback"
                Variant = HeadingVariant.Standard }
            Fuaran.form
              "ex-a11y-form"
              { Defaults.form<obj> with
                  SubmitLabel = TextSource.Literal "Request a callback"
                  OnSubmit =
                    Action.setState "callback.status" (Fuaran.Core.JStr "Callback requested. We will ring you back.")
                  Fields =
                    [ formFieldOf
                        "a11y-phone"
                        "Phone number"
                        true
                        (FormFieldKind.textDeclarative (Binding.State("callback.phone", Some "")))
                      formFieldOf
                        "a11y-when"
                        "Best time to call"
                        false
                        (FormFieldKind.choiceDeclarative
                          (Binding.Static(
                            Some [ { Label = "Morning"; Value = "am" }; { Label = "Afternoon"; Value = "pm" } ]
                          ))
                          (Binding.State("callback.when", Some "am"))) ] }
            // A POLITE live region: a screen reader announces a change here
            // without interrupting whatever it is already reading. The
            // announcement is a property of the TREE, not of a host script — so
            // it survives the wire and holds in every conformant renderer.
            Fuaran.markdownSpec
              "ex-a11y-status"
              { Text = TextSource.Bound(Binding.State("callback.status", Some "No callback requested yet.")) }
            |> Node.withAccessibility (
              Some
                { Defaults.Accessibility.empty with
                    LiveRegion = Some LiveRegionKind.Polite
                    Label = Some(Binding.Static(Some "Callback status")) }
            ) ] }

/// The showcase, in display order — one cool simple app per feature area.
///
/// The two headlines lead, each speaking to a different reader: the **sales
/// explorer** (client-side recompute over a declarative pipeline — the data
/// audience) and the **docs / tutorial page** (code, an equation and a table as
/// first-class primitives — the developer audience). The rest walk the
/// vocabulary, one feature area each.
let examples: Example list =
  [ { Title = "Sales explorer"
      Feature = "Compute"
      Blurb = "Filter, group and sort as data — the browser re-derives the rows, with no handler and no server."
      Tree = datasetExplorer }
    { Title = "Docs / tutorial page"
      Feature = "Content"
      Blurb = "Code, an equation and a table, each a first-class node rather than a wall of markdown."
      Tree = docsPage }
    { Title = "Sales dashboard"
      Feature = "Layout"
      Blurb = "The everyday shape: a dashboard, a grid of KPIs, a note underneath."
      Tree = salesDashboard }
    { Title = "Release status board"
      Feature = "Display"
      Blurb = "Callouts, badges and progress — the status vocabulary, without a component library."
      Tree = statusBoard }
    { Title = "Settings and FAQ"
      Feature = "Navigation"
      Blurb = "Tabs and disclosures, each bound to a state slot, so a permalink restores where you were."
      Tree = settingsAndFaq }
    { Title = "Mode toggle"
      Feature = "Interaction"
      Blurb = "A button carries a typed action; a switch renders the branch that matches. No update function."
      Tree = modeToggle }
    { Title = "Signup form"
      Feature = "Forms"
      Blurb = "Text, choice and toggle fields bound declaratively, with a typed action on submit."
      Tree = signupForm }
    { Title = "Regional review"
      Feature = "Charts, tables and maps"
      Blurb = "A line chart, a summary breakdown and a map of markers — three readings of one dataset, in one tree."
      Tree = regionalReview }
    { Title = "Price board"
      Feature = "Bindings"
      Blurb = "One state slot, read three ways — including a Format binding that does the locale work."
      Tree = priceBoard }
    { Title = "Run a computation"
      Feature = "Capabilities"
      Blurb = "A typed call to a host-registered computation, with the arguments checked before it runs."
      Tree = capabilityCard }
    { Title = "Reusable stat card"
      Feature = "Fragments"
      Blurb = "One declared subtree expanded at three call sites, each keeping addressable ids of its own."
      Tree = fragmentBoard }
    { Title = "Graceful fallback"
      Feature = "Resilience"
      Blurb = "A declared fallback for a region that may fail, so one broken part cannot take the page with it."
      Tree = resilientPage }
    { Title = "Accessible callback form"
      Feature = "Accessibility"
      Blurb = "Roles and a polite live region authored in the tree, so the announcement survives the wire."
      Tree = accessibleForm }
    { Title = "Welcome card"
      Feature = "Getting started"
      Blurb = "The smallest useful tree: a card, a heading and some markdown."
      Tree = welcomeCard }
    { Title = "Service health"
      Feature = "Metrics"
      Blurb = "A stack of formatted metrics — currency, percent and plain number, each tone-tagged."
      Tree = statStack } ]

/// Every distinct feature area, in the display order of the examples that lead
/// them — the gallery's grouping key, derived rather than restated.
let features: string list = examples |> List.map _.Feature |> List.distinct

/// The canonical wire JSON of each example, as a JS array – the cross-boundary
/// surface the gallery test uses to assert every example is valid + shareable.
let exampleWires (unit: unit) : string[] =
  examples |> List.map (fun e -> Canon.encodeNode e.Tree) |> List.toArray

/// The `(title, feature)` pairs, as a JS array of two-element arrays — the
/// cross-boundary surface the gallery test reads to assert every entry is
/// feature-tagged and that no two entries collide on a title.
let exampleTags (unit: unit) : string[][] =
  examples |> List.map (fun e -> [| e.Title; e.Feature |]) |> List.toArray
