# Security posture — fuaran-live

`fuaran-live` is a **serverless, client-only** browser playground. You paste your own
LLM provider API key, and the page calls the provider directly from your browser.
There is no account, no backend, and no server that ever sees your key or your
prompts. This document is the threat model for that design: what the key is, where
it can and cannot go, why a client-only "bring your own key" (BYOK) surface is an
acceptable design for this app, the residual risks, and the mitigations that bound
them.

The governing principle is **deny-by-default for the key's egress**: the API key is
the single most sensitive value the app touches, so every path it could take is
closed unless explicitly opened. Exactly one path is opened — the request to the
provider you chose.

## The asset

The asset under protection is **your provider API key** (and, secondarily, the
content of your prompts and the model's responses). Everything else the app handles
— the wire-format JSON the model emits, the rendered UI tree, the op stream — is not
sensitive.

## Architecture, in one line

Browser ⇄ provider. Nothing else is in the loop. The app is static files (HTML, JS,
CSS) served from any plain static host; once loaded, the only network connection it
opens is the BYOK call to the provider origin you selected.

## Key-handling guarantees

1. **Memory only — never persisted.** The key lives in a single in-memory variable
   (the key store in `app/Byok.fs`). It is **never** written to `localStorage`,
   `sessionStorage`, IndexedDB, a cookie, or any other storage surface. The store
   module references no storage global at all.
2. **Cleared on reload and tab close.** Because the key exists only on the JS heap,
   reloading or closing the tab forgets it. The app additionally registers a
   `pagehide` scrub (`scrubOnUnload`) that drops every provider's key reference
   proactively — `pagehide` rather than `unload` because it also fires when the
   page enters the browser's back/forward cache, where the heap survives and
   `unload` never runs. You re-paste the key for each session, by design — there
   is no "remember me".
3. **One egress point per provider.** The key leaves the browser only as the
   provider's auth header on the call to the provider you selected — `x-api-key`
   for Claude, `Authorization: Bearer` for OpenAI, Kimi (Moonshot) and Grok (xAI),
   `x-goog-api-key` for Gemini.
   Each adapter routes every call through a single egress helper, and each fetches
   exactly one origin (the set in `src/byok/origins.ts`). The key is never placed
   in a URL, a query string, a request body, a log line, or any other request.
   Keys for different providers are held in independent in-memory stores;
   switching providers never moves a key between them. The request sets
   `redirect: 'error'`, so a redirect is refused rather than followed: browsers
   strip `Authorization` across an origin boundary but **not** custom headers, and
   a 30x between two allow-listed providers would otherwise replay `x-api-key` to
   an origin you did not choose.
4. **Not surfaced in error messages.** The two strings on the failure path that
   this app does not author — the provider's own error text, and a transport
   exception — are scrubbed of the request's auth-header values before they reach
   the UI, the warning port, or any log. A provider that echoes back the
   credential it rejected cannot put it on screen.
5. **No telemetry, analytics, or corpus sink.** The app ships with no
   analytics, no error-reporting beacon, and no usage-telemetry path. Apart from
   loading its own static assets from its own origin, there is no network egress
   other than the provider call — and, on the optional live-drive mode, the STUN
   server described below, which never sees UI data or the key. If an opt-in telemetry or
   anonymous-corpus feature is ever added, it must be **key-blind by construction**
   — it must never be able to observe the key value — and the guard test below
   enforces that the key reaches no non-provider boundary.

These guarantees are not just documented — they are **tested**, by
[`test/networkEgress.test.ts`](test/networkEgress.test.ts), which runs in CI on
every push and pull request. It installs an instrumented world — fake
`localStorage` / `sessionStorage` / IndexedDB / `document.cookie`, recording
`sendBeacon` / `XMLHttpRequest` / `WebSocket` / `EventSource` / `Image` /
`RTCPeerConnection`, a patched `console`, and a `fetch` that captures the URL,
the body and every non-auth header — then drives the **real** adapter for every
provider, on both the single-shot and the tool-use path, across the success
branch and every failure branch (401, 429, a 500 with an HTML body, a non-JSON
200, a transport exception, and a provider that echoes the key back). Anything
written anywhere is checked against the key: a match outside the provider's own
auth header fails the test.

Two details of that test are worth stating, because they are what make it
evidence rather than decoration. It asserts the key **is** present in the auth
header, so "no leak found" can never be the vacuous result of a probe that
carried no key. And the instrumentation is itself tested: a synthetic leaky
client writes the key to each surface in turn and every one must be caught, so a
harness that quietly stopped observing goes red instead of passing like a
harness with nothing to find.

## Network egress — Content-Security-Policy

The shipped production build carries a strict Content-Security-Policy injected into
`index.html` at build time (see `vite.config.ts`):

```
default-src 'self';
connect-src 'self' https://api.anthropic.com https://api.openai.com https://generativelanguage.googleapis.com https://api.moonshot.ai https://api.x.ai;
img-src 'self' data:;
style-src 'self' 'unsafe-inline';
script-src 'self' 'sha256-…';
font-src 'self' data:;
frame-src 'self';
base-uri 'none';
object-src 'none';
form-action 'none'
```

The load-bearing line is the **`connect-src`** allow-list, which names only the
app itself plus the supported BYOK provider origins (Claude / OpenAI / Gemini /
Kimi / Grok):
the browser will refuse to open a network connection to any origin other than
those. The list is generated from `src/byok/origins.ts` — the same constants the
adapters fetch — so the policy and the egress code cannot drift apart; the egress
test asserts those two sets are equal, so that is enforced rather than merely
intended. This is the _structural_ guard behind the key-egress posture — even if
a dependency were compromised and tried to POST the key to an attacker origin,
the browser blocks the connection. `script-src 'self'` (no `'unsafe-inline'`, no
`'unsafe-eval'`) blocks injected and inline scripts; the one `'sha256-…'`
expression pins the inline pre-paint theme script by digest, computed from the
page at build time, so an edit to it re-derives the hash rather than silently
widening the policy. `object-src 'none'` / `base-uri 'none'` / `form-action
'none'` close the usual CSP-bypass side doors. `style-src 'unsafe-inline'` is
required because the renderer sets inline `style` attributes and injects theme
custom-properties inline; no inline _scripts_ are permitted. `font-src` allows
`data:` because the build inlines the smallest font subsets as data URIs.

The dev server uses a relaxed variant (HMR needs inline script + `eval` + a
websocket); the **shipped `dist/index.html` always carries the strict policy
above**.

### What the policy does not cover — delivery, and clickjacking

The policy above ships as a `<meta http-equiv>` tag, and that has one consequence
worth stating plainly, because naming a boundary is what makes the rest of this
document credible:

- **`frame-ancestors` is header-only by specification.** In a `<meta>` policy the
  browser ignores it. So this app's anti-framing protection does **not** come from
  the CSP above; it comes from response headers, which only a host configured to
  send them will send.
- **`public/staticwebapp.config.json` configures Azure Static Web Apps and
  nothing else.** On that host the app is served with `X-Content-Type-Options:
nosniff`, `Referrer-Policy: no-referrer`, `Content-Security-Policy:
frame-ancestors 'self'` and `X-Frame-Options: SAMEORIGIN` — note **`'self'`,
  not `'none'`**: same-origin framing is permitted, because the wire-format
  parity pages frame each other.
- **A plain static host — GitHub Pages among them — serves none of those.** It
  ignores `staticwebapp.config.json` entirely, so a Pages deployment has no
  `frame-ancestors`, no `X-Frame-Options`, no `Referrer-Policy` and no `nosniff`
  header at all. Everything in the `<meta>` policy still applies, including the
  `connect-src` allow-list that bounds the key — but the header-delivered
  protections, and clickjacking protection in particular, are a property of the
  deployment rather than of this codebase.

If you self-host, serve those four headers from your own host configuration.

## Trust boundaries — what an attacker can and cannot reach

| Actor                                 | Can reach                                  | Cannot reach                                                                                         |
| ------------------------------------- | ------------------------------------------ | ---------------------------------------------------------------------------------------------------- |
| **The app's own code**                | The key in memory; the provider call       | Any storage surface (it writes none); any non-provider HTTP origin (`connect-src` blocks it)         |
| **A passive network observer**        | Nothing — the provider call is HTTPS (TLS) | The key (encrypted in transit); there is no other traffic to observe                                 |
| **The static host serving the files** | The static assets it serves                | The key (entered after load, never sent back to the origin); prompts and responses (provider-direct) |
| **A site operator / "backend"**       | — there is none —                          | Everything (no server exists)                                                                        |

Because there is no server, there is no server-side store to breach, no logs that
could capture the key, and no operator who can see your prompts. The trust you
extend is to (a) the provider you chose, (b) the static host serving the page, and
(c) your own browser environment. The first is inherent to using the provider at
all; the CSP + memory-only posture bound the second; the third is the residual-risk
surface below.

## Live-drive channel (present → audience)

The optional "present live" mode drives a second window's — or a second device's —
UI from this one. What crosses the channel is only **UI-as-data**: a canonical
wire-format tree, then each subsequent `TreeOp` — the exact same data a shareable
permalink already puts in the URL. The BYOK key **cannot** cross the channel by
construction: the channel's message type admits only a tree or an op, and the key is
held solely in the prompting tab's key store (an egress-guard test asserts this).
There are two transports for that one message contract:

- **Same-window (Stage 1) — `BroadcastChannel`.** A browser-local channel between
  two tabs of the same origin, with **no network egress at all**.
- **Cross-device (Stage 2) — WebRTC peer-to-peer.** An explicit, opt-in "pair a
  device" mode. The two devices are paired by an out-of-band SDP handshake (a QR
  code / copy-paste code the operator carries across — there is **no signalling
  server**), and the UI data then flows **directly peer-to-peer** over a WebRTC data
  channel; **no server ever stores or relays it**. This introduces exactly one new
  network destination: a single public **STUN** server (`stun:stun.l.google.com:19302`,
  `WebRtc.stunServer` in the source), used **only** for NAT traversal during the
  handshake so the two peers can find each other. No UI data and never the key
  touches STUN — the data channel needs no server, and the key still never leaves
  the prompting tab (the `LiveDriveMessage` type has no case that could carry it,
  and the SDP signalling codec round-trips only an SDP string; guard tests in
  `test/liveDrive.test.ts` and `test/webRtc.test.ts` assert both).
  **That destination is not, and cannot be, expressed in `connect-src`.** CSP has
  no source expression for the `stun:` scheme, and browsers do not gate
  `RTCPeerConnection` ICE on `connect-src` at all — CSP3 reserves a separate
  `webrtc` directive for it, defaulting to allow. An earlier version of this
  document described a `stun:` entry in the policy; the entry existed, was
  reported invalid and ignored by the browser, and was removed in July 2026.
  Pairing worked because the directive never applied, not because of the entry.
  So the bound on this channel is structural rather than policy-enforced: the
  message type admits only a tree or an op. Pairing is explicit (you generate or
  scan a code) and revocable (End session tears the connection down).

