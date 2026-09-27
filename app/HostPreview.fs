module Fuaran.Live.HostPreview

// ============================================================================
//  Host-fidelity preview (Phase 1818) — one tree, shown as each declared render
//  tier would show it.
//
//  The per-kind fidelity facts are DATA: the specification corpus publishes
//  `render-fidelity.json`, and this module reads a bundled copy of it
//  (`app/fidelity/render-fidelity.json`, byte-locked to the corpus by
//  `test/hostPreview.test.ts`). Nothing here restates a kind's posture — the
//  switcher offers the render tiers the manifest declares, and each node's
//  status is read off its kind's row.
//
//  What the manifest declares, and therefore what this can show. The manifest
//  carries three TIERS — `source` (the wire data; never a rendered form),
//  `fallback` (the deterministic render a no-script reader, a crawler or a
//  non-browser host gets) and `rich` (the declared client-only render) — and,
//  per kind, a `rich.class` of `none`, `behavioural` or `clientOnly`. It does
//  NOT carry a per-host column for the native render projections or for the
//  server projections; those facts are prose in each surface's own docs today.
//  So the "hosts" here are the manifest's render tiers, and a named-host column
//  appears the day the manifest carries one — never before, and never from a
//  table written here.
//
//  The status rule, per tier, is the manifest's own tier contract (§13):
//    * `rich`     — the whole render: every declared kind is Full.
//    * `fallback` — `none` is Full (the fallback IS the whole render);
//                   `behavioural` renders its full markup with the behaviour
//                   the rich tier attaches absent (Inert); `clientOnly` shows
//                   the declared fallback in place of the client-only render
//                   (Placeholder).
//  A tier with no rule here (`source`, or a tier a later manifest adds) is not
//  offered: the parity test pins the tier set, so a new tier is a red test and
//  a deliberate ruling rather than a silent omission.
//
//  The speech view (round 2 of Phase 1818). Phase 1813 gave every kind row a
//  `speech` column — a class drawn from the manifest's closed `speechClasses`
//  (spoken / derived / announced-only / omitted) and a note — so the switcher
//  gains one more entry, `speech`, by a ruling on that vocabulary: it shows,
//  per node, the class the node's kind DECLARES, with the kind's note, and
//  lists the nodes the class says are omitted (plus any kind with no row). It
//  does not run a speech projection, it infers nothing about a subtree beyond
//  what each node's own row says, and it cannot see a node's own authored
//  speech override — the wire member that carries one is not in the pinned
//  decoder, so the view is the per-kind declaration and says so. A class the
//  manifest adds is refused at parse until it is ruled on (the test pins the
//  vocabulary).
//
//  This is a DECLARED-FIDELITY SIMULATION: it shows what the manifest declares,
//  not any host's pixels, and imitates no host's theme.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module Introspect = Fuaran.UI.Ops.Introspect
module Decode = Fuaran.UI.Ops.JsonDecode
module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── the manifest ────────────────────────────────────────────────────────────

/// A render tier the manifest declares, offered as a preview host.
type Host = { Id: string; Meaning: string }

/// A class from the manifest's closed `speechClasses` vocabulary.
type SpeechClass = { Class: string; Meaning: string }

/// One kind's declared speech ruling (its `speech` column).
type SpeechRow = { Class: string; Note: string }

/// One kind's row, reduced to the members the preview reads.
type KindRow =
  {
    Kind: string
    RichClass: string
    Fallback: string
    /// `None` when the row declares no `speech` column.
    Speech: SpeechRow option
  }

type Manifest =
  {
    Hosts: Host list
    Tiers: string list
    /// The declared speech vocabulary, in manifest order (empty when the
    /// manifest declares none — the speech view is then not offered).
    SpeechClasses: SpeechClass list
    Rows: Map<string, KindRow>
  }

/// The switcher entry for the speech view. It is not a render tier: it is
/// offered when, and only when, the manifest declares `speechClasses`.
[<Literal>]
let SpeechHostId = "speech"

/// The speech class whose nodes the view lists (the class's declared meaning:
/// "reported in the projection's omission list rather than dropped").
[<Literal>]
let OmittedClass = "omitted"

