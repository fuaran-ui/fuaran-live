// Run the projection-conformance suite and account for every ARM of it.
//
// A conformance arm that cannot run on this machine (the Python arm with no
// interpreter carrying the pinned host) registers a skipped `NOT RUN: <reason>`
// test, and a suite with a skipped arm exits 0. That read as a green gate: the
// arm measured nothing and nothing said so. This wrapper prints, per arm, RAN or
// SKIPPED with the reason, then a total, and exits non-zero on a skipped arm when
// strict mode is set (`--require-all-arms`, or FUARAN_REQUIRE_ARMS=1, which CI
// sets). The exit code is chosen explicitly per outcome, never read from
// whichever process ran last.
//
// Usage: node scripts/arm-report.mjs [--require-all-arms]
// Requires `pnpm run fable:app` first, as the suite itself does.

import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync } from 'node:fs';
import { basename, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// The arm registers this title even when it runs (its describe is then skipped, with an
// EMPTY reason), so only a title that carries a reason is a real NOT RUN.
const NOT_RUN = /^NOT RUN:\s*(?=\S)/;

/** The arm a conformance test file belongs to (`python.test.ts` -> `python`). */
export function armOf(file) {
  return basename(file).replace(/\.test\.[cm]?[jt]s$/, '');
}

/**
 * Per-arm tally from vitest's JSON report. An arm is SKIPPED when it registered
 * a `NOT RUN:` test (carrying its reason) and RAN otherwise. Skipped tests that
 * are not `NOT RUN` (the ahead-of-host fixtures) are counted but do not make
 * the arm skipped.
 */
export function tallyArms(report) {
  return (report.testResults ?? []).map((file) => {
    const tests = file.assertionResults ?? [];
    const notRun = tests
      .filter((t) => NOT_RUN.test(t.title ?? ''))
      .map((t) => t.title.replace(NOT_RUN, ''));
    const failedTests = tests.filter((t) => t.status === 'failed').length;
    const passed = tests.filter((t) => t.status === 'passed').length;
    // A file that failed to load has no tests to count, and reads as nothing ran.
    const failedToLoad = file.status === 'failed' && failedTests === 0;
    if (passed === 0 && failedTests === 0 && notRun.length === 0 && !failedToLoad) {
      notRun.push('no test ran');
    }
    return {
      arm: armOf(file.name),
      passed,
      failed: failedTests + (failedToLoad ? 1 : 0),
      skipped: tests.filter((t) => t.status === 'skipped' || t.status === 'pending').length,
      notRun,
      ran: notRun.length === 0,
    };
  });
}

/** The printed lines: one per arm, then the total. */
export function formatArms(arms) {
  const lines = arms.map((a) =>
    a.ran
      ? `  RAN      ${a.arm}: ${a.passed} passed, ${a.failed} failed, ${a.skipped} skipped`
      : `  SKIPPED  ${a.arm}: ${a.notRun.join('; ')}`,
  );
  const ran = arms.filter((a) => a.ran).length;
  lines.push(`  total: ran ${ran}, skipped ${arms.length - ran} of ${arms.length} arms`);
  return lines;
}

/** Exit code for a finished run: vitest's own failure wins; else a skipped arm fails under strict. */
export function exitCodeFor(vitestStatus, arms, strict) {
  if (vitestStatus !== 0) return vitestStatus;
  if (strict && arms.some((a) => !a.ran)) return 1;
  return 0;
}

function main() {
  const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
  const strict =
    process.argv.includes('--require-all-arms') || process.env.FUARAN_REQUIRE_ARMS === '1';
  const vitestBin = resolve(repoRoot, 'node_modules/vitest/vitest.mjs');
  if (!existsSync(vitestBin)) {
    console.error('[arm-report] vitest not found at ' + vitestBin + ' - run `pnpm install`.');
    process.exit(1);
  }
  const rawPath = resolve(repoRoot, 'node_modules/.cache/arm-report.json');
  mkdirSync(dirname(rawPath), { recursive: true });
  const r = spawnSync(
    process.execPath,
    [
      vitestBin,
      'run',
      '-c',
      'vitest.conformance.config.ts',
      '--reporter=default',
      '--reporter=json',
      '--outputFile.json=' + rawPath,
    ],
    { cwd: repoRoot, stdio: 'inherit' },
  );
  let report;
  try {
    report = JSON.parse(readFileSync(rawPath, 'utf8'));
  } catch {
    console.error('[arm-report] the suite produced no machine-readable result; failing.');
    process.exit(r.status || 1);
  }
  const arms = tallyArms(report);
  console.log('\n[arm-report] conformance arms:\n' + formatArms(arms).join('\n'));
  const status = r.status ?? 1;
  const code = exitCodeFor(status, arms, strict);
  if (code !== 0 && status === 0) {
    console.error(
      '[arm-report] FAILED: an arm was SKIPPED and strict mode is on (FUARAN_REQUIRE_ARMS=1 / --require-all-arms). ' +
        'A skipped arm is unverified, not green. See CONTRIBUTING.md "Running every arm locally".',
    );
  }
  process.exit(code);
}

if (process.argv[1] === fileURLToPath(import.meta.url)) main();
