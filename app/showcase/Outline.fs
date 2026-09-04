module Fuaran.Showcase.Outline

// ============================================================================
//  The Outline — a hierarchy you walk with one focus (Phase 1120).
//  Pillar: "the machine can see the UI".
//
//  This kind spent a year undecided, and the argument that finally settled it is
//  not the obvious one. The obvious claim was DEPTH — that arbitrary nesting is
//  irreducible — and it does not hold: a finite literal nesting IS a static
//  composition, and a depth-forcing probe found nobody reaching for a container
//  to express unbounded nesting. That argument is struck rather than left
//  standing weakly, because a claim nobody re-examines is one the next proposal
//  inherits.
//
//  WHAT BREAKS THE COMPOSITION IS BEHAVIOUR. A list of disclosures is N
//  independent toggles: every row is its own tab stop, there is no focus that
//  walks the structure, Left does not close the row you are in and move to its
//  parent, Home does not go to the first row of the whole hierarchy, and no row
//  announces its depth or its position among its siblings. The tree pattern is
//  ONE composite widget with a ROVING TABINDEX — the whole thing is a single tab
//  stop and the arrow keys move a focus inside it — and that is not a property
//  any arrangement of independently-focusable containers has.
//
//  THE COUNTER-PRECEDENT, ANSWERED. The navigation cluster declined a nav kind
//  because an ARIA role already carried the landmark: the fact needing saying
//  was an attribute, and an attribute is data on a box. No projection can do the
//  same here. The roles and levels could be carried that way; the roving focus,
//  the six key bindings and the expand-collapse-traverse semantics are not
//  attributes at all — they are a behaviour a host performs over rows it has not
//  yet expanded.
//
//  SCOPE IS DELIBERATELY NARROW. Static recursive items only. Finite static
//  nesting stays with `Disclosure`, so this kind sits BESIDE that composition
//  rather than swallowing it — the discriminator a reader (and an emitter) is
//  taught is *keyboard traversal over a hierarchy*, not *nesting*. A bound or
//  lazily-fetched children source is reserved and out.
//
//  BOTH READER BEHAVIOURS ARE NAMED STATE KEYS. `expandedStateKey` carries the
//  set of open rows, `selectionStateKey` the focused one. The key IS the
//  affordance: there is no `expandable` boolean, because a flag with no key
//  behind it is a decorative control writing state nothing reads, and no
//  per-item `expanded` flag, because a node-local shadow copy is free to
//  disagree with the key.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

let private kExpanded = "outline.expanded"
let private kSelected = "outline.selected"

let private item (id: string) (label: string) (children: TreeItem list) : TreeItem =
  { Defaults.treeItem with
      Id = id
      Label = TextSource.Literal label
      Children = children }

let private leaf (id: string) (label: string) : TreeItem = item id label []

/// A publisher's catalogue: series, then works, then editions. Three levels
/// deep with an uneven shape, which is what a reader actually has to walk.
let private items: TreeItem list =
  [ item
      "highlands"
      "Highland & Island series"
      [ item
          "carmina"
          "Carmina Gadelica"
          [ leaf "carmina-1900" "First edition, 1900"
            leaf "carmina-1928" "Second edition, 1928"
            leaf "carmina-1992" "Scholarly reprint, 1992" ]
        item
          "waulking"
          "Waulking Songs of Barra"
          [ leaf "waulking-1938" "Field recordings, 1938"
            leaf "waulking-1977" "Annotated edition, 1977" ]
        leaf "gaelic-place" "Gaelic Place-Names of the West" ]
    item
      "urban"
      "Urban histories"
      [ item
          "tenement"
          "The Glasgow Tenement"
          [ leaf "tenement-1979" "First edition, 1979"
            leaf "tenement-2014" "Revised, 2014" ]
        leaf "clyde" "Shipbuilding on the Clyde"
        leaf "necropolis" "The Necropolis: a reading" ]
    item
      "reference"
      "Reference"
      [ leaf "dsl" "Dictionary of the Scots Language"
        item
          "atlas"
          "Linguistic Atlas"
          [ leaf "atlas-i" "Volume I — phonology"
            leaf "atlas-ii" "Volume II — morphology"
            leaf "atlas-iii" "Volume III — lexis" ] ]
    leaf "ephemera" "Ephemera and broadsides" ]

let private catalogue: Node<obj> =
  Fuaran.treeSpec
    "ol-tree"
    { Defaults.tree<obj> with
        Items = items
        ExpandedStateKey = Some kExpanded
        SelectionStateKey = Some kSelected }

/// The same hierarchy as the composition it is NOT. Two levels of disclosure,
/// each its own tab stop — perfectly good markup, and unable to be walked. It
/// is here so the discriminator is visible rather than described.
let private asDisclosures: Node<obj> =
  let work (id: string) (label: string) (editions: string list) : Node<obj> =
    Fuaran.disclosure
      id
      { Defaults.disclosure<obj> with
          Heading = TextSource.Literal label
          Children = [ Fuaran.list (id + "-eds") editions ] }

  Fuaran.box
    "ol-disclosures"
    { Layout = LayoutMode.Flex(Orientation.Vertical, false, Some 6)
      Role = BoxRole.Group
      KeepTogether = false
      BreakBefore = false
      Heading = Some(TextSource.Literal "Highland & Island series")
      Children =
        [ work
            "ol-d-carmina"
            "Carmina Gadelica"
            [ "First edition, 1900"; "Second edition, 1928"; "Scholarly reprint, 1992" ]
          work "ol-d-waulking" "Waulking Songs of Barra" [ "Field recordings, 1938"; "Annotated edition, 1977" ] ] }

