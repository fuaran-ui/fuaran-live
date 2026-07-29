module Fuaran.Live.Gallery

// ============================================================================
//  A small curated showcase (F# port of the src/gallery idea). Each example is a
//  ready-made `Fuaran.UI` tree the visitor can load WITHOUT a key – proof the
//  renderer + the wire format stand on their own, and a starting point to then
//  edit by prompt. Built with the F# smart constructors and rendered by the same
//  F# renderer that draws an LLM's emission.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

type Example = { Title: string; Tree: Node<obj> }

let private metric (id: string) (label: string) (value: float) (fmt: CellFormat) (tone: ToneVariant) : Node<obj> =
  Fuaran.metric
    id
    { Defaults.metric with
        Label = TextSource.Literal label
        Value = Binding.Static(Some value)
        Format = fmt
        Tone = tone }

let private salesDashboard: Node<obj> =
  Fuaran.dashboard
    "ex-sales"
    { Defaults.dashboard with
        Children =
          [ Fuaran.heading
              "ex-sales-title"
              { Level = 1
                Text = TextSource.Literal "Q4 Sales"
                Variant = HeadingVariant.Standard }
            Fuaran.gridLayout
              "ex-sales-kpis"
              { Defaults.gridLayout<obj> with
                  Cols = 3
                  Children =
                    [ metric "ex-sales-rev" "Revenue" 142500.0 (CellFormat.Currency "GBP") ToneVariant.Brand
                      metric "ex-sales-orders" "Orders" 1284.0 (CellFormat.Number(Some 0)) ToneVariant.Default
                      metric "ex-sales-conv" "Conversion" 0.043 (CellFormat.Percent(Some 1)) ToneVariant.Success ] }
            Fuaran.markdown
              "ex-sales-note"
              "Updated hourly. **No server** – this is canonical Fuaran wire JSON rendered live. Type a prompt to edit it." ] }

let private welcomeCard: Node<obj> =
  Fuaran.card
    "ex-welcome"
    { Defaults.card<obj> with
        Heading = Some(TextSource.Literal "Welcome to fuaran-live")
        Children =
          [ Fuaran.markdown
              "ex-welcome-body"
              "Prompt an LLM with your own key and it emits a typed UI as **canonical wire-format JSON** – decoded, applied, and rendered live, with no server and no code execution.\n\nThe whole playground is itself a Fuaran app, built in F#." ] }

// ─── the developer headline – a generated docs / tutorial page (Phase 294) ───
//
// Code + an equation + a GFM table, each a first-class Fuaran primitive rather
// than plain Markdown: `CodeBlock` (Phase 290) carries a language tag + copy
// button, `Math` (Phase 293) KaTeX-renders live, and the Markdown table routes
// to the real `fuaran-table` render. The dev-audience flagship – "prompt → a
// polished docs page → grab the code" – alongside the data + designer demos.

let private tsSnippet =
  """import { fuaran, binding } from '@fuaran-ui/ui';

// A UI is data: author it, or let a model emit the same wire JSON.
const revenue = fuaran.metric({
  id: 'revenue',
  label: 'Revenue',
  value: binding.static(142500),
  format: { kind: 'currency', code: 'GBP' },
});"""

// Triple-quoted so the LaTeX backslashes stay literal (no escape processing).
let private conversionLatex =
  """\text{conversion} = \frac{\text{orders}}{\text{sessions}} \times 100\%"""

let private docsPage: Node<obj> =
  Fuaran.dashboard
    "ex-docs"
    { Defaults.dashboard with
        Children =
          [ Fuaran.heading
              "ex-docs-title"
              { Level = 1
                Text = TextSource.Literal "Getting started with Fuaran"
                Variant = HeadingVariant.Standard }
            Fuaran.markdown
              "ex-docs-intro"
              "A Fuaran UI is **data** – a typed tree emitted as canonical wire-format JSON and rendered live, with no code execution. Author one by hand in TypeScript, F#, or Python, or let an LLM emit it."
            Fuaran.heading
              "ex-docs-code-h"
              { Level = 2
                Text = TextSource.Literal "Author a metric in TypeScript"
                Variant = HeadingVariant.Standard }
            Fuaran.codeBlockSpec
              "ex-docs-code"
              { Defaults.codeBlock with
                  Language = "typescript"
                  LineNumbers = true
                  Code = tsSnippet }
            Fuaran.heading
              "ex-docs-math-h"
              { Level = 2
                Text = TextSource.Literal "How conversion is computed"
                Variant = HeadingVariant.Standard }
            Fuaran.math "ex-docs-math" conversionLatex
            Fuaran.heading
              "ex-docs-table-h"
              { Level = 2
                Text = TextSource.Literal "Display kinds at a glance"
                Variant = HeadingVariant.Standard }
            Fuaran.markdown
              "ex-docs-table"
              "| Kind | Use |\n|---|---|\n| `Metric` | A single KPI value |\n| `CodeBlock` | A syntax-tagged code sample with copy |\n| `Math` | A LaTeX equation, KaTeX-rendered |\n| `Chart` | A data visualisation |" ] }

let private statStack: Node<obj> =
  Fuaran.stack
    "ex-stats"
    { Defaults.stack with
        Orientation = Orientation.Vertical
        Children =
          [ Fuaran.heading
              "ex-stats-title"
              { Level = 2
                Text = TextSource.Literal "Service health"
                Variant = HeadingVariant.Standard }
            metric "ex-stats-uptime" "Uptime" 0.9993 (CellFormat.Percent(Some 2)) ToneVariant.Success
            metric "ex-stats-p95" "p95 latency (ms)" 184.0 (CellFormat.Number(Some 0)) ToneVariant.Default
            metric "ex-stats-errors" "Error rate" 0.002 (CellFormat.Percent(Some 1)) ToneVariant.Warning ] }

/// The showcase, in display order. The generated docs/tutorial page leads as the
/// developer headline (code + equation + table, each a first-class primitive).
let examples: Example list =
  [ { Title = "Docs / tutorial page"
      Tree = docsPage }
    { Title = "Sales dashboard"
      Tree = salesDashboard }
    { Title = "Welcome card"
      Tree = welcomeCard }
    { Title = "Service health"
      Tree = statStack } ]

/// The canonical wire JSON of each example, as a JS array – the cross-boundary
/// surface the gallery test uses to assert every example is valid + shareable.
let exampleWires (unit: unit) : string[] =
  examples |> List.map (fun e -> Canon.encodeNode e.Tree) |> List.toArray
