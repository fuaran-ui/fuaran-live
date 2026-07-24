// Phase 327 – the multi-provider connector wire mapping, exercised headlessly
// over the Fable output. Byok.fs re-expresses all three providers (Claude / GPT
// / Gemini) over the vendored `FuaranLive.AiWire` portable `JsonValue` model + the
// `IHttpTransport` egress seam, and every provider now implements `SendAgentic`
// (tool-use). The pure request-build + response-parse halves (no `fetch`) are
// the smoke surface: `agenticRequestBodyFlat` / `parseAgenticResponseFlat`
// project the F# body builders + response parsers to flat values assertable
// across the Fable boundary – proving the block↔wire translation per provider
// without a live LLM. The real fetch egress is operator-verified.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { describe, it, expect } from 'vitest';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
import {
  agenticRequestBodyFlat,
  parseAgenticResponseFlat,
  estimateCostUsdFlat,
  defaultModelIdsFlat,
  // @ts-expect-error untyped Fable output
} from '../app/output/Byok.js';

// Canned tool-use responses in each provider's native wire shape: a text block +
// one tool_use on getNodeState(nodeId: "n1") + real token usage.
const cannedResponse: Record<string, string> = {
  anthropic: JSON.stringify({
    content: [
      { type: 'text', text: 'hi' },
      { type: 'tool_use', id: 'tu', name: 'getNodeState', input: { nodeId: 'n1' } },
    ],
    stop_reason: 'tool_use',
    usage: { input_tokens: 10, output_tokens: 5 },
  }),
  openai: JSON.stringify({
    choices: [
      {
        message: {
          content: 'hi',
          tool_calls: [
            {
              id: 'tc',
              type: 'function',
              function: { name: 'getNodeState', arguments: '{"nodeId":"n1"}' },
            },
          ],
        },
        finish_reason: 'tool_calls',
      },
    ],
    usage: { prompt_tokens: 10, completion_tokens: 5 },
  }),
  gemini: JSON.stringify({
    candidates: [
      {
        content: {
          parts: [
            { text: 'hi' },
            { functionCall: { name: 'getNodeState', args: { nodeId: 'n1' } } },
          ],
        },
        // Gemini reports STOP even alongside a functionCall – the mapper must
        // still surface tool_use because a call part is present.
        finishReason: 'STOP',
      },
    ],
    usageMetadata: { promptTokenCount: 10, candidatesTokenCount: 5 },
  }),
};

describe('the multi-provider agentic response parse (shared JsonValue model)', () => {
  for (const provider of ['anthropic', 'openai', 'gemini']) {
    it(`${provider}: parses text + tool_use blocks, stop reason, and usage`, () => {
      const r = parseAgenticResponseFlat(provider, cannedResponse[provider]);
      expect(r.Blocks).toBe(2);
      expect(r.ToolUses).toBe(1);
      expect(r.FirstToolName).toBe('getNodeState');
      expect(r.StopReason).toBe('tool_use');
      expect(r.InTokens).toBe(10);
      expect(r.OutTokens).toBe(5);
    });
  }
});

describe('the multi-provider agentic request build (block → wire per provider)', () => {
  it('anthropic builds ordered content blocks + tool_result threading', () => {
    const body = agenticRequestBodyFlat('anthropic');
    expect(body).toContain('input_schema');
    expect(body).toContain('"type":"tool_use"');
    expect(body).toContain('"type":"tool_result"');
    expect(body).toContain('tool_use_id');
  });

  it('anthropic system block carries the ephemeral prompt-cache breakpoint', () => {
    // The ~15k-token pack prompt is written to the provider cache on turn 1
    // and read at a fraction of input price thereafter – the cost posture the
    // published evaluation's cached steady-state numbers assume.
    const body = agenticRequestBodyFlat('anthropic');
    expect(body).toContain('"cache_control":{"type":"ephemeral"}');
  });

  it('openai fans tool calls + results into tool_calls + tool-role messages', () => {
    const body = agenticRequestBodyFlat('openai');
    expect(body).toContain('tool_calls');
    expect(body).toContain('"role":"tool"');
    expect(body).toContain('tool_call_id');
    expect(body).toContain('parameters');
    expect(body).toContain('"tool_choice":"auto"');
  });

  it('openai sends the prompt-cache routing key (caching itself is automatic)', () => {
    const body = agenticRequestBodyFlat('openai');
    expect(body).toContain('"prompt_cache_key":"fuaran-live"');
  });

  it('gemini builds functionDeclarations + name-keyed functionCall/functionResponse', () => {
    const body = agenticRequestBodyFlat('gemini');
    expect(body).toContain('functionDeclarations');
    expect(body).toContain('functionCall');
    expect(body).toContain('functionResponse');
    expect(body).toContain('"role":"model"');
    expect(body).toContain('"role":"user"');
  });
});

// Phase 115 – the indicative cost estimate behind the agent-mode readout. The
// assertions pin behaviour, not prices (prices are maintained data): every
// default model must be priced, cost must scale linearly, output tokens must
// cost at least input tokens (true of every current entry), and an unknown
// model must fall back to "tokens only" (-1 across the flat boundary).
describe('estimateCostUsdFlat (the agent-mode cost readout)', () => {
  it('prices every default model (no default may fall back to tokens-only)', () => {
    for (const model of defaultModelIdsFlat()) {
      expect(estimateCostUsdFlat(model, 1_000_000, 1_000_000)).toBeGreaterThan(0);
    }
  });

  it('scales linearly with tokens and charges output above input', () => {
    for (const model of defaultModelIdsFlat()) {
      const oneX = estimateCostUsdFlat(model, 10_000, 5_000);
      const twoX = estimateCostUsdFlat(model, 20_000, 10_000);
      expect(twoX).toBeCloseTo(2 * oneX, 10);
      expect(estimateCostUsdFlat(model, 0, 1_000)).toBeGreaterThanOrEqual(
        estimateCostUsdFlat(model, 1_000, 0),
      );
    }
  });

  it('returns 0 for zero tokens and -1 for an unknown model', () => {
    const [first] = defaultModelIdsFlat();
    expect(estimateCostUsdFlat(first, 0, 0)).toBe(0);
    expect(estimateCostUsdFlat('some-unknown-model', 1_000, 1_000)).toBe(-1);
  });
});
