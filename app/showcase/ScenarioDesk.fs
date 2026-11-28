module Fuaran.Showcase.ScenarioDesk

// ============================================================================
//  The Scenario Desk — move a parameter, compare two scenarios, copy the view
//  (Phase 2759).
//
//  The claim: a "what if" is a pipeline with named parameters, and a reader's
//  refinement of a table is a set of values over a fixed pipeline. Both are
//  data, so the page can say which sites read each parameter, and a copied
//  view pasted back gives the same table.
//
//  Every part of the desk is a certified fragment from the published library
//  (`Fuaran.UI.Fragments`), built through its own F# authoring forms and never
//  copied: the parameter panel (`Stdlib.parameterPanel`), the two-scenario
//  comparison (`Stdlib.scenarioComparisonOf`) and the refinable table
//  (`Stdlib.refinableTableOf`). The two totals beside the comparison call the
//  same model function the comparison calls, once per scenario.
//
//  Which sites read a parameter is the language tier's static read walk
//  (`BindingWalk.reactiveRenderFacts`, projected on the filter channel by
//  `BindingWalk.readerMap`), over the tree the page renders. It says which sites
//  READ a parameter. It times nothing and counts no re-evaluation.
//
//  The copied view is `Stdlib.refinedPipeline`: the table's fixed pipeline with
//  the chosen filters written in as literals, then the sort and the page as
//  literal steps. Nothing in the library turns that pipeline back into the
//  table's view state, so the paste path here applies the pasted pipeline to
//  the same rows, and compares the result with what the live table shows.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Fragments
open Fuaran.UI.Renderer

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── the data ────────────────────────────────────────────────────────────────

/// A bakery's week: each line's item, its category, the units it sells in a
/// week and its unit price. Deterministic and inline; nothing is fetched.
let lineRows: (string * string * int * float) list =
  [ "Sourdough", "Bread", 120, 4.5
    "Rye", "Bread", 60, 4.0
    "Baguette", "Bread", 150, 2.2
    "Focaccia", "Bread", 45, 3.8
    "Croissant", "Pastry", 200, 1.9
    "Pain au chocolat", "Pastry", 140, 2.1
    "Cinnamon bun", "Pastry", 90, 2.6
    "Scone", "Bake", 80, 1.8
    "Flapjack", "Bake", 70, 1.5
    "Brownie", "Bake", 110, 2.4 ]

let lines: Table =
  let col build name values =
    build name (Vector.ofList values) AllValid

  { Schema =
      [ Field.create "item" StringType
        Field.create "category" StringType
        Field.create "units" IntType
        Field.create "price" FloatType ]
    Columns =
      [ col Fuaran.Core.Column.ofStrs "item" [ for i, _, _, _ in lineRows -> i ]
        col Fuaran.Core.Column.ofStrs "category" [ for _, c, _, _ in lineRows -> c ]
        col Fuaran.Core.Column.ofInts "units" [ for _, _, u, _ in lineRows -> u ]
        col Fuaran.Core.Column.ofFloats "price" [ for _, _, _, p in lineRows -> p ] ] }

let private source: DataSource = DataSource.Embedded lines

let categories: string list = [ "Bread"; "Pastry"; "Bake" ]

// ─── the parameters ──────────────────────────────────────────────────────────

/// One parameter the panel moves: the filter key its control writes, its label,
/// its bounds and step, and the value it starts at.
type Parameter =
  { Key: string
    Label: string
    Min: float
    Max: float
    Step: float
    Start: float }

let parameters: Parameter list =
  [ { Key = "a-growth"
      Label = "Steady: weekly growth"
      Min = -0.2
      Max = 0.5
      Step = 0.01
      Start = 0.0 }
    { Key = "b-growth"
      Label = "Busy season: weekly growth"
      Min = -0.2
      Max = 0.5
      Step = 0.01
      Start = 0.15 }
    { Key = "weeks"
      Label = "Weeks planned"
      Min = 1.0
      Max = 13.0
      Step = 1.0
      Start = 4.0 } ]

