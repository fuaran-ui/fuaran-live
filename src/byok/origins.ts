// The provider egress origins – the single source of truth for "where a BYOK
// key may travel".
//
// Each supported provider has exactly one origin its adapter ever fetches, and
// the user's key rides only a header on that call (never a URL, never a body).
// vite.config.ts imports PROVIDER_ORIGINS to build the strict CSP `connect-src`,
// and each adapter imports its own origin – so the CSP and the code that opens
// the connection can never drift apart. The networkEgress test asserts that a
// key only ever reaches its provider's origin.
//
// This module is a leaf of pure string constants on purpose: vite.config.ts
// imports it at config-eval time, so it must pull in no DOM, no React, nothing.

export const ANTHROPIC_ORIGIN = 'https://api.anthropic.com';
export const OPENAI_ORIGIN = 'https://api.openai.com';
export const GEMINI_ORIGIN = 'https://generativelanguage.googleapis.com';
export const KIMI_ORIGIN = 'https://api.moonshot.ai';

/** Every origin a BYOK key may egress to – the exact `connect-src` allow-list
 *  (besides 'self'). No wildcards: the chosen provider's origin only. */
export const PROVIDER_ORIGINS = [
  ANTHROPIC_ORIGIN,
  OPENAI_ORIGIN,
  GEMINI_ORIGIN,
  KIMI_ORIGIN,
] as const;
