module Fuaran.Showcase.Responsive

// ============================================================================
//  Every Screen – one tree, phone / tablet / desktop, zero media queries.
//  Pillar: "intent, not implementation".
//
//  You author ONE tree: a dashboard whose grid declares `Cols = 3` – an intent,
//  not a breakpoint. You write no `@media` rule. The shipped `Fuaran.UI.Renderer`
//  reference CSS carries the responsive behaviour: below 768px the grid folds to
//  two columns, below 640px to one, overriding the author's inline
//  `repeat(3, 1fr)`. The wire JSON is identical at every width – it holds the
//  intent; the renderer holds the breakpoints.
//
//  The three frames are REAL iframes at genuine device viewport widths (375 /
//  768 / 1280): an iframe's content sees its own width as the viewport, so the
//  shipped media queries fire per-frame – this is the actual renderer reacting,
//  not a re-implementation. Each frame's column count is read back from its
//  computed `grid-template-columns` (a fact, not a caption). The frames are
//  scaled down to sit side by side; the LAYOUT is at true device width.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── interop ──────────────────────────────────────────────────────────────────

/// Serialise the shipped rules the tree needs – every rule that mentions a
/// `fuaran` class/token, plus `:root` custom properties, `@media` (the shipped
/// responsive breakpoints), and `@font-face`. The rules are copied VERBATIM from
/// the live stylesheets (the reference CSS is the bulk); we only drop the demo
/// site's own chrome rules, which the exhibit tree never references – this keeps
/// the iframe faithful to the shipped renderer while trimming the injected payload.
[<Emit("(function(){ var out=''; for (var i=0;i<document.styleSheets.length;i++){ try { var rs=document.styleSheets[i].cssRules; if(!rs) continue; for (var j=0;j<rs.length;j++){ var t=rs[j].cssText; if(!t) continue; if(t.indexOf('fuaran')>=0 || t.indexOf(':root')>=0 || t.indexOf('@media')>=0 || t.indexOf('@font-face')>=0){ out+=t+'\\n'; } } } catch(e){} } return out; })()")>]
let private getReferenceCss () : string = jsNative

[<Emit("(function(){ var el=document.getElementById($0); return el?el.innerHTML:''; })()")>]
let private innerHtmlOfId (id: string) : string = jsNative

/// Measure the column count the renderer chose inside every loaded frame – the
/// live `grid-template-columns` of each frame's `.fuaran-layout-grid`, at that
/// frame's own viewport. Re-scanned on every frame load (a stale merge into
/// captured state would keep only the last frame's count), so the returned
/// name→count pairs always reflect whatever is currently loaded.
[<Emit("(function(){ var out=[]; document.querySelectorAll('.rw-iframe').forEach(function(f){ try{ var d=f.contentDocument; var g=d&&d.querySelector('.fuaran-layout-grid'); var n=f.getAttribute('data-rw-name'); if(g&&n){ var c=getComputedStyle(g).gridTemplateColumns||''; out.push([n, c.trim().split(/\\s+/).filter(function(x){return x.length>0}).length]); } }catch(e){} }); return out; })()")>]
let private readAllCols () : (string * int)[] = jsNative

// ─── the one authored tree (Cols = 3 – an intent, not a breakpoint) ──────────

let private metricNode (id: string) (label: string) (value: float) (tone: ToneVariant) : Node<unit> =
  Fuaran.metric
    id
    { Defaults.metric with
        Label = TextSource.Literal label
        Value = Binding.Static(Some value)
        Tone = tone }

let private tree: Node<unit> =
  Fuaran.box
    "rw-root"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 16)
      Role = BoxRole.Dashboard
      Heading = Some(TextSource.Literal "Team dashboard")
      Children =
        [ Fuaran.gridLayout
            "rw-grid"
            { Defaults.gridLayout<unit> with
                Cols = 3
                Children =
                  [ metricNode "rw-m0" "Revenue" 128000.0 ToneVariant.Brand
                    metricNode "rw-m1" "Orders" 1318.0 ToneVariant.Default
                    metricNode "rw-m2" "Margin %" 58.0 ToneVariant.Success
                    metricNode "rw-m3" "Refunds" 42.0 ToneVariant.Warning
                    metricNode "rw-m4" "New users" 904.0 ToneVariant.Default
                    metricNode "rw-m5" "Churn %" 3.1 ToneVariant.Critical ] } ] }

let private treeWire: string = CJson.encodeNode tree

/// The responsive CSS the renderer SHIPS – the two rules you would otherwise
/// hand-author. Shown verbatim so the "you wrote zero" claim is checkable.
let private shippedMediaQueries =
  "/* shipped by Fuaran.UI.Renderer – you author none of this */\n"
  + "@media (max-width: 768px) {\n"
  + "  .fuaran-layout-grid { grid-template-columns: repeat(2, 1fr); }\n"
  + "}\n"
  + "@media (max-width: 640px) {\n"
  + "  .fuaran-layout-grid { grid-template-columns: 1fr; }\n"
  + "}"

let private buildDoc (css: string) (html: string) : string =
  "<!doctype html><html><head><meta charset=\"utf-8\">"
  + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><style>"
  + css
  + " html,body{margin:0} body{padding:14px;background:var(--fuaran-color-surface,#fbfbfd)}</style></head><body>"
  + html
  + "</body></html>"

