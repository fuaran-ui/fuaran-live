// Publish the conformance-gate report the showcase's status panel renders.
//
// Runs the projection-conformance gate (tests/projection-conformance/, the same
// suite `pnpm conformance` runs) through vitest's JSON reporter, then distils the
// counts into public/conformance/report.generated.json. `vite build` copies
// public/ into the artifact, so the published site serves the report beside the
// pages that read it (app/showcase/Conformance.fs).
//
// The load-bearing rule, inherited from the panel: NEVER write a report we cannot
// substantiate. If the harness produces no machine-readable result the script
// writes nothing and fails — the panel then finds no file, goes grey, and says
// so. A fabricated green is the one outcome worse than no report at all.
//
// Exit status mirrors the gate, so a deploy step running this halts on a failing
// gate rather than shipping a red panel. The report is still written first, so
// the failure is inspectable in the workflow artefacts.
//
// Requires `pnpm run fable:app` first (the harness executes the Fable-compiled
// projector from app/output/).

import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '..');

const vitestBin = resolve(repoRoot, 'node_modules/vitest/vitest.mjs');
const rawPath = resolve(repoRoot, 'node_modules/.cache/conformance-result.json');
const reportPath = resolve(repoRoot, 'public/conformance/report.generated.json');

if (!existsSync(vitestBin)) {
  console.error('[conformance-report] vitest not found at ' + vitestBin + ' — run `pnpm install`.');
  process.exit(1);
}

/** The deployed commit: CI supplies it; locally fall back to the working HEAD. */
const resolveCommit = () => {
  const fromEnv = process.env.GITHUB_SHA ?? '';
  if (fromEnv !== '') return fromEnv;
  const r = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: repoRoot, encoding: 'utf8' });
  return r.status === 0 ? r.stdout.trim() : '';
};

mkdirSync(dirname(rawPath), { recursive: true });

// The JSON reporter writes a jest-shaped summary to --outputFile; the human
// output still streams to the console for the workflow log.
const run = spawnSync(
  process.execPath,
  [
    vitestBin,
    'run',
    '-c',
    'vitest.conformance.config.ts',
    '--reporter=default',
    '--reporter=json',
    `--outputFile.json=${rawPath}`,
  ],
  { cwd: repoRoot, stdio: 'inherit' },
);

if (!existsSync(rawPath)) {
  console.error(
    '[conformance-report] the harness produced no result file — publishing NOTHING rather than an ' +
      'unsubstantiated report. The panel will show its honest grey state.',
  );
  process.exit(run.status === 0 ? 1 : (run.status ?? 1));
}

const result = JSON.parse(readFileSync(rawPath, 'utf8'));

const passed = result.numPassedTests ?? 0;
const failed = result.numFailedTests ?? 0;
const total = result.numTotalTests ?? 0;

// `ok` is deliberately conservative: green needs vitest's own verdict AND a zero
// failure count AND a non-empty corpus. A harness that ran nothing is not a pass.
const ok = result.success === true && failed === 0 && total > 0;

const report = {
  generated: new Date().toISOString().replace(/\.\d{3}Z$/, 'Z'),
  commit: resolveCommit(),
  passed,
  failed,
  total,
  ok,
};

mkdirSync(dirname(reportPath), { recursive: true });
writeFileSync(reportPath, JSON.stringify(report, null, 2) + '\n', 'utf8');

console.log(
  '[conformance-report] wrote public/conformance/report.generated.json — ' +
    passed +
    '/' +
    total +
    ' passed, ' +
    failed +
    ' failed, ok=' +
    ok,
);

process.exit(run.status ?? 1);
