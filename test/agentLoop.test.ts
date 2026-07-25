// Phase 326 (Stage 6) – the F#/Fable agent self-debug loop, exercised headlessly
// over the Fable output. The pure tool dispatch + the multi-turn loop + the
// budget live in app/Agent.fs and compile to app/output/Agent.js; this drives them
// in node via vitest. The flat `dispatchToolFlat` / `runLoopProbe` / `runBudgetProbe`
// helpers project the F# DU / Async / list results to assertable plain values (and
// a JS Promise) across the Fable boundary – the loop's tool-running + result-
// threading is testable without a live LLM or a DOM. The real provider fetch +
// getRenderedDom (live preview geometry) are operator-verified.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { describe, it, expect } from 'vitest';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck). The
// module specifier resolves to a real .js on disk, so tsc reports the implicit-any
// (TS7016) at the `from` clause below – the directive has to sit on that line, not
// the `import` keyword, to suppress it.
import {
  dispatchToolFlat,
  runLoopProbe,
  runBudgetProbe,
  runEmitRepairProbe,
  runResumeProbe,
  runPanelProbe,
  runAdvisoryProbe,
  runAdvisoryRepeatProbe,
  // @ts-expect-error untyped Fable output (no .d.ts for app/output/Agent.js)
} from '../app/output/Agent.js';

// A corpus-valid full-tree emission (wire-format-fixtures/nodes/metric-1 – the
// canonical flat shape the F# decoder certifies against).
const metricNode =
  '{"id":"metric-1","kind":{"$type":"Metric","format":{"$type":"Currency","code":"GBP"},"icon":"trending-up","label":"Revenue","subtext":"vs last month","tone":"Brand","trend":{"$type":"Static","value":0.07},"trendFormat":{"$type":"Percent","decimals":1},"value":{"$type":"Static","value":1234.5}}}';
const treeEmission = 'Here is your metric.\n\n```json\n' + metricNode + '\n```';

describe('the agent tool dispatch (pure over the wire tree)', () => {
  it('getNodeState finds a node and reports its kind path', () => {
    const r = dispatchToolFlat(metricNode, 'getNodeState', { nodeId: 'metric-1' });
    expect(r.Ok).toBe(true);
    const result = JSON.parse(r.Content);
    expect(result.found).toBe(true);
    expect(result.id).toBe('metric-1');
    expect(result.kindPath).toBe('Metric');
  });

  it('getNodeState on a missing id is not-found and lists the available ids', () => {
    const r = dispatchToolFlat(metricNode, 'getNodeState', { nodeId: 'nope' });
    expect(r.Ok).toBe(false);
    const result = JSON.parse(r.Content);
    expect(result.found).toBe(false);
    expect(result.availableIds).toContain('metric-1');
  });

  it('getNodeState with no tree (null) is a clean not-found, never throws', () => {
    const r = dispatchToolFlat(null, 'getNodeState', { nodeId: 'metric-1' });
    expect(r.Ok).toBe(false);
    expect(JSON.parse(r.Content).found).toBe(false);
  });

  it('getNodeState without a nodeId argument is a recoverable error', () => {
    const r = dispatchToolFlat(metricNode, 'getNodeState', {});
    expect(r.Ok).toBe(false);
    expect(r.Content).toContain('requires');
  });

  it('getBindingValue lists the static bindings with their values', () => {
    const r = dispatchToolFlat(metricNode, 'getBindingValue', { nodeId: 'metric-1' });
    expect(r.Ok).toBe(true);
    const result = JSON.parse(r.Content);
    expect(result.found).toBe(true);
    const staticBindings = result.bindings.filter((b: { type: string }) => b.type === 'Static');
    expect(staticBindings.length).toBeGreaterThanOrEqual(1);
    expect(staticBindings.some((b: { value?: unknown }) => b.value === 1234.5)).toBe(true);
  });

  it('getRuntimeErrors returns a zero-count report when nothing failed', () => {
    const r = dispatchToolFlat(metricNode, 'getRuntimeErrors', {});
    expect(r.Ok).toBe(true);
    expect(JSON.parse(r.Content).count).toBe(0);
  });

  it('an unknown tool is a recoverable error', () => {
    const r = dispatchToolFlat(metricNode, 'getNonsense', {});
    expect(r.Ok).toBe(false);
    expect(r.Content).toContain('Unknown tool');
  });
});