let parameterByKey (key: string) : Parameter option =
  parameters |> List.tryFind (fun p -> p.Key = key)

/// The filter a parameter's control writes, read with its starting value.
let private reads (key: string) : Binding<JVal> =
  let start =
    parameterByKey key |> Option.map (fun p -> p.Start) |> Option.defaultValue 0.0

  Binding.Filter(key, Some(JFloat start))

/// The library's parameter panel, bound to the three parameters.
let panel: Node<obj> =
  Stdlib.parameterPanel<obj>
    .Materialize(
      Map.ofList
        [ yield "heading", box "Assumptions"
          yield "count", box (List.length parameters)
          for k, p in List.indexed parameters do
            let hole (field: string) = sprintf "param-%d-%s" (k + 1) field
            yield hole "name", box p.Key
            yield hole "label", box p.Label
            yield hole "control", box "number"
            yield hole "min", box p.Min
            yield hole "max", box p.Max
            yield hole "step", box p.Step
            yield hole "default", box p.Start ]
    )

// ─── the model and the two scenarios ─────────────────────────────────────────

/// One model, run per line: the week's units, grown by `growth`, at the line's
/// price, over `weeks` weeks.
let model: FnDef =
  { Name = "takings"
    Params =
      [ { Name = "units"; Default = None }
        { Name = "price"; Default = None }
        { Name = "growth"; Default = None }
        { Name = "weeks"; Default = None } ]
    Body =
      Binary(
        Mul,
        Binary(
          Mul,
          Binary(Mul, Cast(FloatType, Arg "units"), Binary(Add, Lit(Cell.Float 1.0), Arg "growth")),
          Arg "price"
        ),
        Arg "weeks"
      ) }

let scenarioModel: ScenarioModel =
  { Source = source
    Key = "item"
    Model = model
    Columns = [ "units", "units"; "price", "price" ] }

let steady: ScenarioSide =
  { Label = "Steady"
    Parameters = [ "growth", reads "a-growth"; "weeks", reads "weeks" ] }

let busy: ScenarioSide =
  { Label = "Busy season"
    Parameters = [ "growth", reads "b-growth"; "weeks", reads "weeks" ] }

let comparison: Result<Node<obj>, string> =
  Stdlib.scenarioComparisonOf<obj> "desk-comparison" "Two scenarios, one model" scenarioModel steady busy

/// A scenario's total: the same model called once per line under the side's
/// own parameters, summed. Expanded as the comparison's pipeline is, so a host
/// that does not carry function definitions reads it.
let private totalPipeline: Result<Transform list, string> =
  [ Define model
    Derive("takings", Call(model.Name, [ Col "units"; Col "price"; ColExpr.Param "growth"; ColExpr.Param "weeks" ]))
    GroupBy(
      [],
      [ { Name = "takings"
          Fn = AggFn.Sum
          Of = "takings" } ]
    ) ]
  |> FnDef.expand
  |> Result.mapError (sprintf "the total does not expand: %A")

let private gbp: CellFormat = CellFormat.Currency("GBP", Some 2, None)

let private total (id: string) (side: ScenarioSide) : Result<Node<obj>, string> =
  totalPipeline
  |> Result.map (fun pipeline ->
    Fuaran.metric
      id
      { Defaults.metric with
          Label = TextSource.Literal(side.Label + " total")
          Value =
            Binding.Transform(
              TransformSource.Data source,
              pipeline,
              Some [ for name, from in side.Parameters -> ({ Name = name; From = from }: TransformParam) ]
            )
          Format = gbp })

// ─── the refinable table ─────────────────────────────────────────────────────

let tableId: string = "desk-lines"

let table: RefinableTable =
  { Source = source
    Key = "item"
    Columns =
      [ { Field = "item"
          Label = "Item"
          Kind = RefinableKind.Text
          Sortable = true
          Filter = None }
        { Field = "category"
          Label = "Category"
          Kind = RefinableKind.Text
          Sortable = true
          Filter =
            Some
              [ for c in categories ->
                  ({ Label = TextSource.Literal c
                     Value = c }
                  : SelectOption) ] }
        { Field = "units"
          Label = "Units a week"
          Kind = RefinableKind.Number
          Sortable = true
          Filter = None }
        { Field = "price"
          Label = "Price"
          Kind = RefinableKind.Number
          Sortable = true
          Filter = None } ]
    PageSize = 4 }

