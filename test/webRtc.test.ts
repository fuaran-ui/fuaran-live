// Phase 295 – serverless live-drive (Stage 2, WebRTC P2P), over the Fable output.
//
// The RTCPeerConnection handshake itself needs a browser (jsdom has no WebRTC),
// so it is exercised in a real two-tab loopback walkthrough. What IS pure – and
// headlessly testable here – is the signalling codec that wraps each SDP blob
// into the copy-paste / QR token exchanged out-of-band, plus its trust invariant:
// a signal token carries ONLY an SDP string, never the BYOK key.

import { describe, it, expect } from 'vitest';

// @ts-expect-error untyped Fable output
import { encodeSignal, signalKind, signalSdp, signalRoundTrips } from '../app/output/WebRtc.js';

// A minimal but structurally-valid SDP body (starts with the version line `v=0`,
// which the codec requires as a cheap "this really is SDP" guard).
const offerSdp =
  'v=0\r\no=- 42 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\nm=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\n';
const answerSdp =
  'v=0\r\no=- 99 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\nm=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\n';

describe('Phase 295 – WebRTC pairing signal codec (Stage 2)', () => {
  it('round-trips an offer token, preserving kind + SDP', () => {
    const token = encodeSignal('offer', offerSdp);
    expect(signalKind(token)).toBe('offer');
    expect(signalSdp(token)).toBe(offerSdp);
    expect(signalRoundTrips(token)).toBe(true);
  });

  it('round-trips an answer token, preserving kind + SDP', () => {
    const token = encodeSignal('answer', answerSdp);
    expect(signalKind(token)).toBe('answer');
    expect(signalSdp(token)).toBe(answerSdp);
    expect(signalRoundTrips(token)).toBe(true);
  });

  it('the token is a single opaque line (copy-paste / QR friendly)', () => {
    const token = encodeSignal('offer', offerSdp);
    expect(token).not.toContain('\n');
    expect(token).not.toContain(' ');
    // and it is NOT the raw SDP – the newlines/structure are encoded away
    expect(token).not.toContain('v=0');
  });

  it('rejects a malformed / non-SDP token', () => {
    expect(signalKind('not-base64-@@@')).toBe('');
    expect(signalKind('')).toBe('');
    // a well-formed base64 of JSON that is NOT an SDP envelope
    expect(signalKind(btoa(JSON.stringify({ v: 1, kind: 'offer', sdp: 'hello' })))).toBe('');
    // an unknown kind
    expect(signalKind(btoa(JSON.stringify({ v: 1, kind: 'candidate', sdp: offerSdp })))).toBe('');
    // a wrong version
    expect(signalKind(btoa(JSON.stringify({ v: 2, kind: 'offer', sdp: offerSdp })))).toBe('');
  });

  it('TRUST: a signal token carries ONLY {v, kind, sdp} – never a key', () => {
    const token = encodeSignal('offer', offerSdp);
    const parsed = JSON.parse(decodeURIComponent(escape(atob(token))));
    expect(Object.keys(parsed).sort()).toEqual(['kind', 'sdp', 'v']);
    expect(token.toLowerCase()).not.toContain('apikey');
    expect(token.toLowerCase()).not.toContain('sk-');
  });
});