/// The speech classes this view has a ruling for. A class outside this set is
/// refused at parse, so a vocabulary the manifest grows is a red test and a
/// deliberate ruling rather than a silently mislabelled node.
let private ruledSpeechClasses =
  set [ "spoken"; "derived"; "announced-only"; OmittedClass ]

/// A node's declared fidelity on the chosen host.
[<RequireQualifiedAccess>]
type Fidelity =
  /// Rendered as the rich client renders it.
  | Full
  /// Its full markup renders; the behaviour the rich tier attaches does not.
  | Inert
  /// The declared fallback stands in for a client-only render.
  | Placeholder
  /// The manifest carries no row for this kind, so no claim is made about it.
  | Undeclared

/// The status rule for one tier, or `None` when the tier is not a render the
/// preview can simulate (`source`, or a tier this module has no ruling for).
let private ruleFor (tier: string) : (KindRow -> Fidelity) option =
  match tier with
  | "rich" -> Some(fun _ -> Fidelity.Full)
  | "fallback" ->
    Some(fun row ->
      match row.RichClass with
      | "none" -> Fidelity.Full
      | "behavioural" -> Fidelity.Inert
      | "clientOnly" -> Fidelity.Placeholder
      | _ -> Fidelity.Undeclared)
  | _ -> None

[<Emit("$0[$1]")>]
let private get (o: obj) (name: string) : obj = jsNative

let private str (o: obj) : string option =
  if jsTypeof o = "string" then
    Some(unbox<string> o)
  else
    None

let private arr (o: obj) : obj array option =
  if JS.Constructors.Array.isArray o then
    Some(unbox<obj array> o)
  else
    None

/// Read the manifest's tiers and kind rows. Refuses (rather than guessing) a
/// document missing either, or a row missing its kind or rich class.
let parseManifest (doc: obj) : Result<Manifest, string> =
  if isNull doc || jsTypeof doc <> "object" then
    Error "the fidelity manifest is not a JSON object"
  else
    match arr (get doc "tiers"), arr (get doc "kinds") with
    | None, _ -> Error "the fidelity manifest declares no `tiers` array"
    | _, None -> Error "the fidelity manifest declares no `kinds` array"
    | Some tiers, Some kinds ->
      let tierPairs =
        tiers
        |> Array.choose (fun t ->
          match str (get t "tier") with
          | Some id -> Some(id, str (get t "meaning") |> Option.defaultValue "")
          | None -> None)
        |> List.ofArray

      let speechClasses =
        match arr (get doc "speechClasses") with
        | None -> Ok []
        | Some classes ->
          classes
          |> Array.map (fun c ->
            match str (get c "class") with
            | Some cls when ruledSpeechClasses.Contains cls ->
              Ok
                { SpeechClass.Class = cls
                  Meaning = str (get c "meaning") |> Option.defaultValue "" }
            | Some cls -> Error(sprintf "the speech class `%s` has no ruling in this preview" cls)
            | None -> Error "a `speechClasses` entry lacks its `class`")
          |> List.ofArray
          |> List.fold
            (fun acc r ->
              match acc, r with
              | Ok xs, Ok x -> Ok(xs @ [ x ])
              | (Error _ as e), _ -> e
              | _, Error e -> Error e)
            (Ok [])

      let declaredSpeech =
        match speechClasses with
        | Ok cs -> cs |> List.map (fun c -> c.Class) |> Set.ofList
        | Error _ -> Set.empty

      let rows =
        kinds
        |> Array.map (fun k ->
          let rich = get k "rich"
          let speech = get k "speech"

          match str (get k "kind"), (if isNull rich then None else str (get rich "class")) with
          | Some kind, Some cls ->
            let speechRow =
              if isNull speech then
                Ok None
              else
                match str (get speech "class") with
                | Some sc when declaredSpeech.Contains sc ->
                  Ok(
                    Some
                      { SpeechRow.Class = sc
                        Note = str (get speech "note") |> Option.defaultValue "" }
                  )
                | _ -> Error(sprintf "the %s row's `speech.class` is not one of the declared `speechClasses`" kind)

            speechRow
            |> Result.map (fun sp ->
              { Kind = kind
                RichClass = cls
                Fallback = str (get k "fallback") |> Option.defaultValue ""
                Speech = sp })
          | _ -> Error "a kind row lacks its `kind` or `rich.class`")
        |> List.ofArray

      let firstError =
        match speechClasses with
        | Error e -> Some e
        | Ok _ ->
          rows
          |> List.tryPick (function
            | Error e -> Some e
            | Ok _ -> None)

      match firstError with
      | Some e -> Error e
      | None ->
        let rows =
          rows
          |> List.choose (function
            | Ok r -> Some r
            | Error _ -> None)

        let speechClasses = speechClasses |> Result.defaultValue []

        let speechHost =
          match speechClasses with
          | [] -> []
          | _ ->
            [ { Id = SpeechHostId
                Meaning =
                  "what a speech projection says for each node, by the speech class its kind declares: "
                  + (speechClasses
                     |> List.map (fun c -> sprintf "%s (%s)" c.Class c.Meaning)
                     |> String.concat "; ") } ]

        Ok
          { Hosts =
              (tierPairs
               |> List.filter (fun (id, _) -> (ruleFor id).IsSome)
               |> List.map (fun (id, meaning) -> { Id = id; Meaning = meaning }))
              @ speechHost
            Tiers = tierPairs |> List.map fst
            SpeechClasses = speechClasses
            Rows = rows |> List.map (fun r -> r.Kind, r) |> Map.ofList }

