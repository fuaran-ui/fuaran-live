module Fuaran.Showcase.Geometry

// ============================================================================
//  Drag the Points — conformal geometric algebra in the browser: a circle, a
//  sign and the Voronoi cells, from one published package (Phase 2851).
//
//  The geometry is `Fuaran.Compute.GeometricAlgebra`, the conformal geometric
//  algebra package, restored from the public registry and compiled by Fable
//  with the rest of this page. Nothing is fetched and no server answers: the
//  circle, the side and every cell are computed in the reader's browser.
//
//  Two claims, each one the reader can check on the screen:
//
//   * GEOMETRY AS ONE OPERATION. The circle through three points is one outer
//     product of the three conformal points (`Circle.through`). Whether a
//     fourth point is inside it is the sign of that point's power against the
//     circle (`Circle.side`). The Delaunay triangles are that same test applied
//     to every triple of points, and the Voronoi cells are read off the
//     triangles' centres, so the aggregate picture is built from the primitive
//     the reader has just watched flip.
//   * THE SAME BITS EVERYWHERE. The package pins its arithmetic, so it answers
//     the same bits under Fable as on .NET. The bytes panel shows the current
//     circle's coefficients as bit patterns, and recomputes one published law
//     vector of the package here, coefficient by coefficient against its
//     published bits.
//
//  What is the page's own arithmetic, and so NOT pinned: the drawing's
//  coordinates (the centre and radius read back for the SVG), the angle that
//  orders a cell's corners, and the clipping of each cell to the frame. They
//  place pixels; no claim on the page rests on them.
//
//  The figure is a `Drawing` node rendered by the shipped renderer. The drag is
//  host-side pointer handling on the stage around it, because the language
//  carries no drag gesture.
// ============================================================================

open System
open Fable.Core
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.Compute.GeometricAlgebra

module CJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── bits ────────────────────────────────────────────────────────────────────

let private hexDigits = "0123456789abcdef"

/// A double's IEEE 754 bit pattern as sixteen lowercase hex digits, the form
/// the package's law vectors publish.
let bitsOf (x: float) : string =
  let u = uint64 (BitConverter.DoubleToInt64Bits x)

  String.init 16 (fun i ->
    let nibble = int ((u >>> (60 - 4 * i)) &&& 15UL)
    string hexDigits.[nibble])

/// The double a sixteen-digit bit pattern names.
let ofBits (hex: string) : float =
  let mutable u = 0UL

  for ch in hex do
    u <- (u <<< 4) ||| uint64 (hexDigits.IndexOf(Char.ToLowerInvariant ch))

  BitConverter.Int64BitsToDouble(int64 u)

// ─── the geometry ────────────────────────────────────────────────────────────

/// A point of the plane, in the drawing's coordinates.
type Pt = float * float

let private conformal ((x, y): Pt) : Point option =
  match Point.up (x, y, 0.0) with
  | Ok p -> Some p
  | Error _ -> None

/// The circle through three points of the plane: one outer product.
let circleThrough (a: Pt) (b: Pt) (c: Pt) : Circle option =
  match conformal a, conformal b, conformal c with
  | Some p, Some q, Some r -> Some(Circle.through p q r)
  | _ -> None

/// Where a point is against a circle: the sign of its power, at threshold zero.
let sideOf (d: Pt) (c: Circle) : Side option =
  match conformal d with
  | Some p ->
    match Circle.side 0.0 p c with
    | Ok s -> Some s
    | Error _ -> None
  | None -> None

/// The point's power against the circle: negative inside.
let powerOf (d: Pt) (c: Circle) : float option =
  match conformal d with
  | Some p ->
    match Circle.power p c with
    | Ok v -> Some v
    | Error _ -> None
  | None -> None

/// The centre and radius of a round circle, read back for drawing. A flat (three
/// collinear points) or any refusal reads as no circle.
let roundOf (c: Circle) : (float * float * float) option =
  match Circle.kind 0.0 c with
  | Ok(CircleKind.Round _) ->
    match Circle.centre c, Circle.squaredRadius c with
    | Ok(x, y, _), Ok r2 when r2 > 0.0 && not (Double.IsInfinity r2) -> Some(x, y, sqrt r2)
    | _ -> None
  | _ -> None

