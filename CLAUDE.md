# CLAUDE.md — fuaran-live (the Fuaran site: showcase + playground)

This repo is the **serverless, client-only Fuaran site** — one codebase, two static entries deployed as two origins:

- **The playground** (`index.html`) — prompt an LLM with your own key, watch it emit canonical wire-format JSON, and see the `Fuaran.UI` tree render live. No account, no server, no backend.
- **The showcase** (`showcase.html` + the bare teleport receiver `receiver.html`) — the zero-key-egress half: small, self-contained pages, one per capability, grouped by four pillars. Every page delivers its wow from recorded replay artefacts — no key, ever. Its own Fable project (`app/showcase/`) imports **none** of the provider/key machinery, so the built showcase artifact contains no key-egress code — inspectable in the shipped files, enforced by its per-entry CSP.

This repo sits in the Fuaran workspace alongside the `fuaran` F# language tier and the `fuaran-ts` TypeScript reference implementation. Cross-repo development conventions (port allocation, launcher patterns, formatting mandate, publication boundary) live at the maintainers' workspace level and are not shipped here.

## Posture

- **Apache 2.0 from day one**, public by design — like `fuaran-ts`, this plants the canonical-implementation flag. It consumes only the public `@fuaran-ui/*` packages; it must never reference a private sibling or a private package name.
- **No server, no login, no secrets.** The only key-bearing network egress anywhere is the playground's BYOK provider call from the user's browser; the visitor's key is held in the tab only and never persisted to disk by default. The showcase entries send no key anywhere (their one non-`'self'` allowance is the pinned Pyodide CDN — a static, opt-in runtime download for the in-browser Python host).

## Layout

