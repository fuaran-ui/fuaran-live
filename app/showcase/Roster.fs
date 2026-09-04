module Fuaran.Showcase.Roster

// ============================================================================
//  The Roster Board — rows that move between grids, and rows you can take away
//  (Phases 1123 / 1125). Pillar: "the app is a value".
//
//  TWO KEYS, NOT ONE SYMMETRIC SLOT. A grid may RELEASE rows onto a named State
//  key (`transferOutKey`) and a grid may ACCEPT rows arriving on one
//  (`transferInKey`). They are separate declarations because they are separate
//  permissions: a backlog that hands work out is not thereby a backlog that
//  takes work back, and a single symmetric flag would make every participating
//  grid a destination the moment it became a source. A grid declaring both does
//  each.
//
//  The key is what pairs them. Two grids naming the same key are two ends of one
//  channel; two grids naming different keys cannot exchange a row however
//  adjacent they look on screen, which is the point — adjacency is layout, and
//  a permission that came from layout would be a permission nobody granted.
//
//  EXPORT NAMES NO KEY, and it is the only grid behaviour that does not. Sort,
//  page, edit and transfer all write something back into the document's own
//  state; an export writes nothing anywhere — it hands the reader the rows the
//  client already holds, as RFC 4180 CSV, and the tree is unchanged afterwards.
//  So `exportable` is a bare declaration: the rows are the reader's to take.
//
//  Both halves are declarations rather than affordances. There is no "drag
//  handle" slot and no "download button" slot, because a document that could
//  author the control without the behaviour would be authoring a lie.
// ============================================================================

open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── state ───────────────────────────────────────────────────────────────────

let private kBacklog = "roster.backlog"
let private kSprint = "roster.sprint"
let private kChannel = "roster.move"

let private watchedKeys = Set.ofList [ kBacklog; kSprint; kChannel ]

let private card (id: string) (title: string) (owner: string) (points: float) : Row =
  Map.ofList [ "id", box id; "title", box title; "owner", box owner; "points", box points ]

let private backlogSeed: Row list =
  [ card "FU-412" "Chapter marks on the media kind" "Ailsa" 3.0
    card "FU-418" "Sandbox relaxations, closed set" "Ruaridh" 5.0
    card "FU-425" "Hint trait on every node" "Ailsa" 2.0
    card "FU-431" "Combobox free-text polarity" "Mhairi" 3.0
    card "FU-437" "Token field suggestion source" "Ruaridh" 2.0
    card "FU-444" "Tree roving tabindex" "Iona" 8.0 ]

let private sprintSeed: Row list =
  [ card "FU-401" "Print-break declarations" "Iona" 5.0
    card "FU-406" "Export as the reader's own rows" "Mhairi" 3.0 ]

let private rowText (field: string) (r: Row) : string =
  defaultArg (Map.tryFind field r |> Option.map string) ""

let private rowFloat (field: string) (r: Row) : float =
  match Map.tryFind field r with
  | Some v -> unbox<float> v
  | None -> 0.0

let private columns: Column<obj> list =
  [ Column.text "Ref" (rowText "id")
    Column.text "Work" (rowText "title")
    Column.text "Owner" (rowText "owner")
    Column.numeric "Points" (rowFloat "points") ]

/// The source grid. It declares `transferOutKey` and nothing else: it may hand
/// a row out, and it may not take one back. That asymmetry is the design, not
/// an omission — see the module header.
let private backlog: Node<obj> =
  Fuaran.grid
    "rs-backlog"
    id
    { Defaults.grid<Row, obj> with
        Source = Binding.State(kBacklog, Some(Seq.ofList backlogSeed))
        RowKey = rowText "id"
        TransferOutKey = Some kChannel
        Columns = columns }

/// The destination grid. It declares BOTH — it accepts rows from the backlog,
/// and it can hand one back. Two declarations, because they are two decisions.
/// It is also `exportable`: what is in this sprint is the team's to take away.
let private sprint: Node<obj> =
  Fuaran.grid
    "rs-sprint"
    id
    { Defaults.grid<Row, obj> with
        Source = Binding.State(kSprint, Some(Seq.ofList sprintSeed))
        RowKey = rowText "id"
        TransferInKey = Some kChannel
        TransferOutKey = Some kChannel
        Exportable = true
        Columns = columns }

/// A third grid, deliberately unable to participate. Same shape, same layout,
/// adjacent on screen — and it names a different channel, so nothing can be
/// dropped into it. Adjacency is layout; a permission that came from layout
/// would be a permission nobody granted.
let private archive: Node<obj> =
  Fuaran.grid
    "rs-archive"
    id
    { Defaults.grid<Row, obj> with
        Source = Binding.Static(Some(Seq.ofList [ card "FU-388" "Retired: table kind" "—" 0.0 ]))
        RowKey = rowText "id"
        TransferInKey = Some "roster.archive"
        Exportable = true
        Columns = columns }

