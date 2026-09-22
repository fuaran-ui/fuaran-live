// Phase 1828 — the source projector reads the transform algebra's renamed
// members, in all three emitters, and projects the same source either way.
//
// Phase 1821 renamed two members of the dataframe algebra across every
// conformant host: a `project` step's rename list is `columns` (was `cols`), and
// a sort key — on a `sort` step and on a `window` spec's frame-ordering entry —
// names its column `column` (was `col`). The former spellings remain DECODE
// ALIASES that no host emits.
//
// `app/Projection.fs` read only the old spellings at nine sites across its three
// emitters. The failure was SILENT and is the reason this suite exists: a tree
// carrying the canonical spellings projected an EMPTY rename list and a sort key
// on the empty-string column, so the playground showed a reader source code that
// parses, runs, and describes a different tree. Nothing threw.
//
// What is asserted here, and why each half is needed:
//
//   * the CANONICAL spellings project to source that actually names the columns
//     — the half that catches the silent-empty defect, which an equality
//     assertion alone cannot (two empty projections are equal);
//   * the LEGACY spellings project to byte-identical source — the alias half,
//     which is what lets a tree saved or pasted before the rename still project;
//   * canonical wins where a value carries both.
//
// The corpus is the oracle for the first two: `lenient-transform-column-member-legacy`
// is a corpus-authored pair carrying all three renamed sites in one tree, and
// every other transform-bearing fixture is swept by rewriting its canonical bytes
// back to the legacy spellings.
//
// This suite is deliberately about the PROJECTOR alone. The byte-identical
// re-encode against the real `@fuaran-ui/*` packages is the conformance harness's
// job (`tests/projection-conformance/`), and that arm additionally depends on the
// pinned host — see `the pinned host has released the rename` at the foot of this
// file.
//
// Requires `pnpm run fable:app` to have produced app/output/.

import { readFileSync, readdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import * as ops from '@fuaran-ui/ops';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { projectByName } from '../app/output/Projection.js';

const here = dirname(fileURLToPath(import.meta.url));
const corpusDir = resolve(here, '../../wire-format-fixtures');

/** The three emitters this phase is about. `projectByName` takes these names. */
const LANGUAGES = ['typescript', 'python', 'fsharp'] as const;

type Json = unknown;

const readJson = (relative: string): Json =>
  JSON.parse(readFileSync(resolve(corpusDir, relative), 'utf8'));

const isObject = (v: Json): v is Record<string, Json> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);

/** Deep-map every object in a tree, outermost first. */
const mapTree = (v: Json, f: (o: Record<string, Json>) => Record<string, Json>): Json => {
  if (Array.isArray(v)) return v.map((x) => mapTree(x, f));
  if (!isObject(v)) return v;
  const mapped = f(v);
  const out: Record<string, Json> = {};
  for (const [k, x] of Object.entries(mapped)) out[k] = mapTree(x, f);
  return out;
};

/** Rename one member of an object, preserving nothing else about it. */
const renameMember = (o: Record<string, Json>, from: string, to: string): Record<string, Json> => {
  if (!(from in o)) return o;
  const out: Record<string, Json> = {};
  for (const [k, v] of Object.entries(o)) out[k === from ? to : k] = v;
  return out;
};

/**
 * Rewrite a canonical tree to the PRE-RENAME spellings — the inverse of what
 * Phase 1821 did, applied only where it applied it: a `project` step's list and
 * the sort keys under a `sort` step's `by` and a `window` spec's `orderBy`.
 *
 * Deliberately NOT a blanket `columns` -> `cols`: a `DataGrid`'s `columns`, an
 * embedded table's `columns` and a Box layout's integer `cols` are different
 * members that did not move, and a sweep that renamed them would be testing the
 * rewriter rather than the projector.
 */
