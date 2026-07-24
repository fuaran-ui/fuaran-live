module Fuaran.Showcase.Rosetta

// ============================================================================
//  Rosetta – nine-host parity theatre. Pillar: "one wire, many worlds".
//
//  Ten authoring/consumption columns across nine host languages – F#, C#, Visual
//  Basic (in two dialects), TypeScript, Python, Go, Rust, Swift, Kotlin – over
//  ONE signature-bearing exemplar (a dashboard + a three-metric strip),
//  parameterised by six typed holes. Below: the single rendered app, and a
//  byte-parity strip in three honest tiers.
//
//  The honesty of the claim (stated in the footer): the source columns are
//  idiomatic *projections* with parameterised holes, not live compilers. The
//  parity strip is honestly tiered:
//    Tier 1 – independent live encoders, five implementations converging on one
//             hash, live:
//      • F#     – the REAL `Fuaran.UI` canonical encoder (CanonicalJson.encodeNode)
//                 compiled to JavaScript via Fable, hashed by the managed SHA-256.
//      • TS     – an independent canonical encoder (app/rosetta-hosts.ts), hashed
//                 by Web Crypto.
//      • Python – an independent canonical encoder run in CPython-on-WASM
//                 (Pyodide, lazy-loaded), hashed by hashlib.
//      • Rust   – the certified reference core compiled to wasm32, its additive
//                 `fuaran_rosetta_encode` export building its own tree from the
//                 six holes (eager, like TS).
//      • Go     – fuaran-go's stdlib-only codec compiled GOOS=js GOARCH=wasm,
//                 lazy-loaded behind a click (the Pyodide precedent).
//    Tier 2 – .NET authoring veneers: C# and Visual Basic build the identical
//             .NET tree, so their bytes are identical by construction.
//    Tier 3 – native render surfaces: Swift and Kotlin decode the same wire and
//             render it natively (SwiftUI / Compose). Decode-only by charter, so
//             they carry NO hash cell – emitting one would be a lie.
//  The "break it" vignette swaps the float rule in a naïve serialiser to show why
//  canonical bytes matter.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

// ─── The six typed holes (the edit-points) ──────────────────────────────────

type Holes =
  { LabelA: string
    ValueA: float
    LabelB: string
    ValueB: float
    LabelC: string
    ValueC: float }

let private defaultHoles: Holes =
  { LabelA = "Signups"
    ValueA = 1280.0
    LabelB = "Revenue"
    ValueB = 42.5
    LabelC = "Churn %"
    ValueC = 12.4 }

/// The plain JS object the TypeScript + Python hosts receive – camelCased to
/// match their `Holes` shape. Only these six scalars cross the language
/// boundary; each host builds its own tree from them.
let private holesObj (h: Holes) : obj =
  createObj
    [ "labelA" ==> h.LabelA
      "valueA" ==> h.ValueA
      "labelB" ==> h.LabelB
      "valueB" ==> h.ValueB
      "labelC" ==> h.LabelC
      "valueC" ==> h.ValueC ]

// ─── The exemplar tree (F# host – the single source of the render) ──────────
//  Built with the real smart-constructors so the metric nodes carry no
//  accessibility default (Accessibility = None) – the wire the three hosts
//  reproduce stays a small, faithful envelope.

let private metricNode (nid: string) (label: string) (value: float) : Node<unit> =
  { Id = NodeId nid
    Kind =
      NodeKind.Display(
        DisplayKind.Metric
          { Defaults.metric with
              Label = TextSource.Literal label
              Value = Binding.Static value }
      )
    State = Defaults.stateBehaviour
    Style = Defaults.style
    Accessibility = None
    Motion = None
    ExtraAttributes = None }

let private exemplarTree (h: Holes) : Node<unit> =
  Fuaran.box
    "rosetta-root"
    { Layout =
        BoxLayout.Flex
          { Direction = Vertical
            Wrap = false
            Gap = None }
      Role = BoxRole.Dashboard
      Heading = Some(TextSource.Literal "Revenue snapshot")
      Children =
        [ Fuaran.box
            "rosetta-strip"
            { Layout =
                BoxLayout.Flex
                  { Direction = Horizontal
                    Wrap = true
                    Gap = None }
              Role = BoxRole.Group
              Heading = None
              Children =
                [ metricNode "rosetta-m-a" h.LabelA h.ValueA
                  metricNode "rosetta-m-b" h.LabelB h.ValueB
                  metricNode "rosetta-m-c" h.LabelC h.ValueC ] } ] }

// ─── Interop into the TS + Python hosts (app/rosetta-hosts.ts) ──────────────

let private encodeWireTs: obj -> string = import "encodeWireTs" "./rosetta-hosts.ts"

let private encodeWireNaive: obj -> string =
  import "encodeWireNaive" "./rosetta-hosts.ts"

let private sha256HexCb (input: string) (cb: string -> unit) : unit =
  import "sha256HexCb" "./rosetta-hosts.ts"

let private ensurePythonCb (onReady: unit -> unit) (onError: string -> unit) : unit =
  import "ensurePythonCb" "./rosetta-hosts.ts"

let private pythonComputeCb (holes: obj) (onOk: System.Func<string, string, unit>) (onError: string -> unit) : unit =
  import "pythonComputeCb" "./rosetta-hosts.ts"

let private ensureRustCb (onReady: unit -> unit) (onError: string -> unit) : unit =
  import "ensureRustCb" "./rosetta-hosts.ts"