## Why client-only BYOK is acceptable here

A BYOK design moves the key's trust boundary from "a server we ask you to trust"
to "your own browser, talking directly to the provider you already trust". For a
no-login public playground that is the _stronger_ posture: there is no shared
backend that could leak every visitor's key at once, no credential store to
compromise, and no operator-side logging. The key's exposure is reduced to the
visitor's own machine and the one HTTPS call they intended to make. The residual
risks below are the price of running provider calls in a browser at all — they are
the same risks any browser-based BYOK tool carries, and they are documented rather
than hidden.

## Residual risks and mitigations

These are the risks the design **cannot** fully eliminate, with how each is bounded:

- **Malicious or over-permissioned browser extension.** An extension with host
  permissions for the page can, in principle, read the DOM and page memory and
  could observe a key while it is being entered or held. _Mitigation:_ none is fully
  effective against a privileged extension — this is true of every web app. The
  memory-only posture means there is no persisted key for an extension to harvest
  later; the exposure is limited to the active session. **Recommendation:** run the
  playground in a clean browser profile without untrusted extensions, especially if
  using a high-value key.
- **Cross-site scripting (XSS) / injected script.** If attacker-controlled script
  ran in the page, it could read the in-memory key. _Mitigations:_ (a) the strict
  CSP forbids inline and injected scripts (`script-src 'self'`, no `'unsafe-inline'`/
  `'unsafe-eval'`) and forbids exfiltration even if script did run
  (`connect-src` locked to the provider); (b) the model's emitted content is **not**
  executed — it is decoded as data (canonical wire-format JSON) and rendered through
  the renderer's **sanitization floor**: strings are escaped by default, Markdown
  output is sanitized (script/iframe/event-handler/`javascript:` stripping), and
  URLs are scheme-allowlisted. The renderer cannot be coerced into running a script
  by anything the model emits. See the renderer's published sanitization contract
  (`SANITIZATION.md` in the language repo) for the per-seam policy.