const toLegacySpellings = (tree: Json): Json =>
  mapTree(tree, (o) => {
    if (o.$type === 'project') return renameMember(o, 'columns', 'cols');
    if (o.$type === 'sort' && Array.isArray(o.by)) {
      return { ...o, by: o.by.map((k) => (isObject(k) ? renameMember(k, 'column', 'col') : k)) };
    }
    if (o.$type === 'window' && Array.isArray(o.orderBy)) {
      return {
        ...o,
        orderBy: o.orderBy.map((k) => (isObject(k) ? renameMember(k, 'column', 'col') : k)),
      };
    }
    return o;
  });

/** Every transform step of the named kinds in a tree, in document order. */
const stepsOfKind = (tree: Json, kinds: readonly string[]): Record<string, Json>[] => {
  const found: Record<string, Json>[] = [];
  const walk = (v: Json): void => {
    if (Array.isArray(v)) return v.forEach(walk);
    if (!isObject(v)) return;
    if (typeof v.$type === 'string' && kinds.includes(v.$type)) found.push(v);
    Object.values(v).forEach(walk);
  };
  walk(tree);
  return found;
};

/**
 * The column names a correct projection must mention: every `project` pair's
 * source and output name, and every sort key's column. These are what went
 * missing silently, so they are what the assertions look for.
 */
const renamedColumnNames = (tree: Json): string[] => {
  const names: string[] = [];
  for (const step of stepsOfKind(tree, ['project'])) {
    const list = step.columns ?? step.cols;
    if (!Array.isArray(list)) continue;
    for (const pair of list) {
      if (!isObject(pair)) continue;
      if (typeof pair.a === 'string') names.push(pair.a);
      if (typeof pair.b === 'string') names.push(pair.b);
    }
  }
  for (const step of stepsOfKind(tree, ['sort'])) {
    const by = step.by;
    if (!Array.isArray(by)) continue;
    for (const key of by) {
      if (isObject(key)) {
        const c = key.column ?? key.col;
        if (typeof c === 'string') names.push(c);
      }
    }
  }
  for (const step of stepsOfKind(tree, ['window'])) {
    const orderBy = step.orderBy;
    if (!Array.isArray(orderBy)) continue;
    for (const key of orderBy) {
      if (isObject(key)) {
        const c = key.column ?? key.col;
        if (typeof c === 'string') names.push(c);
      }
    }
  }
  return [...new Set(names)];
};

// ── The corpus fixtures this phase is measured against ───────────────────────
//
// Both families, because neither alone covers the three renamed sites: the
// `nodes/` family carries `project` and `sort` steps and no `window` step, and
// the `lenient/` family's EXPECTED documents (canonical by definition — they are
// what a conformant decoder normalises the input to) carry the window frame
// ordering. Discovered rather than listed, so a corpus that grows a transform
// fixture is covered without an edit here.

interface Fixture {
  readonly id: string;
  readonly tree: Json;
}

const canonicalFixtures = (): Fixture[] => {
  const out: Fixture[] = [];
  for (const family of ['nodes', 'lenient'] as const) {
    for (const file of readdirSync(resolve(corpusDir, family))) {
      if (!file.endsWith('.json')) continue;
      if (family === 'lenient' && !file.endsWith('.expected.json')) continue;
      const tree = readJson(`${family}/${file}`);
      if (stepsOfKind(tree, ['project', 'sort', 'window']).length === 0) continue;
      out.push({ id: `${family}/${file}`, tree });
    }
  }
  return out;
};

const FIXTURES = canonicalFixtures();

describe('the corpus exercises every renamed site (non-vacuity)', () => {
  // Without this, every assertion below could pass over an empty set — the
  // shape of vacuous green this repo's conformance harness names by name.
  for (const kind of ['project', 'sort', 'window'] as const) {
    it(`at least one fixture carries a '${kind}' step`, () => {
      const carrying = FIXTURES.filter((f) => stepsOfKind(f.tree, [kind]).length > 0);
      expect(
        carrying.map((f) => f.id),
        `no corpus fixture carries a '${kind}' step — the site this phase fixed is unexercised`,
      ).not.toHaveLength(0);
    });
  }
});

