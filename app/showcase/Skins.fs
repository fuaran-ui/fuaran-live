module Fuaran.Showcase.Skins

// ============================================================================
//  Infinite Skins – intent, not implementation. Pillar: "intent, not
//  implementation".
//
//  ONE app tree (a small billing dashboard) rendered live across five design
//  systems – Pure-Fuaran, Tailwind, shadcn, Material, Dark – by swapping only
//  the theme value passed to the renderer. A permanent caption shows the tree's
//  canonical hash: it never changes, because the authored artefact carries
//  intent (Tone.Brand, Weight.Standard), not a single pixel value; the theme
//  decides what "brand" looks like.
//
//  Brand it: pick a brand colour and the whole app re-projects – the per-tenant
//  branding mechanism in one interaction. Drag the brand somewhere illegible
//  and the auditor fires: it runs the SHIPPED `Fuaran.UI.StyleObserver` WCAG
//  derivation over the browser's real computed colours (read back from the
//  rendered DOM), so `ContrastBelowAA` is a machine-checked property of the
//  rendered UI, not a guess. Accessibility as a fact about UI-as-data.
// ============================================================================

open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.StyleObserver
open Fuaran.UI.StyleObserver.Flags

// ─── The five design systems (theme values over ONE tree) ───────────────────

type private Skin =
  { Name: string
    Backdrop: string
    BrandFg: string
    Theme: Theme }

let private stops (bg: string) (fg: string) (border: string) : ToneStops =
  { Background = ColorVar.Hex bg
    Foreground = ColorVar.Hex fg
    Border = ColorVar.Hex border }

/// Build a design-system theme by overriding the dominant knobs (surface /
/// subdued / brand tones, radius, border width) on the reference defaults –
/// the rest of the 149-token surface inherits, so every variable still resolves.
let private mkTheme
  (surface: string * string * string)
  (subdued: string * string * string)
  (brand: string * string * string)
  (radius: string)
  (border: string)
  : Theme =
  let d1, d2, d3 = surface
  let s1, s2, s3 = subdued
  let b1, b2, b3 = brand

  { Defaults.theme with
      Tones =
        { Defaults.theme.Tones with
            Default = stops d1 d2 d3
            Subdued = stops s1 s2 s3
            Brand = stops b1 b2 b3 }
      Radius =
        { Sm = radius
          Md = radius
          Lg = radius
          Full = "9999px" }
      BorderWidth = border }

let private skins: Skin list =
  [ { Name = "Pure-Fuaran"
      Backdrop = "#f9fafb"
      BrandFg = "#1d4ed8"
      Theme =
        mkTheme
          ("#ffffff", "#1f2937", "#e5e7eb")
          ("#f3f4f6", "#6b7280", "#d1d5db")
          ("#eff6ff", "#1d4ed8", "#93c5fd")
          "8px"
          "1px" }
    { Name = "Tailwind"
      Backdrop = "#f8fafc"
      BrandFg = "#4f46e5"
      Theme =
        mkTheme
          ("#ffffff", "#0f172a", "#e2e8f0")
          ("#f1f5f9", "#475569", "#cbd5e1")
          ("#eef2ff", "#4f46e5", "#a5b4fc")
          "6px"
          "1px" }
    { Name = "shadcn"
      Backdrop = "#ffffff"
      BrandFg = "#18181b"
      Theme =
        mkTheme
          ("#ffffff", "#09090b", "#e4e4e7")
          ("#f4f4f5", "#71717a", "#e4e4e7")
          ("#fafafa", "#18181b", "#d4d4d8")
          "10px"
          "1px" }
    { Name = "Material"
      Backdrop = "#fef7ff"
      BrandFg = "#6750a4"
      Theme =
        mkTheme
          ("#ffffff", "#1c1b1f", "#cac4d0")
          ("#f7f2fa", "#49454f", "#cac4d0")
          ("#eaddff", "#6750a4", "#d0bcff")
          "16px"
          "1px" }
    { Name = "Dark"
      Backdrop = "#0b1220"
      BrandFg = "#93c5fd"
      Theme =
        mkTheme
          ("#1e293b", "#e2e8f0", "#334155")
          ("#0f172a", "#94a3b8", "#334155")
          ("#1e3a8a", "#93c5fd", "#3b82f6")
          "8px"
          "1px" } ]

/// Override just the brand foreground (the tenant's brand colour). The
/// illegible-brand vignette rides this: a pale value drops the callout's
/// brand text below the WCAG floor.
let private withBrand (brandFg: string) (t: Theme) : Theme =
  { t with
      Tones =
        { t.Tones with
            Brand =
              { t.Tones.Brand with
                  Foreground = ColorVar.Hex brandFg } } }

// ─── The one app – intent only, no pixel values ─────────────────────────────

let private metric (nid: string) (label: string) (value: float) (tone: ToneVariant) : Node<unit> =
  Fuaran.metric
    nid
    { Defaults.metric with
        Label = TextSource.Literal label
        Value = Binding.Static(Some value)
        Tone = tone }

