# fuaran-live

A **serverless, client-only BYOK playground** for the [Fuaran](https://github.com/fuaran-ui) UI language. Open it in a browser, paste your own LLM API key, and prompt a model in plain language — it emits **canonical Fuaran wire-format JSON**, and the `Fuaran.UI` tree renders live in front of you. **No account, no server, no backend.**

It is the thirty-second, no-login companion to the language: the interactive proof that one canonical wire-format contract, authored by an AI, renders identically across conformant hosts.

## What it does

Three panes, all in the browser:

1. **Conversation** — you chat with the model using your own key (Claude day one). The key is held in the tab only.
2. **Live preview** — the emitted `Fuaran.UI` tree, rendered via [`@fuaran-ui/renderer`](https://github.com/fuaran-ui/fuaran-ts).
3. **Inspector** — a dropdown toggling the **canonical wire JSON** and the **TreeOp stream** that built the current tree, both live.

The model emits canonical wire JSON; the app decodes it via `@fuaran-ui/schema`, applies ops via `@fuaran-ui/ops`, and renders via `@fuaran-ui/renderer`. An in-memory session cache (folded tree + op stream + history) is injected back into each turn, so a follow-up like _“make the third button red”_ edits the existing tree incrementally — a closed loop with **no persistence and no server**. The playground's own chrome is itself a host-neutral `Fuaran.UI` tree (the demo of Fuaran is a Fuaran app).

## Bring your own key (BYOK)

- Paste an Anthropic API key into the Conversation pane. It is held **in memory only** — remembered for this session, **never written to disk or any browser storage** (`localStorage` / `sessionStorage` / IndexedDB), and **never sent anywhere but the provider** (`api.anthropic.com`). Reloading or closing the tab forgets it.
- A strict Content-Security-Policy locks the production build's `connect-src` to the provider origin, so even a compromised dependency cannot exfiltrate the key. A guard test (`test/networkEgress.test.ts`) asserts the key reaches no storage, logging, or non-provider boundary.
- The full threat model — trust boundaries, residual risks (malicious extension, XSS, supply chain), and mitigations — is in [`SECURITY.md`](SECURITY.md).

## Run it

```powershell
.\run.ps1                 # install + serve the playground on http://localhost:24040 + open a browser
.\run.ps1 -Build          # produce a static dist/ (no server)
.\run.ps1 -NoBrowser      # serve without opening a browser
```

Or drive the package manager directly:

```bash
pnpm install
pnpm dev        # serve on 24040
pnpm build      # static dist/
pnpm test       # the full test gate: unit suite + wire-format conformance suite
```

`pnpm test` runs both vitest suites: the unit tests (`test/`) and the projection-conformance
suite (`tests/projection-conformance/`), which re-encodes every Node fixture in the sibling
`../wire-format-fixtures` corpus byte-identically. Both consume the Fable-compiled output, so
run `pnpm run fable:app` (or `pnpm build`) first. `pnpm run test:unit` runs the unit leg alone.

The static build runs from any plain static host, or directly from `file://` (`base: './'` emits relative asset URLs).

## Deploy

The app is **fully static — no server, no API tier, no secrets** (the visitor's provider key is used browser-direct at runtime). Any static host works.

### Two artifacts, two origins

One codebase builds two independent static artifacts, each deployed to its own origin:

| Artifact                                                | Build                 | Output                                                                        | Production origin             |
| ------------------------------------------------------- | --------------------- | ----------------------------------------------------------------------------- | ----------------------------- |
| **The playground** (BYOK authoring)                     | `pnpm build`          | `dist/`                                                                       | **https://fuaran-ui.live**    |
| **The showcase** (the demo gallery + teleport receiver) | `pnpm build:showcase` | `dist-showcase/` (`showcase.html` renamed to `index.html`, + `receiver.html`) | **https://fuaran-ui.gallery** |

Each artifact carries its own build-time CSP: the playground allows `connect-src` to the BYOK provider origins; the showcase allows `'self'` plus only the pinned Pyodide CDN. Keeping them on separate origins is what makes the showcase's zero-key-egress claim inspectable per origin.

The doors between the two are wired at build time and fall back to same-origin pages when unset (right for dev and a single-origin test deploy):

```bash
# showcase build: its "open the playground" door
VITE_PLAYGROUND_ORIGIN=https://fuaran-ui.live   pnpm build:showcase
# playground build: its door across to the demo gallery
VITE_SHOWCASE_ORIGIN=https://fuaran-ui.gallery  pnpm build
```

Deploy routes wired today:

- **GitHub Pages** (playground artifact) — [`.github/workflows/pages.yml`](.github/workflows/pages.yml), on push to `main`.
- **Azure Static Web Apps, playground** (static SKU, no Functions) — [`.github/workflows/azure-static-web-apps.yml`](.github/workflows/azure-static-web-apps.yml), on push to `main` + PR preview environments.
- **Azure Static Web Apps, showcase** — [`.github/workflows/azure-static-web-apps-showcase.yml`](.github/workflows/azure-static-web-apps-showcase.yml), the same shape against the showcase's own resource + token, building `dist-showcase/` with the playground door pre-pointed at fuaran-ui.live.

The workflows bake the cross-door origins in, so a push to `main` produces both origins correctly linked to each other.

### Azure Static Web Apps

One-time setup, **per origin** (one resource for the playground, one for the showcase):

1. Create the resource (Portal → _Create a resource_ → _Static Web App_, or CLI):
   ```bash
   az staticwebapp create -n fuaran-live    -g <resource-group> -l <region>   # playground
   az staticwebapp create -n fuaran-gallery -g <resource-group> -l <region>   # showcase
   ```
   Choose **deployment source: Other** so the GitHub workflow drives uploads (don't let Azure inject its own workflow — this repo already has one).
2. Copy each resource's **deployment token** (Portal → the resource → _Manage deployment token_, or `az staticwebapp secrets list -n <name> -g <resource-group> --query "properties.apiKey" -o tsv`).
3. Add each token as a GitHub repo secret (Settings → Secrets and variables → Actions): the playground's as **`AZURE_STATIC_WEB_APPS_API_TOKEN`**, the showcase's as **`AZURE_STATIC_WEB_APPS_API_TOKEN_SHOWCASE`**.
4. Bind the custom domains (Portal → the resource → _Custom domains_, or CLI):
   ```bash
   az staticwebapp hostname set -n fuaran-live    -g <resource-group> --hostname fuaran-ui.live
   az staticwebapp hostname set -n fuaran-gallery -g <resource-group> --hostname fuaran-ui.gallery
   ```

After that, every push to `main` builds and deploys both artifacts to their origins; PRs get a temporary preview URL per resource commented on the PR and torn down on close. [`public/staticwebapp.config.json`](public/staticwebapp.config.json) provides SPA fallback routing and a couple of safe security headers (the Content-Security-Policy is the strict one injected into each entry at build time by the Vite plugin — locking network egress per artifact as above — so it travels with the document and isn't duplicated as a header here).

### Deploy a build manually (bypassing CI)

The push-to-`main` automation above is the normal route. To push a one-off build by hand (a hotfix while CI is down, or a smoke deploy from a branch), build locally and upload each artifact with the SWA CLI:

```bash
# from the fuaran-live/ dir inside the Fuaran workspace
pnpm install --frozen-lockfile

# playground -> fuaran-ui.live
VITE_SHOWCASE_ORIGIN=https://fuaran-ui.gallery pnpm run build   # -> dist/ (strict prod CSP)
npx @azure/static-web-apps-cli deploy ./dist \
  --deployment-token <playground AZURE_STATIC_WEB_APPS_API_TOKEN> \
  --env production

# showcase -> fuaran-ui.gallery
VITE_PLAYGROUND_ORIGIN=https://fuaran-ui.live pnpm run build:showcase   # -> dist-showcase/
npx @azure/static-web-apps-cli deploy ./dist-showcase \
  --deployment-token <showcase AZURE_STATIC_WEB_APPS_API_TOKEN> \
  --env production
```

## Known limitations (MVP)

- **Client-only interactivity.** Emitted UIs render and are explorable, but server-backed forms / data fetches are inert — closures don't cross the wire (by design; see the wire-format spec).
- **Ephemeral session.** The tree, op stream, and conversation are held in browser memory and cleared on reload.
- **Claude day one.** Other providers layer on later behind the same `IAIProvider` seam.

## License

[Apache-2.0](LICENSE). © 2026 Diametrical Ltd. Third-party attributions in [`NOTICE`](NOTICE).

The licence covers the code, not the name — see [`TRADEMARK.md`](TRADEMARK.md) if you're
forking (short, and it says yes to more than you'd expect).

## Contributing

**This project does not accept external code contributions** — bug reports are very welcome,
pull requests will be closed. The reasoning, and where contributions _do_ have leverage, are
in [`CONTRIBUTING.md`](CONTRIBUTING.md).