describe('the renamed members project, in all three emitters (Phase 1828)', () => {
  for (const fixture of FIXTURES) {
    const legacy = toLegacySpellings(fixture.tree);
    const names = renamedColumnNames(fixture.tree);

    it(`${fixture.id}: the rewrite to the legacy spellings is not a no-op`, () => {
      // The sweep is only evidence if it changed something. A fixture whose
      // canonical bytes already read as legacy would otherwise pass the
      // equality assertion below by comparing a tree with itself.
      expect(
        JSON.stringify(legacy),
        `${fixture.id} carries a transform step but no renamed member — the rewriter missed it`,
      ).not.toBe(JSON.stringify(fixture.tree));
    });

    for (const language of LANGUAGES) {
      it(`${fixture.id}: the canonical spellings project to source naming the columns (${language})`, () => {
        const projected = projectByName(language, JSON.stringify(fixture.tree)) as string;
        expect(projected.length).toBeGreaterThan(0);
        // This is the half that catches the silent empty: an empty rename list
        // and an empty-string sort column both project to source that compiles.
        for (const name of names) {
          expect(
            projected,
            `${fixture.id}: the ${language} projection drops the column '${name}' — a reader is shown source that describes a different tree`,
          ).toContain(name);
        }
      });

      it(`${fixture.id}: the legacy spellings project identically (${language})`, () => {
        expect(
          projectByName(language, JSON.stringify(legacy)) as string,
          `${fixture.id}: a tree saved before the rename must project to the same ${language} source`,
        ).toBe(projectByName(language, JSON.stringify(fixture.tree)) as string);
      });
    }
  }
});

describe("the corpus's own legacy-spelling pair", () => {
  // `lenient-transform-column-member-legacy` is authored by the corpus for
  // exactly this rename and carries all three renamed sites in one tree, so it
  // is asserted by name rather than only swept: a corpus that dropped it would
  // otherwise quietly reduce the cover.
  const input = readJson('lenient/lenient-transform-column-member-legacy.json');
  const expected = readJson('lenient/lenient-transform-column-member-legacy.expected.json');

  it('carries a project step, a sort key and a window frame ordering', () => {
    expect(stepsOfKind(expected, ['project'])).not.toHaveLength(0);
    expect(stepsOfKind(expected, ['sort'])).not.toHaveLength(0);
    expect(stepsOfKind(expected, ['window'])).not.toHaveLength(0);
  });

  for (const language of LANGUAGES) {
    it(`the pre-rename input and the normalised expectation project identically (${language})`, () => {
      const fromInput = projectByName(language, JSON.stringify(input)) as string;
      const fromExpected = projectByName(language, JSON.stringify(expected)) as string;
      expect(fromInput).toBe(fromExpected);
      for (const name of renamedColumnNames(expected)) expect(fromExpected).toContain(name);
    });
  }
});

describe('canonical wins where a value carries both spellings', () => {
  // The substrate's alias rule is canonical-wins, and a conformant decoder
  // REFUSES a step carrying both as ambiguous — so this is never reached by a
  // tree that came through a decoder. It is asserted because the projector is
  // total by design and reads raw JSON, so it needs a defined answer, and
  // "whichever member the reader happened to try first" is not one.
  const both = JSON.stringify({
    id: 'both-spellings',
    kind: {
      $type: 'DataGrid',
      columns: [{ field: 'keep', kind: { $type: 'Text' }, label: 'Keep' }],
      rowKeyField: 'keep',
      source: {
        $type: 'Transform',
        pipeline: [
          {
            $type: 'project',
            columns: [{ a: 'canonicalSource', b: 'keep' }],
            cols: [{ a: 'aliasSource', b: 'drop' }],
          },
          {
            $type: 'sort',
            by: [{ column: 'canonicalSortColumn', col: 'aliasSortColumn', dir: 'asc' }],
          },
        ],
        source: {
          columns: { keep: { validity: [true], values: ['a'] } },
          schema: [{ name: 'keep', type: 'string' }],
        },
      },
    },
  });

  for (const language of LANGUAGES) {
    it(`reads the canonical member and not the alias (${language})`, () => {
      const projected = projectByName(language, both) as string;
      expect(projected).toContain('canonicalSource');
      expect(projected).toContain('canonicalSortColumn');
      expect(projected).not.toContain('aliasSource');
      expect(projected).not.toContain('aliasSortColumn');
    });
  }
});