let refinable: Result<Node<obj>, string> =
  Stdlib.refinableTableOf<obj> tableId "The week's lines" SchemaWalk.noSources table

// ─── the desk ────────────────────────────────────────────────────────────────

/// The whole desk as one tree: the panel, the two totals, the comparison and
/// the table. Refused, with the library's reason, if a fragment is.
let desk: Result<Node<obj>, string> =
  match comparison, refinable, total "desk-total-a" steady, total "desk-total-b" busy with
  | Ok c, Ok r, Ok a, Ok b ->
    Ok(
      Fuaran.stack
        "scenario-desk"
        { Defaults.stack with
            Children =
              [ panel
                Fuaran.stack
                  "desk-totals"
                  { Defaults.stack with
                      Children = [ a; b ] }
                c
                r ] }
    )
  | Error e, _, _, _
  | _, Error e, _, _
  | _, _, Error e, _
  | _, _, _, Error e -> Error e

// ─── which sites read each parameter ─────────────────────────────────────────

/// Every rendered reader in the desk and the filter keys it reads, from the
/// tier's static read walk.
let readerEntries (tree: Node<obj>) : (string * Set<string> * bool) list =
  BindingWalk.reactiveRenderFacts tree
  |> snd
  |> List.map (fun e -> e.Node.Id, e.Keys.Filter, e.Opaque)

/// For each filter key the desk's tree reads, the ids of the sites that read it.
let readersByKey (tree: Node<obj>) : Map<string, Set<string>> =
  BindingWalk.reactiveRenderFacts tree
  |> snd
  |> BindingWalk.readerMap (fun k -> k.Filter)

/// The sites that read `key`, in a stable order.
let readersOf (tree: Node<obj>) (key: string) : string list =
  readersByKey tree
  |> Map.tryFind key
  |> Option.map Set.toList
  |> Option.defaultValue []

/// The readers whose state access the walk cannot see. On this desk there are
/// none, so the read sets above are complete.
let opaqueReaders (tree: Node<obj>) : string list =
  (BindingWalk.collect tree).StateKeys.OpaqueReaders
  |> List.map (function
    | BindingWalk.OpaqueReaderAt.Computed reader -> reader
    | BindingWalk.OpaqueReaderAt.Custom nodeId -> nodeId)

/// The readers that read none of the three parameters.
let readersOfNoParameter (tree: Node<obj>) : string list =
  let keys = parameters |> List.map (fun p -> p.Key) |> Set.ofList

  readerEntries tree
  |> List.filter (fun (_, filters, _) -> Set.isEmpty (Set.intersect filters keys))
  |> List.map (fun (id, _, _) -> id)
  |> List.distinct

/// What a site is called on the page.
let siteName (id: string) : string =
  match id with
  | "parameter-panel-controls" -> "the parameter's own control, which shows its value"
  | "desk-comparison-grid" -> "the comparison grid"
  | "desk-total-a" -> "the Steady total"
  | "desk-total-b" -> "the Busy season total"
  | "desk-lines-grid" -> "the week's lines table"
  | "desk-lines-filters" -> "the table's category filter"
  | other -> other

// ─── the view, copied and pasted ─────────────────────────────────────────────

/// The refinable table's grid spec, from the desk's tree.
let private gridSpec (tree: Node<obj>) : DataGridSpec<obj> option =
  match Fuaran.UI.Ops.Introspect.findNode (NodeId(tableId + "-grid")) tree with
  | Some n ->
    match n.Kind with
    | NodeKind.DataGrid spec -> Some spec
    | _ -> None
  | None -> None