describe('getKindSchema (the authoritative per-kind shape lookup)', () => {
  it('returns every required field for a complex kind in one call', () => {
    // The Select trap: a valid Select needs THREE required fields, not the one the
    // model would discover per decode error. getKindSchema surfaces them at once.
    // (`onChange` is optional since Phase 426 – the control write-back default.)
    const r = dispatchToolFlat(null, 'getKindSchema', { kind: 'Select' });
    expect(r.Ok).toBe(true);
    const result = JSON.parse(r.Content);
    expect(result.kind).toBe('Select');
    expect(result.required).toEqual(expect.arrayContaining(['label', 'source', 'value']));
    expect(result.required).not.toContain('onChange');
    // The referenced binding/text defs travel with it – a self-contained sub-schema.
    expect(Object.keys(result.defs)).toEqual(expect.arrayContaining(['Binding', 'TextSource']));
  });

  it('with no argument lists every valid kind', () => {
    const r = dispatchToolFlat(null, 'getKindSchema', {});
    expect(r.Ok).toBe(true);
    // `Box` is the unified container (Phase 390) – the former Dashboard/Card/
    // GridLayout kinds are Box roles/layouts, not wire $types.
    const kinds = JSON.parse(r.Content).kinds;
    expect(kinds).toEqual(expect.arrayContaining(['Metric', 'Select', 'Chart', 'Tabs', 'Box']));
  });

  it('rejects an unknown kind and points at the real ones', () => {
    // "Grid" is not a wire $type – the real kinds are Box (the unified container;
    // Grid is a BoxLayout mode) and DataGrid (vis). The error must name the valid
    // vocabulary so the model can self-correct.
    const r = dispatchToolFlat(null, 'getKindSchema', { kind: 'Grid' });
    const result = JSON.parse(r.Content);
    expect(result.error).toContain('Unknown kind');
    expect(result.validKinds).toEqual(expect.arrayContaining(['Box', 'DataGrid']));
  });
});

describe('the agent loop with a mock agentic provider (tool_use → end_turn)', () => {
  it('runs the tool, threads the result back, and halts on end_turn', async () => {
    const r = await runLoopProbe(treeEmission, 'getNodeState', 'metric-1');

    // Two provider round-trips: the tool_use turn, then the end_turn.
    expect(r.HaltReason).toBe('completed');
    expect(r.Iterations).toBe(2);

    // The single tool call ran against the live tree and resolved.
    expect(r.ToolCalls.length).toBe(1);
    expect(r.ToolCalls[0].Name).toBe('getNodeState');
    expect(r.ToolCalls[0].Ok).toBe(true);
    expect(r.ToolCalls[0].ResultSummary).toContain('metric-1');

    // The crux: the SECOND provider call carried the tool_result threaded back
    // from the first turn (multi-turn tool_use/tool_result content-block threading).
    expect(r.SecondTurnThreadedToolResult).toBe(true);

    // The timeline streamed the expected step kinds.
    expect(Array.from(r.TimelineKinds)).toEqual(
      expect.arrayContaining(['user-prompt', 'model-call', 'tool-call', 'halt']),
    );
  });

  it('a runaway loop is halted by the iteration budget (default-deny)', async () => {
    const r = await runBudgetProbe(2);
    expect(r.HaltReason).toBe('max-iterations');
    expect(r.Iterations).toBe(2);
  });

  it('does not report "satisfied" when the final emission failed – it feeds the error back and recovers', async () => {
    // Turn 1 emits a TreeOp with no tree yet (ingest fails) and calls no tools –
    // the exact "emit-and-stop over a broken emission" trap. The loop must NOT
    // halt as completed there; it feeds the failure back, and turn 2's valid full
    // tree completes the run with a real tree and a clean error slate.
    const r = await runEmitRepairProbe();
    expect(r.Iterations).toBe(2);
    expect(r.HaltReason).toBe('completed');
    expect(r.FinalTreeSet).toBe(true);
    expect(r.FinalErrorCount).toBe(0);
  });
});