describe('the pinned host has released the rename', () => {
  // This block used to assert the OPPOSITE — that the pinned `@fuaran-ui/ops`
  // still encoded the LEGACY names — as a falsifier naming why the TypeScript
  // conformance arm could not be green however correct this projector was: that
  // arm re-encodes a projected tree through the pinned packages and compares it
  // to the corpus, the corpus is read UNPINNED and carried the canonical
  // spellings, and no published release carried them yet. Its own instruction
  // was to raise the pin the moment it went red and retire it.
  //
  // `@fuaran-ui/ops` 0.28.0 released the rename and this repo now pins it, so
  // that is what happened. What replaces the falsifier is its mirror image,
  // because the property is still worth holding and a deleted assertion holds
  // nothing: the pinned host EMITS the canonical spellings, and still DECODES
  // the legacy ones. The second half is the load-bearing one — the aliases are
  // what let a tree saved, shared or permalinked before the rename still open,
  // and an encoder-only rename would pass the first assertion alone.
  //
  // The two hosts this arm does NOT pin were both behind when this block was
  // first written, and that is where the remaining conformance red lived rather
  // than here. Both have since been raised, in two commits that had to meet:
  //
  //   - the F# tier now pins `Fuaran.UI.*` 0.85.0, the first release carrying
  //     the rename, so its five transform fixtures pass;
  //   - the Python tier now installs `fuaran-ui` 0.7.0. That host needed a
  //     RENAME before it could be raised at all — Phase 1694 moved the
  //     distribution to `fuaran-ui` and the import package to `fuaran_ui`, and
  //     `fuaran-py` stops at 0.5.0, which does not carry the rename and never
  //     will. Its five transform fixtures pass now too, and the Python
  //     capability-manifest falsifier — which fired alongside them, because
  //     five failures the manifest did not predict read as projector lag —
  //     agrees again.
  //
  // Neither was an npm pin and neither was fixable from this file. What is
  // worth keeping from all of that is the shape: this projector was correct
  // before any of the three hosts could prove it, and the arms went green by
  // pins moving, not by this file changing.
  const legacyInput = readFileSync(
    resolve(corpusDir, 'lenient/lenient-transform-column-member-legacy.json'),
    'utf8',
  ).trim();

  const reEncodeThroughPinnedHost = (wire: string): string => {
    const decoded = (ops as { decodeNode: (s: string) => unknown }).decodeNode(wire);
    return (ops as { encodeNode: (n: unknown) => string }).encodeNode(
      (decoded as { value?: unknown }).value ?? decoded,
    );
  };

  it('still DECODES the legacy member names — the aliases did not go away', () => {
    const decoded = (ops as { decodeNode: (s: string) => { ok?: boolean } }).decodeNode(
      legacyInput,
    );
    expect(
      decoded.ok,
      'the pinned @fuaran-ui/ops rejects the legacy spellings — a tree saved or permalinked before the rename no longer opens',
    ).toBe(true);
  });

  it('ENCODES the canonical member names', () => {
    const reEncoded = reEncodeThroughPinnedHost(legacyInput);
    // Asserted as MEMBER tokens — with the colon — and not as bare substrings.
    // `"col"` is also the `$type` of a column-reference expression
    // (`{"$type":"col","name":"amount"}`), which this fixture carries and which
    // the rename never touched; a bare `not.toContain('"col"')` fails on it and
    // would be testing the wrong thing. `"columns":` alone is likewise satisfied
    // by a `DataGrid`'s untouched member, so the negatives carry the weight.
    expect(reEncoded).toContain('"columns":');
    expect(reEncoded).toContain('"column":');
    expect(reEncoded).not.toContain('"cols":');
    expect(reEncoded).not.toContain('"col":');
  });
});
