module Fuaran.Live.Brand

// ============================================================================
//  The Fuaran brand module – the single home for the loch / mist / paper / ink
//  palette as a rendered-node Theme, plus the persisted light/dark preference.
//  Chrome and content re-colour from ONE definition: the shell injects
//  `Brand.theme dark` via `themeStyleElement`, and the visitor's choice is
//  honoured across visits (an explicit choice first, then the OS setting).
// ============================================================================

open Fable.Core
open Fuaran.UI
open Fuaran.UI.Types

let private tone bg fg border =
  { Background = ColorVar.Hex bg
    Foreground = ColorVar.Hex fg
    Border = ColorVar.Hex border }

// The brand interaction matrices. These MUST stay byte-identical to the
// hover/focus/active/disabled blocks in app/brand/fuaran-brand.css (the
// injected theme and the brand stylesheet both emit the same 84 vars, and
// whichever mounts later wins — they may never disagree). Locked by
// test/brandThemeParity.test.ts.

let private focusRingMist: FocusRing =
  { Color = ColorVar.Hex "#62a5be"
    Width = "2px"
    Offset = "2px"
    Style = "solid" }

let private lightInteraction: Interaction =
  { FocusRing = focusRingMist
    Hover =
      { Default = tone "#f3eee1" "#10151a" "#cfc5ae"
        Subdued = tone "#e0d7c2" "#444e58" "#c4b89c"
        Brand = tone "#d3e2e6" "#183a45" "#7fb3c4"
        Success = tone "#d2e8dc" "#256a4c" "#6fba93"
        Warning = tone "#f1e4c3" "#745010" "#d3ae4e"
        Critical = tone "#eed6cd" "#a03a27" "#d57f66"
        Info = tone "#d6e6eb" "#2c6172" "#86b7c6" }
    Focus =
      { Default = tone "#fcfaf4" "#1a2026" "#62a5be"
        Subdued = tone "#eae3d3" "#55606b" "#62a5be"
        Brand = tone "#e2ecee" "#1e4754" "#62a5be"
        Success = tone "#e2f0e8" "#2e7d5b" "#62a5be"
        Warning = tone "#f7eed9" "#8a5f16" "#62a5be"
        Critical = tone "#f4e4de" "#b8442f" "#62a5be"
        Info = tone "#e4eff2" "#357588" "#62a5be" }
    Active =
      { Default = tone "#eae3d3" "#10151a" "#c4b89c"
        Subdued = tone "#d6cbb1" "#39424b" "#b9ab8b"
        Brand = tone "#c2d7dd" "#132e37" "#62a5be"
        Success = tone "#c0dece" "#1d573e" "#4fab7c"
        Warning = tone "#ead8ab" "#5e410d" "#c69d33"
        Critical = tone "#e7c6ba" "#88301f" "#cb6a4d"
        Info = tone "#c6dbe2" "#234e5c" "#69a6b9" }
    Disabled =
      let drained = tone "#eae3d3" "#8a939b" "#dad2c0"

      { Default = drained
        Subdued = drained
        Brand = drained
        Success = drained
        Warning = drained
        Critical = drained
        Info = drained } }

