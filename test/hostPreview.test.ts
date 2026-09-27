// Phase 1818 — the host-fidelity preview.
//
// Two locks and one behaviour:
//
//   1. The bundled manifest (app/fidelity/render-fidelity.json) is BYTE-identical
//      to the specification corpus's render-fidelity.json — the brandThemeParity
//      pattern applied to a build input. A drifted copy fails here, and the
//      comparison is itself checked against a drifted copy so the lock cannot
//      pass vacuously.
//   2. The manifest's tier set is pinned. The preview offers the render tiers it
//      has a status rule for; a tier a later manifest adds would otherwise be
//      silently left out of the switcher, so it turns this suite red instead.
//   3. The pure annotation reads every node's status off its kind's row, and the
//      summary's counts equal a count taken straight from the manifest JSON.
//
// The corpus is read at $FUARAN_WIRE_FIXTURES, else at the sibling
// ../wire-format-fixtures clone (the layout CI checks out). An absent corpus is
// a FAILURE, never a skip: a parity test that skips when it cannot compare is a
// parity test that passes when nobody looked.
//
// Requires `pnpm run fable:app` to have produced app/output/.

import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

import {
  annotate,
  bundled,
  dress,
  honesty,
  parseManifest,
  summary,
  wrapperId,
  // @ts-expect-error untyped Fable output
} from '../app/output/HostPreview.js';
import { decodeNode, findNode as findNodeById, NodeId } from './tierOutput.js';

/** `Introspect.findNode` takes a `NodeId`; `None` comes back as `undefined`. */
const findNode = (id: string, node: unknown) => findNodeById(new NodeId(id), node);

const here = dirname(fileURLToPath(import.meta.url));
const bundledPath = join(here, '../app/fidelity/render-fidelity.json');
const corpusDir = process.env.FUARAN_WIRE_FIXTURES || resolve(here, '../../wire-format-fixtures');
const corpusPath = join(corpusDir, 'render-fidelity.json');

/** Byte equality — the lock's whole comparison, exercised both ways below. */
const sameBytes = (a: Buffer, b: Buffer): boolean => a.length === b.length && a.equals(b);

// Fidelity case tags, in declaration order (HostPreview.fs `Fidelity`).
const FULL = 0;
const INERT = 1;
const PLACEHOLDER = 2;

/** Unwrap an F# `Result` from Fable output, failing loudly on `Error`. */
/** The Fable-compiled manifest record — untyped output, so its members are read loosely. */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Manifest = any;

const ok = (r: { tag: number; fields: unknown[] }, what: string): Manifest => {
  expect(r.tag, `${what} must be Ok, got Error: ${String(r.fields[0])}`).toBe(0);
  return r.fields[0];
};

/** Decode a hand-authored wire tree through the real strict decoder. */
const tree = (wire: object) => {
  const r = decodeNode(JSON.stringify(wire));
  expect(r.tag, 'the hand-authored wire must decode through the real strict decoder').toBe(0);
  return r.fields[0].fields[0];
};

const box = (id: string, children: object[]) => ({
  id,
  kind: {
    $type: 'Box',
    children,
    layout: { $type: 'Flex', direction: 'Vertical', wrap: false },
    role: 'Group',
  },
});

// One kind of each class the manifest declares: Heading and Badge (`none`),
// Button (`behavioural`), Markdown and Image (`clientOnly`).
const sample = tree(
  box('root', [
    {
      id: 'heading-1',
      kind: { $type: 'Heading', level: 2, text: 'Channel performance', variant: 'Standard' },
    },
    { id: 'badge-1', kind: { $type: 'Badge', label: 'Beta', variant: 'Info' } },
    {
      id: 'button-print',
      kind: {
        $type: 'Button',
        label: 'Print this invoice',
        onClick: {
          $type: 'Chain',
          ops: [{ $type: 'Print' }, { $type: 'Notify', channel: 'printed', payload: 'invoice' }],
        },
        variant: 'Secondary',
      },
    },
    { id: 'markdown-1', kind: { $type: 'Markdown', text: 'Updated hourly.' } },
    {
      id: 'image-1',
      kind: {
        $type: 'Image',
        alt: 'User avatar',
        src: { $type: 'Static', value: '/avatar.png' },
        variant: 'Avatar',
      },
    },
  ]),
);

describe('the bundled fidelity manifest is the corpus manifest', () => {
  it('the corpus clone is present (an absent corpus fails, it does not skip)', () => {
    expect(existsSync(corpusPath), `no render-fidelity.json at ${corpusPath}`).toBe(true);
  });

  it('the bundled copy is byte-identical to the corpus copy', () => {
    const bundledBytes = readFileSync(bundledPath);
    const corpusBytes = readFileSync(corpusPath);
    expect(
      sameBytes(bundledBytes, corpusBytes),
      'app/fidelity/render-fidelity.json has drifted from the corpus — copy the corpus file over it',
    ).toBe(true);
  });

  it('the comparison goes red on a drifted copy (the lock is not vacuous)', () => {
    const bundledBytes = readFileSync(bundledPath);
    const drifted = Buffer.from(bundledBytes);
    const at = drifted.indexOf('"clientOnly"');
    expect(at).toBeGreaterThan(0);
    drifted[at + 1] = 'C'.charCodeAt(0);
    expect(sameBytes(bundledBytes, drifted)).toBe(false);
    expect(sameBytes(bundledBytes, bundledBytes.subarray(0, bundledBytes.length - 1))).toBe(false);
  });
});

