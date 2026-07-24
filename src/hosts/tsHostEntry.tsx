// Phase 85 – the TypeScript render-host iframe entry.
//
// Standalone page loaded inside an iframe by the preview-pane host-switch. The
// parent posts canonical wire-format JSON; this entry decodes it through
// `@fuaran-ui/ops` `decodeNode` and renders the tree through the TS
// `@fuaran-ui/renderer`. Its sibling iframe runs the F# (Fable) host
// (fable-host/Host.fs) on the SAME wire JSON – the behavioural-parity pair.
//
// No effect ports are wired: a decoded tree's actions are inert (the wire form
// carries no live closures), so the runtime is the empty no-op object. The host
// renders structure + class vocabulary, which is what parity asserts.

import { StrictMode, type ReactElement } from 'react';
import { createRoot, type Root } from 'react-dom/client';

import { FuaranRenderer, emptySources, defaultTheme, enhanceMath } from '@fuaran-ui/renderer';
import { decodeNode, encodeNode } from '@fuaran-ui/ops';
import type { Node } from '@fuaran-ui/schema';

import '@fuaran-ui/renderer/css';
import 'katex/dist/katex.min.css';
// The icon-contract glyph map — rendered trees carry empty data-icon hooks;
// this maps the common names to currentColor SVG masks.
import '../../app/icon-glyphs.css';

import { RENDER, CLEAR, READY, RENDERED, type HostInbound } from './protocol';

const HOST = 'ts' as const;

const container = document.getElementById('ts-host-root');
if (container === null) throw new Error('ts-host-root mount target missing');
// Bind the narrowed non-null element to a typed local: the `container` const's
// null-narrowing is lost inside the `requestAnimationFrame` closure below (tsc
// re-widens it to `HTMLElement | null`), but an explicitly-`HTMLElement` local
// carries the non-null type into the capture.
const mount: HTMLElement = container;
const root: Root = createRoot(mount);

const postToParent = (message: object): void => window.parent?.postMessage(message, '*');

const renderTree = (tree: Node<unknown> | null): ReactElement =>
  tree === null ? (
    <></>
  ) : (
    <FuaranRenderer tree={tree} sources={emptySources} runtime={{}} theme={defaultTheme} />
  );

const renderError = (message: string): ReactElement => (
  <pre className="fl-ts-host-error">{message}</pre>
);

function paint(content: ReactElement): void {
  root.render(<StrictMode>{content}</StrictMode>);
  // Phase 293 – client-only KaTeX pass over the just-rendered deterministic
  // math fallback. Scheduled after the React commit (next frame) so the DOM
  // exists; idempotent, so a re-render of a fresh tree re-enhances cleanly.
  requestAnimationFrame(() => enhanceMath(mount));
}

window.addEventListener('message', (ev: MessageEvent) => {
  const data = ev.data as HostInbound | undefined;
  if (data === undefined || data === null) return;

  if (data.kind === CLEAR) {
    paint(renderTree(null));
    return;
  }
  if (data.kind === RENDER) {
    const decoded = decodeNode(data.wire);
    if (decoded.ok) {
      paint(renderTree(decoded.value));
      // Report the canonical re-encoding so the parent can assert byte-parity
      // against the sibling F# host (encode(decode(wire)) must match).
      postToParent({
        kind: RENDERED,
        host: HOST,
        ok: true,
        error: null,
        canonical: encodeNode(decoded.value),
      });
    } else {
      paint(
        renderError(`${decoded.error.code} – ${decoded.error.message} (at ${decoded.error.path})`),
      );
      postToParent({
        kind: RENDERED,
        host: HOST,
        ok: false,
        error: decoded.error.message,
        canonical: null,
      });
    }
  }
});

// Announce readiness so the parent posts the current wire JSON.
postToParent({ kind: READY, host: HOST });
