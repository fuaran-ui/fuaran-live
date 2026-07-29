module Fuaran.Showcase.Pandas

// ============================================================================
//  The Pandas Dashboard – four lines of Python, a serverless interactive
//  dashboard. Pillar: "one wire, many worlds".
//
//  A notebook cell runs real pandas over a bundled CSV IN THE BROWSER (Pyodide),
//  then authors a Fuaran tree with a small ergonomic surface and emits canonical
//  wire JSON. The F# host decodes that wire and renders the dashboard beside the
//  cell – no server, no JavaScript written by the author.
//
//  The centrepiece is diff-not-rerun: on a re-run the page derives the real
//  structural tree-diff between the previous tree and the new one
//  (`TreeOpStream.Replay.TreeOpDiff.diffBatched`, the fuaran-core#245 op-script
//  lineage) and shows the op ticker – the UI is PATCHED, not re-rendered. That is
//  what separates this from a Streamlit server that re-runs the whole script.
//
//  Honest scope: pandas + the authoring + the decode + the tree-diff all run for
//  real, client-side. Pyodide's cold start (~10 MB CPython + pandas) is lazy and
//  behind the Run button; heavy workloads are slow in-browser – the honesty note
//  points at the server-side fuaran-py path for those.
// ============================================================================

open Fable.Core.JsInterop
open Feliz
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops.Types

module Decode = Fuaran.UI.Ops.JsonDecode
module Diff = Fuaran.UI.OpStream.Replay.TreeOpDiff

let private runCellCb
  (code: string)
  (onProgress: string -> unit)
  (onOk: string -> unit)
  (onError: string -> unit)
  : unit =
  import "runCellCb" "./pandas-host.ts"

let private defaultCell =
  "df = pd.read_csv(\"sales.csv\")\n"
  + "totals = df.groupby(\"region\")[\"revenue\"].sum().sort_values(ascending=False)\n"
  + "\n"
  + "app = fuaran.dashboard(\"Regional revenue\",\n"
  + "    fuaran.metric_strip([(r, int(v)) for r, v in totals.items()]),\n"
  + "    fuaran.markdown(f\"**{totals.index[0]}** leads on revenue.\"),\n"
  + "    fuaran.grid(df.to_dict(\"records\")))"

// ─── op-ticker rendering ──────────────────────────────────────────────────────

let private opSummary (op: TreeOp<obj>) : string =
  let idOf (NodeId s) = s

  match op with
  | TreeOp.UpdateProp(n, path, _) -> sprintf "UpdateProp  %s.%s" (idOf n) path
  | TreeOp.ReplaceBinding(n, slot, _) -> sprintf "ReplaceBinding  %s.%s" (idOf n) slot
  | TreeOp.EditNode(n, _) -> sprintf "EditNode  %s" (idOf n)
  | TreeOp.UpdateStyle(n, _) -> sprintf "UpdateStyle  %s" (idOf n)
  | TreeOp.UpdateState(n, _) -> sprintf "UpdateState  %s" (idOf n)
  | TreeOp.InsertChild(p, c) -> sprintf "InsertChild  %s ← %s" (idOf p) c.Id
  | TreeOp.RemoveNode n -> sprintf "RemoveNode  %s" (idOf n)
  | TreeOp.MoveNode(n, p) -> sprintf "MoveNode  %s → %s" (idOf n) (idOf p)
  | TreeOp.ReorderChildren(p, _) -> sprintf "ReorderChildren  %s" (idOf p)
  | TreeOp.ReplaceRoot _ -> "ReplaceRoot"
  | TreeOp.Batch xs -> sprintf "Batch (%d)" (List.length xs)

// ─── The page ────────────────────────────────────────────────────────────────

[<RequireQualifiedAccess>]
type private Phase =
  | Idle
  | Running
  | Ready
  | Failed