/// Three far points around the frame, so that every real point's cell is
/// bounded and can be clipped to the frame. They are never drawn.
let private sentinels: Pt list =
  [ (50.0, -4000.0); (-4000.0, 4000.0); (4100.0, 4000.0) ]

/// The Delaunay triangles of the points, as index triples into `points` (the
/// sentinels follow the real points): every triple whose circle holds no other
/// point INSIDE it, by the package's side test, with the triangle's centre.
let delaunay (points: Pt list) : ((int * int * int) * Pt) list =
  let all = Array.ofList (points @ sentinels)
  let n = all.Length

  [ for i in 0 .. n - 3 do
      for j in i + 1 .. n - 2 do
        for k in j + 1 .. n - 1 do
          match circleThrough all.[i] all.[j] all.[k] with
          | Some c ->
            match roundOf c with
            | Some(cx, cy, _) ->
              let empty =
                seq { 0 .. n - 1 }
                |> Seq.forall (fun m -> m = i || m = j || m = k || sideOf all.[m] c <> Some Side.Inside)

              if empty then
                yield (i, j, k), (cx, cy)
            | None -> ()
          | None -> () ]

/// The frame the figure draws in.
let width = 100.0
let height = 64.0

/// Clip a convex polygon to the frame (Sutherland–Hodgman, one edge at a time).
let private clipToFrame (poly: Pt list) : Pt list =
  let clipEdge (inside: Pt -> bool) (cross: Pt -> Pt -> Pt) (pts: Pt list) : Pt list =
    match pts with
    | [] -> []
    | _ ->
      let arr = Array.ofList pts

      [ for idx in 0 .. arr.Length - 1 do
          let cur = arr.[idx]
          let prev = arr.[(idx + arr.Length - 1) % arr.Length]

          match inside cur, inside prev with
          | true, true -> yield cur
          | true, false ->
            yield cross prev cur
            yield cur
          | false, true -> yield cross prev cur
          | false, false -> () ]

  let atX (x0: float) ((x1, y1): Pt) ((x2, y2): Pt) : Pt =
    x0, y1 + (y2 - y1) * (x0 - x1) / (x2 - x1)

  let atY (y0: float) ((x1, y1): Pt) ((x2, y2): Pt) : Pt =
    x1 + (x2 - x1) * (y0 - y1) / (y2 - y1), y0

  poly
  |> clipEdge (fun (x, _) -> x >= 0.0) (atX 0.0)
  |> clipEdge (fun (x, _) -> x <= width) (atX width)
  |> clipEdge (fun (_, y) -> y >= 0.0) (atY 0.0)
  |> clipEdge (fun (_, y) -> y <= height) (atY height)

/// Each real point's Voronoi cell: the centres of the Delaunay triangles that
/// touch it, in angular order around it, clipped to the frame.
let cells (points: Pt list) (triangles: ((int * int * int) * Pt) list) : Pt list list =
  points
  |> List.mapi (fun i (px, py) ->
    triangles
    |> List.filter (fun ((a, b, c), _) -> a = i || b = i || c = i)
    |> List.map snd
    |> List.distinct
    |> List.sortBy (fun (x, y) -> Math.Atan2(y - py, x - px))
    |> clipToFrame)

// ─── the page's state ────────────────────────────────────────────────────────

let private names = [| "A"; "B"; "C"; "D"; "E"; "F"; "G" |]

/// The starting points: A, B and C make the circle, D is the point tested
/// against it (inside, to begin with), and E to G complete the cells.
let initialPoints: Pt list =
  [ 22.0, 40.0
    50.0, 14.0
    78.0, 40.0
    54.0, 34.0
    10.0, 12.0
    90.0, 10.0
    46.0, 58.0 ]

let private cellPaint =
  [| "#7aa6d8"
     "#e3a96b"
     "#8cc59a"
     "#d68aa0"
     "#b19bd6"
     "#d7c86e"
     "#7cc4c4" |]

// ─── the published example ───────────────────────────────────────────────────

