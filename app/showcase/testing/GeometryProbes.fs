module Fuaran.Showcase.Testing.GeometryProbes

// ============================================================================
//  TEST-ONLY. The headless surface of the geometry page (Phase 2851).
//
//  Imported by test/geometryShowcase.test.ts from the Fable output and by
//  nothing in the app. Every probe calls the page's own functions, so the suite
//  checks what the page draws and says rather than a restatement of it.
// ============================================================================

module G = Fuaran.Showcase.Geometry

let private pt (xs: float array) (i: int) : G.Pt = xs.[2 * i], xs.[2 * i + 1]

/// The published example, recomputed: the circle's ten coefficients' bits.
let geoExampleComputed () : string array = G.computedExample ()

/// The published bits as the page states them.
let geoExamplePublished () : string array = G.publishedExpected

/// Where the fourth point is against the circle through the first three, from
/// eight coordinates `ax ay bx by cx cy dx dy`: "Inside", "On", "Outside", or
/// "none" when the three points define no circle.
let geoSide (xs: float array) : string =
  match G.circleThrough (pt xs 0) (pt xs 1) (pt xs 2) with
  | Some c ->
    match G.sideOf (pt xs 3) c with
    | Some Fuaran.Compute.GeometricAlgebra.Side.Inside -> "Inside"
    | Some Fuaran.Compute.GeometricAlgebra.Side.On -> "On"
    | Some Fuaran.Compute.GeometricAlgebra.Side.Outside -> "Outside"
    | None -> "none"
  | None -> "none"

/// The page's starting points, flattened `x0 y0 x1 y1 …`.
let geoInitialPoints () : float array =
  G.initialPoints |> List.collect (fun (x, y) -> [ x; y ]) |> Array.ofList

/// The Delaunay triangles among the REAL points (no sentinel), each as its three
/// indices `a,b,c` sorted, for the points given flattened.
let geoTriangles (xs: float array) : string array =
  let points = [ for i in 0 .. xs.Length / 2 - 1 -> pt xs i ]
  let n = List.length points

  G.delaunay points
  |> List.map fst
  |> List.filter (fun (a, b, c) -> a < n && b < n && c < n)
  |> List.map (fun (a, b, c) -> sprintf "%d,%d,%d" a b c)
  |> Array.ofList

/// The corner count of each point's Voronoi cell, for the points given flattened.
let geoCellSizes (xs: float array) : int array =
  let points = [ for i in 0 .. xs.Length / 2 - 1 -> pt xs i ]
  G.cells points (G.delaunay points) |> List.map List.length |> Array.ofList

/// The bit pattern of a double, as the page writes it.
let geoBits (x: float) : string = G.bitsOf x

/// The double a bit pattern names, as the page reads it.
let geoOfBits (hex: string) : float = G.ofBits hex

/// The figure's canonical wire, for the page's starting points.
let geoFigureWire () : string =
  Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode (G.figure G.initialPoints 3)
