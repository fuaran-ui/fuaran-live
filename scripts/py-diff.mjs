// Local diagnostic for the Python projection arm — NOT part of the gate.
// Prints, per failing fixture, the first differing character window (or the
// executor error), so a projector fix can be aimed rather than guessed.
//
//   node scripts/py-diff.mjs [idSubstring]
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const repoRoot = resolve(import.meta.dirname, '..');
const corpusDir = resolve(repoRoot, '../wire-format-fixtures');
const { projectPythonExpr } = await import('../app/output/Projection.js');

const manifest = JSON.parse(readFileSync(resolve(corpusDir, 'manifest.json'), 'utf8'));
const fixtures = manifest.fixtures.filter((f) => f.kind === 'node-round-trip');
const filter = process.argv[2];

const py = existsSync(resolve(repoRoot, '.venv/Scripts/python.exe'))
  ? resolve(repoRoot, '.venv/Scripts/python.exe')
  : 'python3';

const wires = new Map(
  fixtures.map((f) => [f.id, readFileSync(resolve(corpusDir, f.inputFile), 'utf8').trim()]),
);
const cases = fixtures.map((f) => ({ id: f.id, expr: projectPythonExpr(wires.get(f.id)) }));

const proc = spawnSync(py, [resolve(repoRoot, 'tests/projection-conformance/python_exec.py')], {
  input: JSON.stringify({ cases }),
  encoding: 'utf8',
  maxBuffer: 64 * 1024 * 1024,
});
const payload = JSON.parse(proc.stdout);
if (payload.fatal) {
  console.log(payload.fatal);
  process.exit(1);
}

let pass = 0;
const errs = new Map();
for (const r of payload.results) {
  const wire = wires.get(r.id);
  if (r.ok && r.encoded === wire) {
    pass++;
    continue;
  }
  if (filter && !r.id.includes(filter)) continue;
  if (!r.ok) {
    errs.set(r.error.split(':').slice(0, 2).join(':'), (errs.get(r.id) ?? 0) + 1);
    console.log(`\n== ${r.id}  EXEC ${r.error}`);
    if (filter) console.log(cases.find((c) => c.id === r.id).expr.slice(0, 1200));
    continue;
  }
  let i = 0;
  while (i < wire.length && wire[i] === r.encoded[i]) i++;
  console.log(`\n== ${r.id}  @${i}`);
  console.log(`  want ${JSON.stringify(wire.slice(Math.max(0, i - 40), i + 90))}`);
  console.log(`  got  ${JSON.stringify(r.encoded.slice(Math.max(0, i - 40), i + 90))}`);
  if (filter) console.log(cases.find((c) => c.id === r.id).expr.slice(0, 1500));
}
console.log(`\n${pass}/${fixtures.length} byte-identical`);