**Built entirely in Fuaran itself (F#/Fable) — Phase 326.** The playground is an
Elmish app rendered through the F# `Fuaran.UI.Renderer`; there is no TypeScript app
shell. The remaining TS is the query-portal Fable bridge, the wire-format parity
render-hosts, and the CSP origin constants.

```
fuaran-live/
├── index.html              # the PLAYGROUND entry; loads /app/output/App.js, mounts #fuaran-live-fs-root
├── showcase.html           # the SHOWCASE entry; loads /app/showcase/output/App.js, mounts #fuaran-showcase-root
├── receiver.html           # the bare teleport receiver (HOST 2); loads /app/showcase/output/Receiver.js
├── vite.config.ts          # port 24040, base './', per-entry CSP plugin; VITE_SITE=showcase builds the
│                           #   showcase artifact (dist-showcase/, entry renamed index.html); VITE_DUAL_HOST parity pages
├── app/                    # the playground Fable project (compiled to app/output/, gitignored)
│   ├── FuaranLive.fsproj   #   ProjectRefs Fuaran.UI + Renderer; LINKS the Fable-safe Ops apply engine
│   ├── shared/Brand.fs     #   the SHARED brand module (palette theme + persisted light/dark preference)
│   ├── Ports.fs            #   IAIProvider + EffectPorts seams (port of the former ports.ts)
│   ├── Byok.fs             #   memory key store + browser effect ports + the Anthropic `fetch` provider
│   ├── SystemPrompt.fs     #   the system prompt = the language repo's drift-checked prompt pack
│   │                       #   (fuaran/docs/prompt-pack/system-prompt.md, ?raw sibling import — the
│   │                       #   eval-measured teaching) + a chat-surface overlay; locked by
│   │                       #   test/promptPack.test.ts + closedLoop.test.ts
│   ├── Session.fs          #   in-memory session + closed loop (decode→apply→fold over linked Fuaran.UI.Ops)
│   ├── App.fs              #   Elmish Model/Msg/update + the chrome + chat/preview/inspector panes + boot
│   └── showcase/           # the showcase Fable project (own Showcase.fsproj; compiled to app/showcase/output/)
│       ├── Pillars.fs      #   the four pillars + the page registry
│       ├── Pages.fs        #   hash routing, stubs, the shared footer
│       ├── Replay.fs       #   page-agnostic scripted-replay loader (every page's keyless mode)
│       ├── Conformance.fs  #   the CI conformance panel (honest staleness — grey on stale, never fake green)
│       ├── App.fs          #   the showcase shell (topbar + pillar nav + routes + footer) + boot
│       ├── Receiver.fs     #   the vacant receiver page root
│       └── …               #   one file per capability page (+ per-page *.ts helpers + app.css)
├── app.css (in app/)       # playground chrome styles (the .fl-* classes); showcase chrome rides app/showcase/app.css (.ds-*)
├── scripts/fable-app.mjs   # `dotnet fable` wrapper for BOTH projects — tolerates the benign F# 222 diagnostic
├── src/                    # remaining TS (NOT the app shell):
│   ├── query-portal/       #   the Phase 324/325 Fable↔TS bridge facade (core.ts + sources.ts)
│   ├── hosts/              #   the wire-format parity render-host iframes (ts/fable) + protocol
│   └── byok/origins.ts     #   provider-origin constants (imported by vite.config.ts for the CSP)
├── fable-host/             # the F# (Fable) parity render host + the query-portal bridge (QueryPortalBridge.fs)
├── test/queryPortal.test.ts   # vitest — the F#↔TS query-portal gate (the shell-coupled tests were retired with the shell)
├── run.ps1                 # Stage-1 launcher (dotnet tool restore + Invoke-Pnpm)
└── .github/workflows/      # ci.yml + pages.yml + azure-static-web-apps.yml
```

**Build:** `pnpm run dev` / `build` run `dotnet fable` over both projects (via `scripts/fable-app.mjs`)
then Vite; `fable:app:watch` rebuilds the playground F# live alongside `vite`. Two artifacts, one per
origin: the default `pnpm build` emits the playground (`dist/`, index.html only); `pnpm build:showcase`
emits the showcase (`dist-showcase/`, showcase.html renamed to index.html + receiver.html). Each entry
carries its own CSP: the playground allows `connect-src` to the BYOK provider origins; the showcase
allows `'self'` plus only the pinned Pyodide CDN (the opt-in in-browser Python host). In dev, one Vite
server (24040) serves all entries; the playground door on the showcase landing targets
`VITE_PLAYGROUND_ORIGIN` in production and falls back to this origin's index page in dev.
`VITE_DUAL_HOST=1` additionally emits the `ts-host.html` / `fable-host.html` parity pages.

**`dotnet build` is not a supported gate for the two app projects** (`app/FuaranLive.fsproj`,
`app/showcase/Showcase.fsproj`), and cannot be made one: their `DefineConstants` reach only their own
sources, so the ProjectReferenced Fuaran projects build as plain .NET assemblies _without_
`FABLE_COMPILER` — Fable-only members there (e.g. the renderer's `StateStore.useStateKeys` React
hook, `#if FABLE_COMPILER`-guarded by design) are absent from the assemblies, and any call site fails
FS0039. `dotnet fable` recompiles the whole graph from source with `FABLE_COMPILER` defined
everywhere, which is why the real pipeline is green. Both fsprojs carry a `FableOnlyGuard` target
that fails a plain `dotnet build` immediately with this explanation (design-time builds are exempt;
`-p:SkipFableOnlyGuard=true` bypasses it). The Fable compile via `scripts/fable-app.mjs` is the
typecheck gate.

## Dependencies

The `@fuaran-ui/*` packages resolve from the public npm registry (`^0.1.0`). The committed
`pnpm-lock.yaml` is authoritative — CI and local installs use `pnpm install --frozen-lockfile`.
For inner-loop iteration against unpublished sibling changes, a temporary `pnpm.overrides`
`link:` bridge to a local `fuaran-ts` checkout works, but must never be committed.

## Effect / provider portability (§4l)

Everything effectful is an injected F# interface (`app/Ports.fs`): `IAIProvider` (the LLM call) and `EffectPorts` (clipboard / download / notify / warn). The browser implementations live in `app/Byok.fs` (the Anthropic `fetch` provider + the memory key store + the browser effect ports). The Elmish `update` loop depends only on the interfaces, so a server host could drive the identical loop with a different provider impl.

## Formatting mandate

Per the workspace mandate, every commit is preceded by a Prettier pass (`pnpm format` / `pnpm format:check`). TS-side commits run Prettier where F#-side commits run Fantomas; both are non-negotiable.

## Emitter-lock convention (in-page wire is CI-locked)

The showcase pages hand-author wire-format JSON in three shapes: TypeScript object
literals with `$type`, Python dicts in the Pyodide host files (`public/**/*.py`), and
canned strings. Any such surface is an **in-page wire emitter**, and every one carries an
automated certification in the vitest suite. The standing lock + the full emitter inventory
(with every deliberate exclusion and its reason) live in
[`test/emitterLocks.test.ts`](test/emitterLocks.test.ts); Rosetta's byte-parity strip is
additionally pinned in [`test/rosettaParity.test.ts`](test/rosettaParity.test.ts).

**The rule:** any NEW in-page wire emitter ships with its lock in the same change-set.
Hand-authored wire without a lock is a review defect. The strongest available form per
surface is: decode the emitter's output with the real strict decoder (`@fuaran-ui/ops`
`decodeNode`) and assert it re-encodes to itself through `encodeNode` (decode then re-encode
is the identity) – so the emitter is both strictly decodable and already canonical. For a
Python host, run it headlessly (spawn `python`, exec the stdlib-only file, drive its builder
over default inputs), skipping cleanly with a named reason when no interpreter exists. A
surface that genuinely cannot run headlessly is documented as page-manual, exactly like
Rosetta's Pyodide leg – but prefer headless.

What is NOT an emitter: F#-authored trees built through the real `Fuaran.*` constructors and
encoded by `CanonicalJson` / `Fuaran.Core.Wire` are canon-by-construction (the reference
encoder is its own oracle). Adversarial reject fixtures (assert-reject, not assert-canonical)
and scripted-provider fixtures emulating an LLM's lenient-accept emissions are excluded with
their reasons named in the inventory.

## Port allocation

Vite dev `24040`, preview `14040` — the website band reserved for `fuaran-live` in the workspace `CLAUDE.md` "Port allocation" table. The app is static; **no server port** is allocated.
