// =============================================================================
//  Agent-mode prompt lock — the public loop carries a REFERENCE prompt.
//
//  WHY THIS FILE EXISTS. app/Agent.fs is this repo's emit→observe→repair loop:
//  the playground's agent mode, running in the visitor's browser on their own
//  key. Its system prompt — the `agentSystemPrompt` suffix appended to
//  SystemPrompt.value — names the tool surface and the procedure (look up
//  shapes, emit, observe, repair, stop) and teaches by PROCEDURE, not by
//  example. The wire TEACHING, with its corpus-generated examples, is the
//  language repo's published prompt pack, inlined by app/SystemPrompt.fs and
//  pinned by test/promptPack.test.ts + test/closedLoop.test.ts.
//
//  That division is a boundary, not a style. Content that TUNES a loop —
//  few-shot exemplar trees, provider-specific routing, convergence heuristics
//  learned from an evaluation corpus — is exactly the kind of thing that makes
//  its way into a prompt one helpful paragraph at a time, and once this repo
//  has published it, it cannot be unpublished. This lock makes the growth
//  visible: a change of SHAPE reddens the suite until someone decides,
//  deliberately, that the content belongs in a public reference prompt. See
//  CLAUDE.md, "The agent-mode prompt is a reference prompt".
//
//  WHAT IT ASSERTS, over the F# source on disk (offline; no build needed):
//    1. The agent-mode suffix contains no fenced code block. An exemplar is a
//       tree, and a tree arrives in a prompt as a fence. Inline code spans
//       (`{"$panel": …}` describing an envelope's SHAPE) are not fences and
//       are allowed — the line is between describing a format and showing a
//       worked emission.
//    2. The tools the prose describes are exactly the tools the loop registers
//       in `toolDefinitions`, in BOTH directions. A tool named in the prose
//       that the loop does not dispatch is a promise to the model nothing keeps;
//       a registered tool the prose omits is a surface the model is never told
//       about. Prose tool names are the bold camelCase identifiers (`**askUser**`,
//       `**getKindSchema**`); every other bold run in the suffix is a phrase or
//       a capitalised step name, so the shape is the discriminator.
//    3. The suffix stays under a size ceiling with headroom above today's
//       length. The ceiling is a NUMBER rather than a judgement on purpose:
//       raising it is one line, and the diff that raises it is where the
//       question "does this belong in the public reference prompt?" is asked.
//
//  MUTATION-VERIFIED — each lock was proven to bite against synthetic source
//  text, and those proofs are kept as the self-test block at the end rather
//  than described. A guard that has silently stopped observing passes exactly
//  like a guard with nothing to find.
// =============================================================================

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const repoRoot = resolve(__dirname, '..');
const agentSource = readFileSync(resolve(repoRoot, 'app/Agent.fs'), 'utf8');

/**
 * The size ceiling for the agent-mode suffix, in characters of F# source
 * between the opening and closing `"""`. Measured 2026-09-15 at ~6,800 with
 * the panel-turn and askUser sections in place; the headroom is for a
 * sentence, not a section. Raise it in the same change-set as the content
 * that needs it, and say in that change what the content is.
 */
const SUFFIX_CEILING_CHARS = 8_000;

const TRIPLE_QUOTE = '"""';

/** The agent-mode suffix: the triple-quoted string bound in `agentSystemPrompt`. */
export function extractAgentSuffix(source: string): string {
  const binding = source.indexOf('let agentSystemPrompt');
  if (binding < 0) throw new Error('app/Agent.fs no longer binds `agentSystemPrompt`');
  const open = source.indexOf(TRIPLE_QUOTE, binding);
  if (open < 0) throw new Error('`agentSystemPrompt` has no triple-quoted suffix');
  const close = source.indexOf(TRIPLE_QUOTE, open + TRIPLE_QUOTE.length);
  if (close < 0) throw new Error('`agentSystemPrompt` suffix is unterminated');
  return source.slice(open + TRIPLE_QUOTE.length, close);
}

/** The tool names the loop registers: every `Name = "…"` inside `toolDefinitions`. */
export function registeredToolNames(source: string): string[] {
  const start = source.indexOf('let toolDefinitions');
  if (start < 0) throw new Error('app/Agent.fs no longer binds `toolDefinitions`');
  // The table ends at the next top-level `let`.
  const rest = source.slice(start + 'let toolDefinitions'.length);
  const next = rest.search(/^let /m);
  const table = next < 0 ? rest : rest.slice(0, next);
  const names: string[] = [];
  for (const [, name] of table.matchAll(/\bName\s*=\s*"([A-Za-z]+)"/g)) if (name) names.push(name);
  return names;
}

