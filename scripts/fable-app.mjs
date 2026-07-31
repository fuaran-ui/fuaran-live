// Build the entirely-F#/Fable app (`app/`) and tolerate the known, benign F# 222
// app-shape diagnostic.
//
// `app/` LINKS the Fable-safe slice of `Fuaran.UI.Ops` (Apply / JsonDecode /
// CanonicalJson) — a ProjectReference would also pull the .NET-only ErrorRender.fs
// and break the Fable compile, so linking is the documented pattern (the parity
// fable-host does the same). With linked sources in an application that has a
// top-level `Program.run`, FCS emits a spurious error 222 ("only the last source
// file may omit a module declaration") and `dotnet fable` exits non-zero — even
// though it transpiles every file correctly. The fable-host simply tolerates it.
//
// The gate is therefore TWO checks, both required:
//  1. Every other compiler ERROR fails the build. File existence is NOT enough:
//     Fable re-emits modules containing `throw` placeholders for expressions it
//     could not compile, so a page can "emit completely" and still be dead at
//     runtime (discovered 2026-07-23 — an overload error shipped a blank
//     showcase with this script reporting success).
//  2. The required JS artefacts exist (a compile that prevents emission
//     entirely still fails even if its error text slipped past check 1).

import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';

// The one tolerated diagnostic: the F# 222 app-shape artefact of source linking.
const TOLERATED = /\(code 222\)|error FS0?222|only the last source file/i;

function runFable(args) {
  const r = spawnSync('dotnet', ['fable', ...args], { encoding: 'utf8' });
  const out = (r.stdout ?? '') + (r.stderr ?? '');
  process.stdout.write(out);
  // A Fable CRASH (e.g. the project cracker dying on a NuGet restore failure)
  // prints an exception, not `error FSHARP` lines — and the artefact-existence
  // check below cannot catch it either, because the PREVIOUS build's JS is
  // still on disk. Discovered 2026-07-31: an NU1605 package downgrade crashed
  // the cracker and this script reported success over stale output.
  if (/Unhandled exception/i.test(out)) {
    return ['Fable crashed (unhandled exception) — see the output above for the cause'];
  }
  return out
    .split(/\r?\n/)
    .filter((l) => /\berror\b/i.test(l) && /error (FSHARP|FABLE|FS\d+)/i.test(l))
    .filter((l) => !TOLERATED.test(l));
}

const errors = [
  ...runFable(['app', '--outDir', 'app/output']),
  // The showcase is its own Fable project (app/showcase/Showcase.fsproj) — the
  // two entries ship as separate artifacts, and the showcase must contain no
  // provider/key machinery, so the compiles stay separate too.
  ...runFable(['app/showcase', '--outDir', 'app/showcase/output']),
];

if (errors.length > 0) {
  console.error(
    '[fable-app] compile FAILED — ' +
      errors.length +
      ' error(s) beyond the tolerated F# 222 diagnostic:\n  ' +
      errors.join('\n  '),
  );
  process.exit(1);
}

const required = [
  'app/output/App.js',
  'app/output/Session.js',
  'app/output/fuaran-dotnet/src/Fuaran.UI.Ops/Apply.js',
  'app/output/fuaran-dotnet/src/Fuaran.UI.Ops/JsonDecode.js',
  'app/output/fuaran-dotnet/src/Fuaran.UI.OpStream.Abstractions/CanonicalJson.js',
  'app/showcase/output/App.js',
  'app/showcase/output/Receiver.js',
];

const missing = required.filter((p) => !existsSync(p));
if (missing.length > 0) {
  console.error('[fable-app] emission incomplete — missing:\n  ' + missing.join('\n  '));
  process.exit(1);
}
console.log('[fable-app] Fable emission complete (F# 222 app-shape diagnostic tolerated).');
process.exit(0);
