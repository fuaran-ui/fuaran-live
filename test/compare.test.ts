// Phase 326 – comparison mode's Fuaran-arm validity check, over the Fable output.
// The typed arm is verifiably valid or rejected by the app's own decode/apply loop.

import { describe, it, expect } from 'vitest';
import {
  fuaranValidates,
  // @ts-expect-error untyped Fable output (no .d.ts for app/output/testing/CompareProbes.js)
} from '../app/output/testing/CompareProbes.js';

const metricNode =
  '{"id":"metric-1","kind":{"$type":"Metric","format":{"$type":"Currency","code":"GBP"},"label":"Revenue","tone":"Brand","value":{"$type":"Static","value":1234.5}}}';

describe('comparison – the typed arm is verifiably valid', () => {
  it('accepts a valid Fuaran emission', () => {
    expect(fuaranValidates('```json\n' + metricNode + '\n```')).toBe(true);
  });
  it('rejects a freeform / non-decodable emission', () => {
    expect(fuaranValidates('Here is some HTML: <div>hi</div>')).toBe(false);
  });
});
