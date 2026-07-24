// Geometry read-back for the Blind Surveyor "layout ledger".
//
// The page's whole claim: layout is READ, not looked at. This helper reads the
// observed geometry of each tracked node straight from the rendered DOM –
// scroll/client width and computed overflow – the exact inputs the shipped
// LayoutObserver derivation (run in F#) turns into typed `OverflowHorizontal`
// flags. No pixels are sampled; every value is a measured fact about the laid-out
// tree, and it is identical whether or not an opaque shutter covers the preview
// (an overlay hides pixels; it does not remove the element from layout).

interface NodeGeom {
  found: boolean;
  w: number;
  h: number;
  scrollW: number;
  clientW: number;
  overflowX: string;
}

// Measure each requested node (addressed by its [data-fuaran-node-id]) inside
// the survey stage. Returns a plain object keyed by node id so F# can read each
// entry back with the `?` dynamic accessor.
export function measureNodes(ids: string[]): Record<string, NodeGeom> {
  const out: Record<string, NodeGeom> = {};
  const stageEl = document.querySelector('.bs-stage') as HTMLElement | null;

  for (const id of ids) {
    // The addressable node ([data-fuaran-node-id]) is a display:block wrapper;
    // the element that actually lays out (and can overflow) is the inner
    // .fuaran-layout-grid. Measure that when present, else the node itself.
    const node = stageEl
      ? (stageEl.querySelector(`[data-fuaran-node-id="${id}"]`) as HTMLElement | null)
      : null;
    const el = (node?.querySelector('.fuaran-layout-grid') as HTMLElement | null) ?? node;
    if (!el) {
      out[id] = { found: false, w: 0, h: 0, scrollW: 0, clientW: 0, overflowX: 'visible' };
      continue;
    }
    const rect = el.getBoundingClientRect();
    out[id] = {
      found: true,
      w: rect.width,
      h: rect.height,
      scrollW: el.scrollWidth,
      clientW: el.clientWidth,
      overflowX: getComputedStyle(el).overflowX,
    };
  }
  return out;
}
