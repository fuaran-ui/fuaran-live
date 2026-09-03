# The Console — the playground's interactive live-debug surface

The playground already shows you _what the tree is_: the preview renders it, the
Source card projects it into ten host languages, the Editor walks it node by
node. The **Console** is where you _interrogate_ it — and where you can change it
with a `TreeOp` and watch the preview move.

It lives under **More tools → "Console: query and poke the live tree"**, beside
the other developer affordances, and it works on whatever tree is on screen right
now: a model emission, a loaded example, or one you have been editing.

## What you can ask it

Type a call and press Enter (Shift+Enter for a newline). The example chips fill
the box for you.

| Call                                 | What comes back                                                                                                                                |
| ------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `getNodeState("submit-btn")`         | The node's **typed** snapshot: its kind, its bound binding slots with their wire-form expressions, its child ids.                              |
| `getBindingValue("readout", "Text")` | What one binding slot currently resolves to, resolved the same way the live preview resolves it.                                               |
| `getRenderedDom("submit-btn")`       | Where the node actually landed: viewport box, plus overflowing / hidden flags.                                                                 |
| `inspectTree()`                      | The same structural snapshot, recursively, for the whole tree. Start here — it lists every id you can address.                                 |
| `findNodes("Button")`                | Every node id of that kind.                                                                                                                    |
| `getAffordances()`                   | The natural-language commands the _host_ has declared. A host declares these; this playground registers none, so the answer is honestly empty. |
| `treeRevision()`                     | An opaque token identifying the current tree state.                                                                                            |
| `apply({ … })`                       | Issue a `TreeOp` against the live tree (below).                                                                                                |
| `help()`                             | The surface's own one-screen reference.                                                                                                        |

An answer, a refusal and a failure all land in the **Log** beneath the box,
newest first.

## Applying an op

`apply` takes a canonical `TreeOp` document — the same wire shape the model
emits:

```
apply({"$type":"UpdateProp","nodeId":"submit-btn","path":"label","value":"Send"})
```

It runs through two gates, in this order:

1. **The dispatch gate.** The host's policy decides _before_ the document is
   even read. This pane's policy permits exactly one thing — applying a
   `TreeOp` — and refuses every other host action.
2. **The edit gate.** The op is applied to a _candidate_ tree, and the candidate
   is refused if the edit **introduces** a validator defect. This is the very
   same gate the Editor uses, so "what the Console will accept" and "what the
   Editor will accept" have one definition rather than two that can drift.

A permitted op folds into the session exactly as an editor change does —
attributed, hash-chained, and undoable from the Editor's history row. A refused
one changes nothing at all: the candidate was never folded in, and the Log says
which gate refused it and why. A document that does not decode is reported as a
failure rather than a refusal, because those are different facts about your
input.

## What it is not

- **It is not a JavaScript console.** The box accepts the fixed set of calls in
  the table above and nothing else. There is no `eval`, no `Function`, no
  dynamic import: an input it cannot parse becomes an error message, never
  something that runs.
- **It sends nothing anywhere.** No network call, no `localStorage`, no cookie,
  no analytics. It reads the tree already in your tab's memory and writes back
  only into the session you are already editing — which, like everything else
  here, disappears when you close the tab.
- **It does not publish a global.** The renderer ships this same introspection
  surface as `window.__fuaran`, gated to development builds. The Console drives
  that surface object directly rather than registering it, so the shipped site
  leaves the gate exactly where the renderer put it — open your browser's own
  DevTools console on this page and `__fuaran` is still `undefined`.

## For maintainers

The pane is `app/Console.fs`; its header carries the design reasoning. The
introspection answers come from the renderer's shipped
`DebugGlobal.buildGlobalWith`, so there is one implementation of `getNodeState`
rather than a second one written for a panel. `test/console.test.ts` drives the
whole thing headlessly over the Fable output, including the two posture claims
above (no egress, no global) checked against the module's own generated source.