/// One law vector of the package, `Circle.through-general-1`, as the package's
/// conformance file publishes it at release 0.47.0: three conformal points,
/// each as its five coefficients' bit patterns, and the circle's ten
/// coefficients' bit patterns. The package's own tests replay it on .NET and
/// under Fable.
let publishedId = "Circle.through-general-1"

let publishedInput: string array array =
  [| [| "bff5869070800000"
        "3fb1cca2ba487590"
        "bfdff02e208b8827"
        "bfaad8e906d76835"
        "bfb86a544cc3f15c" |]
     [| "bff1eef339800000"
        "bfd1d6428a3b4258"
        "3fd47ca4b6b7c53e"
        "bfd2b312d146b052"
        "bfbe5258989e17e6" |]
     [| "3fd15f4883800000"
        "3f8a7b1b6aaf506d"
        "bfa4b56834b31cbd"
        "bfb1e1789e9f5704"
        "3f8934d315c50752" |] |]

let publishedExpected: string array =
  [| "bfa30f8bb05bf4a5"
     "bfa7436b2629c064"
     "bf72a7e28475b123"
     "3fc03b1c420b39aa"
     "3f8d388bd4ed4a5c"
     "3f5f21cd3ad42b58"
     "3f821fa9f81a79ed"
     "bf5ba3e1c0088025"
     "bf69bc8ed53470d0"
     "3f82b6e2757a2d2e" |]

/// The names of a circle's ten blades, in the package's index order.
let bladeNames: string array =
  [| "n0∧e1∧e2"
     "n0∧e1∧e3"
     "n0∧e1∧n∞"
     "n0∧e2∧e3"
     "n0∧e2∧n∞"
     "n0∧e3∧n∞"
     "e1∧e2∧e3"
     "e1∧e2∧n∞"
     "e1∧e3∧n∞"
     "e2∧e3∧n∞" |]

/// The circle's ten coefficients, in the package's index order, with the name
/// of each blade.
let coefficients (c: Circle) : (string * float) list =
  [ c.N0E1E2
    c.N0E1E3
    c.N0E1NInf
    c.N0E2E3
    c.N0E2NInf
    c.N0E3NInf
    c.E1E2E3
    c.E1E2NInf
    c.E1E3NInf
    c.E2E3NInf ]
  |> List.mapi (fun i v -> bladeNames.[i], v)

let private pointOfBits (bits: string array) : Point =
  { N0 = ofBits bits.[0]
    E1 = ofBits bits.[1]
    E2 = ofBits bits.[2]
    E3 = ofBits bits.[3]
    NInf = ofBits bits.[4] }

/// The published example, computed here: the circle's coefficients' bits.
let computedExample () : string array =
  let p = pointOfBits publishedInput.[0]
  let q = pointOfBits publishedInput.[1]
  let r = pointOfBits publishedInput.[2]

  Circle.through p q r |> coefficients |> List.map (snd >> bitsOf) |> Array.ofList

// ─── the drawing ─────────────────────────────────────────────────────────────

let private round2 (x: float) : float = Math.Round(x * 100.0) / 100.0

let private paint (fill: string option) (stroke: string option) (strokeWidth: float option) (opacity: float option) =
  let bind v =
    v |> Option.map (fun x -> Binding.Static(Some x))

  { Defaults.drawStyle with
      Fill = bind fill
      Stroke = bind stroke
      StrokeWidth = bind strokeWidth
      Opacity = bind opacity }

let private sideColour (s: Side option) : string =
  match s with
  | Some Side.Inside -> "#2f9e5b"
  | Some Side.On -> "#c78a12"
  | Some Side.Outside -> "#d0453a"
  | None -> "#888888"