/// The bundled copy of the corpus manifest (a build input; see the header).
[<Import("default", "./fidelity/render-fidelity.json")>]
let private bundledDoc: obj = jsNative

let bundled: Result<Manifest, string> = parseManifest bundledDoc

// ─── the pure annotation ─────────────────────────────────────────────────────

/// One node of the tree, annotated with its declared fidelity on one host.
type PreviewNode =
  {
    Id: string
    Kind: string
    Fidelity: Fidelity
    /// The manifest's declared fallback for the kind (empty when undeclared).
    Fallback: string
    Children: PreviewNode list
  }

type PreviewTree =
  {
    Host: Host
    Root: PreviewNode
    Total: int
    FullCount: int
    /// Every node not at full fidelity on this host, in document order.
    Degraded: PreviewNode list
  }

let private idOf (node: Node<obj>) : string = node.Id

/// Annotate every node of `node` (structural children and the non-structural
/// slots alike — the same traversal the navigator walks) with its declared
/// fidelity on `hostId`, stopping at a placeholder (which stands in for its
/// whole subtree). `None` when the manifest does not offer that host.
let annotate (manifest: Manifest) (node: Node<obj>) (hostId: string) : PreviewTree option =
  match manifest.Hosts |> List.tryFind (fun h -> h.Id = hostId), ruleFor hostId with
  | Some host, Some rule ->
    let rec walk (n: Node<obj>) : PreviewNode =
      let kind = RenderFidelity.wireNameOf n.Kind

      let fidelity, fallback =
        match Map.tryFind kind manifest.Rows with
        | Some row -> rule row, row.Fallback
        | None -> Fidelity.Undeclared, ""

      { Id = idOf n
        Kind = kind
        Fidelity = fidelity
        Fallback = fallback
        // A placeholder stands in for its WHOLE subtree — the declared fallback
        // is the render of the node and everything under it — so the nodes
        // beneath one are neither walked, counted nor rendered.
        Children =
          if fidelity = Fidelity.Placeholder then
            []
          else
            Introspect.descendantNodes n |> List.map walk }

    let root = walk node

    let rec flatten (p: PreviewNode) : PreviewNode list =
      p :: (p.Children |> List.collect flatten)

    let all = flatten root

    Some
      { Host = host
        Root = root
        Total = all.Length
        FullCount = all |> List.filter (fun p -> p.Fidelity = Fidelity.Full) |> List.length
        Degraded = all |> List.filter (fun p -> p.Fidelity <> Fidelity.Full) }
  | _ -> None

let private count (preview: PreviewTree) (f: Fidelity) : int =
  preview.Degraded |> List.filter (fun p -> p.Fidelity = f) |> List.length

let private plural (n: int) (one: string) (many: string) : string =
  sprintf "%d %s" n (if n = 1 then one else many)

