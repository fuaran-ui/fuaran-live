// ============================================================================
//  NL → query + UI emission loop (Phase 324, task 3).
//
//  The BYOK LLM writes BOTH halves of a dashboard in one shot: a `query` (the SQL
//  / Transform a driver runs) AND a `dashboard` (a canonical Fuaran wire-format
//  `Node` tree). This module builds the grounding prompt, calls the injected
//  provider, parses the two-part emission, then hands off to the shipped
//  `resolveAndCheck` gate – so BOTH are validated before anything renders:
//
//    • a malformed emission never reaches the driver (parse → `emissionInvalid`);
//    • a query that errors is a typed `sourceError` (the driver maps it);
//    • a dashboard that mis-types against the live result schema is a
//      `typeMismatch` (FUARAN066/067) and NEVER renders – default-deny, the F#
//      `QueryBinding.check` via the Fable bridge, not a TS re-implementation.
//
//  The provider is the only effectful edge and it is injected (`IEmissionProvider`),
//  so the whole loop is headlessly testable with a canned model response – no key,
//  no network. The browser BYOK provider (Anthropic / OpenAI / Gemini `fetch`)
//  drops into the same seam.
// ============================================================================

import type { ResultSchema } from './core';
import { resolveAndCheck, type IClientQuerySource, type PortalOutcome } from './sources';

/** The BYOK model edge: a system + user prompt in, raw model text out. */
export interface IEmissionProvider {
  complete(systemPrompt: string, userPrompt: string): Promise<string>;
}

/** A natural-language dashboard request + the privacy toggle. */
export interface EmissionRequest {
  /** The user's natural-language ask ("revenue by category, last quarter"). */
  readonly nl: string;
  /** Optional known source schema, fed to the model to ground the query columns. */
  readonly schema?: ResultSchema;
  /** Schema-only privacy mode – threaded through to the source on execution. */
  readonly schemaOnly: boolean;
}

/** The two-part emission the model is asked to produce. */
export interface Emission {
  /** The query the driver executes (dialect SQL / a serialised Transform). */
  readonly sql: string;
  /** The dashboard as canonical Fuaran wire JSON (a string, ready for the gate). */
  readonly dashboardWire: string;
}

/** Parse result for a raw model response. */
export type EmissionParse = { ok: true; value: Emission } | { ok: false; error: string };

/** The loop's outcome: the gate's verdict, or an emission/provider failure before it. */
export type EmissionOutcome =
  | PortalOutcome
  | { readonly kind: 'emissionInvalid'; readonly error: string }
  | { readonly kind: 'providerError'; readonly error: string };

const SYSTEM_PROMPT_BASE = [
  'You turn a natural-language request into a data dashboard.',
  'Reply with ONE JSON object and nothing else:',
  '  { "query": "<a single read-only SQL statement>",',
  '    "dashboard": <a Fuaran canonical wire-format Node tree> }',
  'The query must be read-only (SELECT/WITH). Bind every dashboard metric, axis, and',
  'grouping to a column the query returns – a binding to an absent or wrong-typed',
  'column is rejected before render, so prefer columns you are sure the query yields.',
].join('\n');

/** Build the grounding system prompt, appending the known schema when available. */
export function buildSystemPrompt(schema?: ResultSchema): string {
  if (!schema || schema.length === 0) return SYSTEM_PROMPT_BASE;
  const cols = schema.map((c) => `${c.name}: ${c.type}`).join(', ');
  return `${SYSTEM_PROMPT_BASE}\n\nThe source exposes these columns: ${cols}.`;
}

/**
 * Parse a raw model response into a typed `Emission`. Tolerant of a ```json fenced
 * block or a bare object, and of extra prose around it – it extracts the first
 * balanced JSON object carrying string `query` + object `dashboard`. A response
 * that does not is a typed parse error (never a throw, never a guess).
 */
export function parseEmission(raw: string): EmissionParse {
  const jsonText = extractJsonObject(raw);
  if (jsonText === null) return { ok: false, error: 'no JSON object found in the model response' };
  let parsed: unknown;
  try {
    parsed = JSON.parse(jsonText);
  } catch (e) {
    return {
      ok: false,
      error: 'emission is not valid JSON: ' + (e instanceof Error ? e.message : String(e)),
    };
  }
  if (typeof parsed !== 'object' || parsed === null)
    return { ok: false, error: 'emission is not a JSON object' };
  const o = parsed as { query?: unknown; dashboard?: unknown };
  if (typeof o.query !== 'string')
    return { ok: false, error: 'emission is missing a string "query"' };
  if (typeof o.dashboard !== 'object' || o.dashboard === null)
    return { ok: false, error: 'emission is missing a "dashboard" object' };
  return { ok: true, value: { sql: o.query, dashboardWire: JSON.stringify(o.dashboard) } };
}

/**
 * Run the full NL→query+UI loop: build prompts → call `provider` → parse the
 * emission → `resolveAndCheck` against `source`. The dashboard is gated against
 * the LIVE result schema, so a mistyped emission is rejected before render and a
 * bad query surfaces as a source error – both before anything reaches the DOM.
 */
export async function runEmission(
  provider: IEmissionProvider,
  source: IClientQuerySource,
  request: EmissionRequest,
): Promise<EmissionOutcome> {
  const system = buildSystemPrompt(request.schema);
  let raw: string;
  try {
    raw = await provider.complete(system, request.nl);
  } catch (e) {
    return { kind: 'providerError', error: e instanceof Error ? e.message : String(e) };
  }
  const parsed = parseEmission(raw);
  if (!parsed.ok) return { kind: 'emissionInvalid', error: parsed.error };
  return resolveAndCheck(
    source,
    { sql: parsed.value.sql, schemaOnly: request.schemaOnly },
    parsed.value.dashboardWire,
  );
}

// ─── JSON extraction (tolerant of fences + surrounding prose) ─────────────────

/** Extract the first balanced top-level `{…}` object from raw model text. */
function extractJsonObject(raw: string): string | null {
  // Prefer a ```json … ``` (or bare ```) fenced block if present.
  const fence = raw.match(/```(?:json)?\s*([\s\S]*?)```/i);
  const haystack = fence ? fence[1]! : raw;
  const start = haystack.indexOf('{');
  if (start < 0) return null;
  let depth = 0;
  let inString = false;
  let escaped = false;
  for (let i = start; i < haystack.length; i++) {
    const ch = haystack[i]!;
    if (inString) {
      if (escaped) escaped = false;
      else if (ch === '\\') escaped = true;
      else if (ch === '"') inString = false;
      continue;
    }
    if (ch === '"') inString = true;
    else if (ch === '{') depth++;
    else if (ch === '}') {
      depth--;
      if (depth === 0) return haystack.slice(start, i + 1);
    }
  }
  return null;
}
