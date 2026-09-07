// ============================================================================
//  HOST 3 — the receiver with no F# on it (/ts-receiver.html).
//
//  Teleport's claim is that a running application crosses to another machine as
//  bytes and resumes there. Two halves have to hold for that to mean anything,
//  and they are different claims:
//
//    HOST 2 (/receiver.html) shows a VACANT host — a page that ships a player
//    and no application, so what you watch materialise came out of the URL
//    fragment and nowhere else.
//
//    HOST 3 (this page) shows a FOREIGN host — every line of it is TypeScript,
//    and the bundle it renders was minted by the F# tier. Nothing on this page
//    was compiled from F#: no Fable output is imported here, which is a fact
//    you can check by reading the imports below and the document that loads it.
//
//  The second half is the one the format actually turns on. A bundle that only
//  ever resumes inside its own producer's runtime is a save file; a bundle that
//  resumes in a different language's runtime is a wire format. The decoder here
//  is `@fuaran-ui/op-stream`'s `decodeTeleport`, certified against the shared
//  corpus's teleport family — the same fixture this page ships as its own
//  demonstration bundle, so the thing you see rendered is the thing the
//  conformance suite checks.
// ============================================================================

import { StrictMode, useCallback, useEffect, useState, type ReactElement } from 'react';
import { createRoot } from 'react-dom/client';

import { decodeTeleport, type TeleportError } from '@fuaran-ui/op-stream';
import { FuaranRenderer, defaultTheme, emptySources } from '@fuaran-ui/renderer';
import type { Node } from '@fuaran-ui/schema';

import '@fuaran-ui/renderer/css';
import '../brand/fuaran-brand.css';
import './app.css';
import '../icon-glyphs.css';

// The demonstration bundle IS the corpus fixture — the reference bundle the F#
// tier minted, promoted into the shared conformance corpus so every host
// certifies against it. Inlined at build time from the sibling checkout, the
// same posture the parity pane's fixtures use. Nothing here is a copy that can
// drift: change the corpus and this page changes with it.
import referenceFixtureRaw from '../../../wire-format-fixtures/teleport/tp-wizard-step1.json?raw';

const REFERENCE_BUNDLE: string = (JSON.parse(referenceFixtureRaw) as { encoded: string }).encoded;

// ─── Reading a bundle out of the address bar ─────────────────────────────────
//
// Everything before the `#` is sent to the server in the HTTP request; the
// fragment never is. Riding the bundle on the fragment is what makes "nothing
// was uploaded" a checkable fact rather than a policy claim — and it is why
// this page can be read as evidence at all.
//
// Two fragment shapes exist in the wild: the site page's
// `#/demo/teleport?t=FT1…` and a bare receiver's `#t=FT1…`. This reader
// tolerates both, so a bundle lands here whichever way it travelled.

const FORMAT_PREFIX = 'FT1.';

/** Cut a payload at the first `&` — a bundle is base64url and holds none, so
 *  anything past one is a further fragment parameter, not application bytes. */
const untilAmp = (s: string): string => {
  const i = s.indexOf('&');
  return i >= 0 ? s.slice(0, i) : s;
};

/** A bundle in the current fragment, in either shape. */
const readFragment = (): string | null => {
  const marker = `t=${FORMAT_PREFIX}`;
  const i = window.location.hash.indexOf(marker);
  return i >= 0 ? untilAmp(window.location.hash.slice(i + 2)) : null;
};

/** Accept a pasted raw `FT1.…` string OR a whole teleport link. */
const extractPayload = (raw: string): string => {
  const s = raw.trim();
  const i = s.indexOf(`t=${FORMAT_PREFIX}`);
  return i >= 0 ? untilAmp(s.slice(i + 2)) : s;
};

// ─── What a refusal says ─────────────────────────────────────────────────────
//
// Every failure is a typed case, never a throw, so the page can say WHICH
// obligation was not met rather than "something went wrong". The digest cases
// are the interesting ones: they mean the bytes changed in transit, and the
// honest thing to do with an app whose state was edited on the way here is to
// refuse to run it.

const describeRefusal = (error: TeleportError): string => {
  switch (error.code) {
    case 'Oversize':
      return `Too large to open safely (limit ${error.limit}). Refused before any decompression.`;
    case 'InvalidFormat':
      return 'That is not a teleport bundle — the tag, the base64url or the compressed stream is wrong.';
    case 'InvalidJson':
      return 'The bundle decompressed, but its envelope is not valid JSON.';
    case 'InvalidEnvelope':
      return `The envelope is missing or mistyped at ${error.path}.`;
    case 'UnsupportedVersion':
      return `This bundle declares version "${error.found}", which this decoder does not implement. Refused by name rather than guessed at.`;
    case 'DigestMismatch':
      return 'The integrity digest does not match: these bytes were altered after they were signed. Refusing to resume an app whose state was edited in transit.';
    case 'TreeDecode':
      return `The application tree inside the bundle failed to decode (${error.error.code} at ${error.error.path}).`;
  }
};