let private rustComputeCb (holes: obj) (onOk: System.Func<string, string, unit>) (onError: string -> unit) : unit =
  import "rustComputeCb" "./rosetta-hosts.ts"

let private ensureGoCb (onReady: unit -> unit) (onError: string -> unit) : unit =
  import "ensureGoCb" "./rosetta-hosts.ts"

let private goComputeCb (holes: obj) (onOk: System.Func<string, string, unit>) (onError: string -> unit) : unit =
  import "goComputeCb" "./rosetta-hosts.ts"

[<Emit("String($0)")>]
let private numText (n: float) : string = jsNative

[<Emit("Number($0)")>]
let private jsNumber (s: string) : float = jsNative

[<Emit("Number.isFinite($0)")>]
let private jsFinite (n: float) : bool = jsNative

// ─── Projected source columns (idiomatic authoring, hole-parameterised) ─────
//  Display-only. What a human would write in each language to build this tree –
//  the hole values are spliced in live. The wire hash, not this text, is what
//  the parity strip computes.

let private fsSource (h: Holes) : string =
  "open Fuaran\n"
  + "open Fuaran.UI.Types\n\n"
  + "// A dashboard with a three-metric strip – one Fuaran tree.\n"
  + "let dashboard =\n"
  + "    Fuaran.box \"rosetta-root\"\n"
  + "        { Layout = Flex Vertical\n"
  + "          Role = Dashboard\n"
  + "          Heading = Some (text \"Revenue snapshot\")\n"
  + "          Children =\n"
  + "            [ Fuaran.box \"rosetta-strip\"\n"
  + "                { Layout = Flex Horizontal; Role = Group\n"
  + "                  Children =\n"
  + "                    [ Fuaran.metric \"m-a\" { label \""
  + h.LabelA
  + "\"; value "
  + numText h.ValueA
  + " }\n"
  + "                      Fuaran.metric \"m-b\" { label \""
  + h.LabelB
  + "\"; value "
  + numText h.ValueB
  + " }\n"
  + "                      Fuaran.metric \"m-c\" { label \""
  + h.LabelC
  + "\"; value "
  + numText h.ValueC
  + " } ] } ] }\n"

let private tsSource (h: Holes) : string =
  "import { box, metric, Flex, Role } from '@fuaran-ui/ui';\n\n"
  + "// The same tree, authored in TypeScript.\n"
  + "const dashboard = box('rosetta-root', {\n"
  + "  layout: Flex.Vertical,\n"
  + "  role: Role.Dashboard,\n"
  + "  heading: 'Revenue snapshot',\n"
  + "  children: [\n"
  + "    box('rosetta-strip', { layout: Flex.Horizontal, role: Role.Group, children: [\n"
  + "      metric('m-a', { label: '"
  + h.LabelA
  + "', value: "
  + numText h.ValueA
  + " }),\n"
  + "      metric('m-b', { label: '"
  + h.LabelB
  + "', value: "
  + numText h.ValueB
  + " }),\n"
  + "      metric('m-c', { label: '"
  + h.LabelC
  + "', value: "
  + numText h.ValueC
  + " }),\n"
  + "    ] }),\n"
  + "  ],\n"
  + "});\n"

let private pySource (h: Holes) : string =
  "from fuaran_py.ui import box, metric, Flex, Role\n\n"
  + "# The same tree, authored in Python.\n"
  + "dashboard = box(\"rosetta-root\",\n"
  + "    layout=Flex.VERTICAL, role=Role.DASHBOARD, heading=\"Revenue snapshot\",\n"
  + "    children=[\n"
  + "        box(\"rosetta-strip\", layout=Flex.HORIZONTAL, role=Role.GROUP, children=[\n"
  + "            metric(\"m-a\", label=\""
  + h.LabelA
  + "\", value="
  + numText h.ValueA
  + "),\n"
  + "            metric(\"m-b\", label=\""
  + h.LabelB
  + "\", value="
  + numText h.ValueB
  + "),\n"
  + "            metric(\"m-c\", label=\""
  + h.LabelC
  + "\", value="
  + numText h.ValueC
  + "),\n"
  + "        ]),\n"
  + "    ])\n"

let private csSource (h: Holes) : string =
  "using static Fuaran.UI.CSharp.Fuaran;\n\n"
  + "// The same tree, authored in C#.\n"
  + "var dashboard = Box(new()\n"
  + "{\n"
  + "    Id = \"rosetta-root\", Layout = Flex.Vertical,\n"
  + "    Role = BoxRole.Dashboard, Heading = \"Revenue snapshot\",\n"
  + "    Children =\n"
  + "    [\n"
  + "        Box(new()\n"
  + "        {\n"
  + "            Id = \"rosetta-strip\", Layout = Flex.Horizontal, Role = BoxRole.Group,\n"
  + "            Children =\n"
  + "            [\n"
  + "                Metric(new() { Id = \"m-a\", Label = \""
  + h.LabelA
  + "\", Value = "
  + numText h.ValueA
  + " }),\n"
  + "                Metric(new() { Id = \"m-b\", Label = \""
  + h.LabelB
  + "\", Value = "
  + numText h.ValueB
  + " }),\n"
  + "                Metric(new() { Id = \"m-c\", Label = \""
  + h.LabelC
  + "\", Value = "
  + numText h.ValueC
  + " }),\n"
  + "            ],\n"
  + "        }),\n"
  + "    ],\n"
  + "});\n\n"
  + "string wireJson = Encode(dashboard);\n"

