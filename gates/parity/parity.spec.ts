// Phase 85 — behavioural cross-implementation parity test.
//
// The same canonical wire JSON, rendered through the TS `@fuaran-ui/renderer`
// (ts-host.html) and the F# `Fuaran.UI.Renderer` (fable-host.html, Fable-
// compiled), must produce structurally identical DOM with the same CSS class
// vocabulary + ARIA contract. This is the regression guard over the Phase 77
// reference-CSS byte-copy + parity-locked class names — exercised on real
// wire-format-fixtures corpus trees, not just hand-picked shapes.
//
// The test drives the two host iframe pages DIRECTLY (no LLM, no BYOK key, no
// toggle UI): it posts the wire JSON over postMessage and reads each host's
// rendered subtree. A divergence in tag nesting, class set, node id, ARIA, or
// text content fails the build — per WIRE_FORMAT.md §11 such a finding is a
// real cross-tier contract break, not a test bug.

import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { test, expect, type Page } from '@playwright/test';

const here = dirname(fileURLToPath(import.meta.url));
// workspace/wire-format-fixtures/nodes — the canonical corpus, shared by both
// hosts (fuaran-live → workspace root is ../.., fixtures live one level up again
// in the workspace repo per the CI workspace assembly).
const corpus = resolve(here, '../../../wire-format-fixtures/nodes');
// The §16 lenient-accept family: shorthand input + the verbose canonical form a
// conformant host MUST normalise it to (WIRE_FORMAT.md §16, normative).
const lenientCorpus = resolve(here, '../../../wire-format-fixtures/lenient');

// The canonical primitive matrix (page/section composite + form + table) plus a
// few more structural primitives. Every entry is a real corpus fixture.
//
// `form-1` encodes a Choice whose options-source round-tripped to the
// `<opaque>` sentinel (a non-array Static options list the encoder could not
// serialise — WIRE_FORMAT.md §5). Phase 131 settled the cross-host contract:
// an opaque/non-array options source renders NO concrete options on every
// conformant host. The TS renderer already does this (its `asArray` coerces a
// non-array source to `[]`); the F# renderer now strips the decoder's opaque
// placeholder (Render.fs `resolveOptions`) so both emit the same `<select>`
// with only the structural `—` placeholder option. `form-1` is therefore in
// the green matrix. `form-ranged` remains the numeric-range form primitive.
//
// `grid-editable-state` joins the matrix for Phase 666: it is the only fixture
// carrying `editable: true` over a `$state` row source, and until it landed the
// two hosts could and did disagree about that shape with nothing to catch it —
// the F# host drew `<input>` cells where the TypeScript host drew static text.
// Structural parity is necessary but NOT sufficient for it, which is why the
// dedicated affordance test below exists: two hosts that BOTH regressed to
// static cells would still be structurally identical.
const MATRIX = [
  'composite-root',
  'form-1',
  'form-ranged',
  'table-1',
  'card-1',
  'stack-1',
  'heading-1',
  'grid-editable-state',
] as const;

interface HostPage {
  readonly path: string;
  readonly rootId: string;
}

const TS_HOST: HostPage = { path: '/ts-host.html', rootId: 'ts-host-root' };
const FABLE_HOST: HostPage = { path: '/fable-host.html', rootId: 'fable-host-root' };

/**
 * Render `wire` in the given host page and return a normalized string capturing
 * the parity contract surface: tag nesting + sorted CSS classes + node id +
 * ARIA/role + trimmed text. Inline styles, React-internal attributes, and
 * whitespace are deliberately excluded — they are not part of the cross-tier
 * contract.
 */
