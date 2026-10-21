// Run `dotnet fable` and return the diagnostics that must fail the build: every
// compiler error, a Fable crash, and an incomplete match (FS0025). Fable does not
// honour WarningsAsErrors, so this is where that promotion is enforced.
// As a script: `node scripts/fable-run.mjs <fable args>` runs one compile and
// exits non-zero on any such diagnostic (the parity render host uses it).

import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

export function runFable(args) {
  const r = spawnSync('dotnet', ['fable', ...args], { encoding: 'utf8' });
  const out = (r.stdout ?? '') + (r.stderr ?? '');
  process.stdout.write(out);
  // A Fable CRASH (e.g. the project cracker dying on a NuGet restore failure)
  // prints an exception, not `error FSHARP` lines, and the artefact-existence
  // check cannot catch it either, because the PREVIOUS build's JS is still on
  // disk. Discovered 2026-07-31: an NU1605 package downgrade crashed the
  // cracker and the build reported success over stale output.
  if (/Unhandled exception/i.test(out)) {
    return ['Fable crashed (unhandled exception) - see the output above for the cause'];
  }
  return out.split(/\r?\n/).filter(
    (l) =>
      (/\berror\b/i.test(l) && /error (FSHARP|FABLE|FS\d+)/i.test(l)) ||
      // An incomplete match compiles to a runtime MatchFailure the user meets.
      /warning FSHARP:.*\(code 25\)/.test(l),
  );
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const errors = runFable(process.argv.slice(2));
  if (errors.length > 0) {
    console.error('[fable-run] FAILED:\n  ' + errors.join('\n  '));
    process.exit(1);
  }
}