/// A filter's selection as the strings it holds; an unset filter is none.
let private selection (sources: BindingSources) (key: string) : string list =
  match Map.tryFind key sources.Filters |> Option.bind BindingResolver.jvalOfResolved with
  | Some(JArr items) ->
    items
    |> List.choose (function
      | JStr s -> Some s
      | _ -> None)
  | Some(JStr s) when s <> "" -> [ s ]
  | _ -> []

let private rowsOf (sources: BindingSources) (binding: Binding<Row seq>) : Result<Row list, string> =
  match BindingResolver.resolveRowsWithFields sources binding with
  | BindingResolver.Resolved rows, _ -> Ok(Seq.toList rows)
  | BindingResolver.Errored m, _ -> Error m
  | _, _ -> Error "the rows did not resolve"

/// What the live table shows under `sources`, through the renderer's own grid
/// model (the rows sorted by the effective descriptor, sliced to the clamped
/// page), and the view the page reads off the same state.
let shownAndView (tree: Node<obj>) (sources: BindingSources) : Result<Row list * RefinableView, string> =
  match gridSpec tree with
  | None -> Error "the table has no grid"
  | Some spec ->
    rowsOf sources spec.Source
    |> Result.bind (fun rows ->
      let descriptor =
        BindingResolver.effectiveSortDescriptor spec.SortStateKey spec.DefaultSort sources

      let sorted = GridModel.sortRowsByDescriptor spec.Columns descriptor rows

      match BindingResolver.gridPage sources spec (List.length sorted) with
      | None -> Error "the table is not paged"
      | Some(_, _, page, _, _) ->
        let filters =
          [ for c in table.Columns do
              if Option.isSome c.Filter then
                c.Field, selection sources (Stdlib.refinableFilterKey tableId c.Field) ]

        Ok(
          GridModel.sliceRowsToPage (defaultArg spec.PageSize 0) page sorted,
          { Sort = descriptor
            Page = page
            Filters = filters }
        ))

/// The view as the canonical pipeline the page copies.
let copiedView (view: RefinableView) : string =
  DataFrameCodec.encodePipeline (Stdlib.refinedPipeline tableId table view)

/// Rows as canonical bytes: the form "byte for byte" is checked in.
let rowsWire (rows: Row list) : string =
  Canonical.write Rendering.CrossHost (RowCodec.encodeRows (rows |> Seq.ofList))

/// The live table's rows under `sources`, as canonical bytes.
let shownWire (tree: Node<obj>) (sources: BindingSources) : Result<string, string> =
  shownAndView tree sources |> Result.map (fst >> rowsWire)

/// A pasted view applied to the same rows. The pasted text must decode as a
/// pipeline and read no parameter: a copied view has every value written in.
let applyPasted (text: string) : Result<Row list, string> =
  match DataFrameCodec.decodePipeline text with
  | Error e -> Error("that is not a pipeline: " + ColumnCodec.errorString e)
  | Ok pipeline ->
    match Transform.paramsOf pipeline with
    | [] -> rowsOf BindingResolver.empty (Binding.Transform(TransformSource.Data source, pipeline, None))
    | names ->
      Error(
        "a copied view has every value written in, and this one still reads "
        + String.concat ", " names
      )

/// The pasted rows as a grid with the live table's own columns.
let private pastedGrid (tree: Node<obj>) (text: string) : Node<obj> option =
  match gridSpec tree, DataFrameCodec.decodePipeline text with
  | Some spec, Ok pipeline ->
    Some
      { Fuaran.stack "desk-pasted-grid" Defaults.stack with
          Kind =
            NodeKind.DataGrid
              { spec with
                  Source = Binding.Transform(TransformSource.Data source, pipeline, None)
                  SortStateKey = None
                  PageStateKey = None
                  PageSize = None }
          Accessibility = Defaults.Accessibility.grid }
  | _ -> None

// ─── the words ───────────────────────────────────────────────────────────────

let emptyReadoutHeading: string = "Nothing moved yet"