/// The one-line summary: "41 of 43 nodes render at full fidelity on this host;
/// 2 placeholders". Inert and undeclared nodes are named only when present.
let summary (preview: PreviewTree) : string =
  let head =
    sprintf "%d of %s render at full fidelity on this host" preview.FullCount (plural preview.Total "node" "nodes")

  let parts =
    [ plural (count preview Fidelity.Placeholder) "placeholder" "placeholders"
      match count preview Fidelity.Inert with
      | 0 -> ()
      | n -> plural n "without its behaviour" "without their behaviour"
      match count preview Fidelity.Undeclared with
      | 0 -> ()
      | n -> plural n "undeclared kind" "undeclared kinds" ]

  head + "; " + String.concat "; " parts

let describe (f: Fidelity) : string =
  match f with
  | Fidelity.Full -> "full fidelity"
  | Fidelity.Inert -> "renders; behaviour not attached"
  | Fidelity.Placeholder -> "placeholder: the declared fallback stands in"
  | Fidelity.Undeclared -> "undeclared: the manifest carries no row for this kind"

// ─── the speech view ─────────────────────────────────────────────────────────

/// One node, annotated with the speech class its kind declares.
type SpeechNode =
  {
    Id: string
    Kind: string
    /// The declared class, or `None` when the kind has no row or no `speech`.
    Class: string option
    Note: string
    Children: SpeechNode list
  }

type SpeechPreview =
  {
    Root: SpeechNode
    Total: int
    /// Per declared class, in the manifest's vocabulary order: how many nodes.
    ByClass: (string * int) list
    Undeclared: int
    /// The nodes whose class is `omitted`, then those with no declared class,
    /// each in document order — the list the pane makes clickable.
    Listed: SpeechNode list
  }

/// Annotate every node (the same traversal as `annotate`, never pruned: each
/// node carries its own kind's ruling) with its declared speech class. `None`
/// when the manifest declares no speech vocabulary.
let annotateSpeech (manifest: Manifest) (node: Node<obj>) : SpeechPreview option =
  match manifest.SpeechClasses with
  | [] -> None
  | classes ->
    let rec walk (n: Node<obj>) : SpeechNode =
      let kind = RenderFidelity.wireNameOf n.Kind

      let speech = Map.tryFind kind manifest.Rows |> Option.bind (fun r -> r.Speech)

      { Id = idOf n
        Kind = kind
        Class = speech |> Option.map (fun s -> s.Class)
        Note = speech |> Option.map (fun s -> s.Note) |> Option.defaultValue ""
        Children = Introspect.descendantNodes n |> List.map walk }

    let root = walk node

    let rec flatten (s: SpeechNode) : SpeechNode list =
      s :: (s.Children |> List.collect flatten)

    let all = flatten root

    Some
      { Root = root
        Total = all.Length
        ByClass =
          classes
          |> List.map (fun c -> c.Class, all |> List.filter (fun s -> s.Class = Some c.Class) |> List.length)
        Undeclared = all |> List.filter (fun s -> s.Class.IsNone) |> List.length
        Listed =
          (all |> List.filter (fun s -> s.Class = Some OmittedClass))
          @ (all |> List.filter (fun s -> s.Class.IsNone)) }

/// The one-line speech summary: "6 nodes on the speech view: 2 spoken,
/// 3 derived, 1 announced-only, 0 omitted", naming undeclared kinds when present.
let speechSummary (preview: SpeechPreview) : string =
  let counts =
    preview.ByClass
    |> List.map (fun (cls, n) -> sprintf "%d %s" n cls)
    |> String.concat ", "

  let tail =
    match preview.Undeclared with
    | 0 -> ""
    | n -> "; " + plural n "undeclared kind" "undeclared kinds"

  sprintf "%s on the speech view: %s%s" (plural preview.Total "node" "nodes") counts tail

let describeSpeech (s: SpeechNode) : string =
  match s.Class with
  | Some cls when s.Note = "" -> cls
  | Some cls -> sprintf "%s: %s" cls s.Note
  | None -> "undeclared: the manifest declares no speech ruling for this kind"

