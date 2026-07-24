/// <reference types="vite/client" />

// Phase 85 – dual-host feature flag. With `VITE_DUAL_HOST=1` the preview pane
// renders the operator's tree through BOTH the TS and F# (Fable) hosts in
// sibling iframes (behavioural parity). Unset/absent → the MVP single (TS,
// in-process) render path, which needs no Fable toolchain in the build.
interface ImportMetaEnv {
  readonly VITE_DUAL_HOST?: string;
}

// The F# host bundle is produced by `dotnet fable fable-host` and is gitignored
// (generated). The committed source is fable-host/Host.fs. Declare the module so
// `tsc --noEmit` over fableHostEntry.ts succeeds before Fable has run.
declare module '*/Host.js';