// ─── The page ────────────────────────────────────────────────────────────────

interface Arrived {
  readonly tree: Node<unknown>;
  readonly digest: string;
  readonly state: Readonly<Record<string, unknown>>;
}

type Screen =
  | { readonly kind: 'vacant' }
  | { readonly kind: 'arrived'; readonly app: Arrived }
  | { readonly kind: 'refused'; readonly why: string };

const Receiver = (): ReactElement => {
  const [screen, setScreen] = useState<Screen>({ kind: 'vacant' });
  const [pasted, setPasted] = useState('');

  const receive = useCallback(async (encoded: string): Promise<void> => {
    const result = await decodeTeleport(encoded);
    setScreen(
      result.ok
        ? {
            kind: 'arrived',
            app: {
              tree: result.value.tree,
              digest: result.value.digest,
              state: result.value.state,
            },
          }
        : { kind: 'refused', why: describeRefusal(result.error) },
    );
  }, []);

  // A bundle in the fragment on load, and on every later landing — a hop from
  // another host rewrites this window's fragment, so `hashchange` IS an arrival.
  useEffect(() => {
    const land = (): void => {
      const encoded = readFragment();
      if (encoded !== null) void receive(encoded);
    };
    land();
    window.addEventListener('hashchange', land);
    return () => window.removeEventListener('hashchange', land);
  }, [receive]);

  const claim = (
    <>
      <div className="rcv-badge">HOST 3 — NO F# ON THIS PAGE</div>
      <p className="rcv-line">
        The bare receiver next door demonstrates one half of teleport&apos;s claim: a{' '}
        <strong>vacant</strong> host, shipping a player and no application, so whatever materialises
        arrived as bytes. This page demonstrates the other half: a <strong>foreign</strong> host.
        Every line of it is TypeScript, and the bundle it renders was minted by the F# tier. A
        bundle that only resumes inside its own producer&apos;s runtime is a save file; one that
        resumes in another language&apos;s runtime is a wire format.
      </p>
    </>
  );

  const paste = (
    <div className="tp-paste-row">
      <input
        type="text"
        value={pasted}
        placeholder="paste a boarding pass (FT1.… or a teleport link)"
        aria-label="Paste a teleport bundle"
        onChange={(e) => setPasted(e.target.value)}
      />
      <button
        type="button"
        onClick={() => {
          if (pasted.trim() !== '') void receive(extractPayload(pasted));
        }}
      >
        Open it
      </button>
      <button type="button" onClick={() => void receive(REFERENCE_BUNDLE)}>
        Open the reference bundle
      </button>
    </div>
  );

  if (screen.kind === 'arrived') {
    return (
      <div className="tp-page rcv-page">
        {claim}
        <p className="rcv-line">
          Decoded and digest-verified here, in TypeScript:{' '}
          <code>{screen.app.digest.slice(0, 16)}…</code>. Resumed with{' '}
          {Object.keys(screen.app.state).length}{' '}
          {Object.keys(screen.app.state).length === 1 ? 'state value' : 'state values'} seated — the
          app below is mid-interaction, not freshly started.
        </p>
        <FuaranRenderer
          tree={screen.app.tree}
          sources={emptySources}
          runtime={{}}
          theme={defaultTheme}
        />
        {paste}
      </div>
    );
  }

  return (
    <div className="tp-page rcv-page">
      {claim}
      {screen.kind === 'refused' ? (
        <p className="tp-banner tp-banner-err">{screen.why}</p>
      ) : (
        <p className="rcv-waiting">
          <span className="rcv-cursor" /> waiting for bytes…
        </p>
      )}
      <p className="rcv-hint">
        Land a bundle here from the teleport page, paste one, or open the reference bundle the
        shared conformance corpus holds:
      </p>
      {paste}
    </div>
  );
};

const container = document.getElementById('fuaran-ts-receiver-root');
if (container === null) throw new Error('fuaran-ts-receiver-root mount target missing');
createRoot(container).render(
  <StrictMode>
    <div className="rcv-shell">
      <Receiver />
    </div>
  </StrictMode>,
);