let private vbSource (h: Holes) : string =
  "Imports Fuaran.UI.VisualBasic\n\n"
  + "' The same tree, authored in Visual Basic (XML literals).\n"
  + "Dim dashboard = <Box id=\"rosetta-root\" layout=\"Flex.Vertical\"\n"
  + "                     role=\"Dashboard\" heading=\"Revenue snapshot\">\n"
  + "                    <Box id=\"rosetta-strip\" layout=\"Flex.Horizontal\" role=\"Group\">\n"
  + "                        <Metric id=\"m-a\" label=\""
  + h.LabelA
  + "\" value=\""
  + numText h.ValueA
  + "\"/>\n"
  + "                        <Metric id=\"m-b\" label=\""
  + h.LabelB
  + "\" value=\""
  + numText h.ValueB
  + "\"/>\n"
  + "                        <Metric id=\"m-c\" label=\""
  + h.LabelC
  + "\" value=\""
  + numText h.ValueC
  + "\"/>\n"
  + "                    </Box>\n"
  + "                </Box>\n\n"
  + "Dim wireJson As String = FuaranXml.Encode(dashboard)\n"

// The second Visual Basic dialect: the fluent factory (the same .NET authoring
// surface C# calls) driven with VB's `With {}` object initialisers – a distinct
// idiom, the same tree, the same bytes.
let private vbFluentSource (h: Holes) : string =
  "Imports Fuaran.UI.CSharp\n\n"
  + "' The same tree, authored in Visual Basic (fluent factory).\n"
  + "Dim dashboard = Fuaran.Box(New BoxOptions With {\n"
  + "    .Id = \"rosetta-root\", .Orientation = Orientation.Vertical,\n"
  + "    .Role = BoxRoleKind.Dashboard, .Heading = \"Revenue snapshot\",\n"
  + "    .Children = {\n"
  + "        Fuaran.Box(New BoxOptions With {\n"
  + "            .Id = \"rosetta-strip\", .Orientation = Orientation.Horizontal,\n"
  + "            .Role = BoxRoleKind.Group, .Wrap = True,\n"
  + "            .Children = {\n"
  + "                Fuaran.Metric(New MetricOptions With {.Id = \"m-a\", .Label = \""
  + h.LabelA
  + "\", .Value = "
  + numText h.ValueA
  + "}),\n"
  + "                Fuaran.Metric(New MetricOptions With {.Id = \"m-b\", .Label = \""
  + h.LabelB
  + "\", .Value = "
  + numText h.ValueB
  + "}),\n"
  + "                Fuaran.Metric(New MetricOptions With {.Id = \"m-c\", .Label = \""
  + h.LabelC
  + "\", .Value = "
  + numText h.ValueC
  + "})\n"
  + "            }\n"
  + "        })\n"
  + "    }\n"
  + "})\n\n"
  + "Dim wireJson As String = Wire.Encode(dashboard)\n"

let private goSource (h: Holes) : string =
  "package main\n\n"
  + "import \"github.com/fuaran-ui/fuaran-go/wire\"\n\n"
  + "// The same tree, authored in Go.\n"
  + "func metric(id, label string, value float64) wire.Node {\n"
  + "    return wire.Node{ID: id, Kind: wire.Obj{Tag: \"Metric\", Fields: map[string]wire.Value{\n"
  + "        \"label\": wire.Str(label),\n"
  + "        \"value\": wire.Obj{Tag: \"Static\", Fields: map[string]wire.Value{\"value\": wire.Float(value)}},\n"
  + "    }}}\n"
  + "}\n\n"
  + "strip := wire.Node{ID: \"rosetta-strip\", Kind: wire.Obj{Tag: \"Box\", Fields: map[string]wire.Value{\n"
  + "    \"role\": wire.Str(\"Group\"),\n"
  + "    \"layout\": wire.Obj{Tag: \"Flex\", Fields: map[string]wire.Value{\"direction\": wire.Str(\"Horizontal\"), \"wrap\": wire.Bool(true)}},\n"
  + "    \"children\": wire.Arr{\n"
  + "        metric(\"m-a\", \""
  + h.LabelA
  + "\", "
  + numText h.ValueA
  + "),\n"
  + "        metric(\"m-b\", \""
  + h.LabelB
  + "\", "
  + numText h.ValueB
  + "),\n"
  + "        metric(\"m-c\", \""
  + h.LabelC
  + "\", "
  + numText h.ValueC
  + "),\n"
  + "    },\n"
  + "}}}\n"
  + "dashboard := wire.Node{ID: \"rosetta-root\", Kind: wire.Obj{Tag: \"Box\", Fields: map[string]wire.Value{\n"
  + "    \"role\": wire.Str(\"Dashboard\"), \"heading\": wire.Str(\"Revenue snapshot\"),\n"
  + "    \"layout\": wire.Obj{Tag: \"Flex\", Fields: map[string]wire.Value{\"direction\": wire.Str(\"Vertical\"), \"wrap\": wire.Bool(false)}},\n"
  + "    \"children\": wire.Arr{strip},\n"
  + "}}}\n\n"
  + "wireJSON, _ := wire.EncodeNode(dashboard)\n"

