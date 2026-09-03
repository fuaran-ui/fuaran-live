// The optional corpus-sink endpoint – the single source of truth for "where an
// opt-in session contribution may travel".
//
// The playground can offer to POST a *key-blind* session bundle (the op stream,
// the folded tree, and provider/model/timestamp/prompt-count metadata) to a
// collection endpoint an operator configures. THE PUBLIC BUILD CONFIGURES NONE:
// `VITE_CORPUS_SINK` is unset, so `corpusSinkOrigin` returns '', nothing is
// added to the CSP, the app reports the feature as unconfigured, and no POST is
// reachable. That is the shipped posture, and it is what the test asserts.
//
// This module exists for the same reason `src/byok/origins.ts` does: the CSP
// `connect-src` allow-list and the code that opens the connection must be
// derived from one value. vite.config.ts imports it at config-eval time, so —
// like its sibling — it is a leaf of pure functions over strings: no DOM, no
// React, no import of anything.

/** Schemes a sink may use.
 *
 *  `https:` is the real answer. Loopback `http:` is admitted for the same
 *  reason the showcase's BYOS page admits it (see vite.config.ts): an operator
 *  wiring a collector on their own machine is not sending anything across a
 *  network at all, and refusing it would push them to weaken something else.
 *  Every other scheme — and every remote `http:` — is refused, silently
 *  yielding no origin, so a mistyped value fails closed rather than opening a
 *  cleartext channel. */
const LOOPBACK_HOSTS = ['localhost', '127.0.0.1', '[::1]'];

/**
 * The `connect-src` origin for a configured corpus sink, or `''` when none is
 * configured or the value is not a usable absolute URL.
 *
 * Returning `''` rather than throwing is deliberate: this runs inside the Vite
 * config, and a build that dies on a stray environment variable is worse than
 * a build that ships with the feature off — which is the default posture
 * anyway. The app-side reader applies the same rule to the same string, so
 * "the CSP allows it" and "the app will post to it" cannot disagree.
 */
export function corpusSinkOrigin(raw: string | undefined | null): string {
  const value = (raw ?? '').trim();
  if (value === '') return '';

  let url: URL;
  try {
    url = new URL(value);
  } catch {
    return '';
  }

  if (url.protocol === 'https:') return url.origin;
  if (url.protocol === 'http:' && LOOPBACK_HOSTS.includes(url.hostname)) return url.origin;
  return '';
}

/**
 * The `connect-src` fragment to append for a configured sink: either a single
 * leading-space-prefixed origin, or the empty string.
 *
 * A separate function from `corpusSinkOrigin` so the "adds nothing when unset"
 * property is the one the CSP builder literally calls, and is the one the test
 * pins — rather than being re-derived at the call site where a stray space
 * could survive review.
 */
export function corpusSinkConnectSrc(raw: string | undefined | null): string {
  const origin = corpusSinkOrigin(raw);
  return origin === '' ? '' : ` ${origin}`;
}

/** The environment variable an operator sets to configure a sink. Named here
 *  so the build, the app and the documentation quote one spelling. */
export const CORPUS_SINK_ENV = 'VITE_CORPUS_SINK';
