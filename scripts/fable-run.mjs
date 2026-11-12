// Run `dotnet fable` and return the diagnostics that must fail the build: EVERY
// compiler diagnostic at error severity, in the order the compiler printed them,
// plus an incomplete match (FS0025) and a failed run that named nothing.
// Fable does not honour WarningsAsErrors, so the one warning promoted here is the
// incomplete match: it compiles to a runtime MatchFailure the user meets.
// As a script: `node scripts/fable-run.mjs <fable args>` runs one compile and
// exits non-zero on any such diagnostic (the parity render host uses it).
//
// What counts as an error is the compiler's own SEVERITY, read off the
// diagnostic grammar (`<location>: error <CODE>: <message>`), never a list of
// codes or message patterns: a list is exactly as complete as its last
// maintenance, and the classes it dropped (parse errors, missing references)
// reached the user as an "emission incomplete" report with no cause attached.

import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

// `<anything> error|warning <CODE>:` - msbuild, FSharp and Fable all print it.
const SEVERITY = /\b(error|warning)\s+[A-Za-z][\w.-]*\s*:/i;
// An incomplete match compiles to a runtime MatchFailure.
const INCOMPLETE_MATCH = /warning FSHARP:.*\(code 25\)/;

/** Every error-severity diagnostic line in the compiler output, in order. */
export function parseDiagnostics(out) {
  return out.split(/\r?\n/).filter((line) => {
    const m = SEVERITY.exec(line);
    return (m !== null && m[1].toLowerCase() === 'error') || INCOMPLETE_MATCH.test(line);
  });
}

/**
 * The failures a finished compile reports. A non-zero exit that parsed no
 * diagnostic prints the raw output, so a failure is never reported as nothing.
 */
export function failuresOf(out, status) {
  const diagnostics = parseDiagnostics(out);
  // A Fable CRASH (e.g. the project cracker dying on a NuGet restore failure)
  // prints an exception, not diagnostics, and the artefact-existence check
  // cannot catch it either, because the PREVIOUS build's JS is still on disk
  // (discovered 2026-07-31: an NU1605 package downgrade crashed the cracker and
  // the build reported success over stale output).
  if (/Unhandled exception/i.test(out)) {
    return [
      ...diagnostics,
      'Fable crashed (unhandled exception) - see the output above for the cause',
    ];
  }
  if (status !== 0 && diagnostics.length === 0) {
    return [
      `dotnet fable exited ${status} with no parsed diagnostic; raw output:\n${out.trimEnd()}`,
    ];
  }
  return diagnostics;
}

// Fable emits JS even when the compile FAILED, and a re-run then prints
// "Skipped compilation because all generated files are up-to-date" and exits 0:
// the failure reads green the second time (measured 2026-11-12 on a fixture with
// type, parse and missing-reference errors). So a failed compile leaves a marker
// beside its output, and a run that finds one recompiles everything (`--noCache`)
// and reports the errors again, until a clean compile removes it.
const FAILED_MARKER = '.fable-run-failed';

function outDirOf(args) {
  const i = args.indexOf('--outDir');
  return i >= 0 ? args[i + 1] : undefined;
}

export function runFable(args) {
  const outDir = outDirOf(args);
  const marker = outDir === undefined ? undefined : join(outDir, FAILED_MARKER);
  const retry = marker !== undefined && existsSync(marker) && !args.includes('--noCache');
  const r = spawnSync('dotnet', ['fable', ...args, ...(retry ? ['--noCache'] : [])], {
    encoding: 'utf8',
  });
  if (r.error) return [`could not run dotnet fable: ${r.error.message}`];
  const out = (r.stdout ?? '') + (r.stderr ?? '');
  process.stdout.write(out);
  const failures = failuresOf(out, r.status ?? 1);
  if (marker !== undefined) {
    if (failures.length > 0) {
      mkdirSync(outDir, { recursive: true });
      writeFileSync(marker, 'the last compile failed; recompile in full\n');
    } else {
      rmSync(marker, { force: true });
    }
  }
  return failures;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const errors = runFable(process.argv.slice(2));
  if (errors.length > 0) {
    console.error(`[fable-run] FAILED - ${errors.length} error(s):\n  ` + errors.join('\n  '));
    process.exit(1);
  }
  process.exit(0);
}