let private darkInteraction: Interaction =
  { FocusRing = focusRingMist
    Hover =
      { Default = tone "#18222a" "#ecf1f3" "#33434e"
        Subdued = tone "#1e2b34" "#a6b3bb" "#3a4c58"
        Brand = tone "#1b3a45" "#b5e1ef" "#7bb5cb"
        Success = tone "#164635" "#84d7b4" "#38b085"
        Warning = tone "#493917" "#ebcd74" "#c7a13c"
        Critical = tone "#461d16" "#f3b9ae" "#d97964"
        Info = tone "#1b3a45" "#b5e1ef" "#7bb5cb" }
    Focus =
      { Default = tone "#10171b" "#dce3e5" "#62a5be"
        Subdued = tone "#18222a" "#93a2ab" "#62a5be"
        Brand = tone "#163039" "#9fd6e8" "#62a5be"
        Success = tone "#123a2c" "#6fcea7" "#62a5be"
        Warning = tone "#3d2f12" "#e6c25a" "#62a5be"
        Critical = tone "#3a1712" "#f0a99c" "#62a5be"
        Info = tone "#163039" "#9fd6e8" "#62a5be" }
    Active =
      { Default = tone "#1e2b34" "#f4f7f8" "#3e5260"
        Subdued = tone "#24343f" "#b9c4ca" "#465b69"
        Brand = tone "#214551" "#cbeaf4" "#93c4d7"
        Success = tone "#1a533e" "#99dfc1" "#43c295"
        Warning = tone "#55431c" "#f0d78e" "#d5b04b"
        Critical = tone "#52231a" "#f6c9c0" "#e28f78"
        Info = tone "#214551" "#cbeaf4" "#93c4d7" }
    Disabled =
      let drained = tone "#18222a" "#6e7b84" "#26333b"

      { Default = drained
        Subdued = drained
        Brand = drained
        Success = drained
        Warning = drained
        Critical = drained
        Info = drained } }

/// A Fuaran dark theme in the loch / mist / ink brand palette – injected so
/// every rendered node re-colours in one move when the site is in dark mode.
/// Only the tone bg/fg/border flip; spacing / radius / type scale stay the
/// default (density is the same either way).
let darkTheme: Theme =
  { Defaults.theme with
      Tones =
        { Default = tone "#10171b" "#dce3e5" "#26333b"
          Subdued = tone "#18222a" "#93a2ab" "#2e3d47"
          Brand = tone "#163039" "#9fd6e8" "#62a5be"
          Success = tone "#123a2c" "#6fcea7" "#2e9d75"
          Warning = tone "#3d2f12" "#e6c25a" "#b8922e"
          Critical = tone "#3a1712" "#f0a99c" "#cf6350"
          Info = tone "#163039" "#9fd6e8" "#62a5be" }
      Interaction = darkInteraction }

/// The brand LIGHT theme – the warm paper/loch tone set, matching the shared
/// fuaran-brand.css light re-bind byte-for-byte. Injecting Defaults.theme here
/// (the pre-design-system behaviour) put the reference sheet's generic blues
/// back on top of the brand layer in light mode; the injected theme and the
/// brand stylesheet must agree in BOTH modes.
let lightTheme: Theme =
  { Defaults.theme with
      Tones =
        { Default = tone "#fcfaf4" "#1a2026" "#dad2c0"
          Subdued = tone "#eae3d3" "#55606b" "#cfc5ae"
          Brand = tone "#e2ecee" "#1e4754" "#9dc0cc"
          Success = tone "#e2f0e8" "#2e7d5b" "#93c9ae"
          Warning = tone "#f7eed9" "#8a5f16" "#ddbe6f"
          Critical = tone "#f4e4de" "#b8442f" "#de9a87"
          Info = tone "#e4eff2" "#357588" "#a3c8d2" }
      Interaction = lightInteraction }

/// The theme to inject for the current dark flag – brand dark or brand light.
let theme (dark: bool) : Theme = if dark then darkTheme else lightTheme

// ─── persisted preference ────────────────────────────────────────────────────
//  An explicit visitor choice (localStorage) wins; otherwise the OS setting.
//  Storage failures (private browsing, storage disabled) fall back to light –
//  the preference is a nicety, never a hard dependency.

[<Emit("(function(){ try { var v = localStorage.getItem('fuaran-theme'); if (v==='dark') return true; if (v==='light') return false; return !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches); } catch(e){ return false; } })()")>]
let initialDark () : bool = jsNative

[<Emit("(function(){ try { localStorage.setItem('fuaran-theme', $0 ? 'dark' : 'light'); } catch(e){} })()")>]
let persistDark (dark: bool) : unit = jsNative