let private appTree: Node<unit> =
  Fuaran.box
    "sk-root"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
      Role = BoxRole.Dashboard
      KeepTogether = false
      BreakBefore = false
      Heading = Some(TextSource.Literal "Acme Billing")
      Children =
        [ Fuaran.box
            "sk-metrics"
            { Layout = LayoutMode.Flex(Orientation.Horizontal, true, None)
              Role = BoxRole.Group
              KeepTogether = false
              BreakBefore = false
              Heading = None
              Children =
                [ metric "sk-m-mrr" "MRR (£k)" 42.5 ToneVariant.Default
                  metric "sk-m-seats" "Active seats" 128.0 ToneVariant.Default
                  metric "sk-m-churn" "Churn %" 2.1 ToneVariant.Warning ] }
          Fuaran.callout
            "sk-cta"
            { Defaults.callout with
                Tone = ToneVariant.Brand
                Heading = Some(TextSource.Literal "You're on the Starter plan")
                Body = TextSource.Literal "Upgrade to Pro for usage analytics, seat management, and priority support." }
          Fuaran.card
            "sk-plan"
            { Defaults.card with
                Heading = Some(TextSource.Literal "This month")
                Children =
                  [ Fuaran.markdown "sk-p1" "**Invoices:** 3 paid · 1 pending"
                    Fuaran.markdown "sk-p2" "**Next charge:** £480 on the 1st"
                    Fuaran.markdown "sk-p3" "**Seats used:** 128 of 150" ] } ] }

/// The tree's canonical hash – constant across every skin (the whole point).
let private treeHash: string = Hashing.sha256Hex (CanonicalJson.encodeNode appTree)

let private treeJson: string = CanonicalJson.encodeNode appTree

let private shortHash (h: string) : string =
  if h.Length > 12 then h.Substring(0, 12) else h

// ─── The real style observer, read back from the DOM ────────────────────────

let private readNodeStyles: unit -> obj array =
  import "readNodeStyles" "./infinite-skins-audit.ts"

let private toRgba (o: obj) : Rgba =
  let r: float = o?r
  let g: float = o?g
  let b: float = o?b
  let a: float = o?a
  Rgba.rgba r g b a

/// Run the shipped manifest-free WCAG derivation over the real computed colours
/// of every text-painting node. Returns the flagged nodes only.
let private auditFlags () : (string * StyleFlag) list =
  readNodeStyles ()
  |> Array.toList
  |> List.collect (fun o ->
    let label: string = o?label
    let fg = toRgba (o?fg)
    let layers = (o?bgLayers: obj array) |> Array.toList |> List.map toRgba

    let input: StyleInput =
      { Foreground = fg
        BackgroundLayers = layers
        FontFamily = None
        EmittedTone = None }

    Flags.derive StyleObserverOptions.defaults input |> List.map (fun f -> label, f))

let private flagText (flag: StyleFlag) : string =
  match flag with
  | StyleFlag.ContrastBelowAA ratio -> sprintf "ContrastBelowAA – %.2f:1 (needs 4.5:1)" ratio
  | StyleFlag.InvisibleText ratio -> sprintf "InvisibleText – %.2f:1 (all but invisible)" ratio
  | StyleFlag.AccentIndistinct ratio -> sprintf "AccentIndistinct – %.2f:1" ratio
  | StyleFlag.TokenResolutionFailed slot -> sprintf "TokenResolutionFailed – %s" slot
  | StyleFlag.OffPaletteColour value -> sprintf "OffPaletteColour – %s" value
  | StyleFlag.UsageBudgetExceeded(t, _, o) -> sprintf "UsageBudgetExceeded – %s (%.0f%%)" t o
  | StyleFlag.ContrastBelowDeclaredFloor(role, ratio, floor) ->
    sprintf "ContrastBelowDeclaredFloor – %s %.2f:1 < %.2f:1" role ratio floor

// ─── The page (a Feliz function component with its own hooks) ────────────────