/** The tool names the prose describes: bold camelCase identifiers. */
export function proseToolNames(suffix: string): string[] {
  const names = new Set<string>();
  for (const [, name] of suffix.matchAll(/\*\*([a-z]+[A-Z][A-Za-z]*)\*\*/g))
    if (name) names.add(name);
  return [...names];
}

/** Every fenced block opener in the text: a line starting with ``` or ~~~. */
export function fenceLines(text: string): string[] {
  return text.split('\n').filter((line) => /^\s*(```|~~~)/.test(line));
}

describe('agent-mode prompt is a reference prompt (app/Agent.fs)', () => {
  const suffix = extractAgentSuffix(agentSource);

  it('carries no fenced example — it teaches by procedure, not by exemplar', () => {
    expect(fenceLines(suffix)).toEqual([]);
  });

  it('describes exactly the tools the loop registers, in both directions', () => {
    const registered = registeredToolNames(agentSource).sort();
    const described = proseToolNames(suffix).sort();
    expect(registered.length).toBeGreaterThan(0);
    expect(described).toEqual(registered);
  });

  it('stays under the size ceiling (raise it deliberately, in the same change as the content)', () => {
    expect(suffix.length).toBeLessThanOrEqual(SUFFIX_CEILING_CHARS);
  });
});

// ─── self-test: the locks bite (mutations that were reverted, kept runnable) ──

const syntheticSource = [
  'let toolDefinitions: ToolDefinition list =',
  '  [ { Name = "getThing"',
  '      Description = "x" }',
  '    { Name = "askUser"',
  '      Description = "y" } ]',
  '',
  'let agentSystemPrompt =',
  '  SystemPrompt.value',
  '  + """',
  'You have two tools: **getThing** reads a thing; **askUser** asks. Use **Emit** then **Observe**.',
  'An envelope looks like `{"$panel": "id"}` inline – that is a shape, not an example.',
  '"""',
  '',
  'let next = 1',
].join('\n');

describe('self-test: each lock bites on a synthetic mutation', () => {
  it('baseline synthetic source passes every lock', () => {
    const suffix = extractAgentSuffix(syntheticSource);
    expect(fenceLines(suffix)).toEqual([]);
    expect(proseToolNames(suffix).sort()).toEqual(registeredToolNames(syntheticSource).sort());
    expect(suffix.length).toBeLessThanOrEqual(SUFFIX_CEILING_CHARS);
  });

  it('fence: a fenced example in the suffix is caught', () => {
    const mutated = syntheticSource.replace(
      'inline – that is a shape, not an example.',
      'inline.\n```json\n{"id":"ex","kind":{"$type":"Metric"}}\n```',
    );
    expect(fenceLines(extractAgentSuffix(mutated))).toHaveLength(2); // opener + closer
  });

  it('tool drift, prose → table: a tool named in prose the loop does not register', () => {
    const mutated = syntheticSource.replace('**getThing** reads', '**getOtherThing** reads');
    const described = proseToolNames(extractAgentSuffix(mutated)).sort();
    expect(described).not.toEqual(registeredToolNames(mutated).sort());
  });

  it('tool drift, table → prose: a registered tool the prose never names', () => {
    const mutated = syntheticSource.replace(
      '      Description = "y" } ]',
      '      Description = "y" }\n    { Name = "getHidden"\n      Description = "z" } ]',
    );
    const described = proseToolNames(extractAgentSuffix(mutated)).sort();
    expect(described).not.toEqual(registeredToolNames(mutated).sort());
  });

  it('ceiling: a suffix past the ceiling is caught', () => {
    const padding = 'x'.repeat(SUFFIX_CEILING_CHARS);
    const mutated = syntheticSource.replace('that is a shape', `that is a shape ${padding}`);
    expect(extractAgentSuffix(mutated).length).toBeGreaterThan(SUFFIX_CEILING_CHARS);
  });

  it('the bold-camelCase discriminator ignores phrases and step names', () => {
    const suffix = extractAgentSuffix(syntheticSource);
    expect(proseToolNames(suffix)).not.toContain('Emit');
    expect(proseToolNames(suffix)).not.toContain('Observe');
  });
});
