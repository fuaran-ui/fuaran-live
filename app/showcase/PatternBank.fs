namespace Fuaran.Showcase

// ============================================================================
//  The Pattern Bank – composition by lookup, not by generation. Pillar: "the
//  machine can see the UI".
//
//  Describe the shape you want – the holes you can fill, the kind of node you
//  want to produce – and the bank returns which known patterns you can run,
//  instantly. The search is the REAL `Fuaran.Core.FunctionRegistry.findBySignature`
//  (the same engine that powers the shipped `Fuaran.UI.FastPath` package),
//  compiled to JavaScript via Fable: deterministic, total, in-memory. No model
//  call, no server, zero latency. Pick a match and it instantiates into a real
//  Fuaran tree you can render, export, or keep building on.
//
//  This is the fast path: before reaching for a model, look up a known-good
//  pattern by its structure. It is the anti-generative half of AI composition –
//  a pattern can't be hallucinated, and it resolves in microseconds.
//
//  The bank itself – the engine and the seed catalogues – is the one shared
//  definition in ../shared/PatternBank.fs, which the playground compiles too;
//  this module is the showcase page over it. Nothing needs a server.
// ============================================================================

module PatternBank =

  open Feliz
  open Fuaran.UI
  open Fuaran.UI.Types
  open Fuaran.UI.Renderer
  open Fuaran.UI.OpStream.Abstractions
  open Fuaran.Live.PatternBankEngine

  let private theBank: Bank<unit> = bank (Fuaran.Live.PatternBankSeeds.showcase ())


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
      "cta"
      "heading"
      "body"
      "message"
      "f0"
      "f1"
      "f2" ]


  let private renderTree (n: Node<unit>) : ReactElement =
    Render.renderWithSources BindingResolver.empty ignore n

  let private kinds =
    [ None, "Any"
      Some "Box", "Dashboard"
      Some "Metric", "Metric"
      Some "Card", "Card"
      Some "Callout", "Callout"
      Some "Markdown", "Text" ]

  let private groups =
    [ "text", "labels & copy"; "numbers", "numbers"; "data", "a data table" ]

  [<ReactComponent>]
  let private PatternBankView () : ReactElement =
    let produce, setProduce = React.useState (None: string option)
    let context, setContext = React.useState (Set.ofList [ "text"; "numbers" ])
    let selected, setSelected = React.useState (None: string option)
    let showWire, setShowWire = React.useState false

    let matches = findRunnable (query (groupHoles textAddrs context) produce) theBank

    let toggleContext (g: string) : unit =
      setContext (
        if context.Contains g then
          Set.remove g context
        else
          Set.add g context
      )

    // ── the query panel ──────────────────────────────────────────────────
    let producePills =
      Html.div
        [ prop.className "pb-pills"
          prop.children
            [ for (k, label) in kinds ->
                Html.button
                  [ prop.className (if produce = k then "pb-pill pb-pill-on" else "pb-pill")
                    prop.text label
                    prop.onClick (fun _ -> setProduce k) ] ] ]

    let contextChips =
      Html.div
        [ prop.className "pb-chips"
          prop.children
            [ for (g, label) in groups ->
                Html.button
                  [ prop.className (
                      if context.Contains g then
                        "pb-chip pb-chip-on"
                      else
                        "pb-chip"
                    )
                    prop.text ((if context.Contains g then "✓ " else "") + label)
                    prop.onClick (fun _ -> toggleContext g) ] ] ]

    let queryPanel =
      Html.div
        [ prop.className "pb-query"
          prop.children
            [ Html.div
                [ prop.className "pb-q-row"
                  prop.children
                    [ Html.span [ prop.className "pb-q-label"; prop.text "I want to produce" ]
                      producePills ] ]
              Html.div
                [ prop.className "pb-q-row"
                  prop.children
                    [ Html.span [ prop.className "pb-q-label"; prop.text "and I can provide" ]
                      contextChips ] ] ] ]

    // ── the results ──────────────────────────────────────────────────────
    let resultCard (p: Pattern<unit>) : ReactElement =
      Html.button
        [ prop.className (
            if selected = Some p.Id then
              "pb-result pb-result-on"
            else
              "pb-result"
          )
          prop.onClick (fun _ -> setSelected (Some p.Id))
          prop.children
            [ Html.div [ prop.className "pb-result-title"; prop.text p.Title ]
              Html.div [ prop.className "pb-result-summary"; prop.text p.Summary ]
              Html.div
                [ prop.className "pb-result-needs"
                  prop.text ("needs " + (holeNames p |> List.distinct |> String.concat ", ")) ] ] ]

    let resultsPanel =
      Html.div
        [ prop.className "pb-results"
          prop.children
            [ Html.div
                [ prop.className "pb-results-head"
                  prop.children
                    [ Html.span
                        [ prop.className "pb-count"
                          prop.text (sprintf "%d patterns match" (List.length matches)) ]
                      Html.span [ prop.className "pb-latency"; prop.text "no model · no server · 0 ms" ] ] ]
              (if List.isEmpty matches then
                 Html.p
                   [ prop.className "pb-empty"
                     prop.text "Nothing matches – turn on more context above." ]
               else
                 Html.div
                   [ prop.className "pb-results-grid"
                     prop.children [ for p in matches -> resultCard p ] ]) ] ]

    // ── the selected pattern preview ─────────────────────────────────────
    let previewPanel =
      match selected |> Option.bind (fun id -> tryPattern id theBank) with
      | None ->
        Html.p
          [ prop.className "pb-preview-hint"
            prop.text "Pick a match to instantiate it into a real Fuaran app." ]
      | Some p ->
        let tree = instantiate p Map.empty
        let wire = CanonicalJson.encodeNode tree

        Html.div
          [ prop.className "pb-preview"
            prop.children
              [ Html.div
                  [ prop.className "pb-preview-head"
                    prop.children
                      [ Html.span [ prop.className "pb-preview-title"; prop.text ("Instantiated: " + p.Title) ]
                        Html.span
                          [ prop.className "pb-preview-note"
                            prop.text "a real Fuaran tree – render it, export it, keep building" ] ] ]
                Html.div [ prop.className "pb-preview-render"; prop.children [ renderTree tree ] ]
                Html.button
                  [ prop.className "pb-wire-toggle"
                    prop.text (if showWire then "Hide the wire" else "Show the wire")
                    prop.onClick (fun _ -> setShowWire (not showWire)) ]
                (if showWire then
                   Html.pre [ prop.className "wire-json"; prop.children [ Html.code [ prop.text wire ] ] ]
                 else
                   Html.none) ] ]

    let liveNote =
      Html.div
        [ prop.className "pb-live-note"
          prop.children
            [ Html.strong [ prop.text "The fast path. " ]
              Html.text "In the "
              Html.a
                [ prop.href "https://fuaran-ui.live"
                  prop.target "_blank"
                  prop.rel "noreferrer"
                  prop.text "fuaran-live playground" ]
              Html.text
                ", this is what happens before you reach for a model: look up a known-good pattern by its structure, get a real app instantly, then edit or prompt from there." ] ]

    let honesty =
      Exhibit.honesty
        "How honest is this?"
        [ Html.li
            [ prop.text
                "The search is the real Fuaran.Core signature-search engine (findBySignature), compiled to JavaScript via Fable – the same engine that powers the shipped Fuaran.UI.FastPath package. It is deterministic and total: a pattern is matched by its structure (the node kind it produces + the holes it requires), never guessed, and it resolves in-memory with no model call and no network." ]
          Html.li
            [ prop.text
                "Each match instantiates into a genuine Fuaran tree, rendered here through the real renderer; the wire JSON is the canonical encoding. Two patterns are compute-bound – their value is a real transform pipeline evaluated client-side, so even a data-driven figure needs no server." ]
          Html.li
            [ prop.text
                "This is composition by lookup, the anti-generative half of AI-built UI: the pattern bank is what the machine consults first, and only when it misses does generation take over. The bank here is the site's own, one definition the playground's pattern bank shares." ]
          Html.li
            [ prop.children
                [ Html.text "The interface is structured data a machine can search – the "
                  Html.a [ prop.href "#/pillar/machine"; prop.text "machine-can-see-the-UI" ]
                  Html.text " thesis, applied to composition itself." ] ] ]

    Exhibit.frame
      "pb"
      "The Pattern Bank"
      (Exhibit.lede
        "Describe the shape you want – the bank finds a runnable pattern instantly. No model call, no server, zero latency. Composition by lookup, not by generation.")
      [ queryPanel; resultsPanel; previewPanel; liveNote ]
      honesty

  let page: ReactElement = PatternBankView()