let emptyReadoutBody: string =
  sprintf
    "The %d parameters start where a planner might: steady trade with no growth, a busy season %.0f%% up, over %.0f weeks. Move one and this panel names the sites that read it; the rest of the desk reads nothing it writes."
    (List.length parameters)
    (parameters[1].Start * 100.0)
    parameters[2].Start

let emptyPasteNote: string =
  "Copy the table's view, change the sort, the page or the category, then paste the copied view back here. It is applied to the same rows, and the page says whether the result is the table you copied, byte for byte."

let whatIfContrast: string =
  "The What-If Machine branches the tree: each scenario is a different interface. Here the tree is fixed and only its parameters move."

// ─── the page ────────────────────────────────────────────────────────────────

let private currentSources () : BindingSources =
  { BindingResolver.empty with
      State = StateStore.snapshot ()
      Filters = FilterStore.snapshot () }

let private parameterValue (sources: BindingSources) (p: Parameter) : string =
  match Map.tryFind p.Key sources.Filters |> Option.bind BindingResolver.jvalOfResolved with
  | Some v -> Canonical.write Rendering.CrossHost v
  | None -> string p.Start

let private readerList (tree: Node<obj>) (key: string) : ReactElement =
  Html.ul
    [ prop.className "sd-readers"
      prop.children
        [ for id in readersOf tree key -> Html.li [ Html.code [ prop.text id ]; Html.text (" " + siteName id) ] ] ]