- **Compromised dependency (supply chain).** A malicious version of a build-time or
  runtime dependency could try to read or exfiltrate the key. _Mitigations:_ the
  runtime dependency graph is deliberately minimal (the public renderer packages +
  React + a Markdown parser; the provider call uses raw `fetch`, no SDK); and the
  `connect-src` CSP blocks exfiltration to any non-provider origin regardless of what
  a dependency attempts.
- **Network interception (MITM).** _Mitigation:_ the provider call is HTTPS; the key
  rides a TLS-encrypted header. The CSP `connect-src` names an `https:` origin.
- **Shoulder-surfing / local screen capture.** The key input is a password field
  (masked), with `autocomplete="off"` and spellcheck disabled so the value enters
  neither the browser's saved-form history nor a remote spellcheck service.
  _Mitigation:_ standard masked entry; a password manager may still offer to save
  the value, which is your own vault and your own call. Beyond that this is the
  user's physical-environment concern.
- **Phishing clone.** A look-alike site could ask for your key. _Mitigation:_ general
  web hygiene — verify the origin before pasting a key. The official source is the
  project's own repository and its documented deploy URLs.

## What is explicitly out of scope

- Protecting against a fully compromised operating system or browser binary.
- Protecting against a malicious _provider_ (you are choosing to send your key to
  that provider — that trust is inherent to BYOK).
