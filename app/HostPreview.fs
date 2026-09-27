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

/// One kind's row, reduced to the members the preview reads.
type KindRow =
  { Kind: string
    RichClass: string
    Fallback: string }

type Manifest =
  { Hosts: Host list
    Tiers: string list
    Rows: Map<string, KindRow> }

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

      let rows =
        kinds
        |> Array.map (fun k ->
          let rich = get k "rich"

          match str (get k "kind"), (if isNull rich then None else str (get rich "class")) with
          | Some kind, Some cls ->
            Ok
              { Kind = kind
                RichClass = cls
                Fallback = str (get k "fallback") |> Option.defaultValue "" }
          | _ -> Error "a kind row lacks its `kind` or `rich.class`")
        |> List.ofArray

      match
        rows
        |> List.tryPick (function
          | Error e -> Some e
          | Ok _ -> None)
      with
      | Some e -> Error e
      | None ->
        let rows =
          rows
          |> List.choose (function
            | Ok r -> Some r
            | Error _ -> None)

        Ok
          { Hosts =
              tierPairs
              |> List.filter (fun (id, _) -> (ruleFor id).IsSome)
              |> List.map (fun (id, meaning) -> { Id = id; Meaning = meaning })
            Tiers = tierPairs |> List.map fst
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

[<Emit("""(function(id, scopeSel){
  try {
    var root = document.querySelector(scopeSel);
    if (!root) { return; }
    var all = root.querySelectorAll('[data-fuaran-node-id]');
    for (var i = 0; i < all.length; i++) { all[i].style.outline = ''; }
    var esc = String(id).split('\\').join('\\\\').split('"').join('\\"');
    var el = root.querySelector('[data-fuaran-node-id="' + esc + '"]');
    if (!el) { return; }
    el.style.outline = '2px solid currentColor';
    el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  } catch (e) { }
})($0, $1)""")>]
let private reveal (nodeId: string) (scopeSelector: string) : unit = jsNative

/// The one sentence that keeps the preview honest.
let honesty =
  "This is a declared-fidelity simulation: it shows what the published render-fidelity manifest declares for each kind on the chosen tier, not that host's pixels, and it imitates no host's theme."

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

      match annotate manifest root hostId with
      | None -> Html.none
      | Some preview ->
        Html.div
          [ prop.className "fl-host-preview"
            prop.children
              [ Html.p [ prop.style [ style.fontSize (length.em 0.9) ]; prop.text honesty ]
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
                Html.p
                  [ prop.style [ style.fontSize (length.em 0.85) ]
                    prop.text preview.Host.Meaning ]
                Html.p [ prop.custom ("aria-live", "polite"); prop.text (summary preview) ]
                (match preview.Degraded with
                 | [] -> Html.none
                 | degraded ->
                   Html.ul
                     [ prop.children
                         [ for d in degraded ->
                             Html.li
                               [ Html.button
                                   [ prop.style [ style.textAlign.left ]
                                     prop.onClick (fun _ -> reveal (wrapperId d.Id) scope)
                                     prop.text (sprintf "%s (%s): %s" d.Id d.Kind (describe d.Fidelity)) ] ] ] ])
                Html.div
                  [ prop.className "fl-host-preview-root"
                    prop.children [ Render.renderWithSources BindingResolver.empty ignore (dress manifest hostId root) ] ] ] ]