// ─── the view ────────────────────────────────────────────────────────────────

/// The id a degraded node's labelled wrapper carries in the preview render.
let wrapperId (nodeId: string) : string = nodeId + ".host-preview"

[<Emit("JSON.stringify($0)")>]
let private jsonString (s: string) : string = jsNative

let private calloutJson (id: string) (tone: string) (heading: string) (body: string) : string =
  sprintf
    """{"id":%s,"kind":{"$type":"Callout","body":%s,"dismissable":false,"heading":%s,"tone":"%s"}}"""
    (jsonString id)
    (jsonString body)
    (jsonString heading)
    tone

let private boxJson (id: string) (children: string list) : string =
  sprintf
    """{"id":%s,"kind":{"$type":"Box","children":[%s],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
    (jsonString id)
    (String.concat "," children)

/// The tree the preview renders: full nodes as they are, every degraded node
/// inside a labelled wrapper naming its declared status (and, for a
/// placeholder, the manifest's declared fallback in place of the node's own
/// client-only render). Built through the real strict decoder, so the
/// wrapper is a canonical tree and not a shape invented beside the language;
/// a wrapper that failed to decode would leave the node unwrapped.
let dress (manifest: Manifest) (hostId: string) (node: Node<obj>) : Node<obj> =
  match manifest.Hosts |> List.tryFind (fun h -> h.Id = hostId), ruleFor hostId with
  | Some host, Some rule ->
    let rec go (n: Node<obj>) : Node<obj> =
      let kind = RenderFidelity.wireNameOf n.Kind
      let id = idOf n

      let fidelity, fallback =
        match Map.tryFind kind manifest.Rows with
        | Some row -> rule row, row.Fallback
        | None -> Fidelity.Undeclared, ""

      let rebuilt () =
        Introspect.replaceDescendantNodes n (Introspect.descendantNodes n |> List.map go)

      let label tone body =
        calloutJson (id + ".host-preview-label") tone (sprintf "%s on %s: %s" kind host.Id (describe fidelity)) body

      let wrapped =
        match fidelity with
        | Fidelity.Full -> Choice1Of2(rebuilt ())
        | Fidelity.Inert ->
          let inner = rebuilt ()

          Choice2Of2(
            inner,
            boxJson
              (wrapperId id)
              [ label "Info" "Shown with its markup; interaction is not attached on this tier."
                Canon.encodeNode inner ]
          )
        | Fidelity.Placeholder ->
          Choice2Of2(n, boxJson (wrapperId id) [ label "Warning" ("Declared fallback: " + fallback) ])
        | Fidelity.Undeclared ->
          let inner = rebuilt ()

          Choice2Of2(
            inner,
            boxJson
              (wrapperId id)
              [ label "Subdued" "No fidelity row is declared for this kind."
                Canon.encodeNode inner ]
          )

      match wrapped with
      | Choice1Of2 full -> full
      | Choice2Of2(unwrapped, json) ->
        match Decode.decodeNodeObj json with
        | Ok dressed -> dressed
        | Error _ -> unwrapped

    go node
  | _ -> node

/// The CSS selector of the host preview's render scope.
let private scope = ".fl-host-preview-root"

/// The attribute the renderer stamps on every node it renders.
let private renderedAttr = "data-fuaran-node-id"

/// The attribute the speech outline stamps on each of its entries.
let private speechAttr = "data-speech-node-id"

[<Emit("""(function(id, scopeSel, attr){
  try {
    var root = document.querySelector(scopeSel);
    if (!root) { return; }
    var all = root.querySelectorAll('[' + attr + ']');
    for (var i = 0; i < all.length; i++) { all[i].style.outline = ''; }
    var esc = String(id).split('\\').join('\\\\').split('"').join('\\"');
    var el = root.querySelector('[' + attr + '="' + esc + '"]');
    if (!el) { return; }
    el.style.outline = '2px solid currentColor';
    el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  } catch (e) { }
})($0, $1, $2)""")>]
let private reveal (nodeId: string) (scopeSelector: string) (attr: string) : unit = jsNative

/// The one sentence that keeps the preview honest.
let honesty =
  "This is a declared-fidelity simulation: it shows what the published render-fidelity manifest declares for each kind on the chosen tier, not that host's pixels, and it imitates no host's theme."

/// The speech view's one sentence: declared, not a screen reader.
let speechHonesty =
  "This is a declared-fidelity simulation: it shows the speech class the published render-fidelity manifest declares for each node's kind, not what a screen reader or any speech projection would actually say."

let private degradedList (items: (string * string) list) (attr: string) : ReactElement =
  match items with
  | [] -> Html.none
  | _ ->
    Html.ul
      [ prop.children
          [ for (targetId, text) in items ->
              Html.li
                [ Html.button
                    [ prop.style [ style.textAlign.left ]
                      prop.onClick (fun _ -> reveal targetId scope attr)
                      prop.text text ] ] ] ]

let rec private speechOutline (s: SpeechNode) : ReactElement =
  let line = s.Id + " (" + s.Kind + ") - " + describeSpeech s

  let nested =
    match s.Children with
    | [] -> []
    | children -> [ Html.ul [ prop.children (children |> List.map speechOutline) ] ]

  Html.li
    [ prop.custom (speechAttr, s.Id)
      prop.children (Html.span [ prop.text line ] :: nested) ]

[<ReactComponent>]
let HostPreviewPane (tree: Node<obj> option) : ReactElement =
  let chosen, setChosen = React.useState (None: string option)

  match bundled, tree with
  | Error e, _ -> Html.div [ prop.className "fl-error"; prop.text ("Fidelity manifest unreadable: " + e) ]
  | Ok _, None ->
    Html.div
      [ prop.className "fl-empty"
        prop.text "Generate or load a tree to preview it per host." ]
  | Ok manifest, Some root ->
    match manifest.Hosts with
    | [] ->
      Html.div
        [ prop.className "fl-empty"
          prop.text "The fidelity manifest declares no render tier to preview." ]
    | first :: _ ->
      let hostId =
        match chosen with
        | Some id when manifest.Hosts |> List.exists (fun h -> h.Id = id) -> id
        | _ -> first.Id

      let host = manifest.Hosts |> List.find (fun h -> h.Id = hostId)

      let switcher =
        Html.div
          [ prop.role "radiogroup"
            prop.ariaLabel "Preview host"
            prop.style [ style.display.flex; style.gap (length.px 6); style.flexWrap.wrap ]
            prop.children
              [ for h in manifest.Hosts ->
                  Html.button
                    [ prop.role "radio"
                      prop.ariaChecked (h.Id = hostId)
                      prop.title h.Meaning
                      prop.style [ style.fontWeight (if h.Id = hostId then 700 else 400) ]
                      prop.onClick (fun _ -> setChosen (Some h.Id))
                      prop.text h.Id ] ] ]

      let body =
        if hostId = SpeechHostId then
          match annotateSpeech manifest root with
          | None -> None
          | Some preview ->
            Some(
              speechHonesty,
              speechSummary preview,
              degradedList
                [ for s in preview.Listed -> s.Id, sprintf "%s (%s): %s" s.Id s.Kind (describeSpeech s) ]
                speechAttr,
              Html.ul [ prop.children [ speechOutline preview.Root ] ]
            )
        else
          match annotate manifest root hostId with
          | None -> None
          | Some preview ->
            Some(
              honesty,
              summary preview,
              degradedList
                [ for d in preview.Degraded -> wrapperId d.Id, sprintf "%s (%s): %s" d.Id d.Kind (describe d.Fidelity) ]
                renderedAttr,
              Render.renderWithSources BindingResolver.empty ignore (dress manifest hostId root)
            )

      match body with
      | None -> Html.none
      | Some(sentence, line, listed, rendered) ->
        Html.div
          [ prop.className "fl-host-preview"
            prop.children
              [ Html.p [ prop.style [ style.fontSize (length.em 0.9) ]; prop.text sentence ]
                switcher
                Html.p [ prop.style [ style.fontSize (length.em 0.85) ]; prop.text host.Meaning ]
                Html.p [ prop.custom ("aria-live", "polite"); prop.text line ]
                listed
                Html.div [ prop.className "fl-host-preview-root"; prop.children [ rendered ] ] ] ]