describe('the host set is what the manifest declares', () => {
  const manifest = ok(bundled, 'the bundled manifest');

  it('the tier set is pinned — a new tier needs a status ruling, not silence', () => {
    expect([...manifest.Tiers]).toEqual(['source', 'fallback', 'rich']);
  });

  it('the kind rows carry no per-host column yet — one appearing is a switcher entry to rule on', () => {
    // The manifest declares tiers, not named hosts. When a projection adds its
    // own column to the kind rows (a speech or card projection, a native
    // surface), this goes red on the bundle refresh, and the preview gains
    // that host by ruling on the column's vocabulary rather than by guessing it.
    const rows: Record<string, unknown>[] = JSON.parse(readFileSync(bundledPath, 'utf8')).kinds;
    const members = new Set(rows.flatMap((r) => Object.keys(r)));
    expect([...members].sort()).toEqual(
      [
        'contract',
        'fallback',
        'fixtures',
        'intrinsic',
        'kind',
        'obligations',
        'rich',
        'sensitive',
        'source',
      ].sort(),
    );
  });

  it('the switcher offers the render tiers, never the source tier', () => {
    expect([...manifest.Hosts].map((h: { Id: string }) => h.Id)).toEqual(['fallback', 'rich']);
    for (const h of manifest.Hosts) expect(h.Meaning.length).toBeGreaterThan(0);
  });

  it('parseManifest refuses a document without tiers or kinds', () => {
    expect(parseManifest({ kinds: [] }).tag).toBe(1);
    expect(parseManifest({ tiers: [] }).tag).toBe(1);
    expect(parseManifest({ tiers: [], kinds: [{ kind: 'Badge' }] }).tag).toBe(1);
  });

  it('an undeclared host is refused rather than guessed', () => {
    expect(annotate(manifest, sample, 'source')).toBeUndefined();
    expect(annotate(manifest, sample, 'no-such-host')).toBeUndefined();
  });
});

describe('the pure annotation', () => {
  const manifest = ok(bundled, 'the bundled manifest');
  const corpusRows: { kind: string; rich: { class: string } }[] = JSON.parse(
    readFileSync(bundledPath, 'utf8'),
  ).kinds;
  const classOf = (kind: string) => corpusRows.find((r) => r.kind === kind)?.rich.class;

  it('on the rich tier every node renders at full fidelity', () => {
    const p = annotate(manifest, sample, 'rich');
    expect(p.Total).toBe(6);
    expect(p.FullCount).toBe(6);
    expect([...p.Degraded]).toEqual([]);
    expect(summary(p)).toBe('6 of 6 nodes render at full fidelity on this host; 0 placeholders');
  });

  it('on the fallback tier each node takes its kind row: clientOnly is a placeholder, behavioural is inert', () => {
    const p = annotate(manifest, sample, 'fallback');
    const byId = new Map(
      [...p.Degraded].map((d: { Id: string; Fidelity: { tag: number } }) => [d.Id, d.Fidelity.tag]),
    );
    expect(byId.get('markdown-1')).toBe(PLACEHOLDER);
    expect(byId.get('image-1')).toBe(PLACEHOLDER);
    expect(byId.get('button-print')).toBe(INERT);
    expect(byId.has('heading-1')).toBe(false);
    expect(byId.has('badge-1')).toBe(false);
    expect(p.Root.Fidelity.tag).toBe(FULL);
    expect(summary(p)).toBe(
      '3 of 6 nodes render at full fidelity on this host; 2 placeholders; 1 without its behaviour',
    );
  });

  it('the summary counts match a count taken straight from the manifest', () => {
    const p = annotate(manifest, sample, 'fallback');
    const kinds = ['Box', 'Heading', 'Badge', 'Button', 'Markdown', 'Image'];
    const expectedFull = kinds.filter((k) => classOf(k) === 'none').length;
    const expectedPlaceholders = kinds.filter((k) => classOf(k) === 'clientOnly').length;
    expect(p.FullCount).toBe(expectedFull);
    expect(
      [...p.Degraded].filter((d: { Fidelity: { tag: number } }) => d.Fidelity.tag === PLACEHOLDER)
        .length,
    ).toBe(expectedPlaceholders);
  });

  it('a placeholder stands in for its whole subtree', () => {
    const nested = tree(
      box('outer', [
        { id: 'md', kind: { $type: 'Markdown', text: 'x' } },
        box('inner', [{ id: 'b', kind: { $type: 'Badge', label: 'y', variant: 'Info' } }]),
      ]),
    );
    const p = annotate(manifest, nested, 'fallback');
    expect(p.Total).toBe(4);
    expect(p.FullCount).toBe(3);
  });
});

describe('the dressed render tree', () => {
  const manifest = ok(bundled, 'the bundled manifest');

  it('wraps every degraded node in a labelled wrapper and leaves full nodes alone', () => {
    const dressed = dress(manifest, 'fallback', sample);
    for (const id of ['markdown-1', 'image-1', 'button-print']) {
      expect(findNode(wrapperId(id), dressed), `wrapper for ${id}`).toBeDefined();
    }
    for (const id of ['heading-1', 'badge-1']) {
      expect(findNode(wrapperId(id), dressed), `no wrapper for ${id}`).toBeUndefined();
      expect(findNode(id, dressed)).toBeDefined();
    }
    // The placeholder REPLACES the client-only render; the inert node keeps its markup.
    expect(findNode('markdown-1', dressed)).toBeUndefined();
    expect(findNode('button-print', dressed)).toBeDefined();
  });

  it('on the rich tier the tree is unchanged', () => {
    const dressed = dress(manifest, 'rich', sample);
    expect(findNode(wrapperId('markdown-1'), dressed)).toBeUndefined();
    expect(findNode('markdown-1', dressed)).toBeDefined();
  });

  it('says in one sentence that it is a declared-fidelity simulation', () => {
    expect(honesty).toMatch(/declared-fidelity simulation/);
    expect(honesty).toMatch(/not that host's pixels/);
    expect(honesty.split('. ').length).toBe(1);
  });
});
