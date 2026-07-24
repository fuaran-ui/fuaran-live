module Fuaran.Showcase.Relay

// ============================================================================
//  The Relay – one app crossing four runtimes with its hash chain intact.
//  Pillar: "one wire, many worlds" (Rosetta in motion).
//
//  A four-station relay track: an app is authored (Python), edited by an AI
//  (TypeScript), healed after an assertion catches a planted defect (.NET/F#),
//  and delivered as a crawlable document + email digest (Server). Under the
//  track runs the chain ribbon – every station's ops are real hash-chained
//  `OpRecord`s folded through the shipped apply engine, and the shipped
//  `Verify.chain` re-runs green at each hand-off.
//
//  The parity seal is the genuinely-polyglot beat: at each station the canonical
//  wire head hash is recomputed by THREE independent SHA-256 implementations –
//  the F# managed digest (always), TypeScript's Web Crypto (always), and
//  Python's hashlib in CPython-on-WASM (on demand) – and they agree, byte for
//  byte. Four languages play telephone; nothing is lost.
//
//  Honest scope (stated in the footer): the relay's spine – encode / apply /
//  hash-chain / Verify.chain – is the real `Fuaran.UI` engine compiled to
//  JavaScript via Fable; it is not four separate engines shipped to the browser.
//  The wire, the op protocol, and the chain are host-neutral, and THAT is the
//  claim the relay dramatises. The genuinely-per-host computation on the page is
//  the hash agreement; the four source panels are idiomatic per-host projections
//  (Rosetta's posture, in motion). Nothing here needs a server.
// ============================================================================

open System
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

module Apply = Fuaran.UI.Ops.Apply
module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

[<Emit("String($0)")>]
let private numText (n: float) : string = jsNative

let private wireStr (s: string) : PropValue = PropValue.Wire(JStr s)

let private shortHash (h: string) : string =
  if h.Length > 12 then h.Substring(0, 12) else h

