import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterAll, describe, expect, it } from 'vitest';

// Go-red: a page with a missing reference, a type error and a parse error is
// really compiled by `dotnet fable`, and the script must carry every diagnostic
// with its file, line and message, and exit non-zero - the first time AND on a
// re-run (Fable emits JS from a failed compile, and a plain re-run then skips
// compilation and exits 0).
const repoRoot = join(__dirname, '..');
const outDir = mkdtempSync(join(tmpdir(), 'fable-red-'));
afterAll(() => rmSync(outDir, { recursive: true, force: true }));

const compile = () =>
  spawnSync(
    process.execPath,
    ['scripts/fable-run.mjs', 'test/fixtures/fable-red', '--outDir', outDir],
    { cwd: repoRoot, encoding: 'utf8' },
  );

describe('a failed Fable compile reports every compiler error', () => {
  for (const attempt of ['first run', 're-run over the failed run']) {
    it(`${attempt}: exits non-zero with each diagnostic`, () => {
      const r = compile();
      expect(r.status).toBe(1);
      const report = r.stderr;
      expect(report).toMatch(
        /Types\.fs\(3,6\).*error FSHARP: The namespace or module 'DoesNotExist'/,
      );
      expect(report).toMatch(/Types\.fs\(5,22\).*error FSHARP: This expression was expected/);
      expect(report).toMatch(/Parse\.fs\(3,\d+\).*error FSHARP: Incomplete conditional/);
      expect(report).toMatch(/Parse\.fs\(3,21\).*error FSHARP: Unexpected keyword 'then'/);
      expect(report).toMatch(/FAILED - 7 error\(s\)/);
    }, 240_000);
  }
});