/// The figure, as a `Drawing` node: the cells, the triangles, the circle through
/// A, B and C, and the points.
let figure (points: Pt list) (selected: int) : Node<obj> =
  let tris = delaunay points
  let cellPolys = cells points tris
  let pts = Array.ofList points
  let realCount = pts.Length
  let circle = circleThrough pts.[0] pts.[1] pts.[2]
  let side = circle |> Option.bind (sideOf pts.[3])

  let cellShapes =
    cellPolys
    |> List.mapi (fun i poly ->
      Shape.Polygon(
        poly |> List.map (fun (x, y) -> { X = round2 x; Y = round2 y }),
        paint (Some cellPaint.[i % cellPaint.Length]) (Some "#ffffff") (Some 0.4) (Some 0.45)
      ))

  let edgeShapes =
    tris
    |> List.collect (fun ((a, b, c), _) -> [ a, b; b, c; a, c ])
    |> List.filter (fun (a, b) -> a < realCount && b < realCount)
    |> List.distinct
    |> List.map (fun (a, b) ->
      let (x1, y1) = pts.[a]
      let (x2, y2) = pts.[b]
      Shape.Line(round2 x1, round2 y1, round2 x2, round2 y2, paint None (Some "#5b6470") (Some 0.25) (Some 0.6)))

  let circleShape =
    match circle |> Option.bind roundOf with
    | Some(cx, cy, r) ->
      [ Shape.Circle(round2 cx, round2 cy, round2 r, paint (Some "none") (Some(sideColour side)) (Some 0.7) None) ]
    | None -> []

  let pointShapes =
    pts
    |> Array.toList
    |> List.mapi (fun i (x, y) ->
      let fill =
        if i < 3 then "#2b5fa8"
        elif i = 3 then sideColour side
        else "#3c434d"

      let ring =
        if i = selected then
          [ Shape.Circle(round2 x, round2 y, 3.2, paint (Some "none") (Some "#f2b705") (Some 0.6) None) ]
        else
          []

      ring
      @ [ Shape.Circle(round2 x, round2 y, 1.7, paint (Some fill) (Some "#ffffff") (Some 0.4) None)
          Shape.Label(
            round2 (x + 2.6),
            round2 (y - 2.2),
            TextSource.Literal names.[i],
            { paint (Some "#1d2329") None None None with
                FontSize = Some 3.6
                TextAnchor = Some TextAnchor.Start }
          ) ])
    |> List.concat

  Fuaran.drawingSpec
    "geometry-figure"
    { Defaults.drawing with
        ViewBox =
          { MinX = 0.0
            MinY = 0.0
            Width = width
            Height = height }
        Shapes = cellShapes @ edgeShapes @ circleShape @ pointShapes
        Title = Some(TextSource.Literal "Seven points, the circle through A, B and C, and the Voronoi cells")
        Description =
          Some(
            TextSource.Literal(
              "Point D is "
              + (match side with
                 | Some Side.Inside -> "inside"
                 | Some Side.On -> "on"
                 | Some Side.Outside -> "outside"
                 | None -> "not tested against")
              + " the circle through A, B and C."
            )
          ) }

// ─── pointer handling (host-side: the language carries no drag gesture) ──────

/// Client coordinates to the drawing's own coordinates, through the SVG's
/// current transform. `null` when the SVG is not mounted.
[<Emit("(function(el,x,y){ var s=el && el.querySelector('svg'); if(!s || !s.getScreenCTM) return null; var m=s.getScreenCTM(); if(!m) return null; var p=new DOMPoint(x,y).matrixTransform(m.inverse()); return [p.x,p.y]; })($0,$1,$2)")>]
let private toDrawing (el: obj) (x: float) (y: float) : float array = jsNative

[<Emit("(function(el,id){ try { el.setPointerCapture(id); } catch(e) {} })($0,$1)")>]
let private capture (el: obj) (id: float) : unit = jsNative

let private clampPt ((x, y): Pt) : Pt =
  Math.Max(1.0, Math.Min(width - 1.0, x)), Math.Max(1.0, Math.Min(height - 1.0, y))

let private nearest (points: Pt list) ((x, y): Pt) : int option =
  points
  |> List.mapi (fun i (px, py) -> i, (px - x) * (px - x) + (py - y) * (py - y))
  |> List.filter (fun (_, d2) -> d2 <= 36.0)
  |> List.sortBy snd
  |> List.tryHead
  |> Option.map fst

let private sideWord (s: Side option) : string =
  match s with
  | Some Side.Inside -> "inside"
  | Some Side.On -> "on"
  | Some Side.Outside -> "outside"
  | None -> "not comparable with"

