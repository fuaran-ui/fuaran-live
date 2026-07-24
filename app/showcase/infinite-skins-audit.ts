// Computed-style read-back for the Infinite Skins contrast auditor.
//
// Decision 3 of the demo plan: the auditor is the *real* style observer,
// reading computed styles back from the rendered DOM – not a colour-math
// prediction from theme values. This helper does exactly the read-back half:
// for each text-painting Fuaran node it captures the browser's actual
// `getComputedStyle` foreground plus the element-first stack of ancestor
// background colours. The WCAG compositing + contrast + flag derivation then
// runs in F# through the SHIPPED `Fuaran.UI.StyleObserver.Flags` module, so the
// panel shows the observer's genuine flag records, computed from real pixels.

interface Rgba {
  r: number;
  g: number;
  b: number;
  a: number;
}

interface NodeStyle {
  nodeId: string;
  label: string;
  fg: Rgba;
  bgLayers: Rgba[];
}

// Parse a computed `rgb(...)` / `rgba(...)` string. Computed styles are always
// in this form (never hex / named), and "transparent" resolves to
// `rgba(0, 0, 0, 0)`, so a channel regex is sufficient.
function parseColor(s: string): Rgba {
  const m = s.match(/rgba?\(([^)]+)\)/);
  if (!m) return { r: 0, g: 0, b: 0, a: 0 };
  const parts = m[1].split(',').map((p) => parseFloat(p.trim()));
  return {
    r: parts[0] || 0,
    g: parts[1] || 0,
    b: parts[2] || 0,
    a: parts.length >= 4 ? parts[3] : 1,
  };
}

// A node "paints text" if it has a direct (non-whitespace) text child – this
// targets the real leaves (heading, markdown paragraph, metric value, callout
// body) rather than the layout containers that merely wrap them.
function paintsText(el: Element): boolean {
  return Array.from(el.childNodes).some(
    (n) => n.nodeType === Node.TEXT_NODE && (n.textContent || '').trim().length > 0,
  );
}

export function readNodeStyles(): NodeStyle[] {
  const out: NodeStyle[] = [];

  // Audit within the demo's themed stage only. The addressable Fuaran node
  // wrappers (`data-fuaran-node-id`) carry the *default* tone; the text a
  // toned node actually paints (a callout body, a metric value) lives in an
  // inner element that resolves `--fuaran-tone-brand-fg`. So we audit every
  // element that paints text – exactly what a style observer observes – and
  // label it by its nearest addressable ancestor.
  const stage = document.querySelector('.sk-stage');
  if (!stage) return out;

  stage.querySelectorAll('*').forEach((el) => {
    // The injected theme <style> element paints no UI – its text content is CSS.
    if (el.tagName === 'STYLE' || el.tagName === 'SCRIPT') return;
    if (!paintsText(el)) return;

    const fg = parseColor(getComputedStyle(el).color);

    // Element-first background stack, folded to the first opaque layer.
    const bgLayers: Rgba[] = [];
    let cur: Element | null = el;
    while (cur) {
      const c = parseColor(getComputedStyle(cur).backgroundColor);
      if (c.a > 0) bgLayers.push(c);
      if (c.a >= 1) break;
      cur = cur.parentElement;
    }

    const addr = el.closest('[data-fuaran-node-id]');
    out.push({
      nodeId: addr ? addr.getAttribute('data-fuaran-node-id') || '' : '',
      label: (el.textContent || '').trim().slice(0, 44),
      fg,
      bgLayers,
    });
  });

  return out;
}