async function renderAndNormalize(page: Page, host: HostPage, wire: string): Promise<string> {
  await page.goto(host.path);
  return page.evaluate(
    async ({ rootId, wire }) => {
      const root = document.getElementById(rootId);
      if (root === null) return '(no root element)';

      // The host attaches its message listener at boot; poll-post until the
      // subtree paints to dodge the listener-attach race.
      const deadline = Date.now() + 5000;
      while (Date.now() < deadline) {
        window.postMessage({ kind: 'fuaran:render', wire }, '*');
        await new Promise((r) => setTimeout(r, 60));
        if (root.children.length > 0) break;
      }

      const SKIP = new Set(['STYLE', 'SCRIPT']);
      const indent = (d: number): string => '  '.repeat(d);

      const walk = (node: ChildNode, depth: number): string => {
        if (node.nodeType === Node.TEXT_NODE) {
          const t = (node.textContent ?? '').replace(/\s+/g, ' ').trim();
          return t.length > 0 ? `${indent(depth)}#text:${t}` : '';
        }
        if (node.nodeType !== Node.ELEMENT_NODE) return '';
        const el = node as Element;
        if (SKIP.has(el.tagName)) return '';

        const classes = Array.from(el.classList).sort().join('.');
        const attrs: string[] = [];
        for (const a of Array.from(el.attributes)) {
          const keep =
            a.name === 'role' ||
            a.name.startsWith('aria-') ||
            a.name === 'data-fuaran-node-id' ||
            a.name === 'type' ||
            a.name === 'href';
          if (keep) attrs.push(`${a.name}=${a.value}`);
        }
        attrs.sort();

        const head = `${indent(depth)}<${el.tagName.toLowerCase()}${classes ? '.' + classes : ''}${
          attrs.length > 0 ? ' ' + attrs.join(' ') : ''
        }>`;
        const kids = Array.from(el.childNodes)
          .map((c) => walk(c, depth + 1))
          .filter((s) => s.length > 0);
        return [head, ...kids].join('\n');
      };

      return Array.from(root.childNodes)
        .map((c) => walk(c, 0))
        .filter((s) => s.length > 0)
        .join('\n');
    },
    { rootId: host.rootId, wire },
  );
}

/** Collect the set of CSS class names used anywhere in a host's render. */
function classVocabulary(normalized: string): Set<string> {
  const classes = new Set<string>();
  for (const line of normalized.split('\n')) {
    const m = /^<\w+((?:\.[\w-]+)+)/.exec(line.trim());
    if (m !== null) for (const c of m[1].split('.').filter(Boolean)) classes.add(c);
  }
  return classes;
}

/**
 * Render `wire` in the given host page and return the host's `fuaran:rendered`
 * acknowledgement — decode ok + the canonical re-encoding (`encode(decode(wire))`).
 * The test page IS the host page, so `window.parent === window` and the host's
 * post-to-parent ack arrives back on the same window.
 */
async function renderAndCanonical(
  page: Page,
  host: HostPage,
  wire: string,
): Promise<{ ok: boolean; canonical: string | null }> {
  await page.goto(host.path);
  return page.evaluate(
    ({ wire }) =>
      new Promise<{ ok: boolean; canonical: string | null }>((resolveAck) => {
        // The host attaches its message listener at boot; poll-post until the
        // first `rendered` ack to dodge the listener-attach race.
        const poster = setInterval(
          () => window.postMessage({ kind: 'fuaran:render', wire }, '*'),
          60,
        );
        const deadline = setTimeout(() => {
          cleanup();
          resolveAck({ ok: false, canonical: null });
        }, 5000);
        const onMessage = (ev: MessageEvent): void => {
          const d = ev.data as { kind?: string; ok?: boolean; canonical?: string | null } | null;
          if (d !== null && typeof d === 'object' && d.kind === 'fuaran:rendered') {
            cleanup();
            resolveAck({ ok: d.ok === true, canonical: d.canonical ?? null });
          }
        };
        const cleanup = (): void => {
          clearInterval(poster);
          clearTimeout(deadline);
          window.removeEventListener('message', onMessage);
        };
        window.addEventListener('message', onMessage);
      }),
    { wire },
  );
}

for (const id of MATRIX) {
  test(`cross-host parity — ${id}`, async ({ page }) => {
    const wire = readFileSync(resolve(corpus, `${id}.json`), 'utf8').trim();

    const tsDom = await renderAndNormalize(page, TS_HOST, wire);
    const fableDom = await renderAndNormalize(page, FABLE_HOST, wire);

    expect(tsDom.length, `TS host rendered nothing for ${id}`).toBeGreaterThan(0);
    expect(fableDom.length, `Fable host rendered nothing for ${id}`).toBeGreaterThan(0);

    // CSS class vocabulary parity (the Phase 77 parity-lock, made visible).
    const tsClasses = [...classVocabulary(tsDom)].sort();
    const fableClasses = [...classVocabulary(fableDom)].sort();
    expect(fableClasses, `class-vocabulary divergence for ${id}`).toEqual(tsClasses);

    // Full structural parity: tag nesting + classes + node id + ARIA + text.
    expect(fableDom, `DOM-structure divergence for ${id}`).toBe(tsDom);
  });
}

