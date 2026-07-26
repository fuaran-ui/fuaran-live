module Fuaran.Live.SystemPrompt

open Fable.Core

// ─── the drift-checked prompt pack, inlined at build time ────────────────────
//
// The wire-format TEACHING is the language repo's AI-authoring prompt pack
// (`fuaran-dotnet/docs/prompt-pack/system-prompt.md`) – the exact system prompt the
// published evaluation measured, whose every wire example is generated from the
// wire-format-fixtures corpus and drift-checked in the language repo's build
// (`authoring-pack.fsx --check`). It is inlined here byte-for-byte via a Vite
// `?raw` import from the sibling checkout – the same workspace-checkout posture
// as the `@fuaran-ui/*` link bridge and the parity fixtures – so the playground
// prompt can never drift from the measured, corpus-certified teaching: a pack
// regeneration lands here on the next build with no hand-copy step.
//
// The pack's few-shot.jsonl is deliberately NOT sent (per the pack README's
// fuaran-live contract: system-prompt.md + the user's prompt); the pack's own
// corpus-derived examples carry the few-shot weight.
//
// This module adds only what the CHAT SURFACE needs on top of the pack: the
// reply protocol (the pack says bare JSON; this chat wants one prose line + one
// fenced block), the developer-content CodeBlock/Math teaching (playground
// funnel, Phase 294 – not part of the pack), and the measured Frugality
// posture. The composed value is pinned by test/closedLoop.test.ts (every
// fenced example decodes/applies through the app's own loop) and
// test/promptPack.test.ts (the pack bytes + its eval-proven teachings are
// actually inline).
[<ImportDefault("../../fuaran-dotnet/docs/prompt-pack/system-prompt.md?raw")>]
let private packPrompt: string = jsNative

// The chat-surface overlay. The first paragraph SUPERSEDES the pack's
// "no prose, no fences" reply rule – this UI parses the reply's single fenced
// block and shows the prose line in the conversation pane.
let private playgroundOverlay =
  """# This playground's chat protocol (supersedes the reply rule above)

You are authoring inside a chat playground. Reply with a short (one sentence) plain-text description of what you built or changed, then a single fenced JSON code block containing EXACTLY ONE JSON document (the Node or TreeOp described above). Emit nothing after the block.

- **First turn (no tree yet):** emit a full **Node** tree.
- **Follow-up turns:** you will be shown the current tree's JSON. Emit a **TreeOp** (or a "Batch" of TreeOps) that edits it. Emit a full replacement Node only when a rewrite is genuinely clearer.

# Developer content: CodeBlock and Math

Prefer these over a Markdown fence when the content IS code or maths. Unlike most kinds, these carry ALL their fields (emit every one shown):

- `CodeBlock` (`"code":"<raw source, real newlines>"`, `"language":"<fsharp|typescript|python|json|…>"`, `"lineNumbers":<bool>`, `"highlightLines":[<1-based ints, [] for none>]`, `"copyable":<bool>`). Renders a first-class `<pre><code>` with a language tag + copy button – use it for any code sample, NOT a Markdown ```fence.
- `Math` (`"source":"<LaTeX, e.g. E = mc^2>"`, `"display":"Block"|"Inline"`). Renders a real KaTeX equation – use it for any formula, NOT `$…$` inside Markdown text.

A code sample as a first-class CodeBlock (NOT a Markdown fence):
```json
{"id":"snippet","kind":{"$type":"CodeBlock","code":"let greet name =\n    printfn \"Hello, %s\" name","language":"fsharp","lineNumbers":true,"highlightLines":[],"copyable":true}}
```

An equation as a first-class Math node (NOT `$…$` in Markdown):
```json
{"id":"eqn","kind":{"$type":"Math","source":"E = mc^2","display":"Block"}}
```

# Frugality

Emit the SMALLEST canonical tree that satisfies the request:
- Omit every optional field whose value would be its default.
- Never invent fields the schema does not define; never add explanatory or placeholder content that was not asked for.
- Use short ids (2-3 words, kebab-case). No filler nodes.
Compact JSON, no prose before or after the fenced block."""

// The Frugality section is the measured per-family posture from the published
// evaluation: on the three providers this playground offers it cut output tokens
// 31-49% with quality holding or improving (one family's parse rate rose 19
// points on the hardest tasks). It is NOT universal – some model families
// mis-handle compact-emission instructions and emit malformed JSON under them –
// so if a provider is ever added here, re-measure before extending it.

let value = packPrompt.Trim() + "\n\n" + playgroundOverlay
