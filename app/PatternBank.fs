namespace Fuaran.Live

// ============================================================================
//  The Pattern Bank – the fast path. Composition by lookup, not by generation.
//
//  Before reaching for a model, look up a known-good pattern by its structure:
//  describe the kind of node you want + the holes you can fill, and the bank
//  returns which patterns you can run – instantly, with no key and no model call.
//  Pick one and it loads into the playground as a real Fuaran tree you can then
//  edit by prompt.
//
//  The search is the REAL `Fuaran.Core.FunctionRegistry.findBySignature` (the
//  same engine that powers the public `Fuaran.UI.FastPath` package), compiled to
//  JavaScript via Fable: deterministic, total, in-memory. No server. The bank
//  itself (engine + seed catalogues) is the shared shared/PatternBank.fs; this
//  module is the playground's panel over it.
// ============================================================================

module PatternBank =

  open Feliz
  open Fuaran.UI
  open Fuaran.UI.Types
  open PatternBankEngine

  let private theBank: Bank<obj> = bank (PatternBankSeeds.playground ())


  // ── the "provide" context groups → the holes they supply ─────────────────
  let private textAddrs =
    [ "metric.label"
      "m0.label"
      "m1.label"
      "m2.label"
      "card.label"
      "card.value"
      "title"
      "headline"
      "sub"
      "heading"
      "body"
      "message"
      "f0"
      "f1"
      "f2" ]


  let private kinds =
    [ None, "Any"
      Some "Box", "Layout"
      Some "Metric", "Metric"
      Some "Card", "Card"
      Some "Callout", "Callout"
      Some "Markdown", "Text" ]

  let private groups = [ "text", "text"; "numbers", "numbers"; "data", "data" ]

  [<ReactComponent>]
  let Panel (onLoad: Node<obj> -> unit) : ReactElement =
    let produce, setProduce = React.useState (None: string option)
    let context, setContext = React.useState (Set.ofList [ "text"; "numbers" ])

    let matches = findRunnable (query (groupHoles textAddrs context) produce) theBank

    let toggle (g: string) : unit =
      setContext (
        if context.Contains g then
          Set.remove g context
        else
          Set.add g context
      )

    Html.div
      [ prop.className "fl-pb"
        prop.children
          [ Html.p
              [ prop.className "fl-pb-intro"
                prop.text "Or search the pattern bank – a known-good app, no key, no model:" ]
            Html.div
              [ prop.className "fl-pb-filters"
                prop.children
                  [ Html.div
                      [ prop.className "fl-pb-row"
                        prop.children
                          [ for (k, label) in kinds ->
                              Html.button
                                [ prop.className (
                                    if produce = k then
                                      "fl-pb-pill fl-pb-pill-on"
                                    else
                                      "fl-pb-pill"
                                  )
                                  prop.text label
                                  prop.onClick (fun _ -> setProduce k) ] ] ]
                    Html.div
                      [ prop.className "fl-pb-row"
                        prop.children
                          [ for (g, label) in groups ->
                              Html.button
                                [ prop.className (
                                    if context.Contains g then
                                      "fl-pb-chip fl-pb-chip-on"
                                    else
                                      "fl-pb-chip"
                                  )
                                  prop.text ((if context.Contains g then "✓ " else "") + label)
                                  prop.onClick (fun _ -> toggle g) ] ] ] ] ]
            Html.div
              [ prop.className "fl-pb-count"
                prop.text (sprintf "%d match · deterministic · 0 ms" (List.length matches)) ]
            Html.div
              [ prop.className "fl-pb-list"
                prop.children
                  [ for p in matches ->
                      Html.button
                        [ prop.className "fl-pb-item"
                          prop.title p.Summary
                          prop.onClick (fun _ -> onLoad (instantiate p Map.empty))
                          prop.children
                            [ Html.span [ prop.className "fl-pb-item-title"; prop.text p.Title ]
                              Html.span [ prop.className "fl-pb-item-summary"; prop.text p.Summary ] ] ] ] ] ] ]

  /// The pattern-bank panel – pick a pattern and it loads into the playground.
  let panel (onLoad: Node<obj> -> unit) : ReactElement = Panel onLoad
