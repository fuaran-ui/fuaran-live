// Phase 327 – the multi-provider connector wire mapping, exercised headlessly
// over the Fable output. Byok.fs re-expresses the providers (Claude / GPT /
// Gemini / Kimi) over the vendored `FuaranLive.AiWire` portable `JsonValue` model + the
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
  // Kimi (Moonshot) is OpenAI-compatible – same response wire shape as openai.
  kimi: JSON.stringify({
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
  for (const provider of ['anthropic', 'openai', 'gemini', 'kimi']) {
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

  it('openai caps output via max_completion_tokens, never the legacy max_tokens', () => {
    // GPT-5.x reasoning models 400 on `max_tokens` ("Use 'max_completion_tokens'
    // instead") – the 2026-07-28 live failure. The legacy name must not come back.
    const body = agenticRequestBodyFlat('openai');
    expect(body).toContain('"max_completion_tokens":1024');
    expect(body).not.toContain('"max_tokens"');
  });

  it('kimi is OpenAI-compatible with the measured low-effort posture', () => {
    // The eval's fourth-family probe: the effort dial transfers, low is the
    // recommended posture. Moonshot documents `max_tokens` (not the OpenAI
    // reasoning-model rename) and has no `prompt_cache_key` routing hint.
    const body = agenticRequestBodyFlat('kimi');
    expect(body).toContain('tool_calls');
    expect(body).toContain('"tool_choice":"auto"');
    expect(body).toContain('"reasoning_effort":"low"');
    expect(body).toContain('"max_tokens":1024');
    expect(body).not.toContain('max_completion_tokens');
    expect(body).not.toContain('prompt_cache_key');
  });

  it('gemini builds functionDeclarations + name-keyed functionCall/functionResponse', () => {
    const body = agenticRequestBodyFlat('gemini');
    expect(body).toContain('functionDeclarations');
    expect(body).toContain('functionCall');
    expect(body).toContain('functionResponse');
    expect(body).toContain('"role":"model"');
    expect(body).toContain('"role":"user"');
  });

  it('gemini strips additionalProperties from tool schemas (its subset rejects it)', () => {
    // Gemini's function_declarations parameters are a restricted OpenAPI
    // subset: `additionalProperties` 400s ("Unknown name") – the 2026-07-28
    // live failure. The representative request's tool schema carries the key,
    // so the other providers must pass it through and Gemini must drop it.
    expect(agenticRequestBodyFlat('gemini')).not.toContain('additionalProperties');
    expect(agenticRequestBodyFlat('anthropic')).toContain('"additionalProperties":false');
    expect(agenticRequestBodyFlat('openai')).toContain('"additionalProperties":false');
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