- Rate-limiting, billing, or abuse protection on the provider account — those are
  governed by your own provider key's settings.

## The rule this document is held to

**A claim in this repository that names a test cites a test that exists and runs
in CI. A claim that names a mitigation cites code that exists.** If neither is
true, the claim is removed. There is no third option — a sentence describing a
guard that is not there is worse than saying nothing, because it is precisely the
sentence a reviewer checks instead of checking the code.

This rule is written down because its absence produced a real defect. Earlier
versions of this file and of the README described an egress test that did not
exist, a `pagehide` scrub that had never been implemented, a `connect-src` entry
that had been deleted, and a `frame-ancestors 'none'` that was never delivered.
None of it was dishonest in intent: each sentence described something intended,
or something once true, and nothing failed when it stopped being true. That is
the failure mode a security document is uniquely prone to, since prose has no
build step.

So, concretely, when changing anything in this repository:

- Adding a security claim here or in the README means adding the test that backs
  it in the same change, and the test must run in the default CI test job.
- Removing or renaming a mitigation means updating every document that names it,
  in the same change.
- Changing the delivered CSP, the provider origin list, or the host header
  configuration means updating the [Network egress](#network-egress--content-security-policy)
  section, in the same change. The provider-origin half of that is enforced —
  the egress test fails if the policy's origin list and the adapters' diverge.

## Reporting a vulnerability

If you find a security issue in `fuaran-live`, please open an issue on the project
repository (or use the repository's private security-advisory channel for sensitive
reports). Because the app is client-only with no backend, the highest-value reports
concern the key-handling guarantees above — anything that could route the key to a
storage surface, a log, or a non-provider origin.
