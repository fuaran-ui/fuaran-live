# Comparison mode — what it measures (and what it doesn't)

`fuaran-live`'s **Compare** pane runs the same natural-language prompt two ways
and shows you the difference, live, on your own prompt:

- **Fuaran** — the playground's normal path. The model is given the wire-format
  teaching system prompt and emits canonical Fuaran wire JSON, which the app
  decodes, applies, and renders.
- **Conventional** — a free-form codegen baseline. The model is given a
  React + TypeScript (shadcn/ui) system prompt and emits a TSX component.

It is the **interactive, anecdotal companion** to the paper's central claim —
that constrained, typed Fuaran emission is more token-efficient _and_ more
reliable than free-form codegen. The paper proves this rigorously _in aggregate_
across a controlled corpus, four providers, and three tiers (single-turn
creation, multi-turn refinement, repair-under-error). This pane lets a visitor
watch the claim hold — or probe where it doesn't — on a single prompt of their
choosing, rather than taking the aggregate numbers on faith. One prompt is an
anecdote, not evidence; the aggregate study is the evidence.

## What it compares

For one prompt, the same provider is called twice, swapping **only the system
prompt**. Each call is a single-turn creation (no closed-loop tree injection), so
the two arms are strictly apples-to-apples — the same shape the controlled eval's
single-turn-creation tier measures.

### Token accounting (the honest part)

A token comparison is easy to rig. Fuaran needs a long wire-format-teaching
system prompt; the conventional baseline needs almost none. If you only counted
_output_ tokens, you'd flatter Fuaran by hiding its input cost. If you counted
the teaching prompt _every turn_, you'd flatter the baseline by ignoring that the
teaching prompt is fixed and amortises across a multi-turn edit session (and is
free after the first turn under prompt caching). So the pane shows all of it:

| Figure                           | Source                            | Meaning                                                      |
| -------------------------------- | --------------------------------- | ------------------------------------------------------------ |
| **Output tokens**                | real `usage.output_tokens`        | the clean headline — what the model produced for this prompt |
| **Input tokens (incl. system)**  | real `usage.input_tokens`         | the per-turn input, which includes the full system prompt    |
| **Teaching cost (one-time)**     | _estimated_ (chars/4)             | the fixed system-prompt cost, billed once per session        |
| **Cumulative output**            | Σ real output                     | session total output                                         |
| **Cumulative input — naive**     | Σ real input                      | system prompt re-billed every turn (no caching)              |
| **Cumulative input — amortised** | teaching once + Σ per-turn deltas | the prompt-caching / fixed-session view                      |

The truth for any given deployment sits **between** the naive and amortised
input figures, depending on whether prompt caching is on. Showing both refuses
to cherry-pick the flattering one.

**Caveat — the teaching cost is estimated.** There is no tokenizer in the
browser, so the one-time teaching cost is a transparent `chars / 4` heuristic and
is labelled "est." in the UI. Output and per-turn input counts are the
provider's **real** `usage` numbers, never estimated.

### Validity (the other half of the thesis)

Token efficiency is only half the claim; reliability is the other half, and
arguably the stronger signal. The two arms are checked asymmetrically because the
artefacts are asymmetric:

- **Fuaran** — checked by the app's **real** decode/apply leg (the same
  `ingest` the live playground uses). "Renders" means exactly what it means in
  the playground: the wire JSON decoded to a `Node`. An invalid emission shows
  the structured decoder reason.
- **Conventional** — there is no React/TSX compiler in the browser, and the app
  **does not execute** untrusted model output. So this is a **best-effort
  structural heuristic**: does the emission look like a single, bracket-balanced
  module that default-exports a component returning JSX? It is intentionally
  generous ("could a bundler plausibly accept this?", not "is it bug-free?") and
  can be fooled. It is a directional signal, not a compiler verdict.

This asymmetry is deliberate and disclosed: the Fuaran check is exact because the
language was designed to be decoder-validated; the conventional check is a
heuristic because validating free-form codegen _is itself the hard problem the
typed approach sidesteps_.

## Security posture

The conventional emission is **displayed** (collapsed code) but **never
executed**. Running untrusted React/JS would require a sandboxed `iframe` under a
strict CSP, with the BYOK key never crossing into the sandbox. The comparison
does not need execution — token counts and the structural validity check stand
on their own — so the playground keeps its no-execution, single-egress posture:
the only origin it ever contacts is the BYOK provider, and the key rides only the
provider call.

## Shared baseline

The conventional-baseline system prompt is a **byte-aligned copy** of the
`JsxShadcn` baseline condition the controlled comparative evaluation uses (the
paper's §5.2 study, roadmap Phase 27.B). Keeping the live demo and the controlled
eval on the _same_ baseline is load-bearing: a visitor sees the identical
baseline the aggregate numbers were measured against, not a strawman authored to
flatter Fuaran. If that baseline prompt changes, the copy in
[`src/compare/conventionalPrompt.ts`](../src/compare/conventionalPrompt.ts) must
be re-synced — the comparison is only honest while the two are identical.

## See also

- The Fuaran paper (workspace `publications/`) — §5.2 for the aggregate
  Fuaran-vs-baseline numbers this pane is the anecdotal companion to.
- [`src/compare/`](../src/compare/) — the dual-emission flow, token accounting,
  and validity checks.
