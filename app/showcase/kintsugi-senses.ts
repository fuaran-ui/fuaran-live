// Structural read-back for the Kintsugi "senses" panel.
//
// The whole point of the page: the machine diagnoses a broken UI from its
// STRUCTURE, not a screenshot. This helper reads the four structural signals
// the senses panel reports, straight from the rendered DOM:
//   - how many rows the grid actually rendered (render-time / binding health),
//   - whether the action button carries the renderer's `fuaran-button-unwired`
//     marker (behavioural health – a dead action),
//   - the computed foreground + ancestor-background stack of each text node
//     (fed to the shipped StyleObserver WCAG derivation in F# – style health),
//   - the app stage's geometry + overflow (fed to the shipped LayoutObserver
//     derivation in F# – layout health).
// No pixels are sampled; every signal is a fact about the rendered tree.

interface Rgba {
  r: number;
  g: number;
  b: number;
  a: number;
}

interface StyleInput {
  fg: Rgba;
  bgLayers: Rgba[];
}

interface Senses {
  gridCells: number;
  buttonUnwired: boolean;
  styleInputs: StyleInput[];
  stage: { w: number; h: number; scrollW: number; clientW: number; overflowX: string };
}

function parseColor(s: string): Rgba {
  const m = s.match(/rgba?\(([^)]+)\)/);
  if (!m) return { r: 0, g: 0, b: 0, a: 0 };
  const p = m[1].split(',').map((x) => parseFloat(x.trim()));
  return { r: p[0] || 0, g: p[1] || 0, b: p[2] || 0, a: p.length >= 4 ? p[3] : 1 };
}

function paintsText(el: Element): boolean {
  return Array.from(el.childNodes).some(
    (n) => n.nodeType === Node.TEXT_NODE && (n.textContent || '').trim().length > 0,
  );
}

export function readSenses(): Senses {
  const stageEl = document.querySelector('.k-stage') as HTMLElement | null;

  const styleInputs: StyleInput[] = [];
  if (stageEl) {
    stageEl.querySelectorAll('*').forEach((el) => {
      if (el.tagName === 'STYLE' || el.tagName === 'SCRIPT') return;
      if (!paintsText(el)) return;
      const fg = parseColor(getComputedStyle(el).color);
      const bgLayers: Rgba[] = [];
      let cur: Element | null = el;
      while (cur) {
        const c = parseColor(getComputedStyle(cur).backgroundColor);
        if (c.a > 0) bgLayers.push(c);
        if (c.a >= 1) break;
        cur = cur.parentElement;
      }
      styleInputs.push({ fg, bgLayers });
    });
  }

  const gridCells = stageEl ? stageEl.querySelectorAll('.fuaran-layout-grid > *').length : 0;
  const buttonUnwired = !!(stageEl && stageEl.querySelector('.fuaran-button-unwired'));

  const stage = stageEl
    ? {
        w: stageEl.getBoundingClientRect().width,
        h: stageEl.getBoundingClientRect().height,
        scrollW: stageEl.scrollWidth,
        clientW: stageEl.clientWidth,
        overflowX: getComputedStyle(stageEl).overflowX,
      }
    : { w: 0, h: 0, scrollW: 0, clientW: 0, overflowX: 'visible' };

  return { gridCells, buttonUnwired, styleInputs, stage };
}