// ─── the page ────────────────────────────────────────────────────────────────

[<ReactComponent>]
let private GeometryView () : ReactElement =
  let points, setPoints = React.useState initialPoints
  let selected, setSelected = React.useState 3
  let dragging, setDragging = React.useState false

  let move (i: int) (p: Pt) =
    setPoints (points |> List.mapi (fun j q -> if j = i then clampPt p else q))

  let at (e: Browser.Types.PointerEvent) : Pt option =
    let v = toDrawing e.currentTarget e.clientX e.clientY

    if isNull (box v) then None else Some(v.[0], v.[1])

  let pts = Array.ofList points
  let circle = circleThrough pts.[0] pts.[1] pts.[2]
  let side = circle |> Option.bind (sideOf pts.[3])
  let power = circle |> Option.bind (powerOf pts.[3])
  let node = figure points selected

  let stage =
    Html.div
      [ prop.className "geo-stage"
        prop.tabIndex 0
        prop.ariaLabel (
          "Drawing of seven points. Point "
          + names.[selected]
          + " is selected; press 1 to 7 to choose a point and the arrow keys to move it."
        )
        prop.onPointerDown (fun e ->
          match at e with
          | Some p ->
            match nearest points p with
            | Some i ->
              setSelected i
              setDragging true
              capture e.currentTarget e.pointerId
              e.preventDefault ()
            | None -> ()
          | None -> ())
        prop.onPointerMove (fun e ->
          if dragging then
            match at e with
            | Some p -> move selected p
            | None -> ())
        prop.onPointerUp (fun _ -> setDragging false)
        prop.onPointerCancel (fun _ -> setDragging false)
        prop.onKeyDown (fun e ->
          let (x, y) = pts.[selected]

          match e.key with
          | "ArrowLeft" ->
            move selected (x - 1.0, y)
            e.preventDefault ()
          | "ArrowRight" ->
            move selected (x + 1.0, y)
            e.preventDefault ()
          | "ArrowUp" ->
            move selected (x, y - 1.0)
            e.preventDefault ()
          | "ArrowDown" ->
            move selected (x, y + 1.0)
            e.preventDefault ()
          | k when k.Length = 1 && k >= "1" && k <= "7" -> setSelected (int k - 1)
          | _ -> ())
        prop.children [ Exhibit.renderStatic node ] ]

  let verdict =
    Html.p
      [ prop.className "geo-verdict"
        prop.role "status"
        prop.children
          [ Html.span
              [ prop.className "geo-dot"
                prop.style [ style.backgroundColor (sideColour side) ] ]
            Html.strong [ prop.text ("D is " + sideWord side + " the circle through A, B and C.") ]
            Html.span (
              match power with
              | Some v ->
                " Its power against the circle is "
                + v.ToString("0.###")
                + (if v < 0.0 then " (negative: inside)."
                   elif v > 0.0 then " (positive: outside)."
                   else " (zero: on the circle).")
              | None -> " The three points are in a line, so they define no circle."
            ) ] ]

  let figurePanel =
    Exhibit.panel
      "Drag the points"
      "A, B and C make the circle. D is tested against it: drag D across the circle and the sign flips. The coloured cells are the Voronoi cells of all seven points, and the thin lines the Delaunay triangles they are read from."
      [ stage; verdict ]

  let howPanel =
    Exhibit.panel
      "One algebra, one operation, applied everywhere"
      ""
      [ Html.ul
          [ prop.className "geo-steps"
            prop.children
              [ Html.li
                  "In the conformal model of geometric algebra, a point x of the plane becomes the vector n0 + x + ½|x|² n∞ of an algebra built on five basis vectors. Circles, lines, spheres and planes are objects of the same algebra, so one set of products serves all of them."
                Html.li
                  "The circle through A, B and C is the outer product of the three conformal points, A ∧ B ∧ C: a trivector that is the circle itself. No centre or radius is solved for; the page reads them back only to draw it (Circle.through)."
                Html.li
                  "Whether D is inside is the sign of D's power against that circle: Circle.side. Negative is inside, whatever order the circle's points came in."
                Html.li
                  "The Delaunay triangles are every triple whose circle has no other point inside it: the same test, run over every triple. Each Voronoi cell is the ring of its triangles' centres."
                Html.li
                  "All of it runs in this page, from the published package compiled by Fable. No request leaves the browser." ] ] ]

  let currentBits =
    match circle with
    | Some c ->
      Html.table
        [ prop.className "geo-bits"
          prop.children
            [ Html.thead [ Html.tr [ Html.th "Blade"; Html.th "Coefficient"; Html.th "Bits" ] ]
              Html.tbody
                [ for (name, v) in coefficients c ->
                    Html.tr
                      [ Html.td [ Html.code name ]
                        Html.td (v.ToString("G6"))
                        Html.td [ Html.code (bitsOf v) ] ] ] ] ]
    | None -> Html.none

  let example = computedExample ()
  let matches = Array.map2 (fun (a: string) b -> a = b) example publishedExpected

  let exampleBits =
    Html.table
      [ prop.className "geo-bits"
        prop.children
          [ Html.thead [ Html.tr [ Html.th "Blade"; Html.th "Computed here"; Html.th "Published"; Html.th "" ] ]
            Html.tbody
              [ for i in 0 .. example.Length - 1 ->
                  Html.tr
                    [ Html.td [ Html.code bladeNames.[i] ]
                      Html.td [ Html.code example.[i] ]
                      Html.td [ Html.code publishedExpected.[i] ]
                      Html.td (if matches.[i] then "same" else "DIFFERENT") ] ] ] ]

  let matchCount = matches |> Array.filter id |> Array.length

  let bytesPanel =
    Exhibit.panel
      "The same bits"
      "The package pins its arithmetic: a fixed order of every sum, no fused multiply-add and no call into the host's maths library, so it answers the same bits in this browser as on .NET."
      [ Html.h4 "The circle through A, B and C, now"
        currentBits
        Html.h4 ("The published example: " + publishedId)
        Html.p
          [ prop.className "geo-match"
            prop.text (
              string matchCount
              + " of "
              + string example.Length
              + " coefficients equal the published bits, computed in this browser from the published inputs."
            ) ]
        exampleBits ]

  Exhibit.shell
    "geometry"
    "Drag the Points"
    "Geometric algebra, running in your browser. In conformal geometric algebra a point and a circle are each one object of the same algebra, and building or testing them is a product, not a formula solved case by case. Drag the points: the circle through three of them is one outer product, whether a fourth is inside it is the sign of one more product, and the Voronoi cells of all seven are that same test applied to every triple. All of it is recomputed on every move by Fuaran.Compute.GeometricAlgebra, a pinned geometric algebra package compiled into this page."
    [ figurePanel
      howPanel
      bytesPanel
      Exhibit.wireDrawer "The figure, as the Drawing node the renderer drew" (CJson.encodeNode node) ]
    [ Exhibit.Claim.Verified
        "The circle, the side test, the triangles and the cells all come from Fuaran.Compute.GeometricAlgebra, restored from the public registry and compiled by Fable into this page. Nothing is fetched."
      Exhibit.Claim.Verified
        "The published example is recomputed here from its published inputs, and each of its ten coefficients is compared with the published bits on the page."
      Exhibit.Claim.Verified
        "The figure is a Drawing node rendered by the shipped renderer; the wire drawer holds its canonical bytes."
      Exhibit.Claim.Limit
        "The .NET side of 'the same bits' is the published law vector, which the package's own tests replay on .NET. This page does not run .NET."
      Exhibit.Claim.Limit
        "The drawing's coordinates (the centre and radius read back for the picture, the order of a cell's corners and the clipping to the frame) are this page's ordinary arithmetic, not the pinned package's. They place pixels; no claim rests on them."
      Exhibit.Claim.Limit
        "The triangles are found by testing every triple, which suits seven points and is not how a triangulation of thousands would be built."
      Exhibit.Claim.Limit
        "Dragging is this page's own pointer handling around the drawing: the language has no drag gesture." ]

let page: ReactElement = GeometryView()
