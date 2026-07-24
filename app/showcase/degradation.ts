// Helpers for the Degradation Ladder.
//
// The ladder shows the SAME rendered tree at two fidelity rungs. Rung 1 gets a
// client-only "rich" enhancement (dependency-free syntax highlighting – exactly
// the post-hydration layer the CodeBlock fidelity contract declares). Rung 2 is
// the same markup dropped into a genuinely script-disabled sandboxed iframe, so
// no client enhancement can run – proving the deterministic fallback tier renders
// everything with JavaScript off. These helpers read the live DOM; they never
// fabricate the difference.

// Keyword sets for the two exhibit languages – the highlighter is illustrative,
// not a full lexer (it is the rich tier, declared outside the parity gate).
const KEYWORDS = new Set([
  'let',
  'module',
  'open',
  'match',
  'with',
  'type',
  'fun',
  'rec',
  'and',
  'in',
  'def',
  'return',
  'import',
  'class',
  'if',
  'else',
  'elif',
  'for',
  'while',
]);

function escapeHtml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

// Apply keyword highlighting to every code element under `.dl-rung1`. Idempotent.
export function highlightRich(): number {
  const codes = document.querySelectorAll('.dl-rung1 .fuaran-codeblock-code');
  let touched = 0;
  codes.forEach((el) => {
    if (el.getAttribute('data-dl-hl')) return;
    const text = el.textContent || '';
    const html = escapeHtml(text).replace(/[A-Za-z_]+/g, (m) =>
      KEYWORDS.has(m) ? `<span class="dl-kw">${m}</span>` : m,
    );
    el.innerHTML = html;
    el.setAttribute('data-dl-hl', '1');
    touched += 1;
  });
  return touched;
}

// Concatenate every readable stylesheet's rules – the real CSS, so the sandboxed
// iframe styles the fallback markup exactly as the parent does. Cross-origin
// sheets that throw on `cssRules` are skipped (there are none same-origin here).
export function collectCss(): string {
  let out = '';
  for (const sheet of Array.from(document.styleSheets)) {
    try {
      for (const rule of Array.from(sheet.cssRules)) out += rule.cssText + '\n';
    } catch {
      /* cross-origin sheet – skip */
    }
  }
  return out;
}

// The clean (un-highlighted) rendered exhibit markup – the source for the
// script-disabled iframe.
export function sourceHtml(): string {
  const el = document.querySelector('.dl-source');
  return el ? el.innerHTML : '';
}

// Read back what actually rendered inside the script-disabled iframe – used to
// prove the fallback tier is intact (equation, code, open modal, scroll) and
// that zero scripts and zero rich-highlight spans reached it.
export function probeIframe(): {
  math: number;
  msup: number;
  code: number;
  modal: number;
  scroll: number;
  scripts: number;
  highlightSpans: number;
} {
  const f = document.querySelector('.dl-rung2 iframe') as HTMLIFrameElement | null;
  const doc = f?.contentDocument;
  const q = (sel: string) => (doc ? doc.querySelectorAll(sel).length : -1);
  return {
    math: q('.fuaran-math'),
    // Phase 658 — the no-JS MathML tier: real <msup> superscripts, rendered by
    // the browser with zero scripts. Proves the equation is genuinely typeset,
    // not shown as raw source.
    msup: q('msup'),
    code: q('.fuaran-codeblock'),
    modal: q('.fuaran-modal-dialog'),
    scroll: q('.fuaran-scrollarea'),
    scripts: q('script'),
    highlightSpans: q('.dl-kw'),
  };
}
