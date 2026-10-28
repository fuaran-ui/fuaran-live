module Fuaran.Showcase.TypedRefusal

// ============================================================================
//  The Typed Refusal — break a chart's binding and read the refusal (Phase 1887).
//
//  A chart names columns: an x field and one or more value fields. Its source is
//  a `Binding.Transform`, a table plus a pipeline, and the pipeline decides which
//  columns exist: a derive adds one, a group-by renames them all. When a name and
//  the pipeline disagree, the usual outcome is a blank chart nobody can explain.
//
//  Here the page asks the shipped pre-emit report (`PreEmitValidate.bindingChecks`,
//  Phase 1889) about the chart before it renders anything. The report grades the
//  chart against the schema its pipeline PROVABLY produces, and every finding
//  carries its code, its message, the JSONPath of the slot the author wrote, and
//  the typed schema the source does produce. A finding is a refusal: the page
//  draws the report, not the chart. No finding, and the chart renders.
//
//  The report is honest about what it cannot judge. Switch the source to a named
//  table the host resolves and the grade becomes "unchecked", with the reason —
//  nothing is refused, because nothing negative can be proved.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── the table and the pipeline ──────────────────────────────────────────────

/// Twelve orders: region, month, units, revenue. Deterministic and inline.
let table: Table =
  let rows =
    [ "North", "2026-01", 40, 820.0
      "North", "2026-02", 46, 930.0
      "North", "2026-03", 52, 1075.0
      "South", "2026-01", 31, 610.0
      "South", "2026-02", 28, 575.0
      "South", "2026-03", 35, 700.0
      "East", "2026-01", 55, 1150.0
      "East", "2026-02", 61, 1260.0
      "East", "2026-03", 58, 1190.0
      "West", "2026-01", 22, 450.0
      "West", "2026-02", 26, 520.0
      "West", "2026-03", 30, 615.0 ]

  let col build name values =
    build name (Vector.ofList values) AllValid

  { Schema =
      [ Field.create "region" StringType
        Field.create "month" StringType
        Field.create "units" IntType
        Field.create "revenue" FloatType ]
    Columns =
      [ col Fuaran.Core.Column.ofStrs "region" [ for r, _, _, _ in rows -> r ]
        col Fuaran.Core.Column.ofStrs "month" [ for _, m, _, _ in rows -> m ]
        col Fuaran.Core.Column.ofInts "units" [ for _, _, u, _ in rows -> u ]
        col Fuaran.Core.Column.ofFloats "revenue" [ for _, _, _, v in rows -> v ] ] }

/// Where the chart's rows come from: the inline table, or a table the host is
/// asked to resolve by name. Same pipeline either way.
[<RequireQualifiedAccess>]
type Source =
  | Inline
  | Named

/// The binding a visitor is editing: the source, two pipeline toggles, and the
/// two field names the chart plots.
type ChartBinding =
  { Source: Source
    Margin: bool
    ByRegion: bool
    XField: string
    YField: string }

/// Revenue by region over the grouped table: a sound binding, so the page opens
/// on a chart, and every way of breaking it is one control away.
let initial: ChartBinding =
  { Source = Source.Inline
    Margin = false
    ByRegion = true
    XField = "region"
    YField = "revenue" }

/// The names a visitor can pick from — every column some pipeline can produce,
/// plus one near-miss spelling, because a typo is the commonest broken binding.
let fieldChoices: string list =
  [ "region"; "month"; "units"; "revenue"; "margin"; "revenu" ]

/// The group-by runs first, so the margin is derived from whatever rows reach it:
/// the orders, or the regional totals.
let pipelineOf (b: ChartBinding) : Transform list =
  [ if b.ByRegion then
      GroupBy(
        [ "region" ],
        [ { Name = "revenue"
            Fn = AggFn.Sum
            Of = "revenue" }
          { Name = "units"
            Fn = AggFn.Sum
            Of = "units" } ]
      )
    if b.Margin then
      Derive("margin", Binary(Mul, Col "revenue", Lit(Cell.Float 0.3))) ]

[<Literal>]
let ChartId = "tr-chart"