let private wireBacklog: string = CJson.encodeNode backlog
let private wireSprint: string = CJson.encodeNode sprint

// ─── the page ────────────────────────────────────────────────────────────────

let private readRows (key: string) (fallback: Row list) : Row list =
  match StateStore.get key with
  | Some v -> unbox<Row seq> v |> List.ofSeq
  | None -> fallback

let private seed () : unit =
  StateStore.set kBacklog (box (Seq.ofList backlogSeed))
  StateStore.set kSprint (box (Seq.ofList sprintSeed))

[<ReactComponent>]
let private RosterView () : ReactElement =
  StateStore.useStateKeys watchedKeys |> ignore
  React.useEffectOnce (fun () -> seed ())

  let backlogNow = readRows kBacklog backlogSeed
  let sprintNow = readRows kSprint sprintSeed
  let sprintPoints = sprintNow |> List.sumBy (rowFloat "points")

  let counter (label: string) (value: string) =
    Html.div
      [ prop.className "rs-count"
        prop.children
          [ Html.span [ prop.className "rs-count-n"; prop.text value ]
            Html.span [ prop.className "rs-count-l"; prop.text label ] ] ]

  let boardPanel =
    Exhibit.panel
      "Move a row between the two grids"
      "Take the move handle on a backlog row and drop it into the sprint, or back again. Nothing here is a drag-and-drop library: both grids declare the same channel key, and the renderer draws the affordance because the declaration is there."
      [ Html.div
          [ prop.className "rs-counts"
            prop.children
              [ counter "in the backlog" (string (List.length backlogNow))
                counter "in the sprint" (string (List.length sprintNow))
                counter "points committed" (string sprintPoints) ] ]
        Html.div
          [ prop.className "rs-board"
            prop.children
              [ Html.div
                  [ prop.className "rs-pane"
                    prop.children
                      [ Html.h4 [ prop.text "Backlog — releases rows only" ]
                        Exhibit.renderLive backlog ] ]
                Html.div
                  [ prop.className "rs-pane"
                    prop.children
                      [ Html.h4 [ prop.text "Sprint — accepts, releases, and exports" ]
                        Exhibit.renderLive sprint ] ] ] ]
        Html.p
          [ prop.className "rs-note"
            prop.text
              "The counts above are read from the two State keys the grids write, not from anything this page kept in step. Move a row and they move." ]

        ]

  let refusalPanel =
    Exhibit.panel
      "The grid that cannot take a row, and looks exactly like one that can"
      "Same kind, same columns, sitting right beside the board. It declares a DIFFERENT channel key, so nothing from the backlog can be dropped here however close it is. Adjacency is layout; the permission is a name."
      [ Exhibit.renderLive archive ]

  let point (text: string) = Html.li [ prop.text text ]

  let exportPanel =
    Exhibit.panel
      "Export names no key, and it is the only behaviour that does not"
      "Use the export control on the sprint grid: it serialises the rows the client already holds to RFC 4180 CSV and hands them to you. Nothing is written back into the document, and the tree is unchanged afterwards."
      [ Html.ul
          [ prop.className "px-points"
            prop.children
              [ point
                  "Sort, page, edit and transfer each write something into the document's own state, so each of them names the State key it writes. An export writes nothing anywhere — so exportable is a bare declaration and not a key."
                point
                  "What is exported is what the CLIENT holds. On a grid whose rows arrive a page at a time that is the page in hand, and the control says so rather than pretending to a completeness it cannot have."
                point
                  "There is no download-button slot in the vocabulary. A document that could author the control without the behaviour would be authoring a lie, so the affordance is the renderer's and the declaration is the document's." ] ]
        Exhibit.wireDrawer "The backlog's wire — one key, out only" wireBacklog
        Exhibit.wireDrawer "The sprint's wire — both keys, and exportable" wireSprint ]

  Exhibit.shell
    "roster"
    "The Roster Board"
    "Two grids that exchange rows because they name the same channel, one that cannot because it names another, and a table whose rows the reader is allowed to take away. Three declarations on the wire; no drag library and no download button anywhere."
    [ boardPanel; refusalPanel; exportPanel ]
    [ Exhibit.Claim.Verified
        "Both grids are the shipped renderer's own DataGrid rendering. The move affordance appears because a transfer key is declared, and it would disappear if you removed the declaration."
      Exhibit.Claim.Verified
        "The counts and the committed points are resolved from the two State keys the grids write. They are the same values the grids render from, not a copy this page maintains."
      Exhibit.Claim.Verified
        "The archive grid genuinely refuses. It is not disabled and not styled differently — it simply names a channel nothing else names, which is the whole mechanism."
      Exhibit.Claim.Verified
        "The export writes a real CSV of the rows in hand. Open it: it is the resolved cell text, quoted per RFC 4180."
      Exhibit.Claim.Limit
        "Nothing is persisted. The two keys live in this tab's store, so a reload puts every card back where it started — which is what a page with no backend should do." ]

let page: ReactElement = RosterView()