let private rustSource (h: Holes) : string =
  "use fuaran_rs::canonical::JVal;\n"
  + "use fuaran_rs::wire::{encode_node, Binding, BoxLayout, BoxRole, BoxSpec,\n"
  + "    MetricSpec, Node, NodeKind, Orientation, StaticValue, TextSource};\n\n"
  + "// The same tree, authored in Rust (native enums, exhaustive by construction).\n"
  + "fn metric(id: &str, label: &str, value: f64) -> Node {\n"
  + "    Node {\n"
  + "        id: id.into(),\n"
  + "        kind: NodeKind::Metric(MetricSpec {\n"
  + "            label: TextSource::Literal(label.into()),\n"
  + "            value: Binding::Static { value: StaticValue::Ast(JVal::Num(value)) },\n"
  + "            ..MetricSpec::default()\n"
  + "        }),\n"
  + "        ..Node::default()\n"
  + "    }\n"
  + "}\n\n"
  + "fn flex_box(id: &str, dir: Orientation, wrap: bool, role: BoxRole,\n"
  + "            heading: Option<&str>, children: Vec<Node>) -> Node {\n"
  + "    Node {\n"
  + "        id: id.into(),\n"
  + "        kind: NodeKind::Box(BoxSpec {\n"
  + "            children,\n"
  + "            heading: heading.map(|h| TextSource::Literal(h.into())),\n"
  + "            layout: BoxLayout::Flex { direction: dir, gap: None, wrap },\n"
  + "            role,\n"
  + "        }),\n"
  + "        ..Node::default()\n"
  + "    }\n"
  + "}\n\n"
  + "let strip = flex_box(\"rosetta-strip\", Orientation::Horizontal, true, BoxRole::Group, None, vec![\n"
  + "    metric(\"m-a\", \""
  + h.LabelA
  + "\", "
  + numText h.ValueA
  + "),\n"
  + "    metric(\"m-b\", \""
  + h.LabelB
  + "\", "
  + numText h.ValueB
  + "),\n"
  + "    metric(\"m-c\", \""
  + h.LabelC
  + "\", "
  + numText h.ValueC
  + "),\n"
  + "]);\n"
  + "let dashboard = flex_box(\"rosetta-root\", Orientation::Vertical, false,\n"
  + "    BoxRole::Dashboard, Some(\"Revenue snapshot\"), vec![strip]);\n\n"
  + "let wire_json = encode_node(&dashboard);\n"

// The Swift + Kotlin columns are CONSUMPTION projections: a native surface over
// the Rust reference core decodes the wire and renders it. They never encode – so
// on the parity strip they carry the tier-3 badge and no hash cell.
let private swiftSource (h: Holes) : string =
  "import Fuaran  // native Swift surface over the Rust reference core\n\n"
  + "// Decode-only: the Rust core owns the wire; SwiftUI renders the projection.\n"
  + "let session = try FuaranSession(wire: rosettaWire)\n\n"
  + "// The SwiftUI render arm over the decoded dashboard tree.\n"
  + "var body: some View {\n"
  + "    Dashboard(\"Revenue snapshot\") {\n"
  + "        HStack {\n"
  + "            Metric(\""
  + h.LabelA
  + "\", value: "
  + numText h.ValueA
  + ")\n"
  + "            Metric(\""
  + h.LabelB
  + "\", value: "
  + numText h.ValueB
  + ")\n"
  + "            Metric(\""
  + h.LabelC
  + "\", value: "
  + numText h.ValueC
  + ")\n"
  + "        }\n"
  + "    }\n"
  + "}\n"

let private ktSource (h: Holes) : string =
  "import ui.fuaran.FuaranSession  // native Kotlin surface over the Rust core\n\n"
  + "// Decode-only: the Rust core owns the wire; Compose renders the projection.\n"
  + "val session = FuaranSession(rosettaWire)\n\n"
  + "// The Jetpack Compose render arm over the decoded dashboard tree.\n"
  + "@Composable\n"
  + "fun Dashboard() = Column {\n"
  + "    Text(\"Revenue snapshot\", style = MaterialTheme.typography.titleMedium)\n"
  + "    Row {\n"
  + "        Metric(\""
  + h.LabelA
  + "\", value = "
  + numText h.ValueA
  + ")\n"
  + "        Metric(\""
  + h.LabelB
  + "\", value = "
  + numText h.ValueB
  + ")\n"
  + "        Metric(\""
  + h.LabelC
  + "\", value = "
  + numText h.ValueC
  + ")\n"
  + "    }\n"
  + "}\n"

// ─── Wire diff (first divergent byte + a context window) ────────────────────

let private firstDiff (a: string) (b: string) : int option =
  let n = min a.Length b.Length
  let mutable i = 0
  let mutable found = -1

  while found < 0 && i < n do
    if a.[i] <> b.[i] then
      found <- i

    i <- i + 1

  if found >= 0 then Some found
  elif a.Length <> b.Length then Some n
  else None

let private windowAround (s: string) (idx: int) : string =
  let lo = max 0 (idx - 32)
  let hi = min s.Length (idx + 32)
  let prefix = if lo > 0 then "…" else ""
  let suffix = if hi < s.Length then "…" else ""
  prefix + s.Substring(lo, hi - lo) + suffix

// ─── The public wire-format profile the page demonstrates ───────────────────

let private wireProfile = "fuaran-wire/1 · canonical-json"

// ─── Python host lifecycle ──────────────────────────────────────────────────

[<RequireQualifiedAccess>]
type private HostPhase =
  | NotLoaded
  | Loading
  | Ready
  | Failed of string

// ─── View helpers ────────────────────────────────────────────────────────────

