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
//   4. The speech view (Phase 1813's `speech` column, ruled on in round 2): the
//      speech vocabulary is pinned, each node carries its kind's declared class,
//      and the omitted nodes are the listed ones.
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
  annotateSpeech,
  bundled,
  dress,
  honesty,
  parseManifest,
  speechHonesty,
  speechSummary,
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

  it('the kind-row columns are exactly the ruled ones — a new column is a switcher entry to rule on', () => {
    // The manifest declares tiers, not named hosts. When a projection adds its
    // own column to the kind rows (a card projection, a native surface), this
    // goes red on the bundle refresh, and the preview gains that host by ruling
    // on the column's vocabulary rather than by guessing it. `speech` (Phase
    // 1813) is ruled on: it is the speech view, and every row must carry it.
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
        'speech',
      ].sort(),
    );
    // `speech` is USED, not merely tolerated: every row declares it, with no
    // member the speech view does not read.
    for (const r of rows)
      expect(Object.keys(r.speech as object).sort(), String(r.kind)).toEqual(['class', 'note']);
  });

  it('the speech vocabulary is pinned — a new class needs a ruling, not silence', () => {
    const pinned = ['spoken', 'derived', 'announced-only', 'omitted'];
    expect([...manifest.SpeechClasses].map((c: { Class: string }) => c.Class)).toEqual(pinned);
    const declared: { class: string }[] = JSON.parse(
      readFileSync(bundledPath, 'utf8'),
    ).speechClasses;
    expect(declared.map((c) => c.class)).toEqual(pinned);
  });

  it('parseManifest refuses a speech class it has no ruling for, and a row outside the vocabulary', () => {
    const doc = JSON.parse(readFileSync(bundledPath, 'utf8'));
    const grown = {
      ...doc,
      speechClasses: [...doc.speechClasses, { class: 'sung', meaning: 'x' }],
    };
    expect(parseManifest(grown).tag).toBe(1);
    const stray = {
      ...doc,
      kinds: doc.kinds.map((k: { kind: string }, i: number) =>
        i === 0 ? { ...k, speech: { class: 'whispered', note: '' } } : k,
      ),
    };
    expect(parseManifest(stray).tag).toBe(1);
    expect(parseManifest(doc).tag).toBe(0);
  });

  it('the switcher offers the render tiers and the speech view, never the source tier', () => {
    expect([...manifest.Hosts].map((h: { Id: string }) => h.Id)).toEqual([
      'fallback',
      'rich',
      'speech',
    ]);
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

describe('the speech view', () => {
  const manifest = ok(bundled, 'the bundled manifest');
  const doc: { kinds: { kind: string; speech: { class: string; note: string } }[] } = JSON.parse(
    readFileSync(bundledPath, 'utf8'),
  );
  const speechOf = (kind: string) => doc.kinds.find((r) => r.kind === kind)?.speech;

  const withOmitted = tree(
    box('root', [
      {
        id: 'heading-1',
        kind: { $type: 'Heading', level: 2, text: 'Totals', variant: 'Standard' },
      },
      { id: 'skeleton-1', kind: { $type: 'Skeleton', rows: 3 } },
      {
        id: 'button-go',
        kind: {
          $type: 'Button',
          label: 'Go',
          onClick: { $type: 'Notify', channel: 'go', payload: 'x' },
          variant: 'Primary',
        },
      },
    ]),
  );

  type SpeechNode = {
    Id: string;
    Kind: string;
    Class: string | undefined;
    Note: string;
    Children: Iterable<SpeechNode>;
  };

  it('each node carries the class its kind declares, with the kind note', () => {
    const p = annotateSpeech(manifest, withOmitted);
    const flat: SpeechNode[] = [];
    const walk = (n: SpeechNode) => {
      flat.push(n);
      for (const c of n.Children) walk(c);
    };
    walk(p.Root);
    expect(flat.map((n) => n.Id)).toEqual(['root', 'heading-1', 'skeleton-1', 'button-go']);
    for (const n of flat) {
      expect(n.Class, n.Kind).toBe(speechOf(n.Kind)?.class);
      expect(n.Note, n.Kind).toBe(speechOf(n.Kind)?.note);
    }
  });

  it('the summary counts every declared class, and the omitted nodes are the listed ones', () => {
    const p = annotateSpeech(manifest, withOmitted);
    expect(p.Total).toBe(4);
    const kinds = ['Box', 'Heading', 'Skeleton', 'Button'];
    for (const [cls, n] of p.ByClass as Iterable<[string, number]>) {
      expect(n, cls).toBe(kinds.filter((k) => speechOf(k)?.class === cls).length);
    }
    expect([...p.Listed].map((s: { Id: string }) => s.Id)).toEqual(['skeleton-1']);
    expect(speechSummary(p)).toBe(
      '4 nodes on the speech view: 1 spoken, 1 derived, 1 announced-only, 1 omitted',
    );
  });

  it('the speech view is not a render tier: annotate and dress leave it alone', () => {
    expect(annotate(manifest, withOmitted, 'speech')).toBeUndefined();
    expect(dress(manifest, 'speech', withOmitted)).toBe(withOmitted);
  });

  it('a manifest without a speech vocabulary offers no speech view', () => {
    const plain = JSON.parse(readFileSync(bundledPath, 'utf8'));
    delete plain.speechClasses;
    for (const k of plain.kinds) delete k.speech;
    const m = ok(parseManifest(plain), 'the speech-less manifest');
    expect([...m.Hosts].map((h: { Id: string }) => h.Id)).toEqual(['fallback', 'rich']);
    expect(annotateSpeech(m, withOmitted)).toBeUndefined();
  });

  it('says in one sentence that it is declared, not a screen reader', () => {
    expect(speechHonesty).toMatch(/declared-fidelity simulation/);
    expect(speechHonesty).toMatch(/not what a screen reader/);
    expect(speechHonesty.split('. ').length).toBe(1);
  });
});

describe('the CodeBlock row is read as the corpus declares it', () => {
  const manifest = ok(bundled, 'the bundled manifest');
  const row = JSON.parse(readFileSync(bundledPath, 'utf8')).kinds.find(
    (r: { kind: string }) => r.kind === 'CodeBlock',
  );
  const code = tree(
    box('root', [
      {
        id: 'code-1',
        kind: {
          $type: 'CodeBlock',
          code: 'let x = 1',
          copyable: true,
          highlightLines: [],
          language: 'fsharp',
          lineNumbers: false,
        },
      },
    ]),
  );

  it('on the fallback tier the placeholder carries the declared fallback verbatim', () => {
    expect(row.rich.class).toBe('clientOnly');
    const p = annotate(manifest, code, 'fallback');
    const d = [...p.Degraded].find((n: { Id: string }) => n.Id === 'code-1');
    expect(d.Fidelity.tag).toBe(PLACEHOLDER);
    expect(d.Fallback).toBe(row.fallback);
  });

  it('on the speech view it carries the declared speech class', () => {
    const p = annotateSpeech(manifest, code);
    const node = [...p.Root.Children][0];
    expect(node.Class).toBe(row.speech.class);
    expect(node.Note).toBe(row.speech.note);
  });
});