let private renderNode (n: Node<unit>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

// ─── The artefact + the scripted, hash-chained op flow ───────────────────────

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

// An empty root the genesis op replaces – so station 1 (Python authoring the
// base app) is itself a real chain link, not an untracked starting state.
let private seedTree: Node<unit> =
  Fuaran.box
    "rl-seed"
    { Layout =
        BoxLayout.Flex
          { Direction = Vertical
            Wrap = false
            Gap = None }
      Role = BoxRole.Group
      Heading = None
      Children = [] }

let private genesisTree: Node<unit> =
  Fuaran.box
    "rl-root"
    { Layout =
        BoxLayout.Flex
          { Direction = Vertical
            Wrap = false
            Gap = Some 14 }
      Role = BoxRole.Dashboard
      Heading = Some(TextSource.Literal "Q3 revenue")
      Children =
        [ Fuaran.box
            "rl-strip"
            { Layout =
                BoxLayout.Flex
                  { Direction = Horizontal
                    Wrap = true
                    Gap = Some 12 }
              Role = BoxRole.Group
              Heading = None
              Children =
                [ metricNode "rl-signups" "Signups" 1280.0
                  metricNode "rl-revenue" "Revenue £k" 42.5 ] }
          Fuaran.callout
            "rl-note"
            { Defaults.callout with
                Tone = ToneVariant.Subdued
                Heading = Some(TextSource.Literal "Headline")
                Body = TextSource.Literal "Draft – awaiting the trend line." } ] }

let private trendMetric: Node<unit> = metricNode "rl-trend" "Trend %" 8.4

[<RequireQualifiedAccess>]
type private Host =
  | Python
  | TypeScript
  | DotNet
  | Server

type private RelayOp =
  { Op: TreeOp<unit>
    Host: Host
    Actor: Actor
    Note: string }

let private author = Actor.Human "data"
let private assistant = Actor.Agent("assistant", "1", "author")
let private healer = Actor.Agent("healer", "1", "repair")

// The scripted flow. Station 1 authors; station 2 (two ops) adds a trend column
// and drafts a headline – but leaves a placeholder, the planted defect; station
// 3 heals it after a real assertion catches it. Station 4 (Server) projects the
// verified tree – no op, delivery preserves the bytes.
let private ops: RelayOp list =
  [ { Op = TreeOp.ReplaceRoot genesisTree
      Host = Host.Python
      Actor = author
      Note = "Author the base dashboard from the Q3 dataset" }
    { Op = TreeOp.InsertChild(NodeId "rl-strip", 2, trendMetric)
      Host = Host.TypeScript
      Actor = assistant
      Note = "Add a QoQ trend column" }
    { Op = TreeOp.UpdateProp(NodeId "rl-note", "Body", wireStr "TODO – write the headline")
      Host = Host.TypeScript
      Actor = assistant
      Note = "Draft the headline (left a placeholder)" }
    { Op =
        TreeOp.UpdateProp(
          NodeId "rl-note",
          "Body",
          wireStr "Revenue up 18% QoQ; the new trend column confirms momentum."
        )
      Host = Host.DotNet
      Actor = healer
      Note = "Assertion caught the placeholder – heal it" } ]

let private opCount = List.length ops

// Fold the SHIPPED apply engine from the seed – genuinely reconstructs the tree
// after any prefix of the flow.
let private treeAfter (n: int) : Node<unit> =
  (seedTree, ops |> List.truncate n)
  ||> List.fold (fun tree o ->
    match Apply.apply o.Op tree with
    | Ok next -> next
    | Error _ -> tree)

// ─── The genuine hash-chained op-records + Verify.chain ──────────────────────

let private streamId = "relay-q3"
let private baseTs = 1719792000L

let private records: OpRecord<unit> list =
  (([], HashChain.genesisPreviousHash), List.indexed ops)
  ||> List.fold (fun (acc, prev) (i, o) ->
    let seq = i + 1
    let ts = DateTimeOffset.FromUnixTimeSeconds(baseTs + int64 i * 3600L)
    let h = HashChain.computeHash prev o.Op seq ts o.Actor None OpResultEnvelope.Success

    let record: OpRecord<unit> =
      { StreamId = streamId
        Sequence = seq
        PreviousHash = prev
        Hash = h
        Op = o.Op
        PromptId = None
        Actor = o.Actor
        Timestamp = ts
        ResultEnvelope = OpResultEnvelope.Success }

    (acc @ [ record ], h))
  |> fst

let private chainOk: bool =
  match Verify.chain records with
  | Ok() -> true
  | Error _ -> false

// ─── The four stations (a station owns a contiguous run of ops) ──────────────

type private Station =
  {
    Host: Host
    Lang: string
    Title: string
    /// The op index this station's head-tree corresponds to (how many ops
    /// have been applied once this station is done). Server re-uses .NET's.
    OpsThrough: int
    Code: string
  }

let private stations: Station[] =
  [| { Host = Host.Python
       Lang = "Python"
       Title = "Author the app"
       OpsThrough = 1
       Code =
         "from fuaran_py.ui import box, metric, callout, Flex, Role\n\n"
         + "# The data scientist authors the base dashboard from the quarter's data.\n"
         + "dashboard = box(\"rl-root\",\n"
         + "    layout=Flex.VERTICAL, role=Role.DASHBOARD, heading=\"Q3 revenue\",\n"
         + "    children=[\n"
         + "        box(\"rl-strip\", layout=Flex.HORIZONTAL, role=Role.GROUP, children=[\n"
         + "            metric(\"rl-signups\", label=\"Signups\", value=1280),\n"
         + "            metric(\"rl-revenue\", label=\"Revenue £k\", value=42.5),\n"
         + "        ]),\n"
         + "        callout(\"rl-note\", tone=\"Subdued\", heading=\"Headline\",\n"
         + "                body=\"Draft – awaiting the trend line.\"),\n"
         + "    ])\n\n"
         + "stream.author(dashboard)   # genesis op → the chain begins" }
     { Host = Host.TypeScript
       Lang = "TypeScript"
       Title = "AI edits it"
       OpsThrough = 3
       Code =
         "import { insertChild, updateProp, metric } from '@fuaran-ui/ops';\n\n"
         + "// The assistant, running on the TypeScript host, edits the SAME wire.\n"
         + "stream.apply(insertChild('rl-strip', 2,\n"
         + "  metric('rl-trend', { label: 'Trend %', value: 8.4 })));   // add a column\n\n"
         + "stream.apply(updateProp('rl-note', 'Body',\n"
         + "  'TODO – write the headline'));   // ...and leaves a placeholder ⚠\n\n"
         + "// Two more links appended to the one chain. No re-export, no conversion." }
     { Host = Host.DotNet
       Lang = ".NET (F#)"
       Title = "Assertions heal it"
       OpsThrough = 4
       Code =
         "// The .NET host re-applies the stream and runs its assertions.\n"
         + "let placeholder =\n"
         + "    tree |> Tree.descendants\n"
         + "         |> Seq.tryFind (fun n -> textOf n |> contains \"TODO\")\n\n"
         + "match placeholder with\n"
         + "| Some node ->\n"
         + "    // closed-loop repair: one corrective op, appended to the chain\n"
         + "    stream.apply (updateProp node.Id \"Body\"\n"
         + "        \"Revenue up 18% QoQ; the new trend column confirms momentum.\")\n"
         + "| None -> ()   // clean" }
     { Host = Host.Server
       Lang = "Server"
       Title = "Delivered as HTML"
       OpsThrough = 4
       Code =
         "// The final, verified tree is projected – no new op, delivery preserves\n"
         + "// the bytes. The same artefact becomes a crawlable document and an\n"
         + "// email-safe digest, both falling out of one wire tree.\n"
         + "let html = Ssr.renderDocument verifiedTree      // crawlable, no client JS\n"
         + "let digest = Email.project verifiedTree         // table-based, inline-styled\n"
         + "// head hash is UNCHANGED from the .NET station – the delivery adds nothing." } |]

let private stationCount = stations.Length

/// The canonical wire (and its managed hash) of a station's head-tree.
let private stationWire (s: int) : string =
  CJson.encodeNode (treeAfter stations.[s].OpsThrough)

let private stationHash (s: int) : string = Hashing.sha256Hex (stationWire s)

let private hostSlug (h: Host) : string =
  match h with
  | Host.Python -> "python"
  | Host.TypeScript -> "ts"
  | Host.DotNet -> "dotnet"
  | Host.Server -> "server"

// Record index → owning host (for the chain-ribbon colouring).
let private recordHost (i: int) : Host = (List.item i ops).Host

// ─── The station-3 assertion – a real scan of the healed-from tree ───────────

let rec private collectText (n: Node<unit>) : string list =
  let here =
    match n.Kind with
    | NodeKind.Display(DisplayKind.Callout c) ->
      (match c.Heading with
       | Some(TextSource.Literal h) -> [ h ]
       | _ -> [])
      @ (match c.Body with
         | TextSource.Literal b -> [ b ]
         | _ -> [])
    | NodeKind.Display(DisplayKind.Markdown m) ->
      match m.Text with
      | TextSource.Literal t -> [ t ]
      | _ -> []
    | NodeKind.Display(DisplayKind.Heading h) ->
      match h.Text with
      | TextSource.Literal t -> [ t ]
      | _ -> []
    | _ -> []

  let kids =
    match n.Kind with
    | NodeKind.Layout(LayoutKind.Box s) -> s.Children
    | _ -> []

  here @ (kids |> List.collect collectText)

// The genuine finding the .NET assertion reports (over the tree AFTER the AI's
// two ops, BEFORE the heal) – the placeholder it will repair, or None if clean.
let private defectText: string option =
  collectText (treeAfter 3)
  |> List.tryFind (fun t -> t.Contains "TODO" || t.Contains "TBD")

// ─── Station 4 – the email-safe delivery projection (client-side, real) ──────

let private txt (ts: TextSource) : string =
  match ts with
  | TextSource.Literal s -> s
  | _ -> ""

let private esc (s: string) : string =
  s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")

let rec private emailNode (n: Node<unit>) : string =
  match n.Kind with
  | NodeKind.Layout(LayoutKind.Box s) ->
    let head =
      match s.Heading |> Option.map txt with
      | Some h when h <> "" ->
        sprintf "<h2 style=\"font:700 19px Arial,sans-serif;color:#1c1c22;margin:0 0 10px\">%s</h2>" (esc h)
      | _ -> ""

    let horizontal =
      match s.Layout with
      | BoxLayout.Flex f -> f.Direction = Horizontal
      | _ -> false

    if horizontal then
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
  | NodeKind.Display(DisplayKind.Metric m) ->
    let value =
      match m.Value with
      | Binding.Static x -> numText x
      | _ -> ""

    sprintf
      "<div style=\"font:12px Arial,sans-serif;color:#7a7a86;text-transform:uppercase;letter-spacing:.4px\">%s</div><div style=\"font:700 22px Arial,sans-serif;color:#1c1c22;margin-top:2px\">%s</div>"
      (esc (txt m.Label))
      (esc value)
  | NodeKind.Display(DisplayKind.Callout c) ->
    let h =
      match c.Heading |> Option.map txt with
      | Some x when x <> "" -> sprintf "<strong style=\"color:#1c1c22\">%s</strong><br>" (esc x)
      | _ -> ""

    sprintf
      "<table cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%%;margin:10px 0\"><tr><td style=\"padding:12px 14px;background:#eef2ff;border-left:4px solid #3358d4;font:15px/1.5 Arial,sans-serif;color:#2a2a34\">%s%s</td></tr></table>"
      h
      (esc (txt c.Body))
  | NodeKind.Display(DisplayKind.Markdown m) ->
    sprintf "<p style=\"font:15px/1.5 Arial,sans-serif;color:#3a3a44;margin:8px 0\">%s</p>" (esc (txt m.Text))
  | _ -> ""

let private emailHtml: string =
  "<div style=\"max-width:560px;margin:0 auto;background:#fff;padding:20px\">"
  + emailNode (treeAfter 4)
  + "</div>"

// ─── Interop – the independent hashers (app/relay-hosts.ts) ──────────────────

let private sha256WebCb (input: string) (cb: string -> unit) : unit = import "sha256HexCb" "./relay-hosts.ts"

let private ensurePyCb (onReady: unit -> unit) (onError: string -> unit) : unit =
  import "ensurePythonCb" "./relay-hosts.ts"

let private pySha256Cb (input: string) (onOk: string -> unit) (onError: string -> unit) : unit =
  import "pythonSha256Cb" "./relay-hosts.ts"

[<RequireQualifiedAccess>]
type private PyPhase =
  | NotLoaded
  | Loading
  | Ready
  | Failed of string

// ─── The page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private RelayView () : ReactElement =
  // reached = number of stations completed (0..4). Run animates it up; Step
  // advances one hand-off at a time.
  let reached, setReached = React.useState 0
  let running, setRunning = React.useState false

  let webHashes, setWebHashes =
    React.useState (Array.create stationCount (None: string option))

  let pyPhase, setPyPhase = React.useState PyPhase.NotLoaded

  let pyHashes, setPyHashes =
    React.useState (Array.create stationCount (None: string option))

  // Run: schedule the next hand-off while running and stations remain.
  React.useEffect (
    (fun () ->
      if running && reached < stationCount then
        let handle = JS.setTimeout (fun () -> setReached (reached + 1)) 1400

        { new IDisposable with
            member _.Dispose() = JS.clearTimeout handle }
      else
        if running && reached >= stationCount then
          setRunning false

        { new IDisposable with
            member _.Dispose() = () }),
    [| box running; box reached |]
  )

  // Web Crypto (TypeScript host) recomputes each reached station's head hash –
  // always on. Compute ONE missing station per run and re-run on webHashes so
  // concurrent callbacks never clobber each other's snapshot of the array.
  React.useEffect (
    (fun () ->
      let target =
        [ 0 .. reached - 1 ]
        |> List.tryFind (fun s -> s < stationCount && webHashes.[s] = None)

      match target with
      | Some s ->
        sha256WebCb (stationWire s) (fun h ->
          setWebHashes (webHashes |> Array.mapi (fun i x -> if i = s then Some h else x)))
      | None -> ()),
    [| box reached; box webHashes |]
  )

  // Python host (hashlib) recomputes each reached station once it is Ready.
  // Same one-at-a-time discipline (re-run on pyHashes) – the Pyodide callbacks
  // resolve slowly, so firing them all at once would clobber the shared array.
  React.useEffect (
    (fun () ->
      match pyPhase with
      | PyPhase.Ready ->
        let target =
          [ 0 .. reached - 1 ]
          |> List.tryFind (fun s -> s < stationCount && pyHashes.[s] = None)

        match target with
        | Some s ->
          pySha256Cb
            (stationWire s)
            (fun h -> setPyHashes (pyHashes |> Array.mapi (fun i x -> if i = s then Some h else x)))
            (fun _ -> ())
        | None -> ()
      | _ -> ()),
    [| box pyPhase; box reached; box pyHashes |]
  )

  let resetAll () =
    setRunning false
    setReached 0
    setWebHashes (Array.create stationCount None)
    setPyHashes (Array.create stationCount None)

  // ─── Controls ─────────────────────────────────────────────────────────────
  let controls =
    Html.div
      [ prop.className "rl-controls"
        prop.children
          [ Html.button
              [ prop.className "rl-btn rl-btn-run"
                prop.disabled running
                prop.text (
                  if reached >= stationCount then
                    "Run it again"
                  else
                    "▶ Run the relay"
                )
                prop.onClick (fun _ ->
                  setWebHashes (Array.create stationCount None)
                  setPyHashes (Array.create stationCount None)
                  setReached 0
                  setRunning true) ]
            Html.button
              [ prop.className "rl-btn rl-btn-step"
                prop.disabled (running || reached >= stationCount)
                prop.text "Step ›"
                prop.onClick (fun _ -> setReached (reached + 1)) ]
            (if reached > 0 then
               Html.button
                 [ prop.className "rl-btn rl-btn-ghost"
                   prop.text "Reset"
                   prop.onClick (fun _ -> resetAll ()) ]
             else
               Html.none) ] ]

  // ─── The relay track ──────────────────────────────────────────────────────
  let stationCard (s: int) : ReactElement =
    let st = stations.[s]
    let slug = hostSlug st.Host
    let isDone = reached > s
    let isNext = reached = s

    let stateLabel =
      if isDone then "✓ done"
      elif isNext && running then "running…"
      elif isNext then "next"
      else "queued"

    let stateCls =
      if isDone then "rl-station-done"
      elif isNext then "rl-station-next"
      else "rl-station-idle"

    let defectBadge =
      if st.Host = Host.DotNet && isDone then
        match defectText with
        | Some d ->
          Html.div
            [ prop.className "rl-assert"
              prop.children
                [ Html.span [ prop.className "rl-assert-mark"; prop.text "assertion" ]
                  Html.span
                    [ prop.className "rl-assert-text"
                      prop.text (sprintf "caught placeholder “%s” → healed" d) ] ] ]
        | None -> Html.none
      else
        Html.none

    Html.div
      [ prop.className (sprintf "rl-station rl-host-%s %s" slug stateCls)
        prop.children
          [ Html.div
              [ prop.className "rl-station-head"
                prop.children
                  [ Html.span [ prop.className (sprintf "rl-badge rl-badge-%s" slug); prop.text st.Lang ]
                    Html.span [ prop.className "rl-station-state"; prop.text stateLabel ] ] ]
            Html.div [ prop.className "rl-station-title"; prop.text st.Title ]
            Html.div
              [ prop.className "rl-station-note"
                prop.text (List.item (min s (opCount - 1)) ops).Note ]
            defectBadge
            Html.details
              [ prop.className "rl-code-drawer"
                prop.children
                  [ Html.summary [ prop.text "show the code" ]
                    Html.pre [ prop.className "rl-code"; prop.children [ Html.code [ prop.text st.Code ] ] ] ] ] ] ]

  let track =
    Html.div
      [ prop.className "rl-track"
        prop.children [ for s in 0 .. stationCount - 1 -> stationCard s ] ]

  // ─── The chain ribbon (record hashes; Verify.chain) ───────────────────────
  let ribbon =
    Html.div
      [ prop.className "rl-ribbon"
        prop.children
          [ Html.div
              [ prop.className "rl-ribbon-links"
                prop.children
                  [ for i in 0 .. opCount - 1 do
                      // A link shows once its owning station is reached.
                      let owningStation =
                        stations
                        |> Array.findIndex (fun st -> st.OpsThrough > i && st.Host <> Host.Server)

                      let visible = reached > owningStation
                      let slug = hostSlug (recordHost i)

                      Html.div
                        [ prop.className (
                            sprintf "rl-link rl-host-%s %s" slug (if visible then "rl-link-on" else "rl-link-off")
                          )
                          prop.title (sprintf "op %d · %s" (i + 1) (shortHash (List.item i records).Hash))
                          prop.text (string (i + 1)) ] ] ]
            (if reached >= stationCount then
               Html.div
                 [ prop.className (
                     if chainOk then
                       "rl-verify rl-verify-ok"
                     else
                       "rl-verify rl-verify-bad"
                   )
                   prop.children
                     [ Html.span [ prop.className "rl-verify-mark"; prop.text (if chainOk then "✓" else "✗") ]
                       Html.span
                         [ prop.text (
                             if chainOk then
                               sprintf "Verify.chain · %d links intact" opCount
                             else
                               "Verify.chain failed"
                           ) ] ] ]
             else
               Html.div
                 [ prop.className "rl-verify rl-verify-pending"
                   prop.text (sprintf "%d of %d hand-offs" reached stationCount) ]) ] ]

  // ─── The parity seal (three independent hashers agree) ────────────────────
  let sealRow (s: int) : ReactElement =
    let managed = stationHash s
    let web = webHashes.[s]
    let py = pyHashes.[s]

    let allAgree =
      (web = Some managed)
      && (match py with
          | Some p -> p = managed
          | None -> true)

    let hashCell (label: string) (value: string option) (pendingNote: string) : ReactElement =
      match value with
      | Some h ->
        let ok = h = managed

        Html.div
          [ prop.className "rl-seal-cell"
            prop.children
              [ Html.span [ prop.className "rl-seal-lang"; prop.text label ]
                Html.code
                  [ prop.className (
                      if ok then
                        "rl-seal-hash rl-seal-ok"
                      else
                        "rl-seal-hash rl-seal-bad"
                    )
                    prop.text (shortHash h) ] ] ]
      | None ->
        Html.div
          [ prop.className "rl-seal-cell"
            prop.children
              [ Html.span [ prop.className "rl-seal-lang"; prop.text label ]
                Html.span [ prop.className "rl-seal-pending"; prop.text pendingNote ] ] ]

    Html.div
      [ prop.className (
          if allAgree then
            "rl-seal-row rl-seal-row-ok"
          else
            "rl-seal-row"
        )
        prop.children
          [ Html.div
              [ prop.className "rl-seal-station"
                prop.children
                  [ Html.span
                      [ prop.className (sprintf "rl-badge rl-badge-%s" (hostSlug stations.[s].Host))
                        prop.text stations.[s].Lang ]
                    Html.span [ prop.className "rl-seal-title"; prop.text stations.[s].Title ] ] ]
            Html.div
              [ prop.className "rl-seal-hashes"
                prop.children
                  [ hashCell "F# (managed)" (Some managed) ""
                    hashCell "TS (Web Crypto)" web "computing…"
                    hashCell
                      "Python (hashlib)"
                      py
                      (match pyPhase with
                       | PyPhase.Ready -> "computing…"
                       | PyPhase.Loading -> "loading…"
                       | _ -> "turn on →") ] ] ] ]

  let seal =
    Html.div
      [ prop.className "rl-seal"
        prop.children
          [ Html.div
              [ prop.className "rl-seal-head"
                prop.children
                  [ Html.h3 [ prop.className "rl-seal-title-h"; prop.text "Parity seal" ]
                    Html.p
                      [ prop.className "rl-seal-sub"
                        prop.text
                          "Each station's canonical wire head hash, recomputed by three independent SHA-256 implementations. They agree, byte for byte." ] ] ]
            (if reached = 0 then
               Html.p [ prop.className "rl-seal-empty"; prop.text "Run the relay to fill the seal." ]
             else
               Html.div
                 [ prop.className "rl-seal-rows"
                   prop.children [ for s in 0 .. reached - 1 -> sealRow s ] ]) ] ]

  let pythonLaunch =
    match pyPhase with
    | PyPhase.NotLoaded ->
      Html.button
        [ prop.className "rl-py-btn"
          prop.text "Add the Python host (hashlib, CPython/WASM)"
          prop.onClick (fun _ ->
            setPyPhase PyPhase.Loading
            ensurePyCb (fun () -> setPyPhase PyPhase.Ready) (fun e -> setPyPhase (PyPhase.Failed e))) ]
    | PyPhase.Loading -> Html.span [ prop.className "rl-py-note"; prop.text "Downloading CPython-on-WebAssembly…" ]
    | PyPhase.Ready ->
      Html.span
        [ prop.className "rl-py-note rl-py-ready"
          prop.text "CPython verifying in your browser ✓" ]
    | PyPhase.Failed e ->
      Html.button
        [ prop.className "rl-py-btn"
          prop.text ("Retry the Python host (" + e + ")")
          prop.onClick (fun _ ->
            setPyPhase PyPhase.Loading
            ensurePyCb (fun () -> setPyPhase PyPhase.Ready) (fun e -> setPyPhase (PyPhase.Failed e))) ]

  // ─── Station 4 delivery (crawlable document + email digest) ───────────────
  let delivery =
    if reached < stationCount then
      Html.none
    else
      Html.div
        [ prop.className "rl-delivery"
          prop.children
            [ Html.h3
                [ prop.className "rl-delivery-h"
                  prop.text "Delivered – one wire, two targets" ]
              Html.div
                [ prop.className "rl-delivery-grid"
                  prop.children
                    [ Html.div
                        [ prop.className "rl-delivery-pane"
                          prop.children
                            [ Html.span
                                [ prop.className "rl-delivery-tag"
                                  prop.text "crawlable document (static markup – no client runtime)" ]
                              Html.div [ prop.className "rl-doc-render"; prop.children [ renderNode (treeAfter 4) ] ] ] ]
                      Html.div
                        [ prop.className "rl-delivery-pane"
                          prop.children
                            [ Html.span
                                [ prop.className "rl-delivery-tag"
                                  prop.text "email-safe digest (table-based, inline-styled)" ]
                              Html.div [ prop.className "rl-email"; prop.dangerouslySetInnerHTML emailHtml ] ] ] ] ] ] ]

  // ─── Honesty ──────────────────────────────────────────────────────────────
  let honesty =
    Html.div
      [ prop.className "rl-honesty"
        prop.children
          [ Html.h3 [ prop.text "How honest is this?" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "The relay's spine is real: every hand-off is a genuine hash-chained op-record, folded through the shipped apply engine, and the shipped Verify.chain re-runs green over the whole chain. The .NET station's assertion genuinely scans the tree and the placeholder it reports is the one the AI left – the heal op is real repair, not a staged beat." ]
                    Html.li
                      [ prop.text
                          "The parity seal is the genuinely-polyglot part: each station's canonical wire head hash is recomputed by three independent SHA-256 implementations – the F# managed digest and TypeScript's Web Crypto always, Python's hashlib in CPython-on-WebAssembly on demand – and they agree, byte for byte." ]
                    Html.li
                      [ prop.text
                          "What is NOT claimed: the encode / apply / hash-chain engine is the real Fuaran.UI, compiled to JavaScript via Fable – one engine, not four shipped to the browser. The four station source panels are idiomatic per-host projections. The point the relay dramatises is that the wire, the op protocol, and the chain are host-neutral, so the artefact survives crossing runtimes – and the independent hashers prove the bytes really are the same." ]
                    Html.li
                      [ prop.children
                          [ Html.text
                              "Your teams don't have to agree on a language to collaborate on an application. It is the same "
                            Html.a [ prop.href "#/pillar/wire"; prop.text "one-wire-many-worlds" ]
                            Html.text " thesis Rosetta shows at rest – here, in motion." ] ] ] ] ] ]

  Html.div
    [ prop.className "rl-page"
      prop.children
        [ Html.h1 [ prop.className "rl-title"; prop.text "The Relay" ]
          Html.p
            [ prop.className "rl-lede"
              prop.text
                "Watch one app pass through four runtimes – authored in Python, edited by an AI on the TypeScript host, healed on .NET, delivered as a document and an email – and arrive with a verified, unbroken hash chain. Four languages played telephone; nothing was lost." ]
          controls
          track
          ribbon
          seal
          Html.div [ prop.className "rl-py-launch"; prop.children [ pythonLaunch ] ]
          delivery
          honesty ] ]

let page: ReactElement = RelayView()