[<ReactComponent>]
let private SkinsView () : ReactElement =
  let activeIdx, setActiveIdx = React.useState 0
  let brandOverride, setBrandOverride = React.useState (None: string option)
  let flags, setFlags = React.useState ([]: (string * StyleFlag) list)

  let skin = skins.[activeIdx]

  let effectiveTheme =
    match brandOverride with
    | Some hex -> withBrand hex skin.Theme
    | None -> skin.Theme

  let brandValue =
    match brandOverride with
    | Some hex -> hex
    | None -> skin.BrandFg

  // Re-audit after the browser has actually repainted the new theme / brand.
  // A double requestAnimationFrame waits for style recalc + paint, so the
  // computed colours we read back are the ones the visitor now sees (a fixed
  // timeout races React 19's concurrent re-render).
  // Re-audit whenever the skin or brand changes. The effect runs after React
  // has committed the new theme <style>, and reading getComputedStyle forces a
  // synchronous style recalc – so the colours we read back are the ones the
  // visitor now sees, with no timer race.
  React.useEffect ((fun () -> setFlags (auditFlags ())), [| box activeIdx; box brandOverride |])

  let themeRail =
    Html.div
      [ prop.className "sk-rail"
        prop.children
          [ for i, s in List.indexed skins ->
              Html.button
                [ prop.className (if i = activeIdx then "sk-chip sk-chip-on" else "sk-chip")
                  prop.text s.Name
                  prop.onClick (fun _ ->
                    setActiveIdx i
                    setBrandOverride None) ] ] ]

  let hashCaption =
    Html.div
      [ prop.className "sk-hash"
        prop.children
          [ Html.span [ prop.className "sk-hash-lead"; prop.text "The app's JSON has not changed:" ]
            Html.code [ prop.className "sk-hash-code"; prop.text (shortHash treeHash + "…") ] ] ]

  let brandEditor =
    Html.div
      [ prop.className "sk-brand"
        prop.children
          [ Html.span [ prop.className "sk-brand-label"; prop.text "Brand colour" ]
            Html.input
              [ prop.className "sk-brand-swatch"
                prop.type' "color"
                prop.value brandValue
                prop.onChange (fun (v: string) -> setBrandOverride (Some v)) ]
            Html.code [ prop.className "sk-brand-hex"; prop.text brandValue ]
            (match brandOverride with
             | Some _ ->
               Html.button
                 [ prop.className "sk-brand-reset"
                   prop.text "Reset"
                   prop.onClick (fun _ -> setBrandOverride None) ]
             | None -> Html.none) ] ]

  let stage =
    Html.div
      [ prop.className "sk-stage"
        prop.style [ style.backgroundColor skin.Backdrop ]
        prop.children
          [ Render.themeStyleElement effectiveTheme
            Render.renderWithSources BindingResolver.empty ignore appTree ] ]

  let auditor =
    let bad =
      flags
      |> List.filter (fun (_, f) ->
        match f with
        | StyleFlag.ContrastBelowAA _
        | StyleFlag.InvisibleText _
        | StyleFlag.AccentIndistinct _ -> true
        | _ -> false)

    Html.div
      [ prop.className "sk-auditor"
        prop.children
          [ Html.div
              [ prop.className "sk-auditor-head"
                prop.children
                  [ Html.span
                      [ prop.className (
                          if List.isEmpty bad then
                            "sk-audit-dot sk-audit-ok"
                          else
                            "sk-audit-dot sk-audit-bad"
                        ) ]
                    Html.span
                      [ prop.className "sk-auditor-title"
                        prop.text "Contrast auditor · real StyleObserver, live from the DOM" ] ] ]
            (if List.isEmpty bad then
               Html.p
                 [ prop.className "sk-audit-clear"
                   prop.text
                     "Every text node clears WCAG AA (4.5:1). Drag the brand colour toward its background to watch the observer flag it." ]
             else
               Html.ul
                 [ prop.className "sk-audit-list"
                   prop.children
                     [ for label, f in bad ->
                         Html.li
                           [ prop.className "sk-audit-item"
                             prop.children
                               [ Html.code [ prop.className "sk-audit-flag"; prop.text (flagText f) ]
                                 Html.span [ prop.className "sk-audit-node"; prop.text ("“" + label + "”") ] ] ] ] ]) ] ]

  let wireDrawer =
    Html.details
      [ prop.className "sk-wire-drawer"
        prop.children
          [ Html.summary [ prop.text "The app's JSON – search it for a colour" ]
            Html.p
              [ prop.className "sk-wire-note"
                prop.text
                  "Nowhere in here is there a hex code. The callout says Tone.Brand; each skin decides what brand looks like. Intent travels; the theme resolves the pixels." ]
            Html.pre [ prop.className "sk-wire"; prop.text treeJson ] ] ]

  let honesty =
    Html.div
      [ prop.className "sk-honesty"
        prop.children
          [ Html.h3 [ prop.text "Why this works" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "The authored app is intent, not CSS – semantic tones (Tone.Brand) and weights, never pixels. A theme is a value; swapping it re-skins the identical tree, and the canonical hash above proves the JSON never moved." ]
                    Html.li
                      [ prop.text
                          "All five looks are genuine theme projections – different palettes, radii, and borders resolved from the same tokens. No CSS filter tricks." ]
                    Html.li
                      [ prop.text
                          "The auditor is the real style observer: it reads the browser's computed colours back from the rendered DOM and runs the shipped WCAG derivation. ContrastBelowAA is a fact the machine checks about the pixels – accessibility as a property of UI-as-data." ] ] ] ] ]

  Html.div
    [ prop.className "sk-page"
      prop.children
        [ Html.h1 [ prop.className "sk-title"; prop.text "Infinite Skins" ]
          Html.p
            [ prop.className "sk-lede"
              prop.text
                "One app, re-skinned live across five design systems and any brand you paste in – and it never emitted a single pixel value. Watch the auditor catch a contrast violation the moment you create one." ]
          Html.div [ prop.className "sk-controls"; prop.children [ themeRail; brandEditor ] ]
          hashCaption
          stage
          auditor
          wireDrawer
          honesty ] ]

let page: ReactElement = SkinsView()
