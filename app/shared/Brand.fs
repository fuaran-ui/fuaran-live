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

/// A Fuaran dark theme in the loch / mist / ink brand palette – injected so
/// every rendered node re-colours in one move when the site is in dark mode.
/// Only the tone bg/fg/border flip; spacing / radius / type scale stay the
/// default (density is the same either way).
let darkTheme: Theme =
  let tone bg fg border =
    { Background = ColorVar.Hex bg
      Foreground = ColorVar.Hex fg
      Border = ColorVar.Hex border }

  { Defaults.theme with
      Tones =
        { Default = tone "#10171b" "#dce3e5" "#26333b"
          Subdued = tone "#18222a" "#93a2ab" "#2e3d47"
          Brand = tone "#163039" "#9fd6e8" "#62a5be"
          Success = tone "#123a2c" "#6fcea7" "#2e9d75"
          Warning = tone "#3d2f12" "#e6c25a" "#b8922e"
          Critical = tone "#3a1712" "#f0a99c" "#cf6350"
          Info = tone "#163039" "#9fd6e8" "#62a5be" } }

/// The theme to inject for the current dark flag – brand dark, or the default light.
let theme (dark: bool) : Theme =
  if dark then darkTheme else Defaults.theme

// ─── persisted preference ────────────────────────────────────────────────────
//  An explicit visitor choice (localStorage) wins; otherwise the OS setting.
//  Storage failures (private browsing, storage disabled) fall back to light –
//  the preference is a nicety, never a hard dependency.

[<Emit("(function(){ try { var v = localStorage.getItem('fuaran-theme'); if (v==='dark') return true; if (v==='light') return false; return !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches); } catch(e){ return false; } })()")>]
let initialDark () : bool = jsNative

[<Emit("(function(){ try { localStorage.setItem('fuaran-theme', $0 ? 'dark' : 'light'); } catch(e){} })()")>]
let persistDark (dark: bool) : unit = jsNative
