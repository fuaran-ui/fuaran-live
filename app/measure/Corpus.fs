module Fuaran.Live.Measure.Corpus

// ============================================================================
//  The render-latency measurement corpus (Phase 1628).
//
//  A fixed corpus of canonical wire-format trees at four shapes — small,
//  medium, large, and deeply-nested — so the render-latency curve can be read
//  as a function of BOTH node count and tree depth.
//
//  The corpus DESIGN is carried forward from the harness Phase 202 built and
//  the 2026-06-25 TypeScript-shell retirement removed; the code is not. Two
//  shapes of that design are deliberately preserved:
//
//   * the width axis (4 / 24 / 96 children), which is what makes a regression
//     in per-node cost visible as a slope rather than as a single number; and
//   * the DeepNested spine, which exercises the nesting axis the width axis
//     cannot reach.
//
//  Trees are emitted as canonical wire JSON rather than as typed values, for
//  the same reason the retired corpus did it: the harness then drives the same
//  decode path a real emission takes, and the corpus stays readable as data.
//  Decoding happens OUTSIDE the clock — this measures rendering, not decoding.
// ============================================================================

/// One labelled corpus entry: the wire document, and the shape facts a report
/// needs in order to say what was measured.
type CorpusTree =
  {
    Label: string
    /// Total nodes in the tree, counted at construction rather than walked.
    NodeCount: int
    /// Node nesting levels, root inclusive.
    Depth: int
    /// The canonical wire-format document.
    Wire: string
  }

/// JSON string escaping for the literal text this module embeds. The corpus
/// authors its own strings (no user input reaches here), so the five characters
/// JSON requires are the whole of it.
let private esc (s: string) : string =
  s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\n").Replace("\r", "\r").Replace("\t", "\t")

let private markdown (id: string) (text: string) : string =
  sprintf """{"id":"%s","kind":{"$type":"Markdown","text":"%s"}}""" (esc id) (esc text)

let private heading (id: string) (text: string) : string =
  sprintf """{"id":"%s","kind":{"$type":"Heading","level":3,"text":"%s","variant":"Standard"}}""" (esc id) (esc text)

let private box (id: string) (children: string list) : string =
  sprintf
    """{"id":"%s","kind":{"$type":"Box","children":[%s],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
    (esc id)
    (String.concat "," children)

/// A flat surface of `n` alternating heading/markdown children — the WIDTH
/// axis. Two leaf kinds rather than one so the measurement is not dominated by
/// a single leaf renderer's cost.
let private wide (id: string) (n: int) : CorpusTree =
  let children =
    [ for i in 0 .. n - 1 ->
        if i % 2 = 0 then
          heading (sprintf "%s-h%d" id i) (sprintf "Section %d" i)
        else
          markdown (sprintf "%s-m%d" id i) (sprintf "Line %d of the measured surface." i) ]

  { Label = id
    NodeCount = n + 1
    Depth = 2
    Wire = box id children }

/// A right-leaning spine of `depth` nested boxes, each carrying one heading
/// label beside the next level — the DEPTH axis.
///
/// THE DEPTH IS A DELIBERATE NUMBER, not a round one. The canonical wire
/// decoder enforces `WireLimits.MaxDepth = 24` and refuses a deeper document
/// with `LIMIT_EXCEEDED`, so a corpus that nests past it measures nothing —
/// it fails to decode. That bound belongs to the format, not to this harness.
/// Phase 1147 hit the same wall from the other side (its streaming-latency
/// table stops its depth row at 20 for exactly this reason) and the retired
/// Phase 202 corpus, written before the limit existed, nested 32 deep — which
/// today would not decode at all. 20 nested boxes plus the leaf is 21 levels:
/// close enough to the limit to exercise the nesting axis, inside it by a
/// margin that a future leaf-wrapping change cannot silently cross.
let private spine (id: string) (depth: int) : CorpusTree =
  let mutable node = markdown (sprintf "%s-leaf" id) "Leaf of the measured spine."

  for d in depth - 1 .. -1 .. 0 do
    node <- box (sprintf "%s-d%d" id d) [ heading (sprintf "%s-d%d-label" id d) (sprintf "Depth %d" d); node ]

  { Label = id
    NodeCount = (2 * depth) + 1
    Depth = depth + 1
    Wire = node }

/// The nesting depth of the DeepNested spine, in nested boxes. See `spine`.
/// A plain binding rather than a `[<Literal>]` so it is EXPORTED across the
/// Fable boundary and the unit suite can pin the corpus against the decoder's
/// `MaxDepth` bound instead of restating the number.
let SpineDepth = 20

/// The fixed corpus. Labels are the second segment of every emitted metric id
/// (`render.ttfp.<Label>.ms`), so renaming one renames a budgeted metric —
/// treat them as part of the gate's contract, not as display text.
let corpus: CorpusTree list =
  [ wide "Small" 4
    wide "Medium" 24
    wide "Large" 96
    spine "DeepNested" SpineDepth ]