describe('Phase 328 – resumable runs + live token accumulation', () => {
  it('resumes a halted run with prior context and accumulates tokens across runs', async () => {
    // Run 1 halts on the (hidden) iteration backstop with one tool call; run 2
    // resumes from run 1's FinalMessages with a follow-up prompt and completes.
    const r = await runResumeProbe();

    // Run 1 was budget-halted mid-loop; run 2 ran fresh to completion.
    expect(r.Run1Halt).toBe('max-iterations');
    expect(r.Run2Halt).toBe('completed');

    // Run 2 was genuinely seeded with run 1's accumulated context, and its own
    // final context is strictly longer than that seed (prior turns + new prompt).
    expect(r.Run2SeedHadPrior).toBe(true);
    expect(r.ResumedWithContext).toBe(true);

    // The token counter accumulates real per-call usage on each run.
    expect(r.Run1Tokens).toBe(18); // 12 in + 6 out
    expect(r.Run2Tokens).toBe(6); // 4 in + 2 out
  });
});

describe('Phase 466 – agent expression turns: live panels with scoped op streams', () => {
  it('streams a panel tree + op, hash-chains the ops, and replay reconstructs the panel', async () => {
    // Turn 1 opens a $panel with a tree; turn 2 extends it with an InsertChild;
    // turn 3 emits a BROKEN panel op (unknown target) with no tools; turn 4 ends.
    const r = await runPanelProbe();

    // The run completes (the broken op is fed back for repair, not fatal).
    expect(r.HaltReason).toBe('completed');

    // Exactly one panel, carrying its declared id + title.
    expect(r.PanelCount).toBe(1);
    expect(r.PanelId).toBe('findings');
    expect(r.PanelTitle).toBe('Findings');

    // One applied op, one chain entry, linked to the base-tree content hash.
    expect(r.OpsApplied).toBe(1);
    expect(r.ChainLength).toBe(1);
    expect(r.ChainLinked).toBe(true);

    // Replay verification: refolding base + ops reproduces the live tree
    // byte-for-byte, and recomputing the chain reproduces the recorded hashes.
    expect(r.ReplayOk).toBe(true);
    expect(r.ChainOk).toBe(true);

    // Transcript rows: the first successful emission is the panel's mount
    // (isNew), the second is a compact op row; the broken op logged too.
    expect(r.FirstEmissionWasNew).toBe(true);
    expect(r.SecondEmissionWasNew).toBe(false);
    expect(r.PanelKindsInTimeline).toBe(3);

    // The broken emission was fed back as a repairable user turn (the
    // anti-"satisfied over a broken emission" guard covers panels too).
    expect(r.BrokenOpFedBack).toBe(true);

    // Panel emissions never leak into the main-preview session.
    expect(r.MainSessionUntouched).toBe(true);
  });
});

describe('pre-emit advisories in the loop (Phase 664)', () => {
  it('feeds an applied-but-inert emission back and completes once corrected', async () => {
    const r = await runAdvisoryProbe();
    // Turn 1 applied but drew FUARAN090; the loop did NOT halt satisfied – it
    // fed the advisory back; turn 2's $state-sourced correction completed.
    expect(r.HaltReason).toBe('completed');
    expect(r.Iterations).toBe(2);
    expect(r.FeedbackTurns).toBe(1);
    expect(r.FeedbackNamesFuaran090).toBe(true);
    // The transcript's emission row names the advisory codes too.
    expect(r.EmissionSummaryNamesFuaran090).toBe(true);
    // The corrected tree carries a clean advisory slate.
    expect(r.FinalAdvisoryCount).toBe(0);
  });

  it('never feeds an identical advisory set twice (warnings cannot burn the budget)', async () => {
    const r = await runAdvisoryRepeatProbe();
    // Same inert wire re-emitted: fed once, then completed normally.
    expect(r.HaltReason).toBe('completed');
    expect(r.Iterations).toBe(2);
    expect(r.FeedbackTurns).toBe(1);
  });
});