/// The chart node exactly as an emitter would send it.
let chartNode (b: ChartBinding) : Node<obj> =
  let source =
    match b.Source with
    | Source.Inline -> DataSource.Embedded table
    | Source.Named -> DataSource.Ref "orders"

  Fuaran.chart
    ChartId
    { Defaults.chart<obj> with
        Kind = ChartKind.Bar
        Source = Binding.Transform(TransformSource.Data source, pipelineOf b, None)
        XField = b.XField
        YFields = [ b.YField ]
        Title = Some(TextSource.Literal(sprintf "%s by %s" b.YField b.XField)) }

/// The report's verdict on this chart: its check, or none when the report has
/// nothing to say about it (it always does for a chart; the option is the
/// report's own list shape).
let checkOf (b: ChartBinding) : PreEmitValidate.BindingCheck option =
  PreEmitValidate.bindingChecks (chartNode b)
  |> List.tryFind (fun c -> c.NodeId = ChartId)

/// The findings that refuse: errors only. A warning is advice, not a refusal.
let refusals (c: PreEmitValidate.BindingCheck) : PreEmitValidate.BindingDiagnostic list =
  c.Diagnostics |> List.filter (fun d -> d.Severity = Severity.Error)

let private gradeText (g: PreEmitValidate.BindingGrade) : string =
  match g with
  | PreEmitValidate.BindingGrade.Checked -> "checked: the produced columns are known exactly"
  | PreEmitValidate.BindingGrade.Unchecked reason ->
    "unchecked: "
    + (match reason with
       | PreEmitValidate.UncheckedReason.OpenSchema why -> why
       | PreEmitValidate.UncheckedReason.LiveSource -> "the source is live"
       | PreEmitValidate.UncheckedReason.NoStaticSchema kind -> "a " + kind + " source has no static schema"
       | PreEmitValidate.UncheckedReason.StaticRows -> "the rows are in the tree")