let private shortHash (h: string) : string =
  if h.Length > 16 then h.Substring(0, 16) + "…" else h

/// One parity-strip row: the language, a status dot, the short hash, and an
/// expandable drawer with the full hash, the host's wire, and a byte-diff vs
/// the F# reference.
let private hostRow
  (lang: string)
  (mechanism: string)
  (reference: string)
  (hostWire: string option)
  (hostHash: string option)
  (pending: string option)
  : ReactElement =
  let statusClass, statusText, hashText =
    match pending, hostHash with
    | Some note, _ -> "rosetta-dot rosetta-dot-idle", note, "–"
    | None, None -> "rosetta-dot rosetta-dot-idle", "computing…", "–"
    | None, Some h ->
      let ok = (hostWire = Some reference)

      (if ok then
         "rosetta-dot rosetta-dot-ok"
       else
         "rosetta-dot rosetta-dot-bad"),
      (if ok then "matches" else "diverges"),
      shortHash h

  let drawer =
    match hostWire, hostHash with
    | Some w, Some h ->
      let diffChild =
        match firstDiff reference w with
        | None ->
          Html.p
            [ prop.className "rosetta-diff-ok"
              prop.text "0 bytes differ – identical to the F# reference wire." ]
        | Some idx ->
          Html.div
            [ prop.className "rosetta-diff-bad"
              prop.children
                [ Html.p [ prop.text (sprintf "First divergence at byte %d:" idx) ]
                  Html.div
                    [ prop.className "rosetta-diff-line"
                      prop.children
                        [ Html.span [ prop.className "rosetta-diff-label"; prop.text "reference " ]
                          Html.code [ prop.text (windowAround reference idx) ] ] ]
                  Html.div
                    [ prop.className "rosetta-diff-line"
                      prop.children
                        [ Html.span [ prop.className "rosetta-diff-label"; prop.text (lang + " ") ]
                          Html.code [ prop.text (windowAround w idx) ] ] ] ] ]

      Html.details
        [ prop.className "rosetta-host-drawer"
          prop.children
            [ Html.summary [ prop.text "wire + byte-diff" ]
              Html.div [ prop.className "rosetta-fullhash"; prop.text ("sha-256  " + h) ]
              diffChild
              Html.pre [ prop.className "rosetta-wire"; prop.text w ] ] ]
    | _ -> Html.none

  Html.div
    [ prop.className "rosetta-host-row"
      prop.children
        [ Html.div
            [ prop.className "rosetta-host-main"
              prop.children
                [ Html.span [ prop.className statusClass ]
                  Html.span [ prop.className "rosetta-host-lang"; prop.text lang ]
                  Html.span [ prop.className "rosetta-host-mech"; prop.text mechanism ]
                  Html.span [ prop.className "rosetta-host-status"; prop.text statusText ]
                  Html.code [ prop.className "rosetta-host-hash"; prop.text hashText ] ] ]
          drawer ] ]

/// A tier subheading in the parity strip – the honest grouping the footer
/// explains (independent encoders / .NET veneers / native render surfaces).
let private tierLabel (text: string) : ReactElement =
  Html.p [ prop.className "rosetta-tier"; prop.text text ]

/// A Tier-3 native-surface row: the language, its render arm, and the badge that
/// states what it is – a decode-only projection over the Rust reference core.
/// **No hash cell** – these surfaces never emit canonical bytes, so a hash would
/// be a lie; the wire they render is the Rust hash above.
let private nativeRow (lang: string) (mechanism: string) : ReactElement =
  Html.div
    [ prop.className "rosetta-host-row"
      prop.children
        [ Html.div
            [ prop.className "rosetta-host-main"
              prop.children
                [ Html.span [ prop.className "rosetta-dot rosetta-dot-idle" ]
                  Html.span [ prop.className "rosetta-host-lang"; prop.text lang ]
                  Html.span [ prop.className "rosetta-host-mech"; prop.text mechanism ]
                  Html.span
                    [ prop.className "rosetta-badge"
                      prop.text "native surface over the Rust reference core – renders the Rust hash above" ] ] ] ] ]

// ─── The page (a Feliz function component with its own hooks) ───────────────