// ─── the page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private ResponsiveView () : ReactElement =
  let srcDoc, setSrcDoc = React.useState (None: string option)
  let cols, setCols = React.useState (Map.empty: Map<string, int>)
  let showWire, setShowWire = React.useState false

  // After mount, the offscreen tree is in the DOM – grab its markup + the live
  // stylesheets and build the iframe document once.
  React.useEffect (
    (fun () ->
      let html = innerHtmlOfId "rw-offscreen"

      if html <> "" then
        setSrcDoc (Some(buildDoc (getReferenceCss ()) html))),
    [||]
  )

  let devices = [ "Phone", 375; "Tablet", 768; "Desktop", 1280 ]

  let frame (name: string) (logical: int) (doc: string) : ReactElement =
    let displayW = 300.0
    let logicalH = 470.0
    let scale = displayW / float logical
    let n = Map.tryFind name cols |> Option.defaultValue 0

    Html.div
      [ prop.className "rw-frame"
        prop.children
          [ Html.div
              [ prop.className "rw-frame-head"
                prop.children
                  [ Html.span [ prop.className "rw-frame-name"; prop.text name ]
                    Html.span [ prop.className "rw-frame-w"; prop.text (sprintf "%dpx" logical) ]
                    Html.span
                      [ prop.className "rw-frame-cols"
                        prop.text (
                          if n = 0 then
                            "…"
                          else
                            sprintf "%d column%s" n (if n = 1 then "" else "s")
                        ) ] ] ]
            Html.div
              [ prop.className "rw-screen"
                prop.style [ style.height (int (logicalH * scale)) ]
                prop.children
                  [ Html.iframe
                      [ prop.className "rw-iframe"
                        prop.title (sprintf "The dashboard at %dpx" logical)
                        prop.custom ("data-rw-name", name)
                        prop.custom ("srcDoc", doc)
                        prop.style
                          [ style.width logical
                            style.height (int logicalH)
                            style.custom ("transform", sprintf "scale(%g)" scale)
                            style.custom ("transformOrigin", "top left") ]
                        prop.onLoad (fun _ -> setCols (Map.ofArray (readAllCols ()))) ] ] ] ] ]

  let framesRow =
    match srcDoc with
    | None -> Html.div [ prop.className "rw-loading"; prop.text "Rendering the tree at three widths…" ]
    | Some doc ->
      Html.div
        [ prop.className "rw-frames"
          prop.children [ for (name, w) in devices -> frame name w doc ] ]

  // offscreen render – the source of the frames' markup (width-independent).
  let offscreen =
    Html.div
      [ prop.id "rw-offscreen"
        prop.className "rw-offscreen"
        prop.ariaHidden true
        prop.children [ Render.renderWithSources BindingResolver.empty ignore tree ] ]

  let notWritten =
    Html.div
      [ prop.className "rw-notwritten"
        prop.children
          [ Html.div
              [ prop.className "rw-nw-head"
                prop.children
                  [ Html.span [ prop.className "rw-nw-title"; prop.text "The media queries you didn't write" ]
                    Html.span [ prop.className "rw-nw-sub"; prop.text "shipped by the renderer" ] ] ]
            Html.pre
              [ prop.className "rw-css"
                prop.children [ Html.code [ prop.text shippedMediaQueries ] ] ] ] ]

  let wireDrawer =
    Html.div
      [ prop.className "rw-wire"
        prop.children
          [ Html.button
              [ prop.className "rw-wire-toggle"
                prop.text (
                  if showWire then
                    "Hide the wire – it is the same at every width"
                  else
                    "Show the wire – one artefact behind all three frames"
                )
                prop.onClick (fun _ -> setShowWire (not showWire)) ]
            (if showWire then
               Html.pre
                 [ prop.className "rw-wire-json"
                   prop.children [ Html.code [ prop.text treeWire ] ] ]
             else
               Html.none) ] ]

  let honesty =
    Html.div
      [ prop.className "rw-honesty"
        prop.children
          [ Html.h3 [ prop.text "How honest is this?" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "The three frames are real iframes at genuine device viewport widths (375 / 768 / 1280). An iframe's content sees its own width as the viewport, so the shipped responsive CSS fires per-frame – this is the actual renderer reacting, not a re-implementation. Each column count is read back from that frame's live grid-template-columns; it is a measured fact, not a caption." ]
                    Html.li
                      [ prop.text
                          "You authored one tree with a grid that declares Cols = 3 – an intent, not a breakpoint. You wrote zero media queries. The renderer ships the two breakpoint rules shown above; the wire JSON carries only the intent, so it is byte-identical at every width." ]
                    Html.li
                      [ prop.text
                          "The frames are scaled down so they sit side by side, but each lays out at its true device width – the fold from three columns to one is the shipped renderer's, decided by each frame's own viewport." ]
                    Html.li
                      [ prop.children
                          [ Html.text "Declare the shape; the substrate resolves the CSS. This is the "
                            Html.a [ prop.href "#/pillar/intent"; prop.text "intent-not-implementation" ]
                            Html.text " thesis, applied to responsive layout." ] ] ] ] ] ]

  Html.div
    [ prop.className "rw-page"
      prop.children
        [ Html.h1 [ prop.className "rw-title"; prop.text "Every Screen" ]
          Html.p
            [ prop.className "rw-lede"
              prop.text
                "One tree at phone, tablet, and desktop widths – reflowing itself from three columns to one, with not a single media query written. You declare the grid; the renderer decides the breakpoints." ]
          framesRow
          offscreen
          Html.div [ prop.className "rw-grid2"; prop.children [ notWritten; wireDrawer ] ]
          honesty ] ]

let page: ReactElement = ResponsiveView()
