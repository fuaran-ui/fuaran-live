# Rosetta host artefacts – provenance

The `/demo/rosetta` page runs five independent canonical encoders live. Three
are JavaScript-side (F# via Fable, the TypeScript encoder, the Python encoder in
Pyodide); the other two are committed WebAssembly artefacts in this folder,
regenerable from their sibling reference implementations. These are **replay
artefacts** – build outputs checked in for a keyless, serverless page, never
hand-authored. Regenerate them from source with the commands below and commit the
result; the CI parity lock (`test/rosettaParity.test.ts`) loads both and asserts
they emit the pinned reference bytes for the default holes, so a stale or
hand-edited artefact fails the build.

## `rosetta-rs.wasm` – the Rust host (Tier 1, eager)

The `fuaran-rs` certified reference core compiled to `wasm32-unknown-unknown`. Its
additive `fuaran_rosetta_encode` export receives the six scalar holes as a JSON
object, builds the exemplar tree with the crate's own typed model, and runs the
corpus-certified canonical encoder over it. Dependency-free (no `wasm-bindgen`);
the page marshals UTF-8 across linear memory through the module's
`fuaran_alloc` / `fuaran_dealloc` + packed `(ptr<<32 | len)` return ABI.

Regenerate (from the `fuaran-rs` repo root):

```
cargo build --target wasm32-unknown-unknown --release
cp target/wasm32-unknown-unknown/release/fuaran_rs.wasm \
   ../fuaran-live/public/rosetta/rosetta-rs.wasm
```

## `rosetta-go.wasm` – the Go host (Tier 1, lazy behind a click)

`fuaran-go`'s stdlib-only codec compiled `GOOS=js GOARCH=wasm`. The
`cmd/rosetta-wasm` entry registers `fuaranGoRosettaEncode(holesJSON)`, which
builds the exemplar tree from the six holes and runs `wire.EncodeNode`. Several
MB (the Go runtime is bundled), so – like the Pyodide host – it loads only when
the visitor clicks "Run the Go host".

Regenerate (from the `fuaran-go` repo root):

```
GOOS=js GOARCH=wasm go build -o ../fuaran-live/public/rosetta/rosetta-go.wasm ./cmd/rosetta-wasm
```

## `wasm_exec.js` – the Go WebAssembly loader glue

The unmodified `wasm_exec.js` that ships with the Go toolchain, required to
instantiate any `js/wasm` module. It defines `globalThis.Go`. It is a verbatim
copy, licensed under the Go project's BSD-3-Clause licence (see the header inside
the file); it is not authored here.

Regenerate:

```
cp "$(go env GOROOT)/lib/wasm/wasm_exec.js" ../fuaran-live/public/rosetta/wasm_exec.js
```

> Pin note: keep `wasm_exec.js` in step with the Go toolchain used to build
> `rosetta-go.wasm` – the loader and the module share an ABI that changes across
> major Go releases. Both were regenerated with Go 1.26.
