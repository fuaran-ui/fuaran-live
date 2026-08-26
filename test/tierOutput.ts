// The ONE place the test suite names a path inside the packaged language tier.
//
// The playground project (`app/FuaranLive.fsproj`) consumes `Fuaran.UI.*` as
// published packages rather than as a sibling checkout, so Fable extracts the
// tier's sources into `app/output/fable_modules/<PackageId>.<Version>/` and emits
// them as `<File>.fs.js` — a path that carries the pinned version. Four tests
// need a handful of tier functions directly; without this module each of them
// would spell that version out, and raising the pin would mean editing five
// files and discovering the misses one failing suite at a time.
//
// Raising the `Fuaran.UI.*` pin in `app/FuaranLive.fsproj` therefore means
// editing the version in this file, and only this file.
//
// Requires `pnpm run fable:app` to have produced `app/output/`.

// @ts-expect-error untyped Fable output (no .d.ts is generated for it)
export { findNode } from '../app/output/fable_modules/Fuaran.UI.Ops.0.35.0/Introspect.fs.js';
// @ts-expect-error untyped Fable output
export { decodeNode } from '../app/output/fable_modules/Fuaran.UI.Ops.0.35.0/JsonDecode.fs.js';
// @ts-expect-error untyped Fable output
export { NodeId } from '../app/output/fable_modules/Fuaran.UI.0.35.0/Types.fs.js';
// @ts-expect-error untyped Fable output
export { toCssVariables } from '../app/output/fable_modules/Fuaran.UI.Renderer.Core.0.35.0/Theme.fs.js';
