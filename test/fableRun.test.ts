import { describe, expect, it } from 'vitest';
import { failuresOf, parseDiagnostics } from '../scripts/fable-run.mjs';

// The classes the old pattern list dropped, each as the compiler prints it.
const typeError =
  'C:\\p\\Page.fs(12,5): (12,10) error FSHARP: This expression was expected to have type int (code 1)';
const parseError =
  'C:\\p\\Page.fs(20,1): (20,4) error FSHARP: Unexpected symbol in expression (code 10)';
const missingRef =
  'C:\\p\\Page.fs(3,6): (3,14) error FSHARP: The namespace Nope is not defined (code 39)';
const msbuild = 'C:\\p\\Page.fsproj : error NU1605: Detected package downgrade';
const warning = 'C:\\p\\Page.fs(1,1): (1,2) warning FSHARP: unused (code 1182)';

describe('fable-run diagnostics', () => {
  it('reports every error-severity diagnostic in order, whatever its class', () => {
    const out = ['Fable 5', typeError, warning, parseError, missingRef, msbuild].join('\n');
    expect(parseDiagnostics(out)).toEqual([typeError, parseError, missingRef, msbuild]);
  });

  it('promotes an incomplete match and nothing else at warning severity', () => {
    const incomplete = 'X.fs(1,1): (1,2) warning FSHARP: Incomplete matches (code 25)';
    expect(parseDiagnostics([incomplete, warning].join('\n'))).toEqual([incomplete]);
  });

  it('prints the raw output when the compiler fails and no diagnostic parsed', () => {
    const [msg] = failuresOf('something unexpected happened', 1);
    expect(msg).toContain('exited 1');
    expect(msg).toContain('something unexpected happened');
  });

  it('is clean on a clean compile', () => {
    expect(failuresOf('Compilation finished', 0)).toEqual([]);
  });

  it('keeps the parsed errors beside a crash report', () => {
    const f = failuresOf(typeError + '\nUnhandled exception: boom', 1);
    expect(f[0]).toBe(typeError);
    expect(f[1]).toContain('crashed');
  });
});