[<ReactComponent>]
let private RosettaView () : ReactElement =
  let holes, setHoles = React.useState defaultHoles
  let tsHash, setTsHash = React.useState (None: string option)
  let naiveHash, setNaiveHash = React.useState (None: string option)
  let pyPhase, setPyPhase = React.useState HostPhase.NotLoaded
  let pyResult, setPyResult = React.useState (None: (string * string) option)
  let goPhase, setGoPhase = React.useState HostPhase.NotLoaded
  let goResult, setGoResult = React.useState (None: (string * string) option)
  let rustPhase, setRustPhase = React.useState HostPhase.NotLoaded
  let rustResult, setRustResult = React.useState (None: (string * string) option)
  let breakIt, setBreakIt = React.useState false
  // Which language tab is showing. The rendered emission + parity strip below are
  // language-independent, so switching tabs only changes which idiom you read.
  let activeLang, setActiveLang = React.useState 0

  let ho = holesObj holes
  let fsWire = CanonicalJson.encodeNode (exemplarTree holes)
  let fsHash = Hashing.sha256Hex fsWire
  let tsWire = encodeWireTs ho
  let naiveWire = encodeWireNaive ho

  // TypeScript + naïve hashes (Web Crypto) recompute on every edit.
  React.useEffect (
    (fun () ->
      setTsHash None
      setNaiveHash None
      sha256HexCb tsWire (fun h -> setTsHash (Some h))
      sha256HexCb naiveWire (fun h -> setNaiveHash (Some h))),
    [| box holes |]
  )

  // Python host: recompute whenever it is Ready and the holes change.
  React.useEffect (
    (fun () ->
      match pyPhase with
      | HostPhase.Ready ->
        setPyResult None

        pythonComputeCb ho (System.Func<_, _, _>(fun w h -> setPyResult (Some(w, h)))) (fun e ->
          setPyPhase (HostPhase.Failed e))
      | _ -> ()),
    [| box holes; box pyPhase |]
  )

  // Rust host: load eagerly on mount (like TS), then recompute on every edit.
  React.useEffect (
    (fun () ->
      setRustPhase HostPhase.Loading
      ensureRustCb (fun () -> setRustPhase HostPhase.Ready) (fun e -> setRustPhase (HostPhase.Failed e))),
    [||]
  )

  React.useEffect (
    (fun () ->
      match rustPhase with
      | HostPhase.Ready ->
        setRustResult None

        rustComputeCb ho (System.Func<_, _, _>(fun w h -> setRustResult (Some(w, h)))) (fun e ->
          setRustPhase (HostPhase.Failed e))
      | _ -> ()),
    [| box holes; box rustPhase |]
  )

  // Go host: lazy behind a click (the ~4 MB module), then recompute like Python.
  React.useEffect (
    (fun () ->
      match goPhase with
      | HostPhase.Ready ->
        setGoResult None

        goComputeCb ho (System.Func<_, _, _>(fun w h -> setGoResult (Some(w, h)))) (fun e ->
          setGoPhase (HostPhase.Failed e))
      | _ -> ()),
    [| box holes; box goPhase |]
  )

  let labelInput (value: string) (onSet: string -> unit) : ReactElement =
    Html.input
      [ prop.className "rosetta-hole rosetta-hole-text"
        prop.type' "text"
        prop.value value
        prop.onChange (fun (v: string) -> onSet v) ]

  let valueInput (value: float) (onSet: float -> unit) : ReactElement =
    Html.input
      [ prop.className "rosetta-hole rosetta-hole-num"
        prop.type' "number"
        prop.value (numText value)
        prop.onChange (fun (v: string) ->
          let n = jsNumber v

          if jsFinite n then
            onSet n) ]

  let holeRow
    (tag: string)
    (label: string)
    (onLabel: string -> unit)
    (value: float)
    (onValue: float -> unit)
    : ReactElement =
    Html.div
      [ prop.className "rosetta-hole-row"
        prop.children
          [ Html.span [ prop.className "rosetta-hole-tag"; prop.text tag ]
            labelInput label onLabel
            valueInput value onValue ] ]

  let editPanel =
    Html.div
      [ prop.className "rosetta-edit-panel"
        prop.children
          [ Html.p
              [ prop.className "rosetta-edit-legend"
                prop.text "Edit points – change a metric and watch every host re-agree." ]
            holeRow "metric A" holes.LabelA (fun v -> setHoles { holes with LabelA = v }) holes.ValueA (fun v ->
              setHoles { holes with ValueA = v })
            holeRow "metric B" holes.LabelB (fun v -> setHoles { holes with LabelB = v }) holes.ValueB (fun v ->
              setHoles { holes with ValueB = v })
            holeRow "metric C" holes.LabelC (fun v -> setHoles { holes with LabelC = v }) holes.ValueC (fun v ->
              setHoles { holes with ValueC = v }) ] ]

  let renderedApp =
    Html.div
      [ prop.className "rosetta-render"
        prop.children [ Render.renderWithSources BindingResolver.empty ignore (exemplarTree holes) ] ]

  // The idiomatic authorings as a tab group – pick your language and edit a
  // hole; the code re-projects here while the render + parity strip below (both
  // language-independent) follow. Beats a ten-high scroll of stacked columns.
  // Visual Basic is shown in two dialects (fluent factory + XML literals); the
  // last two columns are native render surfaces (consumption, not encoding).
  let langTabs =
    let langs =
      [ "F#", "Fuaran.UI", fsSource holes
        "C#", "Fuaran.UI.CSharp", csSource holes
        "Visual Basic", "Fuaran.UI.CSharp", vbFluentSource holes
        "Visual Basic (XML)", "Fuaran.UI.VisualBasic", vbSource holes
        "TypeScript", "@fuaran-ui/ui", tsSource holes
        "Python", "fuaran_py.ui", pySource holes
        "Go", "fuaran-go", goSource holes
        "Rust", "fuaran-rs", rustSource holes
        "Swift", "fuaran-swift (SwiftUI)", swiftSource holes
        "Kotlin", "fuaran-kt (Compose)", ktSource holes ]

    let _, activeTag, activeSrc = List.item activeLang langs

    Html.div
      [ prop.className "rosetta-tabs"
        prop.children
          [ Html.div
              [ prop.className "rosetta-tablist"
                prop.role "tablist"
                prop.children
                  [ for i, (name, _, _) in List.indexed langs do
                      let isActive = i = activeLang

                      Html.button
                        [ prop.className (
                            if isActive then
                              "rosetta-tab rosetta-tab-active"
                            else
                              "rosetta-tab"
                          )
                          prop.role "tab"
                          prop.ariaSelected isActive
                          prop.text name
                          prop.onClick (fun _ -> setActiveLang i) ] ] ]
            Html.div
              [ prop.className "rosetta-tabpanel"
                prop.children
                  [ Html.span [ prop.className "rosetta-tabpanel-tag"; prop.text activeTag ]
                    Html.pre
                      [ prop.className "rosetta-src"
                        prop.children [ Html.code [ prop.text activeSrc ] ] ] ] ] ] ]

  let pythonPending, pythonWire, pythonHash =
    match pyPhase, pyResult with
    | HostPhase.NotLoaded, _ -> Some "click “Run the Python host” →", None, None
    | HostPhase.Loading, _ -> Some "loading CPython (Pyodide, ~10 MB)…", None, None
    | HostPhase.Failed e, _ -> Some("failed: " + e), None, None
    | HostPhase.Ready, None -> Some "encoding…", None, None
    | HostPhase.Ready, Some(w, h) -> None, Some w, Some h

  let rustPending, rustWire, rustHash =
    match rustPhase, rustResult with
    | HostPhase.NotLoaded, _
    | HostPhase.Loading, _ -> Some "loading fuaran-rs (certified core, ~1 MB wasm)…", None, None
    | HostPhase.Failed e, _ -> Some("failed: " + e), None, None
    | HostPhase.Ready, None -> Some "encoding…", None, None
    | HostPhase.Ready, Some(w, h) -> None, Some w, Some h

  let goPending, goWire, goHash =
    match goPhase, goResult with
    | HostPhase.NotLoaded, _ -> Some "click “Run the Go host” →", None, None
    | HostPhase.Loading, _ -> Some "loading fuaran-go codec (~4 MB wasm)…", None, None
    | HostPhase.Failed e, _ -> Some("failed: " + e), None, None
    | HostPhase.Ready, None -> Some "encoding…", None, None
    | HostPhase.Ready, Some(w, h) -> None, Some w, Some h

  let pythonButton =
    match pyPhase with
    | HostPhase.NotLoaded ->
      Html.button
        [ prop.className "rosetta-py-btn"
          prop.text "Run the Python host"
          prop.onClick (fun _ ->
            setPyPhase HostPhase.Loading
            ensurePythonCb (fun () -> setPyPhase HostPhase.Ready) (fun e -> setPyPhase (HostPhase.Failed e))) ]
    | HostPhase.Loading ->
      Html.span
        [ prop.className "rosetta-py-note"
          prop.text "Downloading CPython-on-WebAssembly…" ]
    | HostPhase.Failed _ ->
      Html.button
        [ prop.className "rosetta-py-btn"
          prop.text "Retry the Python host"
          prop.onClick (fun _ ->
            setPyPhase HostPhase.Loading
            ensurePythonCb (fun () -> setPyPhase HostPhase.Ready) (fun e -> setPyPhase (HostPhase.Failed e))) ]
    | HostPhase.Ready ->
      Html.span
        [ prop.className "rosetta-py-note"
          prop.text "CPython running in your browser ✓" ]

  let goButton =
    match goPhase with
    | HostPhase.NotLoaded ->
      Html.button
        [ prop.className "rosetta-py-btn"
          prop.text "Run the Go host"
          prop.onClick (fun _ ->
            setGoPhase HostPhase.Loading
            ensureGoCb (fun () -> setGoPhase HostPhase.Ready) (fun e -> setGoPhase (HostPhase.Failed e))) ]
    | HostPhase.Loading ->
      Html.span
        [ prop.className "rosetta-py-note"
          prop.text "Downloading the fuaran-go WebAssembly module…" ]
    | HostPhase.Failed _ ->
      Html.button
        [ prop.className "rosetta-py-btn"
          prop.text "Retry the Go host"
          prop.onClick (fun _ ->
            setGoPhase HostPhase.Loading
            ensureGoCb (fun () -> setGoPhase HostPhase.Ready) (fun e -> setGoPhase (HostPhase.Failed e))) ]
    | HostPhase.Ready ->
      Html.span
        [ prop.className "rosetta-py-note"
          prop.text "fuaran-go running in your browser ✓" ]

  let parityStrip =
    Html.div
      [ prop.className "rosetta-parity"
        prop.children
          [ Html.h3 [ prop.className "rosetta-parity-title"; prop.text "Byte-parity strip" ]
            Html.p
              [ prop.className "rosetta-parity-sub"
                prop.text
                  "One canonical wire, three honest tiers. Tier 1: five independent encoders (F#, TypeScript, Python, Rust, Go) each recompute the bytes and converge on one hash. Tier 2: the C# and Visual Basic veneers build the identical .NET tree, so their bytes match by construction. Tier 3: the Swift and Kotlin native surfaces decode and render the same wire – they never encode, so they carry no hash." ]
            tierLabel "Tier 1 · independent live encoders"
            hostRow "F#" "real Fuaran.UI encoder · Fable → JS · managed SHA-256" fsWire (Some fsWire) (Some fsHash) None
            hostRow "TypeScript" "independent encoder · Web Crypto SHA-256" fsWire (Some tsWire) tsHash None
            hostRow
              "Python"
              "independent encoder · Pyodide (CPython/WASM) · hashlib"
              fsWire
              pythonWire
              pythonHash
              pythonPending
            hostRow
              "Rust"
              "certified reference core · fuaran-rs, wasm32 · independent canonical encode"
              fsWire
              rustWire
              rustHash
              rustPending
            hostRow
              "Go"
              "fuaran-go codec · GOOS=js GOARCH=wasm · independent canonical encode"
              fsWire
              goWire
              goHash
              goPending
            tierLabel "Tier 2 · .NET authoring veneers"
            hostRow
              "C#"
              "authoring veneer · builds the identical .NET tree → same bytes by construction"
              fsWire
              (Some fsWire)
              (Some fsHash)
              None
            hostRow
              "Visual Basic"
              "authoring veneer · fluent factory and XML literals both lower to the identical .NET tree → same bytes"
              fsWire
              (Some fsWire)
              (Some fsHash)
              None
            tierLabel "Tier 3 · native render surfaces"
            nativeRow "Swift" "SwiftUI render arm · decodes the wire, renders natively"
            nativeRow "Kotlin" "Jetpack Compose render arm · decodes the wire, renders natively"
            (if breakIt then
               hostRow "Naïve serialiser" "same tree, non-canonical float rule" fsWire (Some naiveWire) naiveHash None
             else
               Html.none)
            Html.div [ prop.className "rosetta-py-launch"; prop.children [ goButton; pythonButton ] ] ] ]

  let breakItPanel =
    Html.div
      [ prop.className "rosetta-breakit"
        prop.children
          [ Html.button
              [ prop.className (
                  if breakIt then
                    "rosetta-breakit-btn rosetta-breakit-on"
                  else
                    "rosetta-breakit-btn"
                )
                prop.text (
                  if breakIt then
                    "Hide the naïve serialiser"
                  else
                    "Break it – add a naïve serialiser"
                )
                prop.onClick (fun _ -> setBreakIt (not breakIt)) ]
            (if breakIt then
               Html.p
                 [ prop.className "rosetta-breakit-note"
                   prop.text
                     "The naïve host builds the same tree but formats floats with a fixed one-decimal rule (1280 → \"1280.0\"). Canonical bytes demand the shortest round-tripping form (1280 → \"1280\"). One wrong byte, one broken hash – that is exactly what the conformance corpus enforces." ]
             else
               Html.none) ] ]

  let footer =
    Html.div
      [ prop.className "rosetta-honesty"
        prop.children
          [ Html.h3 [ prop.text "How honest is this?" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "The language tabs are idiomatic projections with parameterised edit-points – not live in-browser compilers. Editing a value re-projects each and re-encodes the wire. Visual Basic is shown in two dialects (the fluent factory and XML literals) – one language, two ways to author the same tree." ]
                    Html.li
                      [ prop.text
                          "The F# hash is computed by the real Fuaran.UI canonical encoder compiled to JavaScript via Fable. The parity claim is about the wire bytes, not about shipping an F# compiler to the browser." ]
                    Html.li
                      [ prop.text
                          "Tier 1 – independent live encoders. TypeScript, Python, Rust, and Go are from-scratch encoders that re-derive the same bytes: TypeScript in Web Crypto, Python as CPython compiled to WebAssembly (Pyodide), Rust as the certified reference core compiled to wasm32 (its additive encode export builds its own tree from the six holes), and Go as its stdlib-only codec compiled GOOS=js GOARCH=wasm. Five implementations, one set of bytes, live." ]
                    Html.li
                      [ prop.text
                          "Tier 2 – .NET authoring veneers. C# (Fuaran.UI.CSharp) and Visual Basic (both the fluent factory and the XML literals) lower to the identical Fuaran.UI tree, so their wire is byte-identical to F# by construction – not an independent re-implementation. Author in the .NET language and idiom you already know; the bytes do not change." ]
                    Html.li
                      [ prop.text
                          "Tier 3 – native render surfaces. Swift and Kotlin are native surfaces over the Rust reference core: they decode the wire and render it (SwiftUI / Compose) but never emit canonical bytes, so they carry no hash cell – the wire they render is the Rust hash above. Giving a decode-only surface a hash would be a lie." ]
                    Html.li
                      [ prop.children
                          [ Html.text (
                              "Wire format profile: "
                              + wireProfile
                              + ". The full specification and its cross-host conformance corpus live at "
                            )
                            Html.a
                              [ prop.href "https://fuaran-ui.io"
                                prop.target "_blank"
                                prop.rel "noreferrer"
                                prop.text "fuaran-ui.io" ]
                            Html.text "." ] ] ] ] ] ]

  let portabilityNote =
    Html.p
      [ prop.className "rosetta-portability"
        prop.children
          [ Html.strong [ prop.text "Design it once, run it anywhere. " ]
            Html.text
              "Because the app is a portable wire value – not framework-bound code – the same design renders on any conformant host, from a .NET or Python service to the browser, and outlives any single framework." ] ]

  Html.div
    [ prop.className "rosetta-page"
      prop.children
        [ Html.h1 [ prop.className "rosetta-title"; prop.text "Rosetta" ]
          Html.p
            [ prop.className "rosetta-lede"
              prop.text
                "One app, expressed across nine host languages – F#, C#, Visual Basic, TypeScript, Python, Go, Rust, Swift, and Kotlin – and the wire bytes are identical wherever they are computed. Pick a language, edit a value, and watch every host follow." ]
          editPanel
          langTabs
          Html.div
            [ prop.className "rosetta-stage"
              prop.children
                [ Html.h3 [ prop.className "rosetta-stage-title"; prop.text "The one rendered app" ]
                  renderedApp ] ]
          parityStrip
          portabilityNote
          breakItPanel
          footer ] ]

let page: ReactElement = RosettaView()
