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
import { existsSync, mkdirSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// `nuget.config` declares a folder package source beside the released feed so a
// local pack can shadow a published package during development. NuGet hard-errors
// (NU1301, "the local source doesn't exist") on a folder source that is absent —
// which is every fresh clone outside a workspace that has packed something. CI
// mints the folder empty for the same reason; do it here too, so `pnpm build`
// works from a bare `git clone`. Empty, the folder changes nothing: every
// package restores from nuget.org exactly as before.
function ensureLocalFeedFolder() {
  const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
  const config = readFileSync(resolve(repoRoot, 'nuget.config'), 'utf8');
  const m = /<add\s+key="local"\s+value="([^"]+)"/.exec(config);
  if (!m) return;
  const feed = resolve(repoRoot, m[1]);
  if (!existsSync(feed)) mkdirSync(feed, { recursive: true });
}
ensureLocalFeedFolder();

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
  // The playground consumes the language tier as PACKAGES, so Fable extracts and
  // emits it under fable_modules/<PackageId>.<Version>/. Raising the Fuaran.UI.*
  // pin in app/FuaranLive.fsproj means raising the version here and in
  // test/tierOutput.ts — the only two places the path is spelled out.
  'app/output/fable_modules/Fuaran.UI.Ops.0.35.0/Apply.fs.js',
  'app/output/fable_modules/Fuaran.UI.Ops.0.35.0/JsonDecode.fs.js',
  'app/output/fable_modules/Fuaran.UI.OpStream.Abstractions.0.35.0/CanonicalJson.fs.js',
  // The bounded program loop — run mode's engine. Its absence means the loop was
  // not reached by the compile, which a green Fable run would otherwise hide.
  'app/output/fable_modules/Fuaran.Program.Runtime.0.1.0/Program.fs.js',
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
