// Phase 401 – client-only voice input, over the Fable output.
//
// The Web Speech API itself is browser-only, so the headless surface is the pure
// input-adapter logic: composing the live prompt from base + final + interim, the
// capability check degrading gracefully where the API is absent, and the friendly
// error mapping. Headless via vitest (node env – no Web Speech).

import { describe, it, expect } from 'vitest';

// @ts-expect-error untyped Fable output
import { composePrompt, isSupported, friendlyError } from '../app/output/Voice.js';

describe('Phase 401 – client-only voice input', () => {
  it('composePrompt appends recognised speech to what the visitor already typed', () => {
    expect(composePrompt('', 'hello world', '')).toBe('hello world');
    // a separating space is inserted when the base has no trailing space
    expect(composePrompt('Build', 'a dashboard', '')).toBe('Build a dashboard');
    expect(composePrompt('Build ', 'a dashboard', '')).toBe('Build a dashboard');
    // interim (the live guess) is appended after the accepted finals
    expect(composePrompt('', 'a chart ', 'and a filt')).toBe('a chart and a filt');
    // an empty transcript leaves what was typed untouched (no stray space)
    expect(composePrompt('typed', '', '')).toBe('typed');
  });

  it('degrades gracefully where Web Speech is unavailable (headless has no API)', () => {
    expect(isSupported()).toBe(false);
  });

  it('maps Web Speech error codes to friendly guidance', () => {
    expect(friendlyError('not-allowed')).toContain('permission');
    expect(friendlyError('audio-capture')).toContain('microphone');
    expect(friendlyError('weird-code')).toContain('weird-code');
  });
});