[<ReactComponent>]
let private ScenarioDeskView (tree: Node<obj>) : ReactElement =
  let watched =
    Set.ofList (
      (parameters |> List.map (fun p -> p.Key))
      @ [ for c in table.Columns do
            if Option.isSome c.Filter then
              Stdlib.refinableFilterKey tableId c.Field ]
    )

  FilterStore.useFilterKeys watched |> ignore

  StateStore.useStateKeys (Set.ofList [ Stdlib.refinableSortKey tableId; Stdlib.refinablePageKey tableId ])
  |> ignore

  let sources = currentSources ()

  let values =
    parameters |> List.map (fun p -> p.Key, parameterValue sources p) |> Map.ofList

  let last = React.useRef values
  let moved, setMoved = React.useState (None: string option)

  React.useEffect (
    (fun () ->
      let changed =
        parameters
        |> List.tryFind (fun p -> Map.tryFind p.Key last.current <> Map.tryFind p.Key values)

      last.current <- values

      match changed with
      | Some p -> setMoved (Some p.Key)
      | None -> ()),
    [| box (String.concat "|" (Map.toList values |> List.map snd)) |]
  )

  let copied, setCopied = React.useState (None: string option)
  let pasted, setPasted = React.useState ""
  let applied, setApplied = React.useState (None: string option)

  let readout =
    match moved |> Option.bind parameterByKey with
    | None ->
      Exhibit.panel
        "Who reads what"
        ""
        [ Html.div
            [ prop.className "sd-empty"
              prop.children
                [ Html.p [ prop.className "sd-empty-heading"; prop.text emptyReadoutHeading ]
                  Html.p [ prop.text emptyReadoutBody ] ] ]
          for p in parameters do
            Html.h5 [ prop.className "sd-subhead"; prop.text (p.Label + " is read by") ]
            readerList tree p.Key ]
    | Some p ->
      let readers = readersOf tree p.Key

      Exhibit.panel
        ("You moved " + p.Label)
        (sprintf
          "Now %s. These %d sites read it, from the tree's own read walk; nothing else on the desk reads what it writes."
          (parameterValue sources p)
          (List.length readers))
        [ readerList tree p.Key
          Html.h5 [ prop.className "sd-subhead"; prop.text "Read by none of the parameters" ]
          Html.ul
            [ prop.className "sd-readers"
              prop.children
                [ for id in readersOfNoParameter tree ->
                    Html.li [ Html.code [ prop.text id ]; Html.text (" " + siteName id) ] ] ] ]

  let viewPanel =
    let current = shownAndView tree sources

    let copy () =
      match current with
      | Ok(_, view) ->
        let text = copiedView view
        Fuaran.Live.Interop.writeClipboard text |> ignore
        setCopied (Some text)
      | Error _ -> ()

    let verdict =
      match applied with
      | None -> Html.p [ prop.className "sd-note"; prop.text emptyPasteNote ]
      | Some text ->
        match applyPasted text, shownWire tree sources with
        | Error e, _ -> Html.p [ prop.className "sd-refusal"; prop.text e ]
        | Ok rows, Ok live ->
          Html.div
            [ prop.children
                [ Html.p
                    [ prop.className (if rowsWire rows = live then "sd-match" else "sd-differs")
                      prop.text (
                        if rowsWire rows = live then
                          sprintf
                            "The pasted view gives %d rows, byte for byte the rows the table shows now."
                            (List.length rows)
                        else
                          sprintf
                            "The pasted view gives %d rows, and they differ from what the table shows now: the view has changed since it was copied."
                            (List.length rows)
                      ) ]
                  match pastedGrid tree text with
                  | Some g -> Exhibit.renderStatic g
                  | None -> Html.none ] ]
        | Ok _, Error e -> Html.p [ prop.className "sd-refusal"; prop.text e ]

    Exhibit.panel
      "The view, as data"
      "The table's sort, page and category filter are values the table holds. Copy them as the one pipeline that describes the view."
      [ Html.div
          [ prop.className "sd-actions"
            prop.children
              [ Html.button
                  [ prop.className "sd-button"
                    prop.text "Copy this view"
                    prop.onClick (fun _ -> copy ()) ]
                Html.button
                  [ prop.className "sd-button"
                    prop.disabled copied.IsNone
                    prop.text "Paste the copied view below"
                    prop.onClick (fun _ -> copied |> Option.iter setPasted) ] ] ]
        match copied with
        | Some text -> Exhibit.wireDrawer "The copied view" text
        | None -> Html.none
        Html.textarea
          [ prop.className "sd-paste"
            prop.ariaLabel "A view to apply"
            prop.placeholder "Paste a copied view here"
            prop.value pasted
            prop.onChange (fun (v: string) -> setPasted v) ]
        Html.button
          [ prop.className "sd-button"
            prop.disabled (pasted.Trim() = "")
            prop.text "Apply the pasted view"
            prop.onClick (fun _ -> setApplied (Some pasted)) ]
        verdict ]

  Exhibit.shell
    "scenario-desk"
    "The Scenario Desk"
    ("Move a parameter and watch two scenarios of one plan diverge, line by line, with their difference. Then sort and filter the week's lines and copy the view as the pipeline that describes it. "
     + whatIfContrast)
    [ Html.div [ prop.className "sd-desk"; prop.children [ Exhibit.renderLive tree ] ]
      Html.div [ prop.className "sd-split"; prop.children [ readout; viewPanel ] ]
      Exhibit.wireDrawer "The desk, as canonical bytes" (CJson.encodeNode tree) ]
    [ Exhibit.Claim.Verified
        "The panel, the comparison and the table are the published fragment library's own, built through its authoring forms. Both scenarios run one model; only their parameters differ."
      Exhibit.Claim.Verified
        "The readout names the sites that read a parameter, from the language tier's read walk over the tree on this page. The test suite moves each parameter and holds every other site's value still."
      Exhibit.Claim.Verified
        "The copied view is one pipeline with every value written in. The test suite pastes it back over the same rows and holds the result to the rows the table shows, byte for byte."
      Exhibit.Claim.Limit
        "The readout says which sites read a parameter. It does not time the update or count re-evaluations."
      Exhibit.Claim.Limit
        "A pasted view is applied to the rows; it does not move the live table's own sort, page or filter back, because nothing turns a pipeline back into those values." ]

let private refused (reason: string) : ReactElement =
  Exhibit.shell
    "scenario-desk"
    "The Scenario Desk"
    "The desk could not be built from the fragment library."
    [ Html.p [ prop.className "sd-refusal"; prop.text reason ] ]
    []

let page: ReactElement =
  match desk with
  | Ok tree -> ScenarioDeskView tree
  | Error reason -> refused reason
