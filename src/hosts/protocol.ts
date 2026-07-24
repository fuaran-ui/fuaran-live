// Phase 85 – the postMessage protocol shared by the preview-pane host-switch
// and the two render-host iframe pages (ts-host.html + fable-host.html).
//
// The Fable host (fable-host/Host.fs) hard-codes the identical string contract
// – F# cannot import this module, so the constants here are the canonical
// source and Host.fs mirrors them. Keep the two in sync.

/** Which conformant render host an iframe runs. */
export type HostKind = 'ts' | 'fsharp';

/** parent window -> host iframe. */
export type HostInbound =
  { readonly kind: 'fuaran:render'; readonly wire: string } | { readonly kind: 'fuaran:clear' };

/** host iframe -> parent window. */
export type HostOutbound =
  | { readonly kind: 'fuaran:ready'; readonly host: HostKind }
  | {
      readonly kind: 'fuaran:rendered';
      readonly host: HostKind;
      readonly ok: boolean;
      readonly error: string | null;
      /**
       * The host's canonical re-encoding of the wire it just decoded –
       * `encode(decode(wire))`, or `null` when the decode failed. The parent's
       * parity view asserts the two hosts' `canonical` strings are byte-identical
       * (and equal to the canonical input), which is the wire-parity claim made
       * visible: two independent conformant hosts round-trip the same wire to the
       * same bytes. Additive field – the Playwright gate reads DOM, not this.
       */
      readonly canonical: string | null;
    };

export const RENDER = 'fuaran:render' as const;
export const CLEAR = 'fuaran:clear' as const;
export const READY = 'fuaran:ready' as const;
export const RENDERED = 'fuaran:rendered' as const;

/** Narrow an untrusted `MessageEvent.data` to a `HostOutbound`. */
export const asOutbound = (data: unknown): HostOutbound | null => {
  if (typeof data !== 'object' || data === null || !('kind' in data)) return null;
  const kind = (data as { kind: unknown }).kind;
  if (kind === READY || kind === RENDERED) return data as HostOutbound;
  return null;
};
