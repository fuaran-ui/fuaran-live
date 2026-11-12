import { describe, expect, it } from 'vitest';
import { exitCodeFor, formatArms, tallyArms } from '../scripts/arm-report.mjs';

const report = {
  testResults: [
    {
      name: '/r/gates/projection-conformance/fsharp.test.ts',
      assertionResults: [
        { title: 'a', status: 'passed' },
        { title: 'ahead', status: 'skipped' },
      ],
    },
    {
      name: '/r/gates/projection-conformance/python.test.ts',
      assertionResults: [{ title: 'NOT RUN: no interpreter', status: 'skipped' }],
    },
  ],
};

describe('arm report', () => {
  const arms = tallyArms(report);

  it('names each arm as ran or skipped with its reason, and a total', () => {
    expect(arms.map((a) => [a.arm, a.ran])).toEqual([
      ['fsharp', true],
      ['python', false],
    ]);
    const lines = formatArms(arms);
    expect(lines[0]).toContain('RAN      fsharp: 1 passed, 0 failed, 1 skipped');
    expect(lines[1]).toContain('SKIPPED  python: no interpreter');
    expect(lines[2]).toContain('ran 1, skipped 1 of 2 arms');
  });

  it('a skipped arm fails under strict mode and is only reported when lax', () => {
    expect(exitCodeFor(0, arms, true)).toBe(1);
    expect(exitCodeFor(0, arms, false)).toBe(0);
  });

  it('the suite failure always wins', () => {
    expect(exitCodeFor(3, arms, false)).toBe(3);
  });
});