// ─── the page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private TypedRefusalView () : ReactElement =
  let b, setB = React.useState initial
  let tree = chartNode b
  let wire = CJson.encodeNode tree
  let check = checkOf b

  let fieldPicker (label: string) (value: string) (set: string -> unit) =
    Html.label
      [ prop.className "tr-field"
        prop.children
          [ Html.span [ prop.text label ]
            Html.select
              [ prop.value value
                prop.onChange (fun (v: string) -> set v)
                prop.children [ for f in fieldChoices -> Html.option [ prop.value f; prop.text f ] ] ] ] ]

  let toggle (label: string) (on: bool) (set: bool -> unit) =
    Html.label
      [ prop.className "tr-toggle"
        prop.children
          [ Html.input
              [ prop.type' "checkbox"
                prop.isChecked on
                prop.onChange (fun (v: bool) -> set v) ]
            Html.span [ prop.text label ] ] ]

  let sourceButton (s: Source) (label: string) =
    Html.button
      [ prop.className (
          if b.Source = s then
            "tr-source tr-source-on"
          else
            "tr-source"
        )
        prop.text label
        prop.onClick (fun _ -> setB { b with Source = s }) ]

  let controlsPanel =
    Exhibit.panel
      "Edit the binding"
      "Pick the fields the chart plots, and change the pipeline under them. Try plotting margin before it is derived, a month while the group-by has removed it, a region as the value, or the misspelling."
      [ Html.div
          [ prop.className "tr-controls"
            prop.children
              [ fieldPicker "x field" b.XField (fun v -> setB { b with XField = v })
                fieldPicker "value field" b.YField (fun v -> setB { b with YField = v })
                toggle "Derive margin = revenue × 0.3" b.Margin (fun v -> setB { b with Margin = v })
                toggle "Group by region" b.ByRegion (fun v -> setB { b with ByRegion = v }) ] ]
        Html.div
          [ prop.className "tr-sources"
            prop.children
              [ sourceButton Source.Inline "Source: the inline table"
                sourceButton Source.Named "Source: a table the host resolves by name" ] ] ]

  let schemaList (c: PreEmitValidate.BindingCheck) =
    if List.isEmpty c.Produced then
      Html.p
        [ prop.className "tr-schema-none"
          prop.text "No columns can be read off this source before it runs." ]
    else
      Html.ul
        [ prop.className "tr-schema"
          prop.children
            [ for col in c.Produced ->
                Html.li
                  [ Html.code [ prop.text col.Name ]
                    Html.span
                      [ prop.className "tr-type"
                        prop.text (defaultArg col.Type "type decided by the data") ] ] ] ]

  let finding (d: PreEmitValidate.BindingDiagnostic) =
    Html.li
      [ prop.className "tr-finding"
        prop.children
          [ Html.div
              [ prop.className "tr-finding-head"
                prop.children
                  [ Html.span [ prop.className "tr-code"; prop.text d.Code ]
                    Html.code [ prop.className "tr-path"; prop.text d.Path ] ] ]
            Html.p [ prop.className "tr-message"; prop.text d.Message ] ] ]

  let stage =
    match check with
    | None ->
      Html.p
        [ prop.className "tr-verdict"
          prop.text "The report returned nothing for this chart." ]
    | Some c ->
      let refused = refusals c

      Html.div
        [ prop.className "tr-stage"
          prop.children
            [ Html.p
                [ prop.className "tr-grade"
                  prop.children
                    [ Html.strong [ prop.text "Grade " ]
                      Html.span [ prop.text (gradeText c.Grade) ] ] ]
              match c.Grade, refused with
              | PreEmitValidate.BindingGrade.Checked, [] when b.Source = Source.Inline ->
                Html.div
                  [ prop.className "tr-chart"
                    prop.children [ Render.renderWithSources BindingResolver.empty ignore tree ] ]
              | PreEmitValidate.BindingGrade.Checked, [] ->
                // A group-by fixes the column set whatever the named table holds,
                // so the names are judged; the ROWS still need a host.
                Html.p
                  [ prop.className "tr-unchecked"
                    prop.text
                      "Every name checks: the group-by fixes which columns come out, whatever the named table holds, so the report can judge the binding without seeing a row. The rows themselves would come from the host. This page has none, so it draws no chart rather than invent one." ]
              | PreEmitValidate.BindingGrade.Checked, findings ->
                Html.div
                  [ prop.className "tr-refusal"
                    prop.role "alert"
                    prop.children
                      [ Html.h4 [ prop.text "Refused: this chart names a column its pipeline cannot produce" ]
                        Html.ul [ prop.className "tr-findings"; prop.children (List.map finding findings) ] ] ]
              | PreEmitValidate.BindingGrade.Unchecked _, _ ->
                Html.p
                  [ prop.className "tr-unchecked"
                    prop.text
                      "Nothing is refused, because nothing can be proved wrong: the columns depend on a table only the host can see. This page has no such host, so it renders nothing here rather than a chart it would have to invent rows for." ]
              Html.div
                [ prop.className "tr-produced"
                  prop.children [ Html.h4 [ prop.text "What the source produces" ]; schemaList c ] ] ] ]

  let verdictPanel =
    Exhibit.panel
      "Before anything renders"
      "The report runs over the chart's tree, which is the same tree the wire drawer shows. A finding names the slot by its path in that wire, beside the schema the pipeline really yields."
      [ stage
        Exhibit.wireDrawer "The chart's wire: the binding the report judged" wire ]

  Exhibit.shell
    "typed-refusal"
    "The Typed Refusal"
    "Break a chart's binding and get a refusal that names the slot, the code and the columns that do exist, instead of a blank chart."
    [ controlsPanel; verdictPanel ]
    [ Exhibit.Claim.Verified
        "The grade, the codes, the messages, the paths and the produced schema all come from the shipped binding-check report, run in your browser over the chart in the wire drawer."
      Exhibit.Claim.Verified
        "The produced schema is derived through the whole pipeline, so a derived column is grounded and a column the group-by removed is refused."
      Exhibit.Claim.Limit
        "Refusing to render on a finding is this page's choice of host behaviour. The report itself refuses nothing; a host decides what to do with it, and this one draws the report."
      Exhibit.Claim.Limit
        "A source whose columns are only known once it runs (a named table nothing narrows, a live store, a pivot) is reported unchecked and never refused. The report does not guess." ]

let page: ReactElement = TypedRefusalView()