[<ReactComponent>]
let private PandasView () : ReactElement =
  let code, setCode = React.useState defaultCell
  let phase, setPhase = React.useState Phase.Idle
  let progress, setProgress = React.useState ""
  let tree, setTree = React.useState (None: Node<obj> option)
  let ops, setOps = React.useState ([]: TreeOp<obj> list)
  let wire, setWire = React.useState ""
  let err, setErr = React.useState ""
  let ranOnce, setRanOnce = React.useState false

  let run () : unit =
    setPhase Phase.Running
    setErr ""

    runCellCb
      code
      (fun msg -> setProgress msg)
      (fun w ->
        match Decode.decodeNodeObj w with
        | Error e ->
          setErr (sprintf "%s at %s – %s" e.Code e.Path e.Message)
          setPhase Phase.Failed
        | Ok node ->
          let derived =
            match tree with
            | Some prev -> Diff.diffBatched prev node
            | None -> []

          // `diffBatched` wraps its op-script in a top-level `Batch`;
          // flatten it so the ticker lists each individual patch op.
          let flat =
            derived
            |> List.collect (fun op ->
              match op with
              | TreeOp.Batch xs -> xs
              | _ -> [ op ])

          setOps flat
          setTree (Some node)
          setWire w
          setRanOnce true
          setPhase Phase.Ready)
      (fun e ->
        setErr e
        setPhase Phase.Failed)

  let isRunning = phase = Phase.Running

  let cellPane =
    Html.div
      [ prop.className "pn-cell-pane"
        prop.children
          [ Html.div
              [ prop.className "pn-cell-head"
                prop.children
                  [ Html.span
                      [ prop.className "pn-cell-tag"
                        prop.text "In [1]: Python – pandas, in your browser" ] ] ]
            Html.textarea
              [ prop.className "pn-cell"
                prop.value code
                prop.rows 8
                prop.onChange (fun (v: string) -> setCode v) ]
            Html.div
              [ prop.className "pn-run-row"
                prop.children
                  [ Html.button
                      [ prop.className "pn-run-btn"
                        prop.disabled isRunning
                        prop.text (
                          match phase with
                          | Phase.Running -> "Running…"
                          | _ when ranOnce -> "Re-run ▸"
                          | _ -> "Run ▸"
                        )
                        prop.onClick (fun _ -> run ()) ] ] ]
            (match phase with
             | Phase.Running -> Html.div [ prop.className "pn-progress"; prop.text ("● " + progress) ]
             | Phase.Failed -> Html.div [ prop.className "pn-err"; prop.children [ Html.code [ prop.text err ] ] ]
             | _ -> Html.none) ] ]

  let renderedPane =
    Html.div
      [ prop.className "pn-render-pane"
        prop.children
          [ Html.span
              [ prop.className "pn-render-tag"
                prop.text "Out [1]: the dashboard (rendered from the wire the cell emitted)" ]
            Html.div
              [ prop.className "pn-stage"
                prop.children
                  [ match tree with
                    | Some node -> Render.renderWithSources BindingResolver.empty ignore node
                    | None ->
                      Html.div
                        [ prop.className "pn-empty"
                          prop.text "Run the cell – the first click downloads CPython + pandas (~10 MB), then renders." ] ] ] ] ]

  let ticker =
    if not ranOnce then
      Html.none
    else
      Html.div
        [ prop.className "pn-ticker"
          prop.children
            [ Html.div
                [ prop.className "pn-ticker-head"
                  prop.text (
                    if List.isEmpty ops then
                      "Op ticker – first render (the whole tree was emitted)"
                    else
                      sprintf "Op ticker – the UI was PATCHED with %d op(s), not re-rendered" (List.length ops)
                  ) ]
              (if List.isEmpty ops then
                 Html.none
               else
                 Html.ul
                   [ prop.className "pn-ops"
                     prop.children
                       [ for op in ops ->
                           Html.li
                             [ prop.className "pn-op"
                               prop.children [ Html.code [ prop.text (opSummary op) ] ] ] ] ]) ] ]

  let wireDrawer =
    if wire = "" then
      Html.none
    else
      Html.details
        [ prop.className "pn-wire"
          prop.children
            [ Html.summary [ prop.text "The wire JSON your cell emitted – paste it into any Fuaran host" ]
              Html.pre [ prop.className "wire-json"; prop.children [ Html.code [ prop.text wire ] ] ] ] ]

  let honesty =
    Html.div
      [ prop.className "pn-honesty"
        prop.children
          [ Html.h3 [ prop.text "Four lines, no server" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "Everything runs in your browser: real CPython and pandas via Pyodide compute over the bundled CSV, the ergonomic surface authors a Fuaran tree, and the F# host decodes the canonical wire and renders it. No server re-runs your script – Streamlit's model is exactly the thing this replaces." ]
                    Html.li
                      [ prop.text
                          "Re-run with a change and the page derives the real structural tree-diff between the old tree and the new one, then shows the op ticker: the UI is patched with a handful of typed operations, not re-rendered. That is the difference between an artefact that is data and a script that re-executes." ]
                    Html.li
                      [ prop.text
                          "Honest limits: Pyodide's cold start and heavy pandas workloads are slow in-browser, so this demo sizes its data to feel instant. For real workloads the same Python authoring runs server-side and streams ops to any host." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "The wire the cell emitted is your app – the same bytes any conformant host renders, the "
                            Html.a [ prop.href "#/pillar/wire"; prop.text "one-wire-many-worlds" ]
                            Html.text " thesis, authored from a notebook." ] ] ] ] ] ]

  Html.div
    [ prop.className "pn-page"
      prop.children
        [ Html.h1 [ prop.className "pn-title"; prop.text "The Pandas Dashboard" ]
          Html.p
            [ prop.className "pn-lede"
              prop.text
                "A data scientist types a few lines of Python. A live, interactive dashboard appears beside the cell – no JavaScript written, and no server running." ]
          Html.div [ prop.className "pn-split"; prop.children [ cellPane; renderedPane ] ]
          ticker
          wireDrawer
          honesty ] ]

let page: ReactElement = PandasView()
