// QR interop for the Teleport page.
//
// The teleported app is a self-contained string (the Fuaran teleport bundle
// rides a URL query param); this renders that URL as a scannable QR code so a
// phone camera can carry the whole app across. `qrcode-generator` is a tiny,
// zero-dependency, pure-JS encoder – no network, no canvas, bundled by Vite,
// so it works under the site's locked-down CSP (the data-URL it emits is an
// `img-src data:` GIF, already allowed).
//
// Error-correction level "L" maximises data capacity (matching the F# codec's
// QR budget, which is sized against version-40-L). `typeNumber` 0 auto-selects
// the smallest QR version that fits; if the text exceeds even version 40, the
// encoder throws and we return "" so the page falls back to a copyable link.

import qrcode from 'qrcode-generator';

/**
 * A GIF data-URL for `text` rendered as a QR code, or "" if the text is too
 * large for a single QR (the caller then shows the link instead).
 * `cellSize` is the pixel size of one QR module.
 */
export function qrDataUrl(text: string, cellSize: number): string {
  try {
    const qr = qrcode(0, 'L');
    qr.addData(text);
    qr.make();
    return qr.createDataURL(cellSize, 4);
  } catch {
    return '';
  }
}

// ─── The receiver origin (two-origin mode) ──────────────────────────────────
//
// Where teleport bundles should land. By default the bare receiver is this
// site's own /receiver.html; in two-origin mode it is a genuinely different
// host – the strongest staging of "the app isn't OF the place it lands".
// Priority: a dev override in localStorage ("fuaran-receiver-origin", e.g.
// http://localhost:14040 – the vite preview server, a different origin even
// locally), then the build-time VITE_RECEIVER_ORIGIN (the production second
// domain), then this page's own origin.

export function receiverOrigin(): string {
  try {
    const o = window.localStorage.getItem('fuaran-receiver-origin');
    if (o) return o.replace(/\/+$/, '');
  } catch {
    /* storage unavailable – fall through */
  }
  const env = (import.meta as any).env?.VITE_RECEIVER_ORIGIN as string | undefined;
  return env ? env.replace(/\/+$/, '') : window.location.origin;
}

// ─── Round-trip hop helpers ─────────────────────────────────────────────────
//
// A window opened by a hop keeps an `opener` reference back to the original
// window, so the app can hop BACK carrying any edits made here. Same-origin
// openers take the fragment-rewrite fast path; a cross-origin opener throws
// on the location access, and the bundle rides `postMessage` instead (both
// hosts listen for it). Every `window.opener` touch is wrapped – cross-origin
// property access throws rather than returning null.

/** True when a live opener window exists to hop back to (any origin –
 *  `closed` is on the cross-origin-safe property allowlist). */
export function hasOpener(): boolean {
  try {
    return !!(window.opener && !window.opener.closed);
  } catch {
    return false;
  }
}

/** Send the app back to the opener window: fragment rewrite when same-origin,
 *  postMessage when not. `targetOrigin: "*"` is deliberate and safe HERE:
 *  the opener already possessed this bundle (it opened this window with it),
 *  and every receive path digest-verifies the bundle as untrusted input. */
export function hopBack(hash: string, encoded: string): boolean {
  try {
    if (!window.opener || window.opener.closed) return false;
  } catch {
    return false;
  }
  try {
    window.opener.location.hash = hash; // same-origin fast path
    window.opener.focus();
    return true;
  } catch {
    try {
      window.opener.postMessage({ type: 'fuaran-teleport-bundle', encoded }, '*');
      window.opener.focus();
      return true;
    } catch {
      return false;
    }
  }
}
