// Dual-host wire-parity demo inputs – real wire-format-fixtures corpus files.
//
// The in-app parity pane (app/App.fs) drives both render hosts with these
// wires so the demonstration runs on the authoritative conformance corpus, not
// hand-authored copies. `?raw` inlines the exact file bytes at build time from
// the sibling `wire-format-fixtures/` checkout (the same workspace-checkout
// posture the @fuaran-ui/* link bridge and the fable-host ProjectReference
// use); `.trim()` mirrors the Playwright parity gate's read – fixture files end
// with a newline, the canonical encoding does not.
//
// Two entry shapes:
// - canonical `nodes/` round-trip fixtures: the file IS the canonical bytes, so
//   the wire posted to the hosts and the expected re-encoding coincide.
// - WIRE_FORMAT.md §16 `lenient-accept` pairs: the wire is the SHORTHAND form
//   (e.g. a bare JSON string where a TextSource object is expected) and
//   `expected` is the verbose canonical form a conformant host MUST normalise
//   it to. Posting the shorthand shows both hosts normalising identically.

import compositeRootRaw from '../../../wire-format-fixtures/nodes/composite-root.json?raw';
import cardRaw from '../../../wire-format-fixtures/nodes/card-1.json?raw';
import formRangedRaw from '../../../wire-format-fixtures/nodes/form-ranged.json?raw';
import tableRaw from '../../../wire-format-fixtures/nodes/table-1.json?raw';
import lenientButtonRaw from '../../../wire-format-fixtures/lenient/lenient-bare-text-button-label.json?raw';
import lenientButtonExpectedRaw from '../../../wire-format-fixtures/lenient/lenient-bare-text-button-label.expected.json?raw';
import lenientCalloutRaw from '../../../wire-format-fixtures/lenient/lenient-bare-text-callout.json?raw';
import lenientCalloutExpectedRaw from '../../../wire-format-fixtures/lenient/lenient-bare-text-callout.expected.json?raw';

export interface ParityFixture {
  /** The corpus fixture id (manifest.json `id` / `nodes/<id>.json`). */
  readonly id: string;
  /** Picker label. */
  readonly label: string;
  /** True for a WIRE_FORMAT.md §16 lenient-accept shorthand input. */
  readonly lenient: boolean;
  /** The wire posted to both hosts. */
  readonly wire: string;
  /** The canonical bytes both hosts' re-encodings must equal. */
  readonly expected: string;
  /** One-line description shown under the picker. */
  readonly description: string;
}

const canonical = (id: string, label: string, raw: string, description: string): ParityFixture => ({
  id,
  label,
  lenient: false,
  wire: raw.trim(),
  expected: raw.trim(),
  description,
});

const lenient = (
  id: string,
  label: string,
  raw: string,
  expectedRaw: string,
  description: string,
): ParityFixture => ({
  id,
  label,
  lenient: true,
  wire: raw.trim(),
  expected: expectedRaw.trim(),
  description,
});

/** The pane's demo inputs, in picker order. */
export const parityFixtures: readonly ParityFixture[] = [
  canonical(
    'composite-root',
    'Corpus: composite page',
    compositeRootRaw,
    'The page/section composite from the canonical primitive matrix.',
  ),
  canonical(
    'card-1',
    'Corpus: card + metric',
    cardRaw,
    'A card containing a formatted currency metric.',
  ),
  canonical(
    'form-ranged',
    'Corpus: numeric-range form',
    formRangedRaw,
    'The numeric-range form primitive.',
  ),
  canonical('table-1', 'Corpus: table', tableRaw, 'The table primitive with typed columns.'),
  lenient(
    'lenient-bare-text-button-label',
    'Corpus §16: bare-string button label',
    lenientButtonRaw,
    lenientButtonExpectedRaw,
    'Button.label as a bare JSON string (§16.1) – both hosts must normalise the shorthand to the same verbose canonical form.',
  ),
  lenient(
    'lenient-bare-text-callout',
    'Corpus §16: bare-string callout',
    lenientCalloutRaw,
    lenientCalloutExpectedRaw,
    'Callout.body and Callout.heading as bare strings (§16.1) – both hosts must normalise identically.',
  ),
];