let private wire: string = CJson.encodeNode catalogue

// ─── the page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private OutlineView () : ReactElement =
  StateStore.useStateKeys (Set.ofList [ kExpanded; kSelected ]) |> ignore

  React.useEffectOnce (fun () ->
    StateStore.set kExpanded (box [ "highlands"; "carmina" ])
    StateStore.set kSelected (box "carmina-1928"))

  let selected =
    match StateStore.get kSelected with
    | Some v -> unbox<string> v
    | None -> "—"

  let expandedCount =
    match StateStore.get kExpanded with
    | Some v ->
      try
        List.length (unbox<string list> v)
      with _ ->
        0
    | None -> 0

  let treePanel =
    Exhibit.panel
      "One tab stop, six keys"
      "Tab into it once — the whole hierarchy is a single stop. Then Down and Up move the focus through what is open, Right opens a row or steps into it, Left closes it or steps out to its parent, and Home and End go to the first and last row of the whole thing."
      [ Html.div [ prop.className "ol-tree"; prop.children [ Exhibit.renderLive catalogue ] ]
        Html.div
          [ prop.className "ol-bar"
            prop.children
              [ Html.span [ prop.className "ol-bar-k"; prop.text "selectionStateKey" ]
                Html.span [ prop.className "ol-bar-v"; prop.text selected ]
                Html.span [ prop.className "ol-bar-k"; prop.text "expandedStateKey" ]
                Html.span [ prop.className "ol-bar-v"; prop.text (string expandedCount + " open") ] ] ]
        Html.p
          [ prop.className "ol-note"
            prop.text
              "Both values are read from the keys the tree writes — not from anything this page kept in step. Move the focus and they move." ] ]

  let comparePanel =
    Exhibit.panel
      "The composition this is NOT"
      "Two levels of disclosure over the same works. Perfectly good markup — and try to walk it. Every heading is its own tab stop, Left does not take you out to the parent, Home does not take you to the top, and no row tells you how deep it is or how many siblings it has."
      [ Exhibit.renderStatic asDisclosures ]

  let point (text: string) = Html.li [ prop.text text ]

  let argumentPanel =
    Exhibit.panel
      "Why this became a kind, and why the obvious argument failed"
      ""
      [ Html.ul
          [ prop.className "px-points"
            prop.children
              [ point
                  "The obvious claim was DEPTH — that arbitrary nesting cannot be composed. It does not hold: a finite literal nesting is a static composition, and nobody was reaching for a container to express unbounded depth. That argument is struck rather than quietly kept, because a weak claim left standing is one the next proposal inherits."
                point
                  "What breaks the composition is BEHAVIOUR. A roving focus over a hierarchy, six key bindings, and expand-collapse-traverse semantics over rows a host has not yet expanded are not attributes, so no arrangement of independently-focusable containers has them."
                point
                  "The counter-precedent is answered rather than ignored. A navigation kind was declined because an ARIA role already carried the landmark — an attribute, expressible as data on a box. Roles and levels could be carried that way here too; the behaviour cannot."
                point
                  "Scope is narrow on purpose. Static items only; a bound or lazily-fetched children source is reserved and out. The discriminator an emitter is taught is keyboard traversal over a hierarchy, not nesting — so Disclosure keeps its job rather than being swallowed."
                point
                  "There is no expandable boolean and no per-item expanded flag. A flag with no key behind it is a decorative control writing state nothing reads; a node-local copy of the open set is free to disagree with the key. The key IS the affordance." ] ]
        Exhibit.wireDrawer "Show the tree's wire — the first self-referential record in the vocabulary" wire ]

  Exhibit.shell
    "outline"
    "The Outline"
    "A publisher's catalogue, three levels deep. One tab stop for the whole thing, arrows to walk it, and two State keys carrying what is open and what is focused — because the affordance and the key are the same fact."
    [ treePanel; comparePanel; argumentPanel ]
    [ Exhibit.Claim.Verified
        "The traversal is the shipped renderer's own tree rendering. Tab into it and the whole hierarchy is one stop; the arrow keys move a focus inside it. This page implements no key handling."
      Exhibit.Claim.Verified
        "The two read-outs resolve the tree's own State keys. They are the values the tree renders from, not a copy."
      Exhibit.Claim.Verified
        "The comparison below it is a real Disclosure composition over the same works, rendered by the same renderer. The difference you can feel with the keyboard is the entire case for the kind."
      Exhibit.Claim.Limit
        "The items are static, which is the kind's declared scope rather than a shortcut. A hierarchy whose children arrive when a row opens needs machinery this kind deliberately does not have, and it is reserved rather than faked."
      Exhibit.Claim.Limit
        "The catalogue is invented. A three-level publisher's list is simply an honest shape for something a reader genuinely has to walk." ]

let page: ReactElement = OutlineView()
