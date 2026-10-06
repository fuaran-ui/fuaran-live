module Fuaran.Live.Disclosure

// ============================================================================
//  The "More tools" disclosure — a `<details>` whose body exists only while it
//  is open (Phase 2047).
//
//  The playground re-renders on every prompt keystroke, every second of a run's
//  tick and every streamed emission. A tool panel rendered as a plain child of a
//  collapsed `<details>` is invisible but still RENDERED on each of those — the
//  host-fidelity preview is a second full render of the tree — so the collapsed
//  panels cost as much per keystroke as open ones. Here the body is a thunk, and
//  it is called only while the disclosure is open: a collapsed tool costs one
//  `<summary>`.
//
//  The open state is the browser's, not this component's. `openByDefault` is
//  passed to the element exactly as the playground always passed it, so the
//  native behaviour is unchanged — including Examples, which is open while
//  there is no tree and closes when one arrives because the attribute is
//  withdrawn. The component only LISTENS: the `toggle` event fires on every
//  change of the `open` attribute, whether a click or that withdrawal made it,
//  and the body follows it.
//
//  What a collapse does now discard is a tool body's own React state: closing
//  and reopening the host-fidelity preview returns it to its first tier. Every
//  other tool's state lives in the page model and survives.
//
//  This lives in its own module rather than in `App.fs` because `App.js` boots
//  the page when it is imported, so nothing in it can be exercised headlessly;
//  `test/keystrokeRecompute.test.ts` renders this component directly.
// ============================================================================

open Fable.Core.JsInterop
open Feliz

/// Whether the element that raised `ev` is an open `<details>`.
let private isOpenTarget (ev: Browser.Types.Event) : bool =
  let target = ev.currentTarget
  not (isNull target) && (target?``open``: bool) = true

/// A collapsible tool panel. `body` is called only while the panel is open.
[<ReactComponent>]
let ToolDetails (title: string) (openByDefault: bool) (body: unit -> ReactElement) : ReactElement =
  let isOpen, setOpen = React.useState openByDefault

  Html.details
    [ prop.className "pg-tool"
      if openByDefault then
        prop.custom ("open", true)
      prop.custom ("onToggle", (fun (ev: Browser.Types.Event) -> setOpen (isOpenTarget ev)))
      prop.children
        [ Html.summary [ prop.text title ]
          if isOpen then
            Html.div [ prop.className "pg-tool-body"; prop.children [ body () ] ] ] ]