// Phase 666 — the editable-grid affordance, asserted rather than implied.
//
// The matrix entry above proves the two hosts agree; this proves what they
// agree ON. A grid declaring `editable: true` over a directly-`$state` row
// source has a destination for its write-back, so every field-projected
// Text/Numeric cell must render as an `<input class="fuaran-grid-cell-editable">`
// — the Phase 663 contract, ported to the TypeScript host by this phase. Both
// hosts silently falling back to static `<span>` cells is the exact regression
// the structural comparison cannot see, and it is the state this host pair was
// actually in before 666.
//
// The expected count is DERIVED from the fixture rather than written down: rows
// × field-projected Text/Numeric columns. A fixture that gains a row or a column
// moves the expectation with it, and a fixture that stops declaring `editable`
// fails the derivation's own preconditions rather than quietly lowering the bar
// to zero.
const EDITABLE_GRID = 'grid-editable-state';

/** Editable grid cells in a normalized render — the class is the affordance. */
function editableCellCount(normalized: string): number {
  return normalized
    .split(/\r?\n/)
    .filter((line) => /^<input[^\s>]*\.fuaran-grid-cell-editable(?![\w-])/.test(line.trim()))
    .length;
}

test(`cross-host editable-grid write-back affordance — ${EDITABLE_GRID}`, async ({ page }) => {
  const wire = readFileSync(resolve(corpus, `${EDITABLE_GRID}.json`), 'utf8').trim();

  interface GridColumn {
    readonly field?: string;
    readonly kind: { readonly $type: string };
  }
  const spec = (JSON.parse(wire) as { kind: Record<string, unknown> }).kind;
  expect(spec['$type'], `${EDITABLE_GRID} is no longer a DataGrid`).toBe('DataGrid');
  expect(spec['editable'], `${EDITABLE_GRID} no longer declares editable: true`).toBe(true);

  const source = spec['source'] as { $type: string; defaultValue: readonly unknown[] };
  expect(source.$type, `${EDITABLE_GRID}'s source is no longer a direct $state binding`).toBe(
    'State',
  );

  const editableColumns = (spec['columns'] as readonly GridColumn[]).filter(
    (c) => c.field !== undefined && (c.kind.$type === 'Text' || c.kind.$type === 'Numeric'),
  );
  const expected = source.defaultValue.length * editableColumns.length;
  expect(expected, `${EDITABLE_GRID} projects no editable cells at all`).toBeGreaterThan(0);

  const tsDom = await renderAndNormalize(page, TS_HOST, wire);
  const fableDom = await renderAndNormalize(page, FABLE_HOST, wire);

  expect(
    editableCellCount(tsDom),
    `TS host rendered ${editableCellCount(tsDom)} editable cells for ${EDITABLE_GRID}, expected ${expected} — an editable grid over a $state source must draw inputs, not static text`,
  ).toBe(expected);
  expect(
    editableCellCount(fableDom),
    `Fable host rendered ${editableCellCount(fableDom)} editable cells for ${EDITABLE_GRID}, expected ${expected}`,
  ).toBe(expected);
});

// §16 lenient-accept normalisation parity: both hosts must ACCEPT the shorthand
// input and re-encode it to byte-identical canonical form — the expected file's
// exact bytes. Rejecting the shorthand, or normalising to different bytes, is
// non-conformant (WIRE_FORMAT.md §16). The same shorthand also feeds the in-app
// parity pane's corpus picker (src/hosts/parityFixtures.ts).
const LENIENT = ['lenient-bare-text-button-label', 'lenient-bare-text-callout'] as const;

for (const id of LENIENT) {
  test(`cross-host §16 lenient-accept normalisation — ${id}`, async ({ page }) => {
    const wire = readFileSync(resolve(lenientCorpus, `${id}.json`), 'utf8').trim();
    const expected = readFileSync(resolve(lenientCorpus, `${id}.expected.json`), 'utf8').trim();

    const ts = await renderAndCanonical(page, TS_HOST, wire);
    const fable = await renderAndCanonical(page, FABLE_HOST, wire);

    expect(ts.ok, `TS host rejected the §16 shorthand for ${id}`).toBe(true);
    expect(fable.ok, `Fable host rejected the §16 shorthand for ${id}`).toBe(true);
    expect(ts.canonical, `TS host normalised ${id} to unexpected bytes`).toBe(expected);
    expect(fable.canonical, `Fable host normalised ${id} to unexpected bytes`).toBe(expected);

    // The normalised trees must also render identically (the DOM-parity claim
    // holds through the lenient path, not just the canonical one).
    const tsDom = await renderAndNormalize(page, TS_HOST, wire);
    const fableDom = await renderAndNormalize(page, FABLE_HOST, wire);
    expect(fableDom, `DOM-structure divergence for ${id}`).toBe(tsDom);
  });
}
